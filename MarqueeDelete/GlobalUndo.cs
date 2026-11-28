using System;
using System.Collections.Generic;
using Colossal.Collections;
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
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using NetElevation = Game.Net.Elevation;
using NetNode = Game.Net.Node;
using GameObject = Game.Objects.Object;

namespace MarqueeDelete
{
    public struct GlobalUndoUiState : IEquatable<GlobalUndoUiState>
    {
        public bool GlobalUndoEnabled;
        public bool TrackBuild;
        public bool TrackBulldoze;
        public bool TrackUpgrade;
        public bool TrackZone;
        public bool TrackTerrain;
        public bool ExternalToolYielding;
        public string ExternalToolName;
        public string ActiveStepKind;
        public string TopStepKind;
        public int TopStepCount;
        public int HistoryDepth;
        public int TerrainMemoryKB;
        public int HistoryRevision;
        public string HistoryJson;

        public bool Equals(GlobalUndoUiState other) =>
            GlobalUndoEnabled == other.GlobalUndoEnabled &&
            TrackBuild == other.TrackBuild &&
            TrackBulldoze == other.TrackBulldoze &&
            TrackUpgrade == other.TrackUpgrade &&
            TrackZone == other.TrackZone &&
            TrackTerrain == other.TrackTerrain &&
            ExternalToolYielding == other.ExternalToolYielding &&
            string.Equals(ExternalToolName, other.ExternalToolName, StringComparison.Ordinal) &&
            string.Equals(ActiveStepKind, other.ActiveStepKind, StringComparison.Ordinal) &&
            string.Equals(TopStepKind, other.TopStepKind, StringComparison.Ordinal) &&
            TopStepCount == other.TopStepCount &&
            HistoryDepth == other.HistoryDepth &&
            TerrainMemoryKB == other.TerrainMemoryKB &&
            HistoryRevision == other.HistoryRevision &&
            string.Equals(HistoryJson, other.HistoryJson, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is GlobalUndoUiState other && Equals(other);
        public override int GetHashCode() => unchecked((HistoryRevision * 397) ^ HistoryDepth ^ (TerrainMemoryKB * 17));
        public static bool operator ==(GlobalUndoUiState left, GlobalUndoUiState right) => left.Equals(right);
        public static bool operator !=(GlobalUndoUiState left, GlobalUndoUiState right) => !left.Equals(right);
    }

    internal struct CreatedEntityRecord
    {
        public Entity Entity;
        public Entity Prefab;
        public float3 Position;
        public bool HasOwner;
        public Entity OwnerEntity;
    }

    internal struct InPlaceNetRecord
    {
        public Entity Entity;
        public Entity Prefab;
        public bool HasOwner;
        public Entity OwnerEntity;
        public bool HasUpgraded;
        public CompositionFlags UpgradedFlags;
        public bool HasElevation;
        public float2 Elevation;
        public bool HasCurve;
        public Bezier4x3 Curve;
        public float CurveLength;
        public bool HasEdge;
        public Entity EdgeStart;
        public Entity EdgeEnd;
        public bool HasSubReplacements;
        public SubReplacement[] SubReplacements;
        public bool IsNode;
        public float3 NodePosition;
        public quaternion NodeRotation;

        // Combine-shrink support: relocate newly created far-end node back to old endpoint pose
        public Entity MoveNodeEntity;
        public float3 TargetNodePosition;
        public quaternion TargetNodeRotation;
        public bool TargetNodeHasElevation;
        public float2 TargetNodeElevation;
        public bool TargetNodeHasUpgraded;
        public CompositionFlags TargetNodeUpgradedFlags;
        public Entity TargetNodePrefab;
        public bool RequireSingleConnectedEdge;

        // Distinguishes incidental adjacent-road adjustment during Build from explicit Upgrade/Replace
        public bool IsIncidentalBuildAdjustment;
    }

    internal struct TempNetLineage
    {
        public Entity TempEntity;
        public Entity Original;
        public TempFlags Flags;
        public bool IsNode;
        public bool IsEdge;
    }

    internal struct ZoneBlockSnapshot
    {
        public Entity BlockEntity;
        public int2 Size;
        public Game.Zones.Cell[] Cells;
    }

    internal sealed class GlobalUndoTransaction : IGlobalUndoHistoryItem
    {
        public int StepId;
        public GlobalUndoActionKind Kind;
        public string SourceToolId = string.Empty;
        public long CreatedUnixSeconds;

        public readonly List<CreatedEntityRecord> CreatedEntities = new List<CreatedEntityRecord>();
        public readonly HashSet<Entity> CreatedEntitySet = new HashSet<Entity>();

        public DeletionUndoManager.DetachedSnapshot DeletedSnapshot;
        public readonly HashSet<Entity> CapturedDeletedOriginals = new HashSet<Entity>();

        public readonly Dictionary<Entity, InPlaceNetRecord> InPlaceNetPatches = new Dictionary<Entity, InPlaceNetRecord>();
        public readonly List<Entity> PreservedSplitRefreshTargets = new List<Entity>();
        public readonly Dictionary<Entity, ZoneBlockSnapshot> ZoneChanges = new Dictionary<Entity, ZoneBlockSnapshot>();
        public TerrainPatchData TerrainPatch;
        public bool TerrainEvicted;
        public bool HasRoadBuildReconciliation;
        public bool HasSemanticUpgradeOrReplace;

        // Per-frame transient maps while the stroke is open; cleared on Finalize().
        internal Dictionary<Entity, InPlaceNetRecord> PendingModifiedInPlace;
        internal Dictionary<Entity, DeletionUndoManager.DetachedSnapshot> PendingModifiedDeleteFallbacks;
        internal Dictionary<Entity, TempNetLineage> PendingTempLineage;
        internal HashSet<Entity> PendingNewTempNodes;
        internal Dictionary<Entity, InPlaceNetRecord> PendingOriginalNetMeta;
        internal Dictionary<Entity, DeletionUndoManager.DetachedSnapshot> PendingDeletedNetFallbacks;

        public bool IsBulkDelete => Kind == GlobalUndoActionKind.BulkDelete;

        public int DeletedCount => DeletedSnapshot != null ? DeletedSnapshot.TotalCount : 0;
        public int CreatedCount => CreatedEntities.Count;
        public int InPlaceNetCount => InPlaceNetPatches.Count;
        public int ZoneBlockCount => ZoneChanges.Count;
        public int TerrainTileCount => TerrainPatch != null ? TerrainPatch.TileCount : 0;

        public int TotalItemCount
        {
            get
            {
                int sum = CreatedEntities.Count + DeletedCount + InPlaceNetPatches.Count + ZoneChanges.Count;
                if (TerrainTileCount > 0) sum += TerrainTileCount;
                return sum;
            }
        }

        public bool HasAnyPayload =>
            CreatedEntities.Count > 0 ||
            (DeletedSnapshot != null && DeletedSnapshot.TotalCount > 0) ||
            InPlaceNetPatches.Count > 0 ||
            ZoneChanges.Count > 0 ||
            (TerrainPatch != null && TerrainPatch.TileCount > 0);

        public long TerrainBytes => TerrainPatch != null ? TerrainPatch.EstimatedBytes : 0L;

        public bool HasNonTerrainPayload =>
            CreatedEntities.Count > 0 ||
            (DeletedSnapshot != null && DeletedSnapshot.TotalCount > 0) ||
            InPlaceNetPatches.Count > 0 ||
            ZoneChanges.Count > 0;

        public void EvictTerrain()
        {
            if (TerrainPatch != null && TerrainPatch.TileCount > 0)
            {
                TerrainPatch = null;
                TerrainEvicted = true;
            }
        }

        public void ClearTransientState()
        {
            PendingModifiedInPlace = null;
            PendingModifiedDeleteFallbacks = null;
            PendingTempLineage = null;
            PendingNewTempNodes = null;
            PendingOriginalNetMeta = null;
            PendingDeletedNetFallbacks = null;
            CreatedEntitySet.Clear();
            CapturedDeletedOriginals.Clear();
        }

        public GlobalUndoStepSummary ToSummary()
        {
            return new GlobalUndoStepSummary
            {
                StepId = StepId,
                Kind = Kind,
                ToolId = SourceToolId ?? string.Empty,
                ItemCount = TotalItemCount,
                CreatedCount = CreatedEntities.Count,
                DeletedCount = DeletedCount,
                InPlaceNetCount = InPlaceNetPatches.Count,
                ZoneBlockCount = ZoneChanges.Count,
                TerrainTileCount = TerrainTileCount,
                TerrainEvicted = TerrainEvicted,
                CreatedUnixSeconds = CreatedUnixSeconds,
            };
        }
    }

    internal sealed class GlobalUndoManager
    {
        private readonly GlobalUndoHistoryStack<GlobalUndoTransaction> m_History =
            new GlobalUndoHistoryStack<GlobalUndoTransaction>(GlobalUndoPolicy.DefaultHistoryDepth, TerrainTileLogic.DefaultMemoryBudgetBytes);
        private readonly StrokeCoalescerState m_Coalescer = new StrokeCoalescerState();

        private GlobalUndoTransaction m_OpenStroke;
        private int m_PreApplyFrame = -1;
        private int m_NextStepId = 1;
        private int m_HistoryRevision = 1;
        private string m_CachedHistoryJson = "[]";
        private int m_CachedHistoryJsonRevision = -1;

        // Active replay state machine
        private GlobalUndoTransaction m_ActiveTx;
        private List<GlobalUndoExecutionPhase> m_ActivePhases;
        private int m_ActivePhaseIndex;
        private UndoAssetMask m_ActiveMask = UndoAssetMask.All;
        private bool m_ActivePaused;
        private bool m_ActiveUsingDeletionUndo;
        private int m_ActiveTotalWork;
        private int m_ActiveCompletedWork;
        private int m_ActiveSkippedWork;
        private int m_ActiveWaitFrames;
        private int m_ActiveDeletedSubTotal;
        private readonly List<float3> m_PendingInPlaceRefreshNetPositions = new List<float3>();

        // Notices mirroring SelectiveUndoUiSnapshot semantics so both HUDs stay in sync
        private string m_Notice = string.Empty;
        private int m_CaptureFailedCount;
        private string m_FinishedNotice = string.Empty;
        private int m_FinishedRestoredCount;
        private int m_FinishedSkippedCount;
        private int m_FinishedTotalCount;
        private int m_FinishedNotCapturedCount;

        public bool IsReplaying => m_ActiveTx != null && !m_ActivePaused;
        public bool HasActiveUndo => m_ActiveTx != null;
        public bool IsPaused => m_ActiveTx != null && m_ActivePaused;
        public int HistoryDepth => m_History.Count;
        public int HistoryRevision => m_HistoryRevision;

        public static GlobalUndoTrackOptions ReadTrackOptions()
        {
            Settings s = Mod.Settings;
            if (s == null)
            {
                return GlobalUndoTrackOptions.AllEnabled;
            }
            return new GlobalUndoTrackOptions
            {
                Enabled = s.EnableGlobalUndo,
                TrackBuild = s.UndoTrackBuild,
                TrackBulldoze = s.UndoTrackBulldoze,
                TrackUpgrade = s.UndoTrackUpgrade,
                TrackZone = s.UndoTrackZone,
                TrackTerrain = s.UndoTrackTerrain,
            };
        }

        private void SyncHistoryDepthLimit()
        {
            int limit = Mod.Settings != null
                ? GlobalUndoPolicy.ClampHistoryDepth(Mod.Settings.MaxUndoHistorySteps)
                : GlobalUndoPolicy.DefaultHistoryDepth;
            if (m_History.MaxDepth != limit)
            {
                int prevCount = m_History.Count;
                m_History.SetMaxDepth(limit);
                if (m_History.Count != prevCount)
                {
                    MarkHistoryChanged();
                }
            }
        }

        private void MarkHistoryChanged()
        {
            m_HistoryRevision++;
        }

        public bool TryConsumeInPlaceNetRefresh(List<float3> dstNetPositions)
        {
            if (m_PendingInPlaceRefreshNetPositions.Count == 0)
            {
                return false;
            }
            if (dstNetPositions != null)
            {
                dstNetPositions.AddRange(m_PendingInPlaceRefreshNetPositions);
            }
            m_PendingInPlaceRefreshNetPositions.Clear();
            return true;
        }

        public void ClearHistory(DeletionUndoManager deletionUndo)
        {
            m_OpenStroke = null;
            m_PreApplyFrame = -1;
            m_Coalescer.Reset();
            m_History.Clear();
            m_PendingInPlaceRefreshNetPositions.Clear();
            ClearActiveReplayState(deletionUndo, clearHistory: true);
            m_Notice = string.Empty;
            m_CaptureFailedCount = 0;
            ClearFinishedNotice();
            MarkHistoryChanged();
        }

        public void ClearFinishedNotice()
        {
            m_FinishedNotice = string.Empty;
            m_FinishedRestoredCount = 0;
            m_FinishedSkippedCount = 0;
            m_FinishedTotalCount = 0;
            m_FinishedNotCapturedCount = 0;
        }

        public void ClearNonAlertFinishedNotice()
        {
            if (m_FinishedNotice == "UndoSubmitted" || m_FinishedNotice == "UndoSubmittedPartial")
            {
                ClearFinishedNotice();
            }
        }

        public bool DismissFinishedNotice(DeletionUndoManager deletionUndo)
        {
            bool changed = !string.IsNullOrEmpty(m_FinishedNotice) || m_CaptureFailedCount > 0;
            ClearFinishedNotice();
            m_CaptureFailedCount = 0;
            if (string.Equals(m_Notice, "UndoNothingCaptured", StringComparison.Ordinal))
            {
                RefreshIdleNotice();
            }
            if (deletionUndo != null && deletionUndo.DismissFinishedNotice())
            {
                changed = true;
            }
            return changed;
        }

        public void PushBulkDelete(DeletionUndoManager.DetachedSnapshot snapshot)
        {
            FlushOpenStroke();
            ClearFinishedNotice();

            if (snapshot == null)
            {
                return;
            }

            if (snapshot.TotalCount <= 0)
            {
                if (snapshot.NotCaptured.Total > 0)
                {
                    m_CaptureFailedCount = snapshot.NotCaptured.Total;
                    m_Notice = "UndoNothingCaptured";
                }
                else
                {
                    m_CaptureFailedCount = 0;
                    RefreshIdleNotice();
                }
                return;
            }

            var tx = new GlobalUndoTransaction
            {
                StepId = m_NextStepId++,
                Kind = GlobalUndoActionKind.BulkDelete,
                SourceToolId = "Marquee Delete",
                CreatedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                DeletedSnapshot = snapshot,
            };
            PushTransactionInternal(tx);
        }

        private void PushTransactionInternal(GlobalUndoTransaction tx)
        {
            if (tx == null || !tx.HasAnyPayload)
            {
                return;
            }

            tx.ClearTransientState();
            SyncHistoryDepthLimit();
            m_CaptureFailedCount = 0;
            ClearFinishedNotice();
            m_History.Push(tx);
            RefreshIdleNotice();
            MarkHistoryChanged();
        }

