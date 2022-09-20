using System;
using System.Collections.Generic;
using System.Linq;
using Libplanet.Blocks;
using Libplanet.Crypto;
using Libplanet.Net.Consensus;

namespace Libplanet.Net.Messages
{
    /// <summary>
    /// A message class for <see cref="Libplanet.Net.Consensus.Step.Propose"/>.
    /// </summary>
    public class ConsensusPropose : ConsensusMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConsensusPropose"/> class.
        /// </summary>
        /// <param name="validator">
        /// A <see cref="BlsPublicKey"/> of the validator whe made this message.</param>
        /// <param name="height">A <see cref="Context{T}.Height"/> the message is for.</param>
        /// <param name="round">A <see cref="Context{T}.Round"/> the message is written for.</param>
        /// <param name="blockHash">A <see cref="BlockHash"/> the message is written for.</param>
        /// <param name="payload">A marshalled <see cref="Block{T}"/>.</param>
        /// <param name="validRound">A last successful
        /// <see cref="Libplanet.Net.Consensus.Step.PreVote"/> round.
        /// </param>
        public ConsensusPropose(
            BlsPublicKey validator,
            long height,
            int round,
            BlockHash blockHash,
            byte[] payload,
            int validRound)
        : base(validator, height, round, blockHash)
        {
            Payload = payload;
            ValidRound = validRound;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsensusPropose"/> class with marshalled
        /// message.
        /// </summary>
        /// <param name="dataframes">A marshalled message.</param>
        public ConsensusPropose(byte[][] dataframes)
        : base(dataframes)
        {
            Payload = dataframes[4];
            ValidRound = BitConverter.ToInt32(dataframes[5], 0);
        }

        /// <summary>
        /// A marshalled <see cref="Block{T}"/>.
        /// </summary>
        public byte[] Payload { get; }

        /// <summary>
        /// A last successful <see cref="Libplanet.Net.Consensus.Step.PreVote"/> round.
        /// </summary>
        public int ValidRound { get; }

        /// <inheritdoc cref="ConsensusMessage.DataFrames"/>
        public override IEnumerable<byte[]> DataFrames
        {
            get
            {
                var frames = new List<byte[]>
                {
                    Validator.KeyBytes.ToArray(),
                    BitConverter.GetBytes(Height),
                    BitConverter.GetBytes(Round),
                    BlockHash is { } blockHash ? blockHash.ToByteArray() : new[] { Nil },
                    Payload,
                    BitConverter.GetBytes(ValidRound),
                };
                return frames;
            }
        }

        /// <inheritdoc cref="Message.MessageType"/>
        public override MessageType Type => MessageType.ConsensusPropose;
    }
}
