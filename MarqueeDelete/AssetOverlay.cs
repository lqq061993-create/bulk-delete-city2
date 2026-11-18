using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MarqueeDelete
{
    public sealed partial class MarqueeDeleteToolSystem
    {
        private struct AssetMarker
        {
            public Entity Entity;
            public SelectionCategory Category;
            public AssetRenderKind RenderKind;
            public float3 Position;
            public Bezier4x3 Bezier;
            public float SizeOrWidth;
            public int PointOffset;
            public int PointCount;
        }

        private readonly List<AssetMarker> m_AssetMarkers = new List<AssetMarker>();
        private readonly List<float3> m_AreaOutlinePoints = new List<float3>();
        private readonly List<int> m_AreaDecimationIndices = new List<int>(AssetOverlayLogic.MaxEdgesPerArea);
        private readonly List<AssetBudgetCandidate> m_AssetCandidates = new List<AssetBudgetCandidate>();
        private readonly List<int> m_AssetScratchOrder = new List<int>();
        private readonly List<bool> m_AssetProjectedFlags = new List<bool>();
        private readonly List<int> m_ProjectedAssetIndices = new List<int>();
        private readonly HashSet<Entity> m_OverflowFallbackEntities = new HashSet<Entity>();
        private readonly HashSet<Entity> m_NextFallbackEntities = new HashSet<Entity>();

        private SelectionResult m_AssetOverlaySelection;
        private float m_NextAssetRefreshTime;
        private float3 m_LastAssetRefreshCamPos;
        private int m_TotalCachedAssetPrimitives;
        private int m_LastProjectedPrimitives;

        private void ClearAssetOverlay()
        {
            m_AssetOverlaySelection = null;
            m_AssetMarkers.Clear();
            m_AreaOutlinePoints.Clear();
            m_AssetCandidates.Clear();
            m_AssetScratchOrder.Clear();
            m_AssetProjectedFlags.Clear();
            m_ProjectedAssetIndices.Clear();
            m_OverflowFallbackEntities.Clear();
            m_NextFallbackEntities.Clear();
            m_NextAssetRefreshTime = 0f;
            m_TotalCachedAssetPrimitives = 0;
            m_LastProjectedPrimitives = 0;
        }

        /// <summary>
        /// Ensures cached geometry and the 1600-primitive projected budget partition are up to date.
        /// Returns true if the set of overflow entities requiring native Highlighted changed.
        /// </summary>
        private bool PrepareAssetOverlays(SelectionResult selection)
        {
            if (selection == null || selection.Items.Count == 0 ||
                (selection.Count - selection.Breakdown.Buildings) <= 0)
            {
                bool hadFallback = m_OverflowFallbackEntities.Count > 0;
                if (m_AssetOverlaySelection != null || m_AssetMarkers.Count > 0)
                {
                    ClearAssetOverlay();
                }
                return hadFallback;
            }

            if (Mod.Settings != null && Mod.Settings.OverlayDetail == OverlayDetailLevel.Minimal)
            {
                bool hadFallback = m_OverflowFallbackEntities.Count > 0;
                if (m_AssetOverlaySelection != null || m_AssetMarkers.Count > 0)
                {
                    ClearAssetOverlay();
                }
                return hadFallback;
            }

            int budget = Mod.Settings != null ? Mod.Settings.MaxAssetOverlayBudget : AssetOverlayLogic.MaxAssetOverlayPrimitives;
            if (Mod.Settings != null && Mod.Settings.OverlayDetail == OverlayDetailLevel.Standard)
            {
                budget = Math.Min(budget, 800);
            }

            bool selectionChanged = m_AssetOverlaySelection != selection;
            if (selectionChanged)
            {
                m_AssetOverlaySelection = selection;
                m_TotalCachedAssetPrimitives = BuildAssetMarkers(selection);
            }

            bool totalExceedsBudget = m_TotalCachedAssetPrimitives > budget;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (selectionChanged || (totalExceedsBudget && now >= m_NextAssetRefreshTime))
            {
                m_NextAssetRefreshTime = now + 0.15f;
                return RefreshProjectedAssetBudget(Camera.main);
            }

            return false;
        }

        private int BuildAssetMarkers(SelectionResult selection)
        {
            m_AssetMarkers.Clear();
            m_AreaOutlinePoints.Clear();

            for (int i = 0; i < selection.Items.Count; i++)
            {
                SelectedEntityItem item = selection.Items[i];
                if (item.Category == SelectionCategory.Building || item.Category == SelectionCategory.None)
                {
                    continue;
                }

                Entity e = item.Entity;
                if (e == Entity.Null || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                {
                    continue;
                }

                switch (item.Category)
                {
                    case SelectionCategory.Network:
                        AddNetworkMarker(e);
                        break;
                    case SelectionCategory.Tree:
                        AddObjectCircleMarker(e, SelectionCategory.Tree, 1.4f, 2.0f, 10.0f);
                        break;
                    case SelectionCategory.Prop:
                        AddObjectCircleMarker(e, SelectionCategory.Prop, 1.1f, 1.6f, 10.0f);
                        break;
                    case SelectionCategory.Decal:
                        AddObjectCircleMarker(e, SelectionCategory.Decal, 1.5f, 2.2f, 14.0f);
                        break;
                    case SelectionCategory.Moving:
                        AddObjectCircleMarker(e, SelectionCategory.Moving, 1.5f, 2.4f, 12.0f);
                        break;
                    case SelectionCategory.Area:
                        AddAreaMarker(e);
                        break;
                }
            }

            int totalPrimitives = 0;
            for (int i = 0; i < m_AssetMarkers.Count; i++)
            {
                AssetMarker m = m_AssetMarkers[i];
                if (m.RenderKind == AssetRenderKind.AreaOutline)
                {
                    totalPrimitives += m.PointCount;
                }
                else if (m.RenderKind != AssetRenderKind.None)
                {
                    totalPrimitives += 1;
                }
            }
            return totalPrimitives;
        }

        private void AddFallbackMarker(Entity entity, SelectionCategory category)
        {
            m_AssetMarkers.Add(new AssetMarker
            {
                Entity = entity,
                Category = category,
                RenderKind = AssetRenderKind.None,
            });
        }

        private void AddNetworkMarker(Entity entity)
        {
            if (EntityManager.HasComponent<Curve>(entity))
            {
                Curve curve = EntityManager.GetComponentData<Curve>(entity);
                if (!IsFiniteBezier(curve.m_Bezier))
                {
                    AddFallbackMarker(entity, SelectionCategory.Network);
                    return;
                }

                float rawWidth = 0f;
                if (EntityManager.HasComponent<Composition>(entity))
                {
                    Entity comp = EntityManager.GetComponentData<Composition>(entity).m_Edge;
                    if (comp != Entity.Null && EntityManager.Exists(comp) && EntityManager.HasComponent<NetCompositionData>(comp))
                    {
                        rawWidth = EntityManager.GetComponentData<NetCompositionData>(comp).m_Width;
                    }
                }

                float3 mid = MathUtils.Position(curve.m_Bezier, 0.5f);
                AssetRenderKind kind = AssetOverlayLogic.EvaluateCurveMarker(
                    curve.m_Bezier.a.x, curve.m_Bezier.a.y, curve.m_Bezier.a.z,
                    curve.m_Bezier.b.x, curve.m_Bezier.b.y, curve.m_Bezier.b.z,
                    curve.m_Bezier.c.x, curve.m_Bezier.c.y, curve.m_Bezier.c.z,
                    curve.m_Bezier.d.x, curve.m_Bezier.d.y, curve.m_Bezier.d.z,
                    mid.x, mid.y, mid.z,
                    rawWidth,
                    out float clampedWidth);
                if (kind == AssetRenderKind.None)
                {
                    AddFallbackMarker(entity, SelectionCategory.Network);
                    return;
                }

                m_AssetMarkers.Add(new AssetMarker
                {
                    Entity = entity,
                    Category = SelectionCategory.Network,
                    RenderKind = kind,
                    Position = mid,
                    Bezier = curve.m_Bezier,
                    SizeOrWidth = clampedWidth,
                });
                return;
            }

            if (EntityManager.HasComponent<Game.Net.Node>(entity))
            {
                float3 pos = EntityManager.GetComponentData<Game.Net.Node>(entity).m_Position;
                AssetRenderKind kind = AssetOverlayLogic.EvaluateNetworkNodeMarker(
                    true,
                    pos.x, pos.y, pos.z,
                    out float diameter);
                if (kind == AssetRenderKind.None)
                {
                    AddFallbackMarker(entity, SelectionCategory.Network);
                    return;
                }

                m_AssetMarkers.Add(new AssetMarker
                {
                    Entity = entity,
                    Category = SelectionCategory.Network,
                    RenderKind = kind,
                    Position = pos,
                    SizeOrWidth = diameter,
                });
                return;
            }

            AddFallbackMarker(entity, SelectionCategory.Network);
        }

        private void AddObjectCircleMarker(
            Entity entity,
            SelectionCategory category,
            float fallbackRadius,
            float minDiameter,
            float maxDiameter)
        {
            if (!EntityManager.HasComponent<Game.Objects.Transform>(entity))
            {
                AddFallbackMarker(entity, category);
                return;
            }

            float3 pos = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
            if (AssetOverlayLogic.EvaluateObjectCircleMarker(true, pos.x, pos.y, pos.z) == AssetRenderKind.None)
            {
                AddFallbackMarker(entity, category);
                return;
            }

            float radius = fallbackRadius;
            if (EntityManager.HasComponent<PrefabRef>(entity))
            {
                Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (prefab != Entity.Null && EntityManager.Exists(prefab) && EntityManager.HasComponent<ObjectGeometryData>(prefab))
                {
                    float3 size = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Size;
                    if (math.all(math.isfinite(size)))
                    {
                        float maxXZ = math.max(size.x, size.z);
                        if (maxXZ > 0.1f)
                        {
                            radius = maxXZ * 0.5f;
                        }
                    }
                }
            }

            m_AssetMarkers.Add(new AssetMarker
            {
                Entity = entity,
                Category = category,
                RenderKind = AssetRenderKind.Circle,
                Position = pos,
                SizeOrWidth = math.clamp(radius * 2f, minDiameter, maxDiameter),
            });
        }

        private void AddAreaMarker(Entity entity)
        {
            if (!EntityManager.HasBuffer<Game.Areas.Node>(entity))
            {
                AddFallbackMarker(entity, SelectionCategory.Area);
                return;
            }

            DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(entity, true);
            if (nodes.Length == 0)
            {
                AddFallbackMarker(entity, SelectionCategory.Area);
                return;
            }

            float3 centroid = float3.zero;
            int validNodes = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                float3 p = nodes[i].m_Position;
                if (math.all(math.isfinite(p)))
                {
                    centroid += p;
                    validNodes++;
                }
            }

            if (validNodes == 0)
            {
                AddFallbackMarker(entity, SelectionCategory.Area);
                return;
            }

            centroid /= validNodes;
            if (!math.all(math.isfinite(centroid)))
            {
                AddFallbackMarker(entity, SelectionCategory.Area);
                return;
            }

            if (nodes.Length < 3)
            {
                AssetRenderKind smallKind = AssetOverlayLogic.EvaluateAreaMarker(
                    true, nodes.Length, validNodes, centroid.x, centroid.y, centroid.z, 0);
                if (smallKind == AssetRenderKind.None)
                {
                    AddFallbackMarker(entity, SelectionCategory.Area);
                    return;
                }

                m_AssetMarkers.Add(new AssetMarker
                {
                    Entity = entity,
                    Category = SelectionCategory.Area,
                    RenderKind = smallKind,
                    Position = centroid,
                    SizeOrWidth = 4.5f,
                });
                return;
            }

            AssetOverlayLogic.DecimatePolygonIndices(
                nodes.Length,
                AssetOverlayLogic.MaxEdgesPerArea,
                m_AreaDecimationIndices);

            int offset = m_AreaOutlinePoints.Count;
            for (int i = 0; i < m_AreaDecimationIndices.Count; i++)
            {
                float3 pt = nodes[m_AreaDecimationIndices[i]].m_Position;
                if (math.all(math.isfinite(pt)))
                {
                    m_AreaOutlinePoints.Add(pt);
                }
            }

            int pointCount = m_AreaOutlinePoints.Count - offset;
            AssetRenderKind areaKind = AssetOverlayLogic.EvaluateAreaMarker(
                true, nodes.Length, validNodes, centroid.x, centroid.y, centroid.z, pointCount);
            if (areaKind == AssetRenderKind.None)
            {
                m_AreaOutlinePoints.RemoveRange(offset, pointCount);
                AddFallbackMarker(entity, SelectionCategory.Area);
                return;
            }

            if (areaKind == AssetRenderKind.Circle)
            {
                m_AreaOutlinePoints.RemoveRange(offset, pointCount);
                m_AssetMarkers.Add(new AssetMarker
                {
                    Entity = entity,
                    Category = SelectionCategory.Area,
                    RenderKind = AssetRenderKind.Circle,
                    Position = centroid,
                    SizeOrWidth = 4.5f,
                });
                return;
            }

            m_AssetMarkers.Add(new AssetMarker
            {
                Entity = entity,
                Category = SelectionCategory.Area,
                RenderKind = AssetRenderKind.AreaOutline,
                Position = centroid,
                SizeOrWidth = 1.4f,
                PointOffset = offset,
                PointCount = pointCount,
            });
        }

        private bool RefreshProjectedAssetBudget(Camera camera)
        {
            float3 cursor = m_HasCursorWorld ? m_CursorWorld : float3.zero;
            if (camera != null)
            {
                Vector3 cp = camera.transform.position;
                m_LastAssetRefreshCamPos = new float3(cp.x, cp.y, cp.z);
            }

            int budget = Mod.Settings != null ? Mod.Settings.MaxAssetOverlayBudget : AssetOverlayLogic.MaxAssetOverlayPrimitives;
            if (Mod.Settings != null && Mod.Settings.OverlayDetail == OverlayDetailLevel.Standard)
            {
                budget = Math.Min(budget, 800);
            }

            m_AssetCandidates.Clear();
            bool needViewport = m_TotalCachedAssetPrimitives > budget && camera != null;
            for (int i = 0; i < m_AssetMarkers.Count; i++)
            {
                AssetMarker m = m_AssetMarkers[i];
                bool entityAlive = EntityManager.Exists(m.Entity) &&
                                   !EntityManager.HasComponent<Deleted>(m.Entity);

                float3 pos = m.Position;
                if (entityAlive && m.RenderKind != AssetRenderKind.None && m.Category == SelectionCategory.Moving && EntityManager.HasComponent<Game.Objects.Transform>(m.Entity))
                {
                    float3 livePos = EntityManager.GetComponentData<Game.Objects.Transform>(m.Entity).m_Position;
                    if (math.all(math.isfinite(livePos)))
                    {
                        pos = livePos;
                    }
                }

                bool inView = !needViewport || IsAssetInViewport(camera, pos, m.RenderKind == AssetRenderKind.AreaOutline ? 80f : 0f);
                float dx = pos.x - cursor.x;
                float dz = pos.z - cursor.z;

                m_AssetCandidates.Add(entityAlive
                    ? AssetOverlayLogic.BuildCandidate(m.RenderKind, m.PointCount, pos.x, pos.z, dx * dx + dz * dz, inView)
                    : AssetOverlayLogic.BuildCandidate(AssetRenderKind.None, 0, pos.x, pos.z, 0f, false));
            }

            m_LastProjectedPrimitives = AssetOverlayLogic.AllocateProjectedBudget(
                m_AssetCandidates,
                budget,
                m_AssetScratchOrder,
                m_AssetProjectedFlags,
                m_ProjectedAssetIndices);

            m_NextFallbackEntities.Clear();
            for (int i = 0; i < m_AssetMarkers.Count; i++)
            {
                if (!m_AssetProjectedFlags[i])
                {
                    m_NextFallbackEntities.Add(m_AssetMarkers[i].Entity);
                }
            }

            bool changed = !m_OverflowFallbackEntities.SetEquals(m_NextFallbackEntities);
            if (changed)
            {
                m_OverflowFallbackEntities.Clear();
                foreach (Entity e in m_NextFallbackEntities)
                {
                    m_OverflowFallbackEntities.Add(e);
                }
            }
            return changed;
        }

        private bool IsAssetInViewport(Camera camera, float3 world, float marginMeters)
        {
            if (marginMeters > 0f && math.distancesq(world, m_LastAssetRefreshCamPos) < marginMeters * marginMeters)
            {
                return true;
            }

            return IsInBuildingViewport(camera, world);
        }

        private void DrawAssetOverlays(
            ref OverlayRenderSystem.Buffer buffer,
            in ResolvedOverlayColors colors)
        {
            if (Mod.Settings != null && Mod.Settings.OverlayDetail == OverlayDetailLevel.Minimal)
            {
                return;
            }

            if (m_ProjectedAssetIndices.Count == 0)
            {
                return;
            }

            float outlineWidth = colors.OutlineAlpha <= 0.001f ? 0f : 0.2f;
            float curveOutlineWidth = colors.OutlineAlpha <= 0.001f ? 0f : 0.25f;
            UnityEngine.Color netFill = new UnityEngine.Color(colors.AssetNetwork.r, colors.AssetNetwork.g, colors.AssetNetwork.b, 0.85f);
            UnityEngine.Color areaFill = new UnityEngine.Color(colors.AssetArea.r, colors.AssetArea.g, colors.AssetArea.b, 0.90f);
            float circleAlpha = math.clamp(colors.BuildingFillAlpha * 1.15f, 0.45f, 0.92f);

            for (int i = 0; i < m_ProjectedAssetIndices.Count; i++)
            {
                int markerIdx = m_ProjectedAssetIndices[i];
                if (markerIdx < 0 || markerIdx >= m_AssetMarkers.Count)
                {
                    continue;
                }

                AssetMarker marker = m_AssetMarkers[markerIdx];
                if (!EntityManager.Exists(marker.Entity) || EntityManager.HasComponent<Deleted>(marker.Entity))
                {
                    continue;
                }

                switch (marker.RenderKind)
                {
                    case AssetRenderKind.Curve:
                        buffer.DrawCurve(
                            colors.DarkOutline,
                            netFill,
                            curveOutlineWidth,
                            OverlayRenderSystem.StyleFlags.Projected,
                            marker.Bezier,
                            marker.SizeOrWidth);
                        break;

                    case AssetRenderKind.Circle:
                    {
                        float3 pos = marker.Position;
                        if (marker.Category == SelectionCategory.Moving &&
                            EntityManager.HasComponent<Game.Objects.Transform>(marker.Entity))
                        {
                            float3 livePos = EntityManager.GetComponentData<Game.Objects.Transform>(marker.Entity).m_Position;
                            if (math.all(math.isfinite(livePos)))
                            {
                                pos = livePos;
                            }
                        }

                        UnityEngine.Color c = GetCategoryColor(marker.Category, in colors);
                        UnityEngine.Color fill = new UnityEngine.Color(c.r, c.g, c.b, circleAlpha);
                        buffer.DrawCircle(
                            colors.DarkOutline,
                            fill,
                            outlineWidth,
                            OverlayRenderSystem.StyleFlags.Projected,
                            new float2(0f, 1f),
                            pos + new float3(0f, 0.1f, 0f),
                            marker.SizeOrWidth);
                        break;
                    }

                    case AssetRenderKind.AreaOutline:
                        DrawAreaOutline(ref buffer, marker, areaFill, colors.DarkOutline, outlineWidth);
                        break;
                }
            }
        }

        private void DrawAreaOutline(
            ref OverlayRenderSystem.Buffer buffer,
            in AssetMarker marker,
            UnityEngine.Color fillColor,
            UnityEngine.Color outlineColor,
            float outlineWidth)
        {
            int start = marker.PointOffset;
            int count = marker.PointCount;
            if (count < 2 || start < 0 || start + count > m_AreaOutlinePoints.Count)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                float3 a = m_AreaOutlinePoints[start + i];
                float3 b = m_AreaOutlinePoints[start + ((i + 1) % count)];
                buffer.DrawLine(
                    outlineColor,
                    fillColor,
                    outlineWidth,
                    OverlayRenderSystem.StyleFlags.Projected,
                    new Line3.Segment(a, b),
                    marker.SizeOrWidth,
                    float2.zero);
            }
        }

        private static UnityEngine.Color GetCategoryColor(SelectionCategory category, in ResolvedOverlayColors colors)
        {
            switch (category)
            {
                case SelectionCategory.Network: return colors.AssetNetwork;
                case SelectionCategory.Tree: return colors.AssetTree;
                case SelectionCategory.Prop: return colors.AssetProp;
                case SelectionCategory.Decal: return colors.AssetDecal;
                case SelectionCategory.Area: return colors.AssetArea;
                case SelectionCategory.Moving: return colors.AssetMoving;
                default: return colors.AssetProp;
            }
        }

        private static bool IsFiniteBezier(in Bezier4x3 b)
        {
            return math.all(math.isfinite(b.a)) &&
                   math.all(math.isfinite(b.b)) &&
                   math.all(math.isfinite(b.c)) &&
                   math.all(math.isfinite(b.d));
        }
    }
}
