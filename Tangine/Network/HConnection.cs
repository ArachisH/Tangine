using System.Net;
using System.Text;
using System.Buffers;
using System.Security.Cryptography.X509Certificates;

using Sulakore.Network;
using Sulakore.Network.Buffers;
using Sulakore.Network.Formats;

namespace Tangine.Network;

public sealed class HConnection : IHConnection
{
    private static readonly byte[] _crossDomainPolicyRequestBytes, _crossDomainPolicyResponseBytes;

    private readonly object _disconnectLock;
    private readonly Action _cancelInterception;

    private bool _isIntercepting;
    private int _inSteps, _outSteps;

    /// <summary>
    /// Occurs when the connection between the client, and server have been intercepted.
    /// </summary>
    public event EventHandler<ConnectedEventArgs>? Connected;
    private void OnConnected(ConnectedEventArgs e)
    {
        Connected?.Invoke(this, e);
    }

    /// <summary>
    /// Occurs when either the game client, or server have disconnected.
    /// </summary>
    public event EventHandler? Disconnected;
    private void OnDisconnected(EventArgs e)
    {
        Disconnected?.Invoke(this, e);
    }

    /// <summary>
    /// Occurs when the client's outgoing data has been intercepted.
    /// </summary>
    public event EventHandler<DataInterceptedEventArgs>? DataOutgoing;
    private void OnDataOutgoing(DataInterceptedEventArgs e)
    {
        DataOutgoing?.Invoke(this, e);
    }

    /// <summary>
    /// Occrus when the server's incoming data has been intercepted.
    /// </summary>
    public event EventHandler<DataInterceptedEventArgs>? DataIncoming;
    private void OnDataIncoming(DataInterceptedEventArgs e)
    {
        DataIncoming?.Invoke(this, e);
    }

    public int SocketSkip { get; set; }
    public int ListenPort { get; set; } = 9567;
    public bool IsConnected { get; private set; }

    public IHFormat? SendFormat { get; }
    public IHFormat? ReceiveFormat { get; }

    public HNode? Local { get; private set; }
    public HNode? Remote { get; private set; }
    public X509Certificate? Certificate { get; set; }

    static HConnection()
    {
        _crossDomainPolicyRequestBytes = Encoding.UTF8.GetBytes("<policy-file-request/>\0");
        _crossDomainPolicyResponseBytes = Encoding.UTF8.GetBytes("<cross-domain-policy><allow-access-from domain=\"*\" to-ports=\"*\"/></cross-domain-policy>\0");
    }
    public HConnection()
    {
        _disconnectLock = new object();
        _cancelInterception = CancelInterception;
    }