        private void RefreshIdleNotice()
        {
            if (m_ActiveTx != null)
            {
                return;
            }

            GlobalUndoTransaction top = m_History.Peek();
            if (top == null)
            {
                m_Notice = string.Empty;
                return;
            }

            if (top.DeletedSnapshot != null && top.DeletedSnapshot.TotalCount > 0)
            {
                m_Notice = top.DeletedSnapshot.Truncated ? "UndoPartial" : "UndoGeometryOnly";
            }
            else if (top.TerrainPatch != null && top.TerrainPatch.Truncated)
            {
                m_Notice = "UndoPartial";
            }
            else
            {
                m_Notice = string.Empty;
            }
        }

        internal GlobalUndoTransaction PeekTopTransaction() => m_History.Peek();

        private static GlobalUndoActionKind ClassifyTransactionKind(GlobalUndoTransaction tx)
        {
            bool hasCreated = tx.CreatedEntities.Count > 0;
            bool hasDeleted = tx.DeletedSnapshot != null && tx.DeletedSnapshot.TotalCount > 0;
            bool hasIncidental = tx.HasRoadBuildReconciliation ||
                (hasCreated && !tx.HasSemanticUpgradeOrReplace && tx.InPlaceNetPatches.Count > 0);
            bool hasSemanticUpgrade = tx.HasSemanticUpgradeOrReplace ||
                (!hasIncidental && tx.InPlaceNetPatches.Count > 0);

            return GlobalUndoPolicy.ClassifyActionKind(
                tx.SourceToolId,
                hasCreated: hasCreated,
                hasDeleted: hasDeleted,
                hasSemanticUpgradeOrReplace: hasSemanticUpgrade,
                hasIncidentalNetAdjustment: hasIncidental,
                hasZone: tx.ZoneChanges.Count > 0,
                hasTerrain: tx.TerrainPatch != null && tx.TerrainPatch.TileCount > 0);
        }

        public void FlushOpenStroke()
        {
            if (m_OpenStroke == null)
            {
                m_Coalescer.Reset();
                m_PreApplyFrame = -1;
                return;
            }

            GlobalUndoTransaction tx = m_OpenStroke;
            m_OpenStroke = null;
            m_Coalescer.Reset();
            m_PreApplyFrame = -1;

            GlobalUndoTrackOptions opts = ReadTrackOptions();
            tx.Kind = ClassifyTransactionKind(tx);

            if (tx.Kind == GlobalUndoActionKind.None || !tx.HasAnyPayload)
            {
                return;
            }

            if (!GlobalUndoPolicy.IsKindTracked(in opts, tx.Kind))
            {
                return;
            }

            PushTransactionInternal(tx);
        }

        public void TickCoalescer(ToolSystem toolSystem, int currentFrame)
        {
            if (m_OpenStroke == null)
            {
                return;
            }

            string toolId = toolSystem?.activeTool?.toolID ?? string.Empty;
            bool isBrushing = toolSystem?.activeTool != null && toolSystem.activeTool.brushing;
            bool appliedThisFrame = m_PreApplyFrame == currentFrame;

            if (m_Coalescer.ShouldFinalize(toolId, isBrushing, appliedThisFrame, currentFrame))
            {
                FlushOpenStroke();
            }
        }

        public void OnPreApply(
            EntityManager em,
            ToolSystem toolSystem,
            TerrainSystem terrainSystem,
            DeletionUndoManager deletionUndo,
            EntityQuery tempQuery,
            EntityQuery zoneTempQuery,
            EntityQuery brushTempQuery)
        {
            if (HasActiveUndo || !Compatibility.CanUndoRebuild || !Compatibility.CanSubmitDeletion)
            {
                return;
            }

            ToolBaseSystem activeTool = toolSystem?.activeTool;
            if (activeTool == null)
            {
                return;
            }

            GlobalUndoTrackOptions opts = ReadTrackOptions();
            string toolId = activeTool.toolID ?? string.Empty;
            string toolTypeName = activeTool.GetType().FullName ?? activeTool.GetType().Name;
            if (!GlobalUndoPolicy.ShouldCaptureForTool(in opts, toolId, toolTypeName, IsReplaying))
            {
                return;
            }

            bool hasTemp = !tempQuery.IsEmptyIgnoreFilter;
            bool hasZoneTemp = !zoneTempQuery.IsEmptyIgnoreFilter;
            bool hasBrushTemp = !brushTempQuery.IsEmptyIgnoreFilter;
            if (!hasTemp && !hasZoneTemp && !hasBrushTemp)
            {
                return;
            }

            int frame = UnityEngine.Time.frameCount;
            bool isBrushing = activeTool.brushing;
            if (!m_Coalescer.ShouldCoalesce(toolId, isBrushing, frame))
            {
                FlushOpenStroke();
            }

            if (m_OpenStroke == null)
            {
                m_OpenStroke = new GlobalUndoTransaction
                {
                    StepId = m_NextStepId++,
                    SourceToolId = toolId,
                    CreatedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    PendingModifiedInPlace = new Dictionary<Entity, InPlaceNetRecord>(),
                    PendingModifiedDeleteFallbacks = new Dictionary<Entity, DeletionUndoManager.DetachedSnapshot>(),
                    PendingTempLineage = new Dictionary<Entity, TempNetLineage>(),
                    PendingNewTempNodes = new HashSet<Entity>(),
                    PendingOriginalNetMeta = new Dictionary<Entity, InPlaceNetRecord>(),
                    PendingDeletedNetFallbacks = new Dictionary<Entity, DeletionUndoManager.DetachedSnapshot>(),
                };
            }

            m_Coalescer.RecordApply(toolId, isBrushing, frame);
            m_PreApplyFrame = frame;

            // 1. Capture Zone Blocks (First-Write-Wins)
            if (opts.TrackZone && hasZoneTemp)
            {
                CaptureZonePreApply(em, zoneTempQuery, m_OpenStroke);
            }

            // 2. Capture Terrain Heightmap 64x64 Dirty Tiles (First-Write-Wins)
            if (opts.TrackTerrain && hasBrushTemp && terrainSystem != null)
            {
                CaptureTerrainPreApply(em, brushTempQuery, terrainSystem, m_OpenStroke);
            }

            // 3. Capture Deleted / Replaced / Modified / Upgraded originals before Apply* mutates them
            if ((opts.TrackBulldoze || opts.TrackUpgrade || opts.TrackBuild) && hasTemp)
            {
                CaptureTempOriginalsPreApply(em, tempQuery, deletionUndo, m_OpenStroke);
            }
        }

        public void OnPostApply(EntityManager em, EntityQuery createdQuery)
        {
            if (m_OpenStroke == null || m_PreApplyFrame != UnityEngine.Time.frameCount)
            {
                return;
            }

            if (HasActiveUndo)
            {
                return;
            }

            GlobalUndoTrackOptions opts = ReadTrackOptions();

            List<Entity> createdList = null;
            if (!createdQuery.IsEmptyIgnoreFilter)
            {
                using (NativeArray<Entity> created = createdQuery.ToEntityArray(Allocator.Temp))
                {
                    if (created.Length > 0)
                    {
                        createdList = new List<Entity>(created.Length);
                        for (int i = 0; i < created.Length; i++)
                        {
                            createdList.Add(created[i]);
                        }
                    }
                }
            }

            // 1. Reconcile road-build entity replacements (Node Replace, Edge Replace, Combine, SplitEdge)
            ReconcileRoadBuildPostApply(
                em,
                m_OpenStroke,
                createdList,
                out HashSet<Entity> reconciledSkipCreated,
                out HashSet<Entity> reconciledOriginals,
                out Dictionary<Entity, Entity> replacedNodeMap);

            // 2. Resolve pending deleted/modified originals: unreconciled deleted -> DeletedSnapshot, alive & mutated -> InPlaceNetPatch
            ResolvePendingModifiedOriginals(em, m_OpenStroke, opts, reconciledOriginals, replacedNodeMap);

            // 3. Capture newly created entities from this frame (excluding reconciled surviving adjacent roads)
            if ((opts.TrackBuild || opts.TrackUpgrade) && createdList != null && createdList.Count > 0)
            {
                CaptureCreatedEntitiesPostApply(em, createdList, m_OpenStroke, reconciledSkipCreated);
            }

            // 4. Clear per-frame lineage maps so subsequent frames in a coalesced stroke start clean
            m_OpenStroke.PendingTempLineage?.Clear();
            m_OpenStroke.PendingNewTempNodes?.Clear();
            m_OpenStroke.PendingOriginalNetMeta?.Clear();
        }

        private static void CaptureZonePreApply(EntityManager em, EntityQuery zoneTempQuery, GlobalUndoTransaction tx)
        {
            using (NativeArray<Entity> temps = zoneTempQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < temps.Length; i++)
                {
                    Entity tempEnt = temps[i];
                    if (!em.Exists(tempEnt) || !em.HasComponent<Temp>(tempEnt))
                    {
                        continue;
                    }

                    Temp temp = em.GetComponentData<Temp>(tempEnt);
                    if ((temp.m_Flags & TempFlags.Cancel) != 0)
                    {
                        continue;
                    }

                    Entity orig = temp.m_Original;
                    if (orig == Entity.Null || tx.ZoneChanges.ContainsKey(orig))
                    {
                        continue;
                    }

                    if (!em.Exists(orig) || em.HasComponent<Deleted>(orig) ||
                        !em.HasComponent<Block>(orig) || !em.HasBuffer<Game.Zones.Cell>(orig))
                    {
                        continue;
                    }

                    Block block = em.GetComponentData<Block>(orig);
                    DynamicBuffer<Game.Zones.Cell> cellBuf = em.GetBuffer<Game.Zones.Cell>(orig, isReadOnly: true);
                    int len = cellBuf.Length;
                    if (len <= 0 || block.m_Size.x <= 0 || block.m_Size.y <= 0 || len != block.m_Size.x * block.m_Size.y)
                    {
                        continue;
                    }

                    // Skip blocks whose zone cells are unchanged by this temp entity (prevents
                    // phantom no-op undo steps and avoids capturing untouched neighbor blocks).
                    if (em.HasBuffer<Game.Zones.Cell>(tempEnt))
                    {
                        DynamicBuffer<Game.Zones.Cell> tempCells = em.GetBuffer<Game.Zones.Cell>(tempEnt, isReadOnly: true);
                        if (tempCells.Length == len)
                        {
                            bool anyChanged = false;
                            for (int c = 0; c < len; c++)
                            {
                                Game.Zones.Cell a = cellBuf[c];
                                Game.Zones.Cell b = tempCells[c];
                                if (a.m_Zone.m_Index != b.m_Zone.m_Index ||
                                    a.m_State != b.m_State ||
                                    a.m_Height != b.m_Height)
                                {
                                    anyChanged = true;
                                    break;
                                }
                            }
                            if (!anyChanged)
                            {
                                continue;
                            }
                        }
                    }

                    var cells = new Game.Zones.Cell[len];
                    for (int c = 0; c < len; c++)
                    {
                        cells[c] = cellBuf[c];
                    }

                    tx.ZoneChanges[orig] = new ZoneBlockSnapshot
                    {
                        BlockEntity = orig,
                        Size = block.m_Size,
                        Cells = cells,
                    };
                }
            }
        }

        private static void CaptureTerrainPreApply(
            EntityManager em,
            EntityQuery brushTempQuery,
            TerrainSystem terrainSystem,
            GlobalUndoTransaction tx)
        {
            var brushBounds = new List<Bounds2>(4);
            using (NativeArray<Entity> brushEnts = brushTempQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < brushEnts.Length; i++)
                {
                    Entity e = brushEnts[i];
                    if (!em.Exists(e) || !em.HasComponent<Game.Tools.Brush>(e))
                    {
                        continue;
                    }

                    Game.Tools.Brush brush = em.GetComponentData<Game.Tools.Brush>(e);
                    if (brush.m_Tool == Entity.Null || !em.Exists(brush.m_Tool) ||
                        !em.HasComponent<TerraformingData>(brush.m_Tool))
                    {
                        continue;
                    }

                    TerraformingData tf = em.GetComponentData<TerraformingData>(brush.m_Tool);
                    if (tf.m_Target != TerraformingTarget.Height)
                    {
                        continue;
                    }

                    Bounds2 bounds = ToolUtils.GetBounds(brush);
                    brushBounds.Add(bounds);
                }
            }

            if (brushBounds.Count == 0)
            {
                return;
            }

            TerrainHeightData hData = default;
            bool readerAcquired = false;
            try
            {
                hData = terrainSystem.GetHeightData(waitForPending: true);
                readerAcquired = true;
                if (!hData.isCreated || hData.resolution.x <= 0 || hData.resolution.z <= 0)
                {
                    return;
                }

                int resX = hData.resolution.x;
                int resZ = hData.resolution.z;
                if (tx.TerrainPatch == null)
                {
                    tx.TerrainPatch = new TerrainPatchData(resX, resZ);
                }
                else if (tx.TerrainPatch.ResolutionX != resX || tx.TerrainPatch.ResolutionZ != resZ)
                {
                    return;
                }

                NativeArray<ushort> heights = hData.heights;
                for (int b = 0; b < brushBounds.Count; b++)
                {
                    Bounds2 area = brushBounds[b];
                    tx.TerrainPatch.ExpandWorldBounds(area.min.x, area.min.y, area.max.x, area.max.y);

                    if (!TerrainTileLogic.ComputeDirtyTileRange(
                            area.min.x, area.min.y, area.max.x, area.max.y,
                            hData.offset.x, hData.offset.z,
                            hData.scale.x, hData.scale.z,
                            resX, resZ,
                            padPixels: 2,
                            out int minTx, out int maxTx, out int minTz, out int maxTz))
                    {
                        continue;
                    }

                    for (int tz = minTz; tz <= maxTz; tz++)
                    {
                        for (int txIdx = minTx; txIdx <= maxTx; txIdx++)
                        {
                            var key = new TerrainTileKey(txIdx, tz);
                            if (tx.TerrainPatch.Tiles.ContainsKey(key))
                            {
                                continue;
                            }

                            if (tx.TerrainPatch.EstimatedBytes + TerrainTileLogic.BytesPerTile > TerrainTileLogic.DefaultMemoryBudgetBytes)
                            {
                                tx.TerrainPatch.Truncated = true;
                                break;
                            }

                            int baseX = txIdx * TerrainTileLogic.TileSize;
                            int baseZ = tz * TerrainTileLogic.TileSize;
                            int maxLx = Math.Min(TerrainTileLogic.TileSize, resX - baseX);
                            int maxLz = Math.Min(TerrainTileLogic.TileSize, resZ - baseZ);
                            if (maxLx <= 0 || maxLz <= 0)
                            {
                                continue;
                            }

                            var buf = new ushort[TerrainTileLogic.PixelsPerTile];
                            for (int lz = 0; lz < maxLz; lz++)
                            {
                                int srcRow = (baseZ + lz) * resX + baseX;
                                int dstRow = lz * TerrainTileLogic.TileSize;
                                for (int lx = 0; lx < maxLx; lx++)
                                {
                                    buf[dstRow + lx] = heights[srcRow + lx];
                                }
                            }
                            tx.TerrainPatch.TryRecordTile(key, buf);
                        }
                    }
                }
            }
            finally
            {
                if (readerAcquired)
                {
                    terrainSystem.AddCPUHeightReader(default(JobHandle));
                }
            }
        }

