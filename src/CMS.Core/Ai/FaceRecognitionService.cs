using System.Diagnostics;
using CMS.Core.Data;
using CMS.Core.Models;
using OpenCvSharp;

namespace CMS.Core.Ai;

/// <summary>
/// Detect, embed, match. Holds the enrolled gallery in memory so matching a
/// frame never touches the disk, and enrols new identities from a still image.
/// </summary>
public sealed class FaceRecognitionService : IDisposable
{
    private readonly FaceDetector _detector = new FaceDetector();
    private readonly FaceEmbedder _embedder = new FaceEmbedder();
    private readonly IFaceStore _repository;
    private readonly ReaderWriterLockSlim _galleryLock = new ReaderWriterLockSlim();
    private List<FaceRecord> _gallery = new List<FaceRecord>();
    private int _incompatibleRecords;
    private bool _disposed;

    public FaceRecognitionService(IFaceStore repository)
    {
        _repository = repository;
        ReloadGallery();
    }

    /// <summary>
    /// Warp the crop onto the canonical template before embedding. Changing this
    /// changes every embedding the service produces, so records are stamped with
    /// the setting they were made under and only like is compared with like.
    /// </summary>
    public bool AlignFaces { get; set; } = true;

    /// <summary>
    /// Enrolments in the store that were made under a different alignment
    /// setting and are therefore excluded from matching. A non-zero count means
    /// those people will not be recognised until they are enrolled again.
    /// </summary>
    public int IncompatibleRecords => _incompatibleRecords;

    public string EmbeddingVersion
        => AlignFaces ? FaceEmbeddingVersions.Aligned : FaceEmbeddingVersions.Legacy;

    public IFaceStore Store => _repository;

    public bool IsReady => _detector.IsReady && _embedder.IsReady;

    public bool DetectorReady => _detector.IsReady;

    public bool EmbedderReady => _embedder.IsReady;

    public string? LastError => _detector.LastError ?? _embedder.LastError;

    public string? DetectorError => _detector.LastError;

    public string? EmbedderError => _embedder.LastError;

    /// <summary>Which head layout the detector recognised, e.g. "YuNet".</summary>
    public string DetectorLayout => _detector.LayoutName;

    /// <summary>How the detector read its output tensor on the last inference.</summary>
    public string DetectorOutput => _detector.OutputSignature;

    /// <summary>Embedding width and the pixel convention chosen for the model.</summary>
    public string EmbedderSignature
        => _embedder.EmbeddingSize + " floats, " + _embedder.InputConvention;

    public string DetectorModelPath => _detector.ModelPath;

    public string EmbedderModelPath => _embedder.ModelPath;

    /// <summary>
    /// Similarity a candidate must reach to count as a match, on the remapped
    /// 0..1 scale. See <see cref="Models.AppSettings.FaceMatchThreshold"/>.
    /// </summary>
    public float MatchThreshold { get; set; } = 0.68f;

    public float DetectionThreshold
    {
        get => _detector.ConfidenceThreshold;
        set => _detector.ConfidenceThreshold = value;
    }

    public int GalleryCount
    {
        get
        {
            _galleryLock.EnterReadLock();
            try
            {
                return _gallery.Count;
            }
            finally
            {
                _galleryLock.ExitReadLock();
            }
        }
    }

    public bool LoadModels(string detectorPath, string embedderPath, bool useGpu = false)
    {
        var detectorLoaded = _detector.Load(detectorPath, useGpu);
        var embedderLoaded = _embedder.Load(embedderPath, useGpu);
        return detectorLoaded && embedderLoaded;
    }

    /// <summary>Re-reads the enrolled faces from SQLite into the match cache.</summary>
    public void ReloadGallery()
    {
        var all = _repository.GetAll()
            .Where(r => r.Enabled && r.Embedding.Length > 0)
            .ToList();

        var version = EmbeddingVersion;

        // Embeddings made from an aligned crop and from a plain one do not
        // compare meaningfully. Including both would not raise an error, it
        // would just quietly fail to recognise half the gallery, so the
        // mismatched records are excluded and counted instead.
        var records = all
            .Where(r => FaceEmbeddingVersions.Comparable(r.EmbeddingVersion, version))
            .ToList();

        _galleryLock.EnterWriteLock();
        try
        {
            _gallery = records;
            _incompatibleRecords = all.Count - records.Count;
        }
        finally
        {
            _galleryLock.ExitWriteLock();
        }
    }

    /// <summary>Faces in a frame with their landmarks, for callers that align themselves.</summary>
    public List<FaceBox> DetectFaces(Mat frame) => _detector.DetectFaces(frame);

    /// <summary>Embeds a crop that has already been aligned to the template.</summary>
    public float[] EmbedAligned(Mat alignedCrop) => _embedder.Embed(alignedCrop);

