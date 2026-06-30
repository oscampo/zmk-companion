// Auto-generated — Unicode Nerd Font glyph table for GlyphPickerDialog.
using System.Drawing;
using System.Windows.Forms;
using ZmkCompanion.Core;

namespace ZmkCompanion.UI;

// Grid of common Nerd Font glyphs; closes with SelectedGlyph set on click.
sealed class GlyphPickerDialog : Form
{
    public string? SelectedGlyph { get; private set; }

    private static readonly (string Glyph, string Label)[] Glyphs =
    [
        ("", "bat-full"),
        ("", "bat-3q"),
        ("", "bat-half"),
        ("", "bat-1q"),
        ("", "bat-empty"),
        ("", "bolt"),
        ("", "bluetooth"),
        ("", "usb"),
        ("", "play"),
        ("", "pause"),
        ("", "stop"),
        ("", "prev"),
        ("", "next"),
        ("", "home"),
        ("", "user"),
        ("", "gear"),
        ("", "bell"),
        ("", "star"),
        ("", "star-o"),
        ("", "clock"),
        ("", "cal"),
        ("", "pin"),
        ("", "bulb"),
        ("", "wifi"),
        ("", "phones"),
        ("", "vol"),
        ("", "mute"),
        ("", "chart"),
        ("", "game"),
        ("", "disk"),
        ("", "drop"),
        ("", "sun"),
        ("", "moon"),
        ("", "cloud"),
        ("", "img"),
        ("", "phone"),
        ("", "mail"),
        ("", "refresh"),
        ("", "check"),
        ("", "x"),
        ("", "square"),
        ("", "circle"),
        ("", "circle-o"),
        ("", "arrow-u"),
        ("", "arrow-d"),
        ("", "arrow-r"),
        ("", "arrow-l"),
        ("", "win"),
        ("", "apple"),
        ("", "linux"),
    ];

    public GlyphPickerDialog()
    {
        Text            = "Glyph Picker — click to insert";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;

        const int cols    = 8;
        const int btnSize = 36;
        const int pad     = 4;
        int rows = (Glyphs.Length + cols - 1) / cols;
        ClientSize = new Size(cols * (btnSize + pad) + pad, rows * (btnSize + pad) + pad + 4);

        var tip = new ToolTip { ShowAlways = true };

        for (int i = 0; i < Glyphs.Length; i++)
        {
            var (glyph, lbl) = Glyphs[i];
            int col = i % cols;
            int row = i / cols;
            var btn = new Button
            {
                Text      = glyph,
                Tag       = glyph,
                Location  = new Point(pad + col * (btnSize + pad), pad + row * (btnSize + pad)),
                Size      = new Size(btnSize, btnSize),
                FlatStyle = FlatStyle.Flat,
                Font      = NerdFont.CreateFont(18f),
            };
            tip.SetToolTip(btn, lbl);
            btn.Click += (_, _) => { SelectedGlyph = (string)btn.Tag!; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btn);
        }
    }
}