        private static void CaptureTempOriginalsPreApply(
            EntityManager em,
            EntityQuery tempQuery,
            DeletionUndoManager deletionUndo,
            GlobalUndoTransaction tx)
        {
            var deleteRoots = new List<Entity>();
            var deferredDeleteNetRoots = new List<Entity>();
            var deferredDeleteNetSeen = new HashSet<Entity>();
            var modifiedRoots = new List<Entity>();
            var modifiedSeen = new HashSet<Entity>();
            bool isBulldozeTool = GlobalUndoPolicy.IsBulldozeToolId(tx.SourceToolId);

            tx.PendingTempLineage ??= new Dictionary<Entity, TempNetLineage>();
            tx.PendingTempLineage.Clear();
            tx.PendingNewTempNodes ??= new HashSet<Entity>();
            tx.PendingNewTempNodes.Clear();
            tx.PendingOriginalNetMeta ??= new Dictionary<Entity, InPlaceNetRecord>();
            tx.PendingDeletedNetFallbacks ??= new Dictionary<Entity, DeletionUndoManager.DetachedSnapshot>();

            using (NativeArray<Entity> temps = tempQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < temps.Length; i++)
                {
                    Entity tempEnt = temps[i];
                    if (!em.Exists(tempEnt) || !em.HasComponent<Temp>(tempEnt))
                    {
                        continue;
                    }

                    if (em.HasComponent<Block>(tempEnt) || em.HasComponent<Game.Tools.Brush>(tempEnt))
                    {
                        continue;
                    }

                    Temp temp = em.GetComponentData<Temp>(tempEnt);
                    if ((temp.m_Flags & TempFlags.Cancel) != 0)
                    {
                        continue;
                    }

                    // Record net Temp lineage (including brand-new Temp nodes with m_Original == Entity.Null)
                    bool tempIsEdge = em.HasComponent<Edge>(tempEnt);
                    bool tempIsNode = em.HasComponent<NetNode>(tempEnt) && !tempIsEdge;
                    if (tempIsEdge || tempIsNode)
                    {
                        tx.PendingTempLineage[tempEnt] = new TempNetLineage
                        {
                            TempEntity = tempEnt,
                            Original = temp.m_Original,
                            Flags = temp.m_Flags,
                            IsNode = tempIsNode,
                            IsEdge = tempIsEdge,
                        };
                        if (tempIsNode && temp.m_Original == Entity.Null)
                        {
                            tx.PendingNewTempNodes.Add(tempEnt);
                        }
                    }

                    Entity orig = temp.m_Original;
                    if (orig == Entity.Null || !em.Exists(orig) || em.HasComponent<Deleted>(orig))
                    {
                        continue;
                    }

                    if (em.HasComponent<Vehicle>(orig) || em.HasComponent<Creature>(orig) ||
                        em.HasComponent<MapTile>(orig) || em.HasComponent<District>(orig) ||
                        em.HasComponent<Lane>(orig))
                    {
                        continue;
                    }

                    bool isOrigEdge = em.HasComponent<Edge>(orig);
                    bool isOrigNode = em.HasComponent<NetNode>(orig) && !isOrigEdge;
                    bool isNetOrig = isOrigEdge || isOrigNode;
                    bool isDelete = (temp.m_Flags & TempFlags.Delete) != 0;
                    bool isModifyOrUpgrade = (temp.m_Flags & (TempFlags.Replace | TempFlags.Modify | TempFlags.Upgrade | TempFlags.Combine)) != 0;
                    if (!isNetOrig)
                    {
                        bool isParentOnly = (temp.m_Flags & TempFlags.Parent) != 0 && !isDelete && !isModifyOrUpgrade;
                        if (isParentOnly)
                        {
                            continue;
                        }
                    }

                    if (isNetOrig)
                    {
                        TryCaptureOriginalNetMeta(em, orig, tx);
                        if (isOrigEdge)
                        {
                            Edge edgeData = em.GetComponentData<Edge>(orig);
                            TryCaptureOriginalNetMeta(em, edgeData.m_Start, tx);
                            TryCaptureOriginalNetMeta(em, edgeData.m_End, tx);
                        }
                    }

                    if (isDelete)
                    {
                        if (isNetOrig && !isBulldozeTool)
                        {
                            if (!tx.CapturedDeletedOriginals.Contains(orig) && deferredDeleteNetSeen.Add(orig))
                            {
                                deferredDeleteNetRoots.Add(orig);
                            }
                        }
                        else if (!tx.CapturedDeletedOriginals.Contains(orig))
                        {
                            deleteRoots.Add(orig);
                        }
                    }
                    else if ((isNetOrig || isModifyOrUpgrade) && modifiedSeen.Add(orig))
                    {
                        modifiedRoots.Add(orig);
                    }
                }
            }

            if (deleteRoots.Count > 0)
            {
                for (int i = 0; i < deleteRoots.Count; i++)
                {
                    Entity delRoot = deleteRoots[i];
                    if (em.HasComponent<Edge>(delRoot))
                    {
                        Edge edgeData = em.GetComponentData<Edge>(delRoot);
                        TryCaptureInPlaceNetSnapshot(em, edgeData.m_Start, tx);
                        TryCaptureInPlaceNetSnapshot(em, edgeData.m_End, tx);
                    }
                }

                DeletionPlan delPlan = Cascade.Plan(em, deleteRoots);
                tx.DeletedSnapshot = deletionUndo.MergeIntoDetachedSnapshot(
                    em, tx.DeletedSnapshot, delPlan, tx.CapturedDeletedOriginals);
            }

            if (deferredDeleteNetRoots.Count > 0)
            {
                var singleRoot = new Entity[1];
                for (int i = 0; i < deferredDeleteNetRoots.Count; i++)
                {
                    Entity orig = deferredDeleteNetRoots[i];
                    if (tx.CapturedDeletedOriginals.Contains(orig))
                    {
                        continue;
                    }

                    if (em.HasComponent<Edge>(orig))
                    {
                        Edge edgeData = em.GetComponentData<Edge>(orig);
                        TryCaptureInPlaceNetSnapshot(em, edgeData.m_Start, tx);
                        TryCaptureInPlaceNetSnapshot(em, edgeData.m_End, tx);
                    }

                    if (!tx.PendingDeletedNetFallbacks.ContainsKey(orig))
                    {
                        singleRoot[0] = orig;
                        DeletionPlan singlePlan = Cascade.Plan(em, singleRoot);
                        DeletionUndoManager.DetachedSnapshot fallback = deletionUndo.CaptureDetachedSnapshot(em, singlePlan);
                        if (fallback != null && fallback.TotalCount > 0)
                        {
                            tx.PendingDeletedNetFallbacks[orig] = fallback;
                        }
                    }
                }
            }

