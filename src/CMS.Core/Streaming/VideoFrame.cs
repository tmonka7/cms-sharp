using OpenCvSharp;

namespace CMS.Core.Streaming;

/// <summary>
/// One decoded frame handed to a consumer. The <see cref="Mat"/> is owned by the
/// decoder and is only valid inside the event handler, so a consumer that needs
/// to keep it must clone it.
/// </summary>
public sealed class VideoFrame
{
    public VideoFrame(int cameraId, Mat image, long sequence, DateTime timestampUtc)
    {
        CameraId = cameraId;
        Image = image;
        Sequence = sequence;
        TimestampUtc = timestampUtc;
    }

    public int CameraId { get; }

    public Mat Image { get; }

    public long Sequence { get; }

    public DateTime TimestampUtc { get; }

    public int Width => Image.Width;

    public int Height => Image.Height;
}
