namespace Pingy.App;

internal sealed partial class MainForm
{
    private void BuildLayout()
    {
        var root = UiTheme.Table(1, 3); root.Padding = new Padding(24, 14, 24, 20);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = UiTheme.Table(4); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var image = new PictureBox { Image = Icon!.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(0, 13, 12, 13), TabStop = false };
        var brand = LabelOf("Pingy"); brand.Font = new Font("Segoe UI", 24, FontStyle.Bold);
        _session.TextAlign = ContentAlignment.MiddleRight; _session.ForeColor = Muted; _session.Margin = new Padding(0, 0, 20, 0);
        _start.Dock = DockStyle.Fill; _start.Margin = new Padding(0, 12, 0, 12);
        header.Controls.Add(image, 0, 0); header.Controls.Add(brand, 1, 0); header.Controls.Add(_session, 2, 0); header.Controls.Add(_start, 3, 0); root.Controls.Add(header, 0, 0);

        var settings = UiTheme.Table(9); foreach (var width in new[] { 88, 92, 96, 92, 145, 88 }) settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settings.Controls.Add(LabelOf("Interval (s)"), 0, 0); settings.Controls.Add(_interval, 1, 0); settings.Controls.Add(LabelOf("Timeout (s)"), 2, 0); settings.Controls.Add(_timeout, 3, 0);
        foreach (var number in new[] { _interval, _timeout }) { number.Anchor = AnchorStyles.Left; number.Margin = new Padding(0, 0, 16, 0); number.Width = 76; }
        _follow.Dock = DockStyle.Fill; _follow.Margin = new Padding(8, 0, 0, 0); _follow.TextAlign = ContentAlignment.MiddleLeft; settings.Controls.Add(_follow, 4, 0);
        _view.Dock = DockStyle.Fill; _view.Margin = new Padding(0, 10, 8, 10); _view.Click += (_, _) => _viewMenu.Show(_view, new Point(0, _view.Height)); settings.Controls.Add(_view, 5, 0);
        _portable.Dock = DockStyle.Fill; _portable.Margin = new Padding(0, 10, 0, 10); settings.Controls.Add(_portable, 7, 0); root.Controls.Add(settings, 0, 1);

        _mainSplit = Split(Orientation.Horizontal);
        _upperSplit = Split(Orientation.Vertical);
        _rightSplit = Split(Orientation.Horizontal);
        _mainSplit.Panel1.Controls.Add(_upperSplit); _upperSplit.Panel2.Controls.Add(_rightSplit); root.Controls.Add(_mainSplit, 0, 2);

        var hostsBody = UiTheme.Table(1, 3); hostsBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); hostsBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); hostsBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _selected.ForeColor = Muted; _selected.Font = new Font("Segoe UI", 9); _manage.Dock = DockStyle.Fill;
        hostsBody.Controls.Add(_selected, 0, 0); hostsBody.Controls.Add(_hosts, 0, 1); hostsBody.Controls.Add(_manage, 0, 2);

        var logBody = UiTheme.Table(1, 2); logBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); logBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _summary.TextAlign = ContentAlignment.MiddleRight; _summary.ForeColor = Muted; _summary.Font = new Font("Segoe UI", 9); logBody.Controls.Add(_summary, 0, 0); logBody.Controls.Add(_log, 0, 1);

        ConfigureStatisticsColumns();
        var networkBody = UiTheme.Table(1, 2); networkBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); networkBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var refresh = ButtonOf("Refresh"); refresh.Width = 100; refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right; refresh.Margin = new Padding(0, 0, 0, 5); refresh.Click += async (_, _) => await RefreshNetworkAsync();
        var refreshRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty }; refreshRow.Controls.Add(refresh);
        ConfigureNetworkColumns(); networkBody.Controls.Add(refreshRow, 0, 0); networkBody.Controls.Add(_network, 0, 1);

        _hostsCard = Card("Hosts", hostsBody); _logCard = Card("Ping log", logBody); _statsCard = Card("Session statistics", _stats); _networkCard = Card("Local network", networkBody);
        foreach (var card in new[] { _hostsCard, _logCard, _statsCard, _networkCard })
        {
            _panelVisible[card] = true; card.CloseRequested += (_, _) => SetPanelVisible(card, false); card.FloatRequested += (_, _) => FloatPanel(card);
            var show = new ToolStripMenuItem(card.PanelTitle) { Checked = true, CheckOnClick = true };
            show.CheckedChanged += (_, _) => SetPanelVisible(card, show.Checked); _viewMenu.Items.Add(show);
            var pop = new ToolStripMenuItem($"Open {card.PanelTitle} separately"); pop.Click += (_, _) => FloatPanel(card); _viewMenu.Items.Add(pop);
        }
        StyleMenu(); UpdateDashboardLayout();
    }

    private static SplitContainer Split(Orientation orientation) => new()
    {
        Dock = DockStyle.Fill, Orientation = orientation, SplitterWidth = 10, BackColor = UiTheme.Canvas, Margin = Padding.Empty
    };

    private void SetInitialSplitterPositions()
    {
        SetSplitter(_mainSplit, Math.Max(300, _mainSplit.Height - 190));
        SetSplitter(_upperSplit, 280);
        SetSplitter(_rightSplit, Math.Max(180, (int)(_rightSplit.Height * .58)));
        (_mainSplit.Panel1MinSize, _mainSplit.Panel2MinSize) = (300, 135);
        (_upperSplit.Panel1MinSize, _upperSplit.Panel2MinSize) = (220, 540);
        (_rightSplit.Panel1MinSize, _rightSplit.Panel2MinSize) = (180, 150);
    }

    private static void SetSplitter(SplitContainer split, int desired)
    {
        var length = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
        var maximum = length - split.Panel2MinSize - split.SplitterWidth;
        if (maximum >= split.Panel1MinSize) split.SplitterDistance = Math.Clamp(desired, split.Panel1MinSize, maximum);
    }

    private DashboardCard Card(string title, Control body) => new(title, body);

    private void ConfigureStatisticsColumns()
    {
        foreach (var (name, text, width) in new[] { ("host", "Host", 175), ("last", "Latest", 82), ("avg", "Average", 90), ("min", "Min", 76), ("max", "Max", 76), ("loss", "Loss", 104), ("sent", "Sent", 76), ("received", "Replies", 82) })
        {
            var column = new DataGridViewTextBoxColumn { Name = name, HeaderText = text, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (name != "host") { column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight; }
            _stats.Columns.Add(column);
        }
        _stats.Columns[0].Frozen = true; _stats.EmptyText = "No session data";
    }

    private void ConfigureNetworkColumns()
    {
        foreach (var (name, title, weight) in new[] { ("adapter", "Adapter", 23f), ("address", "IP address", 49f), ("gateway", "Gateway", 28f) })
            _network.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        _network.ColumnHeadersHeight = 30; _network.RowTemplate.Height = 30; _network.EmptyText = "No active adapters";
    }

    private void StyleMenu()
    {
        _viewMenu.BackColor = UiTheme.Surface; _viewMenu.ForeColor = UiTheme.Ink; _viewMenu.Font = new Font("Segoe UI", 10);
        _viewMenu.ShowImageMargin = false; _viewMenu.ShowCheckMargin = true;
        _viewMenu.RenderMode = ToolStripRenderMode.System;
    }

    private void SetPanelVisible(DashboardCard card, bool visible)
    {
        _panelVisible[card] = visible;
        if (!visible && _floatingPanels.TryGetValue(card, out var window))
        {
            window.FormClosing -= FloatingClosing; window.Controls.Remove(card); _floatingPanels.Remove(card); window.Close();
        }
        foreach (ToolStripItem item in _viewMenu.Items) if (item is ToolStripMenuItem menu && menu.Text == card.PanelTitle) menu.Checked = visible;
        UpdateDashboardLayout();
    }

    private void FloatPanel(DashboardCard card)
    {
        if (_floatingPanels.TryGetValue(card, out var existing)) { existing.Activate(); return; }
        _panelVisible[card] = true; card.Parent?.Controls.Remove(card);
        var window = new Form { Text = $"Pingy — {card.PanelTitle}", Icon = Icon, BackColor = UiTheme.Canvas, ForeColor = UiTheme.Ink,
            Font = Font, StartPosition = FormStartPosition.CenterParent, Size = new Size(1000, 650), MinimumSize = new Size(540, 360) };
        window.Controls.Add(card); window.FormClosing += FloatingClosing; window.Tag = card; _floatingPanels[card] = window;
        UpdateDashboardLayout(); window.Show(this);
    }

    private void FloatingClosing(object? sender, FormClosingEventArgs e)
    {
        if (sender is not Form window || window.Tag is not DashboardCard card) return;
        window.Controls.Remove(card); _floatingPanels.Remove(card);
        if (!_allowClose && !IsDisposed) BeginInvoke(UpdateDashboardLayout);
    }

    private void UpdateDashboardLayout()
    {
        bool Main(DashboardCard c) => _panelVisible.GetValueOrDefault(c) && !_floatingPanels.ContainsKey(c);
        var hosts = Main(_hostsCard); var log = Main(_logCard); var stats = Main(_statsCard); var network = Main(_networkCard); var right = log || stats;
        Mount(_hostsCard, _upperSplit.Panel1, hosts); Mount(_logCard, _rightSplit.Panel1, log); Mount(_statsCard, _rightSplit.Panel2, stats); Mount(_networkCard, _mainSplit.Panel2, network);
        _rightSplit.Panel1Collapsed = !log; _rightSplit.Panel2Collapsed = !stats;
        _upperSplit.Panel1Collapsed = !hosts; _upperSplit.Panel2Collapsed = !right;
        _mainSplit.Panel1Collapsed = !hosts && !right; _mainSplit.Panel2Collapsed = !network;
    }

    private static void Mount(Control card, Control parent, bool show)
    {
        if (!show) { if (card.Parent == parent) parent.Controls.Remove(card); return; }
        if (card.Parent != parent) { card.Parent?.Controls.Remove(card); parent.Controls.Add(card); }
    }
}