    public async Task InterceptAsync(IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        await InterceptAsync(new HotelEndPoint(endpoint), cancellationToken).ConfigureAwait(false);
    }
    public async Task InterceptAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await InterceptAsync(HotelEndPoint.Parse(host, port), cancellationToken).ConfigureAwait(false);
    }
    public async Task InterceptAsync(HotelEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        _isIntercepting = true;
        cancellationToken.Register(_cancelInterception);

        int interceptCount = 0;
        while (!IsConnected && _isIntercepting)
        {
            try
            {
                Local = await HNode.AcceptAsync(ListenPort, cancellationToken).ConfigureAwait(false);
                if (!_isIntercepting) break;

                if (++interceptCount == SocketSkip)
                {
                    interceptCount = 0;
                    continue;
                }

                bool wasDetermined = await Local.DetermineFormatsAsync(cancellationToken).ConfigureAwait(false);
                if (!_isIntercepting) break;

                if (Local.IsWebSocket)
                {
                    await Local.UpgradeWebSocketAsServerAsync(Certificate).ConfigureAwait(false);
                }
                else if (!wasDetermined)
                {
                    using IMemoryOwner<byte> receiveBufferOwner = MemoryPool<byte>.Shared.Rent(512);
                    int received = await Local.ReceiveAsync(receiveBufferOwner.Memory, cancellationToken).ConfigureAwait(false);
                    if (!_isIntercepting) break;

                    if (!receiveBufferOwner.Memory.Span.Slice(0, received).SequenceEqual(_crossDomainPolicyRequestBytes))
                    {
                        ThrowHelper.ThrowNotSupportedException("Expected cross-domain policy request.");
                    }

                    await Local.SendAsync(_crossDomainPolicyResponseBytes, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var args = new ConnectedEventArgs(this, endpoint);
                OnConnected(args);

                endpoint = args.HotelServer ?? endpoint;
                if (endpoint == null)
                {
                    endpoint = await args.HotelServerSource.Task.ConfigureAwait(false);
                }

                if (args.IsFakingPolicyRequest)
                {
                    using var tempRemote = await HNode.ConnectAsync(endpoint).ConfigureAwait(false);
                    await tempRemote.SendAsync(_crossDomainPolicyRequestBytes, cancellationToken).ConfigureAwait(false);
                }

                Remote = await HNode.ConnectAsync(endpoint).ConfigureAwait(false);
                IsConnected = !Local.IsWebSocket || await Remote.UpgradeWebSocketAsClientAsync().ConfigureAwait(false);

                _outSteps = 0;
                _ = InterceptOutgoingAsync();

                _inSteps = 0;
                _ = InterceptIncomingAsync();
            }
            finally
            {
                if (!IsConnected)
                {
                    Local?.Dispose();
                    Remote?.Dispose();
                }
            }
        }
        _isIntercepting = false;
    }

    public async Task SendToClientAsync(HPacket packet, CancellationToken cancellationToken = default)
    {
        if (Local == null)
        {
            ThrowHelper.ThrowNullReferenceException();
        }
        await Local.SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }
    public async Task SendToServerAsync(HPacket packet, CancellationToken cancellationToken = default)
    {
        if (Remote == null)
        {
            ThrowHelper.ThrowNullReferenceException();
        }
        await Remote.SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    private async Task InterceptOutgoingAsync()
    {
        if (Local == null)
        {
            ThrowHelper.ThrowNullReferenceException();
        }

        HPacket packet = await Local.ReceivePacketAsync(SendFormat).ConfigureAwait(false);
        if (packet == null)
        {
            ThrowHelper.ThrowNullReferenceException();
        }

        var args = new DataInterceptedEventArgs(packet, ++_outSteps, true, InterceptOutgoingAsync, ServerRelayer);
        OnDataOutgoing(args);

        if (!args.IsBlocked && !args.WasRelayed)
        {
            await SendToServerAsync(args.Packet).ConfigureAwait(false);
        }
        if (!args.HasContinued)
        {
            if (args.WaitUntil != null)
            {
                await args.WaitUntil.ConfigureAwait(false);
            }
            args.Continue();
        }
    }
    private async Task InterceptIncomingAsync()
    {
        if (Remote == null)
        {
            ThrowHelper.ThrowNullReferenceException();
        }

        HPacket packet = await Remote.ReceivePacketAsync(ReceiveFormat).ConfigureAwait(false);
        if (packet == null)
        {
            ThrowHelper.ThrowNullReferenceException();
        }

        var args = new DataInterceptedEventArgs(packet, ++_inSteps, false, InterceptIncomingAsync, ClientRelayer);
        OnDataIncoming(args);

        if (!args.IsBlocked && !args.WasRelayed)
        {
            await SendToClientAsync(args.Packet).ConfigureAwait(false);
        }
        if (!args.HasContinued)
        {
            args.Continue();
        }
    }

    private Task ClientRelayer(DataInterceptedEventArgs relayedFrom) => SendToClientAsync(relayedFrom.Packet);
    private Task ServerRelayer(DataInterceptedEventArgs relayedFrom) => SendToServerAsync(relayedFrom.Packet);

    private void CancelInterception() => _isIntercepting = false;

    public void Dispose()
    {
        Disconnect();
    }
    public void Disconnect()
    {
        if (!Monitor.TryEnter(_disconnectLock)) return;
        try
        {
            _isIntercepting = false;
            if (Local != null)
            {
                Local.Dispose();
                Local = null;
            }
            if (Remote != null)
            {
                Remote.Dispose();
                Remote = null;
            }
            if (IsConnected)
            {
                IsConnected = false;
                OnDisconnected(EventArgs.Empty);
            }
        }
        finally { Monitor.Exit(_disconnectLock); }
    }
}