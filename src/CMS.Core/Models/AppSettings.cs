namespace CMS.Core.Models;

/// <summary>
/// Everything on the Settings screen. Stored as key/value rows in SQLite so the
/// product needs no configuration server.
/// </summary>
public sealed class AppSettings
{
    // ---- General ----
    public string SystemName { get; set; } = "Camera Management System";

    public string Language { get; set; } = "English";

    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    public string DateFormat { get; set; } = "yyyy-MM-dd";

    public int AutoLogoutMinutes { get; set; } = 30;

    public bool StartWithSystem { get; set; } = true;

    // ---- Network ----
    public int OnvifDiscoveryTimeoutSeconds { get; set; } = 4;

    public int RtspConnectTimeoutSeconds { get; set; } = 10;

    public bool PreferTcpTransport { get; set; } = true;

    // ---- Storage ----
    public string StorageRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CameraManagementSystem",
        "Media");

    public int RetentionDays { get; set; } = 30;

    public int MaxStorageGb { get; set; } = 512;

    public bool OverwriteWhenFull { get; set; } = true;

    // ---- Object detection ----
    public string ObjectModelName { get; set; } = "YOLOv26n (Fast & Lightweight)";

    public string ObjectModelPath { get; set; } = @"Models\yolov26n.onnx";

    public float ObjectConfidenceThreshold { get; set; } = 0.50f;

    public float ObjectNmsThreshold { get; set; } = 0.45f;

    public bool ObjectDetectionEnabled { get; set; } = true;

    /// <summary>How often a frame is handed to the detector, in milliseconds.</summary>
    public int DetectionIntervalMs { get; set; } = 200;

    public HashSet<string> EnabledClasses { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "person", "car", "bicycle", "motorcycle", "bus", "truck", "dog", "cat"
    };

    // ---- Face recognition ----
    public string FaceDetectorPath { get; set; } = @"Models\face_detection.onnx";

    public string FaceEmbedderPath { get; set; } = @"Models\face_recognition.onnx";

    /// <summary>
    /// On the 0..1 scale the UI shows, which is cosine similarity remapped as
    /// (cosine + 1) / 2. This default is the 0.363 cosine that OpenCV publishes
    /// for SFace; 0.60 here would be a cosine of 0.20, low enough to match
    /// strangers.
    /// </summary>
    public float FaceMatchThreshold { get; set; } = 0.68f;

    public bool FaceRecognitionEnabled { get; set; } = true;

    public bool UseGpu { get; set; }

    /// <summary>
    /// Align the crop to the detector landmarks before embedding. Turning this
    /// off reverts to the older plain crop; the two produce embeddings that must
    /// not be compared with each other, which is why every record carries the
    /// version it was made with.
    /// </summary>
    public bool AlignFaces { get; set; } = true;

    // ---- Attendance and MongoDB ----

    /// <summary>
    /// The gallery and attendance live in MongoDB when this is on. Everything
    /// else stays in the local SQLite file, so the product still starts and
    /// shows video when the server is unreachable.
    /// </summary>
    public bool AttendanceEnabled { get; set; }

    public string MongoConnectionString { get; set; } = "mongodb://localhost:27017";

    public string MongoDatabase { get; set; } = "cms";

    /// <summary>Arc the automatic sweep covers, centred on the current view.</summary>
    public float SweepArcDegrees { get; set; } = 180f;

    /// <summary>
    /// Horizontal field of view assumed at the sweep zoom. ONVIF does not report
    /// this, and setting it too wide leaves unswept gaps between stops.
    /// </summary>
    public float SweepFieldOfViewDegrees { get; set; } = 30f;

    public float SweepOverlapFraction { get; set; } = 0.25f;

    public float SweepZoom { get; set; }

    /// <summary>Time allowed for the camera to stop moving before a frame is taken.</summary>
    public int SweepSettleMs { get; set; } = 900;

    public int SweepFramesPerStop { get; set; } = 4;

    /// <summary>Raw cosine above which two captures in one sweep are the same person.</summary>
    public float SweepClusterCosine { get; set; } = 0.50f;

    /// <summary>Register people the sweep finds who match nobody already enrolled.</summary>
    public bool SweepEnrolUnknown { get; set; } = true;

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.EnabledClasses = new HashSet<string>(EnabledClasses, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}
