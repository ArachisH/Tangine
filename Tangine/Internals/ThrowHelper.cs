using System.Diagnostics.CodeAnalysis;

namespace Tangine;

internal static class ThrowHelper
{
    [DoesNotReturn]
    internal static void ThrowNullReferenceException(string? message = null) => throw new NullReferenceException(message);

    [DoesNotReturn]
    internal static void ThrowNotSupportedException(string? message = null) => throw new NotSupportedException(message);

    [DoesNotReturn]
    internal static void ThrowArgumentException(string? message, string? paramName = null) => throw new ArgumentException(message, paramName);

    [DoesNotReturn]
    internal static void ThrowObjectDisposedException(string? message) => throw new ObjectDisposedException(message);

    [DoesNotReturn]
    internal static void ThrowFileNotFoundException(string? message, string? fileName) => throw new FileNotFoundException(message, fileName);
}