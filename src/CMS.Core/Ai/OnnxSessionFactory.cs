using Microsoft.ML.OnnxRuntime;

namespace CMS.Core.Ai;

/// <summary>
/// Builds ONNX Runtime sessions. Models are loaded from local files only — no
/// download step and no model server — so inference works on an air-gapped
/// machine.
/// </summary>
public static class OnnxSessionFactory
{
    /// <summary>Resolves a path that may be relative to the application directory.</summary>
    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));
    }

    public static bool ModelExists(string path)
    {
        var resolved = ResolvePath(path);
        return !string.IsNullOrEmpty(resolved) && File.Exists(resolved);
    }

    /// <summary>
    /// Creates a session tuned for real-time video. GPU is opt-in because the
    /// CUDA provider is not present in a default offline install.
    /// </summary>
    public static InferenceSession Create(string modelPath, bool useGpu, int intraOpThreads = 0)
    {
        var resolved = ResolvePath(modelPath);
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException("ONNX model not found: " + resolved, resolved);
        }

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = intraOpThreads > 0
                ? intraOpThreads
                : Math.Max(1, Environment.ProcessorCount / 2)
        };

        if (useGpu)
        {
            try
            {
                options.AppendExecutionProvider_CUDA();
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException)
            {
                // Fall back to CPU rather than refusing to start.
            }
        }

        return new InferenceSession(resolved, options);
    }
}
