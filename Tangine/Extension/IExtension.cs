using Sulakore.Network;

namespace Tangine.Extension;

public interface IExtension : IDisposable
{
    bool IsStandalone { get; }
    IInstaller Installer { get; set; }

    void OnConnected();
    void HandleOutgoing(DataInterceptedEventArgs e);
    void HandleIncoming(DataInterceptedEventArgs e);
}