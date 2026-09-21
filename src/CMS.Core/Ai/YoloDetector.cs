using System.Diagnostics;
using CMS.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace CMS.Core.Ai;

/// <summary>
/// YOLO object detector running on ONNX Runtime.
///
/// The output layout is detected at load time, so the same code drives the
/// anchor-free head used by YOLOv26n / v8 / v11 — shape [1, 4 + classes, boxes] —
/// and the older [1, boxes, 5 + classes] head that carries an objectness score.
/// </summary>
public sealed class YoloDetector : IDisposable
{
    private readonly object _gate = new object();
    private InferenceSession? _session;
    private string _inputName = "images";
    private string _outputName = string.Empty;
    private int _inputWidth = 640;
    private int _inputHeight = 640;
    private int _classCount = 80;
    private bool _transposedOutput;
    private bool _hasObjectness;
    private bool _disposed;

    public bool IsReady => _session != null;

    public string ModelPath { get; private set; } = string.Empty;

    public string? LastError { get; private set; }

    public string[] Labels { get; set; } = CocoLabels.Names;

    public float ConfidenceThreshold { get; set; } = 0.5f;

    public float NmsThreshold { get; set; } = 0.45f;

    /// <summary>When non-empty, only these labels are reported.</summary>
    public HashSet<string> ClassFilter { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int InputWidth => _inputWidth;

    public int InputHeight => _inputHeight;

    /// <summary>
    /// Loads a model. Returns false rather than throwing when the file is
    /// missing, so the UI can report "model not loaded" and keep running.
    /// </summary>
    public bool Load(string modelPath, bool useGpu = false)
    {
        lock (_gate)
        {
            Unload();

            if (!OnnxSessionFactory.ModelExists(modelPath))
            {
                LastError = "Model file not found: " + OnnxSessionFactory.ResolvePath(modelPath);
                return false;
            }

            try
            {
                _session = OnnxSessionFactory.Create(modelPath, useGpu);
                ModelPath = OnnxSessionFactory.ResolvePath(modelPath);
                Inspect();
                LastError = null;
                return true;
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or FileNotFoundException or InvalidOperationException)
            {
                LastError = ex.Message;
                Unload();
                return false;
            }
        }
    }

    private void Inspect()
    {
        if (_session == null)
        {
            return;
        }

        var input = _session.InputMetadata.First();
        _inputName = input.Key;

        var dimensions = input.Value.Dimensions;
        if (dimensions.Length == 4)
        {
            // NCHW; dynamic axes come through as -1.
            if (dimensions[2] > 0)
            {
                _inputHeight = dimensions[2];
            }

            if (dimensions[3] > 0)
            {
                _inputWidth = dimensions[3];
            }
        }

        var output = _session.OutputMetadata.First();
        _outputName = output.Key;

        var outputDimensions = output.Value.Dimensions;
        if (outputDimensions.Length == 3 && outputDimensions[1] > 0 && outputDimensions[2] > 0)
        {
            var a = outputDimensions[1];
            var b = outputDimensions[2];

            // [1, 4+nc, anchors] keeps the channel count small relative to the
            // anchor count; [1, anchors, 5+nc] is the other way round.
            _transposedOutput = a < b;

            var channels = _transposedOutput ? a : b;
            _hasObjectness = !_transposedOutput && channels == 85;
            _classCount = _hasObjectness ? channels - 5 : channels - 4;
        }

        if (_classCount <= 0)
        {
            _classCount = 80;
        }
    }

    /// <summary>Runs detection on a BGR frame and returns boxes in frame pixels.</summary>
    public DetectionFrameResult Detect(Mat frame, int cameraId = 0)
    {
        if (frame == null || frame.Empty())
        {
            return new DetectionFrameResult { CameraId = cameraId };
        }

        lock (_gate)
        {
            if (_session == null)
            {
                return new DetectionFrameResult
                {
                    CameraId = cameraId,
                    FrameWidth = frame.Width,
                    FrameHeight = frame.Height
                };
            }

            var stopwatch = Stopwatch.StartNew();

            var tensor = Preprocess(frame, out var scale, out var padX, out var padY);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, tensor)
            };

            using var results = _session.Run(inputs);

            var output = results
                .First(r => string.IsNullOrEmpty(_outputName) || r.Name == _outputName)
                .AsTensor<float>();

            var detections = Postprocess(output, scale, padX, padY, frame.Width, frame.Height);
            stopwatch.Stop();

            return new DetectionFrameResult
            {
                CameraId = cameraId,
                Detections = detections,
                InferenceMs = stopwatch.Elapsed.TotalMilliseconds,
                FrameWidth = frame.Width,
                FrameHeight = frame.Height
            };
        }
    }

    /// <summary>
    /// Letterbox resize into a normalised NCHW RGB tensor. Preserving the aspect
    /// ratio is what lets the boxes map cleanly back onto the source frame.
    /// </summary>
    private DenseTensor<float> Preprocess(Mat frame, out float scale, out int padX, out int padY)
    {
        scale = Math.Min((float)_inputWidth / frame.Width, (float)_inputHeight / frame.Height);

        var scaledWidth = (int)Math.Round(frame.Width * scale);
        var scaledHeight = (int)Math.Round(frame.Height * scale);
        padX = (_inputWidth - scaledWidth) / 2;
        padY = (_inputHeight - scaledHeight) / 2;

        using var resized = new Mat();
        Cv2.Resize(frame, resized, new OpenCvSharp.Size(scaledWidth, scaledHeight), 0, 0, InterpolationFlags.Linear);

        using var canvas = new Mat(
            new OpenCvSharp.Size(_inputWidth, _inputHeight),
            MatType.CV_8UC3,
            new Scalar(114, 114, 114));

        using (var roi = new Mat(canvas, new Rect(padX, padY, scaledWidth, scaledHeight)))
        {
            resized.CopyTo(roi);
        }

        using var rgb = new Mat();
        Cv2.CvtColor(canvas, rgb, ColorConversionCodes.BGR2RGB);

        var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
        var buffer = tensor.Buffer.Span;
        var planeSize = _inputWidth * _inputHeight;

        // A three-channel Mat must be read as Vec3b. The byte[] overload rejects
        // any CV_8UC3 image whose pixel count is not a multiple of three, which is
        // almost every frame size.
        rgb.GetArray(out Vec3b[] pixels);

        for (var i = 0; i < planeSize; i++)
        {
            var pixel = pixels[i];
            buffer[i] = pixel.Item0 / 255f;
            buffer[planeSize + i] = pixel.Item1 / 255f;
            buffer[(planeSize * 2) + i] = pixel.Item2 / 255f;
        }

        return tensor;
    }

    private List<Detection> Postprocess(
        Tensor<float> output,
        float scale,
        int padX,
        int padY,
        int frameWidth,
        int frameHeight)
    {
        var candidates = new List<Detection>();
        var dimensions = output.Dimensions;

        if (dimensions.Length != 3)
        {
            return candidates;
        }

        var channels = _transposedOutput ? dimensions[1] : dimensions[2];
        var boxes = _transposedOutput ? dimensions[2] : dimensions[1];
        var classCount = _hasObjectness ? channels - 5 : channels - 4;

        if (classCount <= 0)
        {
            return candidates;
        }

        var classOffset = _hasObjectness ? 5 : 4;

        for (var i = 0; i < boxes; i++)
        {
            var objectness = 1f;
            if (_hasObjectness)
            {
                objectness = Read(output, i, 4);
                if (objectness < ConfidenceThreshold)
                {
                    continue;
                }
            }

            var bestScore = 0f;
            var bestClass = -1;

            for (var c = 0; c < classCount; c++)
            {
                var score = Read(output, i, classOffset + c);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = c;
                }
            }

            var confidence = bestScore * objectness;
            if (bestClass < 0 || confidence < ConfidenceThreshold)
            {
                continue;
            }

            var label = bestClass < Labels.Length ? Labels[bestClass] : "class_" + bestClass;
            if (ClassFilter.Count > 0 && !ClassFilter.Contains(label))
            {
                continue;
            }

            // The model emits centre-x, centre-y, width and height in letterbox
            // space; undo the padding and the scale to get frame pixels.
            var centerX = Read(output, i, 0);
            var centerY = Read(output, i, 1);
            var width = Read(output, i, 2);
            var height = Read(output, i, 3);

            var left = (centerX - (width / 2f) - padX) / scale;
            var top = (centerY - (height / 2f) - padY) / scale;

            var x1 = MathEx.Clamp(left, 0, frameWidth);
            var y1 = MathEx.Clamp(top, 0, frameHeight);
            var x2 = MathEx.Clamp(left + (width / scale), 0, frameWidth);
            var y2 = MathEx.Clamp(top + (height / scale), 0, frameHeight);

            if (x2 - x1 < 2 || y2 - y1 < 2)
            {
                continue;
            }

            candidates.Add(new Detection(bestClass, label, confidence, x1, y1, x2 - x1, y2 - y1));
        }

        return NonMaximumSuppression(candidates, NmsThreshold);
    }

    private float Read(Tensor<float> output, int box, int channel)
        => _transposedOutput ? output[0, channel, box] : output[0, box, channel];

    /// <summary>Greedy per-class non-maximum suppression.</summary>
    public static List<Detection> NonMaximumSuppression(List<Detection> detections, float iouThreshold)
    {
        var kept = new List<Detection>();

        foreach (var group in detections.GroupBy(d => d.ClassId))
        {
            var ordered = group.OrderByDescending(d => d.Confidence).ToList();

            while (ordered.Count > 0)
            {
                var best = ordered[0];
                kept.Add(best);
                ordered.RemoveAt(0);
                ordered.RemoveAll(candidate => IntersectionOverUnion(best, candidate) > iouThreshold);
            }
        }

        return kept.OrderByDescending(d => d.Confidence).ToList();
    }

    public static float IntersectionOverUnion(Detection a, Detection b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);

        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        if (intersection <= 0)
        {
            return 0;
        }

        return intersection / (a.Area + b.Area - intersection);
    }

    private void Unload()
    {
        _session?.Dispose();
        _session = null;
        ModelPath = string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            Unload();
        }
    }
}
