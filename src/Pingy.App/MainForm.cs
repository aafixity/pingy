using System.Diagnostics;
using System.Globalization;
using Pingy.Core;

namespace Pingy.App;

internal sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(16, 24, 32), PanelColor = Color.FromArgb(23, 35, 45), Ink = Color.FromArgb(229, 237, 242), Muted = Color.FromArgb(151, 172, 188), Accent = Color.FromArgb(89, 214, 178), Line = Color.FromArgb(43, 61, 75);
    private AppConfig _config;
    private readonly ConfigStore _store;
    private readonly Button _start = ButtonOf("▶  Старт", true), _manage = ButtonOf("Управление хостами"), _portable = ButtonOf("Создать копию .exe");
    private readonly TreeView _hosts = new() { Dock = DockStyle.Fill, CheckBoxes = true, ShowLines = false, ShowPlusMinus = true, ShowRootLines = false, BorderStyle = BorderStyle.None, FullRowSelect = true, HideSelection = false, ItemHeight = 30 };
    private readonly Label _selected = LabelOf(""), _session = LabelOf("Готов к работе"), _status = LabelOf(""), _summary = LabelOf("Нажмите «Старт», чтобы начать новую сессию");
    private readonly NumericUpDown _interval = NumberOf(0.25m, 60, 1), _timeout = NumberOf(0.1m, 10, 1);
    private readonly CheckBox _follow = new() { Text = "Автопрокрутка", Checked = true, AutoSize = true, ForeColor = Ink, Margin = new Padding(14, 12, 8, 0) };
    private readonly DataGridView _log = Grid(), _stats = Grid();
    private readonly TextBox _network = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
    private readonly Dictionary<Guid, PingStatistics> _statistics = [];
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _networkClock = new() { Interval = 15000 };
    private PingBatch?[] _history = new PingBatch?[10000];
    private int _historyStart, _historyCount;
    private long _totalRows;
    private bool _treeUpdating, _logUpdating, _closing, _allowClose, _copying, _networkLoading;
    private HostEntry[] _active = [];
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly Stopwatch _elapsed = new();

    public MainForm(AppConfig config, ConfigStore store, string? warning)
    {
        _config = config; _store = store;
        Text = "pingy — мониторинг сети"; BackColor = Bg; ForeColor = Ink;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1240, 820); MinimumSize = new Size(1040, 700); StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        _hosts.BackColor = PanelColor; _hosts.ForeColor = Ink; _hosts.LineColor = Line;
        _network.BackColor = PanelColor; _network.ForeColor = Muted; _network.Font = new Font("Consolas", 10);
        _interval.Value = config.IntervalMs / 1000m; _timeout.Value = config.TimeoutMs / 1000m;
        _hosts.AfterCheck += HostChecked;
        _hosts.BeforeCheck += (_, e) => { if (!_treeUpdating && _cts != null) e.Cancel = true; };
        _start.Click += async (_, _) => await ToggleAsync();
        _manage.Click += (_, _) => ManageHosts();
        _portable.Click += async (_, _) => await CreatePortableAsync();
        _follow.CheckedChanged += (_, _) => { if (_follow.Checked) ScrollToEnd(); };
        _log.VirtualMode = true;
        _log.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex >= _historyCount) return;
            var row = History(e.RowIndex);
            e.Value = e.ColumnIndex == 0 ? row.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff") : SampleText(row.Samples[e.ColumnIndex - 1]);
        };
        _log.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _historyCount || e.ColumnIndex <= 0) return;
            var sample = History(e.RowIndex).Samples[e.ColumnIndex - 1];
            e.CellStyle!.ForeColor = sample.RoundtripMs is null ? Color.FromArgb(255, 133, 139) : sample.RoundtripMs >= 100 ? Color.FromArgb(249, 201, 112) : Accent;
        };
        _log.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _historyCount) return;
            var row = History(e.RowIndex);
            e.ToolTipText = e.ColumnIndex == 0 ? row.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") : row.Samples[e.ColumnIndex - 1].Status;
        };
        _log.Scroll += (_, e) => { if (!_logUpdating && e.ScrollOrientation == ScrollOrientation.VerticalScroll) _follow.Checked = false; };
        _clock.Tick += (_, _) => UpdateSession();
        _networkClock.Tick += async (_, _) => await RefreshNetworkAsync();
        FormClosing += ClosingAsync;
        Shown += async (_, _) =>
        {
            await RefreshNetworkAsync(); _networkClock.Start();
            if (warning != null) MessageBox.Show(this, warning, "pingy — настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        RebuildTree(); PrepareSession([]);
        _status.Text = "Настройки сохраняются автоматически · примеры 10.10.10.x отключены";
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 8), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        var brand = LabelOf("pingy"); brand.Font = new Font("Segoe UI", 28, FontStyle.Bold); brand.ForeColor = Accent; brand.AutoEllipsis = false;
        header.Controls.Add(brand, 0, 0);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(8, 9, 0, 6) };
        var title = LabelOf("Сеть под наблюдением"); title.Font = new Font(Font, FontStyle.Bold); heading.Controls.Add(title); _session.ForeColor = Muted; heading.Controls.Add(_session);
        header.Controls.Add(heading, 1, 0); _start.Dock = DockStyle.Fill; _start.Margin = new Padding(0, 9, 0, 11); _start.Font = new Font(Font, FontStyle.Bold); header.Controls.Add(_start, 2, 0); root.Controls.Add(header, 0, 0);
        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        settings.Controls.Add(InlineLabel("Интервал, с")); settings.Controls.Add(_interval); settings.Controls.Add(InlineLabel("Тайм-аут, с")); settings.Controls.Add(_timeout);
        settings.Controls.Add(_follow); _portable.AutoSize = true; _portable.Margin = new Padding(14, 3, 0, 3); settings.Controls.Add(_portable); root.Controls.Add(settings, 0, 1);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1180, 490), FixedPanel = FixedPanel.Panel1, Panel1MinSize = 220, Panel2MinSize = 560, SplitterWidth = 12, SplitterDistance = 300, BackColor = Bg, Margin = new Padding(0, 0, 0, 12) };
        root.Controls.Add(split, 0, 2);
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = PanelColor, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var hostsTitle = LabelOf("ХОСТЫ И ГРУППЫ"); hostsTitle.Font = new Font("Segoe UI", 9, FontStyle.Bold); hostsTitle.ForeColor = Muted; sidebar.Controls.Add(hostsTitle, 0, 0); sidebar.Controls.Add(_selected, 0, 1); sidebar.Controls.Add(_hosts, 0, 2); _manage.Dock = DockStyle.Fill; sidebar.Controls.Add(_manage, 0, 3); split.Panel1.Controls.Add(sidebar);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        _summary.Font = new Font("Segoe UI", 10, FontStyle.Bold); right.Controls.Add(_summary, 0, 0); right.Controls.Add(_log, 0, 1);
        var statTitle = LabelOf("СТАТИСТИКА С НАЧАЛА СЕССИИ   ·   среднее только по ответам"); statTitle.ForeColor = Muted; statTitle.Font = new Font("Segoe UI", 9, FontStyle.Bold); right.Controls.Add(statTitle, 0, 2); right.Controls.Add(_stats, 0, 3); split.Panel2.Controls.Add(right);
        foreach (var (name, text, width) in new[] { ("host", "Хост", 190), ("last", "Сейчас", 88), ("avg", "Среднее", 88), ("min", "Мин.", 72), ("max", "Макс.", 82), ("loss", "Потери", 88), ("sent", "Отправлено", 100), ("received", "Ответы", 80) })
            _stats.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = text, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        _stats.Columns[0].Frozen = true;
        var networkPanel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = PanelColor, Padding = new Padding(12, 8, 12, 8), RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        networkPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); networkPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var networkHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty }; networkHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); networkHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        var netTitle = LabelOf("ЭТОТ КОМПЬЮТЕР   ·   IP-адреса и шлюзы активных адаптеров"); netTitle.ForeColor = Muted; netTitle.Font = new Font("Segoe UI", 9, FontStyle.Bold); networkHeader.Controls.Add(netTitle, 0, 0);
        var refresh = ButtonOf("Обновить"); refresh.Dock = DockStyle.Fill; refresh.Margin = Padding.Empty; refresh.Font = new Font("Segoe UI", 8); refresh.Click += async (_, _) => await RefreshNetworkAsync(); networkHeader.Controls.Add(refresh, 1, 0); networkPanel.Controls.Add(networkHeader, 0, 0); networkPanel.Controls.Add(_network, 0, 1); root.Controls.Add(networkPanel, 0, 3);
        _status.ForeColor = Muted; _status.Font = new Font("Segoe UI", 9); root.Controls.Add(_status, 0, 4);
    }

    private void RebuildTree()
    {
        _treeUpdating = true; _hosts.BeginUpdate(); _hosts.Nodes.Clear();
        foreach (var group in _config.Hosts.GroupBy(h => h.Group))
        {
            var parent = new TreeNode(group.Key) { Checked = group.All(h => h.Enabled), ForeColor = Muted };
            foreach (var host in group) parent.Nodes.Add(new TreeNode($"{host.Name}  ({host.Address})") { Tag = host, Checked = host.Enabled, ToolTipText = host.Description });
            _hosts.Nodes.Add(parent); parent.Expand();
        }
        _hosts.ShowNodeToolTips = true; _hosts.EndUpdate(); _treeUpdating = false; UpdateSelection();
    }

    private void HostChecked(object? sender, TreeViewEventArgs e)
    {
        if (_treeUpdating || e.Node == null) return;
        _treeUpdating = true;
        if (e.Node.Tag is HostEntry host) host.Enabled = e.Node.Checked;
        else foreach (TreeNode child in e.Node.Nodes) { child.Checked = e.Node.Checked; ((HostEntry)child.Tag!).Enabled = child.Checked; }
        if (e.Node.Parent is { } parent) parent.Checked = parent.Nodes.Cast<TreeNode>().All(n => n.Checked);
        _treeUpdating = false; UpdateSelection(); SaveSettings();
    }

    private void UpdateSelection() => _selected.Text = $"Выбрано {_config.Hosts.Count(h => h.Enabled)} из {_config.Hosts.Count}";
    private bool SaveSettings()
    {
        _config.IntervalMs = (int)(_interval.Value * 1000); _config.TimeoutMs = (int)(_timeout.Value * 1000);
        try { _store.Save(_config); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        { MessageBox.Show(this, "Не удалось сохранить настройки.\n\n" + ex.Message, "pingy", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }

    private void ManageHosts()
    {
        _config.IntervalMs = (int)(_interval.Value * 1000); _config.TimeoutMs = (int)(_timeout.Value * 1000);
        using var dialog = new HostManagerDialog(_config);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _config = dialog.Result; _interval.Value = _config.IntervalMs / 1000m; _timeout.Value = _config.TimeoutMs / 1000m;
        RebuildTree(); SaveSettings(); _status.Text = "Хосты обновлены · изменения применятся при следующем старте";
    }

    private async Task ToggleAsync()
    {
        if (_cts != null)
        {
            _start.Enabled = false; _start.Text = "Остановка…"; _cts.Cancel();
            if (_runTask != null) await _runTask;
            return;
        }
        var hosts = _config.Hosts.Where(h => h.Enabled).Select(h => h.Clone()).ToArray();
        if (hosts.Length == 0) { MessageBox.Show(this, "Выберите хотя бы один хост слева или добавьте его через «Управление хостами».", "pingy"); return; }
        SaveSettings(); PrepareSession(hosts); _cts = new CancellationTokenSource();
        _elapsed.Restart(); _clock.Start(); SetRunning(true); UpdateSession();
        _runTask = RunSessionAsync(hosts, _cts.Token);
    }

    private async Task RunSessionAsync(HostEntry[] hosts, CancellationToken token)
    {
        try
        {
            await Task.Run(() => new PingMonitor().RunAsync(hosts, _config.IntervalMs, _config.TimeoutMs, batch =>
            {
                if (token.IsCancellationRequested || IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(() => { if (!token.IsCancellationRequested && !IsDisposed) AddBatch(batch); }); }
                catch (InvalidOperationException) { /* Window closed while a ping completed. */ }
            }, token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { MessageBox.Show(this, "Мониторинг остановлен.\n\n" + ex.Message, "pingy", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            _elapsed.Stop(); _clock.Stop(); _cts?.Dispose(); _cts = null; SetRunning(false); UpdateSession();
            _status.Text = $"Сессия остановлена · {_totalRows:N0} строк · следующий «Старт» сбросит журнал и статистику";
        }
    }

    private void SetRunning(bool running)
    {
        _start.Text = running ? "■  Стоп" : "▶  Старт"; _start.Enabled = true;
        _start.BackColor = running ? Color.FromArgb(242, 149, 132) : Accent;
        _manage.Enabled = _interval.Enabled = _timeout.Enabled = _portable.Enabled = !running;
        if (running) _status.Text = "Идёт опрос · прокрутка вверх приостанавливает слежение · выбор хостов доступен после остановки";
    }

    private void PrepareSession(HostEntry[] hosts)
    {
        _logUpdating = true; _active = hosts; _statistics.Clear(); _history = new PingBatch?[Math.Min(10000, Math.Max(1000, 500000 / Math.Max(1, hosts.Length)))]; _historyStart = _historyCount = 0; _totalRows = 0;
        _log.RowCount = 0; _log.Columns.Clear(); _stats.Rows.Clear();
        _log.Columns.Add(new DataGridViewTextBoxColumn { Name = "time", HeaderText = "Время", Width = 130, Frozen = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        foreach (var host in hosts)
        {
            _log.Columns.Add(new DataGridViewTextBoxColumn { Name = host.Id.ToString("N"), HeaderText = $"{host.Name}\n{host.Address}", Width = 180, MinimumWidth = 100, SortMode = DataGridViewColumnSortMode.NotSortable });
            _statistics.Add(host.Id, new PingStatistics()); _stats.Rows.Add(host.Name, "—", "—", "—", "—", "—", 0, 0);
        }
        _follow.Checked = true; _logUpdating = false;
        _summary.Text = hosts.Length == 0 ? "Выберите хосты и нажмите «Старт»" : $"Ожидаем ответы от {hosts.Length} хостов…";
    }

    private PingBatch History(int index) => _history[(_historyStart + index) % _history.Length]!;
    private void AddBatch(PingBatch batch)
    {
        _logUpdating = true;
        if (_historyCount == _history.Length) { _history[_historyStart] = batch; _historyStart = (_historyStart + 1) % _history.Length; }
        else { _history[(_historyStart + _historyCount) % _history.Length] = batch; _historyCount++; _log.RowCount = _historyCount; }
        _totalRows++;
        for (var i = 0; i < _active.Length; i++)
        {
            var sample = batch.Samples[i]; var stat = _statistics[_active[i].Id]; stat.Add(sample);
            _stats.Rows[i].SetValues(_active[i].Name, SampleText(sample), Ms(stat.AverageMs), Ms(stat.MinimumMs), Ms(stat.MaximumMs), $"{stat.LossPercent:0.0}% ({stat.Lost})", stat.Sent, stat.Received);
            _stats.Rows[i].Cells[1].Style.ForeColor = sample.RoundtripMs is null ? Color.Salmon : Accent;
        }
        _log.Invalidate(); if (_follow.Checked) ScrollToEnd(); _logUpdating = false;
        var answers = batch.Samples.Count(s => s.RoundtripMs.HasValue);
        _summary.Text = $"Ответили {answers} / {_active.Length}   ·   серия #{_totalRows:N0}   ·   {batch.Timestamp.ToLocalTime():HH:mm:ss}";
        if (_totalRows > _history.Length) _status.Text = $"В журнале последние {_history.Length:N0} строк · статистика учитывает всю сессию · всего {_totalRows:N0} строк";
    }

    private void ScrollToEnd()
    {
        if (_log.RowCount == 0) return;
        var before = _logUpdating; _logUpdating = true;
        _log.FirstDisplayedScrollingRowIndex = Math.Max(0, _log.RowCount - Math.Max(1, _log.DisplayedRowCount(false)));
        _logUpdating = before;
    }

    private void UpdateSession() => _session.Text = _cts != null ? $"● Мониторинг   ·   {_elapsed.Elapsed:hh\\:mm\\:ss}   ·   интервал {_interval.Value:0.##} с" : _totalRows == 0 ? "Готов к работе · ICMP / IPv4 / IPv6" : $"Сессия завершена   ·   {_elapsed.Elapsed:hh\\:mm\\:ss}";

    private async Task RefreshNetworkAsync()
    {
        if (_networkLoading) return; _networkLoading = true;
        try
        {
            var adapters = await Task.Run(NetworkInfo.GetAdapters);
            if (!IsDisposed) _network.Text = adapters.Count == 0 ? "Нет активных сетевых адаптеров с IP-адресами." : string.Join(Environment.NewLine, adapters.Select(a => $"{a.Name}  |  IP: {string.Join(", ", a.Addresses)}  |  Шлюз: {(a.Gateways.Count == 0 ? "нет" : string.Join(", ", a.Gateways))}"));
        }
        catch (Exception ex) { if (!IsDisposed) _network.Text = "Не удалось получить сетевые адреса: " + ex.Message; }
        finally { _networkLoading = false; }
    }

    private async Task CreatePortableAsync()
    {
#pragma warning disable IL3000 // An empty assembly Location deliberately detects a bundled executable.
        if (!string.IsNullOrEmpty(typeof(MainForm).Assembly.Location))
#pragma warning restore IL3000
        { MessageBox.Show(this, "Эта функция доступна в переносимой сборке. Выполните scripts/publish.ps1 и запустите готовый pingy.exe из artifacts.", "pingy"); return; }
        _config.IntervalMs = (int)(_interval.Value * 1000); _config.TimeoutMs = (int)(_timeout.Value * 1000);
        using var save = new SaveFileDialog { Title = "Сохранить программу со встроенными хостами", Filter = "Программа pingy (*.exe)|*.exe", FileName = "pingy-custom.exe", DefaultExt = "exe", AddExtension = true, OverwritePrompt = true };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        _portable.Enabled = _start.Enabled = false; _copying = true;
        try
        {
            var config = _config.Clone();
            await Task.Run(() => PortableConfig.WriteCopy(Environment.ProcessPath!, save.FileName, config));
            _status.Text = "Копия программы создана: " + save.FileName;
            MessageBox.Show(this, "Готово. Передайте этот .exe на другой компьютер — текущие хосты, группы и выбор уже внутри.\n\nУ копии собственные сохранённые настройки.", "pingy", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, "Не удалось создать копию.\n\n" + ex.Message, "pingy", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _copying = false; if (!IsDisposed) _portable.Enabled = _start.Enabled = true; }
    }

    private async void ClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        if (_copying) { _status.Text = "Создаётся копия программы — дождитесь завершения перед закрытием"; return; }
        _closing = true;
        try
        {
            if (_cts != null) { _cts.Cancel(); if (_runTask != null) await _runTask; }
            if (!SaveSettings() && MessageBox.Show(this, "Настройки не сохранены. Закрыть программу без сохранения?", "pingy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _clock.Stop(); _networkClock.Stop(); _allowClose = true;
            BeginInvoke(Close);
        }
        finally { _closing = false; }
    }

    protected override void Dispose(bool disposing)
    { if (disposing) { _clock.Dispose(); _networkClock.Dispose(); _cts?.Cancel(); } base.Dispose(disposing); }

    private static string Ms(double? value) => value.HasValue ? value.Value < 1 ? "<1 мс" : $"{value.Value:0.0} мс" : "—";
    private static string SampleText(PingSample sample) => sample.RoundtripMs is { } ms ? ms == 0 ? "<1 мс" : $"{ms} мс" : sample.Status == "TimedOut" ? "Тайм-аут" : "Нет ответа";
    private static Label LabelOf(string text) => new() { Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink, Margin = Padding.Empty };
    private static Label InlineLabel(string text) => new() { Text = text, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 12, 8, 0) };
    private static NumericUpDown NumberOf(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = value, DecimalPlaces = 2, Increment = 0.25m, Width = 78, BackColor = PanelColor, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 7, 18, 0) };
    private static Button ButtonOf(string text, bool primary = false)
    {
        var button = new Button { Text = text, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : PanelColor, ForeColor = primary ? Bg : Ink, Cursor = Cursors.Hand, Padding = new Padding(8, 2, 8, 2), UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = Line; button.FlatAppearance.BorderSize = primary ? 0 : 1; return button;
    }
    private static DataGridView Grid()
    {
        var grid = new BufferedGrid { Dock = DockStyle.Fill, BackgroundColor = PanelColor, BorderStyle = BorderStyle.None, GridColor = Line, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, EnableHeadersVisualStyles = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = true, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText, ColumnHeadersHeight = 50, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, Margin = Padding.Empty };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = PanelColor, ForeColor = Ink, SelectionBackColor = Color.FromArgb(43, 71, 87), SelectionForeColor = Ink, Font = new Font("Consolas", 10), Padding = new Padding(9, 0, 6, 0) };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(31, 47, 59), ForeColor = Ink, Font = new Font("Segoe UI", 9, FontStyle.Bold), SelectionBackColor = Line, WrapMode = DataGridViewTriState.True, Padding = new Padding(8, 0, 6, 0) };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(19, 30, 39); grid.RowTemplate.Height = 29; return grid;
    }

    private sealed class BufferedGrid : DataGridView { public BufferedGrid() { DoubleBuffered = true; } }
}
