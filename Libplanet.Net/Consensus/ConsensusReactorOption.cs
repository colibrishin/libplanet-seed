using System;
using System.Collections.Immutable;
using Libplanet.Crypto;
using Libplanet.Net.Messages;
using Libplanet.Net.Transports;

namespace Libplanet.Net.Consensus
{
    /// <summary>
    /// A option struct for initializing <see cref="ConsensusReactor{T}"/>.
    /// </summary>
    public struct ConsensusReactorOption
    {
        /// <summary>
        /// A port number that is used for exchanging <see cref="ConsensusMessage"/>.
        /// </summary>
        public int ConsensusPort { get; set; }

        /// <summary>
        /// A number of <see cref="ITransport"/>'s worker.
        /// </summary>
        public int ConsensusWorkers { get; set; }

        /// <summary>
        /// A <see cref="BlsPrivateKey"/> for signing block and message.
        /// </summary>
        public BlsPrivateKey ConsensusPrivateKey { get; set; }

        /// <summary>
        /// A list of validators.
        /// </summary>
        public ImmutableList<BoundPeer> ConsensusPeers { get; set; }

        /// <summary>
        /// A time delay in starting the consensus for the next height block.
        /// <seealso cref="ConsensusContext{T}.OnBlockChainTipChanged"/>
        /// </summary>
        public TimeSpan TargetBlockInterval { get; set; }
    }
}
