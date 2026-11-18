using System;
using System.Collections.Generic;

namespace MarqueeDelete
{
    public enum AssetRenderKind : byte
    {
        None = 0,
        Curve = 1,
        Circle = 2,
        AreaOutline = 3,
    }

    public struct AssetBudgetCandidate
    {
        public float WorldX;
        public float WorldZ;
        public float DistSqToCursor;
        public int PrimitiveCost;
        public bool InViewport;
        public bool Valid;
    }

    /// <summary>
    /// Pure scheduling, polygon decimation, and 1600-primitive budget allocator for
    /// non-building projected overlays. Keeps zero Unity/ECS dependencies so it can be
    /// unit-tested and benchmarked directly under dotnet test harnesses.
    /// </summary>
    public static class AssetOverlayLogic
    {
        public const int MaxAssetOverlayPrimitives = 1600;
        public const int MaxEdgesPerArea = 20;

        /// <summary>
        /// Computes evenly spaced polygon vertex indices capped at <paramref name="maxEdges"/>.
        /// Preserves full outline shape for large area lots without blowing the shared budget.
        /// </summary>
        public static void DecimatePolygonIndices(int nodeCount, int maxEdges, List<int> outIndices)
        {
            if (outIndices == null)
            {
                return;
            }

            outIndices.Clear();
            if (nodeCount < 2 || maxEdges < 2)
            {
                return;
            }

            if (nodeCount <= maxEdges)
            {
                for (int i = 0; i < nodeCount; i++)
                {
                    outIndices.Add(i);
                }
                return;
            }

            int last = -1;
            for (int i = 0; i < maxEdges; i++)
            {
                int idx = (int)((long)i * nodeCount / maxEdges);
                if (idx <= last)
                {
                    idx = last + 1;
                }
                if (idx >= nodeCount)
                {
                    break;
                }
                outIndices.Add(idx);
                last = idx;
            }
        }

        public static float ClampNetWidth(float rawWidth)
        {
            if (float.IsNaN(rawWidth) || float.IsInfinity(rawWidth) || rawWidth <= 0.05f)
            {
                return 3.2f;
            }

            float scaled = rawWidth * 0.85f;
            if (scaled < 1.2f) return 1.2f;
            if (scaled > 24.0f) return 24.0f;
            return scaled;
        }

