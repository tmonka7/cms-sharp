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

    /// <summary>Identifier when this record lives in MongoDB rather than SQLite.</summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// True when the record was created automatically from a sweep and nobody
    /// has named it yet. A provisional identity still matches, so the same
    /// person is recognised on later sweeps, but it is not a claim about who
    /// they are.
    /// </summary>
    public bool Provisional { get; set; }

    /// <summary>
    /// How the crop was prepared before embedding. Embeddings made from an
    /// aligned crop do not compare meaningfully against ones made from a plain
    /// crop, so records carrying different versions must not be matched.
    /// </summary>
    public string EmbeddingVersion { get; set; } = FaceEmbeddingVersions.Legacy;

    /// <summary>How many accepted captures have been merged into this identity.</summary>
    public int CaptureCount { get; set; }

    public DateTime? LastSeenUtc { get; set; }

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

/// <summary>
/// Identifies how a face crop was prepared before embedding.
///
/// This exists because enabling landmark alignment changes every embedding the
/// model produces. Measured on the reference faces, an aligned and an unaligned
/// embedding of the same person score around 0.35 raw cosine - well below the
/// match threshold. A gallery holding both kinds would silently fail to
/// recognise its own enrolments, so the two are never compared.
/// </summary>
public static class FaceEmbeddingVersions
{
    /// <summary>Box crop with a margin, no landmark alignment.</summary>
    public const string Legacy = "crop-v1";

    /// <summary>Five-point similarity warp onto the canonical template.</summary>
    public const string Aligned = "aligned-v1";

    public static string Current => Aligned;

    public static bool Comparable(string a, string b)
        => string.Equals(
            string.IsNullOrEmpty(a) ? Legacy : a,
            string.IsNullOrEmpty(b) ? Legacy : b,
            StringComparison.OrdinalIgnoreCase);
}
