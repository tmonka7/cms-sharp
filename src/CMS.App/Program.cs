using System.Windows.Forms;
using CMS.App.Forms;
using CMS.Core.Models;
using CMS.Core.Services;

namespace CMS.App;

internal static class Program
{
    /// <summary>The service container shared by every form in the process.</summary>
    public static AppServices Services { get; private set; } = null!;

    public static string Version => "1.0.0";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // A background analytics fault should surface as a message, not kill a
        // monitoring station that is meant to run unattended.
        Application.ThreadException += (s, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportFatal(e.ExceptionObject as Exception);

        try
        {
            Services = new AppServices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The local database could not be opened:\n\n" + ex.Message,
                "Camera Management System",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            using (var splash = new SplashForm())
            {
                splash.ShowDialog();
            }

            using (var login = new LoginForm())
            {
                if (login.ShowDialog() != DialogResult.OK || Services.Auth.CurrentUser == null)
                {
                    return;
                }
            }

            Services.LogEvent(
                EventKind.System,
                "User " + Services.Auth.CurrentUser!.Username + " signed in.");

            Application.Run(new MainForm());
        }
        finally
        {
            Services.Dispose();
        }
    }

    private static void ReportFatal(Exception? exception)
    {
        if (exception == null)
        {
            return;
        }

        try
        {
            Services?.LogEvent(EventKind.System, "Error: " + exception.Message, EventSeverity.Critical);
        }
        catch (Exception)
        {
            // Nothing useful to do if even logging fails.
        }

        MessageBox.Show(
            exception.Message,
            "Camera Management System",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
