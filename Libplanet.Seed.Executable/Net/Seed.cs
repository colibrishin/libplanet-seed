﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Crypto;
using Libplanet.Net;
using Libplanet.Net.Messages;
using Libplanet.Net.Transports;
using Nito.AsyncEx;
using Serilog;

namespace Libplanet.Seed.Executable.Net
{
    public class Seed
    {
        private readonly int _maximumPeersToRefresh;
        private readonly TimeSpan _refreshInterval;
        private readonly TimeSpan _peerLifetime;
        private readonly TimeSpan _pingTimeout;

        private readonly ITransport _transport;
        private readonly CancellationTokenSource _runtimeCancellationTokenSource;
        private readonly ILogger _logger;

        public Seed(
            IPrivateKey privateKey,
            string? host,
            int? port,
            int workers,
            IceServer[] iceServers,
            AppProtocolVersion appProtocolVersion,
            int maximumPeersToToRefresh,
            TimeSpan refreshInterval,
            TimeSpan peerLifetime,
            TimeSpan pingTimeout)
        {
            _maximumPeersToRefresh = maximumPeersToToRefresh;
            _refreshInterval = refreshInterval;
            _peerLifetime = peerLifetime;
            _pingTimeout = pingTimeout;
            _runtimeCancellationTokenSource = new CancellationTokenSource();
            _transport = new NetMQTransport(
                        privateKey,
                        appProtocolVersion,
                        null,
                        workers: workers,
                        host: host,
                        listenPort: port,
                        iceServers: iceServers,
                        differentAppProtocolVersionEncountered: null);
            PeerInfos = new ConcurrentDictionary<Address, PeerInfo>();
            _transport.ProcessMessageHandler.Register(ReceiveMessageAsync);

            _logger = Log.ForContext<Seed>();
        }

        public ConcurrentDictionary<Address, PeerInfo> PeerInfos { get; }

        private IEnumerable<BoundPeer> Peers =>
            PeerInfos.Values.Select(peerState => peerState.BoundPeer);

        public async Task StartAsync(
            HashSet<BoundPeer> staticPeers,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task>
            {
                StartTransportAsync(cancellationToken),
                RefreshTableAsync(cancellationToken),
            };
            if (staticPeers.Any())
            {
                tasks.Add(CheckStaticPeersAsync(staticPeers, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        public async Task StopAsync(TimeSpan waitFor)
        {
            await _transport.StopAsync(waitFor);
        }

        private async Task<Task> StartTransportAsync(CancellationToken cancellationToken)
        {
            Task task = _transport.StartAsync(cancellationToken);
            await _transport.WaitForRunningAsync();
            return task;
        }

        private async Task ReceiveMessageAsync(Message message)
        {
            switch (message)
            {
                case PingMsg ping:
                    var pong = new PongMsg { Identity = ping.Identity };
                    await _transport.ReplyMessageAsync(pong, _runtimeCancellationTokenSource.Token);

                    break;

                case FindNeighborsMsg findNeighbors:
                    var neighbors = new NeighborsMsg(Peers) { Identity = findNeighbors.Identity };
                    await _transport.ReplyMessageAsync(
                        neighbors,
                        _runtimeCancellationTokenSource.Token);
                    break;
            }

            if (message.Remote is BoundPeer boundPeer)
            {
                AddOrUpdate(boundPeer);
            }
        }

        private async Task AddPeersAsync(
            BoundPeer[] peers,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            IEnumerable<Task> tasks = peers.Select(async peer =>
                {
                    try
                    {
                        var ping = new PingMsg();
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        Message? reply = await _transport.SendMessageAsync(
                            peer,
                            ping,
                            timeout,
                            cancellationToken);
                        TimeSpan elapsed = stopwatch.Elapsed;
                        stopwatch.Stop();

                        if (reply is PongMsg)
                        {
                            AddOrUpdate(peer, elapsed);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Information(
                            "Operation canceled during {FName}().",
                            nameof(AddPeersAsync),
                            peer);
                        throw;
                    }
                    catch (Exception e)
                    {
                        _logger.Error(
                            e,
                            "Unexpected error occurred during {FName} to {Peer}.",
                            nameof(AddPeersAsync),
                            peer);
                    }
                });

            await tasks.WhenAll();
        }

        private PeerInfo AddOrUpdate(BoundPeer peer, TimeSpan? latency = null)
        {
            PeerInfo peerInfo;
            peerInfo.BoundPeer = peer;
            peerInfo.LastUpdated = DateTimeOffset.UtcNow;
            peerInfo.Latency = latency;
            return PeerInfos.AddOrUpdate(
                peer.Address,
                peerInfo,
                (address, info) =>
                {
                    peerInfo.Latency = latency ?? info.Latency;
                    return peerInfo;
                });
        }

        private async Task RefreshTableAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // FIXME: Ordered selection of peers may cause some peers does not refreshed
                    // forever.
                    await Task.Delay(_refreshInterval, cancellationToken);
                    BoundPeer[] peersToUpdate = PeerInfos.Values
                        .Where(
                            peerState => DateTimeOffset.UtcNow - peerState.LastUpdated >
                                         _peerLifetime)
                        .Select(state => state.BoundPeer)
                        .Take(_maximumPeersToRefresh)
                        .ToArray();
                    _logger.Debug(
                        "Refreshing peers in table. (Total: {Total}, Candidate: {Candidate})",
                        Peers.Count(),
                        peersToUpdate.Length);
                    if (peersToUpdate.Any())
                    {
                        await AddPeersAsync(
                            peersToUpdate.ToArray(),
                            _pingTimeout,
                            cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Information(
                        "Operation canceled during {FName}().",
                        nameof(RefreshTableAsync));
                    throw;
                }
                catch (Exception e)
                {
                    Log.Warning(
                        e,
                        "Unexpected exception occurred during {FName}().",
                        nameof(RefreshTableAsync));
                }
            }
        }

        private async Task CheckStaticPeersAsync(
            IEnumerable<BoundPeer> peers,
            CancellationToken cancellationToken)
        {
            var boundPeers = peers as BoundPeer[] ?? peers.ToArray();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    Log.Warning("Checking static peers. {@Peers}", boundPeers);
                    var peersToAdd = boundPeers.Where(peer => !Peers.Contains(peer)).ToArray();
                    if (peersToAdd.Any())
                    {
                        Log.Warning("Some of peers are not in routing table. {@Peers}", peersToAdd);
                        await AddPeersAsync(
                            peersToAdd,
                            _pingTimeout,
                            cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Information(
                        "Operation canceled during {FName}().",
                        nameof(CheckStaticPeersAsync));
                    throw;
                }
                catch (Exception e)
                {
                    Log.Warning(
                        e,
                        "Unexpected exception occurred during {FName}().",
                        nameof(CheckStaticPeersAsync));
                }
            }
        }
    }
}
