using System.Text;

using Sulakore.Network;
using Sulakore.Network.Buffers;
using Sulakore.Network.Formats;

namespace Tangine.Network;

public sealed class HConnection : IHConnection
{
    private static readonly byte[] _crossDomainPolicyRequestBytes, _crossDomainPolicyResponseBytes;

    private readonly object _disconnectLock;

    private int _inSteps, _outSteps;
    private CancellationTokenSource? _interceptCancellationSource;

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

    static HConnection()
    {
        _crossDomainPolicyRequestBytes = Encoding.UTF8.GetBytes("<policy-file-request/>\0");
        _crossDomainPolicyResponseBytes = Encoding.UTF8.GetBytes("<cross-domain-policy><allow-access-from domain=\"*\" to-ports=\"*\"/></cross-domain-policy>\0");
    }
    public HConnection()
    {
        _disconnectLock = new object();
    }

    public async Task InterceptAsync(HotelEndPoint endpoint, HConnectionOptions options = default, CancellationToken cancellationToken = default)
    {
        CancelAndNullifySource(ref _interceptCancellationSource);

        _interceptCancellationSource = new CancellationTokenSource();
        CancellationTokenSource? linkedInterceptCancellationSource = null;

        if (cancellationToken != default)
        {
            linkedInterceptCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(_interceptCancellationSource.Token, cancellationToken);
            cancellationToken = linkedInterceptCancellationSource.Token; // This token will also 'cancel' when the Dispose or Disconnect method of this type is called.
        }

        try
        {
            int listenSkipAmount = options.ListenSkipAmount;
            while (!IsConnected && !cancellationToken.IsCancellationRequested)
            {
                Local = await HNode.AcceptAsync(ListenPort, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested) return;
                if (--listenSkipAmount > 0)
                {
                    Local.Dispose();
                    continue;
                }

                if (options.IsUsingWebSockets)
                {
                    if (options.Certificate == null)
                    {
                        ThrowHelper.ThrowNullReferenceException("No certificate was provided for local authentication using the WebSocket Secure protocol.");
                    }
                    await Local.UpgradeWebSocketAsServerAsync(options.Certificate, cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) return;
                }

                // We should 'Peek' the incoming bytes from the local client, and determine whether we should mimic the policy request.
                // Options.PeekAmount / Options.IsPolicyRequest(Bytes)
                //if (true)
                //{
                //    using IMemoryOwner<byte> receiveBufferOwner = MemoryPool<byte>.Shared.Rent(512);
                //    int received = await Local.ReceiveAsync(receiveBufferOwner.Memory, cancellationToken).ConfigureAwait(false);
                //    if (cancellationToken.IsCancellationRequested) return;

                //    if (!receiveBufferOwner.Memory.Span.Slice(0, received).SequenceEqual(_crossDomainPolicyRequestBytes))
                //    {
                //        ThrowHelper.ThrowNotSupportedException("Expected cross-domain policy request.");
                //    }

                //    await Local.SendAsync(_crossDomainPolicyResponseBytes, cancellationToken).ConfigureAwait(false);

                //    Local.Dispose();
                //    continue;
                //}

                var args = new ConnectedEventArgs(this, endpoint);

                OnConnected(args);
                if (args.Cancel || cancellationToken.IsCancellationRequested) return;

                endpoint = args.HotelServer ?? endpoint;
                if (endpoint == null)
                {
                    endpoint = await args.HotelServerSource.Task.ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) return;
                }

                if (args.IsFakingPolicyRequest)
                {
                    using var tempRemote = await HNode.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) return;

                    await tempRemote.SendAsync(_crossDomainPolicyRequestBytes, cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) return;
                }

                Remote = await HNode.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) return;

                if (options.IsUsingWebSockets)
                {
                    await Remote.UpgradeWebSocketAsClientAsync(cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested) return;
                }

                IsConnected = Local.IsConnected && Remote.IsConnected;
                if (options.IsUsingWebSockets)
                {
                    IsConnected = options.IsUsingWebSockets && Local.IsUpgraded && Remote.IsUpgraded;
                }

                // Last chance to cancel.
                if (cancellationToken.IsCancellationRequested) return;

                _outSteps = 0;
                _ = InterceptOutgoingAsync();

                _inSteps = 0;
                _ = InterceptIncomingAsync();
            }
        }
        finally
        {
            if (!IsConnected || cancellationToken.IsCancellationRequested)
            {
                Local?.Dispose();
                Remote?.Dispose();
            }
            CancelAndNullifySource(ref _interceptCancellationSource);
            CancelAndNullifySource(ref linkedInterceptCancellationSource);
        }
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

        using HPacket packet = await Local.ReceivePacketAsync(SendFormat).ConfigureAwait(false);
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

        using HPacket packet = await Remote.ReceivePacketAsync(ReceiveFormat).ConfigureAwait(false);
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

    private static void CancelAndNullifySource(ref CancellationTokenSource? cancellationTokenSource)
    {
        if (cancellationTokenSource == null) return;
        if (!cancellationTokenSource.IsCancellationRequested)
        {
            cancellationTokenSource.Cancel();
        }
        cancellationTokenSource.Dispose();
        cancellationTokenSource = null;
    }

    public void Dispose()
    {
        Disconnect();
    }
    public void Disconnect()
    {
        if (!Monitor.TryEnter(_disconnectLock)) return;
        try
        {
            CancelAndNullifySource(ref _interceptCancellationSource);
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