using System.Diagnostics;
using System.Security.Cryptography;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Lets the C# compiler emit <c>init</c> accessors and records while
    /// targeting .NET Framework 4.7, which does not ship this marker type.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}

namespace CMS.Core
{
    /// <summary>
    /// Small stand-ins for BCL helpers that only exist on newer runtimes. Keeping
    /// them in one place makes the 4.7 target explicit rather than scattering
    /// workarounds through the code.
    /// </summary>
    public static class MathEx
    {
        public static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;

        public static float Clamp(float value, float min, float max)
            => value < min ? min : value > max ? max : value;

        public static double Clamp(double value, double min, double max)
            => value < min ? min : value > max ? max : value;

        public static long Clamp(long value, long min, long max)
            => value < min ? min : value > max ? max : value;

        public static float Sqrt(float value) => (float)Math.Sqrt(value);
    }

    /// <summary>
    /// A monotonic millisecond clock. Replaces Environment.TickCount64, which is
    /// not available on .NET Framework, and does not wrap like TickCount.
    /// </summary>
    public static class Clock
    {
        private static readonly Stopwatch Timer = Stopwatch.StartNew();

        public static long TickCount => Timer.ElapsedMilliseconds;
    }

    /// <summary>Cryptographic helpers missing from the 4.7 surface.</summary>
    public static class CryptoEx
    {
        /// <summary>Cryptographically secure random bytes.</summary>
        public static byte[] RandomBytes(int count)
        {
            var buffer = new byte[count];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(buffer);
            }

            return buffer;
        }

        public static byte[] Sha1(byte[] data)
        {
            using (var sha = SHA1.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        /// <summary>
        /// PBKDF2 over HMAC-SHA256 (RFC 2898). .NET Framework 4.7 only offers the
        /// SHA-1 variant, so the derivation is implemented here.
        /// </summary>
        public static byte[] Pbkdf2Sha256(byte[] password, byte[] salt, int iterations, int outputLength)
        {
            using (var hmac = new HMACSHA256(password))
            {
                var hashLength = hmac.HashSize / 8;
                var blockCount = (outputLength + hashLength - 1) / hashLength;
                var output = new byte[outputLength];
                var written = 0;

                for (var block = 1; block <= blockCount; block++)
                {
                    var seed = new byte[salt.Length + 4];
                    Buffer.BlockCopy(salt, 0, seed, 0, salt.Length);
                    seed[salt.Length] = (byte)(block >> 24);
                    seed[salt.Length + 1] = (byte)(block >> 16);
                    seed[salt.Length + 2] = (byte)(block >> 8);
                    seed[salt.Length + 3] = (byte)block;

                    var u = hmac.ComputeHash(seed);
                    var result = (byte[])u.Clone();

                    for (var iteration = 1; iteration < iterations; iteration++)
                    {
                        u = hmac.ComputeHash(u);
                        for (var i = 0; i < result.Length; i++)
                        {
                            result[i] ^= u[i];
                        }
                    }

                    var toCopy = Math.Min(hashLength, outputLength - written);
                    Buffer.BlockCopy(result, 0, output, written, toCopy);
                    written += toCopy;
                }

                return output;
            }
        }

        /// <summary>
        /// Comparison whose running time does not depend on where the first
        /// difference is, so it cannot be used as a timing oracle.
        /// </summary>
        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }

            return difference == 0;
        }
    }

    /// <summary>String helpers for overloads the 4.7 BCL does not expose.</summary>
    public static class StringEx
    {
        public static bool Contains(this string source, string value, StringComparison comparison)
            => source != null && value != null && source.IndexOf(value, comparison) >= 0;
    }
}
