using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal.Mathematics;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Creatures;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Game.Vehicles;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using NetElevation = Game.Net.Elevation;
using NetNode = Game.Net.Node;
using NetSubLane = Game.Net.SubLane;
using ObjElevation = Game.Objects.Elevation;
using GameObject = Game.Objects.Object;

namespace MarqueeDelete
{
    public sealed class MarqueeDeleteUndoSystem : GameSystemBase
    {
        private MarqueeDeleteToolSystem m_ToolSystem;
        private ToolOutputBarrier m_OutputBarrier;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<MarqueeDeleteToolSystem>();
            m_OutputBarrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
        }

        protected override void OnUpdate()
        {
            if (m_ToolSystem != null && m_OutputBarrier != null)
            {
                Dependency.Complete();
                m_ToolSystem.TickUndo(m_OutputBarrier);
            }
        }
    }

    internal sealed class DeletionUndoManager
    {
        public const int MaxUndoSnapshots = 5;
        public const int MaxEntitiesPerSnapshot = 8000;
        private const int MaxRequestsPerStep = 16;
        private const double StepTimeBudgetMs = 3.0;

        private enum UndoOperationKind { Boundary, Net, Area, Building, Upgrade, Object }
        private struct UndoOperation
        {
            public UndoOperationKind Kind;
            public UndoNetRecord Net;
            public UndoAreaRecord Area;
            public UndoBuildingRecord Building;
            public UndoUpgradeRecord Upgrade;
            public UndoObjectRecord Object;
            public bool HasOwner;
            public Entity AncestorPrefab;
            public OwnerDefinition Ancestor;
            public OwnerDefinition Owner;
        }

        private struct UndoNetRecord
        {
            public Entity Prefab;
            public Entity StartNode;
            public Entity EndNode;
            public float3 StartPos;
            public quaternion StartRot;
            public float2 StartElevation;
            public float3 EndPos;
            public quaternion EndRot;
            public float2 EndElevation;
            public Bezier4x3 Curve;
            public float Length;
            public float2 EdgeElevation;
            public bool HasUpgraded;
            public CompositionFlags UpgradedFlags;
            public int Seed;
            /// <summary>Edges meeting the recorded start node when the segment was captured (-1 unknown).</summary>
            public int StartLinks;
            /// <summary>Edges meeting the recorded end node when the segment was captured (-1 unknown).</summary>
            public int EndLinks;
            /// <summary>Lanes the segment carried at capture (-1 when unreadable). See <see cref="LaneOwnershipPolicy"/>.</summary>
            public int LaneCount;
            /// <summary>Lanes of those that named this segment as their owner at capture.</summary>
            public int LaneOwned;
            /// <summary>Lanes of those that named another road as their owner at capture.</summary>
            public int LaneForeign;
        }

        private struct UndoAreaRecord
        {
            public Entity Prefab;
            public Game.Areas.Node[] Nodes;
            public int Seed;
        }

        private struct UndoUpgradeRecord
        {
            public Entity Prefab;
            public float3 Position;
            public quaternion Rotation;
            public bool HasElevation;
            public float Elevation;
            public ElevationFlags ElevationFlags;
            public int Seed;
            public List<UndoAreaRecord> SubAreas;
            public List<UndoNetRecord> SubNets;
        }

        private struct UndoBuildingRecord
        {
            public Entity Prefab;
            public float3 Position;
            public quaternion Rotation;
            public bool HasElevation;
            public float Elevation;
            public ElevationFlags ElevationFlags;
            public Entity AttachedParent;
            public int Seed;
            public List<UndoUpgradeRecord> Upgrades;
            public List<UndoAreaRecord> SubAreas;
            public List<UndoNetRecord> SubNets;
        }

        private struct UndoObjectRecord
        {
            public Entity Prefab;
            public SelectionCategory Category;
            public float3 Position;
            public quaternion Rotation;
            public bool HasElevation;
            public float Elevation;
            public ElevationFlags ElevationFlags;
            public bool IsDecoration;
            public bool IsTree;
            public TreeState TreeState;
            public byte TreeGrowth;
            public Entity AttachedParent;
            public int Seed;
        }

        private sealed class UndoSnapshot : IUndoSnapshotMeta
        {
            public readonly List<UndoNetRecord> Nets = new List<UndoNetRecord>();
            public readonly List<UndoAreaRecord> Areas = new List<UndoAreaRecord>();
            public readonly List<UndoBuildingRecord> Buildings = new List<UndoBuildingRecord>();
            public readonly List<UndoObjectRecord> Objects = new List<UndoObjectRecord>();

            public UndoCategoryCounts Counts;
            public UndoCategoryCounts NotCaptured;
            public int TotalCount;
            public int SkippedCount;
            public bool HasOwnerTimeout;
            public IEnumerator<UndoOperation> Operations;
            public bool Truncated;

            public int RestoredCount;

            UndoCategoryCounts IUndoSnapshotMeta.Counts => Counts;
            UndoCategoryCounts IUndoSnapshotMeta.NotCaptured => NotCaptured;
            int IUndoSnapshotMeta.TotalCount => TotalCount;
            bool IUndoSnapshotMeta.Truncated => Truncated;
            bool IUndoSnapshotMeta.HasOwnerTimeout => HasOwnerTimeout;
            int IUndoSnapshotMeta.RestoredCount => RestoredCount;
            int IUndoSnapshotMeta.SkippedCount => SkippedCount;
        }

        private UndoSnapshot m_DeletionCapture;
        private readonly SelectiveUndoTracker<UndoSnapshot> m_Tracker = new SelectiveUndoTracker<UndoSnapshot>(MaxUndoSnapshots);
        private int m_NextAllowedFrame;
        private int m_LastStepFrame = -1;
        private int m_LastObservedFrame = -1;

        private UndoOperation? m_WaitingOperation;
        private int m_OwnerWaitStarted = -1;
        private readonly HashSet<OwnerDefinition> m_FailedOwners = new HashSet<OwnerDefinition>();
        private readonly Dictionary<OwnerDefinition, Entity> m_ResolvedOwners = new Dictionary<OwnerDefinition, Entity>();
        private readonly Dictionary<Entity, List<Entity>> m_OwnerIndex = new Dictionary<Entity, List<Entity>>();
        private OwnerCandidate[] m_OwnerCandidates = new OwnerCandidate[32];
        private readonly HashSet<Entity> m_OwnerPrefabs = new HashSet<Entity>();
        private int m_OwnerIndexFrame = -1;

        /// <summary>
        /// A same-model building that already stood somewhere before this undo started. Matching one
        /// of these means the host we found is not the one this undo created, so children are not
        /// attached to it and a building is not rebuilt on top of it.
        /// </summary>
        private struct PreExistingOwner
        {
            public Entity Prefab;
            public float3 Position;
        }

        private readonly List<PreExistingOwner> m_PreExistingOwners = new List<PreExistingOwner>();
        private bool m_NeedPreExistingSnapshot;
        private int m_AmbiguousOwners;
        private int m_ConnectivitySkips;
        private int m_OccupiedRestores;
        private int m_LaneUnconfirmed;
        private UndoBatchPolicy m_BatchPolicy = new UndoBatchPolicy();

        public bool HasActiveUndo => m_Tracker.HasActiveUndo;
        public bool IsInProgress => m_Tracker.IsInProgress;
        public SelectiveUndoUiSnapshot GetUiSnapshot(UndoAssetMask mask) => m_Tracker.GetUiSnapshot(mask);

        public void ClearFinishedNotice()
        {
            m_Tracker.ClearFinishedNotice();
        }

        public bool DismissFinishedNotice()
        {
            return m_Tracker.DismissNotices();
        }

        public bool StopActiveUndo()
        {
            if (!HasActiveUndo) return false;

            m_Tracker.ActiveSnapshot?.Operations?.Dispose();
            m_Tracker.FailActive("UndoStopped");
            m_WaitingOperation = null;
            m_OwnerWaitStarted = -1;
            m_ResolvedOwners.Clear();
            m_FailedOwners.Clear();
            m_OwnerPrefabs.Clear();
            m_OwnerIndex.Clear();
            m_OwnerIndexFrame = -1;
            m_PreExistingOwners.Clear();
            m_NeedPreExistingSnapshot = false;
            return true;
        }

        public void ClearHistory()
        {
            m_Tracker.ActiveSnapshot?.Operations?.Dispose();
            m_Tracker.ClearAll();
            m_DeletionCapture = null;
            m_ResolvedOwners.Clear();
            m_FailedOwners.Clear();
            m_OwnerWaitStarted = -1;
            m_OwnerIndex.Clear();
            m_OwnerIndexFrame = -1;
            m_WaitingOperation = null;
            m_NextAllowedFrame = 0;
            m_LastStepFrame = -1;
            m_PreExistingOwners.Clear();
            m_NeedPreExistingSnapshot = false;
        }

        public void CaptureBeforeDelete(EntityManager em, DeletionPlan plan, bool deferCommit = false)
        {
            if (plan == null || plan.Submitted.Count == 0)
            {
                return;
            }

            if (HasActiveUndo) return;
            var snapshot = m_DeletionCapture ?? new UndoSnapshot();
            var seen = new HashSet<Entity>();
            int recordedWeight = snapshot.TotalCount;
            int buildingNotCaptured = 0;

            var rootsToCapture = plan.Roots ?? plan.Submitted;
            foreach (Entity entity in rootsToCapture)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot)
                {
                    snapshot.Truncated = true;
                    break;
                }

                if (entity == Entity.Null || !seen.Add(entity) || !em.Exists(entity) || em.HasComponent<Deleted>(entity))
                {
                    continue;
                }

                // Never recreate transient simulation entities (vehicles, citizens, animals).
                if (em.HasComponent<Vehicle>(entity) || em.HasComponent<Creature>(entity))
                {
                    continue;
                }

                // Root nodes are recreated automatically from restored Edge NetCourse records.
                if (em.HasComponent<NetNode>(entity) && !em.HasComponent<Edge>(entity))
                {
                    continue;
                }

                if (em.HasComponent<Edge>(entity))
                {
                    bool hasOwner = em.HasComponent<Owner>(entity);
                    bool ownerInPlan = false;
                    if (hasOwner)
                    {
                        Entity owner = em.GetComponentData<Owner>(entity).m_Owner;
                        ownerInPlan = owner != Entity.Null && (plan.Submitted.Contains(owner) || (plan.Roots != null && plan.Roots.Contains(owner)));
                    }

                    if (em.HasComponent<Attached>(entity))
                    {
                        Entity parent = em.GetComponentData<Attached>(entity).m_Parent;
                        if (parent != Entity.Null && (plan.Submitted.Contains(parent) || (plan.Roots != null && plan.Roots.Contains(parent))))
                        {
                            continue;
                        }
                    }

                    if (!SelectiveUndoLogic.ShouldCaptureEdgeAsStandalone(hasOwner, ownerInPlan))
                    {
                        continue;
                    }

                    if (TryCaptureNetRecord(em, entity, out UndoNetRecord netRec))
                    {
                        snapshot.Nets.Add(netRec);
                        snapshot.Counts.Networks++;
                        recordedWeight++;
                    }
                    else
                    {
                        snapshot.NotCaptured.Networks++;
                    }
                    continue;
                }

                if (em.HasComponent<Area>(entity))
                {
                    if (em.HasComponent<MapTile>(entity) || em.HasComponent<District>(entity) || em.HasComponent<Owner>(entity))
                    {
                        continue;
                    }

                    if (TryCaptureAreaRecord(em, entity, out UndoAreaRecord areaRec))
                    {
                        snapshot.Areas.Add(areaRec);
                        snapshot.Counts.Areas++;
                        recordedWeight++;
                    }
                    else
                    {
                        snapshot.NotCaptured.Areas++;
                    }
                    continue;
                }

                if (em.HasComponent<Building>(entity))
                {
                    if (em.HasComponent<Owner>(entity))
                    {
                        continue;
                    }

                    int weightBefore = recordedWeight;
                    if (TryCaptureBuildingRecord(em, entity, ref recordedWeight, ref snapshot.Truncated, ref buildingNotCaptured, out UndoBuildingRecord bldRec))
                    {
                        snapshot.Buildings.Add(bldRec);
                        snapshot.Counts.Buildings += (recordedWeight - weightBefore);
                    }
                    else
                    {
                        buildingNotCaptured++;
                    }
                    continue;
                }

                if (em.HasComponent<GameObject>(entity) && !em.HasComponent<Owner>(entity))
                {
                    SelectionCategory objCategory = ClassifyObjectCategory(em, entity);
                    if (SelectiveUndoClassifier.ToMaskBit(objCategory) == UndoAssetMask.None)
                    {
                        continue;
                    }

                    if (TryCaptureObjectRecord(em, entity, objCategory, out UndoObjectRecord objRec))
                    {
                        snapshot.Objects.Add(objRec);
                        snapshot.Counts.Add(objRec.Category, 1);
                        recordedWeight++;
                    }
                    else
                    {
                        snapshot.NotCaptured.Add(objCategory, 1);
                    }
                }
            }

            snapshot.NotCaptured.Buildings += buildingNotCaptured;
            snapshot.TotalCount = recordedWeight;

            if (deferCommit)
                m_DeletionCapture = snapshot;
            else
            {
                if (Mod.Settings != null) m_Tracker.MaxHistory = Mod.Settings.MaxUndoHistorySteps;
                m_Tracker.OnCaptureCompleted(snapshot);
            }
        }

        public void CommitDeletionCapture()
        {
            if (m_DeletionCapture != null)
            {
                if (Mod.Settings != null) m_Tracker.MaxHistory = Mod.Settings.MaxUndoHistorySteps;
                m_Tracker.OnCaptureCompleted(m_DeletionCapture);
                m_DeletionCapture = null;
            }
        }

        public bool StartUndo(UndoAssetMask mask)
        {
            if (!m_Tracker.TryStartOrResume(mask, out UndoSnapshot snap, out UndoAssetMask activeMask, out bool isResume))
            {
                return false;
            }

            if (isResume)
            {
                if (m_WaitingOperation.HasValue) m_OwnerWaitStarted = UnityEngine.Time.frameCount;
                return true;
            }

            m_ResolvedOwners.Clear();
            m_FailedOwners.Clear();
            m_OwnerWaitStarted = -1;
            m_OwnerIndex.Clear();
            m_OwnerIndexFrame = -1;
            m_WaitingOperation = null;
            m_BatchPolicy = new UndoBatchPolicy();
            m_OwnerPrefabs.Clear();
            m_PreExistingOwners.Clear();
            m_NeedPreExistingSnapshot = true;
            m_AmbiguousOwners = 0;
            m_ConnectivitySkips = 0;
            m_OccupiedRestores = 0;
            m_LaneUnconfirmed = 0;
            if ((activeMask & UndoAssetMask.Buildings) != 0)
            {
                foreach (var building in snap.Buildings)
                {
                    m_OwnerPrefabs.Add(building.Prefab);
                    if (building.Upgrades != null) foreach (var upgrade in building.Upgrades) m_OwnerPrefabs.Add(upgrade.Prefab);
                }
            }
            snap.Operations = EnumerateOperations(snap, activeMask).GetEnumerator();
            m_NextAllowedFrame = UnityEngine.Time.frameCount;
            return true;
        }

        public bool CancelActiveUndo()
        {
            return m_Tracker.Pause();
        }

        // Counts successful creation requests, never claims that simulation state was restored.
        public bool Step(EntityManager em, EntityCommandBuffer ecb, out bool completed)
        {
            completed = false;
            if (!IsInProgress) return false;
            int frame = UnityEngine.Time.frameCount;
            if (frame != m_LastObservedFrame)
            {
                m_BatchPolicy.ObserveFrame(UnityEngine.Time.unscaledDeltaTime);
                m_LastObservedFrame = frame;
            }
            if (frame == m_LastStepFrame || frame < m_NextAllowedFrame) return false;

            // Hard gate at the actual write boundary: this is the only place that emits rebuilt
            // entities, so no path can restore into a build that was not verified for rebuilding.
            if (!MarqueeDeleteToolSystem.CanRebuildNow)
            {
                MarqueeDeleteToolSystem.AppendStatusLine("[State: undo-blocked, Reason: UndoRebuild " + MarqueeDeleteToolSystem.TierFor(CompatibilityGroup.UndoRebuild) + ", Stage: emit]");
                m_WaitingOperation = null;
                m_Tracker.FailActive("UndoUnavailable");
                completed = true;
                return true;
            }

            m_LastStepFrame = frame;
            EnsurePreExistingSnapshot(em);
            var snap = m_Tracker.ActiveSnapshot;
            long started = Stopwatch.GetTimestamp();
            double budget = StepTimeBudgetMs * Stopwatch.Frequency / 1000.0;
            int attempted = 0, buildings = 0, networks = 0;
            bool waiting = false, saturated = false;
            while (attempted < MaxRequestsPerStep)
            {
                if (!m_WaitingOperation.HasValue && !snap.Operations.MoveNext())
                {
                    snap.Operations.Dispose();
                    m_Tracker.CompleteActive();
                    LogRestoreAudit(snap);
                    completed = true;
                    return true;
                }
                UndoOperation op = m_WaitingOperation ?? snap.Operations.Current;
                if (op.Kind == UndoOperationKind.Boundary) break;
                bool buildingRequest = op.Kind == UndoOperationKind.Building || op.Kind == UndoOperationKind.Upgrade;
                bool networkRequest = op.Kind == UndoOperationKind.Net || op.Kind == UndoOperationKind.Area;
                if (attempted >= m_BatchPolicy.SimpleLimit ||
                    (buildingRequest && buildings >= m_BatchPolicy.BuildingLimit) ||
                    (networkRequest && networks >= m_BatchPolicy.NetworkLimit))
                {
                    m_WaitingOperation = op;
                    m_OwnerWaitStarted = -1;
                    saturated = true;
                    break;
                }
                Entity owner = Entity.Null;
                bool ownerValid = !op.HasOwner || TryResolveOwner(em, op.Owner, out owner);
                if (ownerValid && op.AncestorPrefab != Entity.Null)
                    ownerValid = TryResolveOwner(em, op.Ancestor, out _);
                bool knownFailure = op.HasOwner && (m_FailedOwners.Contains(op.Owner) ||
                    (op.AncestorPrefab != Entity.Null && m_FailedOwners.Contains(op.Ancestor)));
                if (!ownerValid && !knownFailure && (m_OwnerWaitStarted < 0 || frame - m_OwnerWaitStarted < 120))
                {
                    if (m_OwnerWaitStarted < 0) m_OwnerWaitStarted = frame;
                    if (frame - m_OwnerWaitStarted >= 30) m_Tracker.SetNotice("UndoWaitingOwner");
                    m_WaitingOperation = op;
                    waiting = true;
                    break;
                }
                if (m_Tracker.Notice == "UndoWaitingOwner")
                {
                    m_Tracker.RestoreActiveNotice();
                }
                m_WaitingOperation = null;
                m_OwnerWaitStarted = -1;
                if (!ownerValid && op.HasOwner)
                {
                    // An owner that could not be proven is not a timeout; the UI must not report it as
                    // one, because waiting longer would never resolve it.
                    if (!m_FailedOwners.Contains(op.Owner))
                    {
                        snap.HasOwnerTimeout = true;
                    }
                    m_FailedOwners.Add(op.Owner);
                }
                bool valid = false;
                if (ownerValid) switch (op.Kind)
                {
                    case UndoOperationKind.Net: valid = ValidPrefab<Game.Prefabs.NetData>(em, op.Net.Prefab) && IsBezierFinite(op.Net.Curve) && NetLinksIntact(em, op.Net) && LaneEvidenceConfirmed(op.Net); if (valid) EmitNetRecord(em, ecb, op.Net, op.HasOwner, owner); break;
                    case UndoOperationKind.Area: valid = ValidPrefab<Game.Prefabs.AreaData>(em, op.Area.Prefab) && op.Area.Nodes != null && op.Area.Nodes.Length >= 3; if (valid) EmitAreaRecord(em, ecb, op.Area, op.HasOwner, owner); break;
                    case UndoOperationKind.Building: valid = ValidObject(em, op.Building.Prefab, op.Building.Position, op.Building.Rotation) && !OccupiedBeforeRestore(op.Building.Prefab, op.Building.Position); if (valid) EmitBuildingRoot(em, ecb, op.Building); break;
                    case UndoOperationKind.Upgrade: valid = ValidObject(em, op.Upgrade.Prefab, op.Upgrade.Position, op.Upgrade.Rotation); if (valid) EmitUpgrade(em, ecb, op.Upgrade, owner); break;
                    case UndoOperationKind.Object: valid = ValidObject(em, op.Object.Prefab, op.Object.Position, op.Object.Rotation); if (valid) EmitObjectRecord(em, ecb, op.Object); break;
                }
                if (valid) snap.RestoredCount++;
                else
                {
                    snap.SkippedCount++;
                    if (op.Kind == UndoOperationKind.Building) m_FailedOwners.Add(new OwnerDefinition { m_Prefab = op.Building.Prefab, m_Position = op.Building.Position, m_Rotation = op.Building.Rotation });
                    if (op.Kind == UndoOperationKind.Upgrade) m_FailedOwners.Add(new OwnerDefinition { m_Prefab = op.Upgrade.Prefab, m_Position = op.Upgrade.Position, m_Rotation = op.Upgrade.Rotation });
                }
                attempted++;
                if (buildingRequest) buildings++;
                if (networkRequest) networks++;
                if (Stopwatch.GetTimestamp() - started >= budget) break;
            }
            double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            m_BatchPolicy.FinishBatch(elapsedMs, saturated || attempted == MaxRequestsPerStep, waiting);
            m_NextAllowedFrame = frame + m_BatchPolicy.FrameGap + 1;
            return true;
        }

        private static IEnumerable<UndoOperation> EnumerateOperations(UndoSnapshot snap, UndoAssetMask mask)
        {
            if ((mask & UndoAssetMask.Networks) != 0)
            {
                foreach (var net in snap.Nets) yield return new UndoOperation { Kind = UndoOperationKind.Net, Net = net };
            }
            if ((mask & UndoAssetMask.Areas) != 0)
            {
                foreach (var area in snap.Areas) yield return new UndoOperation { Kind = UndoOperationKind.Area, Area = area };
            }
            if ((mask & UndoAssetMask.Buildings) != 0)
            {
                foreach (var building in snap.Buildings)
                    yield return new UndoOperation { Kind = UndoOperationKind.Building, Building = building };
                if (snap.Buildings.Count > 0) yield return new UndoOperation { Kind = UndoOperationKind.Boundary };
                foreach (var building in snap.Buildings)
                {
                    var owner = new OwnerDefinition { m_Prefab = building.Prefab, m_Position = building.Position, m_Rotation = building.Rotation };
                    if (building.SubAreas != null) foreach (var area in building.SubAreas) yield return new UndoOperation { Kind = UndoOperationKind.Area, Area = area, HasOwner = true, Owner = owner };
                    if (building.SubNets != null) foreach (var net in building.SubNets) yield return new UndoOperation { Kind = UndoOperationKind.Net, Net = net, HasOwner = true, Owner = owner };
                    if (building.Upgrades != null) foreach (var upgrade in building.Upgrades)
                    {
                        yield return new UndoOperation { Kind = UndoOperationKind.Upgrade, Upgrade = upgrade, HasOwner = true, Owner = owner };
                        if ((upgrade.SubAreas != null && upgrade.SubAreas.Count > 0) || (upgrade.SubNets != null && upgrade.SubNets.Count > 0))
                            yield return new UndoOperation { Kind = UndoOperationKind.Boundary };
                        var upgradeOwner = new OwnerDefinition { m_Prefab = upgrade.Prefab, m_Position = upgrade.Position, m_Rotation = upgrade.Rotation };
                        if (upgrade.SubAreas != null) foreach (var area in upgrade.SubAreas) yield return new UndoOperation { Kind = UndoOperationKind.Area, Area = area, HasOwner = true, AncestorPrefab = building.Prefab, Ancestor = owner, Owner = upgradeOwner };
                        if (upgrade.SubNets != null) foreach (var net in upgrade.SubNets) yield return new UndoOperation { Kind = UndoOperationKind.Net, Net = net, HasOwner = true, AncestorPrefab = building.Prefab, Ancestor = owner, Owner = upgradeOwner };
                    }
                }
            }
            foreach (var obj in snap.Objects)
            {
                UndoAssetMask bit = SelectiveUndoClassifier.ToMaskBit(obj.Category);
                if ((mask & bit) != 0)
                {
                    yield return new UndoOperation { Kind = UndoOperationKind.Object, Object = obj };
                }
            }
        }

        private static bool ValidPrefab<T>(EntityManager em, Entity prefab) where T : unmanaged, IComponentData =>
            prefab != Entity.Null && em.Exists(prefab) && !em.HasComponent<Deleted>(prefab) && em.HasComponent<T>(prefab);

        private static bool ValidObject(EntityManager em, Entity prefab, float3 position, quaternion rotation) =>
            ValidPrefab<ObjectData>(em, prefab) && em.GetComponentData<ObjectData>(prefab).m_Archetype.Valid &&
            IsFinite(position) && IsFinite(rotation) && math.lengthsq(rotation.value) > 0.5f;

        /// <summary>
        /// Records the same-model buildings that already stand before this undo creates anything, so a
        /// later match can tell "the game kept a building there" from "this undo just built it".
        /// </summary>
        private void EnsurePreExistingSnapshot(EntityManager em)
        {
            if (!m_NeedPreExistingSnapshot) return;
            m_NeedPreExistingSnapshot = false;
            m_PreExistingOwners.Clear();
            if (m_OwnerPrefabs.Count == 0) return;

            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.Transform>()))
            using (var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                foreach (Entity candidate in entities)
                {
                    if (em.HasComponent<Deleted>(candidate) || em.HasComponent<Temp>(candidate)) continue;
                    Entity prefab = em.GetComponentData<PrefabRef>(candidate).m_Prefab;
                    if (!m_OwnerPrefabs.Contains(prefab)) continue;
                    var transform = em.GetComponentData<Game.Objects.Transform>(candidate);
                    m_PreExistingOwners.Add(new PreExistingOwner { Prefab = prefab, Position = transform.m_Position });
                }
            }
        }

        private bool IsPreExisting(Entity prefab, float3 position)
        {
            float limit = OccupancyPolicy.OverlapRadius * OccupancyPolicy.OverlapRadius;
            for (int i = 0; i < m_PreExistingOwners.Count; i++)
            {
                PreExistingOwner existing = m_PreExistingOwners[i];
                if (existing.Prefab != prefab) continue;
                if (math.distancesq(existing.Position, position) <= limit) return true;
            }
            return false;
        }

        /// <summary>
        /// A building is not rebuilt where a same-model building already stands: the lot is taken, and
        /// attaching this record's upgrades to that unrelated building would be worse than skipping it.
        /// </summary>
        private bool OccupiedBeforeRestore(Entity prefab, float3 position)
        {
            if (!IsPreExisting(prefab, position)) return false;
            m_OccupiedRestores++;
            return true;
        }

        private bool TryResolveOwner(EntityManager em, OwnerDefinition definition, out Entity owner)
        {
            owner = Entity.Null;
            if (m_FailedOwners.Contains(definition)) return false;
            if (m_ResolvedOwners.TryGetValue(definition, out owner))
            {
                if (OwnerMatches(em, owner, definition)) return true;
                m_ResolvedOwners.Remove(definition);
            }

            if (m_OwnerIndexFrame != UnityEngine.Time.frameCount)
            {
                m_OwnerIndex.Clear();
                using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.Transform>()))
                using (var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                {
                    foreach (Entity candidate in entities)
                    {
                        if (em.HasComponent<Deleted>(candidate) || em.HasComponent<Temp>(candidate)) continue;
                        Entity prefab = em.GetComponentData<PrefabRef>(candidate).m_Prefab;
                        if (!m_OwnerPrefabs.Contains(prefab)) continue;
                        if (!m_OwnerIndex.TryGetValue(prefab, out var matches)) m_OwnerIndex[prefab] = matches = new List<Entity>();
                        matches.Add(candidate);
                    }
                }
                m_OwnerIndexFrame = UnityEngine.Time.frameCount;
            }

            OwnerMatchStatus status = MatchOwner(em, definition, out owner);
            if (status == OwnerMatchStatus.Unique && owner != Entity.Null && em.Exists(owner))
            {
                m_ResolvedOwners[definition] = owner;
                return true;
            }

            // Unique without a live entity is a wiring fault, not a host: never let it through.
            if (status == OwnerMatchStatus.Unique) status = OwnerMatchStatus.NotFound;

            owner = Entity.Null;
            if (status == OwnerMatchStatus.NotFound) return false;

            // Ambiguous, or the only match stood there before this undo: the host cannot be proven to be
            // the object this undo created, so its children are not attached. Recording the failure also
            // stops the wait loop straight away instead of waiting for a host that will never appear.
            m_AmbiguousOwners++;
            m_FailedOwners.Add(definition);
            return false;
        }

        private OwnerMatchStatus MatchOwner(EntityManager em, OwnerDefinition definition, out Entity owner)
        {
            owner = Entity.Null;
            if (!m_OwnerIndex.TryGetValue(definition.m_Prefab, out var candidates) || candidates == null || candidates.Count == 0)
            {
                return OwnerMatchStatus.NotFound;
            }

            // A coarse radius keeps the policy input small; the policy itself applies the exact position
            // and rotation tolerances.
            const float coarseRadiusSq = 16f;
            if (m_OwnerCandidates.Length < candidates.Count)
            {
                m_OwnerCandidates = new OwnerCandidate[candidates.Count];
            }

            int count = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Entity candidate = candidates[i];
                if (!em.Exists(candidate) || em.HasComponent<Deleted>(candidate) || em.HasComponent<Temp>(candidate) ||
                    !em.HasComponent<Game.Objects.Transform>(candidate))
                {
                    continue;
                }

                var transform = em.GetComponentData<Game.Objects.Transform>(candidate);
                if (math.distancesq(transform.m_Position, definition.m_Position) > coarseRadiusSq) continue;

                m_OwnerCandidates[count++] = new OwnerCandidate
                {
                    Slot = i,
                    X = transform.m_Position.x,
                    Y = transform.m_Position.y,
                    Z = transform.m_Position.z,
                    QX = transform.m_Rotation.value.x,
                    QY = transform.m_Rotation.value.y,
                    QZ = transform.m_Rotation.value.z,
                    QW = transform.m_Rotation.value.w,
                    PreExisting = IsPreExisting(definition.m_Prefab, transform.m_Position),
                };
            }

            OwnerMatchStatus status = OwnerMatchPolicy.Resolve(m_OwnerCandidates, count,
                definition.m_Position.x, definition.m_Position.y, definition.m_Position.z,
                definition.m_Rotation.value.x, definition.m_Rotation.value.y, definition.m_Rotation.value.z, definition.m_Rotation.value.w,
                out int slot, out _);

            if (status != OwnerMatchStatus.Unique) return status;

            // The slot indexes the compacted array, whose payload is the source index, so the entity
            // must come from the source list. A resolution that cannot produce a live entity is not a
            // success: attaching children to Entity.Null is exactly the silent mis-binding this guards.
            if (slot < 0 || slot >= count)
            {
                MarqueeDeleteToolSystem.AppendStatusLine("[State: undo-owner-slot-invalid, Slot: " + slot + ", Count: " + count + "]");
                return OwnerMatchStatus.NotFound;
            }

            int sourceIndex = m_OwnerCandidates[slot].Slot;
            if (sourceIndex < 0 || sourceIndex >= candidates.Count)
            {
                MarqueeDeleteToolSystem.AppendStatusLine("[State: undo-owner-source-invalid, Source: " + sourceIndex + ", Count: " + candidates.Count + "]");
                return OwnerMatchStatus.NotFound;
            }

            owner = candidates[sourceIndex];
            if (owner == Entity.Null || !em.Exists(owner))
            {
                MarqueeDeleteToolSystem.AppendStatusLine("[State: undo-owner-dead, Source: " + sourceIndex + "]");
                owner = Entity.Null;
                return OwnerMatchStatus.NotFound;
            }

            return OwnerMatchStatus.Unique;
        }

        /// <summary>
        /// Verifies the junctions this segment was recorded between are still where they were and were
        /// not re-linked by something else, so the rebuild cannot splice into a road that changed.
        /// </summary>
        private bool NetLinksIntact(EntityManager em, in UndoNetRecord rec)
        {
            if (!EndpointIntact(em, rec.StartNode, rec.StartPos, rec.StartLinks) ||
                !EndpointIntact(em, rec.EndNode, rec.EndPos, rec.EndLinks))
            {
                m_ConnectivitySkips++;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Refuses a segment whose lanes were claimed by another road. The game has no lane identity to
        /// compare against — lanes die with the road and come back as new entities — so the only thing
        /// that can be checked is the one relation it does record: which road a lane names as its
        /// <c>Owner</c>. A lane naming another road is a competing claim and the segment is skipped;
        /// lanes naming nobody are the normal case for most networks and are not held against the
        /// segment, which leaves the junction evidence to decide. Unreadable counts refuse as well.
        /// </summary>
        private bool LaneEvidenceConfirmed(in UndoNetRecord rec)
        {
            if (LaneOwnershipPolicy.AllowsRebuild(rec.LaneCount, rec.LaneOwned, rec.LaneForeign))
            {
                return true;
            }

            m_LaneUnconfirmed++;
            return false;
        }

        private static bool EndpointIntact(EntityManager em, Entity node, float3 recorded, int recordedLinks)
        {
            // Nothing was recorded to check against, or the junction is gone and the course is placed by
            // position: either way there is no identity left to contradict, so the rebuild proceeds.
            if (node == Entity.Null || !em.Exists(node) || em.HasComponent<Deleted>(node)) return true;
            if (!em.HasComponent<NetNode>(node)) return false;

            NetNode netNode = em.GetComponentData<NetNode>(node);
            NetLinkStatus status = NetConnectivityPolicy.EvaluateEndpoint(
                recorded.x, recorded.z, netNode.m_Position.x, netNode.m_Position.z,
                recordedLinks, ReadLinkCount(em, node), NetConnectivityPolicy.EndpointTolerance);
            return status == NetLinkStatus.Reconnected || status == NetLinkStatus.ReconnectedReversed;
        }

        private static bool OwnerMatches(EntityManager em, Entity entity, OwnerDefinition definition)
        {
            if (!em.Exists(entity) || em.HasComponent<Deleted>(entity) || em.HasComponent<Temp>(entity) ||
                !em.HasComponent<PrefabRef>(entity) || !em.HasComponent<Game.Objects.Transform>(entity) ||
                em.GetComponentData<PrefabRef>(entity).m_Prefab != definition.m_Prefab) return false;
            var transform = em.GetComponentData<Game.Objects.Transform>(entity);
            return math.distancesq(transform.m_Position, definition.m_Position) <= OwnerMatchPolicy.PositionTolerance * OwnerMatchPolicy.PositionTolerance &&
                math.abs(math.dot(transform.m_Rotation.value, definition.m_Rotation.value)) >= OwnerMatchPolicy.RotationTolerance;
        }

        private static int ReadSeed(EntityManager em, Entity entity, Entity prefab)
        {
            if (em.HasComponent<PseudoRandomSeed>(entity))
            {
                return em.GetComponentData<PseudoRandomSeed>(entity).m_Seed;
            }
            unchecked
            {
                int h = (entity.Index * 73856093) ^ (prefab.Index * 19349663) ^ 0x51F3A1;
                return h == 0 ? 1 : h;
            }
        }

        private static bool IsFinite(float3 v) => math.all(math.isfinite(v));
        private static bool IsFinite(float2 v) => math.all(math.isfinite(v));
        private static bool IsFinite(quaternion q) => math.all(math.isfinite(q.value));

        private static bool IsBezierFinite(Bezier4x3 b) =>
            IsFinite(b.a) && IsFinite(b.b) && IsFinite(b.c) && IsFinite(b.d);

        private static bool TryCaptureNetRecord(EntityManager em, Entity entity, out UndoNetRecord rec)
        {
            rec = default;
            if (!em.HasComponent<PrefabRef>(entity) || !em.HasComponent<Edge>(entity) || !em.HasComponent<Curve>(entity))
            {
                return false;
            }

            Entity prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null || !em.Exists(prefab))
            {
                return false;
            }

            Edge edge = em.GetComponentData<Edge>(entity);
            Curve curve = em.GetComponentData<Curve>(entity);
            if (edge.m_Start == Entity.Null || edge.m_End == Entity.Null ||
                !em.Exists(edge.m_Start) || !em.Exists(edge.m_End) ||
                !em.HasComponent<NetNode>(edge.m_Start) || !em.HasComponent<NetNode>(edge.m_End) ||
                !IsBezierFinite(curve.m_Bezier) || !math.isfinite(curve.m_Length) || curve.m_Length <= 0.01f)
            {
                return false;
            }

            NetNode startNode = em.GetComponentData<NetNode>(edge.m_Start);
            NetNode endNode = em.GetComponentData<NetNode>(edge.m_End);
            if (!IsFinite(startNode.m_Position) || !IsFinite(startNode.m_Rotation) ||
                !IsFinite(endNode.m_Position) || !IsFinite(endNode.m_Rotation))
            {
                return false;
            }

            float2 edgeElev = em.HasComponent<NetElevation>(entity)
                ? em.GetComponentData<NetElevation>(entity).m_Elevation
                : float2.zero;
            float2 startElev = em.HasComponent<NetElevation>(edge.m_Start)
                ? em.GetComponentData<NetElevation>(edge.m_Start).m_Elevation
                : float2.zero;
            float2 endElev = em.HasComponent<NetElevation>(edge.m_End)
                ? em.GetComponentData<NetElevation>(edge.m_End).m_Elevation
                : float2.zero;

            if (!IsFinite(edgeElev)) edgeElev = float2.zero;
            if (!IsFinite(startElev)) startElev = float2.zero;
            if (!IsFinite(endElev)) endElev = float2.zero;

            bool hasUpgraded = em.HasComponent<Upgraded>(entity);
            CompositionFlags upgFlags = hasUpgraded ? em.GetComponentData<Upgraded>(entity).m_Flags : default;

            rec = new UndoNetRecord
            {
                Prefab = prefab,
                StartNode = edge.m_Start,
                EndNode = edge.m_End,
                StartPos = startNode.m_Position,
                StartRot = startNode.m_Rotation,
                StartElevation = startElev,
                EndPos = endNode.m_Position,
                EndRot = endNode.m_Rotation,
                EndElevation = endElev,
                Curve = curve.m_Bezier,
                Length = curve.m_Length,
                EdgeElevation = edgeElev,
                HasUpgraded = hasUpgraded,
                UpgradedFlags = upgFlags,
                Seed = ReadSeed(em, entity, prefab),
                StartLinks = ReadLinkCount(em, edge.m_Start),
                EndLinks = ReadLinkCount(em, edge.m_End),
            };
            ReadLaneEvidence(em, entity, out rec.LaneCount, out rec.LaneOwned, out rec.LaneForeign);
            return true;
        }

        /// <summary>
        /// Counts the lanes the segment carries and how many of them name an owner. The lane entities
        /// themselves are not recorded: they die with the road and the game creates new ones when it
        /// comes back, so a reference would only be a number that can never be matched again.
        /// A segment with no lane buffer is recorded as zero lanes, which is an answer, not a failure.
        /// </summary>
        private static void ReadLaneEvidence(EntityManager em, Entity edge, out int laneCount, out int laneOwned, out int laneForeign)
        {
            // "Could not read" and "genuinely had no lanes" are different answers: the first must
            // refuse the rebuild, the second is a complete observation. Starting from the unknown
            // sentinel keeps that distinction; starting from zero would silently turn every read
            // failure into "this road carried no lanes".
            laneCount = LaneOwnershipPolicy.UnknownLaneCount;
            laneOwned = LaneOwnershipPolicy.UnknownLaneCount;
            laneForeign = LaneOwnershipPolicy.UnknownLaneCount;
            if (edge == Entity.Null || !em.Exists(edge) || !em.HasBuffer<NetSubLane>(edge))
            {
                return;
            }

            DynamicBuffer<NetSubLane> lanes = em.GetBuffer<NetSubLane>(edge, true);
            laneCount = lanes.Length;
            int owned = 0;
            int foreign = 0;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (lane == Entity.Null || !em.Exists(lane) || !em.HasComponent<Owner>(lane))
                {
                    continue;
                }

                Entity owner = em.GetComponentData<Owner>(lane).m_Owner;
                if (owner == edge)
                {
                    owned++;
                }
                else if (owner != Entity.Null && em.Exists(owner) && em.HasComponent<Edge>(owner))
                {
                    // Only another road counts as a competing claim; an owner of any other kind says
                    // nothing about which segment these lanes belong to.
                    foreign++;
                }
            }

            laneOwned = owned;
            laneForeign = foreign;
        }

        /// <summary>Number of edges meeting a junction, or -1 when the buffer is not readable.</summary>
        private static int ReadLinkCount(EntityManager em, Entity node)
        {
            if (node == Entity.Null || !em.Exists(node) || !em.HasBuffer<ConnectedEdge>(node))
            {
                return -1;
            }

            return em.GetBuffer<ConnectedEdge>(node, true).Length;
        }

        /// <summary>
        /// Writes what the restore actually managed and what it refused, so the save/reload check has
        /// the same numbers the player was shown.
        /// </summary>
        private void LogRestoreAudit(UndoSnapshot snap)
        {
            if (snap == null) return;
            MarqueeDeleteToolSystem.AppendStatusLine(
                $"[State: undo-restore, Restored: {snap.RestoredCount}, Skipped: {snap.SkippedCount}, Total: {snap.TotalCount}, " +
                $"AmbiguousOwners: {m_AmbiguousOwners}, ConnectivitySkips: {m_ConnectivitySkips}, OccupiedLots: {m_OccupiedRestores}, " +
                $"LaneUnconfirmed: {m_LaneUnconfirmed}]");
        }

        private static bool TryCaptureAreaRecord(EntityManager em, Entity entity, out UndoAreaRecord rec)
        {
            rec = default;
            if (!em.HasComponent<PrefabRef>(entity) || !em.HasBuffer<Game.Areas.Node>(entity))
            {
                return false;
            }

            Entity prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null || !em.Exists(prefab))
            {
                return false;
            }

            DynamicBuffer<Game.Areas.Node> buf = em.GetBuffer<Game.Areas.Node>(entity, true);
            if (buf.Length < 3)
            {
                return false;
            }

            var nodes = new Game.Areas.Node[buf.Length];
            for (int i = 0; i < buf.Length; i++)
            {
                Game.Areas.Node n = buf[i];
                if (!IsFinite(n.m_Position) || !math.isfinite(n.m_Elevation))
                {
                    return false;
                }
                nodes[i] = n;
            }

            rec = new UndoAreaRecord
            {
                Prefab = prefab,
                Nodes = nodes,
                Seed = ReadSeed(em, entity, prefab),
            };
            return true;
        }

        private static bool TryCaptureBuildingRecord(
            EntityManager em,
            Entity entity,
            ref int recordedWeight,
            ref bool truncated,
            ref int notCaptured,
            out UndoBuildingRecord rec)
        {
            rec = default;
            if (!em.HasComponent<PrefabRef>(entity) || !em.HasComponent<Game.Objects.Transform>(entity))
            {
                return false;
            }

            Entity prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null || !em.Exists(prefab))
            {
                return false;
            }

            var tr = em.GetComponentData<Game.Objects.Transform>(entity);
            if (!IsFinite(tr.m_Position) || !IsFinite(tr.m_Rotation))
            {
                return false;
            }

            bool hasElev = em.HasComponent<ObjElevation>(entity);
            ObjElevation elev = hasElev ? em.GetComponentData<ObjElevation>(entity) : default;
            if (hasElev && !math.isfinite(elev.m_Elevation))
            {
                elev.m_Elevation = 0f;
            }

            Entity attachedParent = em.HasComponent<Attached>(entity)
                ? em.GetComponentData<Attached>(entity).m_Parent
                : Entity.Null;

            recordedWeight++;
            List<UndoAreaRecord> subAreas = CaptureSubAreas(em, entity, ref recordedWeight, ref truncated, ref notCaptured);
            List<UndoNetRecord> subNets = CaptureSubNets(em, entity, ref recordedWeight, ref truncated, ref notCaptured);
            List<UndoUpgradeRecord> upgrades = CaptureInstalledUpgrades(em, entity, ref recordedWeight, ref truncated, ref notCaptured);

            rec = new UndoBuildingRecord
            {
                Prefab = prefab,
                Position = tr.m_Position,
                Rotation = tr.m_Rotation,
                HasElevation = hasElev,
                Elevation = hasElev ? elev.m_Elevation : 0f,
                ElevationFlags = hasElev ? elev.m_Flags : 0,
                AttachedParent = attachedParent,
                Seed = ReadSeed(em, entity, prefab),
                Upgrades = upgrades,
                SubAreas = subAreas,
                SubNets = subNets,
            };
            return true;
        }

        private static List<UndoUpgradeRecord> CaptureInstalledUpgrades(
            EntityManager em,
            Entity owner,
            ref int recordedWeight, ref bool truncated, ref int notCaptured)
        {
            if (!em.HasBuffer<InstalledUpgrade>(owner))
            {
                return null;
            }

            DynamicBuffer<InstalledUpgrade> buf = em.GetBuffer<InstalledUpgrade>(owner, true);
            if (buf.Length == 0)
            {
                return null;
            }

            List<UndoUpgradeRecord> list = null;
            for (int i = 0; i < buf.Length; i++)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot) { truncated = true; break; }

                Entity upg = buf[i].m_Upgrade;
                if (upg == Entity.Null || !em.Exists(upg) || em.HasComponent<Deleted>(upg))
                {
                    continue;
                }

                if (!em.HasComponent<PrefabRef>(upg) || !em.HasComponent<Game.Objects.Transform>(upg))
                {
                    notCaptured++;
                    continue;
                }

                Entity prefab = em.GetComponentData<PrefabRef>(upg).m_Prefab;
                if (prefab == Entity.Null || !em.Exists(prefab))
                {
                    notCaptured++;
                    continue;
                }

                var tr = em.GetComponentData<Game.Objects.Transform>(upg);
                if (!IsFinite(tr.m_Position) || !IsFinite(tr.m_Rotation))
                {
                    notCaptured++;
                    continue;
                }

                bool hasElev = em.HasComponent<ObjElevation>(upg);
                ObjElevation elev = hasElev ? em.GetComponentData<ObjElevation>(upg) : default;
                if (hasElev && !math.isfinite(elev.m_Elevation))
                {
                    elev.m_Elevation = 0f;
                }

                recordedWeight++;
                list ??= new List<UndoUpgradeRecord>(buf.Length);
                list.Add(new UndoUpgradeRecord
                {
                    Prefab = prefab,
                    Position = tr.m_Position,
                    Rotation = tr.m_Rotation,
                    HasElevation = hasElev,
                    Elevation = hasElev ? elev.m_Elevation : 0f,
                    ElevationFlags = hasElev ? elev.m_Flags : 0,
                    Seed = ReadSeed(em, upg, prefab),
                    SubAreas = CaptureSubAreas(em, upg, ref recordedWeight, ref truncated, ref notCaptured),
                    SubNets = CaptureSubNets(em, upg, ref recordedWeight, ref truncated, ref notCaptured),
                });
            }

            return list;
        }

        private static List<UndoAreaRecord> CaptureSubAreas(EntityManager em, Entity owner, ref int recordedWeight, ref bool truncated, ref int notCaptured)
        {
            if (!em.HasBuffer<Game.Areas.SubArea>(owner))
            {
                return null;
            }

            DynamicBuffer<Game.Areas.SubArea> buf = em.GetBuffer<Game.Areas.SubArea>(owner, true);
            if (buf.Length == 0)
            {
                return null;
            }

            List<UndoAreaRecord> list = null;
            for (int i = 0; i < buf.Length; i++)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot) { truncated = true; break; }

                Entity area = buf[i].m_Area;
                if (area == Entity.Null || !em.Exists(area) || em.HasComponent<Deleted>(area) ||
                    em.HasComponent<MapTile>(area) || em.HasComponent<District>(area))
                {
                    continue;
                }

                if (TryCaptureAreaRecord(em, area, out UndoAreaRecord areaRec))
                {
                    recordedWeight++;
                    list ??= new List<UndoAreaRecord>(buf.Length);
                    list.Add(areaRec);
                }
                else
                {
                    notCaptured++;
                }
            }

            return list;
        }

        private static List<UndoNetRecord> CaptureSubNets(EntityManager em, Entity owner, ref int recordedWeight, ref bool truncated, ref int notCaptured)
        {
            if (!em.HasBuffer<Game.Net.SubNet>(owner))
            {
                return null;
            }

            DynamicBuffer<Game.Net.SubNet> buf = em.GetBuffer<Game.Net.SubNet>(owner, true);
            if (buf.Length == 0)
            {
                return null;
            }

            List<UndoNetRecord> list = null;
            for (int i = 0; i < buf.Length; i++)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot) { truncated = true; break; }

                Entity subNet = buf[i].m_SubNet;
                if (subNet == Entity.Null || !em.Exists(subNet) || em.HasComponent<Deleted>(subNet) ||
                    !em.HasComponent<Edge>(subNet))
                {
                    continue;
                }

                if (TryCaptureNetRecord(em, subNet, out UndoNetRecord netRec))
                {
                    recordedWeight++;
                    list ??= new List<UndoNetRecord>(buf.Length);
                    list.Add(netRec);
                }
                else
                {
                    notCaptured++;
                }
            }

            return list;
        }

        private static SelectionCategory ClassifyObjectCategory(EntityManager em, Entity entity)
        {
            bool isPlant = em.HasComponent<Game.Objects.Plant>(entity) || em.HasComponent<Tree>(entity);
            bool isPillar = em.HasComponent<Game.Objects.Pillar>(entity);
            bool hasSurface = em.HasComponent<Game.Objects.Surface>(entity);
            bool hasNetObj = em.HasComponent<Game.Objects.NetObject>(entity) ||
                             em.HasComponent<Game.Objects.UtilityObject>(entity) ||
                             em.HasComponent<Game.Objects.AssetStamp>(entity);
            return SelectionClassifier.ClassifyNonBuildingObject(isPlant, isPillar, hasSurface, hasNetObj);
        }

        private static bool TryCaptureObjectRecord(EntityManager em, Entity entity, SelectionCategory category, out UndoObjectRecord rec)
        {
            rec = default;
            if (!em.HasComponent<PrefabRef>(entity) || !em.HasComponent<Game.Objects.Transform>(entity))
            {
                return false;
            }

            Entity prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null || !em.Exists(prefab))
            {
                return false;
            }

            var tr = em.GetComponentData<Game.Objects.Transform>(entity);
            if (!IsFinite(tr.m_Position) || !IsFinite(tr.m_Rotation))
            {
                return false;
            }

            bool hasElev = em.HasComponent<ObjElevation>(entity);
            ObjElevation elev = hasElev ? em.GetComponentData<ObjElevation>(entity) : default;
            if (hasElev && !math.isfinite(elev.m_Elevation))
            {
                elev.m_Elevation = 0f;
            }

            bool isTree = em.HasComponent<Tree>(entity);
            Tree tree = isTree ? em.GetComponentData<Tree>(entity) : default;
            Entity attachedParent = em.HasComponent<Attached>(entity)
                ? em.GetComponentData<Attached>(entity).m_Parent
                : Entity.Null;

            rec = new UndoObjectRecord
            {
                Prefab = prefab,
                Category = category,
                Position = tr.m_Position,
                Rotation = tr.m_Rotation,
                HasElevation = hasElev,
                Elevation = hasElev ? elev.m_Elevation : 0f,
                ElevationFlags = hasElev ? elev.m_Flags : 0,
                IsDecoration = em.HasComponent<Decoration>(entity),
                IsTree = isTree,
                TreeState = isTree ? tree.m_State : 0,
                TreeGrowth = isTree ? tree.m_Growth : (byte)0,
                AttachedParent = attachedParent,
                Seed = ReadSeed(em, entity, prefab),
            };
            return true;
        }

        private static void EmitNetRecord(
            EntityManager em,
            EntityCommandBuffer ecb,
            in UndoNetRecord rec,
            bool hasOwner,
            Entity ownerEntity)
        {
            if (rec.Prefab == Entity.Null || !em.Exists(rec.Prefab))
            {
                return;
            }

            Entity startEntity = (!hasOwner && rec.StartNode != Entity.Null && em.Exists(rec.StartNode) && !em.HasComponent<Deleted>(rec.StartNode))
                ? rec.StartNode
                : Entity.Null;
            Entity endEntity = (!hasOwner && rec.EndNode != Entity.Null && em.Exists(rec.EndNode) && !em.HasComponent<Deleted>(rec.EndNode))
                ? rec.EndNode
                : Entity.Null;

            CoursePosFlags startFlags = CoursePosFlags.IsFirst;
            CoursePosFlags endFlags = CoursePosFlags.IsLast;
            if (hasOwner)
            {
                startFlags |= CoursePosFlags.DisableMerge;
                endFlags |= CoursePosFlags.DisableMerge;
            }
            if (math.any(rec.StartElevation != float2.zero) || math.any(rec.EdgeElevation != float2.zero))
            {
                startFlags |= CoursePosFlags.FreeHeight;
            }
            if (math.any(rec.EndElevation != float2.zero) || math.any(rec.EdgeElevation != float2.zero))
            {
                endFlags |= CoursePosFlags.FreeHeight;
            }

            var course = new NetCourse
            {
                m_StartPosition = new CoursePos
                {
                    m_Entity = startEntity,
                    m_Position = rec.StartPos,
                    m_Rotation = rec.StartRot,
                    m_Elevation = rec.StartElevation,
                    m_CourseDelta = 0f,
                    m_SplitPosition = 0f,
                    m_Flags = startFlags,
                    m_ParentMesh = -1,
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = endEntity,
                    m_Position = rec.EndPos,
                    m_Rotation = rec.EndRot,
                    m_Elevation = rec.EndElevation,
                    m_CourseDelta = 1f,
                    m_SplitPosition = 0f,
                    m_Flags = endFlags,
                    m_ParentMesh = -1,
                },
                m_Curve = rec.Curve,
                m_Elevation = rec.EdgeElevation,
                m_Length = rec.Length,
                m_FixedIndex = -1,
            };

            Entity carrier = ecb.CreateEntity();
            ecb.AddComponent(carrier, new CreationDefinition
            {
                m_Prefab = rec.Prefab,
                m_Owner = hasOwner ? ownerEntity : Entity.Null,
                m_Flags = CreationFlags.Permanent,
                m_RandomSeed = rec.Seed,
            });
            ecb.AddComponent(carrier, course);
            if (rec.HasUpgraded)
            {
                ecb.AddComponent(carrier, new Upgraded { m_Flags = rec.UpgradedFlags });
            }
            if (hasOwner)
            {
                
            }
            ecb.AddComponent<Updated>(carrier);
            ecb.AddComponent<Deleted>(carrier);
        }

        private static void EmitAreaRecord(
            EntityManager em,
            EntityCommandBuffer ecb,
            in UndoAreaRecord rec,
            bool hasOwner,
            Entity ownerEntity)
        {
            if (rec.Prefab == Entity.Null || !em.Exists(rec.Prefab) || rec.Nodes == null || rec.Nodes.Length < 3)
            {
                return;
            }

            Entity carrier = ecb.CreateEntity();
            ecb.AddComponent(carrier, new CreationDefinition
            {
                m_Prefab = rec.Prefab,
                m_Owner = hasOwner ? ownerEntity : Entity.Null,
                m_Flags = CreationFlags.Permanent,
                m_RandomSeed = rec.Seed,
            });
            if (hasOwner)
            {
                
            }
            DynamicBuffer<Game.Areas.Node> nodes = ecb.AddBuffer<Game.Areas.Node>(carrier);
            for (int i = 0; i < rec.Nodes.Length; i++)
            {
                nodes.Add(rec.Nodes[i]);
            }
            ecb.AddComponent<Updated>(carrier);
            ecb.AddComponent<Deleted>(carrier);
        }

        private static void EmitBuildingRoot(EntityManager em, EntityCommandBuffer ecb, in UndoBuildingRecord rec)
        {
            if (rec.Prefab == Entity.Null || !em.Exists(rec.Prefab))
            {
                return;
            }

            CreationFlags flags = CreationFlags.Permanent;
            if (rec.HasElevation && (rec.ElevationFlags & ElevationFlags.Lowered) != 0)
            {
                flags |= CreationFlags.Lowered;
            }

            Entity attached = Entity.Null;
            if (rec.AttachedParent != Entity.Null && em.Exists(rec.AttachedParent) && !em.HasComponent<Deleted>(rec.AttachedParent))
            {
                attached = rec.AttachedParent;
                flags |= CreationFlags.Attach;
            }

            Entity carrier = ecb.CreateEntity();
            ecb.AddComponent(carrier, new CreationDefinition
            {
                m_Prefab = rec.Prefab,
                m_Attached = attached,
                m_Flags = flags,
                m_RandomSeed = rec.Seed,
            });
            ecb.AddComponent(carrier, new ObjectDefinition
            {
                m_Position = rec.Position,
                m_Rotation = rec.Rotation,
                m_LocalPosition = rec.Position,
                m_LocalRotation = rec.Rotation,
                m_Scale = new float3(1f, 1f, 1f),
                m_Elevation = rec.HasElevation ? rec.Elevation : 0f,
                m_Intensity = 1f,
                m_ParentMesh = -1,
                m_Probability = 100,
                m_PrefabSubIndex = -1,
            });
            ecb.AddComponent<Updated>(carrier);
            ecb.AddComponent<Deleted>(carrier);

        }

        private static void EmitUpgrade(EntityManager em, EntityCommandBuffer ecb, in UndoUpgradeRecord upg, Entity owner)
        {
            CreationFlags upgFlags = CreationFlags.Permanent | CreationFlags.Upgrade;
            if (upg.HasElevation && (upg.ElevationFlags & ElevationFlags.Lowered) != 0)
            {
                upgFlags |= CreationFlags.Lowered;
            }

            var parent = em.GetComponentData<Game.Objects.Transform>(owner);
            quaternion inverse = math.inverse(parent.m_Rotation);
            Entity upgCarrier = ecb.CreateEntity();
            ecb.AddComponent(upgCarrier, new CreationDefinition
            {
                m_Prefab = upg.Prefab,
                m_Owner = owner,
                m_Flags = upgFlags,
                m_RandomSeed = upg.Seed,
            });
            ecb.AddComponent(upgCarrier, new ObjectDefinition
            {
                m_Position = upg.Position,
                m_Rotation = upg.Rotation,
                m_LocalPosition = math.rotate(inverse, upg.Position - parent.m_Position),
                m_LocalRotation = math.mul(inverse, upg.Rotation),
                m_Scale = new float3(1f, 1f, 1f),
                m_Elevation = upg.HasElevation ? upg.Elevation : 0f,
                m_Intensity = 1f,
                m_ParentMesh = -1,
                m_Probability = 100,
                m_PrefabSubIndex = -1,
            });
            ecb.AddComponent<Updated>(upgCarrier);
            ecb.AddComponent<Deleted>(upgCarrier);

        }

        private static void EmitObjectRecord(EntityManager em, EntityCommandBuffer ecb, in UndoObjectRecord rec)
        {
            if (rec.Prefab == Entity.Null || !em.Exists(rec.Prefab))
            {
                return;
            }

            CreationFlags flags = CreationFlags.Permanent;
            if (rec.HasElevation && (rec.ElevationFlags & ElevationFlags.Lowered) != 0)
            {
                flags |= CreationFlags.Lowered;
            }

            Entity attached = Entity.Null;
            if (rec.AttachedParent != Entity.Null && em.Exists(rec.AttachedParent) && !em.HasComponent<Deleted>(rec.AttachedParent))
            {
                attached = rec.AttachedParent;
                flags |= CreationFlags.Attach;
            }

            float age = rec.IsTree
                ? UndoMath.TreeToDefinitionAge((byte)rec.TreeState, rec.TreeGrowth)
                : 0f;

            Entity carrier = ecb.CreateEntity();
            ecb.AddComponent(carrier, new CreationDefinition
            {
                m_Prefab = rec.Prefab,
                m_Attached = attached,
                m_Flags = flags,
                m_RandomSeed = rec.Seed,
            });
            ecb.AddComponent(carrier, new ObjectDefinition
            {
                m_Position = rec.Position,
                m_Rotation = rec.Rotation,
                m_LocalPosition = rec.Position,
                m_LocalRotation = rec.Rotation,
                m_Scale = new float3(1f, 1f, 1f),
                m_Elevation = rec.HasElevation ? rec.Elevation : 0f,
                m_Intensity = 1f,
                m_Age = age,
                m_IsDecoration = rec.IsDecoration,
                m_ParentMesh = -1,
                m_Probability = 100,
                m_PrefabSubIndex = -1,
            });
            ecb.AddComponent<Updated>(carrier);
            ecb.AddComponent<Deleted>(carrier);
        }
    }
}
