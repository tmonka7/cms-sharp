namespace CMS.Core.Models;

/// <summary>A recorded clip on local disk, used by the Playback timeline.</summary>
public sealed class RecordingSegment
{
    public long Id { get; set; }

    public int CameraId { get; set; }

    public string CameraName { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public bool HasEvents { get; set; }

    public DateTime Start => StartUtc.ToLocalTime();

    public DateTime End => EndUtc.ToLocalTime();

    public TimeSpan Duration => EndUtc - StartUtc;

    public string RangeText => Start.ToString("HH:mm:ss") + " - " + End.ToString("HH:mm:ss");
}

/// <summary>A searchable marker on the playback timeline.</summary>
public sealed class PlaybackMarker
{
    public DateTime TimestampUtc { get; set; }

    public EventKind Kind { get; set; }

    public string Label { get; set; } = string.Empty;

    public string CameraName { get; set; } = string.Empty;

    public double Score { get; set; }

    public byte[]? Thumbnail { get; set; }

    public DateTime Timestamp => TimestampUtc.ToLocalTime();

    public string TimeText => Timestamp.ToString("HH:mm:ss");
}
