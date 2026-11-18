using System;
using System.Collections.Generic;
using Unity.Entities;

namespace MarqueeDelete
{
    /// <summary>
    /// Which of the batches the game is still processing has been observed as finished.
    /// </summary>
    internal enum BatchAdvance
    {
        Wait,
        Advance,
        TimedOut,
    }

    /// <summary>
    /// Sizes the next demolition batch from the measured main-thread time of the previous one
    /// instead of a fixed item count, so a slow frame shortens the batch before it stalls.
    /// </summary>
    internal static class BatchWindowPolicy
    {
        internal const int MinBatchLength = 1;
        internal const int DefaultBudgetMs = 8;

        /// <summary>
        /// Returns the length to try next. A measurement within 25% of the budget keeps the
        /// current length so the batch size does not oscillate around the budget.
        /// </summary>
        internal static int NextLength(int maxLength, int lastLength, int measuredMs, int budgetMs)
        {
            if (maxLength < MinBatchLength)
            {
                return MinBatchLength;
            }

            int last = lastLength;
            if (last < MinBatchLength) last = MinBatchLength;
            if (last > maxLength) last = maxLength;

            if (measuredMs <= 0)
            {
                return last;
            }

            int budget = budgetMs < 1 ? 1 : budgetMs;
            long scaled = (long)last * budget / measuredMs;
            if (scaled < MinBatchLength) scaled = MinBatchLength;
            if (scaled > maxLength) scaled = maxLength;
            int next = (int)scaled;

            if (Math.Abs(next - last) * 4 <= last)
            {
                return last;
            }

            return next;
        }

        /// <summary>
        /// Decides whether the batch loop may submit again. <paramref name="outstanding"/> is the
        /// number of entities the previous batch asked the game to delete; the loop only advances
        /// once the game has actually removed them, or once the observation deadline passes.
        /// </summary>
        internal static BatchAdvance Evaluate(int outstanding, bool allGone, float now, float deadline)
        {
            if (outstanding <= 0 || allGone)
            {
                return BatchAdvance.Advance;
            }

            return now >= deadline ? BatchAdvance.TimedOut : BatchAdvance.Wait;
        }
    }

    /// <summary>
    /// Collects the remainder of a running batch so every pause entry keeps the same work.
    /// </summary>
    internal static class InterruptedCapture
    {
        /// <summary>
        /// Fills <paramref name="units"/> and <paramref name="nodes"/> with the indices of the
        /// still-live targets that have not been submitted yet, and returns how many were kept.
        /// The node floor mirrors the batch loop: once every unit has been submitted the node
        /// pass starts at the first deferred node.
        /// </summary>
        internal static void Capture(
            int nextUnit,
            int unitCount,
            bool[] unitAlive,
            bool[] nodeAlive,
            List<int> units,
            List<int> nodes,
            out int unitRemainder,
            out int nodeRemainder)
        {
            unitRemainder = 0;
            nodeRemainder = 0;
            units?.Clear();
            nodes?.Clear();

            int start = nextUnit < 0 ? 0 : nextUnit;
            if (unitAlive != null)
            {
                for (int i = start; i < unitCount && i < unitAlive.Length; i++)
                {
                    if (!unitAlive[i]) continue;
                    units?.Add(i);
                    unitRemainder++;
                }
            }

            if (nodeAlive != null)
            {
                int floor = FirstNodeIndex(start, unitCount);
                for (int i = floor; i < nodeAlive.Length; i++)
                {
                    if (!nodeAlive[i]) continue;
                    nodes?.Add(i);
                    nodeRemainder++;
                }
            }
        }

        /// <summary>Index inside the deferred node list where the node pass starts.</summary>
        internal static int FirstNodeIndex(int nextUnit, int unitCount)
        {
            return nextUnit > unitCount ? nextUnit - unitCount : 0;
        }
    }

    /// <summary>
    /// Counts the facility units a leftover list still covers, so a pause can report "N facilities,
    /// largest M entities" without shipping the whole grouping through the UI binding.
    /// </summary>
    internal static class UnitRemainder
    {
        /// <summary>
        /// <paramref name="itemUnit"/> maps each leftover item to the index of its unit
        /// (-1 when it has none) and <paramref name="unitSize"/> holds each unit's entity count.
        /// </summary>
        internal static void Count(
            int[] itemUnit,
            int itemCount,
            int[] unitSize,
            int unitTotal,
            out int unitCount,
            out int largestUnit)
        {
            unitCount = 0;
            largestUnit = 0;
            if (itemUnit == null || unitSize == null || itemCount <= 0 || unitTotal <= 0)
            {
                return;
            }

            int max = itemCount < itemUnit.Length ? itemCount : itemUnit.Length;
            for (int i = 0; i < max; i++)
            {
                int unit = itemUnit[i];
                if (unit < 0 || unit >= unitTotal || unit >= unitSize.Length) continue;

                // Each unit is counted once even when several of its members are still pending.
                if (IsFirstMention(itemUnit, i, unit, max)) unitCount++;

                int size = unitSize[unit];
                if (size > largestUnit) largestUnit = size;
            }
        }

