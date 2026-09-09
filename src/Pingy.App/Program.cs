using System.Globalization;
using System.Reflection;
using Pingy.Core;

namespace Pingy.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => UiDialogs.Show(e.Exception.Message, "Pingy", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            { warning = "Could not read saved settings. The embedded profile has been loaded.\n\n" + ex.Message; }
            ProfileMigration.RemoveUnmodifiedSamples(config);
            Application.Run(new MainForm(config, store, warning));
        }
        catch (Exception ex)
        { UiDialogs.Show("Could not start Pingy.\n\n" + ex.Message, "pingy", MessageBoxButtons.OK, MessageBoxIcon.Error); Environment.ExitCode = 1; }
    }
}
