namespace CMS.Core.Models;

public enum CameraStatus
{
    Offline = 0,
    Online = 1,
    Connecting = 2,
    Error = 3
}

public enum CameraProtocol
{
    Rtsp = 0,
    Onvif = 1,
    Http = 2
}

public enum CameraKind
{
    IpCamera = 0,
    PtzCamera = 1,
    Nvr = 2,
    UsbCamera = 3
}

public enum UserRole
{
    Viewer = 0,
    Operator = 1,
    Administrator = 2
}

public enum EventKind
{
    FaceRecognized = 0,
    ObjectDetected = 1,
    CameraOffline = 2,
    CameraOnline = 3,
    MotionDetected = 4,
    System = 5,
    Recording = 6
}

public enum EventSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum PtzMove
{
    Up,
    Down,
    Left,
    Right,
    UpLeft,
    UpRight,
    DownLeft,
    DownRight,
    Stop
}

public enum FaceGroup
{
    Employee = 0,
    Visitor = 1,
    Blacklist = 2,
    Unknown = 3
}
