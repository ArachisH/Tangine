using System.Diagnostics.CodeAnalysis;

namespace Tangine;

internal static class ThrowHelper
{
    [DoesNotReturn]
    internal static void ThrowNullReferenceException() => throw new NullReferenceException();

    [DoesNotReturn]
    internal static void ThrowNotSupportedException(string message) => throw new NotSupportedException(message);
}