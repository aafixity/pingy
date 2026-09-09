using System.Diagnostics;
using System.Globalization;
using Pingy.Core;

namespace Pingy.App;

internal sealed class MainForm : Form
{
    private static readonly Color Bg = UiTheme.Canvas, PanelColor = UiTheme.Surface, Ink = UiTheme.Ink, Muted = UiTheme.Muted, Accent = UiTheme.Coral, Line = UiTheme.Line;
    private AppConfig _config;
    private readonly ConfigStore _store;
    private readonly Button _start = ButtonOf("Start", true), _manage = ButtonOf("Manage hosts"), _portable = ButtonOf("Export app with hosts");
    private readonly TreeView _hosts = new() { Dock = DockStyle.Fill, CheckBoxes = true, ShowLines = false, ShowPlusMinus = true, ShowRootLines = false, BorderStyle = BorderStyle.None, FullRowSelect = true, HideSelection = false, ItemHeight = 30 };
    private readonly Label _selected = LabelOf(""), _session = LabelOf(""), _summary = LabelOf("");
    private readonly NumericUpDown _interval = NumberOf(0.25m, 60, 1), _timeout = NumberOf(0.1m, 10, 1);
    private readonly CheckBox _follow = new() { Text = "Auto-scroll", Checked = true, AutoSize = true, ForeColor = Ink, Margin = new Padding(14, 12, 8, 0) };
    private readonly CleanGrid _log = Grid(), _stats = Grid();
    private readonly CleanGrid _network = Grid();
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
        Text = "Pingy"; Icon = UiTheme.LoadIcon(); BackColor = Bg; ForeColor = Ink;
        Font = new Font("Segoe UI", 10); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1280, 860); MinimumSize = new Size(1080, 760); StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        _hosts.BackColor = PanelColor; _hosts.ForeColor = Ink; _hosts.LineColor = Line;
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
            e.CellStyle!.ForeColor = sample.RoundtripMs is null ? UiTheme.Danger : sample.RoundtripMs >= 100 ? UiTheme.Muted : UiTheme.Success;
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
            if (warning != null) UiDialogs.Show(this, warning, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        RebuildTree(); PrepareSession([]);
    }

    private void BuildLayout()
    {
        var root = UiTheme.Table(1, 4);
        root.Padding = new Padding(24, 14, 24, 20);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        Controls.Add(root);

        var header = UiTheme.Table(4);
        foreach (var width in new[] { 52, 130 }) header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var image = new PictureBox { Image = Icon!.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(0, 13, 12, 13), TabStop = false };
        var brand = LabelOf("Pingy"); brand.Font = new Font("Segoe UI", 24, FontStyle.Bold);
        header.Controls.Add(image, 0, 0); header.Controls.Add(brand, 1, 0);
        _session.TextAlign = ContentAlignment.MiddleRight; _session.ForeColor = Muted; _session.Margin = new Padding(0, 0, 20, 0);
        header.Controls.Add(_session, 2, 0);
        _start.Dock = DockStyle.Fill; _start.Margin = new Padding(0, 12, 0, 12); header.Controls.Add(_start, 3, 0);
        root.Controls.Add(header, 0, 0);

        var settings = UiTheme.Table(7);
        foreach (var width in new[] { 88, 92, 96, 92, 150 }) settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settings.Controls.Add(LabelOf("Interval (s)"), 0, 0); settings.Controls.Add(_interval, 1, 0);
        settings.Controls.Add(LabelOf("Timeout (s)"), 2, 0); settings.Controls.Add(_timeout, 3, 0);
        foreach (var number in new[] { _interval, _timeout }) { number.Anchor = AnchorStyles.Left; number.Margin = new Padding(0, 0, 16, 0); number.Width = 76; }
        _follow.Dock = DockStyle.Fill; _follow.Margin = new Padding(8, 0, 0, 0); _follow.TextAlign = ContentAlignment.MiddleLeft;
        settings.Controls.Add(_follow, 4, 0); _portable.Dock = DockStyle.Fill; _portable.Margin = new Padding(0, 10, 0, 10); settings.Controls.Add(_portable, 6, 0);
        root.Controls.Add(settings, 0, 1);

        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1200, 500), FixedPanel = FixedPanel.Panel1, Panel1MinSize = 230, Panel2MinSize = 640, SplitterWidth = 16, SplitterDistance = 280, BackColor = Bg, Margin = new Padding(0, 8, 0, 16) };
        root.Controls.Add(split, 0, 2);
        var sidebar = UiTheme.Card(4);
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var hostsTitle = LabelOf("Hosts"); hostsTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        _selected.ForeColor = Muted; _selected.Font = new Font("Segoe UI", 9);
        sidebar.Controls.Add(hostsTitle, 0, 0); sidebar.Controls.Add(_selected, 0, 1); sidebar.Controls.Add(_hosts, 0, 2);
        _manage.Dock = DockStyle.Fill; sidebar.Controls.Add(_manage, 0, 3); split.Panel1.Controls.Add(sidebar);

        var right = UiTheme.Table(1, 2); right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 64)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        var logCard = UiTheme.Card(2); logCard.Margin = new Padding(0, 0, 0, 12);
        logCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); logCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logHeader = UiTheme.Table(2); logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); logHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logTitle = LabelOf("Ping log"); logTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold); logHeader.Controls.Add(logTitle, 0, 0);
        _summary.TextAlign = ContentAlignment.MiddleRight; _summary.ForeColor = Muted; _summary.Font = new Font("Segoe UI", 9); logHeader.Controls.Add(_summary, 1, 0);
        logCard.Controls.Add(logHeader, 0, 0); logCard.Controls.Add(_log, 0, 1);
        var statCard = UiTheme.Card(2); statCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); statCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statTitle = LabelOf("Session statistics"); statTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold); statCard.Controls.Add(statTitle, 0, 0); statCard.Controls.Add(_stats, 0, 1);
        right.Controls.Add(logCard, 0, 0); right.Controls.Add(statCard, 0, 1); split.Panel2.Controls.Add(right);
        foreach (var (name, text, width) in new[] { ("host", "Host", 175), ("last", "Latest", 82), ("avg", "Average", 90), ("min", "Min", 76), ("max", "Max", 76), ("loss", "Loss", 104), ("sent", "Sent", 76), ("received", "Replies", 82) })
        {
            var column = new DataGridViewTextBoxColumn { Name = name, HeaderText = text, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (name != "host") { column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight; }
            _stats.Columns.Add(column);
        }
        _stats.Columns[0].Frozen = true; _stats.EmptyText = "No session data";

        var networkCard = UiTheme.Card(2);
        networkCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); networkCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var networkHeader = UiTheme.Table(2); networkHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); networkHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); networkHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var netTitle = LabelOf("Local network"); netTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold); networkHeader.Controls.Add(netTitle, 0, 0);
        var refresh = ButtonOf("Refresh"); refresh.Dock = DockStyle.Fill; refresh.Margin = new Padding(0, 0, 0, 5); refresh.Click += async (_, _) => await RefreshNetworkAsync(); networkHeader.Controls.Add(refresh, 1, 0);
        foreach (var (name, title, weight) in new[] { ("adapter", "Adapter", 23f), ("address", "IP address", 49f), ("gateway", "Gateway", 28f) })
            _network.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        _network.ColumnHeadersHeight = 30; _network.RowTemplate.Height = 30; _network.EmptyText = "No active adapters";
        networkCard.Controls.Add(networkHeader, 0, 0); networkCard.Controls.Add(_network, 0, 1); root.Controls.Add(networkCard, 0, 3);
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

    private void UpdateSelection() { _selected.Text = $"{_config.Hosts.Count(h => h.Enabled)} of {_config.Hosts.Count} selected"; if (_cts == null && !_copying) _start.Enabled = _config.Hosts.Any(h => h.Enabled); }
    private bool SaveSettings()
    {
        _config.IntervalMs = (int)(_interval.Value * 1000); _config.TimeoutMs = (int)(_timeout.Value * 1000);
        try { _store.Save(_config); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        { UiDialogs.Show(this, "Could not save settings.\n\n" + ex.Message, "pingy", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
    }

    private void ManageHosts()
    {
        _config.IntervalMs = (int)(_interval.Value * 1000); _config.TimeoutMs = (int)(_timeout.Value * 1000);
        using var dialog = new HostManagerDialog(_config);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _config = dialog.Result; _interval.Value = _config.IntervalMs / 1000m; _timeout.Value = _config.TimeoutMs / 1000m;
        RebuildTree(); SaveSettings(); if (_totalRows == 0) PrepareSession([]);
    }

    private async Task ToggleAsync()
    {
        if (_cts != null)
        {
            _start.Enabled = false; _start.Text = "Stopping…"; _cts.Cancel();
            if (_runTask != null) await _runTask;
            return;
        }
        var hosts = _config.Hosts.Where(h => h.Enabled).Select(h => h.Clone()).ToArray();
        if (hosts.Length == 0) { UiDialogs.Show(this, "Select at least one host, or add one in Manage hosts.", "pingy"); return; }
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
        catch (Exception ex) { UiDialogs.Show(this, "Monitoring stopped.\n\n" + ex.Message, "pingy", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            _elapsed.Stop(); _clock.Stop(); _cts?.Dispose(); _cts = null; SetRunning(false); UpdateSession();
        }
    }

    private void SetRunning(bool running)
    {
        _start.Text = running ? "Stop" : "Start"; _start.Enabled = true;
        _start.BackColor = running ? UiTheme.Sand : Accent;
        _manage.Enabled = _interval.Enabled = _timeout.Enabled = _portable.Enabled = !running;
        if (!running) UpdateSelection();
    }

    private void PrepareSession(HostEntry[] hosts)
    {
        _logUpdating = true; _active = hosts; _statistics.Clear(); _history = new PingBatch?[Math.Min(10000, Math.Max(1000, 500000 / Math.Max(1, hosts.Length)))]; _historyStart = _historyCount = 0; _totalRows = 0;
        _log.RowCount = 0; _log.Columns.Clear(); _stats.Rows.Clear();
        if (hosts.Length > 0) _log.Columns.Add(new DataGridViewTextBoxColumn { Name = "time", HeaderText = "Time", Width = 130, Frozen = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        foreach (var host in hosts)
        {
            _log.Columns.Add(new DataGridViewTextBoxColumn { Name = host.Id.ToString("N"), HeaderText = $"{host.Name}\n{host.Address}", Width = 190, MinimumWidth = 120, SortMode = DataGridViewColumnSortMode.NotSortable });
            _statistics.Add(host.Id, new PingStatistics()); _stats.Rows.Add(host.Name, "—", "—", "—", "—", "—", 0, 0);
        }
        _log.ColumnHeadersHeight = 54; _log.EmptyText = hosts.Length == 0 ? (_config.Hosts.Count == 0 ? "Add a host to begin" : "Select hosts and press Start") : "Waiting for replies";
        _log.ClearSelection(); _stats.ClearSelection();
        _follow.Checked = true; _logUpdating = false;
        _summary.Text = "";
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
            _stats.Rows[i].Cells[1].Style.ForeColor = sample.RoundtripMs is null ? UiTheme.Danger : UiTheme.Success;
        }
        _log.Invalidate(); if (_follow.Checked) ScrollToEnd(); _logUpdating = false;
        var answers = batch.Samples.Count(s => s.RoundtripMs.HasValue);
        _summary.Text = $"{answers}/{_active.Length} replies  ·  {_totalRows:N0} rounds" + (_totalRows > _history.Length ? $"  ·  last {_history.Length:N0} shown" : "");
    }

    private void ScrollToEnd()
    {
        if (_log.RowCount == 0) return;
        var before = _logUpdating; _logUpdating = true;
        _log.FirstDisplayedScrollingRowIndex = Math.Max(0, _log.RowCount - Math.Max(1, _log.DisplayedRowCount(false)));
        _logUpdating = before;
    }

    private void UpdateSession() => _session.Text = _totalRows == 0 && _cts == null ? "" : $"{(_cts == null ? "Stopped" : "Running")}  ·  {_elapsed.Elapsed:hh\\:mm\\:ss}";

    private async Task RefreshNetworkAsync()
    {
        if (_networkLoading) return; _networkLoading = true;
        try
        {
            var adapters = await Task.Run(NetworkInfo.GetAdapters);
            if (!IsDisposed)
            {
                _network.Rows.Clear();
                foreach (var adapter in adapters)
                {
                    var index = _network.Rows.Add(adapter.Name, string.Join(", ", adapter.Addresses), adapter.Gateways.Count == 0 ? "—" : string.Join(", ", adapter.Gateways));
                    _network.Rows[index].Cells[1].ToolTipText = string.Join(Environment.NewLine, adapter.Addresses);
                    _network.Rows[index].Cells[2].ToolTipText = string.Join(Environment.NewLine, adapter.Gateways);
                }
                _network.ClearSelection();
            }
        }
        catch (Exception) { if (!IsDisposed) { _network.EmptyText = "Network information unavailable"; _network.Invalidate(); } }
        finally { _networkLoading = false; }
    }

    private async Task CreatePortableAsync()
    {
#pragma warning disable IL3000 // An empty assembly Location deliberately detects a bundled executable.
        if (!string.IsNullOrEmpty(typeof(MainForm).Assembly.Location))
#pragma warning restore IL3000
        { UiDialogs.Show(this, "Open a published portable build to export an app with embedded hosts.", "pingy"); return; }
        _config.IntervalMs = (int)(_interval.Value * 1000); _config.TimeoutMs = (int)(_timeout.Value * 1000);
        using var save = new SaveFileDialog { Title = "Export app with hosts", Filter = "Pingy application (*.exe)|*.exe", FileName = "pingy-custom.exe", DefaultExt = "exe", AddExtension = true, OverwritePrompt = true };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        _portable.Enabled = _start.Enabled = false; _copying = true;
        try
        {
            var config = _config.Clone();
            await Task.Run(() => PortableConfig.WriteCopy(Environment.ProcessPath!, save.FileName, config));
            UiDialogs.Show(this, "The executable includes your hosts, groups and settings.\n\nTransfer it to another PC to use the same setup.", "pingy", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { UiDialogs.Show(this, "Could not export the application.\n\n" + ex.Message, "pingy", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _copying = false; if (!IsDisposed) { _portable.Enabled = true; UpdateSelection(); } }
    }

    private async void ClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        if (_copying) { UiDialogs.Show(this, "Wait for the executable copy to finish before closing."); return; }
        _closing = true;
        try
        {
            if (_cts != null) { _cts.Cancel(); if (_runTask != null) await _runTask; }
            if (!SaveSettings() && UiDialogs.Show(this, "Settings were not saved. Close without saving?", "pingy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _clock.Stop(); _networkClock.Stop(); _allowClose = true;
            BeginInvoke(Close);
        }
        finally { _closing = false; }
    }

    protected override void Dispose(bool disposing)
    { if (disposing) { _clock.Dispose(); _networkClock.Dispose(); _cts?.Cancel(); } base.Dispose(disposing); }

    private static string Ms(double? value) => value.HasValue ? value.Value < 1 ? "<1 ms" : $"{value.Value:0.0} ms" : "—";
    private static string SampleText(PingSample sample) => sample.RoundtripMs is { } ms ? ms == 0 ? "<1 ms" : $"{ms} ms" : sample.Status == "TimedOut" ? "Timeout" : "No reply";
    private static Label LabelOf(string text) => UiTheme.Label(text);
    private static NumericUpDown NumberOf(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = value, DecimalPlaces = 2, Increment = 0.25m, Width = 76, BackColor = PanelColor, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
    private static Button ButtonOf(string text, bool primary = false) => new SoftButton(text, primary);
    private static CleanGrid Grid()
    {
        var grid = new CleanGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = true, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText, Margin = Padding.Empty };
        UiTheme.StyleGrid(grid); return grid;
    }
}
