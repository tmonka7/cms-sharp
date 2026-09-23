using System;
using System.Windows.Forms;

namespace CMS.KeyGen;

internal static class Program
{
    public static string Version => "1.0.0";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (s, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception);

        Application.Run(new KeyGenForm());
    }

    private static void Report(Exception? exception)
    {
        if (exception == null)
        {
            return;
        }

        MessageBox.Show(
            exception.Message,
            "Licence Generator",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