            if (modifiedRoots.Count > 0)
            {
                tx.PendingModifiedInPlace ??= new Dictionary<Entity, InPlaceNetRecord>();
                tx.PendingModifiedDeleteFallbacks ??= new Dictionary<Entity, DeletionUndoManager.DetachedSnapshot>();

                var singleRoot = new Entity[1];
                for (int i = 0; i < modifiedRoots.Count; i++)
                {
                    Entity orig = modifiedRoots[i];
                    if (tx.CapturedDeletedOriginals.Contains(orig))
                    {
                        continue;
                    }

                    // Snapshot in-place net fields before ApplyNetSystem mutates them (First-Write-Wins)
                    TryCaptureInPlaceNetSnapshot(em, orig, tx);
                    if (em.HasComponent<Edge>(orig))
                    {
                        Edge edgeData = em.GetComponentData<Edge>(orig);
                        TryCaptureInPlaceNetSnapshot(em, edgeData.m_Start, tx);
                        TryCaptureInPlaceNetSnapshot(em, edgeData.m_End, tx);
                    }

                    // Also capture fallback delete snapshot in case Apply* actually deletes orig (e.g. edge split)
                    if (!tx.PendingModifiedDeleteFallbacks.ContainsKey(orig))
                    {
                        singleRoot[0] = orig;
                        DeletionPlan singlePlan = Cascade.Plan(em, singleRoot);
                        DeletionUndoManager.DetachedSnapshot fallback = deletionUndo.CaptureDetachedSnapshot(em, singlePlan);
                        if (fallback != null && fallback.TotalCount > 0)
                        {
                            tx.PendingModifiedDeleteFallbacks[orig] = fallback;
                        }
                    }
                }
            }
        }

        private static bool TryBuildNetRecord(EntityManager em, Entity orig, out InPlaceNetRecord rec)
        {
            rec = default;
            if (orig == Entity.Null || !em.Exists(orig) || em.HasComponent<Deleted>(orig) ||
                (!em.HasComponent<Edge>(orig) && !em.HasComponent<NetNode>(orig)) ||
                !em.HasComponent<PrefabRef>(orig))
            {
                return false;
            }

            bool hasEdge = em.HasComponent<Edge>(orig);
            Edge edgeData = hasEdge ? em.GetComponentData<Edge>(orig) : default;
            bool hasOwner = em.HasComponent<Owner>(orig);
            Entity ownerEnt = hasOwner ? em.GetComponentData<Owner>(orig).m_Owner : Entity.Null;
            bool hasSubRep = em.HasBuffer<SubReplacement>(orig);
            SubReplacement[] subReps = null;
            if (hasSubRep)
            {
                DynamicBuffer<SubReplacement> srBuf = em.GetBuffer<SubReplacement>(orig, isReadOnly: true);
                if (srBuf.Length > 0)
                {
                    subReps = new SubReplacement[srBuf.Length];
                    for (int s = 0; s < srBuf.Length; s++)
                    {
                        subReps[s] = srBuf[s];
                    }
                }
                else
                {
                    subReps = Array.Empty<SubReplacement>();
                }
            }

            rec = new InPlaceNetRecord
            {
                Entity = orig,
                Prefab = em.GetComponentData<PrefabRef>(orig).m_Prefab,
                HasOwner = hasOwner,
                OwnerEntity = ownerEnt,
                HasUpgraded = em.HasComponent<Upgraded>(orig),
                UpgradedFlags = em.HasComponent<Upgraded>(orig) ? em.GetComponentData<Upgraded>(orig).m_Flags : default,
                HasElevation = em.HasComponent<NetElevation>(orig),
                Elevation = em.HasComponent<NetElevation>(orig) ? em.GetComponentData<NetElevation>(orig).m_Elevation : default,
                HasCurve = em.HasComponent<Curve>(orig),
                Curve = em.HasComponent<Curve>(orig) ? em.GetComponentData<Curve>(orig).m_Bezier : default,
                CurveLength = em.HasComponent<Curve>(orig) ? em.GetComponentData<Curve>(orig).m_Length : 0f,
                HasEdge = hasEdge,
                EdgeStart = edgeData.m_Start,
                EdgeEnd = edgeData.m_End,
                HasSubReplacements = hasSubRep,
                SubReplacements = subReps,
                IsNode = em.HasComponent<NetNode>(orig) && !hasEdge,
                NodePosition = em.HasComponent<NetNode>(orig) ? em.GetComponentData<NetNode>(orig).m_Position : default,
                NodeRotation = em.HasComponent<NetNode>(orig) ? em.GetComponentData<NetNode>(orig).m_Rotation : quaternion.identity,
            };
            return true;
        }

        private static void TryCaptureOriginalNetMeta(EntityManager em, Entity orig, GlobalUndoTransaction tx)
        {
            if (orig == Entity.Null)
            {
                return;
            }
            tx.PendingOriginalNetMeta ??= new Dictionary<Entity, InPlaceNetRecord>();
            if (tx.PendingOriginalNetMeta.ContainsKey(orig))
            {
                return;
            }
            if (TryBuildNetRecord(em, orig, out InPlaceNetRecord rec))
            {
                tx.PendingOriginalNetMeta[orig] = rec;
            }
        }

        private static void TryCaptureInPlaceNetSnapshot(EntityManager em, Entity orig, GlobalUndoTransaction tx)
        {
            if (orig == Entity.Null ||
                tx.CapturedDeletedOriginals.Contains(orig) ||
                tx.InPlaceNetPatches.ContainsKey(orig))
            {
                return;
            }

            tx.PendingModifiedInPlace ??= new Dictionary<Entity, InPlaceNetRecord>();
            if (tx.PendingModifiedInPlace.ContainsKey(orig))
            {
                return;
            }

            if (tx.PendingOriginalNetMeta != null && tx.PendingOriginalNetMeta.TryGetValue(orig, out InPlaceNetRecord cached))
            {
                tx.PendingModifiedInPlace[orig] = cached;
                return;
            }

            if (TryBuildNetRecord(em, orig, out InPlaceNetRecord rec))
            {
                tx.PendingModifiedInPlace[orig] = rec;
            }
        }

        private static bool IsDeletedOrMissing(EntityManager em, Entity e)
        {
            return e == Entity.Null || !em.Exists(e) || em.HasComponent<Deleted>(e);
        }

        private static bool IsAlivePermanent(EntityManager em, Entity e)
        {
            return e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e) && !em.HasComponent<Temp>(e);
        }

        private static bool HasSameOwner(EntityManager em, in InPlaceNetRecord oldMeta, Entity newEnt)
        {
            bool newHasOwner = em.HasComponent<Owner>(newEnt);
            Entity newOwner = newHasOwner ? em.GetComponentData<Owner>(newEnt).m_Owner : Entity.Null;
            if (oldMeta.HasOwner != newHasOwner)
            {
                return false;
            }
            return !oldMeta.HasOwner || oldMeta.OwnerEntity == newOwner;
        }

        private static bool IsSemanticNetMutation(EntityManager em, Entity entity, in InPlaceNetRecord before, string toolId)
        {
            if (GlobalUndoPolicy.IsUpgradeToolId(toolId))
            {
                return true;
            }

            if (em.HasComponent<PrefabRef>(entity) && em.GetComponentData<PrefabRef>(entity).m_Prefab != before.Prefab)
            {
                return true;
            }

            bool liveHasUpgraded = em.HasComponent<Upgraded>(entity);
            if (liveHasUpgraded != before.HasUpgraded)
            {
                return true;
            }
            if (liveHasUpgraded)
            {
                CompositionFlags liveFlags = em.GetComponentData<Upgraded>(entity).m_Flags;
                if (liveFlags.m_General != before.UpgradedFlags.m_General ||
                    liveFlags.m_Left != before.UpgradedFlags.m_Left ||
                    liveFlags.m_Right != before.UpgradedFlags.m_Right)
                {
                    return true;
                }
            }

            bool liveHasSubRep = em.HasBuffer<SubReplacement>(entity);
            if (liveHasSubRep != before.HasSubReplacements)
            {
                return true;
            }
            if (liveHasSubRep)
            {
                DynamicBuffer<SubReplacement> liveBuf = em.GetBuffer<SubReplacement>(entity, isReadOnly: true);
                int beforeLen = before.SubReplacements != null ? before.SubReplacements.Length : 0;
                if (liveBuf.Length != beforeLen)
                {
                    return true;
                }
                for (int i = 0; i < beforeLen; i++)
                {
                    if (!liveBuf[i].Equals(before.SubReplacements[i]))
                    {
                        return true;
                    }
                }
            }

            if (before.HasEdge && em.HasComponent<Edge>(entity))
            {
                Edge liveEdge = em.GetComponentData<Edge>(entity);
                if (GlobalUndoPolicy.ShouldRestoreReversedEdgeEndpoints(
                        before.EdgeStart.Index, before.EdgeStart.Version,
                        before.EdgeEnd.Index, before.EdgeEnd.Version,
                        liveEdge.m_Start.Index, liveEdge.m_Start.Version,
                        liveEdge.m_End.Index, liveEdge.m_End.Version))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReconcileRoadBuildPostApply(
            EntityManager em,
            GlobalUndoTransaction tx,
            List<Entity> createdList,
            out HashSet<Entity> reconciledSkipCreated,
            out HashSet<Entity> reconciledOriginals,
            out Dictionary<Entity, Entity> replacedNodeMap)
        {
            reconciledSkipCreated = new HashSet<Entity>();
            reconciledOriginals = new HashSet<Entity>();
            replacedNodeMap = new Dictionary<Entity, Entity>();

            if (GlobalUndoPolicy.IsBulldozeToolId(tx.SourceToolId))
            {
                return;
            }

            Dictionary<Entity, TempNetLineage> lineageMap = tx.PendingTempLineage;
            Dictionary<Entity, InPlaceNetRecord> origMetaMap = tx.PendingOriginalNetMeta;
            if (origMetaMap == null || origMetaMap.Count == 0)
            {
                return;
            }

            Dictionary<Entity, Entity> nodeMap = replacedNodeMap;
            Entity MapNode(Entity n) => nodeMap.TryGetValue(n, out Entity mapped) ? mapped : n;

            // Step 1: Build replacedNodeMap (N_old -> N_new via TempFlags.Replace on NetNode)
            if (lineageMap != null && lineageMap.Count > 0)
            {
                foreach (var kv in lineageMap)
                {
                    TempNetLineage lin = kv.Value;
                    if (!lin.IsNode || (lin.Flags & TempFlags.Replace) == 0 || lin.Original == Entity.Null)
                    {
                        continue;
                    }
                    if (!IsDeletedOrMissing(em, lin.Original) || !IsAlivePermanent(em, lin.TempEntity) ||
                        !em.HasComponent<NetNode>(lin.TempEntity) || !em.HasComponent<PrefabRef>(lin.TempEntity))
                    {
                        continue;
                    }
                    if (!origMetaMap.TryGetValue(lin.Original, out InPlaceNetRecord oldNodeMeta) || !oldNodeMeta.IsNode)
                    {
                        continue;
                    }

                    replacedNodeMap[lin.Original] = lin.TempEntity;
                    reconciledOriginals.Add(lin.Original);
                    reconciledSkipCreated.Add(lin.TempEntity);

                    InPlaceNetRecord patch = oldNodeMeta;
                    patch.Entity = lin.TempEntity;
                    bool isSemantic = IsSemanticNetMutation(em, lin.TempEntity, in oldNodeMeta, tx.SourceToolId) ||
                                      (lin.Flags & TempFlags.Upgrade) != 0;
                    if (isSemantic)
                    {
                        tx.HasSemanticUpgradeOrReplace = true;
                        patch.IsIncidentalBuildAdjustment = false;
                    }
                    else
                    {
                        tx.HasRoadBuildReconciliation = true;
                        patch.IsIncidentalBuildAdjustment = true;
                    }

                    if (HasNetMutatedInPlace(em, lin.TempEntity, in patch) && !tx.InPlaceNetPatches.ContainsKey(lin.TempEntity))
                    {
                        tx.InPlaceNetPatches[lin.TempEntity] = patch;
                    }
                }

                // Step 2: Case B — Edge TempFlags.Replace reconciliation (same Prefab & Owner, matching endpoints)
                foreach (var kv in lineageMap)
                {
                    TempNetLineage lin = kv.Value;
                    if (!lin.IsEdge || (lin.Flags & TempFlags.Replace) == 0 || (lin.Flags & TempFlags.Combine) != 0 ||
                        lin.Original == Entity.Null || reconciledOriginals.Contains(lin.Original))
                    {
                        continue;
                    }
                    if (!IsDeletedOrMissing(em, lin.Original) || !IsAlivePermanent(em, lin.TempEntity) ||
                        !em.HasComponent<Edge>(lin.TempEntity) || !em.HasComponent<PrefabRef>(lin.TempEntity))
                    {
                        continue;
                    }
                    if (!origMetaMap.TryGetValue(lin.Original, out InPlaceNetRecord oldEdgeMeta) || !oldEdgeMeta.HasEdge)
                    {
                        continue;
                    }
                    if (em.GetComponentData<PrefabRef>(lin.TempEntity).m_Prefab != oldEdgeMeta.Prefab ||
                        !HasSameOwner(em, in oldEdgeMeta, lin.TempEntity))
                    {
                        continue;
                    }

                    Entity expectedStart = MapNode(oldEdgeMeta.EdgeStart);
                    Entity expectedEnd = MapNode(oldEdgeMeta.EdgeEnd);
                    Edge newEdge = em.GetComponentData<Edge>(lin.TempEntity);
                    bool forwardMatch = newEdge.m_Start == expectedStart && newEdge.m_End == expectedEnd;
                    bool reverseMatch = newEdge.m_Start == expectedEnd && newEdge.m_End == expectedStart;
                    if (!forwardMatch && !reverseMatch)
                    {
                        continue;
                    }

                    reconciledSkipCreated.Add(lin.TempEntity);
                    reconciledOriginals.Add(lin.Original);

                    InPlaceNetRecord patch = oldEdgeMeta;
                    patch.Entity = lin.TempEntity;
                    patch.EdgeStart = expectedStart;
                    patch.EdgeEnd = expectedEnd;

                    bool isSemantic = reverseMatch ||
                                      (lin.Flags & TempFlags.Upgrade) != 0 ||
                                      IsSemanticNetMutation(em, lin.TempEntity, in patch, tx.SourceToolId);
                    if (isSemantic)
                    {
                        tx.HasSemanticUpgradeOrReplace = true;
                        patch.IsIncidentalBuildAdjustment = false;
                    }
                    else
                    {
                        tx.HasRoadBuildReconciliation = true;
                        patch.IsIncidentalBuildAdjustment = true;
                    }

                    if (HasNetMutatedInPlace(em, lin.TempEntity, in patch) && !tx.InPlaceNetPatches.ContainsKey(lin.TempEntity))
                    {
                        tx.InPlaceNetPatches[lin.TempEntity] = patch;
                    }
                }

                // Step 3: Case A — Edge TempFlags.Combine reconciliation (straight extension merge)
                foreach (var kv in lineageMap)
                {
                    TempNetLineage lin = kv.Value;
                    if (!lin.IsEdge || (lin.Flags & TempFlags.Combine) == 0 ||
                        lin.Original == Entity.Null || reconciledOriginals.Contains(lin.Original))
                    {
                        continue;
                    }
                    if (!IsDeletedOrMissing(em, lin.Original) || !IsAlivePermanent(em, lin.TempEntity) ||
                        !em.HasComponent<Edge>(lin.TempEntity) || !em.HasComponent<PrefabRef>(lin.TempEntity))
                    {
                        continue;
                    }
                    if (!origMetaMap.TryGetValue(lin.Original, out InPlaceNetRecord oldEdgeMeta) || !oldEdgeMeta.HasEdge)
                    {
                        continue;
                    }
                    if (em.GetComponentData<PrefabRef>(lin.TempEntity).m_Prefab != oldEdgeMeta.Prefab ||
                        !HasSameOwner(em, in oldEdgeMeta, lin.TempEntity))
                    {
                        continue;
                    }

                    Entity mappedOldStart = MapNode(oldEdgeMeta.EdgeStart);
                    Entity mappedOldEnd = MapNode(oldEdgeMeta.EdgeEnd);
                    Edge newEdge = em.GetComponentData<Edge>(lin.TempEntity);

                    Entity keepNode = Entity.Null;
                    Entity oldEndNode = Entity.Null;
                    Entity newFarNode = Entity.Null;
                    Entity restoredStart = Entity.Null;
                    Entity restoredEnd = Entity.Null;

                    bool startSurvived = IsAlivePermanent(em, mappedOldStart) &&
                                         (newEdge.m_Start == mappedOldStart || newEdge.m_End == mappedOldStart);
                    bool endSurvived = IsAlivePermanent(em, mappedOldEnd) &&
                                       (newEdge.m_Start == mappedOldEnd || newEdge.m_End == mappedOldEnd);

                    if (startSurvived && !endSurvived && IsDeletedOrMissing(em, oldEdgeMeta.EdgeEnd))
                    {
                        keepNode = mappedOldStart;
                        oldEndNode = oldEdgeMeta.EdgeEnd;
                        newFarNode = newEdge.m_Start == mappedOldStart ? newEdge.m_End : newEdge.m_Start;
                        restoredStart = keepNode;
                        restoredEnd = newFarNode;
                    }
                    else if (endSurvived && !startSurvived && IsDeletedOrMissing(em, oldEdgeMeta.EdgeStart))
                    {
                        keepNode = mappedOldEnd;
                        oldEndNode = oldEdgeMeta.EdgeStart;
                        newFarNode = newEdge.m_End == mappedOldEnd ? newEdge.m_Start : newEdge.m_End;
                        restoredStart = newFarNode;
                        restoredEnd = keepNode;
                    }
                    else
                    {
                        continue;
                    }

                    // Safety guard: newFarNode MUST be newly created in this stroke (not a pre-existing intersection)
                    if (newFarNode == Entity.Null || newFarNode == keepNode ||
                        tx.PendingNewTempNodes == null || !tx.PendingNewTempNodes.Contains(newFarNode) ||
                        !IsAlivePermanent(em, newFarNode) || !em.HasComponent<NetNode>(newFarNode))
                    {
                        continue;
                    }

                    if (!origMetaMap.TryGetValue(oldEndNode, out InPlaceNetRecord oldEndNodeMeta) || !oldEndNodeMeta.IsNode)
                    {
                        continue;
                    }

                    reconciledSkipCreated.Add(lin.TempEntity);
                    reconciledOriginals.Add(lin.Original);
                    reconciledOriginals.Add(oldEndNode);

                    InPlaceNetRecord patch = oldEdgeMeta;
                    patch.Entity = lin.TempEntity;
                    patch.EdgeStart = restoredStart;
                    patch.EdgeEnd = restoredEnd;
                    patch.MoveNodeEntity = newFarNode;
                    patch.TargetNodePosition = oldEndNodeMeta.NodePosition;
                    patch.TargetNodeRotation = oldEndNodeMeta.NodeRotation;
                    patch.TargetNodeHasElevation = oldEndNodeMeta.HasElevation;
                    patch.TargetNodeElevation = oldEndNodeMeta.Elevation;
                    patch.TargetNodeHasUpgraded = oldEndNodeMeta.HasUpgraded;
                    patch.TargetNodeUpgradedFlags = oldEndNodeMeta.UpgradedFlags;
                    patch.TargetNodePrefab = oldEndNodeMeta.Prefab;
                    patch.RequireSingleConnectedEdge = true;
                    bool isSemantic = (lin.Flags & TempFlags.Upgrade) != 0 ||
                                      IsSemanticNetMutation(em, lin.TempEntity, in patch, tx.SourceToolId);
                    if (isSemantic)
                    {
                        tx.HasSemanticUpgradeOrReplace = true;
                        patch.IsIncidentalBuildAdjustment = false;
                    }
                    else
                    {
                        tx.HasRoadBuildReconciliation = true;
                        patch.IsIncidentalBuildAdjustment = true;
                    }

                    if (!tx.InPlaceNetPatches.ContainsKey(lin.TempEntity))
                    {
                        tx.InPlaceNetPatches[lin.TempEntity] = patch;
                    }
                }
            }

            // Step 4: Case C — SplitEdge collinear chain reconciliation (keep split halves, delete only new branch)
            if (createdList == null || createdList.Count < 2)
            {
                return;
            }

            var candidateEdges = new List<Entity>();
            var entityToId = new Dictionary<Entity, int> { [Entity.Null] = 0 };
            var idToEntity = new List<Entity> { Entity.Null };

            int GetOrAssignId(Entity e)
            {
                if (!entityToId.TryGetValue(e, out int id))
                {
                    id = idToEntity.Count;
                    entityToId[e] = id;
                    idToEntity.Add(e);
                }
                return id;
            }

            var allCandidates = new List<SplitChainCandidate>();
            for (int i = 0; i < createdList.Count; i++)
            {
                Entity e = createdList[i];
                if (reconciledSkipCreated.Contains(e) || !IsAlivePermanent(em, e) ||
                    !em.HasComponent<Edge>(e) || !em.HasComponent<Curve>(e) || !em.HasComponent<PrefabRef>(e))
                {
                    continue;
                }

                Edge edge = em.GetComponentData<Edge>(e);
                Curve curve = em.GetComponentData<Curve>(e);
                Bezier4x3 b = curve.m_Bezier;
                float3 mid = 0.125f * b.a + 0.375f * b.b + 0.375f * b.c + 0.125f * b.d;
                float3 startPos = em.HasComponent<NetNode>(edge.m_Start) ? em.GetComponentData<NetNode>(edge.m_Start).m_Position : b.a;
                float3 endPos = em.HasComponent<NetNode>(edge.m_End) ? em.GetComponentData<NetNode>(edge.m_End).m_Position : b.d;
                Entity prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                Entity owner = em.HasComponent<Owner>(e) ? em.GetComponentData<Owner>(e).m_Owner : Entity.Null;

                candidateEdges.Add(e);
                allCandidates.Add(new SplitChainCandidate
                {
                    EdgeId = GetOrAssignId(e),
                    StartNodeId = GetOrAssignId(edge.m_Start),
                    EndNodeId = GetOrAssignId(edge.m_End),
                    PrefabId = GetOrAssignId(prefab),
                    OwnerId = GetOrAssignId(owner),
                    StartPos = ToNetPoint3(startPos),
                    MidPos = ToNetPoint3(mid),
                    EndPos = ToNetPoint3(endPos),
                    Length = curve.m_Length,
                });
            }

            if (allCandidates.Count < 2)
            {
                return;
            }

            // Gather all unreconciled deleted edge originals from deferred delete & modified fallbacks
            var deletedOldEdges = new List<Entity>();
            var deletedOldSeen = new HashSet<Entity>();
            if (tx.PendingDeletedNetFallbacks != null)
            {
                foreach (Entity orig in tx.PendingDeletedNetFallbacks.Keys)
                {
                    if (!reconciledOriginals.Contains(orig) && deletedOldSeen.Add(orig))
                    {
                        deletedOldEdges.Add(orig);
                    }
                }
            }
            if (tx.PendingModifiedDeleteFallbacks != null)
            {
                foreach (Entity orig in tx.PendingModifiedDeleteFallbacks.Keys)
                {
                    if (!reconciledOriginals.Contains(orig) && deletedOldSeen.Add(orig))
                    {
                        deletedOldEdges.Add(orig);
                    }
                }
            }

            var usedCandidateIndices = new HashSet<int>();
            var matchedIndices = new List<int>(4);
            var matchedMidNodeIds = new List<int>(4);
            var refreshTargetSet = new HashSet<Entity>(tx.PreservedSplitRefreshTargets);

            for (int i = 0; i < deletedOldEdges.Count; i++)
            {
                Entity oldEdgeEnt = deletedOldEdges[i];
                if (!IsDeletedOrMissing(em, oldEdgeEnt))
                {
                    continue;
                }
                if (!origMetaMap.TryGetValue(oldEdgeEnt, out InPlaceNetRecord oldMeta) ||
                    !oldMeta.HasEdge || !oldMeta.HasCurve)
                {
                    continue;
                }

                Entity mappedStart = MapNode(oldMeta.EdgeStart);
                Entity mappedEnd = MapNode(oldMeta.EdgeEnd);
                if (!entityToId.TryGetValue(mappedStart, out int startId) ||
                    !entityToId.TryGetValue(mappedEnd, out int endId))
                {
                    continue;
                }
                if (!entityToId.TryGetValue(oldMeta.Prefab, out int prefabId))
                {
                    continue;
                }
                Entity oldOwner = oldMeta.HasOwner ? oldMeta.OwnerEntity : Entity.Null;
                if (!entityToId.TryGetValue(oldOwner, out int ownerId))
                {
                    continue;
                }

                // Filter out candidates already claimed by an earlier split match
                var available = new List<SplitChainCandidate>(allCandidates.Count);
                var availToAll = new List<int>(allCandidates.Count);
                for (int c = 0; c < allCandidates.Count; c++)
                {
                    if (!usedCandidateIndices.Contains(c))
                    {
                        available.Add(allCandidates[c]);
                        availToAll.Add(c);
                    }
                }

                NetBezier3 oldBezier = ToNetBezier3(oldMeta.Curve);
                if (!GlobalUndoPolicy.TryFindCollinearSplitChain(
                        in oldBezier,
                        oldMeta.CurveLength,
                        startId,
                        endId,
                        prefabId,
                        ownerId,
                        available,
                        matchedIndices,
                        matchedMidNodeIds))
                {
                    continue;
                }

                reconciledOriginals.Add(oldEdgeEnt);
                tx.HasRoadBuildReconciliation = true;

                for (int m = 0; m < matchedIndices.Count; m++)
                {
                    int allIdx = availToAll[matchedIndices[m]];
                    usedCandidateIndices.Add(allIdx);
                    Entity partEdge = candidateEdges[allIdx];
                    reconciledSkipCreated.Add(partEdge);
                    if (refreshTargetSet.Add(partEdge))
                    {
                        tx.PreservedSplitRefreshTargets.Add(partEdge);
                    }
                }

                for (int m = 0; m < matchedMidNodeIds.Count; m++)
                {
                    int nodeId = matchedMidNodeIds[m];
                    if (nodeId > 0 && nodeId < idToEntity.Count)
                    {
                        Entity midNode = idToEntity[nodeId];
                        if (midNode != Entity.Null && refreshTargetSet.Add(midNode))
                        {
                            tx.PreservedSplitRefreshTargets.Add(midNode);
                        }
                    }
                }
            }
        }

        private static NetPoint3 ToNetPoint3(float3 p) => new NetPoint3(p.x, p.y, p.z);

        private static NetBezier3 ToNetBezier3(Bezier4x3 b) => new NetBezier3(
            ToNetPoint3(b.a),
            ToNetPoint3(b.b),
            ToNetPoint3(b.c),
            ToNetPoint3(b.d));

        private static void ResolvePendingModifiedOriginals(
            EntityManager em,
            GlobalUndoTransaction tx,
            GlobalUndoTrackOptions opts,
            HashSet<Entity> reconciledOriginals,
            Dictionary<Entity, Entity> replacedNodeMap)
        {
            if (tx.PendingDeletedNetFallbacks != null && tx.PendingDeletedNetFallbacks.Count > 0)
            {
                foreach (var kv in tx.PendingDeletedNetFallbacks)
                {
                    Entity orig = kv.Key;
                    if (reconciledOriginals != null && reconciledOriginals.Contains(orig))
                    {
                        continue;
                    }
                    bool wasDeleted = IsDeletedOrMissing(em, orig);
                    if (wasDeleted && tx.CapturedDeletedOriginals.Add(orig))
                    {
                        MergeSnapshotInto(tx, kv.Value);
                    }
                }
                tx.PendingDeletedNetFallbacks.Clear();
            }

            if (tx.PendingModifiedDeleteFallbacks != null && tx.PendingModifiedDeleteFallbacks.Count > 0)
            {
                foreach (var kv in tx.PendingModifiedDeleteFallbacks)
                {
                    Entity orig = kv.Key;
                    if (reconciledOriginals != null && reconciledOriginals.Contains(orig))
                    {
                        continue;
                    }
                    bool wasDeleted = IsDeletedOrMissing(em, orig);
                    if (wasDeleted && tx.CapturedDeletedOriginals.Add(orig))
                    {
                        MergeSnapshotInto(tx, kv.Value);
                    }
                }
                tx.PendingModifiedDeleteFallbacks.Clear();
            }

            if (tx.PendingModifiedInPlace != null && tx.PendingModifiedInPlace.Count > 0)
            {
                foreach (var kv in tx.PendingModifiedInPlace)
                {
                    Entity orig = kv.Key;
                    InPlaceNetRecord before = kv.Value;
                    if ((reconciledOriginals != null && reconciledOriginals.Contains(orig)) ||
                        tx.CapturedDeletedOriginals.Contains(orig) ||
                        tx.InPlaceNetPatches.ContainsKey(orig))
                    {
                        continue;
                    }
                    if (!em.Exists(orig) || em.HasComponent<Deleted>(orig) || !em.HasComponent<PrefabRef>(orig))
                    {
                        continue;
                    }

                    if (before.HasEdge && replacedNodeMap != null && replacedNodeMap.Count > 0)
                    {
                        if (replacedNodeMap.TryGetValue(before.EdgeStart, out Entity mappedStart))
                        {
                            before.EdgeStart = mappedStart;
                        }
                        if (replacedNodeMap.TryGetValue(before.EdgeEnd, out Entity mappedEnd))
                        {
                            before.EdgeEnd = mappedEnd;
                        }
                    }

                    if (HasNetMutatedInPlace(em, orig, in before))
                    {
                        bool isSemantic = IsSemanticNetMutation(em, orig, in before, tx.SourceToolId);
                        if (isSemantic)
                        {
                            tx.HasSemanticUpgradeOrReplace = true;
                            before.IsIncidentalBuildAdjustment = false;
                        }
                        else
                        {
                            before.IsIncidentalBuildAdjustment = true;
                        }
                        tx.InPlaceNetPatches[orig] = before;
                    }
                }
                tx.PendingModifiedInPlace.Clear();
            }
        }

        private static bool HasNetMutatedInPlace(EntityManager em, Entity entity, in InPlaceNetRecord before)
        {
            Entity livePrefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (livePrefab != before.Prefab)
            {
                return true;
            }

            bool liveHasUpgraded = em.HasComponent<Upgraded>(entity);
            if (liveHasUpgraded != before.HasUpgraded)
            {
                return true;
            }
            if (liveHasUpgraded)
            {
                CompositionFlags liveFlags = em.GetComponentData<Upgraded>(entity).m_Flags;
                if (liveFlags.m_General != before.UpgradedFlags.m_General ||
                    liveFlags.m_Left != before.UpgradedFlags.m_Left ||
                    liveFlags.m_Right != before.UpgradedFlags.m_Right)
                {
                    return true;
                }
            }

            bool liveHasElev = em.HasComponent<NetElevation>(entity);
            if (liveHasElev != before.HasElevation)
            {
                return true;
            }
            if (liveHasElev)
            {
                float2 liveElev = em.GetComponentData<NetElevation>(entity).m_Elevation;
                if (math.distancesq(liveElev, before.Elevation) > 1e-4f)
                {
                    return true;
                }
            }

            if (before.HasEdge && em.HasComponent<Edge>(entity))
            {
                Edge liveEdge = em.GetComponentData<Edge>(entity);
                if (liveEdge.m_Start != before.EdgeStart || liveEdge.m_End != before.EdgeEnd)
                {
                    return true;
                }
            }

            if (before.HasCurve && em.HasComponent<Curve>(entity))
            {
                Bezier4x3 liveCurve = em.GetComponentData<Curve>(entity).m_Bezier;
                if (math.distancesq(liveCurve.a, before.Curve.a) > 1e-4f ||
                    math.distancesq(liveCurve.b, before.Curve.b) > 1e-4f ||
                    math.distancesq(liveCurve.c, before.Curve.c) > 1e-4f ||
                    math.distancesq(liveCurve.d, before.Curve.d) > 1e-4f)
                {
                    return true;
                }
            }

            if (before.IsNode && em.HasComponent<NetNode>(entity))
            {
                NetNode liveNode = em.GetComponentData<NetNode>(entity);
                if (math.distancesq(liveNode.m_Position, before.NodePosition) > 1e-4f ||
                    math.abs(math.dot(liveNode.m_Rotation.value, before.NodeRotation.value)) < 0.9999f)
                {
                    return true;
                }
            }

            bool liveHasSubRep = em.HasBuffer<SubReplacement>(entity);
            if (liveHasSubRep != before.HasSubReplacements)
            {
                return true;
            }
            if (liveHasSubRep)
            {
                DynamicBuffer<SubReplacement> liveBuf = em.GetBuffer<SubReplacement>(entity, isReadOnly: true);
                int beforeLen = before.SubReplacements != null ? before.SubReplacements.Length : 0;
                if (liveBuf.Length != beforeLen)
                {
                    return true;
                }
                for (int i = 0; i < beforeLen; i++)
                {
                    if (!liveBuf[i].Equals(before.SubReplacements[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void MergeSnapshotInto(GlobalUndoTransaction tx, DeletionUndoManager.DetachedSnapshot src)
        {
            tx.DeletedSnapshot = DeletionUndoManager.MergeDetachedSnapshots(tx.DeletedSnapshot, src);
        }

        private static void CaptureCreatedEntitiesPostApply(
            EntityManager em,
            List<Entity> created,
            GlobalUndoTransaction tx,
            HashSet<Entity> reconciledSkipCreated)
        {
            if (created == null || created.Count == 0)
            {
                return;
            }

            var batchSet = new HashSet<Entity>(created.Count);
            for (int i = 0; i < created.Count; i++)
            {
                batchSet.Add(created[i]);
            }

            for (int i = 0; i < created.Count; i++)
            {
                Entity e = created[i];
                if (e == Entity.Null || !em.Exists(e) || em.HasComponent<Deleted>(e) || em.HasComponent<Temp>(e))
                {
                    continue;
                }

                if (reconciledSkipCreated != null && reconciledSkipCreated.Contains(e))
                {
                    continue;
                }

                if (tx.CreatedEntitySet.Contains(e))
                {
                    continue;
                }

                // Exclude volatile/simulation entities, lanes, standalone net nodes (Cascade handles nodes),
                // and protected map tiles/districts.
                if (em.HasComponent<Vehicle>(e) || em.HasComponent<Creature>(e) || em.HasComponent<Moving>(e) ||
                    em.HasComponent<Lane>(e) || em.HasComponent<MapTile>(e) || em.HasComponent<District>(e) ||
                    em.HasComponent<Block>(e) || em.HasComponent<Game.Tools.Brush>(e))
                {
                    continue;
                }

                if (em.HasComponent<NetNode>(e) && !em.HasComponent<Edge>(e))
                {
                    continue;
                }

                // Must be a recognized static buildable entity
                bool isStaticBuildable =
                    em.HasComponent<Edge>(e) ||
                    em.HasComponent<Building>(e) ||
                    em.HasComponent<Area>(e) ||
                    em.HasComponent<GameObject>(e) ||
                    em.HasComponent<Game.Buildings.ServiceUpgrade>(e);
                if (!isStaticBuildable)
                {
                    continue;
                }

                // Skip sub-elements whose owner was ALSO created in the same stroke (Cascade deletes them with root).
                // Keep newly added building upgrades whose Owner is an existing pre-built building!
                bool hasOwner = em.HasComponent<Owner>(e);
                Entity ownerEntity = Entity.Null;
                if (hasOwner)
                {
                    ownerEntity = em.GetComponentData<Owner>(e).m_Owner;
                    if (ownerEntity != Entity.Null &&
                        (batchSet.Contains(ownerEntity) || tx.CreatedEntitySet.Contains(ownerEntity)))
                    {
                        continue;
                    }
                    // Non-upgrade owned child (e.g. auto-spawned sub-net/sub-area/sub-object of an existing building)
                    // is only tracked if it's an InstalledUpgrade or ServiceUpgrade.
                    if (!em.HasComponent<Game.Buildings.ServiceUpgrade>(e))
                    {
                        continue;
                    }
                }

                if (tx.CreatedEntities.Count >= DeletionUndoManager.MaxEntitiesPerSnapshot)
                {
                    break;
                }

                Entity prefab = em.HasComponent<PrefabRef>(e) ? em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;
                float3 pos = float3.zero;
                if (em.HasComponent<Game.Objects.Transform>(e))
                {
                    pos = em.GetComponentData<Game.Objects.Transform>(e).m_Position;
                }
                else if (em.HasComponent<Curve>(e))
                {
                    Bezier4x3 c = em.GetComponentData<Curve>(e).m_Bezier;
                    pos = 0.125f * c.a + 0.375f * c.b + 0.375f * c.c + 0.125f * c.d;
                }

                tx.CreatedEntitySet.Add(e);
                tx.CreatedEntities.Add(new CreatedEntityRecord
                {
                    Entity = e,
                    Prefab = prefab,
                    Position = pos,
                    HasOwner = hasOwner,
                    OwnerEntity = ownerEntity,
                });
            }
        }

        private bool IsOpenStrokeTrackableNow()
        {
            if (m_OpenStroke == null || !m_OpenStroke.HasAnyPayload)
            {
                return false;
            }
            GlobalUndoTrackOptions opts = ReadTrackOptions();
            GlobalUndoActionKind kind = ClassifyTransactionKind(m_OpenStroke);
            return kind != GlobalUndoActionKind.None && GlobalUndoPolicy.IsKindTracked(in opts, kind);
        }

        public bool CanUndo(UndoAssetMask currentBulkMask)
        {
            if (IsPaused)
            {
                return true;
            }
            if (m_ActiveTx != null)
            {
                return false;
            }

            GlobalUndoTransaction top = IsOpenStrokeTrackableNow()
                ? m_OpenStroke
                : m_History.Peek();
            if (top == null)
            {
                return false;
            }

            if (top.IsBulkDelete)
            {
                return top.DeletedSnapshot != null && top.DeletedSnapshot.Counts.GetFilteredSum(currentBulkMask) > 0;
            }

            return top.HasAnyPayload;
        }

        public bool StartOrResumeUndo(UndoAssetMask bulkMask, DeletionUndoManager deletionUndo)
        {
            if (m_ActiveTx != null)
            {
                if (!m_ActivePaused)
                {
                    return false;
                }

                m_ActivePaused = false;
                ClearFinishedNotice();
                m_CaptureFailedCount = 0;
                if (m_ActiveUsingDeletionUndo && deletionUndo != null)
                {
                    deletionUndo.StartUndo(m_ActiveMask);
                }
                m_Notice = m_ActiveTx.DeletedSnapshot != null && m_ActiveTx.DeletedSnapshot.Truncated
                    ? "UndoPartial"
                    : (m_ActiveTx.DeletedSnapshot != null ? "UndoGeometryOnly" : string.Empty);
                return true;
            }

            FlushOpenStroke();
            GlobalUndoTransaction top = m_History.Peek();
            if (top == null)
            {
                return false;
            }

            UndoAssetMask effectiveMask = top.IsBulkDelete ? bulkMask : UndoAssetMask.All;
            int delWork = top.DeletedSnapshot != null ? top.DeletedSnapshot.Counts.GetFilteredSum(effectiveMask) : 0;
            if (top.IsBulkDelete && delWork <= 0)
            {
                return false;
            }

            int createdWork = top.CreatedEntities.Count;
            int inPlaceWork = top.InPlaceNetPatches.Count;
            int zoneWork = top.ZoneChanges.Count;
            int terrainWork = top.TerrainTileCount;
            int totalWork = createdWork + delWork + inPlaceWork + zoneWork + terrainWork;
            if (totalWork <= 0)
            {
                return false;
            }

            m_History.Pop();
            MarkHistoryChanged();
            ClearFinishedNotice();
            m_CaptureFailedCount = 0;

            m_ActiveTx = top;
            m_ActiveMask = effectiveMask;
            m_ActivePaused = false;
            m_ActiveUsingDeletionUndo = false;
            m_ActiveTotalWork = totalWork;
            m_ActiveCompletedWork = 0;
            m_ActiveSkippedWork = 0;
            m_ActiveWaitFrames = 0;
            m_ActiveDeletedSubTotal = delWork;
            m_PendingInPlaceRefreshNetPositions.Clear();

            m_ActivePhases = GlobalUndoPolicy.PlanExecutionPhases(
                hasCreated: createdWork > 0,
                hasDeleted: delWork > 0,
                hasInPlaceNets: inPlaceWork > 0,
                hasZone: zoneWork > 0,
                hasTerrain: terrainWork > 0);
            m_ActivePhaseIndex = 0;

            m_Notice = (top.DeletedSnapshot != null && top.DeletedSnapshot.Truncated) ||
                       (top.TerrainPatch != null && top.TerrainPatch.Truncated)
                ? "UndoPartial"
                : (delWork > 0 ? "UndoGeometryOnly" : string.Empty);
            return true;
        }

        public bool PauseActiveUndo(DeletionUndoManager deletionUndo)
        {
            if (m_ActiveTx == null || m_ActivePaused)
            {
                return false;
            }

            m_ActivePaused = true;
            m_Notice = "UndoPaused";
            if (m_ActiveUsingDeletionUndo && deletionUndo != null)
            {
                deletionUndo.CancelActiveUndo();
            }
            return true;
        }

        public bool StopActiveUndo(DeletionUndoManager deletionUndo)
        {
            if (m_ActiveTx == null)
            {
                return false;
            }

            int done = m_ActiveCompletedWork;
            int skipped = m_ActiveSkippedWork;
            if (m_ActiveUsingDeletionUndo && deletionUndo != null)
            {
                SelectiveUndoUiSnapshot delUi = deletionUndo.GetUiSnapshot(m_ActiveMask);
                done += delUi.RestoredCount;
                skipped += delUi.SkippedCount;
                deletionUndo.StopActiveUndo();
            }

            m_FinishedRestoredCount = done;
            m_FinishedSkippedCount = skipped;
            m_FinishedTotalCount = m_ActiveTotalWork;
            m_FinishedNotCapturedCount = m_ActiveTx.DeletedSnapshot != null
                ? m_ActiveTx.DeletedSnapshot.NotCaptured.GetFilteredSum(m_ActiveMask)
                : 0;
            m_FinishedNotice = "UndoStopped";

            ClearActiveReplayState(deletionUndo, clearHistory: false);
            RefreshIdleNotice();
            return true;
        }

        private void ClearActiveReplayState(DeletionUndoManager deletionUndo, bool clearHistory, bool clearPendingRefresh = true)
        {
            m_ActiveTx = null;
            m_ActivePhases = null;
            m_ActivePhaseIndex = 0;
            m_ActiveMask = UndoAssetMask.All;
            m_ActivePaused = false;
            m_ActiveUsingDeletionUndo = false;
            m_ActiveTotalWork = 0;
            m_ActiveCompletedWork = 0;
            m_ActiveSkippedWork = 0;
            m_ActiveWaitFrames = 0;
            m_ActiveDeletedSubTotal = 0;
            if (clearPendingRefresh)
            {
                m_PendingInPlaceRefreshNetPositions.Clear();
            }
            if (clearHistory && deletionUndo != null)
            {
                deletionUndo.ClearHistory();
            }
        }

        public bool TickReplay(
            EntityManager em,
            ToolOutputBarrier barrier,
            TerrainSystem terrainSystem,
            WaterSystem waterSystem,
            Game.Objects.SearchSystem objSearch,
            Game.Net.SearchSystem netSearch,
            DeletionUndoManager deletionUndo,
            out bool completed,
            bool isLeftHandTraffic = false)
        {
            completed = false;
            if (m_ActiveTx == null || m_ActivePaused)
            {
                return false;
            }

            if (!Compatibility.CanUndoRebuild || !Compatibility.CanSubmitDeletion)
            {
                FailActiveReplay(deletionUndo, "UndoUnavailable");
                completed = true;
                return true;
            }

            if (m_ActivePhases == null || m_ActivePhaseIndex >= m_ActivePhases.Count)
            {
                bool ownerTimeout = m_ActiveTx.DeletedSnapshot != null && m_ActiveTx.DeletedSnapshot.HasOwnerTimeout;
                CompleteActiveReplay(deletionUndo, ownerTimeout);
                completed = true;
                return true;
            }

            GlobalUndoExecutionPhase phase = m_ActivePhases[m_ActivePhaseIndex];
            switch (phase)
            {
                case GlobalUndoExecutionPhase.DeletingCreated:
                {
                    ExecuteDeleteCreatedPhase(em, barrier.CreateCommandBuffer(), m_ActiveTx, m_PendingInPlaceRefreshNetPositions, out int submitted, out int skipped);
                    m_ActiveCompletedWork += submitted;
                    m_ActiveSkippedWork += skipped;
                    m_ActivePhaseIndex++;
                    if (m_ActivePhaseIndex < m_ActivePhases.Count &&
                        m_ActivePhases[m_ActivePhaseIndex] == GlobalUndoExecutionPhase.WaitingPostDelete)
                    {
                        m_ActiveWaitFrames = 2;
                    }
                    break;
                }

                case GlobalUndoExecutionPhase.WaitingPostDelete:
                {
                    if (m_ActiveWaitFrames > 0)
                    {
                        m_ActiveWaitFrames--;
                        return true;
                    }
                    m_ActivePhaseIndex++;
                    break;
                }

                case GlobalUndoExecutionPhase.RestoringDeleted:
                {
                    if (!m_ActiveUsingDeletionUndo)
                    {
                        if (m_ActiveTx.DeletedSnapshot != null &&
                            deletionUndo.StartDetachedUndo(m_ActiveTx.DeletedSnapshot, m_ActiveMask))
                        {
                            m_ActiveUsingDeletionUndo = true;
                        }
                        else
                        {
                            m_ActiveSkippedWork += m_ActiveDeletedSubTotal;
                            m_ActivePhaseIndex++;
                            break;
                        }
                    }

                    deletionUndo.Step(em, barrier.CreateCommandBuffer(), terrainSystem, waterSystem, isLeftHandTraffic, out bool delCompleted);
                    if (!delCompleted)
                    {
                        return true;
                    }

                    SelectiveUndoUiSnapshot delUi = deletionUndo.GetUiSnapshot(m_ActiveMask);
                    m_ActiveCompletedWork += delUi.FinishedRestoredCount;
                    m_ActiveSkippedWork += delUi.FinishedSkippedCount;
                    m_ActiveUsingDeletionUndo = false;
                    m_ActivePhaseIndex++;

                    if (delUi.FinishedNotice == "UndoUnavailable" || delUi.FinishedNotice == "UndoError")
                    {
                        FailActiveReplay(deletionUndo, delUi.FinishedNotice);
                        completed = true;
                        return true;
                    }
                    break;
                }

                case GlobalUndoExecutionPhase.RestoringInPlaceNets:
                {
                    ExecuteInPlaceNetsPhase(
                        em,
                        barrier.CreateCommandBuffer(),
                        m_ActiveTx,
                        m_PendingInPlaceRefreshNetPositions,
                        out int restored,
                        out int skipped);
                    m_ActiveCompletedWork += restored;
                    m_ActiveSkippedWork += skipped;
                    m_ActivePhaseIndex++;
                    if (m_ActivePhaseIndex < m_ActivePhases.Count &&
                        m_ActivePhases[m_ActivePhaseIndex] == GlobalUndoExecutionPhase.WaitingPostInPlaceNets)
                    {
                        m_ActiveWaitFrames = 2;
                    }
                    break;
                }

                case GlobalUndoExecutionPhase.WaitingPostInPlaceNets:
                {
                    if (m_ActiveWaitFrames > 0)
                    {
                        m_ActiveWaitFrames--;
                        return true;
                    }
                    m_ActivePhaseIndex++;
                    break;
                }

                case GlobalUndoExecutionPhase.RestoringZoneAndTerrain:
                {
                    EntityCommandBuffer ecb = barrier.CreateCommandBuffer();
                    if (m_ActiveTx.ZoneChanges.Count > 0)
                    {
                        ExecuteZoneRestorePhase(em, ecb, m_ActiveTx, out int zRestored, out int zSkipped);
                        m_ActiveCompletedWork += zRestored;
                        m_ActiveSkippedWork += zSkipped;
                    }
                    if (m_ActiveTx.TerrainPatch != null && m_ActiveTx.TerrainPatch.TileCount > 0)
                    {
                        ExecuteTerrainRestorePhase(em, ecb, terrainSystem, waterSystem, objSearch, netSearch, m_ActiveTx.TerrainPatch, out int tRestored, out int tSkipped);
                        m_ActiveCompletedWork += tRestored;
                        m_ActiveSkippedWork += tSkipped;
                    }
                    m_ActivePhaseIndex++;
                    break;
                }
            }

            if (m_ActivePhaseIndex >= m_ActivePhases.Count)
            {
                bool ownerTimeout = m_ActiveTx.DeletedSnapshot != null && m_ActiveTx.DeletedSnapshot.HasOwnerTimeout;
                CompleteActiveReplay(deletionUndo, ownerTimeout);
                completed = true;
            }
            return true;
        }

        private void CompleteActiveReplay(DeletionUndoManager deletionUndo, bool hasOwnerTimeout)
        {
            GlobalUndoTransaction tx = m_ActiveTx;
            int notCaptured = tx != null && tx.DeletedSnapshot != null
                ? tx.DeletedSnapshot.NotCaptured.GetFilteredSum(m_ActiveMask)
                : 0;
            bool truncated = (tx?.DeletedSnapshot?.Truncated ?? false) || (tx?.TerrainPatch?.Truncated ?? false);

            m_FinishedRestoredCount = m_ActiveCompletedWork;
            m_FinishedSkippedCount = m_ActiveSkippedWork;
            m_FinishedTotalCount = m_ActiveTotalWork;
            m_FinishedNotCapturedCount = notCaptured;
            m_FinishedNotice = hasOwnerTimeout
                ? "UndoStalled"
                : (m_ActiveSkippedWork > 0 || truncated || notCaptured > 0)
                    ? "UndoSubmittedPartial"
                    : "UndoSubmitted";

            ClearActiveReplayState(deletionUndo, clearHistory: false, clearPendingRefresh: false);
            RefreshIdleNotice();
        }

        private void FailActiveReplay(DeletionUndoManager deletionUndo, string reason)
        {
            m_FinishedRestoredCount = m_ActiveCompletedWork;
            m_FinishedSkippedCount = m_ActiveSkippedWork;
            m_FinishedTotalCount = m_ActiveTotalWork;
            m_FinishedNotCapturedCount = m_ActiveTx?.DeletedSnapshot != null
                ? m_ActiveTx.DeletedSnapshot.NotCaptured.GetFilteredSum(m_ActiveMask)
                : 0;
            m_FinishedNotice = string.IsNullOrEmpty(reason) ? "UndoError" : reason;

            ClearActiveReplayState(deletionUndo, clearHistory: false, clearPendingRefresh: false);
            RefreshIdleNotice();
        }

        private static bool IsHardProtected(EntityManager em, Entity entity)
        {
            return em.HasComponent<MapTile>(entity) ||
                   em.HasComponent<District>(entity) ||
                   em.HasComponent<Game.Objects.OutsideConnection>(entity) ||
                   em.HasComponent<Game.Net.OutsideConnection>(entity) ||
                   em.HasComponent<Game.Objects.ElectricityOutsideConnection>(entity) ||
                   em.HasComponent<Game.Objects.WaterPipeOutsideConnection>(entity);
        }

        private static bool TryResolveRebuiltCreatedEntity(
            EntityManager em,
            in CreatedEntityRecord rec,
            HashSet<Entity> resolvedClaimed,
            out Entity resolved)
        {
            resolved = Entity.Null;
            if (rec.Prefab == Entity.Null || !em.Exists(rec.Prefab))
            {
                return false;
            }

            EntityQuery query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Vehicle>(),
                    ComponentType.ReadOnly<Creature>(),
                    ComponentType.ReadOnly<Moving>(),
                    ComponentType.ReadOnly<Lane>(),
                },
            });

            int matchCount = 0;
            Entity matched = Entity.Null;
            try
            {
                using (NativeArray<Entity> candidates = query.ToEntityArray(Allocator.Temp))
                {
                    const float maxDistSq = 0.5f * 0.5f;
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        Entity cand = candidates[i];
                        if (cand == Entity.Null || !em.Exists(cand) ||
                            (resolvedClaimed != null && resolvedClaimed.Contains(cand)) ||
                            IsHardProtected(em, cand))
                        {
                            continue;
                        }

                        if (em.GetComponentData<PrefabRef>(cand).m_Prefab != rec.Prefab)
                        {
                            continue;
                        }

                        if (!rec.HasOwner)
                        {
                            if (em.HasComponent<Owner>(cand))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (!em.HasComponent<Owner>(cand) ||
                                em.GetComponentData<Owner>(cand).m_Owner != rec.OwnerEntity)
                            {
                                continue;
                            }
                        }

                        float3 candPos;
                        if (em.HasComponent<Game.Objects.Transform>(cand))
                        {
                            candPos = em.GetComponentData<Game.Objects.Transform>(cand).m_Position;
                        }
                        else if (em.HasComponent<Curve>(cand))
                        {
                            Bezier4x3 c = em.GetComponentData<Curve>(cand).m_Bezier;
                            candPos = 0.125f * c.a + 0.375f * c.b + 0.375f * c.c + 0.125f * c.d;
                        }
                        else
                        {
                            continue;
                        }

                        if (math.distancesq(candPos, rec.Position) > maxDistSq)
                        {
                            continue;
                        }

                        matchCount++;
                        if (matchCount > 1)
                        {
                            return false;
                        }
                        matched = cand;
                    }
                }
            }
            finally
            {
                query.Dispose();
            }

            if (matchCount == 1 && matched != Entity.Null)
            {
                resolved = matched;
                return true;
            }
            return false;
        }

        private static void ExecuteDeleteCreatedPhase(
            EntityManager em,
            EntityCommandBuffer ecb,
            GlobalUndoTransaction tx,
            List<float3> refreshNetPositions,
            out int submitted,
            out int skipped)
        {
            submitted = 0;
            skipped = 0;
            var liveRoots = new List<Entity>(tx.CreatedEntities.Count);
            var parentOwnersToRefresh = new HashSet<Entity>();
            var resolvedClaimed = new HashSet<Entity>();

            for (int i = 0; i < tx.CreatedEntities.Count; i++)
            {
                Entity existing = tx.CreatedEntities[i].Entity;
                if (existing != Entity.Null && em.Exists(existing) && !em.HasComponent<Deleted>(existing))
                {
                    resolvedClaimed.Add(existing);
                }
            }

            for (int i = 0; i < tx.CreatedEntities.Count; i++)
            {
                CreatedEntityRecord rec = tx.CreatedEntities[i];
                Entity target = rec.Entity;
                if (target == Entity.Null || !em.Exists(target) || em.HasComponent<Deleted>(target))
                {
                    if (rec.Prefab != Entity.Null && em.Exists(rec.Prefab) &&
                        TryResolveRebuiltCreatedEntity(em, in rec, resolvedClaimed, out Entity rebuiltEntity))
                    {
                        target = rebuiltEntity;
                    }
                    else
                    {
                        skipped++;
                        continue;
                    }
                }
                else if (IsHardProtected(em, target))
                {
                    skipped++;
                    continue;
                }

                resolvedClaimed.Add(target);
                liveRoots.Add(target);
                if (rec.HasOwner && rec.OwnerEntity != Entity.Null &&
                    em.Exists(rec.OwnerEntity) && !em.HasComponent<Deleted>(rec.OwnerEntity))
                {
                    parentOwnersToRefresh.Add(rec.OwnerEntity);
                }
            }

            if (liveRoots.Count == 0)
            {
                return;
            }

            DeletionPlan plan = Cascade.Plan(em, liveRoots);
            Cascade.Submit(em, ecb, plan, out _);

            foreach (Entity owner in parentOwnersToRefresh)
            {
                if (!plan.Submitted.Contains(owner) && !plan.Neighbors.Contains(owner) &&
                    !em.HasComponent<Deleted>(owner) && !em.HasComponent<Updated>(owner))
                {
                    ecb.AddComponent<Updated>(owner);
                }
            }

            if (tx.PreservedSplitRefreshTargets != null && tx.PreservedSplitRefreshTargets.Count > 0)
            {
                var refreshed = new HashSet<Entity>();
                for (int i = 0; i < tx.PreservedSplitRefreshTargets.Count; i++)
                {
                    Entity target = tx.PreservedSplitRefreshTargets[i];
                    if (target == Entity.Null || !refreshed.Add(target) ||
                        !em.Exists(target) || em.HasComponent<Deleted>(target) ||
                        plan.Submitted.Contains(target))
                    {
                        continue;
                    }

                    if (!plan.Neighbors.Contains(target) && !em.HasComponent<Updated>(target))
                    {
                        ecb.AddComponent<Updated>(target);
                    }

                    if (refreshNetPositions != null &&
                        em.HasComponent<Curve>(target) && em.HasComponent<Edge>(target) &&
                        !em.HasComponent<Owner>(target))
                    {
                        Bezier4x3 c = em.GetComponentData<Curve>(target).m_Bezier;
                        float3 mid = 0.125f * c.a + 0.375f * c.b + 0.375f * c.c + 0.125f * c.d;
                        refreshNetPositions.Add(mid);
                    }
                }
            }

            submitted = liveRoots.Count;
        }

        private static void ExecuteInPlaceNetsPhase(
            EntityManager em,
            EntityCommandBuffer ecb,
            GlobalUndoTransaction tx,
            List<float3> restoredRoadMidpoints,
            out int restored,
            out int skipped)
        {
            restored = 0;
            skipped = 0;
            var toUpdate = new HashSet<Entity>();

            foreach (var kv in tx.InPlaceNetPatches)
            {
                InPlaceNetRecord rec = kv.Value;
                Entity e = rec.Entity;
                if (e == Entity.Null || !em.Exists(e) || em.HasComponent<Deleted>(e) ||
                    rec.Prefab == Entity.Null || !em.Exists(rec.Prefab) || em.HasComponent<Deleted>(rec.Prefab))
                {
                    skipped++;
                    continue;
                }

                // Combine-shrink topology safety check & far-end node relocation
                if (rec.MoveNodeEntity != Entity.Null)
                {
                    Entity moveNode = rec.MoveNodeEntity;
                    if (!em.Exists(moveNode) || em.HasComponent<Deleted>(moveNode) || !em.HasComponent<NetNode>(moveNode))
                    {
                        skipped++;
                        continue;
                    }

                    if (rec.HasEdge)
                    {
                        if (!em.HasComponent<Edge>(e))
                        {
                            skipped++;
                            continue;
                        }
                        Edge combineLiveEdge = em.GetComponentData<Edge>(e);
                        bool sameEndpoints =
                            (combineLiveEdge.m_Start == rec.EdgeStart && combineLiveEdge.m_End == rec.EdgeEnd) ||
                            GlobalUndoPolicy.ShouldRestoreReversedEdgeEndpoints(
                                rec.EdgeStart.Index, rec.EdgeStart.Version,
                                rec.EdgeEnd.Index, rec.EdgeEnd.Version,
                                combineLiveEdge.m_Start.Index, combineLiveEdge.m_Start.Version,
                                combineLiveEdge.m_End.Index, combineLiveEdge.m_End.Version);
                        if (!sameEndpoints)
                        {
                            skipped++;
                            continue;
                        }
                    }

                    if (rec.RequireSingleConnectedEdge)
                    {
                        if (!em.HasBuffer<ConnectedEdge>(moveNode))
                        {
                            skipped++;
                            continue;
                        }

                        DynamicBuffer<ConnectedEdge> conn = em.GetBuffer<ConnectedEdge>(moveNode, isReadOnly: true);
                        bool hasForeignLiveEdge = false;
                        int liveEdgeCount = 0;
                        for (int c = 0; c < conn.Length; c++)
                        {
                            Entity ce = conn[c].m_Edge;
                            if (ce == Entity.Null || !em.Exists(ce) || em.HasComponent<Deleted>(ce) || em.HasComponent<Temp>(ce))
                            {
                                continue;
                            }
                            liveEdgeCount++;
                            if (ce != e)
                            {
                                hasForeignLiveEdge = true;
                                break;
                            }
                        }

                        if (hasForeignLiveEdge || liveEdgeCount != 1)
                        {
                            skipped++;
                            continue;
                        }
                    }

                    ecb.SetComponent(moveNode, new NetNode
                    {
                        m_Position = rec.TargetNodePosition,
                        m_Rotation = rec.TargetNodeRotation,
                    });

                    if (rec.TargetNodePrefab != Entity.Null &&
                        em.Exists(rec.TargetNodePrefab) && !em.HasComponent<Deleted>(rec.TargetNodePrefab) &&
                        em.HasComponent<PrefabRef>(moveNode))
                    {
                        ecb.SetComponent(moveNode, new PrefabRef { m_Prefab = rec.TargetNodePrefab });
                    }

                    if (rec.TargetNodeHasUpgraded)
                    {
                        if (em.HasComponent<Upgraded>(moveNode))
                        {
                            ecb.SetComponent(moveNode, new Upgraded { m_Flags = rec.TargetNodeUpgradedFlags });
                        }
                        else
                        {
                            ecb.AddComponent(moveNode, new Upgraded { m_Flags = rec.TargetNodeUpgradedFlags });
                        }
                    }
                    else if (em.HasComponent<Upgraded>(moveNode))
                    {
                        ecb.RemoveComponent<Upgraded>(moveNode);
                    }

                    if (rec.TargetNodeHasElevation)
                    {
                        if (em.HasComponent<NetElevation>(moveNode))
                        {
                            ecb.SetComponent(moveNode, new NetElevation { m_Elevation = rec.TargetNodeElevation });
                        }
                        else
                        {
                            ecb.AddComponent(moveNode, new NetElevation { m_Elevation = rec.TargetNodeElevation });
                        }
                    }
                    else if (em.HasComponent<NetElevation>(moveNode))
                    {
                        ecb.RemoveComponent<NetElevation>(moveNode);
                    }

                    toUpdate.Add(moveNode);
                }

                if (em.HasComponent<PrefabRef>(e))
                {
                    ecb.SetComponent(e, new PrefabRef { m_Prefab = rec.Prefab });
                }

                if (rec.HasUpgraded)
                {
                    if (em.HasComponent<Upgraded>(e))
                    {
                        ecb.SetComponent(e, new Upgraded { m_Flags = rec.UpgradedFlags });
                    }
                    else
                    {
                        ecb.AddComponent(e, new Upgraded { m_Flags = rec.UpgradedFlags });
                    }
                }
                else if (em.HasComponent<Upgraded>(e))
                {
                    ecb.RemoveComponent<Upgraded>(e);
                }

                if (rec.HasElevation)
                {
                    if (em.HasComponent<NetElevation>(e))
                    {
                        ecb.SetComponent(e, new NetElevation { m_Elevation = rec.Elevation });
                    }
                    else
                    {
                        ecb.AddComponent(e, new NetElevation { m_Elevation = rec.Elevation });
                    }
                }
                else if (em.HasComponent<NetElevation>(e))
                {
                    ecb.RemoveComponent<NetElevation>(e);
                }

                if (rec.HasCurve && em.HasComponent<Curve>(e))
                {
                    ecb.SetComponent(e, new Curve { m_Bezier = rec.Curve, m_Length = rec.CurveLength });
                }

                if (rec.HasEdge && em.HasComponent<Edge>(e))
                {
                    Edge liveEdge = em.GetComponentData<Edge>(e);
                    if (rec.EdgeStart != Entity.Null && rec.EdgeEnd != Entity.Null && rec.EdgeStart != rec.EdgeEnd &&
                        em.Exists(rec.EdgeStart) && !em.HasComponent<Deleted>(rec.EdgeStart) &&
                        em.Exists(rec.EdgeEnd) && !em.HasComponent<Deleted>(rec.EdgeEnd) &&
                        GlobalUndoPolicy.ShouldRestoreReversedEdgeEndpoints(
                            rec.EdgeStart.Index, rec.EdgeStart.Version,
                            rec.EdgeEnd.Index, rec.EdgeEnd.Version,
                            liveEdge.m_Start.Index, liveEdge.m_Start.Version,
                            liveEdge.m_End.Index, liveEdge.m_End.Version))
                    {
                        ecb.SetComponent(e, new Edge { m_Start = rec.EdgeStart, m_End = rec.EdgeEnd });
                    }
                    if (liveEdge.m_Start != Entity.Null) toUpdate.Add(liveEdge.m_Start);
                    if (liveEdge.m_End != Entity.Null) toUpdate.Add(liveEdge.m_End);
                    if (rec.EdgeStart != Entity.Null) toUpdate.Add(rec.EdgeStart);
                    if (rec.EdgeEnd != Entity.Null) toUpdate.Add(rec.EdgeEnd);
                }

                if (rec.IsNode && em.HasComponent<NetNode>(e))
                {
                    ecb.SetComponent(e, new NetNode { m_Position = rec.NodePosition, m_Rotation = rec.NodeRotation });
                    if (em.HasBuffer<ConnectedEdge>(e))
                    {
                        DynamicBuffer<ConnectedEdge> connEdges = em.GetBuffer<ConnectedEdge>(e, isReadOnly: true);
                        for (int c = 0; c < connEdges.Length; c++)
                        {
                            Entity adj = connEdges[c].m_Edge;
                            if (adj == Entity.Null || !em.Exists(adj) || em.HasComponent<Deleted>(adj) || em.HasComponent<Temp>(adj))
                            {
                                continue;
                            }
                            toUpdate.Add(adj);
                            if (em.HasComponent<Edge>(adj))
                            {
                                Edge adjEdge = em.GetComponentData<Edge>(adj);
                                if (adjEdge.m_Start != Entity.Null) toUpdate.Add(adjEdge.m_Start);
                                if (adjEdge.m_End != Entity.Null) toUpdate.Add(adjEdge.m_End);
                            }
                        }
                    }
                }

                if (rec.HasSubReplacements)
                {
                    DynamicBuffer<SubReplacement> dstBuf = em.HasBuffer<SubReplacement>(e)
                        ? ecb.SetBuffer<SubReplacement>(e)
                        : ecb.AddBuffer<SubReplacement>(e);
                    if (rec.SubReplacements != null)
                    {
                        for (int s = 0; s < rec.SubReplacements.Length; s++)
                        {
                            dstBuf.Add(rec.SubReplacements[s]);
                        }
                    }
                }
                else if (em.HasBuffer<SubReplacement>(e))
                {
                    ecb.RemoveComponent<SubReplacement>(e);
                }

                toUpdate.Add(e);
                if (restoredRoadMidpoints != null && rec.HasCurve && rec.HasEdge && !em.HasComponent<Owner>(e))
                {
                    float3 mid = 0.125f * rec.Curve.a + 0.375f * rec.Curve.b + 0.375f * rec.Curve.c + 0.125f * rec.Curve.d;
                    restoredRoadMidpoints.Add(mid);
                }
                restored++;
            }

            foreach (Entity u in toUpdate)
            {
                if (u != Entity.Null && em.Exists(u) && !em.HasComponent<Deleted>(u) && !em.HasComponent<Updated>(u))
                {
                    ecb.AddComponent<Updated>(u);
                }
            }
        }

        private static void ExecuteZoneRestorePhase(
            EntityManager em,
            EntityCommandBuffer ecb,
            GlobalUndoTransaction tx,
            out int restored,
            out int skipped)
        {
            restored = 0;
            skipped = 0;

            foreach (var kv in tx.ZoneChanges)
            {
                ZoneBlockSnapshot snap = kv.Value;
                Entity blockEnt = snap.BlockEntity;
                bool exists = blockEnt != Entity.Null && em.Exists(blockEnt) &&
                              !em.HasComponent<Deleted>(blockEnt) &&
                              em.HasComponent<Block>(blockEnt) &&
                              em.HasBuffer<Game.Zones.Cell>(blockEnt);

                int liveSizeX = 0, liveSizeY = 0, liveBufLen = 0;
                if (exists)
                {
                    Block liveBlock = em.GetComponentData<Block>(blockEnt);
                    liveSizeX = liveBlock.m_Size.x;
                    liveSizeY = liveBlock.m_Size.y;
                    liveBufLen = em.GetBuffer<Game.Zones.Cell>(blockEnt, isReadOnly: true).Length;
                }

                ZoneRestoreVerdict verdict = ZoneBlockValidationLogic.ValidateRestore(
                    exists,
                    snap.Size.x, snap.Size.y,
                    liveSizeX, liveSizeY,
                    snap.Cells != null ? snap.Cells.Length : 0,
                    liveBufLen);

                if (verdict != ZoneRestoreVerdict.Valid)
                {
                    skipped++;
                    continue;
                }

                DynamicBuffer<Game.Zones.Cell> buf = em.GetBuffer<Game.Zones.Cell>(blockEnt, isReadOnly: false);
                for (int i = 0; i < snap.Cells.Length; i++)
                {
                    buf[i] = snap.Cells[i];
                }

                if (!em.HasComponent<Updated>(blockEnt))
                {
                    ecb.AddComponent<Updated>(blockEnt);
                }
                if (!em.HasComponent<BatchesUpdated>(blockEnt))
                {
                    ecb.AddComponent<BatchesUpdated>(blockEnt);
                }
                restored++;
            }
        }

        private struct TerrainOverlapCollector : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public float MinX, MaxX, MinZ, MaxZ;
            public HashSet<Entity> Results;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return bounds.m_Bounds.max.x >= MinX && bounds.m_Bounds.min.x <= MaxX &&
                       bounds.m_Bounds.max.z >= MinZ && bounds.m_Bounds.min.z <= MaxZ;
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (Intersect(bounds) && item != Entity.Null)
                {
                    Results.Add(item);
                }
            }
        }

        private static void ExecuteTerrainRestorePhase(
            EntityManager em,
            EntityCommandBuffer ecb,
            TerrainSystem terrainSystem,
            WaterSystem waterSystem,
            Game.Objects.SearchSystem objSearch,
            Game.Net.SearchSystem netSearch,
            TerrainPatchData patch,
            out int restored,
            out int skipped)
        {
            restored = 0;
            skipped = 0;

            if (patch == null || patch.TileCount == 0 || terrainSystem == null || terrainSystem.heightmap == null)
            {
                skipped = patch != null ? patch.TileCount : 0;
                return;
            }

            List<TerrainCopyBatch> batches = null;
            bool readerAcquired = false;
            try
            {
                TerrainHeightData hData = terrainSystem.GetHeightData(waitForPending: true);
                readerAcquired = true;
                if (!hData.isCreated ||
                    hData.resolution.x != patch.ResolutionX ||
                    hData.resolution.z != patch.ResolutionZ)
                {
                    skipped = patch.TileCount;
                    return;
                }

                int resX = hData.resolution.x;
                NativeArray<ushort> liveHeights = hData.heights;
                batches = patch.BuildCopyBatches((px, pz) => liveHeights[pz * resX + px]);
            }
            finally
            {
                if (readerAcquired)
                {
                    terrainSystem.AddCPUHeightReader(default(JobHandle));
                }
            }

            if (batches == null || batches.Count == 0)
            {
                skipped = patch.TileCount;
                return;
            }

            Texture heightmap = terrainSystem.heightmap;
            for (int i = 0; i < batches.Count; i++)
            {
                TerrainCopyBatch batch = batches[i];
                if (batch.Width <= 0 || batch.Height <= 0 || batch.Pixels == null ||
                    batch.DstX < 0 || batch.DstY < 0 ||
                    batch.DstX + batch.Width > heightmap.width ||
                    batch.DstY + batch.Height > heightmap.height)
                {
                    continue;
                }

                Texture2D tempTex = null;
                try
                {
                    tempTex = new Texture2D(batch.Width, batch.Height, GraphicsFormat.R16_UNorm, TextureCreationFlags.None);
                    tempTex.SetPixelData(batch.Pixels, 0, 0);
                    tempTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                    Graphics.CopyTexture(tempTex, 0, 0, 0, 0, batch.Width, batch.Height, heightmap, 0, 0, batch.DstX, batch.DstY);
                }
                finally
                {
                    if (tempTex != null)
                    {
                        UnityEngine.Object.Destroy(tempTex);
                    }
                }
            }

            const float boundsPad = 8f;
            Bounds2 worldBounds = patch.HasBounds
                ? new Bounds2(
                    new float2(patch.BoundsMinX - boundsPad, patch.BoundsMinZ - boundsPad),
                    new float2(patch.BoundsMaxX + boundsPad, patch.BoundsMaxZ + boundsPad))
                : new Bounds2(new float2(-100f, -100f), new float2(100f, 100f));

            waterSystem?.TerrainWillChangeFromBrush(worldBounds);
            Bounds2 areaCopy = worldBounds;
            terrainSystem.OnAreaChanged(in areaCopy);
            // In normal gameplay, OnAreaChanged schedules UpdateCascades -> RenderCascades which copies
            // m_Heightmap into m_HeightmapCascade for the dirty viewport and then calls TriggerAsyncChange()
            // with the tight m_LastWrite rect. Only invoke TriggerAsyncChange() directly when cascades are frozen.
            if (GlobalUndoPolicy.ShouldTriggerTerrainAsyncChangeDirectly(terrainSystem.freezeCascadeUpdates))
            {
                terrainSystem.TriggerAsyncChange();
            }

            // Mark overlapping static objects & networks as Updated so they conform to restored ground height.
            if (patch.HasBounds)
            {
                var affected = new HashSet<Entity>();
                const float margin = 16f;
                float minX = patch.BoundsMinX - margin;
                float maxX = patch.BoundsMaxX + margin;
                float minZ = patch.BoundsMinZ - margin;
                float maxZ = patch.BoundsMaxZ + margin;

                if (netSearch != null)
                {
                    bool acquired = false;
                    try
                    {
                        var tree = netSearch.GetNetSearchTree(true, out JobHandle deps);
                        acquired = true;
                        deps.Complete();
                        var iter = new TerrainOverlapCollector { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, Results = affected };
                        tree.Iterate(ref iter);
                    }
                    finally
                    {
                        if (acquired) netSearch.AddNetSearchTreeReader(default(JobHandle));
                    }
                }

                if (objSearch != null)
                {
                    bool acquired = false;
                    try
                    {
                        var tree = objSearch.GetStaticSearchTree(true, out JobHandle deps);
                        acquired = true;
                        deps.Complete();
                        var iter = new TerrainOverlapCollector { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, Results = affected };
                        tree.Iterate(ref iter);
                    }
                    finally
                    {
                        if (acquired) objSearch.AddStaticSearchTreeReader(default(JobHandle));
                    }
                }

                foreach (Entity ent in affected)
                {
                    if (ent != Entity.Null && em.Exists(ent) &&
                        !em.HasComponent<Deleted>(ent) && !em.HasComponent<Temp>(ent) && !em.HasComponent<Updated>(ent))
                    {
                        ecb.AddComponent<Updated>(ent);
                    }
                }
            }

            restored = patch.TileCount;
        }

        public SelectiveUndoUiSnapshot GetSelectiveUiSnapshot(UndoAssetMask currentBulkMask, DeletionUndoManager deletionUndo)
        {
            bool hasActive = m_ActiveTx != null;
            bool paused = hasActive && m_ActivePaused;
            bool inProgress = hasActive && !m_ActivePaused;

            int activeRestored = m_ActiveCompletedWork;
            int activeSkipped = m_ActiveSkippedWork;
            string notice = m_Notice ?? string.Empty;

            if (hasActive && m_ActiveUsingDeletionUndo && deletionUndo != null)
            {
                SelectiveUndoUiSnapshot delUi = deletionUndo.GetUiSnapshot(m_ActiveMask);
                activeRestored += delUi.RestoredCount;
                activeSkipped += delUi.SkippedCount;
                if (!string.IsNullOrEmpty(delUi.Notice))
                {
                    notice = delUi.Notice;
                }
            }

            GlobalUndoTransaction source = m_ActiveTx ?? m_History.Peek();
            UndoAssetMask effectiveMask = hasActive ? m_ActiveMask : currentBulkMask;
            UndoCategoryCounts counts = source?.DeletedSnapshot != null ? source.DeletedSnapshot.Counts : default;

            int nextCount;
            if (hasActive)
            {
                int rem = m_ActiveTotalWork - activeRestored - activeSkipped;
                nextCount = rem > 0 ? rem : 0;
            }
            else if (source == null)
            {
                nextCount = 0;
            }
            else if (source.IsBulkDelete)
            {
                nextCount = source.DeletedSnapshot != null ? source.DeletedSnapshot.Counts.GetFilteredSum(currentBulkMask) : 0;
            }
            else
            {
                nextCount = source.TotalItemCount;
            }

            bool canUndo = CanUndo(currentBulkMask);
            bool zeroMatch = !hasActive && source != null && source.IsBulkDelete && nextCount == 0;
            int notCaptured = source?.DeletedSnapshot != null
                ? source.DeletedSnapshot.NotCaptured.GetFilteredSum(source.IsBulkDelete ? effectiveMask : UndoAssetMask.All)
                : 0;

            return new SelectiveUndoUiSnapshot
            {
                HasActiveUndo = hasActive,
                IsInProgress = inProgress,
                IsPaused = paused,
                CanUndo = canUndo,
                HistoryDepth = m_History.Count,
                ZeroMatch = zeroMatch,
                EffectiveMask = effectiveMask,
                Counts = counts,
                NextCount = nextCount,
                NotCapturedCount = notCaptured,
                RestoredCount = hasActive ? activeRestored : 0,
                SkippedCount = hasActive ? activeSkipped : 0,
                ActiveTotalCount = hasActive ? m_ActiveTotalWork : 0,
                Notice = notice,
                CaptureFailedCount = m_CaptureFailedCount,
                FinishedNotice = m_FinishedNotice ?? string.Empty,
                FinishedRestoredCount = m_FinishedRestoredCount,
                FinishedSkippedCount = m_FinishedSkippedCount,
                FinishedTotalCount = m_FinishedTotalCount,
                FinishedNotCapturedCount = m_FinishedNotCapturedCount,
            };
        }

        public GlobalUndoUiState GetGlobalUiState(ToolSystem toolSystem)
        {
            GlobalUndoTrackOptions opts = ReadTrackOptions();
            ToolBaseSystem activeTool = toolSystem?.activeTool;
            string activeToolId = activeTool?.toolID ?? string.Empty;
            string activeToolType = activeTool != null ? (activeTool.GetType().FullName ?? activeTool.GetType().Name) : string.Empty;
            bool yielding = GlobalUndoPolicy.ShouldYieldCtrlZ(activeToolId, activeToolType);

            if (m_CachedHistoryJsonRevision != m_HistoryRevision)
            {
                int n = m_History.Count;
                var summaries = new List<GlobalUndoStepSummary>(n);
                for (int i = 0; i < n; i++)
                {
                    GlobalUndoTransaction tx = m_History.GetNewestAt(i);
                    if (tx != null)
                    {
                        summaries.Add(tx.ToSummary());
                    }
                }
                m_CachedHistoryJson = GlobalUndoPolicy.SerializeHistoryJson(summaries, maxItems: 12);
                m_CachedHistoryJsonRevision = m_HistoryRevision;
            }

            GlobalUndoTransaction top = m_History.Peek();
            return new GlobalUndoUiState
            {
                GlobalUndoEnabled = opts.Enabled,
                TrackBuild = opts.TrackBuild,
                TrackBulldoze = opts.TrackBulldoze,
                TrackUpgrade = opts.TrackUpgrade,
                TrackZone = opts.TrackZone,
                TrackTerrain = opts.TrackTerrain,
                ExternalToolYielding = yielding,
                ExternalToolName = yielding ? (string.IsNullOrEmpty(activeToolId) ? activeToolType : activeToolId) : string.Empty,
                ActiveStepKind = m_ActiveTx != null ? GlobalUndoPolicy.FormatActionKindCode(m_ActiveTx.Kind) : "none",
                TopStepKind = top != null ? GlobalUndoPolicy.FormatActionKindCode(top.Kind) : "none",
                TopStepCount = top != null ? top.TotalItemCount : 0,
                HistoryDepth = m_History.Count,
                TerrainMemoryKB = (int)(m_History.TotalTerrainBytes / 1024L),
                HistoryRevision = m_HistoryRevision,
                HistoryJson = m_CachedHistoryJson,
            };
        }
    }

    [UpdateBefore(typeof(ApplyObjectsSystem))]
    [UpdateBefore(typeof(ApplyNetSystem))]
    [UpdateBefore(typeof(ApplyAreasSystem))]
    [UpdateBefore(typeof(ApplyZonesSystem))]
    [UpdateBefore(typeof(ApplyBrushesSystem))]
    public sealed class GlobalUndoPreApplySystem : GameSystemBase
    {
        private ToolSystem m_ToolSystem;
        private TerrainSystem m_TerrainSystem;
        private MarqueeDeleteToolSystem m_MarqueeTool;
        private EntityQuery m_TempQuery;
        private EntityQuery m_ZoneTempQuery;
        private EntityQuery m_BrushTempQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_MarqueeTool = World.GetOrCreateSystemManaged<MarqueeDeleteToolSystem>();

            m_TempQuery = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.Exclude<Deleted>());

            m_ZoneTempQuery = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Block>(),
                ComponentType.ReadOnly<Game.Zones.Cell>(),
                ComponentType.Exclude<Deleted>());

            m_BrushTempQuery = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Game.Tools.Brush>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            if (!Mod.RuntimeActive || m_ToolSystem == null || m_MarqueeTool == null)
            {
                return;
            }

            if ((m_ToolSystem.actionMode & GameMode.GameOrEditor) == 0 || m_ToolSystem.activeTool == null)
            {
                return;
            }

            if (m_ToolSystem.applyMode != ApplyMode.Apply && m_ToolSystem.activeTool.applyMode != ApplyMode.Apply)
            {
                return;
            }

            Dependency.Complete();
            m_MarqueeTool.OnGlobalUndoPreApply(
                m_ToolSystem,
                m_TerrainSystem,
                m_TempQuery,
                m_ZoneTempQuery,
                m_BrushTempQuery);
        }
    }

    public sealed class GlobalUndoPostApplySystem : GameSystemBase
    {
        private MarqueeDeleteToolSystem m_MarqueeTool;
        private EntityQuery m_CreatedQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_MarqueeTool = World.GetOrCreateSystemManaged<MarqueeDeleteToolSystem>();

            m_CreatedQuery = GetEntityQuery(
                ComponentType.ReadOnly<Applied>(),
                ComponentType.ReadOnly<Created>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Vehicle>(),
                ComponentType.Exclude<Creature>(),
                ComponentType.Exclude<Moving>(),
                ComponentType.Exclude<Lane>());
        }

        protected override void OnUpdate()
        {
            if (!Mod.RuntimeActive || m_MarqueeTool == null)
            {
                return;
            }

            Dependency.Complete();
            m_MarqueeTool.OnGlobalUndoPostApply(m_CreatedQuery);
        }
    }
}
