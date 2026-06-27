using System.Runtime.InteropServices;
using System.Windows.Forms;
using ZmkCompanion.Cli;

namespace ZmkCompanion;

static class Program
{
    // Attach to the parent shell console so stderr is visible in cmd/PowerShell.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    static int Main(string[] args)
    {
        // CLI mode: any argument → delegate to CliRunner, skip tray startup entirely.
        if (args.Length > 0)
        {
            AttachConsole(-1);
            return CliRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        // Tray app mode: enforce single instance.
        using var mutex = new Mutex(true, "ZmkCompanion_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "ZMK Companion is already running in the system tray.",
                "ZMK Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new ZmkAppContext());
        return 0;
    }
}