        private static bool IsFirstMention(int[] itemUnit, int index, int unit, int count)
        {
            for (int i = 0; i < index && i < count; i++)
            {
                if (itemUnit[i] == unit) return false;
            }

            return true;
        }
    }

    /// <summary>How a recorded owner pose resolved against the entities that exist now.</summary>
    internal enum OwnerMatchStatus
    {
        Unique,
        Ambiguous,
        NotFound,
        OccupiedBefore,
    }

    internal struct OwnerCandidate
    {
        internal int Slot;
        internal float X;
        internal float Y;
        internal float Z;
        internal float QX;
        internal float QY;
        internal float QZ;
        internal float QW;
        internal bool PreExisting;
    }

    /// <summary>
    /// Resolves the host an undo should attach sub-objects to. Attachment is only allowed when the
    /// pose resolves to exactly one entity that this undo created: several matches (a same-prefab
    /// building standing on the same spot) and a pose that was already occupied before the undo
    /// step both refuse, because attaching there would graft upgrades onto somebody else's building.
    /// </summary>
    internal static class OwnerMatchPolicy
    {
        internal const float PositionTolerance = 0.05f;
        internal const float RotationTolerance = 0.9995f;

        internal static OwnerMatchStatus Resolve(
            OwnerCandidate[] candidates,
            int count,
            float x,
            float y,
            float z,
            float qx,
            float qy,
            float qz,
            float qw,
            out int slot,
            out int matches)
        {
            slot = -1;
            matches = 0;
            if (candidates == null || count <= 0)
            {
                return OwnerMatchStatus.NotFound;
            }

            int limit = count < candidates.Length ? count : candidates.Length;
            int firstMatch = -1;
            for (int i = 0; i < limit; i++)
            {
                OwnerCandidate candidate = candidates[i];
                float dx = candidate.X - x;
                float dy = candidate.Y - y;
                float dz = candidate.Z - z;
                if (dx * dx + dy * dy + dz * dz > PositionTolerance * PositionTolerance)
                {
                    continue;
                }

                float dot = candidate.QX * qx + candidate.QY * qy + candidate.QZ * qz + candidate.QW * qw;
                if (dot < 0f) dot = -dot;
                if (dot < RotationTolerance)
                {
                    continue;
                }

                matches++;
                if (firstMatch < 0)
                {
                    firstMatch = i;
                }
            }

            if (matches == 0)
            {
                return OwnerMatchStatus.NotFound;
            }

            if (matches > 1)
            {
                return OwnerMatchStatus.Ambiguous;
            }

            if (candidates[firstMatch].PreExisting)
            {
                return OwnerMatchStatus.OccupiedBefore;
            }

            // The slot is an index into the (already compacted) candidate array this method was given,
            // so the caller can dereference it directly. OwnerCandidate.Slot is the candidate's index in
            // the unfiltered source list and is only its own payload, never a position here.
            slot = firstMatch;
            return OwnerMatchStatus.Unique;
        }
    }

    /// <summary>Whether a rebuilt network segment still joins the junctions it was captured on.</summary>
    internal enum NetLinkStatus
    {
        Reconnected,
        ReconnectedReversed,
        Moved,
        ForeignSplice,
    }

    /// <summary>
    /// Verifies a restored segment against the endpoints recorded before the demolition. A segment
    /// that ends somewhere else, or that picked up a neighbour the original did not have, is
    /// reported instead of being counted as a clean reconnect.
    /// </summary>
    internal static class NetConnectivityPolicy
    {
        internal const float EndpointTolerance = 0.5f;

