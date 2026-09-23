using CMS.Core.Models;
using OpenCvSharp;

namespace CMS.Core.Ai;

/// <summary>Why a captured face was rejected, or that it was accepted.</summary>
public enum FaceQualityVerdict
{
    Accepted = 0,
    TooSmall,
    TooBlurred,
    TooDark,
    TooBright,
    TurnedAway,
    Tilted,
    LowConfidence,
    NoLandmarks
}

/// <summary>The measurements behind a verdict, kept so a rejection can be explained.</summary>
public sealed class FaceQualityReport
{
    public FaceQualityVerdict Verdict { get; set; } = FaceQualityVerdict.Accepted;

    public bool Accepted => Verdict == FaceQualityVerdict.Accepted;

    /// <summary>Distance between the eyes in source pixels.</summary>
    public float InterocularPixels { get; set; }

    /// <summary>Variance of the Laplacian. Low means blurred.</summary>
    public double Sharpness { get; set; }

    /// <summary>Mean luminance, 0 to 255.</summary>
    public double Brightness { get; set; }

    /// <summary>Head turn, 0 when the nose sits midway between the eyes.</summary>
    public float Yaw { get; set; }

    /// <summary>Head tilt in degrees, from the angle of the eye line.</summary>
    public float Roll { get; set; }

    public float DetectionConfidence { get; set; }

    /// <summary>A single 0 to 1 score used to pick the best capture of a person.</summary>
    public float Score { get; set; }

    public string Explain() => Verdict switch
    {
        FaceQualityVerdict.Accepted => "accepted",
        FaceQualityVerdict.TooSmall => "face too small (" + InterocularPixels.ToString("0") + " px between eyes)",
        FaceQualityVerdict.TooBlurred => "too blurred (sharpness " + Sharpness.ToString("0") + ")",
        FaceQualityVerdict.TooDark => "too dark (" + Brightness.ToString("0") + ")",
        FaceQualityVerdict.TooBright => "too bright (" + Brightness.ToString("0") + ")",
        FaceQualityVerdict.TurnedAway => "head turned away (yaw " + Yaw.ToString("0.00") + ")",
        FaceQualityVerdict.Tilted => "head tilted (" + Roll.ToString("0") + " degrees)",
        FaceQualityVerdict.LowConfidence => "detection confidence " + DetectionConfidence.ToString("0.00"),
        FaceQualityVerdict.NoLandmarks => "detector supplied no landmarks",
        _ => "rejected"
    };
}

/// <summary>Thresholds a captured face must clear before it is embedded.</summary>
public sealed class FaceQualityPolicy
{
    /// <summary>Minimum eye separation in source pixels.</summary>
    public float MinimumInterocularPixels { get; set; } = 28f;

    /// <summary>Minimum Laplacian variance. Motion blur from a moving camera falls well below this.</summary>
    public double MinimumSharpness { get; set; } = 45d;

    public double MinimumBrightness { get; set; } = 40d;

    public double MaximumBrightness { get; set; } = 225d;

    /// <summary>Maximum nose offset from the eye midpoint, as a fraction of eye separation.</summary>
    public float MaximumYaw { get; set; } = 0.30f;

    /// <summary>Maximum head tilt in degrees.</summary>
    public float MaximumRoll { get; set; } = 25f;

    /// <summary>Minimum detector confidence. Higher than live view, because an enrolment lasts.</summary>
    public float MinimumConfidence { get; set; } = 0.80f;

    /// <summary>A policy that accepts almost anything, for diagnosing an empty sweep.</summary>
    public static FaceQualityPolicy Permissive() => new FaceQualityPolicy
    {
        MinimumInterocularPixels = 12f,
        MinimumSharpness = 5d,
        MinimumBrightness = 10d,
        MaximumBrightness = 250d,
        MaximumYaw = 0.60f,
        MaximumRoll = 45f,
        MinimumConfidence = 0.50f
    };
}

