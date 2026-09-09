using System.Reflection;
using Pingy.Core;

namespace Pingy.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "pingy — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pingy.defaults.json")!;
            using var reader = new StreamReader(stream);
            var seed = PortableConfig.Read(Environment.ProcessPath!) ?? ConfigCodec.Parse(reader.ReadToEnd());
            // Useful for verifying the final bundled EXE without changing the user's profile.
            if (args.Length == 2 && args[0] == "--write-embedded-config")
            {
                File.WriteAllText(args[1], ConfigCodec.Serialize(seed));
                return;
            }
            var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pingy", "profiles", seed.ProfileId.ToString("N") + ".json");
            var store = new ConfigStore(configPath);
            var config = seed;
            string? warning = null;
            try { config = store.Load() ?? seed; config.ProfileId = seed.ProfileId; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            { warning = "Не удалось прочитать сохранённые настройки. Загружены хосты из программы.\n\n" + ex.Message; }
            Application.Run(new MainForm(config, store, warning));
        }
        catch (Exception ex)
        { MessageBox.Show("Не удалось запустить pingy.\n\n" + ex.Message, "pingy", MessageBoxButtons.OK, MessageBoxIcon.Error); Environment.ExitCode = 1; }
    }
}
