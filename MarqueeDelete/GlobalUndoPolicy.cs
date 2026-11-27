using System;
using System.Collections.Generic;
using System.Text;

namespace MarqueeDelete
{
    public enum GlobalUndoActionKind
    {
        None = 0,
        BulkDelete = 1,
        Bulldoze = 2,
        Build = 3,
        UpgradeReplace = 4,
        Zone = 5,
        Terrain = 6,
        Mixed = 7,
    }

    public enum GlobalUndoExecutionPhase
    {
        Idle = 0,
        DeletingCreated = 1,
        WaitingPostDelete = 2,
        RestoringDeleted = 3,
        RestoringInPlaceNets = 4,
        RestoringZoneAndTerrain = 5,
        Completed = 6,
        WaitingPostInPlaceNets = 7,
    }

    public enum ZoneRestoreVerdict
    {
        Valid = 0,
        BlockMissing = 1,
        SizeMismatch = 2,
        EmptyOrCorrupt = 3,
    }

    public struct GlobalUndoTrackOptions : IEquatable<GlobalUndoTrackOptions>
    {
        public bool Enabled;
        public bool TrackBuild;
        public bool TrackBulldoze;
        public bool TrackUpgrade;
        public bool TrackZone;
        public bool TrackTerrain;

        public static GlobalUndoTrackOptions AllEnabled => new GlobalUndoTrackOptions
        {
            Enabled = true,
            TrackBuild = true,
            TrackBulldoze = true,
            TrackUpgrade = true,
            TrackZone = true,
            TrackTerrain = true,
        };

        public bool AnyCategoryEnabled =>
            TrackBuild || TrackBulldoze || TrackUpgrade || TrackZone || TrackTerrain;

        public bool Equals(GlobalUndoTrackOptions other) =>
            Enabled == other.Enabled &&
            TrackBuild == other.TrackBuild &&
            TrackBulldoze == other.TrackBulldoze &&
            TrackUpgrade == other.TrackUpgrade &&
            TrackZone == other.TrackZone &&
            TrackTerrain == other.TrackTerrain;

        public override bool Equals(object obj) => obj is GlobalUndoTrackOptions other && Equals(other);

        public override int GetHashCode()
        {
            int mask = 0;
            if (Enabled) mask |= 1;
            if (TrackBuild) mask |= 2;
            if (TrackBulldoze) mask |= 4;
            if (TrackUpgrade) mask |= 8;
            if (TrackZone) mask |= 16;
            if (TrackTerrain) mask |= 32;
            return mask;
        }

        public static bool operator ==(GlobalUndoTrackOptions left, GlobalUndoTrackOptions right) => left.Equals(right);
        public static bool operator !=(GlobalUndoTrackOptions left, GlobalUndoTrackOptions right) => !left.Equals(right);
    }

    public struct GlobalUndoStepSummary
    {
        public int StepId;
        public GlobalUndoActionKind Kind;
        public string ToolId;
        public int ItemCount;
        public int CreatedCount;
        public int DeletedCount;
        public int InPlaceNetCount;
        public int ZoneBlockCount;
        public int TerrainTileCount;
        public bool TerrainEvicted;
        public long CreatedUnixSeconds;
    }

    public struct NetPoint3
    {
        public float X;
        public float Y;
        public float Z;