        public static bool IsFiniteFloat(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFiniteVec3(float x, float y, float z)
        {
            return IsFiniteFloat(x) && IsFiniteFloat(y) && IsFiniteFloat(z);
        }

        public static AssetRenderKind EvaluateCurveMarker(
            float ax, float ay, float az,
            float bx, float by, float bz,
            float cx, float cy, float cz,
            float dx, float dy, float dz,
            float midX, float midY, float midZ,
            float rawWidth,
            out float clampedWidth)
        {
            if (!IsFiniteVec3(ax, ay, az) ||
                !IsFiniteVec3(bx, by, bz) ||
                !IsFiniteVec3(cx, cy, cz) ||
                !IsFiniteVec3(dx, dy, dz) ||
                !IsFiniteVec3(midX, midY, midZ))
            {
                clampedWidth = 0f;
                return AssetRenderKind.None;
            }

            clampedWidth = ClampNetWidth(rawWidth);
            return AssetRenderKind.Curve;
        }

        public static AssetRenderKind EvaluateNetworkNodeMarker(
            bool hasNode,
            float posX, float posY, float posZ,
            out float diameter)
        {
            if (!hasNode || !IsFiniteVec3(posX, posY, posZ))
            {
                diameter = 0f;
                return AssetRenderKind.None;
            }

            diameter = 3.2f;
            return AssetRenderKind.Circle;
        }

        public static AssetRenderKind EvaluateObjectCircleMarker(
            bool hasTransform,
            float posX, float posY, float posZ)
        {
            if (!hasTransform || !IsFiniteVec3(posX, posY, posZ))
            {
                return AssetRenderKind.None;
            }

            return AssetRenderKind.Circle;
        }

        public static AssetRenderKind EvaluateAreaMarker(
            bool hasBuffer,
            int nodeCount,
            int validFiniteNodes,
            float centroidX, float centroidY, float centroidZ,
            int decimatedVertexCount)
        {
            if (!hasBuffer ||
                nodeCount <= 0 ||
                validFiniteNodes <= 0 ||
                !IsFiniteVec3(centroidX, centroidY, centroidZ))
            {
                return AssetRenderKind.None;
            }

            if (nodeCount < 3 || decimatedVertexCount < 2)
            {
                return AssetRenderKind.Circle;
            }

            return AssetRenderKind.AreaOutline;
        }

        public static int GetPrimitiveCost(AssetRenderKind kind, int areaVertexCount)
        {
            switch (kind)
            {
                case AssetRenderKind.Curve:
                case AssetRenderKind.Circle:
                    return 1;
                case AssetRenderKind.AreaOutline:
                    return areaVertexCount >= 2 ? areaVertexCount : 0;
                default:
                    return 0;
            }
        }

        public static AssetBudgetCandidate BuildCandidate(
            AssetRenderKind kind,
            int areaVertexCount,
            float worldX,
            float worldZ,
            float distSqToCursor,
            bool inViewport)
        {
            int primitiveCost = GetPrimitiveCost(kind, areaVertexCount);
            bool valid = primitiveCost > 0 && IsFiniteFloat(worldX) && IsFiniteFloat(worldZ);
            return new AssetBudgetCandidate
            {
                WorldX = worldX,
                WorldZ = worldZ,
                DistSqToCursor = distSqToCursor,
                PrimitiveCost = valid ? primitiveCost : 0,
                InViewport = inViewport,
                Valid = valid,
            };
        }

        public static int BuildFallbackSet<TId>(
            IReadOnlyList<TId> markerIds,
            IReadOnlyList<bool> projectedFlags,
            HashSet<TId> outFallbackIds)
        {
            if (outFallbackIds == null)
            {
                return 0;
            }

            outFallbackIds.Clear();
            if (markerIds == null || projectedFlags == null)
            {
                return 0;
            }

            int count = markerIds.Count < projectedFlags.Count ? markerIds.Count : projectedFlags.Count;
            for (int i = 0; i < count; i++)
            {
                if (!projectedFlags[i])
                {
                    outFallbackIds.Add(markerIds[i]);
                }
            }

            return outFallbackIds.Count;
        }

        public static bool ShouldApplyNativeHighlight(
            SelectionCategory category,
            bool inFallbackSet,
            bool entityAlive)
        {
            return category != SelectionCategory.Building && inFallbackSet && entityAlive;
        }

        /// <summary>
        /// Assigns which candidate indices render in custom 6-color projected overlays vs
        /// fall back to native Highlighted within the <paramref name="maxPrimitives"/> cap:
        ///   1) If total valid primitives <= maxPrimitives: admit all valid items (no culling needed).
        ///   2) Otherwise filter to in-viewport items first.
        ///   3) If in-viewport still exceeds maxPrimitives, prioritize closest to cursor (XZ).
        ///   4) Anything unadmitted sets outIsProjected[i] = false so caller restores native Highlighted.
        /// </summary>
        public static int AllocateProjectedBudget(
            List<AssetBudgetCandidate> candidates,
            int maxPrimitives,
            List<int> scratchOrder,
            List<bool> outIsProjected,
            List<int> outProjectedIndices)
        {
            outIsProjected.Clear();
            outProjectedIndices.Clear();
            if (candidates == null || candidates.Count == 0 || maxPrimitives <= 0)
            {
                return 0;
            }

            int count = candidates.Count;
            long totalValidPrimitives = 0;
            long inViewportPrimitives = 0;
            for (int i = 0; i < count; i++)
            {
                outIsProjected.Add(false);
                AssetBudgetCandidate c = candidates[i];
                if (!c.Valid || c.PrimitiveCost <= 0)
                {
                    continue;
                }

                totalValidPrimitives += c.PrimitiveCost;
                if (c.InViewport)
                {
                    inViewportPrimitives += c.PrimitiveCost;
                }
            }

            if (totalValidPrimitives == 0)
            {
                return 0;
            }

            // Fast path: everything fits under the budget -> project all without sorting/culling.
            if (totalValidPrimitives <= maxPrimitives)
            {
                int used = 0;
                for (int i = 0; i < count; i++)
                {
                    AssetBudgetCandidate c = candidates[i];
                    if (c.Valid && c.PrimitiveCost > 0)
                    {
                        outIsProjected[i] = true;
                        outProjectedIndices.Add(i);
                        used += c.PrimitiveCost;
                    }
                }
                return used;
            }

            // Viewport path: if all in-viewport primitives fit <= maxPrimitives, admit all in-viewport.
            if (inViewportPrimitives <= maxPrimitives)
            {
                int used = 0;
                for (int i = 0; i < count; i++)
                {
                    AssetBudgetCandidate c = candidates[i];
                    if (c.Valid && c.InViewport && c.PrimitiveCost > 0)
                    {
                        outIsProjected[i] = true;
                        outProjectedIndices.Add(i);
                        used += c.PrimitiveCost;
                    }
                }
                return used;
            }

            // Overflow path: sort in-viewport candidates nearest-to-cursor first, greedy pack <= maxPrimitives.
            scratchOrder.Clear();
            for (int i = 0; i < count; i++)
            {
                AssetBudgetCandidate c = candidates[i];
                if (c.Valid && c.InViewport && c.PrimitiveCost > 0)
                {
                    scratchOrder.Add(i);
                }
            }

            var comparer = new CandidateDistanceComparer(candidates);
            scratchOrder.Sort(comparer);

            int budgetUsed = 0;
            for (int k = 0; k < scratchOrder.Count; k++)
            {
                int idx = scratchOrder[k];
                int cost = candidates[idx].PrimitiveCost;
                if (budgetUsed + cost <= maxPrimitives)
                {
                    outIsProjected[idx] = true;
                    outProjectedIndices.Add(idx);
                    budgetUsed += cost;
                    if (budgetUsed == maxPrimitives)
                    {
                        break;
                    }
                }
            }

            return budgetUsed;
        }

        private sealed class CandidateDistanceComparer : IComparer<int>
        {
            private readonly List<AssetBudgetCandidate> m_Candidates;

            public CandidateDistanceComparer(List<AssetBudgetCandidate> candidates)
            {
                m_Candidates = candidates;
            }

            public int Compare(int a, int b)
            {
                float da = m_Candidates[a].DistSqToCursor;
                float db = m_Candidates[b].DistSqToCursor;
                int cmp = da.CompareTo(db);
                return cmp != 0 ? cmp : a.CompareTo(b);
            }
        }
    }
}
