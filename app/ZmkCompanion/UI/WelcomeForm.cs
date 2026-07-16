using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using ZmkCompanion.Core;

namespace ZmkCompanion.UI;

// First-run welcome / help screen. Content is loaded from the embedded
// Resources/welcome-<lang>.md and rendered via MarkdownRenderer (see that
// file for exactly what subset of Markdown is supported).
sealed class WelcomeForm : Form
{
    private readonly CheckBox _chkDontShowAgain;

    // Read after the form closes: true only if the user checked the box.
    // AppContext decides what "don't show again" actually means (it persists
    // the running app's version string) — this form has no AppSettings access.
    public bool DontShowAgain => _chkDontShowAgain.Checked;

    public WelcomeForm()
    {
        Text            = Strings.WelcomeTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MinimizeBox     = false;
        MaximizeBox     = false;
        ClientSize      = new Size(520, 460);
        BackColor       = Color.FromArgb(28, 28, 28);
        ForeColor       = Color.White;

        var rtb = new RichTextBox
        {
            Location      = new Point(12, 12),
            Size          = new Size(496, 380),
            ReadOnly      = true,
            BorderStyle   = BorderStyle.None,
            BackColor     = Color.FromArgb(28, 28, 28),
            ForeColor     = Color.White,
            DetectUrls    = true,
            ScrollBars    = RichTextBoxScrollBars.Vertical,
        };
        rtb.LinkClicked += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = e.LinkText, UseShellExecute = true }); }
            catch { /* best-effort — a bad/unreachable URL isn't worth a MessageBox here */ }
        };
        Controls.Add(rtb);

        string markdown = LoadWelcomeMarkdown();
        MarkdownRenderer.Render(rtb, markdown, SystemFonts.MessageBoxFont!);

        _chkDontShowAgain = new CheckBox
        {
            Text     = Strings.DontShowAgainCheck,
            Location = new Point(12, 400),
            Size     = new Size(200, 24),
            ForeColor = Color.White,
        };
        Controls.Add(_chkDontShowAgain);

        var btnClose = new Button
        {
            Text         = Strings.Close,
            DialogResult = DialogResult.OK,
            Location     = new Point(432, 424),
            Size         = new Size(76, 26),
        };
        Controls.Add(btnClose);
        AcceptButton = btnClose;
        CancelButton = btnClose;
    }

    private static string LoadWelcomeMarkdown()
    {
        string suffix = Strings.Current == AppLanguage.Es ? "welcome-es.md" : "welcome-en.md";
        var asm  = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        // Falls back to a visible placeholder instead of an empty string: a
        // silently blank dialog (what happened when this resource got
        // diverted to an unshipped satellite assembly, see the .csproj
        // comment) is much harder to notice/diagnose than a plainly wrong
        // message would be.
        if (name is null) return $"(missing embedded resource: {suffix})";

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