/// <summary>
/// Decides whether a face found during a sweep is good enough to enrol.
///
/// This gate matters more than it looks. A sweep yields many more unusable
/// crops than a deliberate enrolment photograph does - blurred while the head
/// moves, in profile at the edge of a tile, or barely a few pixels across - and
/// admitting them produces embeddings that sit nowhere useful. The damage shows
/// up later as false matches that are hard to trace back to their cause.
/// </summary>
public static class FaceQuality
{
    public static FaceQualityReport Evaluate(
        Mat frame,
        FaceBox face,
        FaceQualityPolicy policy)
    {
        var report = new FaceQualityReport
        {
            DetectionConfidence = face.Box.Confidence
        };

        if (face.Box.Confidence < policy.MinimumConfidence)
        {
            report.Verdict = FaceQualityVerdict.LowConfidence;
            return report;
        }

        if (!face.HasLandmarks)
        {
            // Without landmarks the face cannot be aligned or its pose judged,
            // so it is not a candidate for an enrolment that will be kept.
            report.Verdict = FaceQualityVerdict.NoLandmarks;
            return report;
        }

        var rightEye = face.Landmarks[0];
        var leftEye = face.Landmarks[1];
        var nose = face.Landmarks[2];

        report.InterocularPixels = FaceAligner.Distance(rightEye, leftEye);

        if (report.InterocularPixels < policy.MinimumInterocularPixels)
        {
            report.Verdict = FaceQualityVerdict.TooSmall;
            return report;
        }

        // Roll from the eye line, and yaw from how far the nose sits off the
        // midpoint between the eyes. Neither is a true head pose, but both are
        // cheap and sufficient to reject a profile or a sideways head.
        var eyeDx = leftEye.X - rightEye.X;
        var eyeDy = leftEye.Y - rightEye.Y;
        report.Roll = (float)(Math.Atan2(eyeDy, eyeDx) * 180d / Math.PI);

        if (Math.Abs(report.Roll) > policy.MaximumRoll)
        {
            report.Verdict = FaceQualityVerdict.Tilted;
            return report;
        }

        var eyeMidX = (rightEye.X + leftEye.X) / 2f;
        var eyeMidY = (rightEye.Y + leftEye.Y) / 2f;
        report.Yaw = Math.Abs(nose.X - eyeMidX) / report.InterocularPixels;

        if (report.Yaw > policy.MaximumYaw)
        {
            report.Verdict = FaceQualityVerdict.TurnedAway;
            return report;
        }

        using var crop = CropForMeasurement(frame, face.Box);
        if (crop == null || crop.Empty())
        {
            report.Verdict = FaceQualityVerdict.TooSmall;
            return report;
        }

        using var grey = new Mat();
        Cv2.CvtColor(crop, grey, ColorConversionCodes.BGR2GRAY);

        report.Brightness = Cv2.Mean(grey).Val0;

        if (report.Brightness < policy.MinimumBrightness)
        {
            report.Verdict = FaceQualityVerdict.TooDark;
            return report;
        }

        if (report.Brightness > policy.MaximumBrightness)
        {
            report.Verdict = FaceQualityVerdict.TooBright;
            return report;
        }

        report.Sharpness = LaplacianVariance(grey);

        if (report.Sharpness < policy.MinimumSharpness)
        {
            report.Verdict = FaceQualityVerdict.TooBlurred;
            return report;
        }

        report.Verdict = FaceQualityVerdict.Accepted;
        report.Score = ComputeScore(report, policy);
        return report;
    }

    /// <summary>
    /// Combines the measurements into one number, so the best capture of a
    /// person across a whole sweep can be chosen without re-reading the frames.
    /// Size and sharpness dominate; pose is a penalty.
    /// </summary>
    private static float ComputeScore(FaceQualityReport report, FaceQualityPolicy policy)
    {
        var size = MathEx.Clamp(report.InterocularPixels / 90f, 0f, 1f);
        var sharp = MathEx.Clamp((float)(report.Sharpness / 400d), 0f, 1f);
        var frontal = MathEx.Clamp(1f - (report.Yaw / Math.Max(0.01f, policy.MaximumYaw)), 0f, 1f);
        var level = MathEx.Clamp(1f - (Math.Abs(report.Roll) / Math.Max(1f, policy.MaximumRoll)), 0f, 1f);
        var confident = MathEx.Clamp(report.DetectionConfidence, 0f, 1f);

        return MathEx.Clamp(
            (size * 0.30f) + (sharp * 0.25f) + (frontal * 0.20f) + (level * 0.10f) + (confident * 0.15f),
            0f,
            1f);
    }

    /// <summary>Variance of the Laplacian, the standard cheap focus measure.</summary>
    public static double LaplacianVariance(Mat grey)
    {
        using var laplacian = new Mat();
        Cv2.Laplacian(grey, laplacian, MatType.CV_64F);

        Cv2.MeanStdDev(laplacian, out var mean, out var stdDev);
        var sigma = stdDev.Val0;

        return sigma * sigma;
    }

    private static Mat? CropForMeasurement(Mat frame, Detection box)
    {
        var x = (int)MathEx.Clamp(box.X, 0, frame.Width - 1);
        var y = (int)MathEx.Clamp(box.Y, 0, frame.Height - 1);
        var width = (int)MathEx.Clamp(box.Width, 1, frame.Width - x);
        var height = (int)MathEx.Clamp(box.Height, 1, frame.Height - y);

        if (width < 2 || height < 2)
        {
            return null;
        }

        using var roi = new Mat(frame, new Rect(x, y, width, height));
        return roi.Clone();
    }
}
