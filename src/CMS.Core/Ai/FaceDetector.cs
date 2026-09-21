using CMS.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace CMS.Core.Ai;

/// <summary>
/// A face found in a frame, with the five landmarks when the model emits them.
/// </summary>
public readonly struct FaceBox
{
    public FaceBox(Detection box, Point2f[] landmarks)
    {
        Box = box;
        Landmarks = landmarks;
    }

    public Detection Box { get; }

    /// <summary>
    /// Right eye, left eye, nose tip, right mouth corner, left mouth corner, in
    /// source-frame pixels. Empty when the head carries no landmark branch.
    /// </summary>
    public Point2f[] Landmarks { get; }

    public bool HasLandmarks => Landmarks.Length == 5;
}

/// <summary>
/// Locates faces in a frame with a local ONNX model.
///
/// Three export shapes are recognised, because the head layout is what decides
/// how the raw tensors are read:
///
///  * YuNet (opencv_zoo) - twelve outputs, cls_/obj_/bbox_/kps_ per stride.
///  * A single-class YOLO-face head - [1, 4+, N] or [1, N, 5+].
///  * A pre-decoded row list - [N, 15] of box, landmarks and score.
///
/// Anything else is refused at load time with a message naming the outputs,
/// rather than loading successfully and then silently finding nothing.
/// </summary>
public sealed class FaceDetector : IDisposable
{
    /// <summary>YuNet predicts on three feature maps, one per stride.</summary>
    private static readonly int[] YunetStrides = { 8, 16, 32 };

    private readonly object _gate = new object();
    private InferenceSession? _session;
    private string _inputName = "input";
    private string _outputName = string.Empty;
    private int _inputWidth = 640;
    private int _inputHeight = 640;
    private bool _transposedOutput;
    private HeadLayout _layout = HeadLayout.Unsupported;
    private bool _disposed;

    private enum HeadLayout
    {
        Unsupported,
        YuNet,
        RowList,
        Yolo
    }

    public bool IsReady => _session != null;

    public string ModelPath { get; private set; } = string.Empty;

    public string? LastError { get; private set; }

    /// <summary>The head the loaded model was recognised as, for the info screen.</summary>
    public string LayoutName => _layout.ToString();

    public int InputWidth => _inputWidth;

    public int InputHeight => _inputHeight;

    public float ConfidenceThreshold { get; set; } = 0.5f;

    public float NmsThreshold { get; set; } = 0.4f;

    /// <summary>Faces smaller than this many pixels on a side are ignored.</summary>
    public int MinimumFaceSize { get; set; } = 24;

