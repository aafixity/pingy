using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Pingy.Core;

namespace Pingy.App;

/// <summary>Edits a private copy so closing the dialog never changes the active profile.</summary>
public sealed class HostManagerDialog : Form
{
    private static readonly Color PageColor = Color.FromArgb(16, 24, 32);
    private static readonly Color SurfaceColor = Color.FromArgb(23, 35, 45);
    private static readonly Color TextColor = Color.FromArgb(229, 237, 242);
    private static readonly Color MutedColor = Color.FromArgb(151, 171, 184);
    private static readonly Color AccentColor = Color.FromArgb(89, 214, 178);
    private static readonly Color BorderColor = Color.FromArgb(47, 67, 81);

    private readonly Guid _profileId;
    private readonly DataGridView _grid = new();
    private readonly BindingSource _source = new();
    private readonly Label _summary = new();
    private AppConfig _working;
    private BindingList<HostEntry> _rows = new();

    public AppConfig Result { get; private set; }

    public HostManagerDialog(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _working = config.Clone();
        _profileId = config.ProfileId;
        Result = config.Clone();

        Text = "pingy — серверы и конфигурация";
        BackColor = PageColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(850, 550);
        Size = new Size(1120, 710);
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = PageColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(layout);

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label
        {
            Text = "Ваши серверы", AutoSize = true, Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = TextColor, Margin = Padding.Empty
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = "Укажите имя и IP-адрес. Одинаковая группа объединяет серверы; галочка включает их в пинг.",
            Dock = DockStyle.Fill, ForeColor = MutedColor, Margin = new Padding(0, 5, 0, 0)
        }, 0, 1);
        layout.Controls.Add(heading, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        toolbar.Controls.Add(MakeButton("+ Добавить", 126, (_, _) => AddHost(), primary: true));
        toolbar.Controls.Add(MakeButton("Удалить выбранные", 181, (_, _) => RemoveSelected()));
        toolbar.Controls.Add(MakeButton("Удалить все", 129, (_, _) => RemoveAll()));
        toolbar.Controls.Add(MakeButton("Импорт…", 105, (_, _) => ImportConfig()));
        toolbar.Controls.Add(MakeButton("Экспорт…", 111, (_, _) => ExportConfig()));
        layout.Controls.Add(toolbar, 0, 1);

        ConfigureGrid();
        layout.Controls.Add(_grid, 0, 2);

        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = MutedColor;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        _summary.Margin = Padding.Empty;
        layout.Controls.Add(_summary, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        footer.Controls.Add(new Label
        {
            Text = "Изменения применятся после сохранения.", Dock = DockStyle.Fill,
            ForeColor = MutedColor, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
        }, 0, 0);
        var cancel = MakeButton("Отмена", 108, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        cancel.DialogResult = DialogResult.Cancel;
        var save = MakeButton("Сохранить", 144, (_, _) => SaveAndClose(), primary: true);
        save.Margin = Padding.Empty;
        footer.Controls.Add(cancel, 1, 0);
        footer.Controls.Add(save, 2, 0);
        layout.Controls.Add(footer, 0, 4);
        CancelButton = cancel;

        BindHosts();
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.Margin = Padding.Empty;
        _grid.BackgroundColor = SurfaceColor;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = BorderColor;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _grid.RowHeadersVisible = false;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 42;
        _grid.RowTemplate.Height = 34;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SurfaceColor, ForeColor = MutedColor,
            SelectionBackColor = SurfaceColor, SelectionForeColor = TextColor,
            Font = new Font("Segoe UI Semibold", 9.5F), Padding = new Padding(7, 0, 7, 0)
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SurfaceColor, ForeColor = TextColor,
            SelectionBackColor = Color.FromArgb(35, 73, 77), SelectionForeColor = TextColor,
            Padding = new Padding(7, 0, 7, 0), NullValue = string.Empty
        };
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(20, 31, 40);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled", DataPropertyName = nameof(HostEntry.Enabled), HeaderText = "Вкл.",
            Width = 57, MinimumWidth = 50, Resizable = DataGridViewTriState.False,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter, Padding = Padding.Empty }
        });
        AddTextColumn("Name", nameof(HostEntry.Name), "Имя сервера", 170, 115);
        AddTextColumn("Address", nameof(HostEntry.Address), "IP-адрес", 170, 135);
        AddTextColumn("Group", nameof(HostEntry.Group), "Группа", 135, 100);
        AddTextColumn("Description", nameof(HostEntry.Description), "Описание (необязательно)", 220, 160);
        _grid.DataSource = _source;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, _) => RefreshSummary();
        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            if (e.RowIndex >= 0) _grid.Rows[e.RowIndex].ErrorText = "Проверьте значение в этой строке.";
        };
        _grid.EditingControlShowing += (_, e) =>
        {
            e.Control.BackColor = SurfaceColor;
            e.Control.ForeColor = TextColor;
        };
    }

    private void AddTextColumn(string name, string property, string title, float weight, int minimumWidth)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, DataPropertyName = property, HeaderText = title,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = weight,
            MinimumWidth = minimumWidth, SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private static Button MakeButton(string text, int width, EventHandler handler, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Width = width, Height = 38, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? AccentColor : SurfaceColor,
            ForeColor = primary ? PageColor : TextColor,
            Font = new Font("Segoe UI Semibold", 9.5F), Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 9, 0), UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? AccentColor : BorderColor;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(118, 231, 197) : Color.FromArgb(36, 53, 65);
        button.Click += handler;
        return button;
    }

    private void BindHosts()
    {
        _rows = new BindingList<HostEntry>(_working.Hosts.Select(host => host.Clone()).ToList());
        _source.DataSource = _rows;
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var groups = _rows.Select(host => host.Group?.Trim() ?? string.Empty)
            .Where(group => group.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _summary.Text = $"Серверов: {_rows.Count}     Включено: {_rows.Count(host => host.Enabled)}     Групп: {groups}     •     Двойной щелчок или F2 — редактирование";
    }

    private void AddHost()
    {
        _grid.EndEdit();
        _source.EndEdit();
        _rows.Add(new HostEntry
        {
            Id = Guid.NewGuid(), Name = "Новый сервер", Address = string.Empty,
            Group = string.Empty, Description = string.Empty, Enabled = true
        });
        int index = _rows.Count - 1;
        _grid.ClearSelection();
        _grid.CurrentCell = _grid.Rows[index].Cells["Name"];
        _grid.Rows[index].Selected = true;
        _grid.FirstDisplayedScrollingRowIndex = index;
        _grid.Focus();
        _grid.BeginEdit(true);
        RefreshSummary();
    }

    private void RemoveSelected()
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem).OfType<HostEntry>().ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Выберите строки, которые нужно удалить. Для нескольких строк удерживайте Ctrl или Shift.",
                "Удаление серверов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"Удалить выбранные серверы ({selected.Length})?\nИзменение применится после сохранения.",
                "Удаление серверов", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _grid.CancelEdit();
        foreach (var host in selected) _rows.Remove(host);
        RefreshSummary();
    }

    private void RemoveAll()
    {
        if (_rows.Count == 0) return;
        if (MessageBox.Show(this, $"Удалить все серверы ({_rows.Count})?\nИзменение применится после сохранения.",
                "Очистить список", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _grid.CancelEdit();
        _rows.Clear();
        RefreshSummary();
    }

    private AppConfig CaptureValidated()
    {
        if (!_grid.EndEdit()) throw new FormatException("Завершите редактирование текущей ячейки.");
        _source.EndEdit();
        var candidate = _working.Clone();
        candidate.Hosts = _rows.Select(host => host.Clone()).ToList();
        ConfigCodec.Validate(candidate);
        return candidate;
    }

    private void SaveAndClose()
    {
        try
        {
            Result = CaptureValidated();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (FormatException ex)
        {
            ShowError("Проверьте серверы", ex.Message);
        }
    }

    private void ImportConfig()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Импорт конфигурации pingy", Filter = "Конфигурация pingy (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(picker.FileName).Length > ConfigCodec.MaxConfigBytes)
                throw new FormatException("Конфигурация не должна превышать 4 МБ.");
            var imported = ConfigCodec.Parse(File.ReadAllText(picker.FileName, Encoding.UTF8));
            ConfigCodec.Validate(imported);
            _grid.EndEdit();
            _source.EndEdit();
            var currentAddresses = _rows.Select(host => CanonicalAddress(host.Address))
                .Where(address => address.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int duplicates = imported.Hosts.Count(host => currentAddresses.Contains(CanonicalAddress(host.Address)));
            using var choice = new ImportChoiceDialog(Path.GetFileName(picker.FileName), _rows.Count, imported.Hosts.Count,
                imported.Hosts.Count(host => host.Enabled), duplicates);
            if (choice.ShowDialog(this) != DialogResult.OK) return;

            if (choice.ReplaceExisting)
            {
                _working = imported.Clone();
                _working.ProfileId = _profileId;
            }
            else
            {
                var current = CaptureValidated();
                _working = ConfigCodec.Merge(current, imported);
                _working.ProfileId = _profileId;
            }
            BindHosts();
            _summary.Text = choice.ReplaceExisting
                ? $"Импортировано серверов: {_rows.Count}. Нажмите «Сохранить», чтобы применить изменения."
                : $"Добавлено: {imported.Hosts.Count - duplicates}. Совпадающих IP пропущено: {duplicates}. Нажмите «Сохранить».";
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowError("Не удалось импортировать конфигурацию", ex.Message);
        }
    }

    private static string CanonicalAddress(string? address)
    {
        if (!IPAddress.TryParse(address?.Trim(), out var parsed)) return string.Empty;
        return parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4().ToString() : parsed.ToString();
    }

    private void ExportConfig()
    {
        try
        {
            var config = CaptureValidated();
            using var picker = new SaveFileDialog
            {
                Title = "Экспорт конфигурации pingy", Filter = "Конфигурация pingy (*.json)|*.json",
                DefaultExt = "json", AddExtension = true, FileName = "pingy-hosts.json", OverwritePrompt = true
            };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(picker.FileName, ConfigCodec.Serialize(config), new UTF8Encoding(false));
            _summary.Text = $"Экспортировано серверов: {config.Hosts.Count} → {Path.GetFileName(picker.FileName)}";
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            ShowError("Не удалось экспортировать конфигурацию", ex.Message);
        }
    }

    private void ShowError(string title, string detail) =>
        MessageBox.Show(this, detail, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _source.Dispose();
        base.Dispose(disposing);
    }

    private sealed class ImportChoiceDialog : Form
    {
        public bool ReplaceExisting { get; private set; }

        public ImportChoiceDialog(string fileName, int currentCount, int incomingCount, int enabledCount, int duplicateCount)
        {
            Text = "Как импортировать серверы?";
            BackColor = PageColor;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(620, 375);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            Controls.Add(layout);
            layout.Controls.Add(new Label
            {
                Text = "Импорт серверов", AutoSize = true, Font = new Font("Segoe UI Semibold", 18F),
                ForeColor = TextColor, Margin = Padding.Empty
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = fileName, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = MutedColor,
                Margin = Padding.Empty
            }, 0, 1);
            layout.Controls.Add(new Label
            {
                Text = $"В файле: {incomingCount} серверов, включено: {enabledCount}.\nВ текущем списке: {currentCount}. Совпадающих IP: {duplicateCount}.",
                Dock = DockStyle.Fill, ForeColor = TextColor, Margin = Padding.Empty
            }, 0, 2);
            layout.Controls.Add(new Label
            {
                Text = "Заменить — загрузить список и настройки из файла.\n\nДобавить — сохранить текущие настройки и серверы; добавить новые IP. Существующие IP сохраняют свои имена и группы.",
                Dock = DockStyle.Fill, ForeColor = MutedColor, Margin = Padding.Empty
            }, 0, 3);
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Margin = Padding.Empty
            };
            var cancel = MakeButton("Отмена", 105, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
            cancel.Margin = Padding.Empty;
            cancel.DialogResult = DialogResult.Cancel;
            var add = MakeButton("Добавить", 125, (_, _) =>
            {
                ReplaceExisting = false;
                DialogResult = DialogResult.OK;
                Close();
            }, primary: true);
            var replace = MakeButton("Заменить все", 146, (_, _) =>
            {
                ReplaceExisting = true;
                DialogResult = DialogResult.OK;
                Close();
            });
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(add);
            buttons.Controls.Add(replace);
            layout.Controls.Add(buttons, 0, 4);
            CancelButton = cancel;
            ActiveControl = cancel;
        }
    }
}
