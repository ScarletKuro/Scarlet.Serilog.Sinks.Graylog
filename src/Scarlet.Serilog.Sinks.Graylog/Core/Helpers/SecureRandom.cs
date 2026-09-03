using System;
using System.Security.Cryptography;

namespace Scarlet.Serilog.Sinks.Graylog.Core.Helpers;

/// <summary>
/// Cryptographically strong random bytes, on every target framework.
/// </summary>
internal static class SecureRandom
{
    /// <summary>
    /// Returns <paramref name="count"/> random bytes.
    /// </summary>
    public static byte[] NextBytes(int count)
    {
        var buffer = new byte[count];
#if NET
        RandomNumberGenerator.Fill(buffer);
#else
        Rng.GetBytes(buffer);
#endif
        return buffer;
    }

#if !NET
    // The static members of RandomNumberGenerator are documented as thread safe, its instance members
    // are not, and the .NET Framework / netstandard2.0 targets have no static Fill overload. One
    // generator per thread costs nothing and sidesteps the question.
    [ThreadStatic]
    private static RandomNumberGenerator? _rng;

    private static RandomNumberGenerator Rng => _rng ??= RandomNumberGenerator.Create();
#endif
}
