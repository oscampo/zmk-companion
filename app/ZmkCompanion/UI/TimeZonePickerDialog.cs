using System.Drawing;
using System.Windows.Forms;
using ZmkCompanion.Core;
using ZmkCompanion.Features;

namespace ZmkCompanion.UI;

// Chip-style multi-timezone picker. Unlike LeaguePickerDialog there is no
// network fetch (IANA's zone database is static and bundled with .NET), so
// this is just a search box over TimeZoneCatalog.Common plus a free-text row
// for typing any raw IANA id not in the curated shortlist.
sealed class TimeZonePickerDialog : Form
{
    private readonly TextBox         _searchBox;
    private readonly ListBox         _listBox;
    private readonly TextBox         _txtCustomId;
    private readonly FlowLayoutPanel _chipsPanel;

    private List<TimeZoneEntry> _filtered = [];
    private readonly List<TimeZoneEntry> _selected;

    public IReadOnlyList<string> SelectedIds => _selected.Select(z => z.Id).ToList();

    public TimeZonePickerDialog(List<string> currentIds)
    {
        Text            = Strings.TimeZonePickerTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(400, 400);

        int y = 12;
        Controls.Add(MakeLabel(Strings.SearchTimeZonesLabel, y));
        _searchBox = new TextBox
        {
            Location        = new Point(12, y + 20),
            Size            = new Size(376, 23),
            PlaceholderText = Strings.SearchTimeZonesPlaceholder,
        };
        Controls.Add(_searchBox);

        y += 54;
        Controls.Add(MakeLabel(Strings.AvailableTimeZonesLabel, y));
        _listBox = new ListBox
        {
            Location      = new Point(12, y + 20),
            Size          = new Size(376, 120),
            SelectionMode = SelectionMode.One,
        };
        Controls.Add(_listBox);

        y += 140;
        Controls.Add(MakeLabel(Strings.CustomTimeZoneIdLabel, y));
        _txtCustomId = new TextBox
        {
            Location        = new Point(12, y + 20),
            Size            = new Size(280, 23),
            PlaceholderText = "Region/City",
        };
        Controls.Add(_txtCustomId);
        var btnAddCustom = new Button { Text = Strings.Add, Location = new Point(298, y + 19), Size = new Size(90, 24) };
        btnAddCustom.Click += (_, _) => AddCustomId();
        Controls.Add(btnAddCustom);

        y += 54;
        Controls.Add(MakeLabel(Strings.SelectedTimeZonesLabel, y));
        _chipsPanel = new FlowLayoutPanel
        {
            Location     = new Point(12, y + 20),
            Size         = new Size(376, 72),
            AutoScroll   = true,
            BorderStyle  = BorderStyle.FixedSingle,
            WrapContents = true,
        };
        Controls.Add(_chipsPanel);

        y += 100;
        var btnOk = new Button
        {
            Text         = Strings.Ok,
            DialogResult = DialogResult.OK,
            Location     = new Point(208, y),
            Size         = new Size(80, 26),
        };
        var btnCancel = new Button
        {
            Text         = Strings.Cancel,
            DialogResult = DialogResult.Cancel,
            Location     = new Point(296, y),
            Size         = new Size(92, 26),
        };
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.AddRange([btnOk, btnCancel]);

        _selected = currentIds.Select(TimeZoneCatalog.FindOrCreate).ToList();
        RebuildChips();

        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _listBox.DoubleClick   += (_, _) => AddFocused();
        _listBox.KeyDown       += (_, e) => { if (e.KeyCode == Keys.Enter) AddFocused(); };
        _txtCustomId.KeyDown   += (_, e) => { if (e.KeyCode == Keys.Enter) { AddCustomId(); e.SuppressKeyPress = true; } };

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string q = _searchBox.Text.Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? TimeZoneCatalog.Common.ToList()
            : TimeZoneCatalog.Common.Where(z =>
                z.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                z.ShortName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                z.Id.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        _listBox.BeginUpdate();
        _listBox.Items.Clear();
        foreach (var z in _filtered)
            _listBox.Items.Add($"{z.DisplayName} ({z.ShortName})");
        _listBox.EndUpdate();
    }

    private void AddFocused()
    {
        if (_listBox.SelectedIndex < 0 || _listBox.SelectedIndex >= _filtered.Count) return;
        AddEntry(_filtered[_listBox.SelectedIndex]);
    }

    private void AddCustomId()
    {
        string id = _txtCustomId.Text.Trim();
        if (id.Length == 0) return;
        if (!TimeZoneCatalog.IsValid(id))
        {
            MessageBox.Show(this, Strings.InvalidTimeZoneBody(id),
                Strings.InvalidTimeZoneTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        AddEntry(TimeZoneCatalog.FindOrCreate(id));
        _txtCustomId.Text = "";
    }

    private void AddEntry(TimeZoneEntry entry)
    {
        if (_selected.Any(s => s.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))) return;
        _selected.Add(entry);
        RebuildChips();
    }

    private void RebuildChips()
    {
        _chipsPanel.Controls.Clear();
        foreach (var z in _selected.ToList())
        {
            var chip = new Button
            {
                Text      = $"{z.ShortName}  ×",
                AutoSize  = true,
                FlatStyle = FlatStyle.Flat,
                Margin    = new Padding(2),
                Tag       = z,
            };
            chip.FlatAppearance.BorderSize = 1;
            chip.Click += (_, _) =>
            {
                _selected.RemoveAll(s => s.Id == ((TimeZoneEntry)chip.Tag!).Id);
                RebuildChips();
            };
            _chipsPanel.Controls.Add(chip);
        }
    }

    private static Label MakeLabel(string text, int y) =>
        new()
        {
            Text     = text,
            Location = new Point(12, y),
            Size     = new Size(376, 18),
            AutoSize = false,
        };
}
