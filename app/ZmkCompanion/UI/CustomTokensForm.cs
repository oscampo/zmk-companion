using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ZmkCompanion.Core;

namespace ZmkCompanion.UI;

// Modal dialog for declaring {custom.NAME} tokens: name + picker category +
// optional staleness balloon threshold. Declaring one here does NOT set a
// value, it only makes the token show up (grouped under Category) in
// CellGridEditorForm's binding picker before any script has ever run
// `zkc --set NAME value`; the value itself is always runtime-only.
sealed class CustomTokensForm : Form
{
    private static readonly Regex ValidName = new("^[a-z0-9_]+$", RegexOptions.Compiled);

    private readonly List<CustomTokenDef> _tokens;
    private readonly ListBox        _list;
    private readonly TextBox        _txtName;
    private readonly TextBox        _txtCategory;
    private readonly NumericUpDown  _nudStaleSeconds;

    public IReadOnlyList<CustomTokenDef> Tokens => _tokens;

    public CustomTokensForm(IEnumerable<CustomTokenDef> existing)
    {
        _tokens = existing
            .Select(t => new CustomTokenDef { Name = t.Name, Category = t.Category, StaleAfterSeconds = t.StaleAfterSeconds })
            .ToList();

        Text            = "Tokens personalizados";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(360, 380);
        Font            = SystemFonts.MessageBoxFont!;

        var lblHelp = new Label
        {
            Text     = "Declara nombres aquí para que aparezcan en el selector de\n" +
                       "tokens del editor. El valor real solo llega con\n" +
                       "\"zkc --set NOMBRE valor\" desde un script.",
            Left     = 12,
            Top      = 10,
            Width    = 336,
            Height   = 48,
        };
        Controls.Add(lblHelp);

        _list = new ListBox { Left = 12, Top = 62, Width = 336, Height = 140 };
        Controls.Add(_list);
        RefreshList();

        var btnRemove = new Button { Text = "Quitar", Left = 12, Top = 208, Width = 80, Height = 24 };
        btnRemove.Click += (_, _) =>
        {
            if (_list.SelectedIndex < 0) return;
            _tokens.RemoveAt(_list.SelectedIndex);
            RefreshList();
        };
        Controls.Add(btnRemove);

        var lblName = new Label { Text = "Nombre:", Left = 12, Top = 246, Width = 60, AutoSize = false };
        Controls.Add(lblName);
        _txtName = new TextBox { Left = 76, Top = 243, Width = 100 };
        Controls.Add(_txtName);

        var lblCategory = new Label { Text = "Categoría:", Left = 182, Top = 246, Width = 62, AutoSize = false };
        Controls.Add(lblCategory);
        _txtCategory = new TextBox { Left = 246, Top = 243, Width = 102, Text = "Personalizado" };
        Controls.Add(_txtCategory);

        var lblStale = new Label
        {
            Text     = "Avisar si no se actualiza en (segundos, 0 = nunca):",
            Left     = 12,
            Top      = 276,
            Width    = 246,
            AutoSize = false,
        };
        Controls.Add(lblStale);
        _nudStaleSeconds = new NumericUpDown
        {
            Left    = 262,
            Top     = 273,
            Width   = 86,
            Minimum = 0,
            Maximum = 86400, // 24h
            Value   = 0,
        };
        Controls.Add(_nudStaleSeconds);

        var btnAdd = new Button { Text = "+ Agregar", Left = 12, Top = 306, Width = 100, Height = 24 };
        btnAdd.Click += (_, _) => OnAdd();
        Controls.Add(btnAdd);

        var btnOk = new Button
        {
            Text         = "Aceptar",
            DialogResult = DialogResult.OK,
            Left         = ClientSize.Width - 170,
            Top          = ClientSize.Height - 36,
            Width        = 75,
            Height       = 24,
        };
        var btnCancel = new Button
        {
            Text         = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Left         = ClientSize.Width - 88,
            Top          = ClientSize.Height - 36,
            Width        = 75,
            Height       = 24,
        };
        Controls.AddRange([btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnAdd()
    {
        string name     = _txtName.Text.Trim();
        string category = _txtCategory.Text.Trim();
        if (category.Length == 0) category = "Personalizado";
        int staleAfter = (int)_nudStaleSeconds.Value;

        if (!ValidName.IsMatch(name))
        {
            MessageBox.Show(this, "El nombre solo puede usar minúsculas, dígitos y guion bajo (a-z, 0-9, _).",
                "Nombre inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_tokens.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"Ya existe un token llamado \"{name}\".",
                "Nombre duplicado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _tokens.Add(new CustomTokenDef { Name = name, Category = category, StaleAfterSeconds = staleAfter });
        _txtName.Text = "";
        _nudStaleSeconds.Value = 0;
        RefreshList();
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var t in _tokens)
        {
            string stale = t.StaleAfterSeconds > 0 ? $"  stale>{t.StaleAfterSeconds}s" : "";
            _list.Items.Add($"{{custom.{t.Name}}}  [{t.Category}]{stale}");
        }
    }
}