        public NetPoint3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public struct NetBezier3
    {
        public NetPoint3 A;
        public NetPoint3 B;
        public NetPoint3 C;
        public NetPoint3 D;

        public NetBezier3(NetPoint3 a, NetPoint3 b, NetPoint3 c, NetPoint3 d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }

    public struct SplitChainCandidate
    {
        public int EdgeId;
        public int StartNodeId;
        public int EndNodeId;
        public int PrefabId;
        public int OwnerId;
        public NetPoint3 StartPos;
        public NetPoint3 MidPos;
        public NetPoint3 EndPos;
        public float Length;
    }

    public static class GlobalUndoPolicy
    {
        public const int MinHistoryDepth = 1;
        public const int MaxHistoryDepth = 20;
        public const int DefaultHistoryDepth = 10;

        private static readonly string[] s_ExternalUndoToolTokens =
        {
            "moveit",
            "move it",
            "move_it",
            "mit_tool",
            "mittool",
            "qmt_tool",
        };

        public static int ClampHistoryDepth(int configured)
        {
            if (configured < MinHistoryDepth) return MinHistoryDepth;
            if (configured > MaxHistoryDepth) return MaxHistoryDepth;
            return configured;
        }

        public static bool IsExternalUndoTool(string toolId, string toolTypeName)
        {
            if (MatchesAnyToken(toolId, s_ExternalUndoToolTokens))
            {
                return true;
            }
            if (MatchesAnyToken(toolTypeName, s_ExternalUndoToolTokens))
            {
                return true;
            }
            return false;
        }

        public static bool ShouldYieldCtrlZ(string activeToolId, string activeToolTypeName)
        {
            return IsExternalUndoTool(activeToolId, activeToolTypeName);
        }

        public static bool IsOwnMarqueeTool(string toolId, string toolTypeName)
        {
            if (!string.IsNullOrEmpty(toolId))
            {
                if (string.Equals(toolId, "Marquee Delete", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(toolId, "MarqueeDelete", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(toolTypeName) &&
                toolTypeName.IndexOf("MarqueeDeleteToolSystem", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return false;
        }

        public static bool ShouldCaptureForTool(
            in GlobalUndoTrackOptions options,
            string activeToolId,
            string activeToolTypeName,
            bool isReplayingUndo)
        {
            if (!options.Enabled || !options.AnyCategoryEnabled || isReplayingUndo)
            {
                return false;
            }

            if (IsOwnMarqueeTool(activeToolId, activeToolTypeName))
            {
                return false;
            }

            if (IsExternalUndoTool(activeToolId, activeToolTypeName))
            {
                return false;
            }

            return true;
        }

        public static bool IsKindTracked(in GlobalUndoTrackOptions options, GlobalUndoActionKind kind)
        {
            switch (kind)
            {
                case GlobalUndoActionKind.BulkDelete:
                    // Bulk Delete always maintains its own undo regardless of Global Undo toggle.
                    return true;
                case GlobalUndoActionKind.Bulldoze:
                    return options.Enabled && options.TrackBulldoze;
                case GlobalUndoActionKind.Build:
                    return options.Enabled && options.TrackBuild;
                case GlobalUndoActionKind.UpgradeReplace:
                    return options.Enabled && options.TrackUpgrade;
                case GlobalUndoActionKind.Zone:
                    return options.Enabled && options.TrackZone;
                case GlobalUndoActionKind.Terrain:
                    return options.Enabled && options.TrackTerrain;
                case GlobalUndoActionKind.Mixed:
                    return options.Enabled && options.AnyCategoryEnabled;
                default:
                    return false;
            }
        }

        public static bool IsTerrainToolId(string toolId)
        {
            return !string.IsNullOrEmpty(toolId) &&
                   toolId.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsZoneToolId(string toolId)
        {
            return !string.IsNullOrEmpty(toolId) &&
                   toolId.IndexOf("Zone", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsBulldozeToolId(string toolId)
        {
            return !string.IsNullOrEmpty(toolId) &&
                   toolId.IndexOf("Bulldoze", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsUpgradeToolId(string toolId)
        {
            return !string.IsNullOrEmpty(toolId) &&
                   toolId.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static GlobalUndoActionKind ClassifyActionKind(
            string toolId,
            bool hasCreated,
            bool hasDeleted,
            bool hasInPlaceUpgradeOrReplace,
            bool hasZone,
            bool hasTerrain)
        {
            return ClassifyActionKind(
                toolId,
                hasCreated,
                hasDeleted,
                hasSemanticUpgradeOrReplace: hasInPlaceUpgradeOrReplace,
                hasIncidentalNetAdjustment: false,
                hasZone: hasZone,
                hasTerrain: hasTerrain);
        }

        public static GlobalUndoActionKind ClassifyActionKind(
            string toolId,
            bool hasCreated,
            bool hasDeleted,
            bool hasSemanticUpgradeOrReplace,
            bool hasIncidentalNetAdjustment,
            bool hasZone,
            bool hasTerrain)
        {
            bool hasBuild = hasCreated || hasIncidentalNetAdjustment;
            if (!hasBuild && !hasDeleted && !hasSemanticUpgradeOrReplace && !hasZone && !hasTerrain)
            {
                return GlobalUndoActionKind.None;
            }

            if (IsTerrainToolId(toolId) && hasTerrain && !hasBuild && !hasDeleted && !hasSemanticUpgradeOrReplace && !hasZone)
            {
                return GlobalUndoActionKind.Terrain;
            }

            if (IsZoneToolId(toolId) && hasZone && !hasBuild && !hasDeleted && !hasSemanticUpgradeOrReplace && !hasTerrain)
            {
                return GlobalUndoActionKind.Zone;
            }

            if (IsBulldozeToolId(toolId) && hasDeleted && !hasBuild && !hasSemanticUpgradeOrReplace && !hasZone && !hasTerrain)
            {
                return GlobalUndoActionKind.Bulldoze;
            }

            if (IsUpgradeToolId(toolId) && (hasBuild || hasDeleted || hasSemanticUpgradeOrReplace))
            {
                return GlobalUndoActionKind.UpgradeReplace;
            }

            if (hasSemanticUpgradeOrReplace)
            {
                return GlobalUndoActionKind.UpgradeReplace;
            }

            // Incidental adjacent-road adjustment / Combine / same-prefab Replace / Split during road build
            // stays Build (even when accompanied by incidental terrain leveling or auto-demolished roadside assets).
            if (hasIncidentalNetAdjustment && !hasZone && !IsTerrainToolId(toolId) && !IsBulldozeToolId(toolId))
            {
                return GlobalUndoActionKind.Build;
            }

            // Unreconciled road replacing creates new edges while deleting the old edge.
            if (hasCreated && hasDeleted && !hasZone && !hasTerrain)
            {
                return GlobalUndoActionKind.UpgradeReplace;
            }

            // Pure single-domain actions
            int domains = 0;
            if (hasBuild) domains++;
            if (hasDeleted) domains++;
            if (hasZone) domains++;
            if (hasTerrain) domains++;

            if (domains == 1)
            {
                if (hasTerrain) return GlobalUndoActionKind.Terrain;
                if (hasZone) return GlobalUndoActionKind.Zone;
                if (hasDeleted) return GlobalUndoActionKind.Bulldoze;
                return GlobalUndoActionKind.Build;
            }

            // Building/road placement may incidentally record terrain leveling; still classify as Build.
            if (hasBuild && hasTerrain && !hasDeleted && !hasZone && !IsTerrainToolId(toolId))
            {
                return GlobalUndoActionKind.Build;
            }

            return GlobalUndoActionKind.Mixed;
        }

        public static List<GlobalUndoExecutionPhase> PlanExecutionPhases(
            bool hasCreated,
            bool hasDeleted,
            bool hasInPlaceNets,
            bool hasZone,
            bool hasTerrain)
        {
            var phases = new List<GlobalUndoExecutionPhase>(6);
            if (hasCreated)
            {
                phases.Add(GlobalUndoExecutionPhase.DeletingCreated);
                // Wait 1+ frames before restoring in-place nets or deleted entities
                // so vanilla systems finish tearing down the newly created segments/nodes.
                if (hasInPlaceNets || hasDeleted)
                {
                    phases.Add(GlobalUndoExecutionPhase.WaitingPostDelete);
                }
            }
            if (hasInPlaceNets)
            {
                phases.Add(GlobalUndoExecutionPhase.RestoringInPlaceNets);
                // Wait so EntityManager commits restored NetNode positions before
                // DeletionUndo validates endpoint positions in RestoringDeleted.
                if (hasDeleted)
                {
                    phases.Add(GlobalUndoExecutionPhase.WaitingPostInPlaceNets);
                }
            }
            if (hasDeleted)
            {
                phases.Add(GlobalUndoExecutionPhase.RestoringDeleted);
            }
            if (hasZone || hasTerrain)
            {
                phases.Add(GlobalUndoExecutionPhase.RestoringZoneAndTerrain);
            }
            return phases;
        }

        /// <summary>
        /// Evaluates whether an open (unflushed) stroke would survive <see cref="IsKindTracked"/>
        /// if flushed right now, so untracked strokes never falsely report CanUndo = true.
        /// </summary>
        public static bool IsOpenStrokeTrackable(
            in GlobalUndoTrackOptions options,
            string toolId,
            bool hasCreated,
            bool hasDeleted,
            bool hasInPlaceUpgradeOrReplace,
            bool hasZone,
            bool hasTerrain)
        {
            return IsOpenStrokeTrackable(
                in options,
                toolId,
                hasCreated,
                hasDeleted,
                hasSemanticUpgradeOrReplace: hasInPlaceUpgradeOrReplace,
                hasIncidentalNetAdjustment: false,
                hasZone: hasZone,
                hasTerrain: hasTerrain);
        }

        public static bool IsOpenStrokeTrackable(
            in GlobalUndoTrackOptions options,
            string toolId,
            bool hasCreated,
            bool hasDeleted,
            bool hasSemanticUpgradeOrReplace,
            bool hasIncidentalNetAdjustment,
            bool hasZone,
            bool hasTerrain)
        {
            GlobalUndoActionKind kind = ClassifyActionKind(
                toolId,
                hasCreated,
                hasDeleted,
                hasSemanticUpgradeOrReplace,
                hasIncidentalNetAdjustment,
                hasZone,
                hasTerrain);
            return kind != GlobalUndoActionKind.None && IsKindTracked(in options, kind);
        }

        public const float DefaultSplitChainMaxPointDistance = 0.75f;
        public const float DefaultSplitChainMinLengthTolerance = 1.5f;
        public const float DefaultSplitChainRelativeLengthTolerance = 0.03f;

        /// <summary>
        /// Computes the minimum squared 3D distance from <paramref name="point"/> to the
        /// piecewise-linear approximation of the cubic Bezier curve <paramref name="bezier"/>.
        /// </summary>
        public static float DistancePointToBezierSq(NetPoint3 point, in NetBezier3 bezier, int samples = 32)
        {
            int n = samples < 4 ? 4 : samples;
            NetPoint3 prev = bezier.A;
            float bestDistSq = float.MaxValue;

            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                NetPoint3 curr = EvaluateBezier(in bezier, t);
                float dSq = DistancePointToSegmentSq(point, prev, curr);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                }
                prev = curr;
            }

            return bestDistSq;
        }

        /// <summary>
        /// Validates that an ordered sequence of >= 2 edges forms a contiguous, acyclic,
        /// collinear split chain of <paramref name="oldCurve"/> from <paramref name="oldStartNodeId"/>
        /// to <paramref name="oldEndNodeId"/> (in either direction) with matching prefab and owner.
        /// </summary>
        public static bool IsCollinearSplitChain(
            in NetBezier3 oldCurve,
            float oldLength,
            int oldStartNodeId,
            int oldEndNodeId,
            int oldPrefabId,
            int oldOwnerId,
            IReadOnlyList<SplitChainCandidate> chain,
            float maxPointDistance = DefaultSplitChainMaxPointDistance)
        {
            if (chain == null || chain.Count < 2 ||
                oldStartNodeId == oldEndNodeId ||
                oldLength <= 0.01f || float.IsNaN(oldLength) || float.IsInfinity(oldLength) ||
                maxPointDistance <= 0f || float.IsNaN(maxPointDistance))
            {
                return false;
            }

            float maxDistSq = maxPointDistance * maxPointDistance;
            float lengthTol = Math.Max(DefaultSplitChainMinLengthTolerance, oldLength * DefaultSplitChainRelativeLengthTolerance);

            return ValidateOrderedChain(in oldCurve, oldLength, oldStartNodeId, oldEndNodeId, oldPrefabId, oldOwnerId, chain, maxDistSq, lengthTol) ||
                   ValidateOrderedChain(in oldCurve, oldLength, oldEndNodeId, oldStartNodeId, oldPrefabId, oldOwnerId, chain, maxDistSq, lengthTol);
        }

        /// <summary>
        /// Searches <paramref name="candidates"/> for a collinear split chain of length >= 2
        /// connecting <paramref name="oldStartNodeId"/> to <paramref name="oldEndNodeId"/>
        /// along <paramref name="oldCurve"/>. Outputs the matched indices into <paramref name="candidates"/>
        /// and the intermediate node IDs along the chain.
        /// </summary>
        public static bool TryFindCollinearSplitChain(
            in NetBezier3 oldCurve,
            float oldLength,
            int oldStartNodeId,
            int oldEndNodeId,
            int oldPrefabId,
            int oldOwnerId,
            IReadOnlyList<SplitChainCandidate> candidates,
            List<int> matchedCandidateIndices,
            List<int> matchedIntermediateNodeIds,
            float maxPointDistance = DefaultSplitChainMaxPointDistance,
            int maxDepth = 8)
        {
            if (matchedCandidateIndices != null) matchedCandidateIndices.Clear();
            if (matchedIntermediateNodeIds != null) matchedIntermediateNodeIds.Clear();

            if (candidates == null || candidates.Count < 2 ||
                oldStartNodeId == oldEndNodeId ||
                oldLength <= 0.01f || float.IsNaN(oldLength) || float.IsInfinity(oldLength) ||
                maxPointDistance <= 0f || float.IsNaN(maxPointDistance))
            {
                return false;
            }

            if (maxDepth < 2) maxDepth = 2;

            float maxDistSq = maxPointDistance * maxPointDistance;
            float lengthTol = Math.Max(DefaultSplitChainMinLengthTolerance, oldLength * DefaultSplitChainRelativeLengthTolerance);
            float maxAllowedLen = oldLength + lengthTol;

            // Pre-filter candidates by Prefab, Owner, non-degenerate endpoints, and midpoint closeness to oldCurve.
            var adj = new Dictionary<int, List<int>>();
            var midDistSqByIdx = new float[candidates.Count];

            for (int i = 0; i < candidates.Count; i++)
            {
                SplitChainCandidate c = candidates[i];
                if (c.PrefabId != oldPrefabId || c.OwnerId != oldOwnerId ||
                    c.StartNodeId == c.EndNodeId ||
                    c.Length <= 0.01f || float.IsNaN(c.Length) || float.IsInfinity(c.Length) ||
                    c.Length > maxAllowedLen)
                {
                    continue;
                }

                float midDistSq = DistancePointToBezierSq(c.MidPos, in oldCurve);
                if (midDistSq > maxDistSq)
                {
                    continue;
                }

                midDistSqByIdx[i] = midDistSq;
                AddAdj(adj, c.StartNodeId, i);
                AddAdj(adj, c.EndNodeId, i);
            }

            if (!adj.ContainsKey(oldStartNodeId) || !adj.ContainsKey(oldEndNodeId))
            {
                return false;
            }

            var visitedNodes = new HashSet<int> { oldStartNodeId };
            var usedEdges = new HashSet<int>();
            var currentPath = new List<int>(maxDepth);
            var currentIntermediates = new List<int>(maxDepth);

            List<int> bestPath = null;
            List<int> bestIntermediates = null;
            float bestScore = float.MaxValue;

            SearchSplitChainDfs(
                in oldCurve,
                oldLength,
                oldEndNodeId,
                candidates,
                adj,
                midDistSqByIdx,
                maxDistSq,
                lengthTol,
                maxAllowedLen,
                maxDepth,
                currentNode: oldStartNodeId,
                accumulatedLength: 0f,
                maxObservedDistSq: 0f,
                visitedNodes,
                usedEdges,
                currentPath,
                currentIntermediates,
                ref bestPath,
                ref bestIntermediates,
                ref bestScore);

            if (bestPath == null)
            {
                return false;
            }

            if (matchedCandidateIndices != null)
            {
                matchedCandidateIndices.AddRange(bestPath);
            }
            if (matchedIntermediateNodeIds != null && bestIntermediates != null)
            {
                matchedIntermediateNodeIds.AddRange(bestIntermediates);
            }
            return true;
        }

        private static bool ValidateOrderedChain(
            in NetBezier3 oldCurve,
            float oldLength,
            int startNodeId,
            int endNodeId,
            int oldPrefabId,
            int oldOwnerId,
            IReadOnlyList<SplitChainCandidate> chain,
            float maxDistSq,
            float lengthTol)
        {
            var visited = new HashSet<int> { startNodeId };
            int curr = startNodeId;
            float sumLen = 0f;

            for (int i = 0; i < chain.Count; i++)
            {
                SplitChainCandidate seg = chain[i];
                if (seg.PrefabId != oldPrefabId || seg.OwnerId != oldOwnerId ||
                    seg.StartNodeId == seg.EndNodeId ||
                    seg.Length <= 0.01f || float.IsNaN(seg.Length) || float.IsInfinity(seg.Length))
                {
                    return false;
                }

                int next;
                NetPoint3 nextPos;
                if (seg.StartNodeId == curr)
                {
                    next = seg.EndNodeId;
                    nextPos = seg.EndPos;
                }
                else if (seg.EndNodeId == curr)
                {
                    next = seg.StartNodeId;
                    nextPos = seg.StartPos;
                }
                else
                {
                    return false;
                }

                if (!visited.Add(next))
                {
                    return false;
                }

                if (DistancePointToBezierSq(seg.MidPos, in oldCurve) > maxDistSq)
                {
                    return false;
                }

                if (i < chain.Count - 1)
                {
                    if (DistancePointToBezierSq(nextPos, in oldCurve) > maxDistSq)
                    {
                        return false;
                    }
                }

                sumLen += seg.Length;
                curr = next;
            }

            if (curr != endNodeId)
            {
                return false;
            }

            return Math.Abs(sumLen - oldLength) <= lengthTol;
        }

        private static void SearchSplitChainDfs(
            in NetBezier3 oldCurve,
            float oldLength,
            int targetEndNodeId,
            IReadOnlyList<SplitChainCandidate> candidates,
            Dictionary<int, List<int>> adj,
            float[] midDistSqByIdx,
            float maxDistSq,
            float lengthTol,
            float maxAllowedLen,
            int maxDepth,
            int currentNode,
            float accumulatedLength,
            float maxObservedDistSq,
            HashSet<int> visitedNodes,
            HashSet<int> usedEdges,
            List<int> currentPath,
            List<int> currentIntermediates,
            ref List<int> bestPath,
            ref List<int> bestIntermediates,
            ref float bestScore)
        {
            if (currentPath.Count >= maxDepth || !adj.TryGetValue(currentNode, out List<int> incident))
            {
                return;
            }

            for (int i = 0; i < incident.Count; i++)
            {
                int candIdx = incident[i];
                if (usedEdges.Contains(candIdx))
                {
                    continue;
                }

                SplitChainCandidate seg = candidates[candIdx];
                int nextNode;
                NetPoint3 nextPos;
                if (seg.StartNodeId == currentNode)
                {
                    nextNode = seg.EndNodeId;
                    nextPos = seg.EndPos;
                }
                else
                {
                    nextNode = seg.StartNodeId;
                    nextPos = seg.StartPos;
                }

                if (visitedNodes.Contains(nextNode))
                {
                    continue;
                }

                float newLen = accumulatedLength + seg.Length;
                if (newLen > maxAllowedLen)
                {
                    continue;
                }

                float segDistSq = midDistSqByIdx[candIdx];
                float newMaxDistSq = segDistSq > maxObservedDistSq ? segDistSq : maxObservedDistSq;

                if (nextNode == targetEndNodeId)
                {
                    if (currentPath.Count + 1 >= 2 && Math.Abs(newLen - oldLength) <= lengthTol)
                    {
                        float score = newMaxDistSq + Math.Abs(newLen - oldLength);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestPath = new List<int>(currentPath.Count + 1);
                            bestPath.AddRange(currentPath);
                            bestPath.Add(candIdx);
                            bestIntermediates = new List<int>(currentIntermediates);
                        }
                    }
                    continue;
                }

                // Intermediate node must lie close to the original curve
                float nodeDistSq = DistancePointToBezierSq(nextPos, in oldCurve);
                if (nodeDistSq > maxDistSq)
                {
                    continue;
                }
                if (nodeDistSq > newMaxDistSq)
                {
                    newMaxDistSq = nodeDistSq;
                }

                visitedNodes.Add(nextNode);
                usedEdges.Add(candIdx);
                currentPath.Add(candIdx);
                currentIntermediates.Add(nextNode);

                SearchSplitChainDfs(
                    in oldCurve,
                    oldLength,
                    targetEndNodeId,
                    candidates,
                    adj,
                    midDistSqByIdx,
                    maxDistSq,
                    lengthTol,
                    maxAllowedLen,
                    maxDepth,
                    nextNode,
                    newLen,
                    newMaxDistSq,
                    visitedNodes,
                    usedEdges,
                    currentPath,
                    currentIntermediates,
                    ref bestPath,
                    ref bestIntermediates,
                    ref bestScore);

                currentIntermediates.RemoveAt(currentIntermediates.Count - 1);
                currentPath.RemoveAt(currentPath.Count - 1);
                usedEdges.Remove(candIdx);
                visitedNodes.Remove(nextNode);
            }
        }

        private static void AddAdj(Dictionary<int, List<int>> adj, int nodeId, int candIdx)
        {
            if (!adj.TryGetValue(nodeId, out List<int> list))
            {
                list = new List<int>(4);
                adj[nodeId] = list;
            }
            list.Add(candIdx);
        }

        private static NetPoint3 EvaluateBezier(in NetBezier3 b, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float tt = t * t;
            float w0 = uu * u;
            float w1 = 3f * uu * t;
            float w2 = 3f * u * tt;
            float w3 = tt * t;
            return new NetPoint3(
                w0 * b.A.X + w1 * b.B.X + w2 * b.C.X + w3 * b.D.X,
                w0 * b.A.Y + w1 * b.B.Y + w2 * b.C.Y + w3 * b.D.Y,
                w0 * b.A.Z + w1 * b.B.Z + w2 * b.C.Z + w3 * b.D.Z);
        }

        private static float DistancePointToSegmentSq(NetPoint3 p, NetPoint3 a, NetPoint3 b)
        {
            float abx = b.X - a.X;
            float aby = b.Y - a.Y;
            float abz = b.Z - a.Z;
            float apx = p.X - a.X;
            float apy = p.Y - a.Y;
            float apz = p.Z - a.Z;
            float abLenSq = abx * abx + aby * aby + abz * abz;
            if (abLenSq <= 1e-8f)
            {
                return apx * apx + apy * apy + apz * apz;
            }
            float t = (apx * abx + apy * aby + apz * abz) / abLenSq;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            float dx = apx - abx * t;
            float dy = apy - aby * t;
            float dz = apz - abz * t;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>
        /// In normal gameplay (<paramref name="freezeCascadeUpdates"/> == false), calling
        /// TerrainSystem.OnAreaChanged triggers UpdateCascades -> RenderCascades, which copies
        /// m_Heightmap into m_HeightmapCascade for the dirty viewport and then calls
        /// TriggerAsyncChange() with the tight m_LastWrite rect. Calling TriggerAsyncChange()
        /// prematurely before RenderCascades would read back the stale full 4096x4096 cascade.
        /// Only call TriggerAsyncChange() directly when cascade updates are frozen.
        /// </summary>
        public static bool ShouldTriggerTerrainAsyncChangeDirectly(bool freezeCascadeUpdates)
        {
            return freezeCascadeUpdates;
        }

        /// <summary>
        /// Only swap Edge.m_Start/m_End in-place when live endpoints are the exact reversed pair
        /// of the recorded endpoints (one-way road reversal), preventing rewiring of unrelated nodes.
        /// </summary>
        public static bool ShouldRestoreReversedEdgeEndpoints(
            int recordedStartIndex,
            int recordedStartVersion,
            int recordedEndIndex,
            int recordedEndVersion,
            int liveStartIndex,
            int liveStartVersion,
            int liveEndIndex,
            int liveEndVersion)
        {
            if (recordedStartIndex == recordedEndIndex && recordedStartVersion == recordedEndVersion)
            {
                return false;
            }
            return liveStartIndex == recordedEndIndex &&
                   liveStartVersion == recordedEndVersion &&
                   liveEndIndex == recordedStartIndex &&
                   liveEndVersion == recordedStartVersion;
        }

        public static string FormatActionKindCode(GlobalUndoActionKind kind)
        {
            switch (kind)
            {
                case GlobalUndoActionKind.BulkDelete: return "bulk_delete";
                case GlobalUndoActionKind.Bulldoze: return "bulldoze";
                case GlobalUndoActionKind.Build: return "build";
                case GlobalUndoActionKind.UpgradeReplace: return "upgrade";
                case GlobalUndoActionKind.Zone: return "zone";
                case GlobalUndoActionKind.Terrain: return "terrain";
                case GlobalUndoActionKind.Mixed: return "mixed";
                default: return "none";
            }
        }

        public static string SerializeHistoryJson(IReadOnlyList<GlobalUndoStepSummary> newestFirst, int maxItems = 12)
        {
            if (newestFirst == null || newestFirst.Count == 0 || maxItems <= 0)
            {
                return "[]";
            }

            int count = newestFirst.Count < maxItems ? newestFirst.Count : maxItems;
            var sb = new StringBuilder(count * 96);
            sb.Append('[');
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                GlobalUndoStepSummary s = newestFirst[i];
                sb.Append("{\"id\":");
                sb.Append(s.StepId);
                sb.Append(",\"kind\":\"");
                sb.Append(FormatActionKindCode(s.Kind));
                sb.Append("\",\"tool\":\"");
                AppendJsonEscaped(sb, s.ToolId ?? string.Empty);
                sb.Append("\",\"count\":");
                sb.Append(s.ItemCount);
                sb.Append(",\"created\":");
                sb.Append(s.CreatedCount);
                sb.Append(",\"deleted\":");
                sb.Append(s.DeletedCount);
                sb.Append(",\"inPlace\":");
                sb.Append(s.InPlaceNetCount);
                sb.Append(",\"zones\":");
                sb.Append(s.ZoneBlockCount);
                sb.Append(",\"tiles\":");
                sb.Append(s.TerrainTileCount);
                sb.Append(",\"terrainEvicted\":");
                sb.Append(s.TerrainEvicted ? "true" : "false");
                sb.Append(",\"ts\":");
                sb.Append(s.CreatedUnixSeconds);
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void AppendJsonEscaped(StringBuilder sb, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\');
                    sb.Append(c);
                }
                else if (c >= 0x20)
                {
                    sb.Append(c);
                }
            }
        }

        private static bool MatchesAnyToken(string text, string[] tokens)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            for (int i = 0; i < tokens.Length; i++)
            {
                if (text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Pure-logic state machine for merging multi-frame brush/drag strokes (terrain brush,
    /// zone painting, tree brush) into a single undo transaction.
    /// </summary>
    public sealed class StrokeCoalescerState
    {
        public const int DefaultIdleGapFrames = 3;

        public int IdleGapFrames { get; }
        public bool HasOpenStroke { get; private set; }
        public string OpenToolId { get; private set; } = string.Empty;
        public int LastApplyFrame { get; private set; } = -1;
        public bool WasBrushing { get; private set; }

        public StrokeCoalescerState(int idleGapFrames = DefaultIdleGapFrames)
        {
            IdleGapFrames = idleGapFrames > 0 ? idleGapFrames : DefaultIdleGapFrames;
        }

        public bool ShouldCoalesce(string activeToolId, bool isBrushing, int currentFrame)
        {
            if (!HasOpenStroke)
            {
                return false;
            }

            if (!string.Equals(OpenToolId, activeToolId ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            int delta = currentFrame - LastApplyFrame;
            if (delta < 0)
            {
                return false;
            }

            // Same frame or immediate consecutive frames while dragging/brushing.
            if (delta <= IdleGapFrames)
            {
                return true;
            }

            // Continuous brush hold even if a few frames had zero brush delta.
            if (isBrushing && WasBrushing && delta <= IdleGapFrames * 4)
            {
                return true;
            }

            return false;
        }

        public void RecordApply(string activeToolId, bool isBrushing, int currentFrame)
        {
            HasOpenStroke = true;
            OpenToolId = activeToolId ?? string.Empty;
            LastApplyFrame = currentFrame;
            WasBrushing = isBrushing;
        }

        public bool ShouldFinalize(string activeToolId, bool isBrushing, bool appliedThisFrame, int currentFrame)
        {
            if (!HasOpenStroke)
            {
                return false;
            }

            if (appliedThisFrame)
            {
                return false;
            }

            if (!string.Equals(OpenToolId, activeToolId ?? string.Empty, StringComparison.Ordinal))
            {
                return true;
            }

            int delta = currentFrame - LastApplyFrame;
            if (delta < 0)
            {
                return true;
            }

            // If a brush stroke just released mouse (brushing true -> false), finalize immediately.
            if (WasBrushing && !isBrushing && delta >= 1)
            {
                return true;
            }

            if (!isBrushing && delta >= IdleGapFrames)
            {
                return true;
            }

            return false;
        }

        public void Reset()
        {
            HasOpenStroke = false;
            OpenToolId = string.Empty;
            LastApplyFrame = -1;
            WasBrushing = false;
        }
    }

    public readonly struct TerrainTileKey : IEquatable<TerrainTileKey>
    {
        public readonly int Tx;
        public readonly int Tz;

        public TerrainTileKey(int tx, int tz)
        {
            Tx = tx;
            Tz = tz;
        }

        public bool Equals(TerrainTileKey other) => Tx == other.Tx && Tz == other.Tz;
        public override bool Equals(object obj) => obj is TerrainTileKey other && Equals(other);
        public override int GetHashCode() => unchecked((Tx * 73856093) ^ (Tz * 19349663));
        public static bool operator ==(TerrainTileKey left, TerrainTileKey right) => left.Equals(right);
        public static bool operator !=(TerrainTileKey left, TerrainTileKey right) => !left.Equals(right);
    }

    public struct TerrainCopyBatch
    {
        public int DstX;
        public int DstY;
        public int Width;
        public int Height;
        public ushort[] Pixels;
    }

    public static class TerrainTileLogic
    {
        public const int TileSize = 64;
        public const int PixelsPerTile = TileSize * TileSize;
        public const int BytesPerTile = PixelsPerTile * sizeof(ushort);
        public const long DefaultMemoryBudgetBytes = 32L * 1024L * 1024L;
        public const int SuperBlockTiles = 8; // 8 * 64 = 512px (512 KB max temp Texture2D)

        public static bool ComputeDirtyTileRange(
            float worldMinX,
            float worldMinZ,
            float worldMaxX,
            float worldMaxZ,
            float offsetX,
            float offsetZ,
            float scaleX,
            float scaleZ,
            int resX,
            int resZ,
            int padPixels,
            out int minTx,
            out int maxTx,
            out int minTz,
            out int maxTz)
        {
            minTx = maxTx = minTz = maxTz = 0;
            if (resX <= 0 || resZ <= 0 || scaleX <= 0f || scaleZ <= 0f)
            {
                return false;
            }
            if (float.IsNaN(worldMinX) || float.IsInfinity(worldMinX) ||
                float.IsNaN(worldMinZ) || float.IsInfinity(worldMinZ) ||
                float.IsNaN(worldMaxX) || float.IsInfinity(worldMaxX) ||
                float.IsNaN(worldMaxZ) || float.IsInfinity(worldMaxZ))
            {
                return false;
            }

            float loX = worldMinX <= worldMaxX ? worldMinX : worldMaxX;
            float hiX = worldMinX <= worldMaxX ? worldMaxX : worldMinX;
            float loZ = worldMinZ <= worldMaxZ ? worldMinZ : worldMaxZ;
            float hiZ = worldMinZ <= worldMaxZ ? worldMaxZ : worldMinZ;

            int pad = padPixels > 0 ? padPixels : 0;
            int px0 = (int)Math.Floor((loX + offsetX) * scaleX) - pad;
            int px1 = (int)Math.Ceiling((hiX + offsetX) * scaleX) + pad;
            int pz0 = (int)Math.Floor((loZ + offsetZ) * scaleZ) - pad;
            int pz1 = (int)Math.Ceiling((hiZ + offsetZ) * scaleZ) + pad;

            if (px1 < 0 || pz1 < 0 || px0 >= resX || pz0 >= resZ)
            {
                return false;
            }

            if (px0 < 0) px0 = 0;
            if (pz0 < 0) pz0 = 0;
            if (px1 >= resX) px1 = resX - 1;
            if (pz1 >= resZ) pz1 = resZ - 1;

            minTx = px0 / TileSize;
            maxTx = px1 / TileSize;
            minTz = pz0 / TileSize;
            maxTz = pz1 / TileSize;
            return minTx <= maxTx && minTz <= maxTz;
        }
    }

    public sealed class TerrainPatchData
    {
        public int ResolutionX { get; }
        public int ResolutionZ { get; }
        public bool HasBounds { get; private set; }
        public float BoundsMinX { get; private set; }
        public float BoundsMinZ { get; private set; }
        public float BoundsMaxX { get; private set; }
        public float BoundsMaxZ { get; private set; }
        public bool Truncated { get; set; }

        public readonly Dictionary<TerrainTileKey, ushort[]> Tiles = new Dictionary<TerrainTileKey, ushort[]>();

        public int TileCount => Tiles.Count;
        public long EstimatedBytes => (long)Tiles.Count * TerrainTileLogic.BytesPerTile;

        public TerrainPatchData(int resolutionX, int resolutionZ)
        {
            ResolutionX = resolutionX;
            ResolutionZ = resolutionZ;
        }

        public void ExpandWorldBounds(float minX, float minZ, float maxX, float maxZ)
        {
            float loX = minX <= maxX ? minX : maxX;
            float hiX = minX <= maxX ? maxX : minX;
            float loZ = minZ <= maxZ ? minZ : maxZ;
            float hiZ = minZ <= maxZ ? maxZ : minZ;

            if (!HasBounds)
            {
                HasBounds = true;
                BoundsMinX = loX;
                BoundsMinZ = loZ;
                BoundsMaxX = hiX;
                BoundsMaxZ = hiZ;
            }
            else
            {
                if (loX < BoundsMinX) BoundsMinX = loX;
                if (loZ < BoundsMinZ) BoundsMinZ = loZ;
                if (hiX > BoundsMaxX) BoundsMaxX = hiX;
                if (hiZ > BoundsMaxZ) BoundsMaxZ = hiZ;
            }
        }

        /// <summary>
        /// First-Write-Wins tile recording so multi-frame strokes always preserve the pre-stroke height.
        /// </summary>
        public bool TryRecordTile(TerrainTileKey key, ushort[] tileHeights)
        {
            if (tileHeights == null || tileHeights.Length != TerrainTileLogic.PixelsPerTile)
            {
                return false;
            }
            if (Tiles.ContainsKey(key))
            {
                return false;
            }
            Tiles[key] = tileHeights;
            return true;
        }

        public bool TryCaptureTileFromSampler(
            TerrainTileKey key,
            Func<int, int, ushort> heightSampler,
            long maxPatchBytes = TerrainTileLogic.DefaultMemoryBudgetBytes)
        {
            if (heightSampler == null || Tiles.ContainsKey(key))
            {
                return false;
            }
            if (EstimatedBytes + TerrainTileLogic.BytesPerTile > maxPatchBytes)
            {
                Truncated = true;
                return false;
            }

            int baseX = key.Tx * TerrainTileLogic.TileSize;
            int baseZ = key.Tz * TerrainTileLogic.TileSize;
            if (baseX < 0 || baseZ < 0 || baseX >= ResolutionX || baseZ >= ResolutionZ)
            {
                return false;
            }

            var buf = new ushort[TerrainTileLogic.PixelsPerTile];
            int maxLocalX = Math.Min(TerrainTileLogic.TileSize, ResolutionX - baseX);
            int maxLocalZ = Math.Min(TerrainTileLogic.TileSize, ResolutionZ - baseZ);
            for (int lz = 0; lz < maxLocalZ; lz++)
            {
                int rowOffset = lz * TerrainTileLogic.TileSize;
                int pz = baseZ + lz;
                for (int lx = 0; lx < maxLocalX; lx++)
                {
                    buf[rowOffset + lx] = heightSampler(baseX + lx, pz);
                }
            }

            Tiles[key] = buf;
            return true;
        }

        /// <summary>
        /// Packs dirty tiles into bounded GPU copy batches (each at most 512x512 pixels = 512 KB).
        /// For untouched interior pixels inside a batch rectangle, <paramref name="liveFallbackSampler"/>
        /// supplies the current live heightmap pixel so untouched areas are never zeroed.
        /// </summary>
        public List<TerrainCopyBatch> BuildCopyBatches(Func<int, int, ushort> liveFallbackSampler = null)
        {
            var batches = new List<TerrainCopyBatch>();
            if (Tiles.Count == 0 || ResolutionX <= 0 || ResolutionZ <= 0)
            {
                return batches;
            }

            // Group dirty tiles by 8x8 tile super-blocks so even a diagonal stroke across the entire
            // 4096x4096 map only allocates small bounded Texture2Ds instead of a full-map texture.
            var groups = new Dictionary<TerrainTileKey, List<TerrainTileKey>>();
            foreach (var kv in Tiles)
            {
                TerrainTileKey tk = kv.Key;
                var sbKey = new TerrainTileKey(
                    tk.Tx / TerrainTileLogic.SuperBlockTiles,
                    tk.Tz / TerrainTileLogic.SuperBlockTiles);
                if (!groups.TryGetValue(sbKey, out List<TerrainTileKey> list))
                {
                    list = new List<TerrainTileKey>();
                    groups[sbKey] = list;
                }
                list.Add(tk);
            }

            foreach (var kv in groups)
            {
                List<TerrainTileKey> groupTiles = kv.Value;
                int minTx = int.MaxValue, maxTx = int.MinValue;
                int minTz = int.MaxValue, maxTz = int.MinValue;
                for (int i = 0; i < groupTiles.Count; i++)
                {
                    TerrainTileKey tk = groupTiles[i];
                    if (tk.Tx < minTx) minTx = tk.Tx;
                    if (tk.Tx > maxTx) maxTx = tk.Tx;
                    if (tk.Tz < minTz) minTz = tk.Tz;
                    if (tk.Tz > maxTz) maxTz = tk.Tz;
                }

                int dstX = minTx * TerrainTileLogic.TileSize;
                int dstY = minTz * TerrainTileLogic.TileSize;
                int endX = Math.Min(ResolutionX, (maxTx + 1) * TerrainTileLogic.TileSize);
                int endY = Math.Min(ResolutionZ, (maxTz + 1) * TerrainTileLogic.TileSize);
                int width = endX - dstX;
                int height = endY - dstY;
                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                var pixels = new ushort[width * height];
                for (int tz = minTz; tz <= maxTz; tz++)
                {
                    int tileBaseZ = tz * TerrainTileLogic.TileSize;
                    int tileEndZ = Math.Min(endY, tileBaseZ + TerrainTileLogic.TileSize);
                    for (int tx = minTx; tx <= maxTx; tx++)
                    {
                        int tileBaseX = tx * TerrainTileLogic.TileSize;
                        int tileEndX = Math.Min(endX, tileBaseX + TerrainTileLogic.TileSize);
                        if (Tiles.TryGetValue(new TerrainTileKey(tx, tz), out ushort[] tileData))
                        {
                            for (int pz = tileBaseZ; pz < tileEndZ; pz++)
                            {
                                int srcRow = (pz - tileBaseZ) * TerrainTileLogic.TileSize;
                                int dstRow = (pz - dstY) * width + (tileBaseX - dstX);
                                int copyLen = tileEndX - tileBaseX;
                                Array.Copy(tileData, srcRow, pixels, dstRow, copyLen);
                            }
                        }
                        else if (liveFallbackSampler != null)
                        {
                            for (int pz = tileBaseZ; pz < tileEndZ; pz++)
                            {
                                int dstRow = (pz - dstY) * width + (tileBaseX - dstX);
                                for (int px = tileBaseX; px < tileEndX; px++)
                                {
                                    pixels[dstRow + (px - tileBaseX)] = liveFallbackSampler(px, pz);
                                }
                            }
                        }
                    }
                }

                batches.Add(new TerrainCopyBatch
                {
                    DstX = dstX,
                    DstY = dstY,
                    Width = width,
                    Height = height,
                    Pixels = pixels,
                });
            }

            return batches;
        }
    }

    public static class ZoneBlockValidationLogic
    {
        public static ZoneRestoreVerdict ValidateRestore(
            bool blockExists,
            int recordedSizeX,
            int recordedSizeY,
            int liveSizeX,
            int liveSizeY,
            int recordedCellCount,
            int liveBufferLength)
        {
            if (!blockExists)
            {
                return ZoneRestoreVerdict.BlockMissing;
            }
            if (recordedSizeX <= 0 || recordedSizeY <= 0 || recordedCellCount <= 0)
            {
                return ZoneRestoreVerdict.EmptyOrCorrupt;
            }
            if (recordedSizeX != liveSizeX || recordedSizeY != liveSizeY)
            {
                return ZoneRestoreVerdict.SizeMismatch;
            }
            int expectedCells = recordedSizeX * recordedSizeY;
            if (recordedCellCount != expectedCells || liveBufferLength != expectedCells)
            {
                return ZoneRestoreVerdict.EmptyOrCorrupt;
            }
            return ZoneRestoreVerdict.Valid;
        }
    }

    public interface IGlobalUndoHistoryItem
    {
        long TerrainBytes { get; }
        bool HasNonTerrainPayload { get; }
        void EvictTerrain();
    }

    /// <summary>
    /// Unified LIFO undo stack enforcing both step depth (1..20) and total terrain memory budget (32 MB).
    /// </summary>
    public sealed class GlobalUndoHistoryStack<T> where T : class, IGlobalUndoHistoryItem
    {
        private readonly List<T> m_Items = new List<T>();
        private int m_MaxDepth;
        private readonly long m_TerrainBudgetBytes;

        public GlobalUndoHistoryStack(
            int maxDepth = GlobalUndoPolicy.DefaultHistoryDepth,
            long terrainBudgetBytes = TerrainTileLogic.DefaultMemoryBudgetBytes)
        {
            m_MaxDepth = GlobalUndoPolicy.ClampHistoryDepth(maxDepth);
            m_TerrainBudgetBytes = terrainBudgetBytes > 0 ? terrainBudgetBytes : TerrainTileLogic.DefaultMemoryBudgetBytes;
        }

        public int Count => m_Items.Count;
        public int MaxDepth => m_MaxDepth;
        public long TerrainBudgetBytes => m_TerrainBudgetBytes;

        public long TotalTerrainBytes
        {
            get
            {
                long total = 0;
                for (int i = 0; i < m_Items.Count; i++)
                {
                    total += m_Items[i].TerrainBytes;
                }
                return total;
            }
        }

        public void SetMaxDepth(int depth)
        {
            m_MaxDepth = GlobalUndoPolicy.ClampHistoryDepth(depth);
            TrimToDepth();
        }

        public int Push(T item)
        {
            if (item == null) return 0;
            m_Items.Add(item);
            TrimToDepth();
            return EnforceTerrainBudget();
        }

        public T Peek() => m_Items.Count > 0 ? m_Items[m_Items.Count - 1] : null;

        public T Pop()
        {
            if (m_Items.Count == 0) return null;
            int idx = m_Items.Count - 1;
            T item = m_Items[idx];
            m_Items.RemoveAt(idx);
            return item;
        }

        public T GetNewestAt(int offsetFromTop)
        {
            int idx = m_Items.Count - 1 - offsetFromTop;
            return idx >= 0 && idx < m_Items.Count ? m_Items[idx] : null;
        }

        public void Clear()
        {
            m_Items.Clear();
        }

        private void TrimToDepth()
        {
            while (m_Items.Count > m_MaxDepth)
            {
                m_Items.RemoveAt(0);
            }
        }

        private int EnforceTerrainBudget()
        {
            int evictedCount = 0;
            long total = TotalTerrainBytes;
            int i = 0;
            while (total > m_TerrainBudgetBytes && i < m_Items.Count)
            {
                T candidate = m_Items[i];
                long bytes = candidate.TerrainBytes;
                if (bytes <= 0)
                {
                    i++;
                    continue;
                }

                // Never evict the sole newest transaction unless it alone exceeds budget;
                // even then, if there are older transactions we always evict oldest first.
                candidate.EvictTerrain();
                total -= bytes;
                evictedCount++;

                if (!candidate.HasNonTerrainPayload)
                {
                    m_Items.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
            return evictedCount;
        }
    }
}
