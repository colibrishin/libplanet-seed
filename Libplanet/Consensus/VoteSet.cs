using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Libplanet.Blocks;
using Libplanet.Crypto;

namespace Libplanet.Consensus
{
    /// <summary>
    /// A collection of <see cref="Vote"/>.
    /// <see cref="VoteSet"/> is storing some <see cref="Vote"/> for elect vote.
    /// It is a reference to each PBFT round.
    /// </summary>
    public class VoteSet
    {
        // FIXME: Should separate prevote lock and commit vote lock?
        private readonly object _lock;
        private Dictionary<BlsPublicKey, Vote> _votes;

        /// <summary>
        /// Creates a <see cref="VoteSet"/> instance with its <paramref name="height"/>,
        /// <paramref name="round"/> and <paramref name="blockHash"/>.
        /// </summary>
        /// <param name="height">Collect target height.</param>
        /// <param name="round">Collect target round.</param>
        /// <param name="blockHash">Collect target value reference.</param>
        /// <param name="validatorSet">Set of all validator <see cref="PublicKey"/>.</param>
        public VoteSet(
            long height,
            int round,
            BlockHash? blockHash,
            IEnumerable<BlsPublicKey> validatorSet)
        {
            Height = height;
            Round = round;
            ValidatorSet = validatorSet.ToImmutableArray();

            // TODO: Order of validators should not depend on given validatorSet's order?
            _votes = Enumerable.Range(0, ValidatorSet.Length)
                .Select(x => new Vote(
                    height,
                    round,
                    blockHash,
                    DateTimeOffset.Now,
                    ValidatorSet[x],
                    VoteFlag.Null,
                    null))
                .ToDictionary(keySelector: x => x.Validator, elementSelector: x => x);

            _lock = new object();
        }

        /// <summary>
        /// Height of <see cref="VoteSet"/>'s target.
        /// </summary>
        public long Height { get; }

        /// <summary>
        /// Round of <see cref="VoteSet"/>'s target.
        /// </summary>
        public int Round { get; }

        /// <summary>
        /// A list of validator's <see cref="BlsPublicKey"/> of <see cref="VoteSet"/>'s target.
        /// </summary>
        public ImmutableArray<BlsPublicKey> ValidatorSet { get; }

        /// <summary>
        /// <see cref="Vote"/>s in this VoteSet.
        /// </summary>
        public ImmutableArray<Vote> Votes
        {
            // ToDictionary() does not keep the order of the source. Creates a new list that will be
            // same as the order of ValidatorSet.
            get => ValidatorSet.Select(publicKey => _votes[publicKey]).ToImmutableArray();
        }

        /// <summary>
        /// Add the <paramref name="vote"/> to the collection.
        /// </summary>
        /// <param name="vote">A <see cref="Vote"/> for add.</param>
        /// <returns><c>true</c> if the <paramref name="vote"/> is successfully added.
        /// <c>false</c> otherwise.</returns>
        public bool Add(Vote vote)
        {
            lock (_lock)
            {
                if (!IsVoteValid(vote))
                {
                    return false;
                }

                _votes[vote.Validator] = vote;
                return true;
            }
        }

        /// <summary>
        /// Check if the validators voted has more than +2/3 voting power.
        /// </summary>
        /// <returns><c>true</c> if +2/3 of voting power in the <see cref="ValidatorSet"/> voted.
        /// <c>false</c> otherwise.</returns>
        public bool HasTwoThirdAny()
        {
            var twoThird = ValidatorSet.Length * 2.0 / 3.0;
            return _votes.Count(x => !(x.Value.Signature is null)) > twoThird;
        }

        /// <summary>
        /// Check if the validators who voted about PreVote state have more than +2/3 voting power.
        /// </summary>
        /// <returns><c>true</c> if +2/3 of the voting power in the <see cref="ValidatorSet"/>
        /// voted about <see cref="VoteFlag.Absent"/>, <c>false</c> otherwise.</returns>
        public bool HasTwoThirdPrevote()
        {
            var twoThird = ValidatorSet.Length * 2.0 / 3.0;
            return _votes.Count(x =>
                !(x.Value.Signature is null) && x.Value.Flag == VoteFlag.Absent) > twoThird;
        }

        /// <summary>
        /// Check if the validators who voted about PreCommit state have more than +2/3 voting
        /// power.
        /// </summary>
        /// <returns><c>true</c> if +2/3 of the voting power in the <see cref="ValidatorSet"/> voted
        /// about <see cref="VoteFlag.Commit"/>, <c>false</c> otherwise.</returns>
        public bool HasTwoThirdCommit()
        {
            var twoThird = ValidatorSet.Length * 2.0 / 3.0;
            return _votes.Count(x =>
                !(x.Value.Signature is null) && x.Value.Flag == VoteFlag.Commit) > twoThird;
        }

        private bool IsVoteValid(Vote vote)
        {
            if (vote.Signature is null)
            {
                return false;
            }

            if (!vote.Verify(vote.Validator))
            {
                return false;
            }

            if (!ValidatorSet.Contains(vote.Validator))
            {
                // The voter is not a validator.
                return false;
            }

            if (vote.Height != Height)
            {
                return false;
            }

            if (vote.Round != Round)
            {
                return false;
            }

            if (vote.Flag < _votes[vote.Validator].Flag)
            {
                return false;
            }

            return true;
        }
    }
}
