using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MarqueeDelete
{
    internal struct RoadFacilitySite
    {
        internal Entity Node;
        internal RoadFacilitySide Side;
        internal float3 Position;
        internal bool Selected => Node != Entity.Null;
    }

    internal struct RoadFacilityTarget
    {
        internal Entity Entity;
        internal Entity Prefab;
        internal RoadFacilitySite Start;
        internal RoadFacilitySite End;
        internal int Count => (Start.Selected ? 1 : 0) + (End.Selected ? 1 : 0);

        internal bool Matches(in RoadFacilityTarget other) =>
            Entity == other.Entity && Prefab == other.Prefab &&
            Start.Node == other.Start.Node && Start.Side == other.Start.Side &&
            End.Node == other.End.Node && End.Side == other.End.Side;
    }

    internal static class RoadFacilities
    {
        private const CompositionFlags.General Unsupported =
            CompositionFlags.General.Tunnel | CompositionFlags.General.LevelCrossing |
            CompositionFlags.General.Roundabout | CompositionFlags.General.DeadEnd;

        internal static SelectionResult Collect(World world, EntityManager em, in MarqueeShapeXZ shape, RoadFacilityMode mode)
        {
            var result = CreateResult(in shape, mode);
            var search = world.GetExistingSystemManaged<Game.Net.SearchSystem>();
            if (search == null || !shape.IsUsable()) return result;
            var tree = search.GetNetSearchTree(true, out JobHandle deps);
            deps.Complete();
            var iterator = new FacilityIterator(em, result);
            tree.Iterate(ref iterator);
            search.AddNetSearchTreeReader(default(JobHandle));
            return result;
        }

        internal static SelectionResult Revalidate(EntityManager em, SelectionResult reviewed, out bool changed)
        {
            var current = CreateResult(in reviewed.Shape, reviewed.FacilityMode);
            changed = false;
            foreach (var target in reviewed.FacilityTargets)
            {
                if (TryResolve(em, target.Entity, in current.Shape, current.FacilityMode, out var resolved))
                {
                    AddTarget(current, in resolved);
                    changed |= !target.Matches(in resolved);
                }
                else
                {
                    changed = true;
                }
            }
            return current;
        }

        private static SelectionResult CreateResult(in MarqueeShapeXZ shape, RoadFacilityMode mode) =>
            new SelectionResult { Shape = shape, FacilityMode = mode, FacilityTargets = new List<RoadFacilityTarget>() };

        private static void AddTarget(SelectionResult result, in RoadFacilityTarget target)
        {
            result.FacilityTargets.Add(target);
            result.Breakdown.Networks += target.Count;
        }

        private static bool TryResolve(
            EntityManager em, Entity entity, in MarqueeShapeXZ shape, RoadFacilityMode mode, out RoadFacilityTarget target)
        {
            target = default;
            if (mode == RoadFacilityMode.TrafficLights)
            {
                if (!em.HasComponent<TrafficLights>(entity) || !em.HasComponent<Node>(entity)) return false;
                float3 position = em.GetComponentData<Node>(entity).m_Position;
                const TrafficLightFlags protectedSignals =
                    TrafficLightFlags.LevelCrossing | TrafficLightFlags.MoveableBridge | TrafficLightFlags.IsSubNode;
                if (!shape.ContainsPoint(new CoordXZ(position.x, position.z)) ||
                    (em.GetComponentData<TrafficLights>(entity).m_Flags & protectedSignals) != 0 ||
                    !IsEditableJunction(em, entity, requireSignals: true))
                    return false;

                target.Start = new RoadFacilitySite { Node = entity, Position = position };
            }
            else
            {
                if (!em.HasComponent<Edge>(entity) || !em.HasComponent<Road>(entity) ||
                    !em.HasComponent<Composition>(entity) || !IsEditable(em, entity) ||
                    !em.HasComponent<StartNodeGeometry>(entity) || !em.HasComponent<EndNodeGeometry>(entity))
                    return false;

                var edge = em.GetComponentData<Edge>(entity);
                var composition = em.GetComponentData<Composition>(entity);
                var prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (!em.HasComponent<NetGeometryData>(prefab)) return false;
                Layer layers = em.GetComponentData<NetGeometryData>(prefab).m_MergeLayers;
                var start = FindCrosswalk(em, edge.m_Start, composition.m_StartNode, layers,
                    em.GetComponentData<StartNodeGeometry>(entity).m_Geometry, in shape, out var startSlot);
                var end = FindCrosswalk(em, edge.m_End, composition.m_EndNode, layers,
                    em.GetComponentData<EndNodeGeometry>(entity).m_Geometry, in shape, out var endSlot);
                if (!start.Selected && !end.Selected) return false;
                // Custom geometries that map both ends onto the same upgrade bit cannot be edited independently.
                if (startSlot != RoadFacilitySide.None && startSlot == endSlot) return false;
                target.Start = start;
                target.End = end;
            }

            target.Entity = entity;
            target.Prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
            return true;
        }

        private static RoadFacilitySite FindCrosswalk(
            EntityManager em, Entity node, Entity composition, Layer mergeLayers, EdgeNodeGeometry geometry,
            in MarqueeShapeXZ shape, out RoadFacilitySide endpointSlot)
        {
            endpointSlot = RoadFacilitySide.None;
            if (!em.HasComponent<NetCompositionData>(composition)) return default;
            var flags = em.GetComponentData<NetCompositionData>(composition).m_Flags.m_General;
            bool inverted = (flags & CompositionFlags.General.Invert) != 0;
            endpointSlot = inverted ? RoadFacilitySide.Left : RoadFacilitySide.Right;
            if ((flags & (Unsupported | CompositionFlags.General.Crosswalk)) != CompositionFlags.General.Crosswalk ||
                !em.HasBuffer<NetCompositionCrosswalk>(composition) ||
                em.GetBuffer<NetCompositionCrosswalk>(composition, true).Length == 0)
                return default;

            float3 position = (geometry.m_Left.m_Left.a + geometry.m_Right.m_Right.a) * 0.5f;
            var side = RoadFacilityPolicy.CrosswalkSlot(in shape, new CoordXZ(position.x, position.z), true, inverted);
            if (side == RoadFacilitySide.None || !IsEditableJunction(em, node, requireSignals: false)) return default;
            bool hasSignals = em.HasComponent<TrafficLights>(node);
            int twoWayRoads = hasSignals ? CountTwoWayRoadApproaches(em, node, mergeLayers) : 0;
            if (!RoadFacilityPolicy.IsIndependentCrosswalk((flags & CompositionFlags.General.Intersection) != 0,
                    (flags & CompositionFlags.General.MedianBreak) != 0, hasSignals, twoWayRoads))
                return default;
            return new RoadFacilitySite { Node = node, Side = side, Position = position };
        }

        private static int CountTwoWayRoadApproaches(EntityManager em, Entity node, Layer mergeLayers)
        {
            const Game.Prefabs.RoadFlags oneWay =
                Game.Prefabs.RoadFlags.DefaultIsForward | Game.Prefabs.RoadFlags.DefaultIsBackward;
            int count = 0;
            foreach (var connection in em.GetBuffer<ConnectedEdge>(node, true))
            {
                Entity edge = connection.m_Edge;
                if (!em.HasComponent<Road>(edge) || !IsEditable(em, edge)) continue;
                Entity prefab = em.GetComponentData<PrefabRef>(edge).m_Prefab;
                if (em.HasComponent<RoadData>(prefab) && em.HasComponent<NetGeometryData>(prefab) &&
                    (em.GetComponentData<RoadData>(prefab).m_Flags & oneWay) == 0 &&
                    (em.GetComponentData<NetGeometryData>(prefab).m_MergeLayers & mergeLayers) != 0)
                    count++;
            }
            return count;
        }

        private static bool IsEditableJunction(EntityManager em, Entity node, bool requireSignals)
        {
            if (!IsEditable(em, node) || !em.HasBuffer<ConnectedEdge>(node)) return false;
            bool supportedRoad = false;
            foreach (var connection in em.GetBuffer<ConnectedEdge>(node, true))
            {
                var edgeEntity = connection.m_Edge;
                if (!em.HasComponent<Edge>(edgeEntity) || IsOutsideConnection(em, edgeEntity)) return false;
                if (!em.HasComponent<Road>(edgeEntity) || !em.HasComponent<Composition>(edgeEntity)) continue;
                var edge = em.GetComponentData<Edge>(edgeEntity);
                var composition = em.GetComponentData<Composition>(edgeEntity);
                var end = edge.m_Start == node ? composition.m_StartNode : composition.m_EndNode;
                if (!em.HasComponent<NetCompositionData>(end)) continue;
                var flags = em.GetComponentData<NetCompositionData>(end).m_Flags.m_General;
                if ((flags & Unsupported) != 0) return false;
                supportedRoad |= !requireSignals || (flags & CompositionFlags.General.TrafficLights) != 0;
            }
            return supportedRoad;
        }

        private static bool IsOutsideConnection(EntityManager em, Entity entity) =>
            em.HasComponent<Game.Net.OutsideConnection>(entity) || em.HasComponent<Game.Objects.OutsideConnection>(entity);

        private static bool IsEditable(EntityManager em, Entity entity) =>
            em.HasComponent<PrefabRef>(entity) && !em.HasComponent<Deleted>(entity) &&
            !em.HasComponent<Temp>(entity) && !em.HasComponent<Hidden>(entity) &&
            !em.HasComponent<Overridden>(entity) && !em.HasComponent<Game.Net.Fixed>(entity) &&
            !em.HasComponent<Owner>(entity) &&
            !IsOutsideConnection(em, entity);

        internal static bool TrySubmit(
            EntityManager em, EntityCommandBuffer buffer, SelectionResult approved,
            in RoadFacilityTarget target, HashSet<Entity> updated)
        {
            if (!TryResolve(em, target.Entity, in approved.Shape, approved.FacilityMode, out var live) ||
                !target.Matches(in live))
                return false;

            var entity = target.Entity;
            bool hadUpgrade = em.HasComponent<Upgraded>(entity);
            var upgrade = hadUpgrade ? em.GetComponentData<Upgraded>(entity) : default;
            if (approved.FacilityMode == RoadFacilityMode.TrafficLights)
            {
                upgrade.m_Flags.m_General = (CompositionFlags.General)RoadFacilityPolicy.WithRemovalOverride(
                    (uint)upgrade.m_Flags.m_General, (uint)CompositionFlags.General.TrafficLights,
                    (uint)CompositionFlags.General.RemoveTrafficLights);
            }
            else
            {
                var sides = live.Start.Side | live.End.Side;
                if ((sides & RoadFacilitySide.Left) != 0)
                    upgrade.m_Flags.m_Left = RemoveCrosswalk(upgrade.m_Flags.m_Left);
                if ((sides & RoadFacilitySide.Right) != 0)
                    upgrade.m_Flags.m_Right = RemoveCrosswalk(upgrade.m_Flags.m_Right);
            }

            if (hadUpgrade) buffer.SetComponent(entity, upgrade);
            else buffer.AddComponent(entity, upgrade);
            MarkUpdated(em, buffer, entity, updated);
            MarkJunctionUpdated(em, buffer, live.Start.Node, updated);
            MarkJunctionUpdated(em, buffer, live.End.Node, updated);
            return true;
        }

        private static CompositionFlags.Side RemoveCrosswalk(CompositionFlags.Side flags) =>
            (CompositionFlags.Side)RoadFacilityPolicy.WithRemovalOverride(
                (uint)flags, (uint)CompositionFlags.Side.AddCrosswalk, (uint)CompositionFlags.Side.RemoveCrosswalk);

        private static void MarkJunctionUpdated(EntityManager em, EntityCommandBuffer buffer, Entity node, HashSet<Entity> updated)
        {
            if (node == Entity.Null) return;
            MarkUpdated(em, buffer, node, updated);
            foreach (var connection in em.GetBuffer<ConnectedEdge>(node, true))
                MarkUpdated(em, buffer, connection.m_Edge, updated);
        }

        private static void MarkUpdated(EntityManager em, EntityCommandBuffer buffer, Entity entity, HashSet<Entity> updated)
        {
            if (updated.Add(entity) && !em.HasComponent<Deleted>(entity)) buffer.AddComponent<Updated>(entity);
        }

        private struct FacilityIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            private readonly EntityManager m_EntityManager;
            private readonly SelectionResult m_Result;
            private readonly HashSet<Entity> m_Seen;

            internal FacilityIterator(EntityManager entityManager, SelectionResult result)
            {
                m_EntityManager = entityManager;
                m_Result = result;
                m_Seen = new HashSet<Entity>();
            }

            public bool Intersect(QuadTreeBoundsXZ bounds) =>
                m_Result.Shape.IntersectsAabb(bounds.m_Bounds.min.x, bounds.m_Bounds.max.x,
                    bounds.m_Bounds.min.z, bounds.m_Bounds.max.z);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (Intersect(bounds) && m_Seen.Add(entity) &&
                    TryResolve(m_EntityManager, entity, in m_Result.Shape, m_Result.FacilityMode, out var target))
                    AddTarget(m_Result, in target);
            }
        }
    }

    public sealed partial class MarqueeDeleteToolSystem
    {
        private const int RoadFacilityBatchSize = 12;
        private const int RoadFacilityMarkerLimit = 256;
        private readonly HashSet<Entity> m_FacilityUpdated = new HashSet<Entity>();
        private SelectionResult m_FacilityBatch;
        private int m_FacilityCursor;
        private int m_FacilitySubmitted;

        internal RoadFacilityMode FacilityMode { get; private set; }
        internal bool IsEditingRoadFacilities => FacilityMode != RoadFacilityMode.None;
        internal bool RoadFacilitiesBusy => m_FacilityBatch != null;

        internal bool ToggleRoadFacilityMode(RoadFacilityMode mode)
        {
            if ((mode != RoadFacilityMode.TrafficLights && mode != RoadFacilityMode.Crosswalks) ||
                !IsToolActive || m_Underground || !CanSubmitDeletionNow || HasQueuedDeletion || HasActiveUndo ||
                m_InterruptedDeletion != null || m_Session.IsBusy || ColorModalOpen || ImportantModalOpen)
                return false;

            ClearPending();
            FacilityMode = FacilityMode == mode ? RoadFacilityMode.None : mode;
            PlayUiSound(selectEntity: IsEditingRoadFacilities);
            return true;
        }

        private void ConfirmRoadFacilities(SelectionResult reviewed)
        {
            if (reviewed.FacilityMode != FacilityMode || m_Underground || !CanSubmitDeletionNow)
            {
                SelectionNotice = "deletion_blocked_compat";
                return;
            }

            var current = RoadFacilities.Revalidate(EntityManager, reviewed, out bool changed);
            if (changed)
            {
                SetPending(current, changed: true);
                return;
            }

            SetPending(null);
            m_FacilityBatch = current;
            m_FacilityCursor = 0;
            m_FacilitySubmitted = 0;
            SelectionNotice = "facility_running";
        }

        private void AdvanceRoadFacilities()
        {
            var batch = m_FacilityBatch;
            int frameCount = UnityEngine.Time.frameCount;
            bool escapeStopped = Keyboard.current?.escapeKey.wasPressedThisFrame == true &&
                                 !GlobalUndoPanelOpen && !ColorModalOpen && !m_ImportantModalOpen &&
                                 !m_EscapeGate.IsConsumedThisFrame(frameCount) &&
                                 m_EscapeGate.TryConsume(frameCount);
            bool stopped = escapeStopped || !CanSubmitDeletionNow;
            if (!stopped)
            {
                var buffer = m_DeletionBarrier.CreateCommandBuffer();
                m_FacilityUpdated.Clear();
                int end = System.Math.Min(m_FacilityCursor + RoadFacilityBatchSize, batch.FacilityTargets.Count);
                for (; m_FacilityCursor < end; m_FacilityCursor++)
                {
                    var target = batch.FacilityTargets[m_FacilityCursor];
                    if (RoadFacilities.TrySubmit(EntityManager, buffer, batch, in target, m_FacilityUpdated))
                        m_FacilitySubmitted += target.Count;
                }
                if (m_FacilityCursor < batch.FacilityTargets.Count) return;
            }

            m_FacilityBatch = null;
            SelectionNotice = $"facility_result|{m_FacilitySubmitted}|{batch.Count - m_FacilitySubmitted}";
        }

        private static void DrawRoadFacilityMarkers(
            ref OverlayRenderSystem.Buffer buffer, SelectionResult selection, in ResolvedOverlayColors colors)
        {
            if (selection?.FacilityTargets == null) return;
            var fill = selection.FacilityMode == RoadFacilityMode.TrafficLights
                ? new Color(1f, 0.48f, 0.27f, 0.96f)
                : new Color(1f, 0.83f, 0.26f, 0.96f);
            int count = System.Math.Min(selection.FacilityTargets.Count, RoadFacilityMarkerLimit);
            for (int i = 0; i < count; i++)
            {
                var target = selection.FacilityTargets[i];
                if (target.Start.Selected) DrawFacilityMarker(ref buffer, target.Start.Position, fill, colors.DarkOutline);
                if (target.End.Selected) DrawFacilityMarker(ref buffer, target.End.Position, fill, colors.DarkOutline);
            }
        }

        private static void DrawFacilityMarker(ref OverlayRenderSystem.Buffer buffer, float3 position, Color fill, Color border)
        {
            buffer.DrawCircle(border, fill, 0.3f, OverlayRenderSystem.StyleFlags.Projected,
                new float2(0f, 1f), position + new float3(0f, 0.12f, 0f), 3.2f);
        }
    }
}
