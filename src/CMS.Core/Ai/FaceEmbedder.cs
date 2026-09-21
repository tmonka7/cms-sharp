using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace CMS.Core.Ai;

/// <summary>
/// Turns an aligned face crop into an L2-normalised embedding (ArcFace-style,
/// typically 112x112 in and 512 floats out). Two faces belong to the same person
/// when the cosine similarity of their embeddings clears the threshold.
/// </summary>
public sealed class FaceEmbedder : IDisposable
{
    private readonly object _gate = new object();
    private InferenceSession? _session;
    private string _inputName = "input";
    private int _inputWidth = 112;
    private int _inputHeight = 112;
    private bool _disposed;

    public bool IsReady => _session != null;

    public string ModelPath { get; private set; } = string.Empty;

    public string? LastError { get; private set; }

    public int EmbeddingSize { get; private set; } = 512;

    /// <summary>
    /// ArcFace exports expect (pixel - 127.5) / 128. Models trained with a plain
    /// 0..1 scale can switch this off.
    /// </summary>
    public bool UseSignedNormalisation { get; set; } = true;

    public bool Load(string modelPath, bool useGpu = false)
    {
        lock (_gate)
        {
            Unload();

            if (!OnnxSessionFactory.ModelExists(modelPath))
            {
                LastError = "Face recognition model not found: " + OnnxSessionFactory.ResolvePath(modelPath);
                return false;
            }

            try
            {
                _session = OnnxSessionFactory.Create(modelPath, useGpu);
                ModelPath = OnnxSessionFactory.ResolvePath(modelPath);

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

                var outputDimensions = _session.OutputMetadata.First().Value.Dimensions;
                if (outputDimensions.Length >= 2)
                {
                    var last = outputDimensions[outputDimensions.Length - 1];
                    if (last > 0)
                    {
                        EmbeddingSize = last;
                    }
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

    /// <summary>Embeds a BGR face crop. Returns an empty array when not loaded.</summary>
    public float[] Embed(Mat faceCrop)
    {
        if (faceCrop == null || faceCrop.Empty())
        {
            return Array.Empty<float>();
        }

        lock (_gate)
        {
            if (_session == null)
            {
                return Array.Empty<float>();
            }

            using var resized = new Mat();
            Cv2.Resize(faceCrop, resized, new OpenCvSharp.Size(_inputWidth, _inputHeight));

            using var rgb = new Mat();
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });
            var buffer = tensor.Buffer.Span;
            var planeSize = _inputWidth * _inputHeight;

            // A three-channel Mat must be read as Vec3b. The byte[] overload rejects
            // any CV_8UC3 image whose pixel count is not a multiple of three.
            rgb.GetArray(out Vec3b[] pixels);

            for (var i = 0; i < planeSize; i++)
            {
                var pixel = pixels[i];
                buffer[i] = Scale(pixel.Item0);
                buffer[planeSize + i] = Scale(pixel.Item1);
                buffer[(planeSize * 2) + i] = Scale(pixel.Item2);
            }

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            using var results = _session.Run(inputs);

            return Normalise(results.First().AsTensor<float>().ToArray());
        }
    }

    private float Scale(byte value)
        => UseSignedNormalisation ? (value - 127.5f) / 128f : value / 255f;

    /// <summary>Scales a vector to unit length so cosine similarity is a dot product.</summary>
    public static float[] Normalise(float[] vector)
    {
        var sum = 0f;
        for (var i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }

        var magnitude = MathEx.Sqrt(sum);
        if (magnitude <= float.Epsilon)
        {
            return vector;
        }

        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            result[i] = vector[i] / magnitude;
        }

        return result;
    }

    /// <summary>
    /// Cosine similarity remapped from -1..1 onto 0..1, which is the number the
    /// UI shows as a match percentage.
    /// </summary>
    public static float Similarity(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0f;
        }

        var dot = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return MathEx.Clamp((dot + 1f) / 2f, 0f, 1f);
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
