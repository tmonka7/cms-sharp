namespace CMS.Core.Models;

/// <summary>One row of the persistent event log.</summary>
public sealed class EventEntry
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public DateTime Timestamp => TimestampUtc.ToLocalTime();

    public EventKind Kind { get; set; }

    public EventSeverity Severity { get; set; } = EventSeverity.Info;

    public int CameraId { get; set; }

    public string CameraName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Confidence or similarity attached to the event, when relevant.</summary>
    public double? Score { get; set; }

    public byte[]? Snapshot { get; set; }

    public string TypeText => Kind switch
    {
        EventKind.FaceRecognized => "Face Recognized",
        EventKind.ObjectDetected => "Object Detected",
        EventKind.CameraOffline => "Camera Offline",
        EventKind.CameraOnline => "Camera Online",
        EventKind.MotionDetected => "Motion Detected",
        EventKind.Recording => "Recording",
        _ => "System"
    };

    public string TimeText => Timestamp.ToString("HH:mm:ss");

    public string DateTimeText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Score rendered as a percentage, or blank when there is none.</summary>
    public string ScoreText => Score.HasValue ? (Score.Value * 100.0).ToString("0.0") + "%" : string.Empty;
}