    /// <summary>
    /// Prepares a detected face for embedding: an aligned warp when alignment is
    /// on and the detector supplied landmarks, otherwise the plain margin crop.
    /// </summary>
    public Mat? PrepareCrop(Mat frame, FaceBox face)
    {
        if (AlignFaces && face.HasLandmarks)
        {
            var aligned = FaceAligner.Align(frame, face.Landmarks);
            if (aligned != null && !aligned.Empty())
            {
                return aligned;
            }

            aligned?.Dispose();
        }

        return CropFace(frame, face.Box);
    }

    /// <summary>Finds and identifies every face in one frame.</summary>
    public FaceFrameResult Process(Mat frame, int cameraId = 0)
    {
        if (frame == null || frame.Empty() || !IsReady)
        {
            return new FaceFrameResult { CameraId = cameraId };
        }

        var stopwatch = Stopwatch.StartNew();
        var faces = _detector.DetectFaces(frame);
        var matches = new List<FaceMatch>(faces.Count);

        foreach (var face in faces)
        {
            // Live matching prepares the crop exactly as enrolment does, or the
            // probe and the gallery would sit in different spaces.
            using var crop = PrepareCrop(frame, face);
            if (crop == null || crop.Empty())
            {
                continue;
            }

            var embedding = _embedder.Embed(crop);
            if (embedding.Length == 0)
            {
                matches.Add(new FaceMatch { Box = face.Box });
                continue;
            }

            var best = FindBestMatch(embedding);
            matches.Add(new FaceMatch
            {
                Box = face.Box,
                Record = best.Similarity >= MatchThreshold ? best.Record : null,
                Similarity = best.Similarity
            });
        }

        stopwatch.Stop();

        return new FaceFrameResult
        {
            CameraId = cameraId,
            Matches = matches,
            InferenceMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    /// <summary>
    /// Enrols a person from a photo. Returns null when no face is found or the
    /// models are not loaded.
    /// </summary>
    public FaceRecord? Enroll(Mat image, string name, FaceGroup group, string note = "")
    {
        if (!IsReady || image == null || image.Empty())
        {
            return null;
        }

        var faces = _detector.DetectFaces(image);
        if (faces.Count == 0)
        {
            return null;
        }

        // The largest face is the subject of an enrolment photo.
        var face = faces.OrderByDescending(f => f.Box.Area).First();

        using var crop = PrepareCrop(image, face);
        if (crop == null || crop.Empty())
        {
            return null;
        }

        var embedding = _embedder.Embed(crop);
        if (embedding.Length == 0)
        {
            return null;
        }

        var record = new FaceRecord
        {
            Name = name,
            Group = group,
            Note = note,
            Embedding = embedding,
            Thumbnail = EncodeThumbnail(crop),
            RegisteredUtc = DateTime.UtcNow,
            Enabled = true,
            EmbeddingVersion = EmbeddingVersion,
            CaptureCount = 1
        };

        record.DocumentId = _repository.Insert(record);
        ReloadGallery();
        return record;
    }

    /// <summary>Best gallery hit for an embedding, with its similarity.</summary>
    public FaceCandidate FindBestMatch(float[] embedding)
    {
        _galleryLock.EnterReadLock();
        try
        {
            FaceRecord? best = null;
            var bestSimilarity = 0f;

            foreach (var candidate in _gallery)
            {
                var similarity = FaceEmbedder.Similarity(embedding, candidate.Embedding);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    best = candidate;
                }
            }

            return new FaceCandidate(best, bestSimilarity);
        }
        finally
        {
            _galleryLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Crops a face with a margin around the detector box, which matches how
    /// recognition models are trained and measurably improves matching.
    /// </summary>
    public static Mat? CropFace(Mat frame, Detection box, float margin = 0.2f)
    {
        var padX = box.Width * margin;
        var padY = box.Height * margin;

        var x = (int)Math.Max(0, box.X - (padX / 2f));
        var y = (int)Math.Max(0, box.Y - (padY / 2f));
        var width = (int)Math.Min(frame.Width - x, box.Width + padX);
        var height = (int)Math.Min(frame.Height - y, box.Height + padY);

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        using var roi = new Mat(frame, new Rect(x, y, width, height));
        return roi.Clone();
    }

    public static byte[] EncodeThumbnail(Mat image, int size = 160)
    {
        using var resized = new Mat();
        Cv2.Resize(image, resized, new OpenCvSharp.Size(size, size));
        Cv2.ImEncode(".jpg", resized, out var buffer, new[] { (int)ImwriteFlags.JpegQuality, 90 });
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detector.Dispose();
        _embedder.Dispose();
        _galleryLock.Dispose();
    }
}

/// <summary>The closest gallery entry to a probe embedding.</summary>
public readonly struct FaceCandidate
{
    public FaceCandidate(FaceRecord? record, float similarity)
    {
        Record = record;
        Similarity = similarity;
    }

    public FaceRecord? Record { get; }

    public float Similarity { get; }
}