    public bool Load(string modelPath, bool useGpu = false)
    {
        lock (_gate)
        {
            Unload();

            if (!OnnxSessionFactory.ModelExists(modelPath))
            {
                LastError = "Face detection model not found: " + OnnxSessionFactory.ResolvePath(modelPath);
                return false;
            }

            try
            {
                _session = OnnxSessionFactory.Create(modelPath, useGpu);
                ModelPath = OnnxSessionFactory.ResolvePath(modelPath);

                Inspect();

                if (_layout == HeadLayout.Unsupported)
                {
                    // Loading a model whose output cannot be read would look like
                    // a working detector that never sees anyone.
                    LastError = DescribeUnsupportedHead();
                    Unload();
                    return false;
                }

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
            if (dimensions[2] > 0)
            {
                _inputHeight = dimensions[2];
            }

            if (dimensions[3] > 0)
            {
                _inputWidth = dimensions[3];
            }
        }

        var outputs = _session.OutputMetadata;

        // YuNet is checked first and by name: its twelve tensors mean the single
        // "first output" test below would read cls_8 and make no sense of it.
        if (YunetStrides.All(stride =>
                outputs.ContainsKey("cls_" + stride) &&
                outputs.ContainsKey("obj_" + stride) &&
                outputs.ContainsKey("bbox_" + stride) &&
                outputs.ContainsKey("kps_" + stride)))
        {
            _layout = HeadLayout.YuNet;
            _outputName = string.Empty;
            return;
        }

        var output = outputs.First();
        _outputName = output.Key;

        var outputDimensions = output.Value.Dimensions;

        if (outputDimensions.Length == 2 && outputDimensions[1] >= 14)
        {
            _layout = HeadLayout.RowList;
            return;
        }

        if (outputDimensions.Length == 3 && outputDimensions[1] > 0 && outputDimensions[2] > 0)
        {
            _transposedOutput = outputDimensions[1] < outputDimensions[2];

            var channels = _transposedOutput ? outputDimensions[1] : outputDimensions[2];
            if (channels >= 5)
            {
                _layout = HeadLayout.Yolo;
                return;
            }
        }

        _layout = HeadLayout.Unsupported;
    }

    private string DescribeUnsupportedHead()
    {
        if (_session == null)
        {
            return "The face detection model could not be inspected.";
        }

        var described = _session.OutputMetadata
            .Select(o => o.Key + "[" + string.Join(",", o.Value.Dimensions) + "]")
            .ToList();

        return "This face detection model's output layout is not supported. It has " +
               described.Count + " output(s): " + string.Join(", ", described.Take(6)) +
               (described.Count > 6 ? ", ..." : string.Empty) +
               ". Supported: YuNet (opencv_zoo), a single-class YOLO-face head, " +
               "or a pre-decoded [N,15] row list. SCRFD exports (score_*/bbox_*/kps_*) are not read.";
    }

    /// <summary>Returns face boxes in source-frame pixel coordinates.</summary>
    public List<Detection> Detect(Mat frame)
        => DetectFaces(frame).Select(f => f.Box).ToList();

    /// <summary>As <see cref="Detect"/>, but keeps the landmarks when there are any.</summary>
    public List<FaceBox> DetectFaces(Mat frame)
    {
        if (frame == null || frame.Empty())
        {
            return new List<FaceBox>();
        }

        lock (_gate)
        {
            if (_session == null)
            {
                return new List<FaceBox>();
            }

            var tensor = Preprocess(frame, out var scale, out var padX, out var padY);
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

            using var results = _session.Run(inputs);

            List<FaceBox> faces;

            if (_layout == HeadLayout.YuNet)
            {
                faces = ParseYunet(results, scale, padX, padY, frame.Width, frame.Height);
            }
            else
            {
                var output = results
                    .First(r => string.IsNullOrEmpty(_outputName) || r.Name == _outputName)
                    .AsTensor<float>();

                faces = _layout == HeadLayout.RowList
                    ? ParseRowList(output, scale, padX, padY, frame.Width, frame.Height)
                    : ParseYoloFace(output, scale, padX, padY, frame.Width, frame.Height);
            }

            return NonMaximumSuppression(faces, NmsThreshold);
        }
    }

    /// <summary>
    /// Letterboxes into the model's input tensor.
    ///
    /// YuNet is trained on the OpenCV blob defaults - BGR channel order and raw
    /// 0..255 values - while the YOLO-face exports expect RGB scaled to 0..1.
    /// Feeding either one the other's tensor produces no detections at all.
    /// </summary>
    private DenseTensor<float> Preprocess(Mat frame, out float scale, out int padX, out int padY)
    {
        var isYunet = _layout == HeadLayout.YuNet;

        scale = Math.Min((float)_inputWidth / frame.Width, (float)_inputHeight / frame.Height);

        var scaledWidth = (int)Math.Round(frame.Width * scale);
        var scaledHeight = (int)Math.Round(frame.Height * scale);
        padX = (_inputWidth - scaledWidth) / 2;
        padY = (_inputHeight - scaledHeight) / 2;

        using var resized = new Mat();
        Cv2.Resize(frame, resized, new OpenCvSharp.Size(scaledWidth, scaledHeight));

        var fill = isYunet ? new Scalar(0, 0, 0) : new Scalar(114, 114, 114);

        using var canvas = new Mat(
            new OpenCvSharp.Size(_inputWidth, _inputHeight),
            MatType.CV_8UC3,
            fill);

        using (var roi = new Mat(canvas, new Rect(padX, padY, scaledWidth, scaledHeight)))
        {
            resized.CopyTo(roi);
        }

        using var prepared = new Mat();
        if (isYunet)
        {
            canvas.CopyTo(prepared);
        }
        else
        {
            Cv2.CvtColor(canvas, prepared, ColorConversionCodes.BGR2RGB);
        }

        var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
        var buffer = tensor.Buffer.Span;
        var planeSize = _inputWidth * _inputHeight;

        // A three-channel Mat must be read as Vec3b. The byte[] overload rejects
        // any CV_8UC3 image whose pixel count is not a multiple of three, which is
        // almost every frame size.
        prepared.GetArray(out Vec3b[] pixels);

        var divisor = isYunet ? 1f : 255f;

        for (var i = 0; i < planeSize; i++)
        {
            var pixel = pixels[i];
            buffer[i] = pixel.Item0 / divisor;
            buffer[planeSize + i] = pixel.Item1 / divisor;
            buffer[(planeSize * 2) + i] = pixel.Item2 / divisor;
        }

        return tensor;
    }

    /// <summary>
    /// Decodes YuNet's three stride heads.
    ///
    /// Each cell predicts an offset from its own position: the centre is the cell
    /// index plus the predicted fraction, scaled by the stride, and the size is
    /// the exponential of the prediction. The score is the geometric mean of the
    /// class and objectness branches, which is what OpenCV's own reader uses.
    /// </summary>
    private List<FaceBox> ParseYunet(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        float scale,
        int padX,
        int padY,
        int width,
        int height)
    {
        var faces = new List<FaceBox>();
        var tensors = results.ToDictionary(r => r.Name, r => r.AsTensor<float>());

        foreach (var stride in YunetStrides)
        {
            if (!tensors.TryGetValue("cls_" + stride, out var cls) ||
                !tensors.TryGetValue("obj_" + stride, out var obj) ||
                !tensors.TryGetValue("bbox_" + stride, out var bbox) ||
                !tensors.TryGetValue("kps_" + stride, out var kps))
            {
                continue;
            }

            var columns = _inputWidth / stride;
            var rows = _inputHeight / stride;

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = (row * columns) + column;

                    var classScore = MathEx.Clamp(cls.GetValue(index), 0f, 1f);
                    var objectScore = MathEx.Clamp(obj.GetValue(index), 0f, 1f);
                    var confidence = (float)Math.Sqrt(classScore * objectScore);

                    if (confidence < ConfidenceThreshold)
                    {
                        continue;
                    }

                    var boxOffset = index * 4;
                    var centerX = (column + bbox.GetValue(boxOffset)) * stride;
                    var centerY = (row + bbox.GetValue(boxOffset + 1)) * stride;
                    var boxWidth = (float)Math.Exp(bbox.GetValue(boxOffset + 2)) * stride;
                    var boxHeight = (float)Math.Exp(bbox.GetValue(boxOffset + 3)) * stride;

                    var box = ToFrameBox(
                        centerX - (boxWidth / 2f),
                        centerY - (boxHeight / 2f),
                        boxWidth,
                        boxHeight,
                        scale, padX, padY, width, height, confidence, MinimumFaceSize);

                    if (!box.HasValue)
                    {
                        continue;
                    }

                    var landmarks = new Point2f[5];
                    var keypointOffset = index * 10;

                    for (var point = 0; point < 5; point++)
                    {
                        landmarks[point] = new Point2f(
                            (((column + kps.GetValue(keypointOffset + (point * 2))) * stride) - padX) / scale,
                            (((row + kps.GetValue(keypointOffset + (point * 2) + 1)) * stride) - padY) / scale);
                    }

                    faces.Add(new FaceBox(box.Value, landmarks));
                }
            }
        }

