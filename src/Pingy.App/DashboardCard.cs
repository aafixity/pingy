namespace Pingy.App;

internal sealed class DashboardCard : UserControl
{
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
    public string PanelTitle { get; }
    public event EventHandler? CloseRequested;
    public event EventHandler? FloatRequested;

    public DashboardCard(string title, Control content)
    {
        PanelTitle = title;
        Dock = DockStyle.Fill; BackColor = UiTheme.Surface; Margin = Padding.Empty;
        var layout = UiTheme.Table(1, 2); layout.Padding = new Padding(16);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = UiTheme.Table(3); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        var label = UiTheme.Label(title); label.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        var pop = HeaderButton("↗", "Open in a separate window");
        var close = HeaderButton("×", "Close panel");
        pop.Click += (_, _) => FloatRequested?.Invoke(this, EventArgs.Empty);
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        header.Controls.Add(label, 0, 0); header.Controls.Add(pop, 1, 0); header.Controls.Add(close, 2, 0);
        content.Dock = DockStyle.Fill; _body.Controls.Add(content);
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(_body, 0, 1); Controls.Add(layout);
    }

    private static Button HeaderButton(string text, string tip)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 12), Cursor = Cursors.Hand, Margin = new Padding(4, 0, 0, 4), TabStop = false };
        button.FlatAppearance.BorderSize = 0; new ToolTip().SetToolTip(button, tip); return button;
    }
}
