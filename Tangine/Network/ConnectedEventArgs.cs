using System.ComponentModel;

using Sulakore.Network;

namespace Tangine.Network;

public sealed class ConnectedEventArgs : CancelEventArgs
{
    public IHConnection Connection { get; }
    public HotelEndPoint HotelServer { get; set; }
    public bool IsFakingPolicyRequest { get; set; }
    public TaskCompletionSource<HotelEndPoint> HotelServerSource { get; }

    public ConnectedEventArgs(IHConnection connection, HotelEndPoint hotelServer)
    {
        Connection = connection;
        HotelServer = hotelServer;
        HotelServerSource = new TaskCompletionSource<HotelEndPoint>();
    }
}