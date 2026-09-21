using CMS.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace CMS.Core.Ai;

/// <summary>
/// Locates faces in a frame with a local ONNX model.
///
/// Two common export layouts are handled: a single-class YOLO-face head
/// ([1, 5+, N] either way round) and YuNet ([N, 15] rows of box, landmarks and
/// score).
/// </summary>
public sealed class FaceDetector : IDisposable
{
    private readonly object _gate = new object();
    private InferenceSession? _session;
    private string _inputName = "input";
    private string _outputName = string.Empty;
    private int _inputWidth = 640;
    private int _inputHeight = 640;
    private bool _transposedOutput;
    private bool _yunetLayout;
    private bool _disposed;

    public bool IsReady => _session != null;

    public string ModelPath { get; private set; } = string.Empty;

    public string? LastError { get; private set; }

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

        var output = _session.OutputMetadata.First();
        _outputName = output.Key;

        var outputDimensions = output.Value.Dimensions;
        _yunetLayout = outputDimensions.Length == 2 && outputDimensions[1] >= 14;

        if (outputDimensions.Length == 3 && outputDimensions[1] > 0 && outputDimensions[2] > 0)
        {
            _transposedOutput = outputDimensions[1] < outputDimensions[2];
        }
    }

    /// <summary>Returns face boxes in source-frame pixel coordinates.</summary>
    public List<Detection> Detect(Mat frame)
    {
        if (frame == null || frame.Empty())
        {
            return new List<Detection>();
        }

        lock (_gate)
        {
            if (_session == null)
            {
                return new List<Detection>();
            }

            var tensor = Preprocess(frame, out var scale, out var padX, out var padY);
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

            using var results = _session.Run(inputs);

            var output = results
                .First(r => string.IsNullOrEmpty(_outputName) || r.Name == _outputName)
                .AsTensor<float>();

            var faces = _yunetLayout
                ? ParseYunet(output, scale, padX, padY, frame.Width, frame.Height)
                : ParseYoloFace(output, scale, padX, padY, frame.Width, frame.Height);

            return YoloDetector.NonMaximumSuppression(faces, NmsThreshold);
        }
    }

    private DenseTensor<float> Preprocess(Mat frame, out float scale, out int padX, out int padY)
    {
        scale = Math.Min((float)_inputWidth / frame.Width, (float)_inputHeight / frame.Height);

        var scaledWidth = (int)Math.Round(frame.Width * scale);
        var scaledHeight = (int)Math.Round(frame.Height * scale);
        padX = (_inputWidth - scaledWidth) / 2;
        padY = (_inputHeight - scaledHeight) / 2;

        using var resized = new Mat();
        Cv2.Resize(frame, resized, new OpenCvSharp.Size(scaledWidth, scaledHeight));

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

    private List<Detection> ParseYoloFace(Tensor<float> output, float scale, int padX, int padY, int width, int height)
    {
        var faces = new List<Detection>();
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

        for (var i = 0; i < boxes; i++)
        {
            var confidence = Read(output, i, 4);

            // Some exports keep objectness in slot 4 and the single class score
            // in slot 5; multiply when both are present.
            if (channels >= 6)
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

            var box = ToFrameBox(
                Read(output, i, 0),
                Read(output, i, 1),
                Read(output, i, 2),
                Read(output, i, 3),
                scale, padX, padY, width, height, confidence, MinimumFaceSize);

            if (box.HasValue)
            {
                faces.Add(box.Value);
            }
        }

        return faces;
    }

    private List<Detection> ParseYunet(Tensor<float> output, float scale, int padX, int padY, int width, int height)
    {
        var faces = new List<Detection>();
        var rows = output.Dimensions[0];
        var columns = output.Dimensions[1];

        for (var i = 0; i < rows; i++)
        {
            var confidence = output[i, columns - 1];
            if (confidence < ConfidenceThreshold)
            {
                continue;
            }

            // YuNet emits top-left x and y plus width and height.
            var left = (output[i, 0] - padX) / scale;
            var top = (output[i, 1] - padY) / scale;

            var x1 = MathEx.Clamp(left, 0, width);
            var y1 = MathEx.Clamp(top, 0, height);
            var x2 = MathEx.Clamp(left + (output[i, 2] / scale), 0, width);
            var y2 = MathEx.Clamp(top + (output[i, 3] / scale), 0, height);

            if (x2 - x1 >= MinimumFaceSize && y2 - y1 >= MinimumFaceSize)
            {
                faces.Add(new Detection(0, "face", confidence, x1, y1, x2 - x1, y2 - y1));
            }
        }

        return faces;
    }

    private float Read(Tensor<float> output, int box, int channel)
        => _transposedOutput ? output[0, channel, box] : output[0, box, channel];

    private static Detection? ToFrameBox(
        float centerX,
        float centerY,
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
        var left = (centerX - (boxWidth / 2f) - padX) / scale;
        var top = (centerY - (boxHeight / 2f) - padY) / scale;

        var x1 = MathEx.Clamp(left, 0, width);
        var y1 = MathEx.Clamp(top, 0, height);
        var x2 = MathEx.Clamp(left + (boxWidth / scale), 0, width);
        var y2 = MathEx.Clamp(top + (boxHeight / scale), 0, height);

        if (x2 - x1 < minimumSize || y2 - y1 < minimumSize)
        {
            return null;
        }

        return new Detection(0, "face", confidence, x1, y1, x2 - x1, y2 - y1);
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