        internal static NetLinkStatus Evaluate(
            float recordedStartX,
            float recordedStartZ,
            float recordedEndX,
            float recordedEndZ,
            float actualStartX,
            float actualStartZ,
            float actualEndX,
            float actualEndZ,
            int recordedAdjacentEdges,
            int actualAdjacentEdges,
            float tolerance)
        {
            if (tolerance <= 0f) tolerance = EndpointTolerance;

            bool forward = Near(recordedStartX, recordedStartZ, actualStartX, actualStartZ, tolerance) &&
                           Near(recordedEndX, recordedEndZ, actualEndX, actualEndZ, tolerance);
            bool reversed = Near(recordedStartX, recordedStartZ, actualEndX, actualEndZ, tolerance) &&
                            Near(recordedEndX, recordedEndZ, actualStartX, actualStartZ, tolerance);

            if (!forward && !reversed)
            {
                return NetLinkStatus.Moved;
            }

            // The shape is right but the junction gained a neighbour that the capture did not have:
            // the segment was spliced into a road that changed while it was gone.
            if (actualAdjacentEdges > recordedAdjacentEdges)
            {
                return NetLinkStatus.ForeignSplice;
            }

            return reversed ? NetLinkStatus.ReconnectedReversed : NetLinkStatus.Reconnected;
        }

        /// <summary>
        /// Checks one endpoint on its own, for a segment that is being rebuilt after its own edge was
        /// removed. Because the capture count still included that edge, a junction that is back at or
        /// above its recorded count has been re-linked by something else, and rebuilding this segment
        /// would double the connection.
        /// </summary>
        internal static NetLinkStatus EvaluateEndpoint(
            float recordedX,
            float recordedZ,
            float actualX,
            float actualZ,
            int recordedAdjacentEdges,
            int actualAdjacentEdges,
            float tolerance)
        {
            if (tolerance <= 0f) tolerance = EndpointTolerance;

            if (!Near(recordedX, recordedZ, actualX, actualZ, tolerance))
            {
                return NetLinkStatus.Moved;
            }

            if (recordedAdjacentEdges >= 0 && actualAdjacentEdges >= recordedAdjacentEdges)
            {
                return NetLinkStatus.ForeignSplice;
            }

            return NetLinkStatus.Reconnected;
        }

