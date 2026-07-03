using System.Drawing;
using System.Windows.Forms;

namespace ZmkCompanion.UI;

// Modal dialog for configuring Pomodoro timer durations.
// Exposes the four values via properties after ShowDialog returns OK.
sealed class PomodoroConfigDialog : Form
{
    private readonly NumericUpDown _nudWork;
    private readonly NumericUpDown _nudBreak;
    private readonly NumericUpDown _nudCycles;
    private readonly NumericUpDown _nudLong;

    public int WorkMin      => (int)_nudWork.Value;
    public int BreakMin     => (int)_nudBreak.Value;
    public int Cycles       => (int)_nudCycles.Value;
    public int LongBreakMin => (int)_nudLong.Value;

    public PomodoroConfigDialog(int workMin, int breakMin, int cycles, int longBreakMin)
    {
        Text            = "Configurar Pomodoro";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(280, 210);
        Font            = SystemFonts.MessageBoxFont!;

        // ── Field rows ────────────────────────────────────────────────────────
        int y = 16;
        (_nudWork,   y) = AddRow("Trabajo (min):",     workMin,      1, 120, y);
        (_nudBreak,  y) = AddRow("Pausa corta (min):", breakMin,     1,  60, y);
        (_nudCycles, y) = AddRow("Ciclos:",             cycles,       1,  20, y);
        (_nudLong,   y) = AddRow("Pausa larga (min):", longBreakMin, 0,  90, y);

        // ── Preset buttons ────────────────────────────────────────────────────
        var lblPresets = new Label { Text = "Presets:", Left = 12, Top = y + 4, AutoSize = true };
        Controls.Add(lblPresets);

        AddPreset("Clásico",  25, 5, 4, 15, left: 70,  top: y);
        AddPreset("Corto",    15, 3, 4, 10, left: 140, top: y);
        AddPreset("Largo",    50, 10, 3, 20, left: 200, top: y);
        y += 30;

        // ── OK / Cancelar ─────────────────────────────────────────────────────
        var btnOk = new Button
        {
            Text         = "Aceptar",
            DialogResult = DialogResult.OK,
            Left         = ClientSize.Width - 170,
            Top          = ClientSize.Height - 36,
            Width        = 75,
        };
        var btnCancel = new Button
        {
            Text         = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Left         = ClientSize.Width - 88,
            Top          = ClientSize.Height - 36,
            Width        = 75,
        };
        Controls.AddRange([btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    // Creates a label + NumericUpDown row, returns the control and next y.
    private (NumericUpDown nud, int nextY) AddRow(string label, int value, int min, int max, int y)
    {
        var lbl = new Label { Text = label, Left = 12, Top = y + 4, Width = 130, AutoSize = false };
        var nud = new NumericUpDown
        {
            Left    = 148,
            Top     = y,
            Width   = 60,
            Minimum = min,
            Maximum = max,
            Value   = Math.Clamp(value, min, max),
        };
        Controls.AddRange([lbl, nud]);
        return (nud, y + 28);
    }

    private void AddPreset(string name, int work, int brk, int cycles, int lng, int left, int top)
    {
        var btn = new Button { Text = name, Left = left, Top = top, Width = 56, Height = 22 };
        btn.Click += (_, _) =>
        {
            _nudWork.Value   = work;
            _nudBreak.Value  = brk;
            _nudCycles.Value = cycles;
            _nudLong.Value   = lng;
        };
        Controls.Add(btn);
    }
}
