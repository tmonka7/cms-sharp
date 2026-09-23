namespace CMS.Core.Models;

/// <summary>A local operator account. Authentication never leaves the machine.</summary>
public sealed class AppUser
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Viewer;

    public bool Enabled { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginUtc { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>Permission keys granted to this account.</summary>
    public HashSet<string> Permissions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string RoleText => Role switch
    {
        UserRole.Administrator => "Administrator",
        UserRole.Operator => "Operator",
        _ => "Viewer"
    };

    public string StatusText => Enabled ? "Active" : "Inactive";

    public AppUser Clone()
    {
        var copy = (AppUser)MemberwiseClone();
        copy.Permissions = new HashSet<string>(Permissions, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}

/// <summary>The permission keys shown on the User Management screen.</summary>
public static class Permissions
{
    public const string LiveView = "live_view";
    public const string Playback = "playback";
    public const string DeviceManagement = "device_management";
    public const string FaceRecognition = "face_recognition";
    public const string ObjectDetection = "object_detection";
    public const string SystemSettings = "system_settings";
    public const string UserManagement = "user_management";
    public const string PtzControl = "ptz_control";

    public const string Attendance = "attendance";

    /// <summary>Key plus display label, in the order the checklist shows them.</summary>
    public static readonly KeyValuePair<string, string>[] All =
    {
        new KeyValuePair<string, string>(LiveView, "Live View"),
        new KeyValuePair<string, string>(Playback, "Playback"),
        new KeyValuePair<string, string>(DeviceManagement, "Device Management"),
        new KeyValuePair<string, string>(FaceRecognition, "Face Recognition"),
        new KeyValuePair<string, string>(ObjectDetection, "Object Detection"),
        new KeyValuePair<string, string>(PtzControl, "PTZ Control"),
        new KeyValuePair<string, string>(Attendance, "Attendance"),
        new KeyValuePair<string, string>(SystemSettings, "System Settings"),
        new KeyValuePair<string, string>(UserManagement, "User Management")
    };

    public static IEnumerable<string> ForRole(UserRole role)
    {
        switch (role)
        {
            case UserRole.Administrator:
                return All.Select(p => p.Key);
            case UserRole.Operator:
                return new[] { LiveView, Playback, FaceRecognition, ObjectDetection, PtzControl, Attendance };
            default:
                return new[] { LiveView, Playback };
        }
    }
}
