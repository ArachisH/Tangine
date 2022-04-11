using System.Security.Cryptography.X509Certificates;

using Sulakore.Habbo;

namespace Tangine.Network;

public readonly record struct HConnectionOptions
{
    public static HConnectionOptions Default { get; }

    public int ListenPort { get; init; } = 9567;
    public int ListenSkipAmount { get; init; } = 0;

    public bool IsUsingWebSockets { get; init; } = false;
    public X509Certificate? Certificate { get; init; } = null;

    public HConnectionOptions(IGame game)
    {
        if (game == null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(game));
        }

        // This may, or may not change in the future.
        IsUsingWebSockets = game.Kind == GameKind.Unity;
    }
}