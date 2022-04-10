using Sulakore.Habbo;
using Sulakore.Network;

namespace Tangine.API;

public interface IInstaller
{
    public Incoming In { get; }
    public Outgoing Out { get; }

    IGame Game { get; }
    IHConnection Connection { get; }
}