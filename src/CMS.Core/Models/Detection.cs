namespace CMS.Core.Models;

/// <summary>A single object-detection box in source-frame pixel coordinates.</summary>
public struct Detection
{
    public Detection(int classId, string label, float confidence, float x, float y, float width, float height)
    {
        ClassId = classId;
        Label = label;
        Confidence = confidence;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int ClassId { get; }

    public string Label { get; }

    public float Confidence { get; }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public float Right => X + Width;

    public float Bottom => Y + Height;

    public float CenterX => X + (Width / 2f);

    public float CenterY => Y + (Height / 2f);

    public float Area => Width * Height;

    /// <summary>Label plus score, drawn above the box, e.g. "person 0.92".</summary>
    public string Caption => Label + " " + Confidence.ToString("0.00");
}

/// <summary>Detections produced for one decoded frame.</summary>
public sealed class DetectionFrameResult
{
    public int CameraId { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public IList<Detection> Detections { get; set; } = new List<Detection>();

    public double InferenceMs { get; set; }

    public int FrameWidth { get; set; }

    public int FrameHeight { get; set; }

    /// <summary>Per-label counts backing the detection count panel.</summary>
    public Dictionary<string, int> CountByLabel()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var detection in Detections)
        {
            counts.TryGetValue(detection.Label, out var current);
            counts[detection.Label] = current + 1;
        }

        return counts;
    }
}