        private static bool Near(float ax, float az, float bx, float bz, float tolerance)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz <= tolerance * tolerance;
        }
    }

    /// <summary>
    /// Whether the lanes of a road may be treated as coming back with that road.
    ///
    /// The game keeps no lane identity: a lane is an entity whose reference lives in the road's
    /// <c>SubLane</c> buffer and dies with the road, so the entity that comes back is a new one and
    /// there is nothing to compare it against. What can be read is the ownership relation, where a lane
    /// names a road as its <c>Owner</c>, and that is only recorded for some networks. So this never
    /// claims to recognise a lane: it refuses a segment one of whose lanes names another road, refuses
    /// one whose counts could not be read, and otherwise leaves the decision to the junction evidence
    /// <see cref="NetConnectivityPolicy"/> already requires. Lanes that name nobody are not treated as
    /// evidence against the road, because that would refuse every network the game leaves unowned.
    /// </summary>
    internal enum LaneOwnershipStatus
    {
        /// <summary>Every captured lane named this road as its owner: rebuild and let lanes regenerate.</summary>
        RegeneratesWithRoad = 0,

        /// <summary>The lane counts could not be read, so nothing about the lanes can be asserted.</summary>
        MissingEvidence = 1,

        /// <summary>A captured lane says it belongs to another road: rebuilding would take it over.</summary>
        UnconfirmedOwnership = 2,

        /// <summary>
        /// The lanes carry no owner at all, which is what the game does for most networks. There is
        /// therefore nothing to confirm or contradict, and the junction evidence alone decides.
        /// </summary>
        NoOwnershipRecorded = 3,
    }

    internal static class LaneOwnershipPolicy
    {
        internal const int UnknownLaneCount = -1;

        /// <summary>
        /// <paramref name="laneCount"/> is how many lanes the road had at capture
        /// (<see cref="UnknownLaneCount"/> when it could not be read), <paramref name="laneOwned"/>
        /// how many named this road as their owner, and <paramref name="laneForeign"/> how many named
        /// some other road. Only a positive claim of another owner refuses the rebuild: a lane that
        /// names nobody is not evidence against this road.
        /// </summary>
        internal static LaneOwnershipStatus Decide(int laneCount, int laneOwned, int laneForeign)
        {
            if (laneCount < 0 || laneOwned < 0 || laneForeign < 0)
            {
                return LaneOwnershipStatus.MissingEvidence;
            }

            if (laneForeign > 0)
            {
                return LaneOwnershipStatus.UnconfirmedOwnership;
            }

            // A path with no lanes is a complete answer, not a missing one.
            if (laneCount == 0 || laneOwned >= laneCount)
            {
                return LaneOwnershipStatus.RegeneratesWithRoad;
            }

            return LaneOwnershipStatus.NoOwnershipRecorded;
        }

        /// <summary>
        /// Whether the road may be rebuilt. Unreadable counts and a lane that belongs elsewhere both
        /// refuse; lanes that name nobody fall back to the junction evidence, which the caller has
        /// already required, because refusing them would remove every road the game keeps no owner for.
        /// </summary>
        internal static bool AllowsRebuild(int laneCount, int laneOwned, int laneForeign)
        {
            LaneOwnershipStatus status = Decide(laneCount, laneOwned, laneForeign);
            return status == LaneOwnershipStatus.RegeneratesWithRoad ||
                   status == LaneOwnershipStatus.NoOwnershipRecorded;
        }
    }

    /// <summary>
    /// Refuses a restore whose lot is already taken by something else, so an undo never stacks a
    /// building on top of a replacement that grew back in the meantime.
    /// </summary>
    internal static class OccupancyPolicy
    {
        internal const float OverlapRadius = 2f;

        /// <summary>
        /// Returns the index of the first occupant that overlaps the planned position, or -1.
        /// </summary>
        internal static int FirstConflict(
            float x,
            float z,
            float radius,
            float[] xs,
            float[] zs,
            int count,
            int skipIndex)
        {
            if (xs == null || zs == null || count <= 0)
            {
                return -1;
            }

            if (radius <= 0f) radius = OverlapRadius;
            float limit = radius * radius;
            int max = count;
            if (max > xs.Length) max = xs.Length;
            if (max > zs.Length) max = zs.Length;
            for (int i = 0; i < max; i++)
            {
                if (i == skipIndex) continue;
                float dx = xs[i] - x;
                float dz = zs[i] - z;
                if (dx * dx + dz * dz <= limit)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// The decision table every write path is gated on, expressed as a pure function of the detected
    /// tier so the same predicate the game runs can be exercised offline for all three tiers.
    /// The tier values mirror <c>CompatibilityTier</c> (Unavailable 0, PreviewOnly 1, Verified 2); the
    /// int form keeps this file free of the reflection-based compatibility layer so it can be compiled
    /// into the offline regression harness.
    /// </summary>
    internal static class CompatibilityGate
    {
        internal const int TierUnavailable = 0;
        internal const int TierPreviewOnly = 1;
        internal const int TierVerified = 2;

        /// <summary>Destructive writes need a verified build; preview-only is read-only.</summary>
        internal static bool AllowsDeletionSubmit(int tier) => tier == TierVerified;

        /// <summary>
        /// Rebuilding entities is a write as well, so it needs the same verified build as a delete.
        /// Preview-only may inspect what an undo would restore but never emits it.
        /// </summary>
        internal static bool AllowsUndoRebuild(int tier) => tier == TierVerified;

        /// <summary>The tool may run when search and pick is not missing.</summary>
        internal static bool AllowsRun(int tier) => tier > TierUnavailable;
    }

    /// <summary>
    /// The single write gateway. Both the delete path and the undo path decide here, and the decision
    /// is made before any command is written, so a refused tier leaves the command buffer untouched
    /// instead of half-filled. The offline regression runs this same function against a real plan and
    /// a real (stand-in) command buffer, which is what proves "refused means nothing was emitted".
    /// </summary>
    internal static class DeletionGate
    {
        internal const string ReasonVerifiedOnly = "tier-not-verified";
        internal const string ReasonUnavailable = "tier-unavailable";

        internal static bool TryClaimSubmit(int tier, out string reason)
        {
            if (CompatibilityGate.AllowsDeletionSubmit(tier))
            {
                reason = string.Empty;
                return true;
            }

            reason = tier == CompatibilityGate.TierUnavailable ? ReasonUnavailable : ReasonVerifiedOnly;
            return false;
        }

        internal static bool TryClaimRebuild(int tier, out string reason)
        {
            if (CompatibilityGate.AllowsUndoRebuild(tier))
            {
                reason = string.Empty;
                return true;
            }

            reason = tier == CompatibilityGate.TierUnavailable ? ReasonUnavailable : ReasonVerifiedOnly;
            return false;
        }

        /// <summary>
        /// Writes the plan only after the tier allows it. Returns false with an empty command buffer
        /// when the tier refuses, and the caller pauses without advancing its cursor.
        /// </summary>
        internal static bool Submit(int tier, EntityManager entities, EntityCommandBuffer commands,
            DeletionPlan plan, out int updatedCount, out string reason)
        {
            updatedCount = 0;
            if (!TryClaimSubmit(tier, out reason))
            {
                return false;
            }

            Cascade.Submit(entities, commands, plan, out updatedCount);
            return true;
        }
    }
}
