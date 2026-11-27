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
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Unity.Entities;
using Unity.Jobs;
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
        private EntityQuery m_OrphanedCarrierQuery;
        private int m_PendingCarrierCleanupFrames;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<MarqueeDeleteToolSystem>();
            m_OutputBarrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_OrphanedCarrierQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CreationDefinition>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Updated>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        protected override void OnUpdate()
        {
            if (m_ToolSystem == null || m_OutputBarrier == null)
            {
                return;
            }

            Dependency.Complete();
            bool hadActive = m_ToolSystem.HasActiveUndo;
            m_ToolSystem.TickUndo(m_OutputBarrier);
            bool hasActive = m_ToolSystem.HasActiveUndo;

            if (hadActive || hasActive)
            {
                m_PendingCarrierCleanupFrames = 4;
            }

            if (m_PendingCarrierCleanupFrames > 0)
            {
                if (!hasActive)
                {
                    m_PendingCarrierCleanupFrames--;
                }

                if (!m_OrphanedCarrierQuery.IsEmptyIgnoreFilter)
                {
                    using (var entities = m_OrphanedCarrierQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
                    {
                        EntityCommandBuffer ecb = default;
                        bool hasEcb = false;
                        for (int i = 0; i < entities.Length; i++)
                        {
                            Entity e = entities[i];
                            if (e == Entity.Null || !EntityManager.Exists(e))
                            {
                                continue;
                            }
                            if ((EntityManager.GetComponentData<CreationDefinition>(e).m_Flags & CreationFlags.Permanent) != 0)
                            {
                                if (!hasEcb)
                                {
                                    ecb = m_OutputBarrier.CreateCommandBuffer();
                                    hasEcb = true;
                                }
                                ecb.AddComponent<Deleted>(e);
                            }
                        }
                    }
                }
            }
        }
    }

    internal sealed class DeletionUndoManager
    {
        public const int MaxUndoSnapshots = 10;
        public const int MaxEntitiesPerSnapshot = 8000;
        private const int MaxRequestsPerStep = 16;
        private const double StepTimeBudgetMs = 3.0;

        /// <summary>
        /// How far a restored segment's length may drift from the recorded one and still be recognised
        /// as this undo's own work. The game rebuilds the same course, so the drift is rounding; a player's
        /// road between the same junctions with the same prefab is not going to land inside this by chance.
        /// </summary>
        private const float OwnEdgeLengthTolerance = 0.05f;
        private const float UninterruptedNodeDriftTolerance = 4.0f;

        private enum UndoOperationKind { Boundary, Net, Area, Building, Upgrade, Object, BuildingUnit, SubObjectUnit }
        private struct UndoOperation
        {
            public UndoOperationKind Kind;
            public UndoNetRecord Net;
            public UndoAreaRecord Area;
            public UndoBuildingRecord Building;
            public UndoUpgradeRecord Upgrade;
            public UndoObjectRecord Object;
            public UndoSubObjectSubs SubObjectSubs;

            /// <summary>Position of this Net operation among all Net operations, for the verdict cache.</summary>
            public int NetIndex;
            public bool HasOwner;
            public Entity AncestorPrefab;
            public OwnerDefinition Ancestor;
            public OwnerDefinition Owner;
        }

        private struct UndoAttachedRef
        {
            public Entity OriginalEntity;
            public Entity Prefab;
            public float3 Position;
            public bool IsNetEdge;
            public bool IsNetNode;
        }

        private struct UndoSubObjectSubs
        {
            public OwnerDefinition OwnerDef;
            public int Seed;
            public int PrefabSubNetCount;
            public List<UndoAreaRecord> SubAreas;
            public List<UndoNetRecord> SubNets;
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
            /// <summary>
            /// Identities (index+version) of the edges meeting the start node at capture, the segment's
            /// own edge excluded. Null means the junction could not be read, which is not the same as an
            /// empty set: null refuses the rebuild, an empty set says the junction really had nothing.
            /// </summary>
            public Entity[] StartLinkIds;
            /// <summary>Identities of the edges meeting the end node at capture. Null means unreadable.</summary>
            public Entity[] EndLinkIds;
            /// <summary>Lanes the segment carried at capture (-1 when unreadable). See <see cref="LaneOwnershipPolicy"/>.</summary>
            public int LaneCount;
            /// <summary>Lanes of those that named this segment as their owner at capture.</summary>
            public int LaneOwned;
            /// <summary>Lanes of those that named another road as their owner at capture.</summary>
            public int LaneForeign;
            public bool IsNodeOnly;
            public int StartParentMesh;
            public int EndParentMesh;
            public bool StartNodeOwnedByParent;
            public bool EndNodeOwnedByParent;
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
            public bool IsPhysical;
            public bool HasOwnerSubBuffer;
            public int ParentMesh;
            public int PrefabSubNetCount;
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
            public UndoAttachedRef AttachedInfo;
            public bool HasOwnerSubBuffer;
            public int Seed;
            public int PrefabSubNetCount;
            public List<UndoUpgradeRecord> Upgrades;
            public List<UndoAreaRecord> SubAreas;
            public List<UndoNetRecord> SubNets;
            public List<UndoSubObjectSubs> SubObjectSubs;
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
            public UndoAttachedRef AttachedInfo;
            public int Seed;
        }

        /// <summary>
        /// What a segment record actually submitted, which is not the same as the junctions it was
        /// recorded against: when the old junction is already gone, <see cref="EmitNetRecord"/> submits
        /// <c>Entity.Null</c> and the game builds a fresh junction there. Recognition of one's own
        /// restored edge has to be made on the submitted endpoints for that reason.
        /// </summary>
        private struct EmittedNet
        {
            public Entity Prefab;
            public float Length;
            public Entity SubmittedStart;
            public Entity SubmittedEnd;

            /// <summary>Where the segment's start sat when it was captured. Used only when both submitted endpoints are unknown, so a same-prefab, same-length road far away cannot be mistaken for this one.</summary>
            public float3 RecordedStartPos;

            /// <summary>Where the segment's end sat when it was captured; see <see cref="RecordedStartPos"/>.</summary>
            public float3 RecordedEndPos;
        }

        internal sealed class DetachedSnapshot
        {
            internal readonly IUndoSnapshotMeta Meta;
            internal readonly object RawSnapshot;

            internal DetachedSnapshot(IUndoSnapshotMeta meta, object rawSnapshot)
            {
                Meta = meta;
                RawSnapshot = rawSnapshot;
            }

            public UndoCategoryCounts Counts => Meta != null ? Meta.Counts : default;
            public UndoCategoryCounts NotCaptured => Meta != null ? Meta.NotCaptured : default;
            public int TotalCount => Meta != null ? Meta.TotalCount : 0;
            public bool Truncated => Meta != null && Meta.Truncated;
            public bool HasOwnerTimeout => Meta != null && Meta.HasOwnerTimeout;
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

            /// <summary>
            /// Junction verdicts decided before a resumed snapshot emits anything. Null on every batch
            /// that is not a resume: there the restore checks junction positions only and never reads
            /// edge identities, so no cache exists to go stale. A resume sets
            /// <see cref="IdentityGate"/> and the next Step decides again against the city as it is
            /// then, tolerating only the segments this snapshot emitted itself.
            /// </summary>
            public NetVerdictCache Verdicts;

            /// <summary>
            /// True when this snapshot is being resumed after a pause. Identity comparison of junction
            /// edges happens on that path and on no other: the restore is supposed to trade a batch of
            /// accuracy for stability, and the acceptance is that a road the player splices into a
            /// paused undo is caught, while one spliced during uninterrupted batches is not.
            /// </summary>
            public bool IdentityGate;

            /// <summary>The mask this undo is running with: the verdict pass must walk the same operations.</summary>
            public UndoAssetMask ActiveMask;

            /// <summary>
            /// One segment this snapshot actually emitted. A pause lets the city change, so a resumed
            /// snapshot predicts its junction verdicts again; these records are how the undo tells its
            /// own restored edges apart from roads the player added meanwhile.
            /// </summary>
            public readonly List<EmittedNet> EmittedNets = new List<EmittedNet>();

            /// <summary>
            /// The <c>NetIndex</c> of every segment this snapshot emitted. The verdict pass skips them:
            /// they were already accepted, and re-judging a segment against a junction that now holds
            /// its own rebuilt edge would only ask the recognition heuristic again.
            /// </summary>
            public readonly HashSet<int> EmittedNetIndices = new HashSet<int>();

            /// <summary>
            /// The edge entity each submitted record holds, indexed like <see cref="EmittedNets"/>, or
            /// <c>Entity.Null</c> while the record has excused nothing. It lives on the snapshot rather
            /// than in the verdict pass so that pausing and resuming again cannot hand the record back
            /// its allowance: a splice refused before a pause stays refused after it. A fresh start
            /// clears the list because the history slot is reused and those claims belong to the run
            /// that made them.
            /// </summary>
            public Entity[] EmittedNetClaims;
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
        private FrozenBatchCapture m_FrozenBatchCapture;
        private readonly SelectiveUndoTracker<UndoSnapshot> m_Tracker = new SelectiveUndoTracker<UndoSnapshot>(MaxUndoSnapshots);

        private sealed class FrozenRootRecord
        {
            public UndoOperationKind Kind;
            public SelectionCategory ObjCategory;
            public UndoNetRecord Net;
            public UndoAreaRecord Area;
            public UndoBuildingRecord Building;
            public UndoObjectRecord Object;
            public int Weight;
            public bool Failed;
            public int BuildingChildNotCaptured;
            public bool Truncated;
        }

        private sealed class FrozenBatchCapture
        {
            public readonly Dictionary<Entity, FrozenRootRecord> Roots = new Dictionary<Entity, FrozenRootRecord>();
            public readonly HashSet<Entity> RecordedRoots = new HashSet<Entity>();
            public readonly HashSet<Entity> SkippedByBudget = new HashSet<Entity>();
            public readonly List<FrozenRootRecord> SubmittedNets = new List<FrozenRootRecord>();
            public readonly List<FrozenRootRecord> SubmittedBuildings = new List<FrozenRootRecord>();
            public readonly List<FrozenRootRecord> SubmittedAreas = new List<FrozenRootRecord>();
            public readonly List<FrozenRootRecord> SubmittedObjects = new List<FrozenRootRecord>();
            public int NetWeight;
            public int BuildingWeight;
            public int AreaWeight;
            public int ObjectWeight;
            public int FrozenTotalWeight;
            public bool FrozenTruncated;
            public bool AnySubmittedSkippedByBudget;

            public int SubmittedWeightUpToTier(int tier) => tier switch
            {
                0 => NetWeight,
                1 => NetWeight + BuildingWeight,
                2 => NetWeight + BuildingWeight + AreaWeight,
                _ => NetWeight + BuildingWeight + AreaWeight + ObjectWeight,
            };

            public void AddSubmitted(FrozenRootRecord rec)
            {
                if (rec == null) return;
                switch (rec.Kind)
                {
                    case UndoOperationKind.Net:
                        SubmittedNets.Add(rec);
                        NetWeight += rec.Weight;
                        break;
                    case UndoOperationKind.Building:
                        SubmittedBuildings.Add(rec);
                        BuildingWeight += rec.Weight;
                        break;
                    case UndoOperationKind.Area:
                        SubmittedAreas.Add(rec);
                        AreaWeight += rec.Weight;
                        break;
                    case UndoOperationKind.Object:
                        SubmittedObjects.Add(rec);
                        ObjectWeight += rec.Weight;
                        break;
                }
            }
        }

        internal int FrozenCaptureRootsForTest => m_FrozenBatchCapture?.Roots.Count ?? 0;
        internal int FrozenCaptureWeightForTest => m_FrozenBatchCapture?.FrozenTotalWeight ?? 0;
        private int m_NextAllowedFrame;
        private int m_LastStepFrame = -1;
        private int m_LastObservedFrame = -1;
        private readonly List<float3> m_PendingRefreshBuildingPositions = new List<float3>();
        private readonly List<float3> m_PendingRefreshNetPositions = new List<float3>();

        private UndoOperation? m_WaitingOperation;
        private int m_OwnerWaitStarted = -1;
        private readonly HashSet<OwnerDefinition> m_FailedOwners = new HashSet<OwnerDefinition>();
        private readonly Dictionary<OwnerDefinition, Entity> m_ResolvedOwners = new Dictionary<OwnerDefinition, Entity>();
        private readonly Dictionary<Entity, List<Entity>> m_OwnerIndex = new Dictionary<Entity, List<Entity>>();
        private OwnerCandidate[] m_OwnerCandidates = new OwnerCandidate[32];
        private readonly HashSet<Entity> m_OwnerPrefabs = new HashSet<Entity>();
        private int m_OwnerIndexFrame = -1;

        private struct LivePosEntry
        {
            public Entity Entity;
            public float3 Position;
        }

        private struct LivePrefabPosEntry
        {
            public Entity Entity;
            public Entity Prefab;
            public float3 Position;
        }

        private const float LiveSpatialCellSize = 16f;
        private int m_LiveNetNodeGridFrame = -1;
        private readonly Dictionary<long, List<LivePosEntry>> m_LiveNetNodeGrid = new Dictionary<long, List<LivePosEntry>>();
        private int m_LiveAttachedEdgeGridFrame = -1;
        private readonly Dictionary<long, List<LivePrefabPosEntry>> m_LiveAttachedEdgeGrid = new Dictionary<long, List<LivePrefabPosEntry>>();
        private int m_LiveAttachedNodeGridFrame = -1;
        private readonly Dictionary<long, List<LivePrefabPosEntry>> m_LiveAttachedNodeGrid = new Dictionary<long, List<LivePrefabPosEntry>>();
        private int m_LiveAttachedObjGridFrame = -1;
        private readonly Dictionary<long, List<LivePrefabPosEntry>> m_LiveAttachedObjGrid = new Dictionary<long, List<LivePrefabPosEntry>>();

        private static long SpatialCellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        private static int SpatialCellCoord(float v) => (int)math.floor(v / LiveSpatialCellSize);

        private void InvalidateLiveSpatialCaches()
        {
            m_LiveNetNodeGridFrame = -1;
            m_LiveNetNodeGrid.Clear();
            m_LiveAttachedEdgeGridFrame = -1;
            m_LiveAttachedEdgeGrid.Clear();
            m_LiveAttachedNodeGridFrame = -1;
            m_LiveAttachedNodeGrid.Clear();
            m_LiveAttachedObjGridFrame = -1;
            m_LiveAttachedObjGrid.Clear();
        }

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
        private int m_NetIdentityRejected;
        private int m_NetVerdictMisses;
        private int m_NetPositionRejected;
        private float m_MaxNetPosDrift;

        // Junction-lookup accounting for the resume pre-pass: one snapshot of the city's junctions per
        // pass, and one array walk per distinct recorded position (see EnsureNetVerdicts).
        private int m_JunctionSnapshotBuilds;
        private int m_JunctionLookups;
        private int m_JunctionScans;
        private int m_JunctionSnapshotNodes;

        internal int JunctionSnapshotBuilds => m_JunctionSnapshotBuilds;
        internal int JunctionLookups => m_JunctionLookups;
        internal int JunctionScans => m_JunctionScans;
        internal int JunctionSnapshotNodes => m_JunctionSnapshotNodes;

        /// <summary>Segments refused because a recorded junction was no longer where it was recorded.</summary>
        internal int NetPositionRejected => m_NetPositionRejected;

        /// <summary>Segments refused because a junction's edges are not the edges it had when captured.</summary>
        internal int NetIdentityRejected => m_NetIdentityRejected;

        private UndoBatchPolicy m_BatchPolicy = new UndoBatchPolicy();

        public bool HasActiveUndo => m_Tracker.HasActiveUndo;
        public bool IsInProgress => m_Tracker.IsInProgress;
        public SelectiveUndoUiSnapshot GetUiSnapshot(UndoAssetMask mask) => m_Tracker.GetUiSnapshot(mask);

        public void ClearFinishedNotice()
        {
            m_Tracker.ClearFinishedNotice();
        }

        public void ClearNonAlertFinishedNotice()
        {
            m_Tracker.ClearNonAlertFinishedNotice();
        }

        public bool HasPendingConnectionRefresh => m_PendingRefreshBuildingPositions.Count > 0 || m_PendingRefreshNetPositions.Count > 0;

        public bool TryConsumeConnectionRefresh(List<float3> buildingPositions, List<float3> netPositions)
        {
            if (!HasPendingConnectionRefresh) return false;
            if (buildingPositions != null) buildingPositions.AddRange(m_PendingRefreshBuildingPositions);
            if (netPositions != null) netPositions.AddRange(m_PendingRefreshNetPositions);
            m_PendingRefreshBuildingPositions.Clear();
            m_PendingRefreshNetPositions.Clear();
            return true;
        }

        private void QueueConnectionRefresh(UndoSnapshot snap)
        {
            m_PendingRefreshBuildingPositions.Clear();
            m_PendingRefreshNetPositions.Clear();
            if (snap == null) return;
            if ((snap.ActiveMask & UndoAssetMask.Buildings) != 0 && snap.Buildings.Count > 0)
            {
                for (int i = 0; i < snap.Buildings.Count; i++)
                {
                    if (IsFinite(snap.Buildings[i].Position))
                    {
                        m_PendingRefreshBuildingPositions.Add(snap.Buildings[i].Position);
                    }
                }
            }
            if ((snap.ActiveMask & UndoAssetMask.Networks) != 0 && snap.Nets.Count > 0)
            {
                for (int i = 0; i < snap.Nets.Count; i++)
                {
                    Bezier4x3 c = snap.Nets[i].Curve;
                    float3 mid = 0.125f * c.a + 0.375f * c.b + 0.375f * c.c + 0.125f * c.d;
                    if (IsFinite(mid))
                    {
                        m_PendingRefreshNetPositions.Add(mid);
                    }
                }
            }
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
            m_FrozenBatchCapture = null;
            m_WaitingOperation = null;
            m_OwnerWaitStarted = -1;
            m_PendingRefreshBuildingPositions.Clear();
            m_PendingRefreshNetPositions.Clear();
            InvalidateLiveSpatialCaches();
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
            m_FrozenBatchCapture = null;
            m_PendingRefreshBuildingPositions.Clear();
            m_PendingRefreshNetPositions.Clear();
            InvalidateLiveSpatialCaches();
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

        private static int GetCapturePriorityTier(EntityManager em, Entity entity)
        {
            if (em.HasComponent<Edge>(entity)) return 0;
            if (em.HasComponent<Building>(entity)) return 1;
            if (em.HasComponent<Area>(entity)) return 2;
            return 3;
        }

        private static List<Entity> CollectPrioritizedCaptureRoots(EntityManager em, IEnumerable<Entity> source)
        {
            var nets = new List<Entity>();
            var buildings = new List<Entity>();
            var areas = new List<Entity>();
            var objects = new List<Entity>();
            var seen = new HashSet<Entity>();

            foreach (Entity entity in source)
            {
                if (entity == Entity.Null || !seen.Add(entity) || !em.Exists(entity) || em.HasComponent<Deleted>(entity))
                {
                    continue;
                }

                if (em.HasComponent<Vehicle>(entity) || em.HasComponent<Creature>(entity) ||
                    (em.HasComponent<NetNode>(entity) && !em.HasComponent<Edge>(entity)))
                {
                    continue;
                }

                switch (GetCapturePriorityTier(em, entity))
                {
                    case 0: nets.Add(entity); break;
                    case 1: buildings.Add(entity); break;
                    case 2: areas.Add(entity); break;
                    default: objects.Add(entity); break;
                }
            }

            if (nets.Count > 1) nets.Sort(EntityOrder.ByIndex);
            if (buildings.Count > 1) buildings.Sort(EntityOrder.ByIndex);
            if (areas.Count > 1) areas.Sort(EntityOrder.ByIndex);
            if (objects.Count > 1) objects.Sort(EntityOrder.ByIndex);

            var ordered = new List<Entity>(nets.Count + buildings.Count + areas.Count + objects.Count);
            ordered.AddRange(nets);
            ordered.AddRange(buildings);
            ordered.AddRange(areas);
            ordered.AddRange(objects);
            return ordered;
        }

        public void PrepareBatchedCapture(EntityManager em, DeletionPlan fullPlan)
        {
            m_FrozenBatchCapture = null;
            m_DeletionCapture = null;
            if (HasActiveUndo || fullPlan == null || fullPlan.Submitted.Count == 0)
            {
                return;
            }

            var frozen = new FrozenBatchCapture();
            List<Entity> ordered = CollectPrioritizedCaptureRoots(em, fullPlan.Roots ?? fullPlan.Submitted);
            int totalWeight = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                Entity entity = ordered[i];
                if (totalWeight >= MaxEntitiesPerSnapshot)
                {
                    frozen.FrozenTruncated = true;
                    frozen.SkippedByBudget.Add(entity);
                    continue;
                }

                int cursor = totalWeight;
                bool localTruncated = false;
                if (TryCaptureSingleRoot(em, entity, fullPlan, ref cursor, ref localTruncated, out FrozenRootRecord record))
                {
                    record.Truncated = localTruncated;
                    frozen.Roots[entity] = record;
                    totalWeight = cursor;
                    if (localTruncated)
                    {
                        frozen.FrozenTruncated = true;
                    }
                }
            }

            frozen.FrozenTotalWeight = totalWeight;
            m_FrozenBatchCapture = frozen;
        }

        public void RecordSubmittedBatch(EntityManager em, DeletionPlan submittedPlan)
        {
            if (HasActiveUndo || submittedPlan == null || submittedPlan.Submitted.Count == 0)
            {
                return;
            }

            if (m_FrozenBatchCapture == null)
            {
                CaptureBeforeDelete(em, submittedPlan, deferCommit: true);
                return;
            }

            var frozen = m_FrozenBatchCapture;
            var roots = submittedPlan.Roots ?? submittedPlan.Submitted;

            foreach (Entity entity in roots)
            {
                if (entity == Entity.Null || !frozen.RecordedRoots.Add(entity))
                {
                    continue;
                }

                if (frozen.Roots.TryGetValue(entity, out FrozenRootRecord frozenRec))
                {
                    frozen.AddSubmitted(frozenRec);
                    continue;
                }

                if (frozen.SkippedByBudget.Contains(entity))
                {
                    frozen.AnySubmittedSkippedByBudget = true;
                    continue;
                }

                int tier = GetCapturePriorityTier(em, entity);
                if (frozen.SubmittedWeightUpToTier(tier) >= MaxEntitiesPerSnapshot)
                {
                    frozen.AnySubmittedSkippedByBudget = true;
                    continue;
                }

                int cursor = 0;
                bool localTruncated = false;
                if (TryCaptureSingleRoot(em, entity, submittedPlan, ref cursor, ref localTruncated, out FrozenRootRecord fallbackRec))
                {
                    fallbackRec.Truncated = localTruncated;
                    frozen.AddSubmitted(fallbackRec);
                }
            }
        }

        public void CaptureBeforeDelete(EntityManager em, DeletionPlan plan, bool deferCommit = false)
        {
            if (plan == null || plan.Submitted.Count == 0)
            {
                return;
            }

            if (HasActiveUndo) return;
            var snapshot = m_DeletionCapture ?? new UndoSnapshot();
            int recordedWeight = snapshot.TotalCount;

            List<Entity> ordered = CollectPrioritizedCaptureRoots(em, plan.Roots ?? plan.Submitted);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot)
                {
                    snapshot.Truncated = true;
                    break;
                }

                Entity entity = ordered[i];
                int tempWeight = recordedWeight;
                bool localTruncated = false;
                if (TryCaptureSingleRoot(em, entity, plan, ref tempWeight, ref localTruncated, out FrozenRootRecord record))
                {
                    if (localTruncated) snapshot.Truncated = true;
                    AppendCapturedRoot(snapshot, record, ref recordedWeight);
                }
            }

            snapshot.TotalCount = recordedWeight;

            if (deferCommit)
                m_DeletionCapture = snapshot;
            else
            {
                m_FrozenBatchCapture = null;
                if (Mod.Settings != null) m_Tracker.MaxHistory = Mod.Settings.MaxUndoHistorySteps;
                m_Tracker.OnCaptureCompleted(snapshot);
            }
        }

        private static bool TryCaptureSingleRoot(
            EntityManager em,
            Entity entity,
            DeletionPlan plan,
            ref int weightCursor,
            ref bool truncated,
            out FrozenRootRecord record)
        {
            record = null;
            if (!em.Exists(entity) || em.HasComponent<Deleted>(entity))
            {
                return false;
            }

            // Never recreate transient simulation entities (vehicles, citizens, animals).
            if (em.HasComponent<Vehicle>(entity) || em.HasComponent<Creature>(entity))
            {
                return false;
            }

            // Root nodes are recreated automatically from restored Edge NetCourse records.
            if (em.HasComponent<NetNode>(entity) && !em.HasComponent<Edge>(entity))
            {
                return false;
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

                if (!ownerInPlan && em.HasComponent<Attached>(entity))
                {
                    Entity parent = em.GetComponentData<Attached>(entity).m_Parent;
                    if (parent != Entity.Null &&
                        (plan.Submitted.Contains(parent) || (plan.Roots != null && plan.Roots.Contains(parent))) &&
                        IsSubNetOf(em, parent, entity))
                    {
                        return false;
                    }
                }

                if (!SelectiveUndoLogic.ShouldCaptureEdgeAsStandalone(hasOwner, ownerInPlan))
                {
                    return false;
                }

                if (TryCaptureNetRecord(em, entity, out UndoNetRecord netRec))
                {
                    weightCursor++;
                    record = new FrozenRootRecord { Kind = UndoOperationKind.Net, Net = netRec, Weight = 1 };
                    return true;
                }

                record = new FrozenRootRecord { Kind = UndoOperationKind.Net, Failed = true };
                return true;
            }

            if (em.HasComponent<Area>(entity))
            {
                if (em.HasComponent<MapTile>(entity) || em.HasComponent<District>(entity) || em.HasComponent<Owner>(entity))
                {
                    return false;
                }

                if (TryCaptureAreaRecord(em, entity, out UndoAreaRecord areaRec))
                {
                    weightCursor++;
                    record = new FrozenRootRecord { Kind = UndoOperationKind.Area, Area = areaRec, Weight = 1 };
                    return true;
                }

                record = new FrozenRootRecord { Kind = UndoOperationKind.Area, Failed = true };
                return true;
            }

            if (em.HasComponent<Building>(entity))
            {
                if (em.HasComponent<Owner>(entity))
                {
                    return false;
                }

                int weightBefore = weightCursor;
                int childNotCaptured = 0;
                if (TryCaptureBuildingRecord(em, entity, ref weightCursor, ref truncated, ref childNotCaptured, out UndoBuildingRecord bldRec))
                {
                    record = new FrozenRootRecord
                    {
                        Kind = UndoOperationKind.Building,
                        Building = bldRec,
                        Weight = weightCursor - weightBefore,
                        BuildingChildNotCaptured = childNotCaptured,
                    };
                    return true;
                }

                record = new FrozenRootRecord
                {
                    Kind = UndoOperationKind.Building,
                    Failed = true,
                    BuildingChildNotCaptured = childNotCaptured,
                };
                return true;
            }

            if (em.HasComponent<GameObject>(entity) && !em.HasComponent<Owner>(entity))
            {
                SelectionCategory objCategory = ClassifyObjectCategory(em, entity);
                if (SelectiveUndoClassifier.ToMaskBit(objCategory) == UndoAssetMask.None)
                {
                    return false;
                }

                if (TryCaptureObjectRecord(em, entity, objCategory, out UndoObjectRecord objRec))
                {
                    weightCursor++;
                    record = new FrozenRootRecord
                    {
                        Kind = UndoOperationKind.Object,
                        ObjCategory = objCategory,
                        Object = objRec,
                        Weight = 1,
                    };
                    return true;
                }

                record = new FrozenRootRecord { Kind = UndoOperationKind.Object, ObjCategory = objCategory, Failed = true };
                return true;
            }

            return false;
        }

        private static void AppendCapturedRoot(UndoSnapshot snapshot, FrozenRootRecord rec, ref int recordedWeight)
        {
            if (rec == null) return;
            switch (rec.Kind)
            {
                case UndoOperationKind.Net:
                    if (rec.Failed) snapshot.NotCaptured.Networks++;
                    else
                    {
                        snapshot.Nets.Add(rec.Net);
                        snapshot.Counts.Networks += rec.Weight;
                        recordedWeight += rec.Weight;
                    }
                    break;
                case UndoOperationKind.Area:
                    if (rec.Failed) snapshot.NotCaptured.Areas++;
                    else
                    {
                        snapshot.Areas.Add(rec.Area);
                        snapshot.Counts.Areas += rec.Weight;
                        recordedWeight += rec.Weight;
                    }
                    break;
                case UndoOperationKind.Building:
                    snapshot.NotCaptured.Buildings += rec.BuildingChildNotCaptured;
                    if (rec.Failed) snapshot.NotCaptured.Buildings++;
                    else
                    {
                        snapshot.Buildings.Add(rec.Building);
                        snapshot.Counts.Buildings += rec.Weight;
                        recordedWeight += rec.Weight;
                    }
                    break;
                case UndoOperationKind.Object:
                    if (rec.Failed) snapshot.NotCaptured.Add(rec.ObjCategory, 1);
                    else
                    {
                        snapshot.Objects.Add(rec.Object);
                        snapshot.Counts.Add(rec.ObjCategory, rec.Weight);
                        recordedWeight += rec.Weight;
                    }
                    break;
            }
        }

        private static void AppendSubmittedTier(UndoSnapshot snapshot, List<FrozenRootRecord> records, ref int recordedWeight)
        {
            for (int i = 0; i < records.Count; i++)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot)
                {
                    snapshot.Truncated = true;
                    break;
                }

                FrozenRootRecord rec = records[i];
                if (rec.Truncated) snapshot.Truncated = true;
                AppendCapturedRoot(snapshot, rec, ref recordedWeight);
            }
        }

        internal DetachedSnapshot CaptureDetachedSnapshot(EntityManager em, DeletionPlan plan)
        {
            return MergeIntoDetachedSnapshot(em, null, plan, null);
        }

        internal DetachedSnapshot MergeIntoDetachedSnapshot(
            EntityManager em,
            DetachedSnapshot target,
            DeletionPlan plan,
            HashSet<Entity> seenOriginals)
        {
            if (plan == null || plan.Submitted.Count == 0)
            {
                return target;
            }

            UndoSnapshot snapshot = (target?.RawSnapshot as UndoSnapshot) ?? new UndoSnapshot();
            int recordedWeight = snapshot.TotalCount;

            List<Entity> ordered = CollectPrioritizedCaptureRoots(em, plan.Roots ?? plan.Submitted);
            for (int i = 0; i < ordered.Count; i++)
            {
                Entity entity = ordered[i];
                if (seenOriginals != null && !seenOriginals.Add(entity))
                {
                    continue;
                }

                if (recordedWeight >= MaxEntitiesPerSnapshot)
                {
                    snapshot.Truncated = true;
                    break;
                }

                int tempWeight = recordedWeight;
                bool localTruncated = false;
                if (TryCaptureSingleRoot(em, entity, plan, ref tempWeight, ref localTruncated, out FrozenRootRecord record))
                {
                    if (localTruncated) snapshot.Truncated = true;
                    AppendCapturedRoot(snapshot, record, ref recordedWeight);
                }
            }

            snapshot.TotalCount = recordedWeight;
            return target != null && ReferenceEquals(target.RawSnapshot, snapshot)
                ? target
                : new DetachedSnapshot(snapshot, snapshot);
        }

        internal static DetachedSnapshot MergeDetachedSnapshots(DetachedSnapshot dstWrap, DetachedSnapshot srcWrap)
        {
            UndoSnapshot src = srcWrap?.RawSnapshot as UndoSnapshot;
            if (src == null || src.TotalCount <= 0)
            {
                return dstWrap;
            }
            UndoSnapshot dst = dstWrap?.RawSnapshot as UndoSnapshot;
            if (dst == null)
            {
                return srcWrap;
            }

            if (dst.TotalCount + src.TotalCount > MaxEntitiesPerSnapshot)
            {
                dst.Truncated = true;
                return dstWrap;
            }

            dst.Nets.AddRange(src.Nets);
            dst.Areas.AddRange(src.Areas);
            dst.Buildings.AddRange(src.Buildings);
            dst.Objects.AddRange(src.Objects);
            dst.Counts.Buildings += src.Counts.Buildings;
            dst.Counts.Networks += src.Counts.Networks;
            dst.Counts.Areas += src.Counts.Areas;
            dst.Counts.Trees += src.Counts.Trees;
            dst.Counts.Props += src.Counts.Props;
            dst.Counts.Decals += src.Counts.Decals;
            dst.NotCaptured.Buildings += src.NotCaptured.Buildings;
            dst.NotCaptured.Networks += src.NotCaptured.Networks;
            dst.NotCaptured.Areas += src.NotCaptured.Areas;
            dst.NotCaptured.Trees += src.NotCaptured.Trees;
            dst.NotCaptured.Props += src.NotCaptured.Props;
            dst.NotCaptured.Decals += src.NotCaptured.Decals;
            dst.TotalCount += src.TotalCount;
            dst.Truncated |= src.Truncated;
            return dstWrap;
        }

        internal DetachedSnapshot FinalizeDeferredCaptureDetached()
        {
            UndoSnapshot snapshot = FinalizeDeferredCaptureInternal();
            return snapshot != null ? new DetachedSnapshot(snapshot, snapshot) : null;
        }

        private UndoSnapshot FinalizeDeferredCaptureInternal()
        {
            FrozenBatchCapture frozen = m_FrozenBatchCapture;
            m_FrozenBatchCapture = null;

            UndoSnapshot snapshot = m_DeletionCapture;
            m_DeletionCapture = null;

            if (frozen != null && frozen.RecordedRoots.Count > 0)
            {
                snapshot ??= new UndoSnapshot();
                int recordedWeight = snapshot.TotalCount;
                if (frozen.AnySubmittedSkippedByBudget)
                {
                    snapshot.Truncated = true;
                }

                AppendSubmittedTier(snapshot, frozen.SubmittedNets, ref recordedWeight);
                AppendSubmittedTier(snapshot, frozen.SubmittedBuildings, ref recordedWeight);
                AppendSubmittedTier(snapshot, frozen.SubmittedAreas, ref recordedWeight);
                AppendSubmittedTier(snapshot, frozen.SubmittedObjects, ref recordedWeight);
                snapshot.TotalCount = recordedWeight;
            }

            return snapshot;
        }

        public void CommitDeletionCapture()
        {
            UndoSnapshot snapshot = FinalizeDeferredCaptureInternal();
            if (snapshot != null)
            {
                if (Mod.Settings != null) m_Tracker.MaxHistory = Mod.Settings.MaxUndoHistorySteps;
                m_Tracker.OnCaptureCompleted(snapshot);
            }
        }

        internal bool StartDetachedUndo(DetachedSnapshot wrap, UndoAssetMask mask)
        {
            UndoSnapshot snap = wrap?.RawSnapshot as UndoSnapshot;
            if (snap == null || !m_Tracker.TryStartExternal(snap, mask))
            {
                return false;
            }

            m_JunctionSnapshotBuilds = 0;
            m_JunctionLookups = 0;
            m_JunctionScans = 0;
            m_JunctionSnapshotNodes = 0;

            m_PendingRefreshBuildingPositions.Clear();
            m_PendingRefreshNetPositions.Clear();
            InvalidateLiveSpatialCaches();
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
            m_NetIdentityRejected = 0;
            m_NetVerdictMisses = 0;
            m_NetPositionRejected = 0;
            m_MaxNetPosDrift = 0f;

            snap.Verdicts = null;
            snap.IdentityGate = false;
            snap.EmittedNets.Clear();
            snap.EmittedNetIndices.Clear();
            snap.EmittedNetClaims = null;
            if ((mask & UndoAssetMask.Buildings) != 0)
            {
                foreach (var building in snap.Buildings)
                {
                    m_OwnerPrefabs.Add(building.Prefab);
                    if (building.Upgrades != null) foreach (var upgrade in building.Upgrades) m_OwnerPrefabs.Add(upgrade.Prefab);
                    if (building.SubObjectSubs != null) foreach (var sub in building.SubObjectSubs) m_OwnerPrefabs.Add(sub.OwnerDef.m_Prefab);
                }
            }
            snap.Operations = EnumerateOperations(snap, mask).GetEnumerator();
            snap.ActiveMask = mask;
            m_NextAllowedFrame = UnityEngine.Time.frameCount;
            return true;
        }

        public bool StartUndo(UndoAssetMask mask)
        {
            if (!m_Tracker.TryStartOrResume(mask, out UndoSnapshot snap, out UndoAssetMask activeMask, out bool isResume))
            {
                return false;
            }

            m_JunctionSnapshotBuilds = 0;
            m_JunctionLookups = 0;
            m_JunctionScans = 0;
            m_JunctionSnapshotNodes = 0;

            if (isResume)
            {
                // A pause is when the city can change: the player may link a new road into a junction
                // this undo judged safe before pausing. Dropping the cache and raising the identity
                // gate makes the next Step decide by identity again; its own emitted segments are told
                // apart by <see cref="UndoSnapshot.EmittedNets"/>, so re-deciding cannot turn this
                // undo's own work into a foreign newcomer.
                snap.Verdicts = null;
                snap.IdentityGate = true;
                snap.ActiveMask = activeMask;
                if (m_WaitingOperation.HasValue) m_OwnerWaitStarted = UnityEngine.Time.frameCount;
                return true;
            }

            m_PendingRefreshBuildingPositions.Clear();
            m_PendingRefreshNetPositions.Clear();
            InvalidateLiveSpatialCaches();
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
            m_NetIdentityRejected = 0;
            m_NetVerdictMisses = 0;
            m_NetPositionRejected = 0;
            m_MaxNetPosDrift = 0f;

            // A snapshot object is reused as history moves, so a fresh start must clear what the
            // previous use of this snapshot left behind: a stale gate would make identity checks run
            // on a batch that is not a resume, and stale emitted records would excuse edges this run
            // never built.
            snap.Verdicts = null;
            snap.IdentityGate = false;
            snap.EmittedNets.Clear();
            snap.EmittedNetIndices.Clear();
            snap.EmittedNetClaims = null;
            if ((activeMask & UndoAssetMask.Buildings) != 0)
            {
                foreach (var building in snap.Buildings)
                {
                    m_OwnerPrefabs.Add(building.Prefab);
                    if (building.Upgrades != null) foreach (var upgrade in building.Upgrades) m_OwnerPrefabs.Add(upgrade.Prefab);
                    if (building.SubObjectSubs != null) foreach (var sub in building.SubObjectSubs) m_OwnerPrefabs.Add(sub.OwnerDef.m_Prefab);
                }
            }
            snap.Operations = EnumerateOperations(snap, activeMask).GetEnumerator();
            snap.ActiveMask = activeMask;
            m_NextAllowedFrame = UnityEngine.Time.frameCount;
            return true;
        }

        public bool CancelActiveUndo()
        {
            return m_Tracker.Pause();
        }

        private struct PrefabSubNetEnv
        {
            public TerrainSystem TerrainSystem;
            public WaterSystem WaterSystem;
            public bool IsLeftHandTraffic;
            public bool TerrainAttempted;
            public bool TerrainAcquired;
            public bool HasTerrain;
            public TerrainHeightData TerrainData;
            public bool WaterAttempted;
            public bool WaterAcquired;
            public bool HasWater;
            public WaterSurfaceData<SurfaceWater> WaterData;
        }

        // Counts successful creation requests, never claims that simulation state was restored.
        public bool Step(EntityManager em, EntityCommandBuffer ecb, out bool completed)
        {
            return Step(em, ecb, null, null, false, out completed);
        }

        public bool Step(
            EntityManager em,
            EntityCommandBuffer ecb,
            TerrainSystem terrainSystem,
            WaterSystem waterSystem,
            bool isLeftHandTraffic,
            out bool completed)
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

            // Junction verdicts, on a resumed snapshot only, are taken here before it emits its first
            // command: once a batch has landed, the junctions include edges this undo itself rebuilt,
            // and judging them then would call our own siblings foreign newcomers. A snapshot that is
            // not a resume keeps no cache at all - its batches check positions only.
            EnsureNetVerdicts(em, snap);
            long started = Stopwatch.GetTimestamp();
            double budget = StepTimeBudgetMs * Stopwatch.Frequency / 1000.0;
            int attempted = 0, buildings = 0, networks = 0;
            bool waiting = false, saturated = false, emittedBuildingWithSubNets = false, hitBoundary = false;
            var env = new PrefabSubNetEnv
            {
                TerrainSystem = terrainSystem,
                WaterSystem = waterSystem,
                IsLeftHandTraffic = isLeftHandTraffic,
            };
            try
            {
                while (attempted < MaxRequestsPerStep)
                {
                    if (!m_WaitingOperation.HasValue && !snap.Operations.MoveNext())
                    {
                        snap.Operations.Dispose();
                        QueueConnectionRefresh(snap);
                        InvalidateLiveSpatialCaches();
                        m_Tracker.CompleteActive();
                        LogRestoreAudit(snap);
                        completed = true;
                        return true;
                    }
                    UndoOperation op = m_WaitingOperation ?? snap.Operations.Current;
                    if (op.Kind == UndoOperationKind.Boundary)
                    {
                        hitBoundary = true;
                        break;
                    }
                    bool buildingRequest = op.Kind == UndoOperationKind.Building || op.Kind == UndoOperationKind.Upgrade || op.Kind == UndoOperationKind.BuildingUnit || op.Kind == UndoOperationKind.SubObjectUnit;
                    bool networkRequest = op.Kind == UndoOperationKind.Net || op.Kind == UndoOperationKind.Area;
                    if (emittedBuildingWithSubNets ||
                        attempted >= m_BatchPolicy.SimpleLimit ||
                        (attempted > 0 && Stopwatch.GetTimestamp() - started >= budget) ||
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
                    bool handledCounts = false;
                    if (op.Kind == UndoOperationKind.SubObjectUnit)
                    {
                        handledCounts = true;
                        if (!ownerValid)
                        {
                            snap.SkippedCount += CountSubObjectUnitItems(op.SubObjectSubs);
                        }
                        else
                        {
                            valid = EmitSubObjectUnit(em, ecb, snap, op.SubObjectSubs, owner, ref env, out bool subEmittedSubNets);
                            if (subEmittedSubNets) emittedBuildingWithSubNets = true;
                        }
                    }
                    else if (ownerValid) switch (op.Kind)
                    {
                        case UndoOperationKind.Net:
                            valid = ValidPrefab<Game.Prefabs.NetData>(em, op.Net.Prefab) &&
                                    (op.Net.IsNodeOnly || IsBezierFinite(op.Net.Curve)) &&
                                    (op.HasOwner || (NetLinksIntact(em, op.Net, op.NetIndex, snap) && LaneEvidenceConfirmed(op.Net)));
                            if (valid)
                            {
                                float liveNodeTolerance = snap.Verdicts == null ? 2.0f : NetConnectivityPolicy.EndpointTolerance;
                                Entity submittedStart, submittedEnd;
                                if (EmitNetRecord(em, ecb, op.Net, op.HasOwner, owner, liveNodeTolerance, out submittedStart, out submittedEnd))
                                {
                                    if (!op.HasOwner)
                                    {
                                        // Remembered so a later re-decide (after a pause) can recognise this
                                        // segment as this undo's own work instead of a road the player spliced
                                        // in. The submitted endpoints are kept, not the recorded ones: a dead
                                        // junction is submitted as Entity.Null and the game builds a new one.
                                        snap.EmittedNets.Add(new EmittedNet
                                        {
                                            Prefab = op.Net.Prefab,
                                            Length = op.Net.Length,
                                            SubmittedStart = submittedStart,
                                            SubmittedEnd = submittedEnd,
                                            RecordedStartPos = op.Net.StartPos,
                                            RecordedEndPos = op.Net.EndPos,
                                        });
                                        snap.EmittedNetIndices.Add(op.NetIndex);
                                    }
                                }
                                else
                                {
                                    valid = false;
                                }
                            }
                            break;
                        case UndoOperationKind.Area: valid = ValidPrefab<Game.Prefabs.AreaData>(em, op.Area.Prefab) && op.Area.Nodes != null && op.Area.Nodes.Length >= 3; if (valid) EmitAreaRecord(em, ecb, op.Area, op.HasOwner, owner); break;
                        case UndoOperationKind.Building: valid = ValidObject(em, op.Building.Prefab, op.Building.Position, op.Building.Rotation) && !OccupiedBeforeRestore(op.Building.Prefab, op.Building.Position); if (valid) EmitBuildingRoot(em, ecb, op.Building); break;
                        case UndoOperationKind.Upgrade: valid = ValidObject(em, op.Upgrade.Prefab, op.Upgrade.Position, op.Upgrade.Rotation); if (valid) EmitUpgrade(em, ecb, op.Upgrade, owner); break;
                        case UndoOperationKind.Object: valid = ValidObject(em, op.Object.Prefab, op.Object.Position, op.Object.Rotation); if (valid) EmitObjectRecord(em, ecb, op.Object); break;
                        case UndoOperationKind.BuildingUnit:
                            handledCounts = true;
                            valid = EmitBuildingUnit(em, ecb, snap, op.Building, ref env, out bool emittedSubNets);
                            if (emittedSubNets) emittedBuildingWithSubNets = true;
                            break;
                    }
                    if (!handledCounts)
                    {
                        if (valid) snap.RestoredCount++;
                        else
                        {
                            snap.SkippedCount++;
                            if (op.Kind == UndoOperationKind.Building) m_FailedOwners.Add(new OwnerDefinition { m_Prefab = op.Building.Prefab, m_Position = op.Building.Position, m_Rotation = op.Building.Rotation });
                            if (op.Kind == UndoOperationKind.Upgrade) m_FailedOwners.Add(new OwnerDefinition { m_Prefab = op.Upgrade.Prefab, m_Position = op.Upgrade.Position, m_Rotation = op.Upgrade.Rotation });
                        }
                    }
                    attempted++;
                    if (buildingRequest) buildings++;
                    if (networkRequest) networks++;
                }
            }
            finally
            {
                if (env.TerrainAcquired && env.TerrainSystem != null)
                {
                    env.TerrainSystem.AddCPUHeightReader(default(JobHandle));
                }
                if (env.WaterAcquired && env.WaterSystem != null)
                {
                    env.WaterSystem.AddSurfaceReader(default(JobHandle));
                }
            }
            double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            m_BatchPolicy.FinishBatch(elapsedMs, saturated || attempted == MaxRequestsPerStep, waiting);
            int stepWait = m_BatchPolicy.FrameGap + 1;
            if (hitBoundary && (snap.ActiveMask & UndoAssetMask.Buildings) != 0 && snap.Nets.Count > 0 && snap.Buildings.Count > 0)
            {
                stepWait = Math.Max(stepWait, 5);
            }
            m_NextAllowedFrame = frame + stepWait;
            return true;
        }

        private static bool HasDeferredUpgrades(in UndoBuildingRecord building)
        {
            if (building.Upgrades == null || building.Upgrades.Count == 0) return false;
            if (!building.HasOwnerSubBuffer) return true;
            for (int i = 0; i < building.Upgrades.Count; i++)
            {
                if (!building.Upgrades[i].IsPhysical) return true;
            }
            return false;
        }

        private static bool HasDeferredSubObjectSubs(in UndoBuildingRecord building)
        {
            return building.SubObjectSubs != null && building.SubObjectSubs.Count > 0;
        }

        private static IEnumerable<UndoOperation> EnumerateOperations(UndoSnapshot snap, UndoAssetMask mask)
        {
            int netIndex = 0;
            bool emittedNetsOrBuildings = false;
            if ((mask & UndoAssetMask.Networks) != 0)
            {
                foreach (var net in snap.Nets)
                {
                    emittedNetsOrBuildings = true;
                    yield return new UndoOperation { Kind = UndoOperationKind.Net, Net = net, NetIndex = netIndex++ };
                }
            }
            if ((mask & UndoAssetMask.Areas) != 0)
            {
                foreach (var area in snap.Areas) yield return new UndoOperation { Kind = UndoOperationKind.Area, Area = area };
            }
            if ((mask & UndoAssetMask.Buildings) != 0)
            {
                if ((mask & UndoAssetMask.Networks) != 0 && snap.Nets.Count > 0 && snap.Buildings.Count > 0)
                {
                    yield return new UndoOperation { Kind = UndoOperationKind.Boundary };
                }

                bool anyDeferredUpgrades = false;
                bool anyDeferredSubObjectSubs = false;
                foreach (var building in snap.Buildings)
                {
                    emittedNetsOrBuildings = true;
                    if (!anyDeferredUpgrades && HasDeferredUpgrades(building)) anyDeferredUpgrades = true;
                    if (!anyDeferredSubObjectSubs && HasDeferredSubObjectSubs(building)) anyDeferredSubObjectSubs = true;
                    yield return new UndoOperation { Kind = UndoOperationKind.BuildingUnit, Building = building };
                }

                // FindOwnersSystem2 runs BEFORE SubObjectSystem in Modification2B and unconditionally
                // strips OwnerDefinition, so SubObject-owned SubNets/SubAreas MUST wait until after a
                // Boundary when SubObjectSystem has spawned the live SubObject entities.
                if (anyDeferredSubObjectSubs || anyDeferredUpgrades)
                {
                    yield return new UndoOperation { Kind = UndoOperationKind.Boundary };
                    if (anyDeferredSubObjectSubs)
                    {
                        foreach (var building in snap.Buildings)
                        {
                            if (!HasDeferredSubObjectSubs(building)) continue;
                            var bldOwner = new OwnerDefinition { m_Prefab = building.Prefab, m_Position = building.Position, m_Rotation = building.Rotation };
                            for (int s = 0; s < building.SubObjectSubs.Count; s++)
                            {
                                UndoSubObjectSubs sub = building.SubObjectSubs[s];
                                yield return new UndoOperation
                                {
                                    Kind = UndoOperationKind.SubObjectUnit,
                                    SubObjectSubs = sub,
                                    HasOwner = true,
                                    AncestorPrefab = building.Prefab,
                                    Ancestor = bldOwner,
                                    Owner = sub.OwnerDef,
                                };
                            }
                        }
                    }
                    if (anyDeferredUpgrades)
                    {
                        foreach (var building in snap.Buildings)
                        {
                            if (!HasDeferredUpgrades(building)) continue;
                            var owner = new OwnerDefinition { m_Prefab = building.Prefab, m_Position = building.Position, m_Rotation = building.Rotation };
                            foreach (var upgrade in building.Upgrades)
                            {
                                if (building.HasOwnerSubBuffer && upgrade.IsPhysical) continue;
                                yield return new UndoOperation { Kind = UndoOperationKind.Upgrade, Upgrade = upgrade, HasOwner = true, Owner = owner };
                                bool hasUpgradeChildren = (upgrade.SubAreas != null && upgrade.SubAreas.Count > 0) ||
                                                          upgrade.PrefabSubNetCount > 0 ||
                                                          (upgrade.SubNets != null && upgrade.SubNets.Count > 0);
                                if (hasUpgradeChildren)
                                    yield return new UndoOperation { Kind = UndoOperationKind.Boundary };
                                var upgradeOwner = new OwnerDefinition { m_Prefab = upgrade.Prefab, m_Position = upgrade.Position, m_Rotation = upgrade.Rotation };
                                if (upgrade.SubAreas != null) foreach (var area in upgrade.SubAreas) yield return new UndoOperation { Kind = UndoOperationKind.Area, Area = area, HasOwner = true, AncestorPrefab = building.Prefab, Ancestor = owner, Owner = upgradeOwner };
                                if (upgrade.PrefabSubNetCount > 0)
                                {
                                    yield return new UndoOperation
                                    {
                                        Kind = UndoOperationKind.SubObjectUnit,
                                        SubObjectSubs = new UndoSubObjectSubs
                                        {
                                            OwnerDef = upgradeOwner,
                                            Seed = upgrade.Seed,
                                            PrefabSubNetCount = upgrade.PrefabSubNetCount,
                                        },
                                        HasOwner = true,
                                        AncestorPrefab = building.Prefab,
                                        Ancestor = owner,
                                        Owner = upgradeOwner,
                                    };
                                }
                                else if (upgrade.SubNets != null)
                                {
                                    foreach (var net in upgrade.SubNets) yield return new UndoOperation { Kind = UndoOperationKind.Net, Net = net, NetIndex = netIndex++, HasOwner = true, AncestorPrefab = building.Prefab, Ancestor = owner, Owner = upgradeOwner };
                                }
                            }
                        }
                    }
                }
            }

            bool needObjectBoundary = false;
            if (emittedNetsOrBuildings)
            {
                foreach (var obj in snap.Objects)
                {
                    UndoAssetMask bit = SelectiveUndoClassifier.ToMaskBit(obj.Category);
                    if ((mask & bit) != 0 && obj.AttachedInfo.OriginalEntity != Entity.Null)
                    {
                        needObjectBoundary = true;
                        break;
                    }
                }
            }
            if (needObjectBoundary)
            {
                yield return new UndoOperation { Kind = UndoOperationKind.Boundary };
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
        /// One read of the city's junctions, taken once per resume pre-pass: the live junction
        /// entities, their positions, and the answers already found for recorded positions.
        /// </summary>
        private sealed class JunctionSnapshot
        {
            public Entity[] Nodes;
            public float3[] Positions;
            public readonly Dictionary<JunctionKey, Entity> Near = new Dictionary<JunctionKey, Entity>();
        }

        /// <summary>
        /// A recorded junction position used as a dictionary key. All three components take part,
        /// because the lookup itself is three-dimensional: keying on XZ alone would hand the answer
        /// found for a junction at height 0 to a junction at height 8 standing in the same spot, cache
        /// included, so the second one would never be searched for at its own height. The value's bits
        /// are used unchanged, so two positions share an answer only when they are the same floats.
        /// Rounding them into a bucket would let the junction found for one position be reused for
        /// another position farther from it, which would change verdicts; -0 is folded into +0, which
        /// compares equal anyway.
        /// </summary>
        private struct JunctionKey : IEquatable<JunctionKey>
        {
            public long X;
            public long Y;
            public long Z;

            public static JunctionKey Of(float3 position)
            {
                float x = position.x == 0f ? 0f : position.x;
                float y = position.y == 0f ? 0f : position.y;
                float z = position.z == 0f ? 0f : position.z;
                return new JunctionKey
                {
                    X = BitConverter.DoubleToInt64Bits(x),
                    Y = BitConverter.DoubleToInt64Bits(y),
                    Z = BitConverter.DoubleToInt64Bits(z),
                };
            }

            public bool Equals(JunctionKey other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is JunctionKey other && Equals(other);
            public override int GetHashCode() => unchecked((int)(X ^ (X >> 32) ^ (Y * 397) ^ (Y >> 31) ^ (Z * 7919) ^ (Z >> 29)));
        }

        /// <summary>
        /// Decides every junction verdict for a resumed snapshot and stores it. It does nothing on a
        /// snapshot that is not a resume: uninterrupted batches check junction positions only, and
        /// giving them a cache would let a stale approval outlive the batch that took it.
        ///
        /// The pass walks the very same operation stream the restore loop walks, so a verdict's index
        /// is the operation's <c>NetIndex</c>. Segments this snapshot already emitted are skipped -
        /// they were accepted once, and the junction now holds their own rebuilt edge.
        /// </summary>
        private void EnsureNetVerdicts(EntityManager em, UndoSnapshot snap)
        {
            if (!snap.IdentityGate) return;
            if (snap.Verdicts != null) return;

            int count = 0;
            foreach (var op in EnumerateOperations(snap, snap.ActiveMask))
            {
                if (op.Kind != UndoOperationKind.Net || op.NetIndex < 0) continue;
                if (op.NetIndex + 1 > count) count = op.NetIndex + 1;
            }

            var verdicts = new NetVerdictCache(count);

            // Claims live on the snapshot, not in this pass. A pause ends the pass but not the undo:
            // rebuilding the allowance every resume would hand a record back its claim, and a splice
            // refused before the pause could be excused after it. Only a fresh start clears them.
            Entity[] claims = EnsureEmittedClaims(snap);

            // One read of the city's junctions serves the whole pass. Adjacent segments commonly share
            // the junction that died with them, so a position is looked up once and its answer reused;
            // without that, every dead endpoint would walk the whole city again. The pass emits
            // nothing, so the live set cannot change while the snapshot is in use.
            m_JunctionSnapshotBuilds++;
            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetNode>()))
            using (var nodes = query.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                // The array's own type is left to inference: this compiles against the game's
                // NativeArray and against the harness stand-in alike.
                var junctions = new JunctionSnapshot
                {
                    Nodes = new Entity[nodes.Length],
                    Positions = new float3[nodes.Length],
                };

                int live = 0;
                for (int i = 0; i < nodes.Length; i++)
                {
                    Entity candidate = nodes[i];
                    if (!em.Exists(candidate) || em.HasComponent<Deleted>(candidate)) continue;
                    junctions.Nodes[live] = candidate;
                    junctions.Positions[live] = em.GetComponentData<NetNode>(candidate).m_Position;
                    live++;
                }

                if (live < junctions.Nodes.Length)
                {
                    System.Array.Resize(ref junctions.Nodes, live);
                    System.Array.Resize(ref junctions.Positions, live);
                }

                m_JunctionSnapshotNodes = junctions.Nodes.Length;

                // Two phases, because recognition is a competition for one allowance per record.
                // Collecting every endpoint before awarding anything makes the outcome depend on what
                // the city holds and on how strict a match is, not on the order the segments happen to
                // be walked: swapping two segments cannot change the result.
                var probes = new List<EndpointProbe>();
                foreach (var op in EnumerateOperations(snap, snap.ActiveMask))
                {
                    if (op.Kind != UndoOperationKind.Net || op.NetIndex < 0) continue;
                    if (snap.EmittedNetIndices.Contains(op.NetIndex)) continue;
                    probes.Add(ProbeEndpoint(em, junctions, op.Net, op.NetIndex, true));
                    probes.Add(ProbeEndpoint(em, junctions, op.Net, op.NetIndex, false));
                }

                ClaimEmittedEdges(em, snap, claims, probes);

                for (int i = 0; i + 1 < probes.Count; i += 2)
                {
                    verdicts.Set(
                        probes[i].NetIndex,
                        JudgeProbe(em, probes[i], claims),
                        JudgeProbe(em, probes[i + 1], claims));
                }
            }

            snap.Verdicts = verdicts;
        }


        /// <summary>
        /// Returns the snapshot's claim list, growing it only when the snapshot emitted more segments
        /// since the last resume. Entries already awarded are carried over untouched.
        /// </summary>
        private static Entity[] EnsureEmittedClaims(UndoSnapshot snap)
        {
            Entity[] claims = snap.EmittedNetClaims;
            if (claims != null && claims.Length == snap.EmittedNets.Count) return claims;

            Entity[] grown = new Entity[snap.EmittedNets.Count];
            if (claims != null) System.Array.Copy(claims, grown, System.Math.Min(claims.Length, grown.Length));
            snap.EmittedNetClaims = grown;
            return grown;
        }

        /// <summary>What one end of a not-yet-emitted segment finds at its junction.</summary>
        private struct EndpointProbe
        {
            public int NetIndex;
            public float3 RecordedPos;
            public int RecordedLinks;
            public Entity[] RecordedIds;
            public bool JunctionFound;
            public bool HasNetNode;
            public Entity Junction;
            public Entity[] ActualIds;
            public int ActualLinks;
        }

        /// <summary>
        /// Resolves one endpoint of a pending segment to the junction it would be spliced against and
        /// reads that junction's edges once. A recorded junction that is gone falls back to a live
        /// junction at the recorded position, and only an empty spot stays empty.
        /// </summary>
        private EndpointProbe ProbeEndpoint(EntityManager em, JunctionSnapshot junctions, in UndoNetRecord rec, int netIndex, bool isStart)
        {
            var probe = new EndpointProbe
            {
                NetIndex = netIndex,
                RecordedPos = isStart ? rec.StartPos : rec.EndPos,
                RecordedLinks = isStart ? rec.StartLinks : rec.EndLinks,
                RecordedIds = isStart ? rec.StartLinkIds : rec.EndLinkIds,
            };

            Entity node = isStart ? rec.StartNode : rec.EndNode;
            if (node == Entity.Null || !em.Exists(node) || em.HasComponent<Deleted>(node))
            {
                Entity replacement;
                if (!TryFindJunctionNear(junctions, probe.RecordedPos, out replacement)) return probe;
                node = replacement;
            }

            probe.JunctionFound = true;
            probe.Junction = node;
            if (!em.HasComponent<NetNode>(node)) return probe;

            probe.HasNetNode = true;
            probe.ActualIds = ReadLinkIdentities(em, node, Entity.Null);
            probe.ActualLinks = ReadLinkCount(em, node);
            return probe;
        }

        /// <summary>
        /// Turns one probed endpoint into a verdict. Pure: the identity work happened in the claim
        /// phase, so this reads the junction position and compares the edge lists that are left.
        /// </summary>
        private static NetLinkStatus JudgeProbe(EntityManager em, in EndpointProbe probe, Entity[] claims)
        {
            if (!probe.JunctionFound) return NetLinkStatus.Reconnected;
            if (!probe.HasNetNode) return NetLinkStatus.IdentityChanged;

            Entity[] actualIds = probe.ActualIds;
            int actualLinks = probe.ActualLinks;
            if (actualIds != null && claims.Length > 0)
            {
                actualIds = WithoutClaimed(probe.RecordedIds, actualIds, claims);
                actualLinks = actualIds.Length;
            }

            NetNode netNode = em.GetComponentData<NetNode>(probe.Junction);
            return NetConnectivityPolicy.EvaluateEndpoint(
                probe.RecordedPos.x, probe.RecordedPos.z, netNode.m_Position.x, netNode.m_Position.z,
                probe.RecordedIds, actualIds,
                probe.RecordedLinks, actualLinks, NetConnectivityPolicy.EndpointTolerance);
        }

        /// <summary>
        /// Says whether the segment may be emitted. Two paths, and which one runs is decided by the
        /// snapshot, never by the batch:
        ///
        /// A plain batch has no verdict cache and checks junction <em>positions</em> only. Edge
        /// identities are not read at all there. The accepted consequence is that a road the player
        /// links while an uninterrupted undo is running is not noticed; that is the stability this
        /// path buys, and it is not a defect to be patched back in per batch.
        ///
        /// A resumed snapshot has a cache taken by <see cref="EnsureNetVerdicts"/> and reads it. A
        /// missing entry, or an index outside the cache, is an internal invariant failure and not a
        /// licence to look at the junction again: by then earlier batches have landed and their
        /// rebuilt edges meet the same junctions, so a live read would refuse segments that are in
        /// fact safe to restore. Such a segment is skipped and counted.
        ///
        /// The cache is not enough on its own. It answers whether the junction is still the recorded
        /// one, not whether it is still where it was, and it is filled once per resume while the
        /// batches that follow can still see the city move. So both ends are position-checked on the
        /// resume path too, before the cache is consulted. That is two component reads, not an
        /// identity read, and it is what stops a segment from being submitted against a junction the
        /// player has since moved.
        /// </summary>
        private bool NetLinksIntact(EntityManager em, in UndoNetRecord rec, int netIndex, UndoSnapshot snap)
        {
            if (snap.Verdicts == null)
            {
                bool startOk = PositionAllowsRebuild(em, rec.StartNode, rec.StartPos, UninterruptedNodeDriftTolerance, out float startDrift);
                bool endOk = PositionAllowsRebuild(em, rec.EndNode, rec.EndPos, UninterruptedNodeDriftTolerance, out float endDrift);
                float maxDrift = startDrift > endDrift ? startDrift : endDrift;
                if (maxDrift > m_MaxNetPosDrift) m_MaxNetPosDrift = maxDrift;
                if (startOk && endOk)
                {
                    return true;
                }

                m_NetPositionRejected++;
                return false;
            }

            // The cache says "this junction is still the one that was recorded"; it cannot say the
            // junction is still where it was. A resume judges identity once, at the moment the player
            // resumes, and the batches that follow can still see the world move, so position is
            // re-checked here on every batch. This is not an identity read: it is two component reads.
            bool resumeStartOk = PositionAllowsRebuild(em, rec.StartNode, rec.StartPos, NetConnectivityPolicy.EndpointTolerance, out float resumeStartDrift);
            bool resumeEndOk = PositionAllowsRebuild(em, rec.EndNode, rec.EndPos, NetConnectivityPolicy.EndpointTolerance, out float resumeEndDrift);
            float resumeDrift = resumeStartDrift > resumeEndDrift ? resumeStartDrift : resumeEndDrift;
            if (resumeDrift > m_MaxNetPosDrift) m_MaxNetPosDrift = resumeDrift;
            if (!resumeStartOk || !resumeEndOk)
            {
                m_NetPositionRejected++;
                return false;
            }

            NetLinkStatus start, end;
            if (!snap.Verdicts.TryGet(netIndex, out start, out end))
            {
                m_NetVerdictMisses++;
                return false;
            }

            if (NetVerdictCache.AllowsRebuild(start) && NetVerdictCache.AllowsRebuild(end))
            {
                return true;
            }

            if (NetVerdictCache.IsIdentityRefusal(start) || NetVerdictCache.IsIdentityRefusal(end))
            {
                m_NetIdentityRejected++;
            }
            else
            {
                m_ConnectivitySkips++;
            }

            return false;
        }

        /// <summary>
        /// Position check for a still-live recorded junction. On an uninterrupted undo CS2's own
        /// intersection geometry update can shift a surviving boundary node slightly when adjacent
        /// edges are removed, so a wider tolerance is used and the rebuilt curve snaps onto the
        /// node's live position; after a pause/resume the stricter EndpointTolerance still applies.
        /// </summary>
        private static bool PositionAllowsRebuild(EntityManager em, Entity node, float3 recorded, float tolerance, out float drift)
        {
            drift = 0f;
            if (node == Entity.Null || !em.Exists(node) || em.HasComponent<Deleted>(node)) return true;
            if (!em.HasComponent<NetNode>(node)) return true;

            NetNode netNode = em.GetComponentData<NetNode>(node);
            float dx = netNode.m_Position.x - recorded.x;
            float dz = netNode.m_Position.z - recorded.z;
            float distSq = dx * dx + dz * dz;
            if (distSq > 0f) drift = math.sqrt(distSq);
            return distSq <= tolerance * tolerance;
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

        /// Finds a live junction standing within
        /// <see cref="NetConnectivityPolicy.EndpointTolerance"/> of a recorded position, nearest first.
        /// The distance is three-dimensional: junctions at the same XZ but a different height are
        /// different places (an interchange stacks them), and a flat comparison would pick the wrong one.
        /// Used only when the recorded junction entity is gone: a new junction at that spot is not the
        /// one this segment was recorded against, but it is where the segment would be spliced, so its
        /// edges decide.
        ///
        /// The city's junctions are read once per resume pre-pass by <see cref="EnsureNetVerdicts"/>;
        /// this method never builds a query of its own, it only walks that snapshot. Answers are cached
        /// per recorded position for the rest of the pass, because adjacent segments commonly record
        /// the same dead junction. The cost is one array walk per distinct dead-junction position: with
        /// N junctions and U distinct dead positions it is O(N) component reads plus O(U x N)
        /// comparisons, instead of one query, one array copy and a component read per candidate for
        /// every dead endpoint.
        /// </summary>
        private bool TryFindJunctionNear(JunctionSnapshot junctions, float3 recorded, out Entity found)
        {
            m_JunctionLookups++;
            JunctionKey key = JunctionKey.Of(recorded);
            if (junctions.Near.TryGetValue(key, out found))
            {
                return found != Entity.Null;
            }

            m_JunctionScans++;
            found = Entity.Null;
            float best = NetConnectivityPolicy.EndpointTolerance * NetConnectivityPolicy.EndpointTolerance;
            for (int i = 0; i < junctions.Nodes.Length; i++)
            {
                // Height is part of the distance on purpose: an interchange stacks several junctions on
                // the same XZ, and a flat comparison would settle on whichever one happens to be listed
                // first instead of the one on the recorded ground.
                float distance = math.distancesq(junctions.Positions[i], recorded);
                if (distance <= best)
                {
                    best = distance;
                    found = junctions.Nodes[i];
                }
            }

            junctions.Near[key] = found;
            return found != Entity.Null;
        }

        /// <summary>

        /// <summary>True when the recording already knew this edge at the junction being judged.</summary>
        private static bool IsRecordedLink(Entity[] recordedIds, Entity edge)
        {
            if (recordedIds == null) return false;
            for (int i = 0; i < recordedIds.Length; i++)
            {
                if (recordedIds[i].Index == edge.Index && recordedIds[i].Version == edge.Version) return true;
            }

            return false;
        }

        /// <summary>
        /// <summary>
        /// The shape of an edge as the game stores it, read once and reused by every tier of the
        /// fingerprint: the prefab, the length, and the two junctions it meets.
        /// </summary>
        private struct EdgeShape
        {
            public Entity Prefab;
            public float Length;
            public Entity Start;
            public Entity End;
        }

        /// <summary>One pairing of a submitted record with an edge actually standing at a judged junction.</summary>
        private struct ClaimCandidate
        {
            public int Tier;
            public int Record;
            public Entity Edge;
            public int Sequence;
        }

        /// <summary>
        /// Grants each submitted record at most one edge entity for the whole prejudge, choosing
        /// strictly before loosely.
        ///
        /// The old shape matched inside the per-junction walk and claimed on the spot, which made the
        /// answer depend on the order segments happened to be visited: a record could be taken by a
        /// loose one-end match before a second junction offered the exact edge that match was for.
        /// Nothing in the city changed between those two orders, yet the outcome did. So every pairing
        /// is collected first, sorted by tier and then by record and edge identity, and only then
        /// awarded. An exact match of both submitted junctions therefore always precedes a one-ended
        /// match, which always precedes a geometric one, whatever the operation order is.
        ///
        /// An edge the recording already listed at a judged junction is a neighbour the deletion left
        /// behind, not something this undo built: it never competes for an allowance.
        /// </summary>
        private static void ClaimEmittedEdges(EntityManager em, UndoSnapshot snap, Entity[] claims, List<EndpointProbe> probes)
        {
            if (snap.EmittedNets.Count == 0) return;

            var candidates = new List<ClaimCandidate>();
            int sequence = 0;
            for (int p = 0; p < probes.Count; p++)
            {
                EndpointProbe probe = probes[p];
                if (!probe.HasNetNode || probe.ActualIds == null) continue;

                for (int e = 0; e < probe.ActualIds.Length; e++)
                {
                    Entity edge = probe.ActualIds[e];
                    if (IsRecordedLink(probe.RecordedIds, edge)) continue;
                    if (IsClaimed(claims, edge)) continue;

                    EdgeShape shape;
                    if (!TryReadEdgeShape(em, edge, out shape)) continue;

                    for (int i = 0; i < snap.EmittedNets.Count; i++)
                    {
                        if (claims[i] != Entity.Null) continue;
                        int tier = ClaimTier(em, shape, snap.EmittedNets[i]);
                        if (tier == 0) continue;
                        candidates.Add(new ClaimCandidate { Tier = tier, Record = i, Edge = edge, Sequence = sequence++ });
                    }
                }
            }

            candidates.Sort(CompareCandidates);

            for (int c = 0; c < candidates.Count; c++)
            {
                ClaimCandidate candidate = candidates[c];
                if (claims[candidate.Record] != Entity.Null) continue;
                if (IsClaimed(claims, candidate.Edge)) continue;
                claims[candidate.Record] = candidate.Edge;
            }
        }

        private static int CompareCandidates(ClaimCandidate a, ClaimCandidate b)
        {
            if (a.Tier != b.Tier) return a.Tier < b.Tier ? -1 : 1;
            if (a.Record != b.Record) return a.Record < b.Record ? -1 : 1;
            if (a.Edge.Index != b.Edge.Index) return a.Edge.Index < b.Edge.Index ? -1 : 1;
            if (a.Edge.Version != b.Edge.Version) return a.Edge.Version < b.Edge.Version ? -1 : 1;
            if (a.Sequence != b.Sequence) return a.Sequence < b.Sequence ? -1 : 1;
            return 0;
        }

        private static bool TryReadEdgeShape(EntityManager em, Entity edge, out EdgeShape shape)
        {
            shape = default;
            if (edge == Entity.Null || !em.Exists(edge) || !em.HasComponent<Edge>(edge) ||
                !em.HasComponent<Curve>(edge) || !em.HasComponent<PrefabRef>(edge))
            {
                return false;
            }

            float length = em.GetComponentData<Curve>(edge).m_Length;
            if (!math.isfinite(length)) return false;

            Game.Net.Edge links = em.GetComponentData<Edge>(edge);
            shape.Prefab = em.GetComponentData<PrefabRef>(edge).m_Prefab;
            shape.Length = length;
            shape.Start = links.m_Start;
            shape.End = links.m_End;
            return true;
        }

        /// <summary>
        /// Which widening of the fingerprint an edge satisfies for one record, or 0 for none. 1 is the
        /// exact pair of submitted junctions, 2 is the single submitted junction, 3 is the geometry of
        /// a record that submitted no junction at all. Strictness is a number here so that the award
        /// can be ordered by it; the matching itself is the same evidence the restore documents.
        /// </summary>
        private static int ClaimTier(EntityManager em, in EdgeShape shape, in EmittedNet emitted)
        {
            if (emitted.Prefab != shape.Prefab) return 0;
            if (math.abs(shape.Length - emitted.Length) > OwnEdgeLengthTolerance) return 0;

            bool startUnknown = emitted.SubmittedStart == Entity.Null;
            bool endUnknown = emitted.SubmittedEnd == Entity.Null;

            if (!startUnknown && !endUnknown)
            {
                bool forward = shape.Start == emitted.SubmittedStart && shape.End == emitted.SubmittedEnd;
                bool reverse = shape.Start == emitted.SubmittedEnd && shape.End == emitted.SubmittedStart;
                return (forward || reverse) ? 1 : 0;
            }

            if (startUnknown ^ endUnknown)
            {
                Entity known = startUnknown ? emitted.SubmittedEnd : emitted.SubmittedStart;
                return (shape.Start == known || shape.End == known) ? 2 : 0;
            }

            return EdgeMeetsRecordedGeometry(em, shape, emitted) ? 3 : 0;
        }

        /// <summary>
        /// Drops the edges a submitted record has already excused. Null in, null out: an unreadable
        /// junction stays unreadable.
        ///
        /// An edge that <paramref name="recordedIds"/> already lists met this junction when the segment
        /// was captured, so it is a neighbour the deletion left in place: it is kept, and it never took
        /// part in a claim either. Everything the submitted records hold is excused; everything else is
        /// the player's and stays in the list for the comparison to refuse.
        /// </summary>
        private static Entity[] WithoutClaimed(Entity[] recordedIds, Entity[] actualIds, Entity[] claims)
        {
            int kept = 0;
            for (int i = 0; i < actualIds.Length; i++)
            {
                Entity edge = actualIds[i];
                if (IsRecordedLink(recordedIds, edge) || !IsClaimed(claims, edge))
                {
                    actualIds[kept++] = edge;
                }
            }

            if (kept == actualIds.Length) return actualIds;
            if (kept == 0) return System.Array.Empty<Entity>();

            Entity[] trimmed = new Entity[kept];
            System.Array.Copy(actualIds, trimmed, kept);
            return trimmed;
        }

        /// <summary>True when some submitted record already holds this edge.</summary>
        private static bool IsClaimed(Entity[] claims, Entity edge)
        {
            if (claims == null) return false;
            for (int i = 0; i < claims.Length; i++)
            {
                if (claims[i] == Entity.Null) continue;
                if (claims[i].Index == edge.Index && claims[i].Version == edge.Version) return true;
            }

            return false;
        }

        /// <summary>
        /// True when the edge stands where the recorded segment stood at <em>both</em> ends, in either
        /// direction, within <see cref="NetConnectivityPolicy.EndpointTolerance"/> in three dimensions.
        /// One end alone is not enough: it only says the road is near one of the recorded junctions, and
        /// a road leaving that junction the other way satisfies it while occupying the ground this
        /// segment needs. Height is part of the distance because an interchange stacks junctions at one
        /// XZ, and a rebuilt junction keeps the recorded ground position.
        ///
        /// Both ends inside the tolerance still says nothing about the road between them: the curve is
        /// not compared, so a road the player built over the same ground, leaving the same two rebuilt
        /// junctions with the same prefab and a near-identical length, is accepted as this undo's own.
        /// This case narrows the earlier single-ended test; it does not remove the misidentification.
        /// </summary>
        private static bool EdgeMeetsRecordedGeometry(EntityManager em, in EdgeShape shape, in EmittedNet emitted)
        {
            if (!TryReadJunctionPosition(em, shape.Start, out float3 start)) return false;
            if (!TryReadJunctionPosition(em, shape.End, out float3 end)) return false;

            float tolerance = NetConnectivityPolicy.EndpointTolerance;
            bool forward = Distance3(start, emitted.RecordedStartPos) <= tolerance &&
                           Distance3(end, emitted.RecordedEndPos) <= tolerance;
            bool reverse = Distance3(start, emitted.RecordedEndPos) <= tolerance &&
                           Distance3(end, emitted.RecordedStartPos) <= tolerance;
            return forward || reverse;
        }

        private static bool TryReadJunctionPosition(EntityManager em, Entity node, out float3 position)
        {
            position = default;
            if (node == Entity.Null || !em.Exists(node) || em.HasComponent<Deleted>(node) ||
                !em.HasComponent<NetNode>(node)) return false;
            position = em.GetComponentData<NetNode>(node).m_Position;
            return true;
        }

        private static float Distance3(float3 a, float3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return math.sqrt(dx * dx + dy * dy + dz * dz);
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

        private static bool IsSubNetOf(EntityManager em, Entity parent, Entity edge)
        {
            if (parent == Entity.Null || edge == Entity.Null || !em.Exists(parent) || !em.HasBuffer<Game.Net.SubNet>(parent))
            {
                return false;
            }

            DynamicBuffer<Game.Net.SubNet> buf = em.GetBuffer<Game.Net.SubNet>(parent, true);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].m_SubNet == edge) return true;
            }
            return false;
        }

        private static int ReadParentMesh(EntityManager em, Entity entity, int fallback)
        {
            if (entity != Entity.Null && em.Exists(entity) && em.HasComponent<LocalTransformCache>(entity))
            {
                return em.GetComponentData<LocalTransformCache>(entity).m_ParentMesh;
            }
            return fallback;
        }

        private static UndoAttachedRef CaptureAttachedRef(EntityManager em, Entity attachedParent)
        {
            if (attachedParent == Entity.Null || !em.Exists(attachedParent) || em.HasComponent<Deleted>(attachedParent))
            {
                return default;
            }

            Entity prefab = em.HasComponent<PrefabRef>(attachedParent)
                ? em.GetComponentData<PrefabRef>(attachedParent).m_Prefab
                : Entity.Null;

            if (em.HasComponent<Edge>(attachedParent) && em.HasComponent<Curve>(attachedParent))
            {
                Bezier4x3 b = em.GetComponentData<Curve>(attachedParent).m_Bezier;
                float3 mid = 0.125f * b.a + 0.375f * b.b + 0.375f * b.c + 0.125f * b.d;
                return new UndoAttachedRef
                {
                    OriginalEntity = attachedParent,
                    Prefab = prefab,
                    Position = IsFinite(mid) ? mid : float3.zero,
                    IsNetEdge = true,
                };
            }

            if (em.HasComponent<NetNode>(attachedParent))
            {
                float3 pos = em.GetComponentData<NetNode>(attachedParent).m_Position;
                return new UndoAttachedRef
                {
                    OriginalEntity = attachedParent,
                    Prefab = prefab,
                    Position = IsFinite(pos) ? pos : float3.zero,
                    IsNetNode = true,
                };
            }

            if (em.HasComponent<Game.Objects.Transform>(attachedParent))
            {
                float3 pos = em.GetComponentData<Game.Objects.Transform>(attachedParent).m_Position;
                return new UndoAttachedRef
                {
                    OriginalEntity = attachedParent,
                    Prefab = prefab,
                    Position = IsFinite(pos) ? pos : float3.zero,
                };
            }

            return new UndoAttachedRef { OriginalEntity = attachedParent, Prefab = prefab };
        }

        private static bool IsNodeOwnedBy(EntityManager em, Entity node, Entity expectedOwner)
        {
            if (expectedOwner == Entity.Null || node == Entity.Null || !em.Exists(node) || !em.HasComponent<Owner>(node))
            {
                return false;
            }
            return em.GetComponentData<Owner>(node).m_Owner == expectedOwner;
        }

        /// <summary>
        /// Outside Editor mode, live building SubNet nodes do not carry LocalTransformCache, so
        /// ReadParentMesh falls back to -1. CourseSplitSystem.CalculateElevation zeroes elevation on
        /// owned SubNets whenever m_ParentMesh &lt; 0. Recover exact m_ParentMesh from the owner
        /// prefab's Game.Prefabs.SubNet buffer by matching world endpoints (forward or LHT-inverted).
        /// </summary>
        private static void ResolveSubNetParentMeshes(
            EntityManager em,
            Entity owner,
            Entity subNetPrefab,
            float3 startPos,
            float3 endPos,
            ref int startParentMesh,
            ref int endParentMesh)
        {
            if (owner == Entity.Null || !em.Exists(owner) ||
                !em.HasComponent<PrefabRef>(owner) || !em.HasComponent<Game.Objects.Transform>(owner))
            {
                return;
            }

            Entity ownerPrefab = em.GetComponentData<PrefabRef>(owner).m_Prefab;
            if (ownerPrefab == Entity.Null || !em.Exists(ownerPrefab) ||
                !em.HasBuffer<Game.Prefabs.SubNet>(ownerPrefab))
            {
                return;
            }

            var ownerTr = em.GetComponentData<Game.Objects.Transform>(owner);
            if (!IsFinite(ownerTr.m_Position) || !IsFinite(ownerTr.m_Rotation))
            {
                return;
            }

            DynamicBuffer<Game.Prefabs.SubNet> prefabSubNets = em.GetBuffer<Game.Prefabs.SubNet>(ownerPrefab, true);
            const float maxEndpointDistSq = 1.0f; // 1.0m XZ tolerance per endpoint
            float bestScore = float.MaxValue;
            int matchedStart = startParentMesh;
            int matchedEnd = endParentMesh;

            for (int i = 0; i < prefabSubNets.Length; i++)
            {
                Game.Prefabs.SubNet psn = prefabSubNets[i];
                if (psn.m_Prefab != subNetPrefab) continue;

                float3 wa = ownerTr.m_Position + math.rotate(ownerTr.m_Rotation, psn.m_Curve.a);
                float3 wd = ownerTr.m_Position + math.rotate(ownerTr.m_Rotation, psn.m_Curve.d);

                float2 dsa = new float2(wa.x - startPos.x, wa.z - startPos.z);
                float2 deb = new float2(wd.x - endPos.x, wd.z - endPos.z);
                float fwdA = math.lengthsq(dsa);
                float fwdD = math.lengthsq(deb);
                if (fwdA <= maxEndpointDistSq && fwdD <= maxEndpointDistSq)
                {
                    float score = fwdA + fwdD;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        matchedStart = psn.m_ParentMesh.x;
                        matchedEnd = psn.m_ParentMesh.y;
                    }
                }

                float2 rds = new float2(wd.x - startPos.x, wd.z - startPos.z);
                float2 rde = new float2(wa.x - endPos.x, wa.z - endPos.z);
                float revA = math.lengthsq(rds);
                float revD = math.lengthsq(rde);
                if (revA <= maxEndpointDistSq && revD <= maxEndpointDistSq)
                {
                    float score = revA + revD;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        matchedStart = psn.m_ParentMesh.y;
                        matchedEnd = psn.m_ParentMesh.x;
                    }
                }
            }

            if (bestScore < float.MaxValue)
            {
                if (startParentMesh < 0) startParentMesh = matchedStart;
                if (endParentMesh < 0) endParentMesh = matchedEnd;
            }
        }

        private static bool TryCaptureNetRecord(EntityManager em, Entity entity, out UndoNetRecord rec, Entity expectedOwner = default)
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
            if (em.HasComponent<Game.Net.Composition>(entity))
            {
                Entity compEdge = em.GetComponentData<Game.Net.Composition>(entity).m_Edge;
                if (compEdge != Entity.Null && em.Exists(compEdge) && em.HasComponent<NetCompositionData>(compEdge))
                {
                    CompositionFlags.General compGen = em.GetComponentData<NetCompositionData>(compEdge).m_Flags.m_General;
                    CompositionFlags.General extra = compGen & (CompositionFlags.General.Elevated | CompositionFlags.General.Invert);
                    if (extra != 0)
                    {
                        upgFlags.m_General |= extra;
                        hasUpgraded = true;
                    }
                }
            }

            bool startOwned = IsNodeOwnedBy(em, edge.m_Start, expectedOwner);
            bool endOwned = IsNodeOwnedBy(em, edge.m_End, expectedOwner);
            int startParentMesh = ReadParentMesh(em, edge.m_Start, -1);
            int endParentMesh = ReadParentMesh(em, edge.m_End, -1);
            if (expectedOwner != Entity.Null && (startParentMesh < 0 || endParentMesh < 0))
            {
                ResolveSubNetParentMeshes(em, expectedOwner, prefab, startNode.m_Position, endNode.m_Position, ref startParentMesh, ref endParentMesh);
            }

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
                IsNodeOnly = false,
                StartParentMesh = startParentMesh,
                EndParentMesh = endParentMesh,
                StartNodeOwnedByParent = startOwned,
                EndNodeOwnedByParent = endOwned,
            };
            rec.StartLinkIds = ReadLinkIdentities(em, edge.m_Start, entity);
            rec.EndLinkIds = ReadLinkIdentities(em, edge.m_End, entity);
            ReadLaneEvidence(em, entity, out rec.LaneCount, out rec.LaneOwned, out rec.LaneForeign);
            return true;
        }

        private static bool TryCaptureNodeOnlySubNet(EntityManager em, Entity nodeEntity, Entity expectedOwner, out UndoNetRecord rec)
        {
            rec = default;
            if (!em.HasComponent<PrefabRef>(nodeEntity) || !em.HasComponent<NetNode>(nodeEntity))
            {
                return false;
            }

            Entity prefab = em.GetComponentData<PrefabRef>(nodeEntity).m_Prefab;
            if (prefab == Entity.Null || !em.Exists(prefab))
            {
                return false;
            }

            NetNode node = em.GetComponentData<NetNode>(nodeEntity);
            if (!IsFinite(node.m_Position) || !IsFinite(node.m_Rotation))
            {
                return false;
            }

            float2 nodeElev = em.HasComponent<NetElevation>(nodeEntity)
                ? em.GetComponentData<NetElevation>(nodeEntity).m_Elevation
                : float2.zero;
            if (!IsFinite(nodeElev)) nodeElev = float2.zero;

            int parentMesh = ReadParentMesh(em, nodeEntity, -1);
            if (expectedOwner != Entity.Null && parentMesh < 0)
            {
                int endMesh = parentMesh;
                ResolveSubNetParentMeshes(em, expectedOwner, prefab, node.m_Position, node.m_Position, ref parentMesh, ref endMesh);
            }
            bool hasUpgraded = em.HasComponent<Upgraded>(nodeEntity);
            CompositionFlags upgFlags = hasUpgraded ? em.GetComponentData<Upgraded>(nodeEntity).m_Flags : default;

            rec = new UndoNetRecord
            {
                Prefab = prefab,
                StartNode = nodeEntity,
                EndNode = nodeEntity,
                StartPos = node.m_Position,
                StartRot = node.m_Rotation,
                StartElevation = nodeElev,
                EndPos = node.m_Position,
                EndRot = node.m_Rotation,
                EndElevation = nodeElev,
                Curve = new Bezier4x3(node.m_Position, node.m_Position, node.m_Position, node.m_Position),
                Length = 0f,
                EdgeElevation = nodeElev,
                HasUpgraded = hasUpgraded,
                UpgradedFlags = upgFlags,
                Seed = ReadSeed(em, nodeEntity, prefab),
                IsNodeOnly = true,
                StartParentMesh = parentMesh,
                EndParentMesh = parentMesh,
                StartNodeOwnedByParent = expectedOwner == Entity.Null || !em.HasComponent<Owner>(nodeEntity) || em.GetComponentData<Owner>(nodeEntity).m_Owner == expectedOwner,
                EndNodeOwnedByParent = expectedOwner == Entity.Null || !em.HasComponent<Owner>(nodeEntity) || em.GetComponentData<Owner>(nodeEntity).m_Owner == expectedOwner,
            };
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
        /// Records which edges met a junction, by index and version, leaving out the segment being
        /// captured. Returns null when the junction cannot be read at all: an unreadable junction is
        /// missing evidence, while an empty array would claim the junction held nothing, which is a
        /// far stronger statement. The two must not collapse into each other.
        /// Sorted so equal junctions always produce equal arrays, which keeps captured records
        /// comparable and the snapshot deterministic.
        /// </summary>
        private static Entity[] ReadLinkIdentities(EntityManager em, Entity node, Entity self)
        {
            if (node == Entity.Null || !em.Exists(node) || !em.HasBuffer<ConnectedEdge>(node))
            {
                return null;
            }

            DynamicBuffer<ConnectedEdge> links = em.GetBuffer<ConnectedEdge>(node, true);
            int count = 0;
            for (int i = 0; i < links.Length; i++)
            {
                if (IsLinkCandidate(em, links[i].m_Edge, self))
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return System.Array.Empty<Entity>();
            }

            var ids = new Entity[count];
            int at = 0;
            for (int i = 0; i < links.Length; i++)
            {
                Entity edge = links[i].m_Edge;
                if (IsLinkCandidate(em, edge, self))
                {
                    ids[at++] = edge;
                }
            }

            System.Array.Sort(ids, CompareLinkIdentity);
            return ids;
        }

        private static bool IsLinkCandidate(EntityManager em, Entity edge, Entity self)
        {
            return edge != Entity.Null && edge != self && em.Exists(edge) && !em.HasComponent<Deleted>(edge);
        }

        private static int CompareLinkIdentity(Entity a, Entity b)
        {
            if (a.Index != b.Index) return a.Index < b.Index ? -1 : 1;
            if (a.Version != b.Version) return a.Version < b.Version ? -1 : 1;
            return 0;
        }

        /// <summary>
        /// Writes what the restore actually managed and what it refused, so the save/reload check has
        /// the same numbers the player was shown.
        /// </summary>
        private void LogRestoreAudit(UndoSnapshot snap)
        {
            if (snap == null) return;
            string driftSuffix = m_MaxNetPosDrift > 0.05f ? $", MaxPosDrift: {m_MaxNetPosDrift:F2}m" : string.Empty;
            MarqueeDeleteToolSystem.AppendStatusLine(
                $"[State: undo-restore, Restored: {snap.RestoredCount}, Skipped: {snap.SkippedCount}, Total: {snap.TotalCount}, " +
                $"AmbiguousOwners: {m_AmbiguousOwners}, ConnectivitySkips: {m_ConnectivitySkips}, OccupiedLots: {m_OccupiedRestores}, " +
                $"LaneUnconfirmed: {m_LaneUnconfirmed}, NetIdentityRejected: {m_NetIdentityRejected}, " +
                $"NetPositionRejected: {m_NetPositionRejected}, NetVerdictMiss: {m_NetVerdictMisses}{driftSuffix}]");
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

        private static int GetPrefabSubNetCount(EntityManager em, Entity prefab)
        {
            return prefab != Entity.Null && em.Exists(prefab) && em.HasBuffer<Game.Prefabs.SubNet>(prefab)
                ? em.GetBuffer<Game.Prefabs.SubNet>(prefab, true).Length
                : 0;
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
            int prefabSubNetCount = GetPrefabSubNetCount(em, prefab);
            List<UndoNetRecord> subNets = null;
            if (prefabSubNetCount > 0)
            {
                recordedWeight += prefabSubNetCount;
                if (recordedWeight > MaxEntitiesPerSnapshot) truncated = true;
            }
            else
            {
                subNets = CaptureSubNets(em, entity, ref recordedWeight, ref truncated, ref notCaptured);
            }
            List<UndoUpgradeRecord> upgrades = CaptureInstalledUpgrades(em, entity, tr, ref recordedWeight, ref truncated, ref notCaptured, out HashSet<Entity> upgradeSet);
            List<UndoSubObjectSubs> subObjectSubs = CaptureSubObjectSubs(em, entity, upgradeSet, ref recordedWeight, ref truncated, ref notCaptured);

            bool hasOwnerSubBuffer = em.HasBuffer<Game.Net.SubNet>(entity) ||
                                     em.HasBuffer<Game.Areas.SubArea>(entity) ||
                                     em.HasBuffer<Game.Objects.SubObject>(entity);

            rec = new UndoBuildingRecord
            {
                Prefab = prefab,
                Position = tr.m_Position,
                Rotation = tr.m_Rotation,
                HasElevation = hasElev,
                Elevation = hasElev ? elev.m_Elevation : 0f,
                ElevationFlags = hasElev ? elev.m_Flags : 0,
                AttachedParent = attachedParent,
                AttachedInfo = CaptureAttachedRef(em, attachedParent),
                HasOwnerSubBuffer = hasOwnerSubBuffer,
                Seed = ReadSeed(em, entity, prefab),
                PrefabSubNetCount = prefabSubNetCount,
                Upgrades = upgrades,
                SubAreas = subAreas,
                SubNets = subNets,
                SubObjectSubs = subObjectSubs,
            };
            return true;
        }

        private static void EnqueueSubObjectChildren(
            EntityManager em,
            Entity owner,
            HashSet<Entity> upgradeSet,
            HashSet<Entity> visited,
            Queue<Entity> queue)
        {
            if (!em.HasBuffer<Game.Objects.SubObject>(owner)) return;
            DynamicBuffer<Game.Objects.SubObject> children = em.GetBuffer<Game.Objects.SubObject>(owner, true);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].m_SubObject;
                if (child == Entity.Null || !visited.Add(child)) continue;
                if (upgradeSet != null && upgradeSet.Contains(child)) continue;
                queue.Enqueue(child);
            }
        }

        private static List<UndoSubObjectSubs> CaptureSubObjectSubs(
            EntityManager em,
            Entity rootOwner,
            HashSet<Entity> upgradeSet,
            ref int recordedWeight,
            ref bool truncated,
            ref int notCaptured)
        {
            if (!em.HasBuffer<Game.Objects.SubObject>(rootOwner))
            {
                return null;
            }

            List<UndoSubObjectSubs> results = null;
            var visited = new HashSet<Entity> { rootOwner };
            var queue = new Queue<Entity>();

            EnqueueSubObjectChildren(em, rootOwner, upgradeSet, visited, queue);
            while (queue.Count > 0)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot) { truncated = true; break; }
                Entity subObj = queue.Dequeue();
                if (!em.Exists(subObj) || em.HasComponent<Deleted>(subObj)) continue;

                EnqueueSubObjectChildren(em, subObj, upgradeSet, visited, queue);

                if (!em.HasComponent<PrefabRef>(subObj) || !em.HasComponent<Game.Objects.Transform>(subObj)) continue;
                bool hasSubBuffers = em.HasBuffer<Game.Net.SubNet>(subObj) || em.HasBuffer<Game.Areas.SubArea>(subObj);
                if (!hasSubBuffers) continue;

                Entity subPrefab = em.GetComponentData<PrefabRef>(subObj).m_Prefab;
                if (subPrefab == Entity.Null || !em.Exists(subPrefab)) continue;
                var subTr = em.GetComponentData<Game.Objects.Transform>(subObj);
                if (!IsFinite(subTr.m_Position) || !IsFinite(subTr.m_Rotation)) continue;

                int prefabSubNetCount = GetPrefabSubNetCount(em, subPrefab);
                List<UndoAreaRecord> subAreas = CaptureSubAreas(em, subObj, ref recordedWeight, ref truncated, ref notCaptured);
                List<UndoNetRecord> subNets = null;
                if (prefabSubNetCount > 0)
                {
                    recordedWeight += prefabSubNetCount;
                    if (recordedWeight > MaxEntitiesPerSnapshot) truncated = true;
                }
                else
                {
                    subNets = CaptureSubNets(em, subObj, ref recordedWeight, ref truncated, ref notCaptured);
                }
                if ((subAreas != null && subAreas.Count > 0) || prefabSubNetCount > 0 || (subNets != null && subNets.Count > 0))
                {
                    results ??= new List<UndoSubObjectSubs>();
                    results.Add(new UndoSubObjectSubs
                    {
                        OwnerDef = new OwnerDefinition { m_Prefab = subPrefab, m_Position = subTr.m_Position, m_Rotation = subTr.m_Rotation },
                        Seed = ReadSeed(em, subObj, subPrefab),
                        PrefabSubNetCount = prefabSubNetCount,
                        SubAreas = subAreas,
                        SubNets = subNets,
                    });
                }
            }

            return results;
        }

        private static List<UndoUpgradeRecord> CaptureInstalledUpgrades(
            EntityManager em,
            Entity owner,
            Game.Objects.Transform ownerTr,
            ref int recordedWeight, ref bool truncated, ref int notCaptured,
            out HashSet<Entity> upgradeSet)
        {
            upgradeSet = null;
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

                if (!em.HasComponent<PrefabRef>(upg))
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

                bool hasTransform = em.HasComponent<Game.Objects.Transform>(upg);
                bool isPhysical;
                float3 pos;
                quaternion rot;
                int parentMesh;
                bool hasOwnerSubBuffer = false;

                if (hasTransform)
                {
                    var tr = em.GetComponentData<Game.Objects.Transform>(upg);
                    if (!IsFinite(tr.m_Position) || !IsFinite(tr.m_Rotation))
                    {
                        notCaptured++;
                        continue;
                    }
                    isPhysical = em.HasComponent<GameObject>(upg);
                    pos = tr.m_Position;
                    rot = tr.m_Rotation;
                    parentMesh = ReadParentMesh(em, upg, em.HasComponent<Building>(upg) ? -1 : 0);
                    hasOwnerSubBuffer = em.HasBuffer<Game.Net.SubNet>(upg) ||
                                        em.HasBuffer<Game.Areas.SubArea>(upg) ||
                                        em.HasBuffer<Game.Objects.SubObject>(upg);
                }
                else if (em.HasComponent<Game.Buildings.ServiceUpgrade>(upg))
                {
                    isPhysical = false;
                    pos = ownerTr.m_Position;
                    rot = ownerTr.m_Rotation;
                    parentMesh = 0;
                    hasOwnerSubBuffer = false;
                }
                else
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
                upgradeSet ??= new HashSet<Entity>();
                upgradeSet.Add(upg);
                int upgPrefabSubNetCount = hasTransform ? GetPrefabSubNetCount(em, prefab) : 0;
                List<UndoAreaRecord> upgSubAreas = hasTransform ? CaptureSubAreas(em, upg, ref recordedWeight, ref truncated, ref notCaptured) : null;
                List<UndoNetRecord> upgSubNets = null;
                if (hasTransform)
                {
                    if (upgPrefabSubNetCount > 0)
                    {
                        recordedWeight += upgPrefabSubNetCount;
                        if (recordedWeight > MaxEntitiesPerSnapshot) truncated = true;
                    }
                    else
                    {
                        upgSubNets = CaptureSubNets(em, upg, ref recordedWeight, ref truncated, ref notCaptured);
                    }
                }
                list ??= new List<UndoUpgradeRecord>(buf.Length);
                list.Add(new UndoUpgradeRecord
                {
                    Prefab = prefab,
                    Position = pos,
                    Rotation = rot,
                    HasElevation = hasElev,
                    Elevation = hasElev ? elev.m_Elevation : 0f,
                    ElevationFlags = hasElev ? elev.m_Flags : 0,
                    Seed = ReadSeed(em, upg, prefab),
                    IsPhysical = isPhysical,
                    HasOwnerSubBuffer = hasOwnerSubBuffer,
                    ParentMesh = parentMesh,
                    PrefabSubNetCount = upgPrefabSubNetCount,
                    SubAreas = upgSubAreas,
                    SubNets = upgSubNets,
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
            HashSet<Entity> usedNodes = null;
            for (int i = 0; i < buf.Length; i++)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot) { truncated = true; break; }

                Entity subNet = buf[i].m_SubNet;
                if (subNet == Entity.Null || !em.Exists(subNet) || em.HasComponent<Deleted>(subNet) ||
                    !em.HasComponent<Edge>(subNet))
                {
                    continue;
                }

                Edge e = em.GetComponentData<Edge>(subNet);
                if (e.m_Start != Entity.Null) (usedNodes ??= new HashSet<Entity>()).Add(e.m_Start);
                if (e.m_End != Entity.Null) (usedNodes ??= new HashSet<Entity>()).Add(e.m_End);

                if (TryCaptureNetRecord(em, subNet, out UndoNetRecord netRec, owner))
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

            for (int i = 0; i < buf.Length; i++)
            {
                if (recordedWeight >= MaxEntitiesPerSnapshot) { truncated = true; break; }

                Entity subNet = buf[i].m_SubNet;
                if (subNet == Entity.Null || !em.Exists(subNet) || em.HasComponent<Deleted>(subNet) ||
                    em.HasComponent<Edge>(subNet) || !em.HasComponent<NetNode>(subNet))
                {
                    continue;
                }

                if (usedNodes != null && usedNodes.Contains(subNet))
                {
                    continue;
                }

                if (TryCaptureNodeOnlySubNet(em, subNet, owner, out UndoNetRecord nodeRec))
                {
                    recordedWeight++;
                    list ??= new List<UndoNetRecord>(buf.Length);
                    list.Add(nodeRec);
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
                AttachedInfo = CaptureAttachedRef(em, attachedParent),
                Seed = ReadSeed(em, entity, prefab),
            };
            return true;
        }

        private bool EmitNetRecord(
            EntityManager em,
            EntityCommandBuffer ecb,
            in UndoNetRecord rec,
            bool hasOwner,
            Entity ownerEntity,
            float liveNodeTolerance,
            out Entity submittedStart,
            out Entity submittedEnd)
        {
            submittedStart = Entity.Null;
            submittedEnd = Entity.Null;
            if (rec.Prefab == Entity.Null || !em.Exists(rec.Prefab))
            {
                return false;
            }

            if (rec.IsNodeOnly)
            {
                if (!IsFinite(rec.StartPos) || !IsFinite(rec.StartRot)) return false;
                CoursePosFlags nodeFlags = CoursePosFlags.IsFirst | CoursePosFlags.IsLast;
                if (!hasOwner && HasSignificantElevation(rec.StartElevation, rec.EdgeElevation)) nodeFlags |= CoursePosFlags.FreeHeight;

                var nodeCourse = new NetCourse
                {
                    m_StartPosition = new CoursePos
                    {
                        m_Entity = Entity.Null,
                        m_Position = rec.StartPos,
                        m_Rotation = rec.StartRot,
                        m_Elevation = rec.StartElevation,
                        m_CourseDelta = 0f,
                        m_SplitPosition = 0f,
                        m_Flags = nodeFlags,
                        m_ParentMesh = rec.StartParentMesh,
                    },
                    m_EndPosition = new CoursePos
                    {
                        m_Entity = Entity.Null,
                        m_Position = rec.StartPos,
                        m_Rotation = rec.StartRot,
                        m_Elevation = rec.StartElevation,
                        m_CourseDelta = 1f,
                        m_SplitPosition = 0f,
                        m_Flags = nodeFlags,
                        m_ParentMesh = rec.EndParentMesh,
                    },
                    m_Curve = new Bezier4x3(rec.StartPos, rec.StartPos, rec.StartPos, rec.StartPos),
                    m_Elevation = rec.EdgeElevation,
                    m_Length = 0f,
                    m_FixedIndex = -1,
                };

                Entity nodeCarrier = ecb.CreateEntity();
                ecb.AddComponent(nodeCarrier, new CreationDefinition
                {
                    m_Prefab = rec.Prefab,
                    m_Owner = hasOwner ? ownerEntity : Entity.Null,
                    m_Flags = CreationFlags.Permanent,
                    m_RandomSeed = rec.Seed,
                });
                ecb.AddComponent(nodeCarrier, nodeCourse);
                if (rec.HasUpgraded) ecb.AddComponent(nodeCarrier, new Upgraded { m_Flags = rec.UpgradedFlags });
                ecb.AddComponent<Updated>(nodeCarrier);
                ecb.AddComponent<Deleted>(nodeCarrier);
                return true;
            }

            Entity startEntity = Entity.Null;
            if (!hasOwner || !rec.StartNodeOwnedByParent)
            {
                if (rec.StartNode != Entity.Null && em.Exists(rec.StartNode) && !em.HasComponent<Deleted>(rec.StartNode))
                {
                    startEntity = rec.StartNode;
                }
                else if (TryFindLiveNetNodeNear(em, rec.StartPos, rec.EndPos, liveNodeTolerance, out Entity liveStart))
                {
                    startEntity = liveStart;
                }
            }

            Entity endEntity = Entity.Null;
            if (!hasOwner || !rec.EndNodeOwnedByParent)
            {
                if (rec.EndNode != Entity.Null && em.Exists(rec.EndNode) && !em.HasComponent<Deleted>(rec.EndNode))
                {
                    endEntity = rec.EndNode;
                }
                else if (TryFindLiveNetNodeNear(em, rec.EndPos, rec.StartPos, liveNodeTolerance, out Entity liveEnd))
                {
                    endEntity = liveEnd;
                }
            }

            if (startEntity != Entity.Null && startEntity == endEntity)
            {
                endEntity = Entity.Null;
            }

            float3 startPos = rec.StartPos;
            float3 endPos = rec.EndPos;
            Bezier4x3 curve = rec.Curve;
            float length = rec.Length;
            bool curveAdjusted = false;

            if (startEntity != Entity.Null && em.HasComponent<NetNode>(startEntity))
            {
                float3 livePos = em.GetComponentData<NetNode>(startEntity).m_Position;
                if (IsFinite(livePos) && math.distancesq(livePos, startPos) > 1e-6f)
                {
                    float3 delta = livePos - startPos;
                    startPos = livePos;
                    curve.a += delta;
                    curve.b += delta;
                    curveAdjusted = true;
                }
            }

            if (endEntity != Entity.Null && em.HasComponent<NetNode>(endEntity))
            {
                float3 livePos = em.GetComponentData<NetNode>(endEntity).m_Position;
                if (IsFinite(livePos) && math.distancesq(livePos, endPos) > 1e-6f)
                {
                    float3 delta = livePos - endPos;
                    endPos = livePos;
                    curve.d += delta;
                    curve.c += delta;
                    curveAdjusted = true;
                }
            }

            if (curveAdjusted)
            {
                float recomputed = MathUtils.Length(curve);
                if (math.isfinite(recomputed) && recomputed > 0.01f)
                {
                    length = recomputed;
                }
            }

            CoursePosFlags startFlags = CoursePosFlags.IsFirst;
            CoursePosFlags endFlags = CoursePosFlags.IsLast;
            if (!hasOwner)
            {
                if (HasSignificantElevation(rec.StartElevation, rec.EdgeElevation))
                {
                    startFlags |= CoursePosFlags.FreeHeight;
                }
                if (HasSignificantElevation(rec.EndElevation, rec.EdgeElevation))
                {
                    endFlags |= CoursePosFlags.FreeHeight;
                }
            }

            var course = new NetCourse
            {
                m_StartPosition = new CoursePos
                {
                    m_Entity = startEntity,
                    m_Position = startPos,
                    m_Rotation = rec.StartRot,
                    m_Elevation = rec.StartElevation,
                    m_CourseDelta = 0f,
                    m_SplitPosition = 0f,
                    m_Flags = startFlags,
                    m_ParentMesh = rec.IsNodeOnly ? rec.StartParentMesh : (hasOwner ? rec.StartParentMesh : -1),
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = endEntity,
                    m_Position = endPos,
                    m_Rotation = rec.EndRot,
                    m_Elevation = rec.EndElevation,
                    m_CourseDelta = 1f,
                    m_SplitPosition = 0f,
                    m_Flags = endFlags,
                    m_ParentMesh = rec.IsNodeOnly ? rec.EndParentMesh : (hasOwner ? rec.EndParentMesh : -1),
                },
                m_Curve = curve,
                m_Elevation = rec.EdgeElevation,
                m_Length = length,
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
            ecb.AddComponent<Updated>(carrier);
            ecb.AddComponent<Deleted>(carrier);
            submittedStart = startEntity;
            submittedEnd = endEntity;
            return true;
        }

        private bool TryFindLiveNetNodeNear(EntityManager em, float3 recordedPos, float3 oppositePos, float maxTolerance, out Entity found)
        {
            found = Entity.Null;
            int frame = UnityEngine.Time.frameCount;
            if (m_LiveNetNodeGridFrame != frame)
            {
                m_LiveNetNodeGrid.Clear();
                using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetNode>()))
                using (var nodes = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                {
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        Entity candidate = nodes[i];
                        if (candidate == Entity.Null || !em.Exists(candidate) ||
                            em.HasComponent<Deleted>(candidate) || em.HasComponent<Temp>(candidate) ||
                            em.HasComponent<Owner>(candidate)) continue;
                        float3 pos = em.GetComponentData<NetNode>(candidate).m_Position;
                        if (!IsFinite(pos)) continue;
                        long key = SpatialCellKey(SpatialCellCoord(pos.x), SpatialCellCoord(pos.z));
                        if (!m_LiveNetNodeGrid.TryGetValue(key, out var bucket))
                        {
                            bucket = new List<LivePosEntry>(4);
                            m_LiveNetNodeGrid[key] = bucket;
                        }
                        bucket.Add(new LivePosEntry { Entity = candidate, Position = pos });
                    }
                }
                m_LiveNetNodeGridFrame = frame;
            }

            float strictSq = NetConnectivityPolicy.EndpointTolerance * NetConnectivityPolicy.EndpointTolerance;
            float wideTol = maxTolerance > NetConnectivityPolicy.EndpointTolerance ? maxTolerance : NetConnectivityPolicy.EndpointTolerance;
            float wideSq = wideTol * wideTol;
            float bestStrict = strictSq;
            Entity foundStrict = Entity.Null;
            float bestWide = wideSq;
            Entity foundWide = Entity.Null;

            int baseCx = SpatialCellCoord(recordedPos.x);
            int baseCz = SpatialCellCoord(recordedPos.z);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!m_LiveNetNodeGrid.TryGetValue(SpatialCellKey(baseCx + dx, baseCz + dz), out var bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        LivePosEntry entry = bucket[i];
                        float distSq = math.distancesq(entry.Position, recordedPos);
                        if (distSq <= bestStrict)
                        {
                            bestStrict = distSq;
                            foundStrict = entry.Entity;
                        }
                        else if (distSq <= bestWide && distSq < math.distancesq(entry.Position, oppositePos))
                        {
                            bestWide = distSq;
                            foundWide = entry.Entity;
                        }
                    }
                }
            }

            found = foundStrict != Entity.Null ? foundStrict : foundWide;
            return found != Entity.Null;
        }

        private Entity ResolveAttachedParent(EntityManager em, in UndoAttachedRef info, Entity legacyParent)
        {
            if (legacyParent != Entity.Null && em.Exists(legacyParent) && !em.HasComponent<Deleted>(legacyParent))
            {
                return legacyParent;
            }
            if (info.OriginalEntity == Entity.Null || info.Prefab == Entity.Null || !em.Exists(info.Prefab))
            {
                return Entity.Null;
            }

            int frame = UnityEngine.Time.frameCount;
            Dictionary<long, List<LivePrefabPosEntry>> grid;
            if (info.IsNetEdge)
            {
                if (m_LiveAttachedEdgeGridFrame != frame)
                {
                    m_LiveAttachedEdgeGrid.Clear();
                    using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>()))
                    using (var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                    {
                        for (int i = 0; i < entities.Length; i++)
                        {
                            Entity c = entities[i];
                            if (!em.Exists(c) || em.HasComponent<Deleted>(c) || em.HasComponent<Temp>(c)) continue;
                            Bezier4x3 b = em.GetComponentData<Curve>(c).m_Bezier;
                            float3 mid = 0.125f * b.a + 0.375f * b.b + 0.375f * b.c + 0.125f * b.d;
                            if (!IsFinite(mid)) continue;
                            long key = SpatialCellKey(SpatialCellCoord(mid.x), SpatialCellCoord(mid.z));
                            if (!m_LiveAttachedEdgeGrid.TryGetValue(key, out var bucket))
                            {
                                bucket = new List<LivePrefabPosEntry>(4);
                                m_LiveAttachedEdgeGrid[key] = bucket;
                            }
                            bucket.Add(new LivePrefabPosEntry
                            {
                                Entity = c,
                                Prefab = em.GetComponentData<PrefabRef>(c).m_Prefab,
                                Position = mid,
                            });
                        }
                    }
                    m_LiveAttachedEdgeGridFrame = frame;
                }
                grid = m_LiveAttachedEdgeGrid;
            }
            else if (info.IsNetNode)
            {
                if (m_LiveAttachedNodeGridFrame != frame)
                {
                    m_LiveAttachedNodeGrid.Clear();
                    using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetNode>(), ComponentType.ReadOnly<PrefabRef>()))
                    using (var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                    {
                        for (int i = 0; i < entities.Length; i++)
                        {
                            Entity c = entities[i];
                            if (!em.Exists(c) || em.HasComponent<Deleted>(c) || em.HasComponent<Temp>(c)) continue;
                            float3 pos = em.GetComponentData<NetNode>(c).m_Position;
                            if (!IsFinite(pos)) continue;
                            long key = SpatialCellKey(SpatialCellCoord(pos.x), SpatialCellCoord(pos.z));
                            if (!m_LiveAttachedNodeGrid.TryGetValue(key, out var bucket))
                            {
                                bucket = new List<LivePrefabPosEntry>(4);
                                m_LiveAttachedNodeGrid[key] = bucket;
                            }
                            bucket.Add(new LivePrefabPosEntry
                            {
                                Entity = c,
                                Prefab = em.GetComponentData<PrefabRef>(c).m_Prefab,
                                Position = pos,
                            });
                        }
                    }
                    m_LiveAttachedNodeGridFrame = frame;
                }
                grid = m_LiveAttachedNodeGrid;
            }
            else
            {
                if (m_LiveAttachedObjGridFrame != frame)
                {
                    m_LiveAttachedObjGrid.Clear();
                    using (var objQuery = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>()))
                    using (var objects = objQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
                    {
                        for (int i = 0; i < objects.Length; i++)
                        {
                            Entity c = objects[i];
                            if (!em.Exists(c) || em.HasComponent<Deleted>(c) || em.HasComponent<Temp>(c)) continue;
                            float3 pos = em.GetComponentData<Game.Objects.Transform>(c).m_Position;
                            if (!IsFinite(pos)) continue;
                            long key = SpatialCellKey(SpatialCellCoord(pos.x), SpatialCellCoord(pos.z));
                            if (!m_LiveAttachedObjGrid.TryGetValue(key, out var bucket))
                            {
                                bucket = new List<LivePrefabPosEntry>(4);
                                m_LiveAttachedObjGrid[key] = bucket;
                            }
                            bucket.Add(new LivePrefabPosEntry
                            {
                                Entity = c,
                                Prefab = em.GetComponentData<PrefabRef>(c).m_Prefab,
                                Position = pos,
                            });
                        }
                    }
                    m_LiveAttachedObjGridFrame = frame;
                }
                grid = m_LiveAttachedObjGrid;
            }

            float maxDistSq = (info.IsNetEdge || info.IsNetNode) ? 16.0f : 1.0f;
            Entity bestEntity = Entity.Null;
            float bestDistSq = maxDistSq;
            int baseCx = SpatialCellCoord(info.Position.x);
            int baseCz = SpatialCellCoord(info.Position.z);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue(SpatialCellKey(baseCx + dx, baseCz + dz), out var bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        LivePrefabPosEntry entry = bucket[i];
                        if (entry.Prefab != info.Prefab) continue;
                        float dSq = math.distancesq(entry.Position, info.Position);
                        if (dSq <= bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestEntity = entry.Entity;
                        }
                    }
                }
            }
            return bestEntity;
        }

        private static bool HasSignificantElevation(float2 a, float2 b)
        {
            const float threshold = 0.05f;
            return math.any(math.abs(a) > threshold) || math.any(math.abs(b) > threshold);
        }

        private bool EmitSubNetWithOwnerDef(
            EntityManager em,
            EntityCommandBuffer ecb,
            in UndoNetRecord rec,
            in OwnerDefinition ownerDef)
        {
            if (rec.Prefab == Entity.Null || !em.Exists(rec.Prefab) || !em.HasComponent<Game.Prefabs.NetData>(rec.Prefab))
            {
                return false;
            }

            if (rec.IsNodeOnly)
            {
                if (!IsFinite(rec.StartPos) || !IsFinite(rec.StartRot)) return false;
                CoursePosFlags flags = CoursePosFlags.IsFirst | CoursePosFlags.IsLast;

                var nodeCourse = new NetCourse
                {
                    m_StartPosition = new CoursePos
                    {
                        m_Entity = Entity.Null,
                        m_Position = rec.StartPos,
                        m_Rotation = rec.StartRot,
                        m_Elevation = rec.StartElevation,
                        m_CourseDelta = 0f,
                        m_SplitPosition = 0f,
                        m_Flags = flags,
                        m_ParentMesh = rec.StartParentMesh,
                    },
                    m_EndPosition = new CoursePos
                    {
                        m_Entity = Entity.Null,
                        m_Position = rec.StartPos,
                        m_Rotation = rec.StartRot,
                        m_Elevation = rec.StartElevation,
                        m_CourseDelta = 1f,
                        m_SplitPosition = 0f,
                        m_Flags = flags,
                        m_ParentMesh = rec.EndParentMesh,
                    },
                    m_Curve = new Bezier4x3(rec.StartPos, rec.StartPos, rec.StartPos, rec.StartPos),
                    m_Elevation = rec.EdgeElevation,
                    m_Length = 0f,
                    m_FixedIndex = -1,
                };

                Entity nodeCarrier = ecb.CreateEntity();
                ecb.AddComponent(nodeCarrier, new CreationDefinition
                {
                    m_Prefab = rec.Prefab,
                    m_Owner = Entity.Null,
                    m_Flags = CreationFlags.Permanent,
                    m_RandomSeed = rec.Seed,
                });
                ecb.AddComponent(nodeCarrier, ownerDef);
                ecb.AddComponent(nodeCarrier, nodeCourse);
                if (rec.HasUpgraded) ecb.AddComponent(nodeCarrier, new Upgraded { m_Flags = rec.UpgradedFlags });
                ecb.AddComponent<Updated>(nodeCarrier);
                ecb.AddComponent<Deleted>(nodeCarrier);
                return true;
            }

            if (!IsBezierFinite(rec.Curve)) return false;

            Entity startEntity = Entity.Null;
            if (!rec.StartNodeOwnedByParent)
            {
                if (rec.StartNode != Entity.Null && em.Exists(rec.StartNode) && !em.HasComponent<Deleted>(rec.StartNode) &&
                    PositionAllowsRebuild(em, rec.StartNode, rec.StartPos, UninterruptedNodeDriftTolerance, out _))
                {
                    startEntity = rec.StartNode;
                }
                else if (TryFindLiveNetNodeNear(em, rec.StartPos, rec.EndPos, 2.0f, out Entity liveStart))
                {
                    startEntity = liveStart;
                }
            }

            Entity endEntity = Entity.Null;
            if (!rec.EndNodeOwnedByParent)
            {
                if (rec.EndNode != Entity.Null && em.Exists(rec.EndNode) && !em.HasComponent<Deleted>(rec.EndNode) &&
                    PositionAllowsRebuild(em, rec.EndNode, rec.EndPos, UninterruptedNodeDriftTolerance, out _))
                {
                    endEntity = rec.EndNode;
                }
                else if (TryFindLiveNetNodeNear(em, rec.EndPos, rec.StartPos, 2.0f, out Entity liveEnd))
                {
                    endEntity = liveEnd;
                }
            }

            CoursePosFlags startFlags = CoursePosFlags.IsFirst;
            CoursePosFlags endFlags = CoursePosFlags.IsLast;

            float3 startPos = rec.StartPos;
            float3 endPos = rec.EndPos;
            Bezier4x3 curve = rec.Curve;
            float length = rec.Length;
            bool curveAdjusted = false;

            if (startEntity != Entity.Null && em.HasComponent<NetNode>(startEntity))
            {
                float3 livePos = em.GetComponentData<NetNode>(startEntity).m_Position;
                if (IsFinite(livePos) && math.distancesq(livePos, startPos) > 1e-6f)
                {
                    float3 delta = livePos - startPos;
                    startPos = livePos;
                    curve.a += delta;
                    curve.b += delta;
                    curveAdjusted = true;
                }
            }

            if (endEntity != Entity.Null && em.HasComponent<NetNode>(endEntity))
            {
                float3 livePos = em.GetComponentData<NetNode>(endEntity).m_Position;
                if (IsFinite(livePos) && math.distancesq(livePos, endPos) > 1e-6f)
                {
                    float3 delta = livePos - endPos;
                    endPos = livePos;
                    curve.d += delta;
                    curve.c += delta;
                    curveAdjusted = true;
                }
            }

            if (curveAdjusted)
            {
                float recomputed = MathUtils.Length(curve);
                if (math.isfinite(recomputed) && recomputed > 0.01f)
                {
                    length = recomputed;
                }
            }

            var course = new NetCourse
            {
                m_StartPosition = new CoursePos
                {
                    m_Entity = startEntity,
                    m_Position = startPos,
                    m_Rotation = rec.StartRot,
                    m_Elevation = rec.StartElevation,
                    m_CourseDelta = 0f,
                    m_SplitPosition = 0f,
                    m_Flags = startFlags,
                    m_ParentMesh = rec.StartParentMesh,
                },
                m_EndPosition = new CoursePos
                {
                    m_Entity = endEntity,
                    m_Position = endPos,
                    m_Rotation = rec.EndRot,
                    m_Elevation = rec.EndElevation,
                    m_CourseDelta = 1f,
                    m_SplitPosition = 0f,
                    m_Flags = endFlags,
                    m_ParentMesh = rec.EndParentMesh,
                },
                m_Curve = curve,
                m_Elevation = rec.EdgeElevation,
                m_Length = length,
                m_FixedIndex = -1,
            };

            Entity carrier = ecb.CreateEntity();
            ecb.AddComponent(carrier, new CreationDefinition
            {
                m_Prefab = rec.Prefab,
                m_Owner = Entity.Null,
                m_Flags = CreationFlags.Permanent,
                m_RandomSeed = rec.Seed,
            });
            ecb.AddComponent(carrier, ownerDef);
            ecb.AddComponent(carrier, course);
            if (rec.HasUpgraded) ecb.AddComponent(carrier, new Upgraded { m_Flags = rec.UpgradedFlags });
            ecb.AddComponent<Updated>(carrier);
            ecb.AddComponent<Deleted>(carrier);
            return true;
        }

        private static bool EmitAreaRecordWithOwnerDef(
            EntityManager em,
            EntityCommandBuffer ecb,
            in UndoAreaRecord rec,
            in OwnerDefinition ownerDef)
        {
            if (!ValidPrefab<Game.Prefabs.AreaData>(em, rec.Prefab) || rec.Nodes == null || rec.Nodes.Length < 3)
            {
                return false;
            }

            Entity carrier = ecb.CreateEntity();
            ecb.AddComponent(carrier, new CreationDefinition
            {
                m_Prefab = rec.Prefab,
                m_Owner = Entity.Null,
                m_Flags = CreationFlags.Permanent,
                m_RandomSeed = rec.Seed,
            });
            ecb.AddComponent(carrier, ownerDef);
            DynamicBuffer<Game.Areas.Node> nodes = ecb.AddBuffer<Game.Areas.Node>(carrier);
            for (int i = 0; i < rec.Nodes.Length; i++)
            {
                nodes.Add(rec.Nodes[i]);
            }
            ecb.AddComponent<Updated>(carrier);
            ecb.AddComponent<Deleted>(carrier);
            return true;
        }

        private static bool EmitUpgradeWithOwnerDef(
            EntityManager em,
            EntityCommandBuffer ecb,
            in UndoUpgradeRecord upg,
            in UndoBuildingRecord parentBld,
            in OwnerDefinition ownerDef)
        {
            if (!ValidObject(em, upg.Prefab, upg.Position, upg.Rotation))
            {
                return false;
            }

            CreationFlags upgFlags = CreationFlags.Permanent | CreationFlags.Upgrade;
            if (upg.HasElevation && (upg.ElevationFlags & ElevationFlags.Lowered) != 0)
            {
                upgFlags |= CreationFlags.Lowered;
            }

            quaternion inverse = math.inverse(parentBld.Rotation);
            Entity upgCarrier = ecb.CreateEntity();
            ecb.AddComponent(upgCarrier, new CreationDefinition
            {
                m_Prefab = upg.Prefab,
                m_Owner = Entity.Null,
                m_Flags = upgFlags,
                m_RandomSeed = upg.Seed,
            });
            ecb.AddComponent(upgCarrier, ownerDef);
            ecb.AddComponent(upgCarrier, new ObjectDefinition
            {
                m_Position = upg.Position,
                m_Rotation = upg.Rotation,
                m_LocalPosition = math.rotate(inverse, upg.Position - parentBld.Position),
                m_LocalRotation = math.mul(inverse, upg.Rotation),
                m_Scale = new float3(1f, 1f, 1f),
                m_Elevation = upg.HasElevation ? upg.Elevation : 0f,
                m_Intensity = 1f,
                m_ParentMesh = upg.ParentMesh,
                m_Probability = 100,
                m_PrefabSubIndex = -1,
            });
            ecb.AddComponent<Updated>(upgCarrier);
            ecb.AddComponent<Deleted>(upgCarrier);
            return true;
        }

        private static int CountInlineBuildingUnitItems(in UndoBuildingRecord building)
        {
            int count = 1;
            if (building.SubAreas != null) count += building.SubAreas.Count;
            count += building.PrefabSubNetCount > 0 ? building.PrefabSubNetCount : (building.SubNets?.Count ?? 0);
            if (building.Upgrades != null && building.HasOwnerSubBuffer)
            {
                for (int i = 0; i < building.Upgrades.Count; i++)
                {
                    UndoUpgradeRecord upg = building.Upgrades[i];
                    if (!upg.IsPhysical) continue;
                    count++;
                    if (upg.SubAreas != null) count += upg.SubAreas.Count;
                    count += upg.PrefabSubNetCount > 0 ? upg.PrefabSubNetCount : (upg.SubNets?.Count ?? 0);
                }
            }
            return count;
        }

        private static int CountSubObjectUnitItems(in UndoSubObjectSubs sub)
        {
            int count = sub.SubAreas != null ? sub.SubAreas.Count : 0;
            count += sub.PrefabSubNetCount > 0 ? sub.PrefabSubNetCount : (sub.SubNets?.Count ?? 0);
            return count;
        }

        private static void EmitPrefabSubNets(
            EntityManager em,
            EntityCommandBuffer ecb,
            Entity ownerPrefab,
            Game.Objects.Transform ownerTransform,
            in OwnerDefinition ownerDef,
            Entity liveOwnerEntity,
            int baseSeed,
            int expectedCount,
            ref PrefabSubNetEnv env,
            out int emittedCount,
            out int skippedCount)
        {
            emittedCount = 0;
            skippedCount = 0;
            if (ownerPrefab == Entity.Null || !em.Exists(ownerPrefab) || !em.HasBuffer<Game.Prefabs.SubNet>(ownerPrefab) ||
                !IsFinite(ownerTransform.m_Position) || !IsFinite(ownerTransform.m_Rotation))
            {
                skippedCount = expectedCount;
                return;
            }

            DynamicBuffer<Game.Prefabs.SubNet> prefabSubNets = em.GetBuffer<Game.Prefabs.SubNet>(ownerPrefab, true);
            if (prefabSubNets.Length == 0)
            {
                skippedCount = expectedCount;
                return;
            }

            if (!env.TerrainAttempted)
            {
                env.TerrainAttempted = true;
                if (env.TerrainSystem != null)
                {
                    env.TerrainData = env.TerrainSystem.GetHeightData(waitForPending: true);
                    env.TerrainAcquired = true;
                    env.HasTerrain = env.TerrainData.isCreated;
                }
            }

            // Pass 1: average local endpoint positions per shared m_NodeIndex (matching ObjectToolBaseSystem.UpdateSubNets).
            var nodePositions = new List<float4>(prefabSubNets.Length * 2);
            for (int i = 0; i < prefabSubNets.Length; i++)
            {
                Game.Prefabs.SubNet rawSn = prefabSubNets[i];
                if (rawSn.m_NodeIndex.x >= 0)
                {
                    while (nodePositions.Count <= rawSn.m_NodeIndex.x)
                    {
                        nodePositions.Add(float4.zero);
                    }
                    nodePositions[rawSn.m_NodeIndex.x] += new float4(rawSn.m_Curve.a, 1f);
                }
                if (rawSn.m_NodeIndex.y >= 0)
                {
                    while (nodePositions.Count <= rawSn.m_NodeIndex.y)
                    {
                        nodePositions.Add(float4.zero);
                    }
                    nodePositions[rawSn.m_NodeIndex.y] += new float4(rawSn.m_Curve.d, 1f);
                }
            }
            for (int k = 0; k < nodePositions.Count; k++)
            {
                nodePositions[k] /= math.max(1f, nodePositions[k].w);
            }

            // Pass 2: replicate NetUtils.GetSubNet + CreateSubNet per prefab SubNet entry.
            for (int i = 0; i < prefabSubNets.Length; i++)
            {
                Game.Prefabs.SubNet sn = prefabSubNets[i];
                if (!ValidPrefab<Game.Prefabs.NetData>(em, sn.m_Prefab))
                {
                    skippedCount++;
                    continue;
                }

                bool hasNetGeo = em.HasComponent<NetGeometryData>(sn.m_Prefab);
                NetGeometryData netGeo = hasNetGeo ? em.GetComponentData<NetGeometryData>(sn.m_Prefab) : default;

                if (NetUtils.ShouldInvert(sn.m_InvertMode, env.IsLeftHandTraffic))
                {
                    if (hasNetGeo && (netGeo.m_Flags & Game.Net.GeometryFlags.FlipTrafficHandedness) != 0)
                    {
                        sn.m_Curve = MathUtils.Invert(sn.m_Curve);
                        sn.m_NodeIndex = sn.m_NodeIndex.yx;
                        sn.m_ParentMesh = sn.m_ParentMesh.yx;
                        NetUtils.FixInvertedUpgradeTrafficHandedness(ref sn.m_Upgrades);
                    }
                    else
                    {
                        NetUtils.FlipUpgradeTrafficHandedness(ref sn.m_Upgrades);
                    }
                }

                Bezier4x3 localCurve = sn.m_Curve;
                if (!IsBezierFinite(localCurve))
                {
                    skippedCount++;
                    continue;
                }

                bool onWater = hasNetGeo && (netGeo.m_Flags & Game.Net.GeometryFlags.OnWater) != 0;
                bool flattenTerrain = hasNetGeo && (netGeo.m_Flags & Game.Net.GeometryFlags.FlattenTerrain) != 0;
                bool bothFixed = sn.m_ParentMesh.x >= 0 && sn.m_ParentMesh.y >= 0;

                Bezier4x3 worldCurve;
                if (onWater)
                {
                    if (!env.WaterAttempted)
                    {
                        env.WaterAttempted = true;
                        if (env.WaterSystem != null)
                        {
                            env.WaterData = env.WaterSystem.GetSurfaceData(out JobHandle waterDeps);
                            waterDeps.Complete();
                            env.WaterAcquired = true;
                            env.HasWater = env.WaterData.isCreated;
                        }
                    }

                    localCurve.y = default(Bezier4x1);
                    worldCurve = ObjectUtils.LocalToWorld(ownerTransform.m_Position, ownerTransform.m_Rotation, localCurve);
                    if (env.HasTerrain && env.HasWater)
                    {
                        worldCurve = NetUtils.AdjustPosition(
                            new Game.Net.Curve { m_Bezier = worldCurve },
                            false, false, false,
                            ref env.TerrainData, ref env.WaterData).m_Bezier;
                    }
                }
                else if (!bothFixed)
                {
                    worldCurve = ObjectUtils.LocalToWorld(ownerTransform.m_Position, ownerTransform.m_Rotation, localCurve);
                    if (!flattenTerrain && env.HasTerrain)
                    {
                        bool startFixed = sn.m_ParentMesh.x >= 0;
                        bool endFixed = sn.m_ParentMesh.y >= 0;
                        worldCurve = NetUtils.AdjustPosition(
                            new Game.Net.Curve { m_Bezier = worldCurve },
                            startFixed, startFixed || endFixed, endFixed,
                            ref env.TerrainData).m_Bezier;
                        worldCurve.a.y += localCurve.a.y;
                        worldCurve.b.y += localCurve.b.y;
                        worldCurve.c.y += localCurve.c.y;
                        worldCurve.d.y += localCurve.d.y;
                    }
                }
                else
                {
                    worldCurve = ObjectUtils.LocalToWorld(ownerTransform.m_Position, ownerTransform.m_Rotation, localCurve);
                }

                float3 startPos = worldCurve.a;
                if (sn.m_NodeIndex.x >= 0 && sn.m_NodeIndex.x < nodePositions.Count)
                {
                    float3 avgWorld = ObjectUtils.LocalToWorld(ownerTransform, nodePositions[sn.m_NodeIndex.x].xyz);
                    if (onWater && env.HasTerrain && env.HasWater)
                    {
                        startPos.xz = avgWorld.xz;
                        startPos.y = WaterUtils.SampleHeight(ref env.WaterData, ref env.TerrainData, startPos);
                    }
                    else
                    {
                        startPos = avgWorld;
                    }
                }

                float3 endPos = worldCurve.d;
                if (sn.m_NodeIndex.y >= 0 && sn.m_NodeIndex.y < nodePositions.Count)
                {
                    float3 avgWorld = ObjectUtils.LocalToWorld(ownerTransform, nodePositions[sn.m_NodeIndex.y].xyz);
                    if (onWater && env.HasTerrain && env.HasWater)
                    {
                        endPos.xz = avgWorld.xz;
                        endPos.y = WaterUtils.SampleHeight(ref env.WaterData, ref env.TerrainData, endPos);
                    }
                    else
                    {
                        endPos = avgWorld;
                    }
                }

                CoursePosFlags startFlags = CoursePosFlags.IsFirst;
                CoursePosFlags endFlags = CoursePosFlags.IsLast;
                if (startPos.Equals(endPos))
                {
                    startFlags |= CoursePosFlags.IsLast;
                    endFlags |= CoursePosFlags.IsFirst;
                }

                int seed = unchecked((baseSeed * 397) ^ (i + 1) ^ sn.m_Prefab.Index);
                if (seed == 0) seed = i + 1;

                Entity carrier = ecb.CreateEntity();
                ecb.AddComponent(carrier, new CreationDefinition
                {
                    m_Prefab = sn.m_Prefab,
                    m_Owner = liveOwnerEntity,
                    m_Flags = CreationFlags.Permanent,
                    m_RandomSeed = seed,
                });
                if (liveOwnerEntity == Entity.Null)
                {
                    ecb.AddComponent(carrier, ownerDef);
                }
                ecb.AddComponent(carrier, new NetCourse
                {
                    m_StartPosition = new CoursePos
                    {
                        m_Entity = Entity.Null,
                        m_Position = startPos,
                        m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(worldCurve), ownerTransform.m_Rotation),
                        m_Elevation = new float2(localCurve.a.y),
                        m_CourseDelta = 0f,
                        m_SplitPosition = 0f,
                        m_Flags = startFlags,
                        m_ParentMesh = sn.m_ParentMesh.x,
                    },
                    m_EndPosition = new CoursePos
                    {
                        m_Entity = Entity.Null,
                        m_Position = endPos,
                        m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(worldCurve), ownerTransform.m_Rotation),
                        m_Elevation = new float2(localCurve.d.y),
                        m_CourseDelta = 1f,
                        m_SplitPosition = 0f,
                        m_Flags = endFlags,
                        m_ParentMesh = sn.m_ParentMesh.y,
                    },
                    m_Curve = worldCurve,
                    m_Elevation = float2.zero,
                    m_Length = MathUtils.Length(worldCurve),
                    m_FixedIndex = -1,
                });
                if (sn.m_Upgrades != default(CompositionFlags))
                {
                    ecb.AddComponent(carrier, new Upgraded { m_Flags = sn.m_Upgrades });
                }
                ecb.AddComponent<Updated>(carrier);
                ecb.AddComponent<Deleted>(carrier);
                emittedCount++;
            }

            if (emittedCount + skippedCount < expectedCount)
            {
                skippedCount += expectedCount - (emittedCount + skippedCount);
            }
        }

        private bool EmitSubObjectUnit(
            EntityManager em,
            EntityCommandBuffer ecb,
            UndoSnapshot snap,
            in UndoSubObjectSubs sub,
            Entity liveOwner,
            ref PrefabSubNetEnv env,
            out bool emittedSubNets)
        {
            emittedSubNets = false;
            int restored = 0;
            int skipped = 0;

            if (sub.SubAreas != null)
            {
                for (int i = 0; i < sub.SubAreas.Count; i++)
                {
                    UndoAreaRecord area = sub.SubAreas[i];
                    if (ValidPrefab<Game.Prefabs.AreaData>(em, area.Prefab) && area.Nodes != null && area.Nodes.Length >= 3)
                    {
                        EmitAreaRecord(em, ecb, area, true, liveOwner);
                        restored++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }

            if (sub.PrefabSubNetCount > 0)
            {
                int seed = sub.Seed != 0 ? sub.Seed : 1;
                Game.Objects.Transform ownerTr = new Game.Objects.Transform
                {
                    m_Position = sub.OwnerDef.m_Position,
                    m_Rotation = sub.OwnerDef.m_Rotation,
                };
                if (liveOwner != Entity.Null && em.Exists(liveOwner) && em.HasComponent<Game.Objects.Transform>(liveOwner))
                {
                    Game.Objects.Transform liveTr = em.GetComponentData<Game.Objects.Transform>(liveOwner);
                    if (IsFinite(liveTr.m_Position) && IsFinite(liveTr.m_Rotation))
                    {
                        ownerTr = liveTr;
                    }
                }
                EmitPrefabSubNets(
                    em,
                    ecb,
                    sub.OwnerDef.m_Prefab,
                    ownerTr,
                    default(OwnerDefinition),
                    liveOwner,
                    seed,
                    sub.PrefabSubNetCount,
                    ref env,
                    out int subRestored,
                    out int subSkipped);
                restored += subRestored;
                skipped += subSkipped;
                if (subRestored > 0) emittedSubNets = true;
            }
            else if (sub.SubNets != null)
            {
                for (int i = 0; i < sub.SubNets.Count; i++)
                {
                    UndoNetRecord net = sub.SubNets[i];
                    if (ValidPrefab<Game.Prefabs.NetData>(em, net.Prefab) &&
                        (net.IsNodeOnly || IsBezierFinite(net.Curve)) &&
                        EmitNetRecord(em, ecb, net, true, liveOwner, 2.0f, out _, out _))
                    {
                        restored++;
                        emittedSubNets = true;
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }

            snap.RestoredCount += restored;
            snap.SkippedCount += skipped;
            return restored > 0;
        }

        private bool EmitBuildingUnit(
            EntityManager em,
            EntityCommandBuffer ecb,
            UndoSnapshot snap,
            in UndoBuildingRecord building,
            ref PrefabSubNetEnv env,
            out bool emittedSubNets)
        {
            emittedSubNets = false;
            int unitTotal = CountInlineBuildingUnitItems(building);
            bool rootValid = ValidObject(em, building.Prefab, building.Position, building.Rotation) &&
                             !OccupiedBeforeRestore(building.Prefab, building.Position);
            if (!rootValid)
            {
                snap.SkippedCount += unitTotal;
                m_FailedOwners.Add(new OwnerDefinition { m_Prefab = building.Prefab, m_Position = building.Position, m_Rotation = building.Rotation });
                return false;
            }

            EmitBuildingRoot(em, ecb, building);
            int restored = 1;
            int skipped = 0;

            var bldOwnerDef = new OwnerDefinition { m_Prefab = building.Prefab, m_Position = building.Position, m_Rotation = building.Rotation };
            if (building.SubAreas != null)
            {
                for (int i = 0; i < building.SubAreas.Count; i++)
                {
                    if (EmitAreaRecordWithOwnerDef(em, ecb, building.SubAreas[i], bldOwnerDef)) restored++;
                    else skipped++;
                }
            }
            if (building.PrefabSubNetCount > 0)
            {
                var bldTr = new Game.Objects.Transform { m_Position = building.Position, m_Rotation = building.Rotation };
                EmitPrefabSubNets(
                    em,
                    ecb,
                    building.Prefab,
                    bldTr,
                    bldOwnerDef,
                    Entity.Null,
                    building.Seed,
                    building.PrefabSubNetCount,
                    ref env,
                    out int subRestored,
                    out int subSkipped);
                restored += subRestored;
                skipped += subSkipped;
                if (subRestored > 0) emittedSubNets = true;
            }
            else if (building.SubNets != null)
            {
                for (int i = 0; i < building.SubNets.Count; i++)
                {
                    if (EmitSubNetWithOwnerDef(em, ecb, building.SubNets[i], bldOwnerDef))
                    {
                        restored++;
                        emittedSubNets = true;
                    }
                    else skipped++;
                }
            }
            if (building.Upgrades != null && building.HasOwnerSubBuffer)
            {
                for (int u = 0; u < building.Upgrades.Count; u++)
                {
                    UndoUpgradeRecord upg = building.Upgrades[u];
                    if (!upg.IsPhysical) continue;
                    if (!EmitUpgradeWithOwnerDef(em, ecb, upg, building, bldOwnerDef))
                    {
                        skipped += 1 + (upg.SubAreas != null ? upg.SubAreas.Count : 0) +
                                   (upg.PrefabSubNetCount > 0 ? upg.PrefabSubNetCount : (upg.SubNets != null ? upg.SubNets.Count : 0));
                        continue;
                    }

                    restored++;
                    var upgOwnerDef = new OwnerDefinition { m_Prefab = upg.Prefab, m_Position = upg.Position, m_Rotation = upg.Rotation };
                    if (upg.SubAreas != null)
                    {
                        for (int i = 0; i < upg.SubAreas.Count; i++)
                        {
                            if (EmitAreaRecordWithOwnerDef(em, ecb, upg.SubAreas[i], upgOwnerDef)) restored++;
                            else skipped++;
                        }
                    }
                    if (upg.PrefabSubNetCount > 0)
                    {
                        var upgTr = new Game.Objects.Transform { m_Position = upg.Position, m_Rotation = upg.Rotation };
                        EmitPrefabSubNets(
                            em,
                            ecb,
                            upg.Prefab,
                            upgTr,
                            upgOwnerDef,
                            Entity.Null,
                            upg.Seed,
                            upg.PrefabSubNetCount,
                            ref env,
                            out int upgRestored,
                            out int upgSkipped);
                        restored += upgRestored;
                        skipped += upgSkipped;
                        if (upgRestored > 0) emittedSubNets = true;
                    }
                    else if (upg.SubNets != null)
                    {
                        for (int i = 0; i < upg.SubNets.Count; i++)
                        {
                            if (EmitSubNetWithOwnerDef(em, ecb, upg.SubNets[i], upgOwnerDef))
                            {
                                restored++;
                                emittedSubNets = true;
                            }
                            else skipped++;
                        }
                    }
                }
            }

            snap.RestoredCount += restored;
            snap.SkippedCount += skipped;
            return true;
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

        private void EmitBuildingRoot(EntityManager em, EntityCommandBuffer ecb, in UndoBuildingRecord rec)
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

            Entity attached = ResolveAttachedParent(em, rec.AttachedInfo, rec.AttachedParent);
            if (attached != Entity.Null)
            {
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
            float3 pos = upg.IsPhysical ? upg.Position : parent.m_Position;
            quaternion rot = upg.IsPhysical ? upg.Rotation : parent.m_Rotation;
            float3 localPos;
            quaternion localRot;
            if (upg.IsPhysical)
            {
                quaternion inverse = math.inverse(parent.m_Rotation);
                localPos = math.rotate(inverse, upg.Position - parent.m_Position);
                localRot = math.mul(inverse, upg.Rotation);
            }
            else
            {
                localPos = float3.zero;
                localRot = quaternion.identity;
            }

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
                m_Position = pos,
                m_Rotation = rot,
                m_LocalPosition = localPos,
                m_LocalRotation = localRot,
                m_Scale = new float3(1f, 1f, 1f),
                m_Elevation = upg.HasElevation ? upg.Elevation : 0f,
                m_Intensity = 1f,
                m_ParentMesh = upg.IsPhysical ? upg.ParentMesh : 0,
                m_Probability = 100,
                m_PrefabSubIndex = -1,
            });
            ecb.AddComponent<Updated>(upgCarrier);
            ecb.AddComponent<Deleted>(upgCarrier);

        }

        private void EmitObjectRecord(EntityManager em, EntityCommandBuffer ecb, in UndoObjectRecord rec)
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

            Entity attached = ResolveAttachedParent(em, rec.AttachedInfo, rec.AttachedParent);
            if (attached != Entity.Null)
            {
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
