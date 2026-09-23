using OpenCvSharp;

namespace CMS.Core.Ai;

/// <summary>
/// Warps a face onto the canonical template the recognition models were trained
/// with, using the five landmarks the detector already produces.
///
/// Without this a crop is only ever centred and scaled, so a head turned or
/// tilted away from the camera lands in a different part of the embedding space
/// than the same person facing it. That costs accuracy on a deliberate
/// enrolment photograph and is decisive for faces captured across a PTZ sweep,
/// where almost nothing is perfectly frontal.
/// </summary>
public static class FaceAligner
{
    /// <summary>
    /// The ArcFace reference landmarks for a 112x112 crop: right eye, left eye,
    /// nose tip, right mouth corner, left mouth corner. Every model this
    /// application supports is trained against this arrangement.
    /// </summary>
    private static readonly Point2f[] Template =
    {
        new Point2f(38.2946f, 51.6963f),
        new Point2f(73.5318f, 51.5014f),
        new Point2f(56.0252f, 71.7366f),
        new Point2f(41.5493f, 92.3655f),
        new Point2f(70.7299f, 92.2041f)
    };

    private const int TemplateSize = 112;

    /// <summary>
    /// Aligns a face from the full frame. Returns null when the landmarks are
    /// unusable, so the caller can fall back to a plain crop rather than embed
    /// a warp built from nonsense.
    /// </summary>
    public static Mat? Align(Mat frame, Point2f[] landmarks, int outputSize = TemplateSize)
    {
        if (frame == null || frame.Empty() || landmarks == null || landmarks.Length != 5)
        {
            return null;
        }

        var scale = outputSize / (float)TemplateSize;
        var destination = new Point2f[5];

        for (var i = 0; i < 5; i++)
        {
            destination[i] = new Point2f(Template[i].X * scale, Template[i].Y * scale);
        }

        if (!TrySimilarityTransform(landmarks, destination, out var transform))
        {
            return null;
        }

        var output = new Mat();

        try
        {
            Cv2.WarpAffine(
                frame,
                output,
                transform,
                new Size(outputSize, outputSize),
                InterpolationFlags.Linear,
                BorderTypes.Replicate);
        }
        catch (OpenCVException)
        {
            output.Dispose();
            transform.Dispose();
            return null;
        }

        transform.Dispose();
        return output;
    }

    /// <summary>
    /// Least-squares similarity transform - uniform scale, rotation and
    /// translation - mapping the detected landmarks onto the template.
    ///
    /// A full affine fit would also absorb shear, which would let a bad landmark
    /// distort the face into matching the template rather than revealing that it
    /// does not. Restricting the fit to a similarity is what keeps a poor
    /// detection looking poor.
    /// </summary>
    private static bool TrySimilarityTransform(Point2f[] source, Point2f[] destination, out Mat transform)
    {
        transform = new Mat();

        double sourceMeanX = 0, sourceMeanY = 0, destMeanX = 0, destMeanY = 0;

        for (var i = 0; i < source.Length; i++)
        {
            sourceMeanX += source[i].X;
            sourceMeanY += source[i].Y;
            destMeanX += destination[i].X;
            destMeanY += destination[i].Y;
        }

        sourceMeanX /= source.Length;
        sourceMeanY /= source.Length;
        destMeanX /= source.Length;
        destMeanY /= source.Length;

        // a and b are the two components of the complex inner product between
        // the centred point sets; c is the source variance that normalises them.
        double a = 0, b = 0, c = 0;

        for (var i = 0; i < source.Length; i++)
        {
            var sx = source[i].X - sourceMeanX;
            var sy = source[i].Y - sourceMeanY;
            var dx = destination[i].X - destMeanX;
            var dy = destination[i].Y - destMeanY;

            a += (sx * dx) + (sy * dy);
            b += (sx * dy) - (sy * dx);
            c += (sx * sx) + (sy * sy);
        }

        // Degenerate input: all five landmarks on one spot, which happens when a
        // detector returns zeros for a face it did not really find.
        if (c < 1e-6)
        {
            transform.Dispose();
            transform = new Mat();
            return false;
        }

        var cos = a / c;
        var sin = b / c;

        var matrix = new double[2, 3];
        matrix[0, 0] = cos;
        matrix[0, 1] = -sin;
        matrix[0, 2] = destMeanX - ((cos * sourceMeanX) - (sin * sourceMeanY));
        matrix[1, 0] = sin;
        matrix[1, 1] = cos;
        matrix[1, 2] = destMeanY - ((sin * sourceMeanX) + (cos * sourceMeanY));

        transform.Dispose();
        transform = new Mat(2, 3, MatType.CV_64FC1);

        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                transform.Set(row, column, matrix[row, column]);
            }
        }

        return true;
    }

    /// <summary>
    /// The scale the alignment applied, as template pixels per source pixel.
    /// Values well above 1 mean the face was small in the frame and has been
    /// enlarged, which is a useful quality signal.
    /// </summary>
    public static float AlignmentScale(Point2f[] landmarks)
    {
        if (landmarks == null || landmarks.Length != 5)
        {
            return 0f;
        }

        var eyeDistance = Distance(landmarks[0], landmarks[1]);
        if (eyeDistance <= 0f)
        {
            return 0f;
        }

        var templateEyeDistance = Distance(Template[0], Template[1]);
        return templateEyeDistance / eyeDistance;
    }

    public static float Distance(Point2f a, Point2f b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathEx.Sqrt((dx * dx) + (dy * dy));
    }
}
