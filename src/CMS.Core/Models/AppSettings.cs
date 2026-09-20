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

    public float FaceMatchThreshold { get; set; } = 0.60f;

    public bool FaceRecognitionEnabled { get; set; } = true;

    public bool UseGpu { get; set; }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.EnabledClasses = new HashSet<string>(EnabledClasses, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}
