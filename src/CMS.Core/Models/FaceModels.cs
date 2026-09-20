namespace CMS.Core.Models;

/// <summary>An enrolled identity plus its embedding vector.</summary>
public sealed class FaceRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public FaceGroup Group { get; set; } = FaceGroup.Visitor;

    public string Note { get; set; } = string.Empty;

    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;

    /// <summary>JPEG bytes of the enrolment thumbnail.</summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>L2-normalised embedding produced by the recognition model.</summary>
    public float[] Embedding { get; set; } = Array.Empty<float>();

    public bool Enabled { get; set; } = true;

    public string GroupText => Group switch
    {
        FaceGroup.Employee => "Employee",
        FaceGroup.Blacklist => "Blacklist",
        FaceGroup.Unknown => "Unknown",
        _ => "Visitor"
    };

    public string RegisteredText => RegisteredUtc.ToLocalTime().ToString("yyyy-MM-dd");
}

/// <summary>A face found in a frame, optionally matched against the database.</summary>
public sealed class FaceMatch
{
    public Detection Box { get; set; }

    public FaceRecord? Record { get; set; }

    /// <summary>Cosine similarity mapped onto 0..1.</summary>
    public float Similarity { get; set; }

    public bool IsRecognized => Record != null;

    public string DisplayName => Record?.Name ?? "Unknown";

    public string SimilarityText => (Similarity * 100f).ToString("0.0") + "%";
}

/// <summary>Recognition output for one frame.</summary>
public sealed class FaceFrameResult
{
    public int CameraId { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public IList<FaceMatch> Matches { get; set; } = new List<FaceMatch>();

    public double InferenceMs { get; set; }
}