        return faces;
    }

    private List<FaceBox> ParseYoloFace(Tensor<float> output, float scale, int padX, int padY, int width, int height)
    {
        var faces = new List<FaceBox>();
        var dimensions = output.Dimensions;

        if (dimensions.Length != 3)
        {
            return faces;
        }

        var boxes = _transposedOutput ? dimensions[2] : dimensions[1];
        var channels = _transposedOutput ? dimensions[1] : dimensions[2];

        if (channels < 5)
        {
            return faces;
        }

        // An anchor-free head ([1, 4+classes, N]) puts the class score straight in
        // slot 4; the older anchored head ([1, N, 5+classes]) puts objectness there
        // and the class score in slot 5. Only the latter is a product.
        var hasObjectness = !_transposedOutput && channels >= 6;

        for (var i = 0; i < boxes; i++)
        {
            var confidence = Read(output, i, 4);

            if (hasObjectness)
            {
                var classScore = Read(output, i, 5);
                if (classScore > 0f && classScore <= 1f)
                {
                    confidence *= classScore;
                }
            }

            if (confidence < ConfidenceThreshold)
            {
                continue;
            }

            var boxWidth = Read(output, i, 2);
            var boxHeight = Read(output, i, 3);

            var box = ToFrameBox(
                Read(output, i, 0) - (boxWidth / 2f),
                Read(output, i, 1) - (boxHeight / 2f),
                boxWidth,
                boxHeight,
                scale, padX, padY, width, height, confidence, MinimumFaceSize);

            if (box.HasValue)
            {
                faces.Add(new FaceBox(box.Value, Array.Empty<Point2f>()));
            }
        }

        return faces;
    }

    /// <summary>Reads an already-decoded [N, 15] list of box, landmarks and score.</summary>
    private List<FaceBox> ParseRowList(Tensor<float> output, float scale, int padX, int padY, int width, int height)
    {
        var faces = new List<FaceBox>();
        var rows = output.Dimensions[0];
        var columns = output.Dimensions[1];

        for (var i = 0; i < rows; i++)
        {
            var confidence = output[i, columns - 1];
            if (confidence < ConfidenceThreshold)
            {
                continue;
            }

            // These rows carry top-left x and y plus width and height.
            var box = ToFrameBox(
                output[i, 0],
                output[i, 1],
                output[i, 2],
                output[i, 3],
                scale, padX, padY, width, height, confidence, MinimumFaceSize);

            if (!box.HasValue)
            {
                continue;
            }

            var landmarks = Array.Empty<Point2f>();

            if (columns >= 15)
            {
                landmarks = new Point2f[5];
                for (var point = 0; point < 5; point++)
                {
                    landmarks[point] = new Point2f(
                        (output[i, 4 + (point * 2)] - padX) / scale,
                        (output[i, 5 + (point * 2)] - padY) / scale);
                }
            }

            faces.Add(new FaceBox(box.Value, landmarks));
        }

        return faces;
    }

    private float Read(Tensor<float> output, int box, int channel)
        => _transposedOutput ? output[0, channel, box] : output[0, box, channel];

    /// <summary>Maps a letterbox-space top-left box back onto the source frame.</summary>
    private static Detection? ToFrameBox(
        float left,
        float top,
        float boxWidth,
        float boxHeight,
        float scale,
        int padX,
        int padY,
        int width,
        int height,
        float confidence,
        int minimumSize)
    {
        var x = (left - padX) / scale;
        var y = (top - padY) / scale;

        var x1 = MathEx.Clamp(x, 0, width);
        var y1 = MathEx.Clamp(y, 0, height);
        var x2 = MathEx.Clamp(x + (boxWidth / scale), 0, width);
        var y2 = MathEx.Clamp(y + (boxHeight / scale), 0, height);

        if (x2 - x1 < minimumSize || y2 - y1 < minimumSize)
        {
            return null;
        }

        return new Detection(0, "face", confidence, x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>Greedy suppression that carries the landmarks through with the box.</summary>
    private static List<FaceBox> NonMaximumSuppression(List<FaceBox> faces, float iouThreshold)
    {
        var ordered = faces.OrderByDescending(f => f.Box.Confidence).ToList();
        var kept = new List<FaceBox>();

        while (ordered.Count > 0)
        {
            var best = ordered[0];
            kept.Add(best);
            ordered.RemoveAt(0);
            ordered.RemoveAll(candidate =>
                YoloDetector.IntersectionOverUnion(best.Box, candidate.Box) > iouThreshold);
        }

        return kept;
    }

    private void Unload()
    {
        _session?.Dispose();
        _session = null;
        ModelPath = string.Empty;
        _layout = HeadLayout.Unsupported;
        _transposedOutput = false;
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
