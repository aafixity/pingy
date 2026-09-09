using System.Drawing.Drawing2D;
using System.Reflection;

namespace Pingy.App;

internal static class UiTheme
{
    public static readonly Color Canvas = ColorTranslator.FromHtml("#F5F4F2");
    public static readonly Color Surface = Color.White;
    public static readonly Color Ink = ColorTranslator.FromHtml("#302C29");
    public static readonly Color Muted = ColorTranslator.FromHtml("#7E685A");
    public static readonly Color Coral = ColorTranslator.FromHtml("#E7717D");
    public static readonly Color Gray = ColorTranslator.FromHtml("#C2CAD0");
    public static readonly Color Sand = ColorTranslator.FromHtml("#C2B9B0");
    public static readonly Color Green = ColorTranslator.FromHtml("#AFD275");
    public static readonly Color Success = ColorTranslator.FromHtml("#4D682D");
    public static readonly Color Danger = ColorTranslator.FromHtml("#AC3548");
    public static readonly Color Line = ColorTranslator.FromHtml("#E9E5E1");
    public static readonly Color Header = ColorTranslator.FromHtml("#F2EFEC");
    public static readonly Color Selection = ColorTranslator.FromHtml("#FBE5E8");

    public static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pingy.icon.ico")!;
        using var icon = new Icon(stream, 48, 48);
        return (Icon)icon.Clone();
    }

    public static Label Label(string text = "", bool muted = false) => new()
    {
        Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        ForeColor = muted ? Muted : Ink, UseMnemonic = false
    };

    public static TableLayoutPanel Table(int columns = 1, int rows = 1) => new()
    {
        Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows,
        Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent
    };

    public static TableLayoutPanel Card(int rows)
    {
        var panel = Table(1, rows);
        panel.BackColor = Surface; panel.Padding = new Padding(16);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    public static void StyleGrid(DataGridView grid, bool editable = false)
    {
        grid.BackgroundColor = Surface; grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Line; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false; grid.RowHeadersVisible = false;
        grid.ColumnHeadersHeight = 44;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Ink, SelectionBackColor = Selection, SelectionForeColor = Ink,
            Font = new Font("Segoe UI", 10), Padding = new Padding(12, 0, 12, 0), Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Header, ForeColor = Muted, SelectionBackColor = Header, SelectionForeColor = Muted,
            Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(12, 0, 12, 0),
            Alignment = DataGridViewContentAlignment.MiddleLeft, WrapMode = DataGridViewTriState.True
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 251, 250);
        grid.RowTemplate.Height = 36;
        // Native header painting adds bevels on some Windows themes, even with flat borders.
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0) return;
            using var fill = new SolidBrush(Header);
            e.Graphics!.FillRectangle(fill, e.CellBounds);
            var padding = Math.Max(8, (int)Math.Round(12 * grid.DeviceDpi / 96f));
            var rect = Rectangle.Inflate(e.CellBounds, -padding, -2);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            flags |= e.CellStyle!.Alignment == DataGridViewContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.Left;
            if (!(e.FormattedValue?.ToString() ?? "").Contains('\n')) flags |= TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, e.FormattedValue?.ToString() ?? "", e.CellStyle.Font, rect, Muted, flags);
            e.Handled = true;
        };
        if (editable)
            grid.EditingControlShowing += (_, e) => { e.Control.BackColor = Surface; e.Control.ForeColor = Ink; };
    }

    internal static GraphicsPath Round(RectangleF rect, float radius)
    {
        var path = new GraphicsPath(); float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure();
        return path;
    }
}

internal sealed class SoftButton : Button
{
    [System.ComponentModel.DefaultValue(false)]
    public bool Primary { get; set; }
    private bool _hover, _pressed;
    public SoftButton(string text = "", bool primary = false)
    {
        Text = text; Primary = primary; Cursor = Cursors.Hand; Height = 40;
        Font = new Font("Segoe UI", 10, FontStyle.Bold);
        BackColor = primary ? UiTheme.Coral : UiTheme.Surface; ForeColor = UiTheme.Ink;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Margin = Padding.Empty; Padding = Padding.Empty; UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var background = Parent;
        while (background != null && background.BackColor.A != 255) background = background.Parent;
        e.Graphics.Clear(background?.BackColor ?? UiTheme.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = !Enabled ? UiTheme.Header : BackColor;
        if (Enabled && _hover) color = ControlPaint.Light(color, _pressed ? 0.05f : 0.18f);
        using var path = UiTheme.Round(new RectangleF(1, 1, Width - 3, Height - 3), 8 * DeviceDpi / 96f);
        using var brush = new SolidBrush(color); e.Graphics.FillPath(brush, path);
        if (!Primary) { using var pen = new Pen(UiTheme.Line); e.Graphics.DrawPath(pen, path); }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(153, 149, 145),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6));
    }
}

internal sealed class CleanGrid : DataGridView
{
    [System.ComponentModel.DefaultValue("")]
    public string EmptyText { get; set; } = "";
    public CleanGrid() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (RowCount != 0 || string.IsNullOrEmpty(EmptyText)) return;
        var top = ColumnCount == 0 ? 0 : ColumnHeadersHeight;
        TextRenderer.DrawText(e.Graphics, EmptyText, Font, new Rectangle(0, top, ClientSize.Width, Math.Max(0, ClientSize.Height - top)), UiTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

/// <summary>App-owned dialogs keep English button labels regardless of the Windows display language.</summary>
internal static class UiDialogs
{
    public static DialogResult Show(string text, string caption = "Pingy", MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None) => Show(null, text, caption, buttons, icon);
    public static DialogResult Show(IWin32Window? owner, string text, string caption = "Pingy", MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        using var dialog = new Form
        {
            Text = caption, Icon = UiTheme.LoadIcon(), BackColor = UiTheme.Surface, ForeColor = UiTheme.Ink,
            Font = new Font("Segoe UI", 10), AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent, ClientSize = new Size(540, 250)
        };
        var layout = UiTheme.Table(1, 2); layout.Padding = new Padding(24); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var content = new TextBox { Text = text, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = UiTheme.Surface, ForeColor = UiTheme.Ink, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, TabStop = false };
        layout.Controls.Add(content, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var choices = buttons == MessageBoxButtons.YesNo ? new[] { ("No", DialogResult.No), ("Yes", DialogResult.Yes) } : new[] { ("OK", DialogResult.OK) };
        foreach (var (label, result) in choices)
        {
            var button = new SoftButton(label, result == DialogResult.OK) { Width = 105, Dock = DockStyle.None, Margin = new Padding(8, 0, 0, 0), DialogResult = result };
            actions.Controls.Add(button);
            if (result is DialogResult.No or DialogResult.OK) { dialog.CancelButton = button; dialog.AcceptButton = button; }
        }
        layout.Controls.Add(actions, 0, 1); dialog.Controls.Add(layout);
        return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}
