using System.Windows.Forms;

namespace ZmkCompanion;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "ZmkCompanion_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "ZMK Companion is already running in the system tray.",
                "ZMK Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new ZmkAppContext());
    }
}
