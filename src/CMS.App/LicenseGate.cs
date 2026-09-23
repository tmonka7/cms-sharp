using System.Windows.Forms;
using CMS.App.Forms;
using CMS.Licensing;

namespace CMS.App;

/// <summary>
/// The licence check the application runs before it will open.
///
/// Held in one place so there is a single answer to "may this run", and so the
/// result can be consulted later for the per-feature limits. The check is
/// performed once at start-up: re-checking on a timer would add nothing, since
/// anyone able to alter the process could remove the timer as easily as the
/// check.
/// </summary>
public static class LicenseGate
{
    /// <summary>The licence this session is running under, once accepted.</summary>
    public static License? Current { get; private set; }

    public static LicenseStatus? Status { get; private set; }

    public static bool IsLicensed => Status != null && Status.IsValid;

    /// <summary>
    /// Checks the installed licence, prompting for one when it is missing or
    /// unusable. Returns false when the application must not start.
    /// </summary>
    public static bool Ensure()
    {
        var status = Check();

        if (status.IsValid)
        {
            Adopt(status);
            return true;
        }

        // A build with no public key cannot check anything, so there is nothing
        // useful for the operator to do in the activation window.
        if (status.Verdict == LicenseVerdict.NoPublicKey)
        {
            MessageBox.Show(
                status.Message + "\n\n" + status.Advice,
                "Camera Management System",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return false;
        }

        using var form = new LicenseForm(status);

        if (form.ShowDialog() == DialogResult.OK && form.Accepted != null)
        {
            Adopt(form.Accepted);
            return true;
        }

        return false;
    }

    /// <summary>Verifies whatever is installed, without prompting.</summary>
    public static LicenseStatus Check()
    {
        var publicKey = LicensePublicKey.Resolve();

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return LicenseStatus.Fail(
                LicenseVerdict.NoPublicKey,
                "This build has no licensing key installed.");
        }

        var text = LicenseStore.Read();

        if (string.IsNullOrWhiteSpace(text))
        {
            return LicenseStatus.Fail(LicenseVerdict.Missing, "No licence is installed.");
        }

        return LicenseVerifier.VerifyText(text, publicKey);
    }

    private static void Adopt(LicenseStatus status)
    {
        Status = status;
        Current = status.License;
    }

    /// <summary>
    /// Whether a feature is licensed. An unlicensed session grants nothing,
    /// which matters because the gate is the only thing that should be able to
    /// let the application run at all.
    /// </summary>
    public static bool Grants(string feature)
        => IsLicensed && Current != null && Current.Grants(feature);

    /// <summary>The camera ceiling, or zero when unlicensed.</summary>
    public static int MaxCameras => IsLicensed && Current != null ? Current.MaxCameras : 0;

    /// <summary>A short line for the System Information screen.</summary>
    public static string Describe()
    {
        if (Current == null)
        {
            return Status?.Message ?? "Not licensed";
        }

        var text = Current.Customer + "  -  " + Current.Edition + ", " +
                   Current.MaxCameras + " cameras, " +
                   (Current.IsPerpetual ? "perpetual" : "expires " + Current.ExpiryText);

        if (Current.IsNodeLocked)
        {
            text += ", locked to this computer";
        }

        return text;
    }

    /// <summary>Warns when a time-limited licence is close to running out.</summary>
    public static string? ExpiryWarning()
    {
        if (Current == null || Current.IsPerpetual)
        {
            return null;
        }

        var days = Current.DaysRemaining;

        if (days <= 0)
        {
            return "The licence has expired.";
        }

        return days <= 30
            ? "The licence expires in " + days + " day" + (days == 1 ? string.Empty : "s") + "."
            : null;
    }
}
