using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Collections;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Audio;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MarqueeDelete
{
    public sealed partial class MarqueeDeleteToolSystem : ToolBaseSystem
    {
        public override string toolID => "Marquee Delete";
        public override bool allowUnderground => true;

        private OverlayRenderSystem m_OverlayRenderSystem;
        private TerrainSystem m_TerrainSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private bool m_InToolUpdate;
        private AudioManager m_AudioManager;
        private BulldozeToolSystem m_BulldozeToolSystem;
        private EntityQuery m_SoundQuery;
        private ProxyAction m_ConfirmAction;
        private ProxyAction m_ForceConfirmImportantAction;
        private ProxyAction m_ExcludeImportantAction;
        private ProxyAction m_ClearSelectionAction;

        private NameSystem m_NameSystem;
        private bool m_ImportantModalOpen;
        private string m_ImportantListRaw = string.Empty;

        private readonly MarqueeToolSession m_Session = new MarqueeToolSession();
        private readonly PresetViewLinkTracker m_PresetViewTracker = new PresetViewLinkTracker();
        private bool m_Underground;
        private Entity m_PressHitEntity;
        private bool m_PolygonSnapActive;
        private bool m_PolygonVertexCapWarned;
        private bool m_LastClosedShapeInvalid;
        private MarqueeSelectionMode m_InvalidShapeMode;
        private uint m_ClosedShapeVersion;
        private float2 m_LastTerrainHeightScaleOffset;
        private bool m_HasLastTerrainHeightScaleOffset;
        private MarqueeShapeXZ m_CachedDraftMetricsShape;
        private MarqueeSelectionMode m_CachedDraftMetricsMode;
        private int m_CachedDraftMetricsRevision = -1;
        private readonly CachedShapeOverlay m_ClosedOverlayCache = new CachedShapeOverlay();
        private readonly CachedShapeOverlay m_DraftOverlayCache = new CachedShapeOverlay();
        private float m_LastPreviewTime;
        private float m_PreviewInterval = 0.10f;
        private MarqueeShapeXZ m_LastPreviewShape;
        private bool m_PreviewDirty;
        private bool m_HasCursorWorld;
        private float3 m_CursorWorld;
        private Entity m_CursorHitEntity;

        private SelectionResult m_PreviewResult;
        private SelectionResult m_PendingResult;
        private SelectionResult m_QueuedDeletion;
        private SelectionResult m_BatchedDeletion;
        private List<SelectedEntityItem> m_DeletionItems;
        private List<Entity> m_DeferredDeletionNodes;
        private List<Entity> m_BlockedDeletionNodes;
        private bool m_BatchedDeletionUnderground;
        private InterruptedDeletion m_InterruptedDeletion;
        private SelectionResult m_InterruptedReview;
        private int m_NextDeletionItem;
        private const int MaxBatchImpact = 512;
        private const int MaxBatchRoots = 128;
        // How long the game gets to actually remove a submitted batch before the tool stops
        // trusting the plan and asks the player to review the remainder.
        private const float BatchObserveTimeout = 3f;
        private const float NodeObserveTimeout = 6f;
        private readonly List<Entity> m_LastBatchSubmitted = new List<Entity>();
        private readonly HashSet<Entity> m_OversizedRoots = new HashSet<Entity>();
        private int m_LastBatchOutstanding;
        private float m_BatchObserveDeadline;
        private int m_BatchLength = MaxBatchRoots;
        private int m_LastBatchCostMs;
        private bool m_TaskSkipBatchRevalidation;
        private bool m_QueuedSkipRevalidation;
        private int m_UndoRefreshDelayFrames;
        private readonly List<float3> m_UndoRefreshBuildingPositions = new List<float3>();
        private readonly List<float3> m_UndoRefreshNetPositions = new List<float3>();
        private int m_UndoDiagDelayFrames;
        private int m_UndoDiagRestoredCount;
        private int m_UndoDiagNeighborCount;
        private readonly List<float3> m_UndoDiagBuildingPositions = new List<float3>();
        private BatchTaskRecord m_TaskRecord;
        private ToolOutputBarrier m_DeletionBarrier;
        private readonly DeletionUndoManager m_UndoManager = new DeletionUndoManager();
        private readonly HashSet<Entity> m_Highlighted = new HashSet<Entity>();
        private readonly HashSet<Entity> m_OwnedHighlights = new HashSet<Entity>();
        private readonly HashSet<Entity> m_DesiredHighlights = new HashSet<Entity>();
        private readonly List<Entity> m_ToRemoveHighlights = new List<Entity>();
        private readonly EscapeFrameGate m_EscapeGate = new EscapeFrameGate();
        private readonly List<MarqueeShapeXZ> m_SelectionHistory = new List<MarqueeShapeXZ>(SelectionHistoryPolicy.MaxHistoryCount);
        public int SelectionHistoryCount => m_SelectionHistory.Count;
        private int m_ResultRevision;
        private int m_ShapeRevision;
        public int ResultRevision => m_ResultRevision;
        public int ShapeRevision => m_ShapeRevision +
            (m_Session.Mode == MarqueeSelectionMode.Polygon ? m_Session.PolygonDraft.Revision :
            (m_Session.Mode == MarqueeSelectionMode.Freehand && m_Session.FreehandDrawing ? m_Session.FreehandDraft.Revision : 0));
        private string m_CachedImpactReport;
        private int m_CachedImpactRevision = -1;
        private bool m_SuppressHistoryPush;
        private float2 m_WidthLabelViewportPos = new float2(0.5f, 0.5f);
        private float2 m_DepthLabelViewportPos = new float2(0.5f, 0.5f);

        private sealed class InterruptedDeletion
        {
            public SelectionResult Approved;
            public List<SelectedEntityItem> Items;
            public List<Entity> Nodes;
            public bool Underground;
            /// <summary>Facility units among the remaining items, so a resume can say how many are involved.</summary>
            public int UnitCount;
            /// <summary>Entities in the largest of those units.</summary>
            public int LargestUnit;
        }

        /// <summary>
        /// Per-task record of what the batched deletion actually did, so a pause or a finish
        /// answers "what is still pending, what was handed over, what did the game really remove,
        /// what was kept, what needs review" for the entities this task touched only.
        /// </summary>
        private sealed class BatchTaskRecord
        {
            public int Pending;
            public int Unconfirmed;
            public int Submitted;
            public int Observed;
            public int Kept;
            public int Review;
            public int OversizedUnits;
            public int LargestUnit;
            public int Batches;

            public void Reset()
            {
                Pending = 0;
                Unconfirmed = 0;
                Submitted = 0;
                Observed = 0;
                Kept = 0;
                Review = 0;
                OversizedUnits = 0;
                LargestUnit = 0;
                Batches = 0;
            }
        }

        private sealed class DeletionAuditLog
        {
            public int Selected;
            public int Affected;
            public int Submitted;
            public int EntitiesBefore;
            public string State = "complete";
            public int FramesRemaining = 2;
            public string Extra = string.Empty;
        }

        private DeletionAuditLog m_PendingAuditLog;

        private bool m_PointerPressed
        {
            get => m_Session.PointerPressed;
            set => m_Session.PointerPressed = value;
        }

        private bool m_Dragging
        {
            get => m_Session.Dragging;
            set => m_Session.Dragging = value;
        }

        private float2 m_PressScreenPos
        {
            get => new float2(m_Session.PressScreenX, m_Session.PressScreenY);
            set
            {
                m_Session.PressScreenX = value.x;
                m_Session.PressScreenY = value.y;
            }
        }

        private float3 m_PressWorld
        {
            get => new float3(m_Session.PressWorldXZ.X, m_Session.PressWorldY, m_Session.PressWorldXZ.Z);
            set
            {
                m_Session.PressWorldXZ = new CoordXZ(value.x, value.z);
                m_Session.PressWorldY = value.y;
            }
        }

        private float3 m_DragStartWorld
        {
            get => new float3(m_Session.DragStartXZ.X, m_Session.DragStartY, m_Session.DragStartXZ.Z);
            set
            {
                m_Session.DragStartXZ = new CoordXZ(value.x, value.z);
                m_Session.DragStartY = value.y;
            }
        }

        private float3 m_DragEndWorld
        {
            get => new float3(m_Session.DragEndXZ.X, m_Session.DragEndY, m_Session.DragEndXZ.Z);
            set
            {
                m_Session.DragEndXZ = new CoordXZ(value.x, value.z);
                m_Session.DragEndY = value.y;
            }
        }

        public MarqueeSelectionMode CurrentSelectionMode => m_Session.Mode;
        public bool HasPolygonDraft => m_Session.HasPolygonDraft;
        public int PolygonDraftVertexCount => m_Session.PolygonDraft.Count;
        public bool IsPolygonSnapActive => m_PolygonSnapActive;
        public bool IsPolygonVertexLimitReached => m_Session.Mode == MarqueeSelectionMode.Polygon &&
                                                   (m_Session.PolygonDraft.Count >= (Mod.Settings != null ? System.Math.Min(128, System.Math.Max(16, Mod.Settings.MaxPolygonVertices)) : MarqueeShapeXZ.MaxPolygonVertices) || m_PolygonVertexCapWarned);
        public bool IsFreehandDrawing => m_Session.FreehandDrawing;
        public bool IsDrawingOrDraftingShape => m_Dragging ||
                                                (m_Session.Mode == MarqueeSelectionMode.Polygon && m_Session.PolygonDraft.Count >= 3) ||
                                                (m_Session.FreehandDrawing && m_Session.FreehandDraft.Count >= 3);
        public bool LastClosedShapeInvalid => m_LastClosedShapeInvalid;
        public MarqueeSelectionMode InvalidShapeMode => m_InvalidShapeMode;

        public bool IsToolActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;
        public bool IsDragging => m_Dragging;
        public bool IsMouseButtonBusy => m_Session.IsMouseButtonActive;
        public bool IsPointerBusy => m_Session.IsBusy;
        public bool Underground => m_Underground;
        public bool IsBulldozeActive => m_ToolSystem != null && m_BulldozeToolSystem != null && m_ToolSystem.activeTool == m_BulldozeToolSystem;
        public bool HasPending => !m_Dragging && m_PendingResult != null && m_PendingResult.Count > 0;
        public bool HasQueuedDeletion => m_QueuedDeletion != null || m_BatchedDeletion != null || RoadFacilitiesBusy;
        public int InterruptedDeletionCount => m_InterruptedDeletion == null
            ? 0 : m_InterruptedDeletion.Items.Count + m_InterruptedDeletion.Nodes.Count;

        /// <summary>
        /// Facility units still pending in the interrupted deletion, i.e. what a resume would bring
        /// back. 0 also reads as "no interruption", so the count alone never claims there is one:
        /// <see cref="InterruptedDeletionCount"/> is what says whether anything is being held.
        /// </summary>
        public int InterruptedDeletionUnitCount => m_InterruptedDeletion == null ? 0 : m_InterruptedDeletion.UnitCount;

        /// <summary>Entities in the largest pending unit; 0 when none is held.</summary>
        public int InterruptedDeletionLargestUnit => m_InterruptedDeletion == null ? 0 : m_InterruptedDeletion.LargestUnit;
        public bool IsReviewingInterruptedDeletion => m_InterruptedReview != null && m_InterruptedReview == m_PendingResult;
        private bool CanConfirmCurrentSelection => m_InterruptedDeletion == null || IsReviewingInterruptedDeletion;
        public SelectionResult CurrentResult => m_Dragging ? m_PreviewResult : m_PendingResult;
        public float2 WidthLabelViewportPos => m_WidthLabelViewportPos;
        public float2 DepthLabelViewportPos => m_DepthLabelViewportPos;

        public bool ImportantModalOpen => !m_Dragging && m_ImportantModalOpen;
        public bool ColorModalOpen { get; private set; }
        public bool PickSimilarArmed => m_Session.PickSimilarArmed && HasPending && !m_Session.HasPolygonDraft;
        public EscapeFrameGate EscapeGate => m_EscapeGate;
        public string SelectionNotice { get; private set; } = string.Empty;

        public bool TogglePickSimilar()
        {
            if (IsReviewingInterruptedDeletion || IsEditingRoadFacilities) return false;
            if (m_Session.HasPolygonDraft)
            {
                m_Session.PickSimilarArmed = false;
                SelectionNotice = "similar_blocked_polygon";
                PlayUiSound(selectEntity: false);
                return false;
            }

            if (!IsToolActive || HasActiveUndo || m_Session.IsMouseButtonActive ||
                m_ImportantModalOpen || ColorModalOpen || m_QueuedDeletion != null || !HasPending)
            {
                m_Session.PickSimilarArmed = false;
                return false;
            }

            m_Session.PickSimilarArmed = !m_Session.PickSimilarArmed;
            PlayUiSound(selectEntity: m_Session.PickSimilarArmed);
            return true;
        }

        public bool ApplyFilterPreset(string presetId)
        {
            Settings s = Mod.Settings;
            if (s == null || string.IsNullOrEmpty(presetId))
            {
                return false;
            }

            FilterPresetValues preset;
            bool prefersUnderground = false;
            if (!FilterPresetValues.TryGetBuiltIn(presetId, out preset, out prefersUnderground))
            {
                int slot = presetId switch
                {
                    "custom1" => 1,
                    "custom2" => 2,
                    "custom3" => 3,
                    _ => 0,
                };
                if (slot <= 0 || !s.TryGetCustomFilterPreset(slot, out preset))
                {
                    return false;
                }
            }

            s.ApplyFilterPresetValues(in preset);
            bool targetUnderground = m_PresetViewTracker.ResolveViewForPreset(prefersUnderground, m_Underground);
            bool viewChanged = targetUnderground != m_Underground;
            if (viewChanged)
            {
                SetUndergroundState(targetUnderground, fromPreset: true);
            }
            else
            {
                ReevaluatePending();
            }

            s.ApplyAndSave();
            PlayUiSound(selectEntity: true);
            return true;
        }

        public void OnFilterResetOrAllChanged()
        {
            bool targetUnderground = m_PresetViewTracker.ResolveViewForPreset(false, m_Underground);
            if (targetUnderground != m_Underground)
            {
                SetUndergroundState(targetUnderground, fromPreset: true);
            }
            else
            {
                ReevaluatePending();
            }
        }

        public void SetColorModalOpen(bool open)
        {
            open = open && !m_ImportantModalOpen;
            bool wasOpen = ColorModalOpen;
            ColorModalOpen = open;
            if (!wasOpen && open)
            {
                m_Session.OnPermissionOrModalInterruption(abortPolygonDraft: false);
                m_PressHitEntity = Entity.Null;
                m_PreviewResult = null;
                m_PolygonSnapActive = false;
                SyncHighlights(m_PendingResult);
            }
        }

        public void CloseColorModalFromUi()
        {
            if (!ColorModalOpen)
            {
                return;
            }

            ColorModalOpen = false;
            PlayUiSound(selectEntity: false);
        }

        public void SetSelectionMode(MarqueeSelectionMode targetMode)
        {
            if (HasActiveUndo)
            {
                return;
            }

            MarqueeSelectionMode clean = MarqueePointerLogic.SanitizeSelectionMode((int)targetMode);
            bool hadActiveDraft = m_Session.IsBusy;
            bool modeChanged = m_Session.SetMode(clean);
            if (hadActiveDraft)
            {
                m_PressHitEntity = Entity.Null;
                m_PreviewResult = null;
                m_PreviewDirty = false;
                m_PreviewInterval = 0.10f;
                m_LastPreviewShape = default(MarqueeShapeXZ);
                m_PolygonSnapActive = false;
                SyncHighlights(m_PendingResult);
            }

            if (modeChanged || hadActiveDraft)
            {
                m_LastClosedShapeInvalid = false;
                m_PolygonVertexCapWarned = false;
                if (Mod.Settings != null)
                {
                    Mod.Settings.SetSelectionModeValue(clean);
                    Mod.Settings.ApplyAndSave();
                }

                PlayUiSound(selectEntity: false);
            }
        }

        public MarqueeSelectionMode CycleSelectionMode()
        {
            MarqueeSelectionMode next = MarqueePointerLogic.CycleSelectionMode(m_Session.Mode);
            SetSelectionMode(next);
            return m_Session.Mode;
        }

        public void ClearInvalidShapeFeedback()
        {
            m_LastClosedShapeInvalid = false;
        }
        public string ImportantListRaw => m_ImportantListRaw ?? string.Empty;
        private UndoAssetMask CurrentUndoMask => Mod.Settings != null ? Mod.Settings.BuildUndoMask() : UndoAssetMask.All;
        public bool HasActiveUndo => m_UndoManager.HasActiveUndo;
        public SelectiveUndoUiSnapshot GetUndoUiSnapshot() => m_UndoManager.GetUiSnapshot(CurrentUndoMask);
        public MarqueeShapeXZ ActiveShape
        {
            get
            {
                if (m_Dragging)
                {
                    return BuildRectShape(m_DragStartWorld, m_DragEndWorld);
                }

                if (m_PendingResult != null)
                {
                    return m_PendingResult.Shape;
                }

                return GetLiveDraftMetricsShape();
            }
        }

        private MarqueeShapeXZ GetLiveDraftMetricsShape()
        {
            if (m_Session.Mode == MarqueeSelectionMode.Polygon && m_Session.PolygonDraft.Count >= 3)
            {
                int rev = m_Session.PolygonDraft.Revision;
                if (m_CachedDraftMetricsMode != MarqueeSelectionMode.Polygon || m_CachedDraftMetricsRevision != rev)
                {
                    m_CachedDraftMetricsMode = MarqueeSelectionMode.Polygon;
                    m_CachedDraftMetricsRevision = rev;
                    m_CachedDraftMetricsShape = MarqueeShapeXZ.FromPolygon(m_Session.PolygonDraft.Vertices, MarqueeSelectionMode.Polygon);
                }
                return m_CachedDraftMetricsShape;
            }

            if (m_Session.FreehandDrawing && m_Session.FreehandDraft.Count >= 3)
            {
                int rev = m_Session.FreehandDraft.Revision;
                if (m_CachedDraftMetricsMode != MarqueeSelectionMode.Freehand || m_CachedDraftMetricsRevision != rev)
                {
                    m_CachedDraftMetricsMode = MarqueeSelectionMode.Freehand;
                    m_CachedDraftMetricsRevision = rev;
                    m_CachedDraftMetricsShape = MarqueeShapeXZ.FromPolygon(m_Session.FreehandDraft.Points, MarqueeSelectionMode.Freehand);
                }
                return m_CachedDraftMetricsShape;
            }

            return default(MarqueeShapeXZ);
        }

        public bool EffectiveUnderground
        {
            get
            {
                if (IsToolActive)
                {
                    return m_Underground;
                }
                if (IsBulldozeActive && m_BulldozeToolSystem != null)
                {
                    return m_BulldozeToolSystem.underground;
                }
                return m_Underground;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_AudioManager = World.GetOrCreateSystemManaged<AudioManager>();
            m_BulldozeToolSystem = World.GetOrCreateSystemManaged<BulldozeToolSystem>();
            m_DeletionBarrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_SoundQuery = GetEntityQuery(ComponentType.ReadOnly<ToolUXSoundSettingsData>());

            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            if (Mod.Settings != null)
            {
                m_ConfirmAction = Mod.Settings.GetAction(Settings.ConfirmActionName);
                m_ForceConfirmImportantAction = Mod.Settings.GetAction(Settings.ForceConfirmImportantActionName);
                m_ExcludeImportantAction = Mod.Settings.GetAction(Settings.ExcludeImportantActionName);
                m_ClearSelectionAction = Mod.Settings.GetAction(Settings.ClearSelectionActionName);
                m_Session.SetMode(Mod.Settings.GetSelectionMode());
            }
        }

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public override void SetUnderground(bool underground)
        {
            SetUndergroundState(underground, fromPreset: false);
        }

        public override void ElevationUp()
        {
            SetUndergroundState(false, fromPreset: false);
        }

        public override void ElevationDown()
        {
            SetUndergroundState(true, fromPreset: false);
        }

        public override void ElevationScroll()
        {
            SetUndergroundState(!m_Underground, fromPreset: false);
        }

        private void SetUndergroundState(bool underground, bool fromPreset = false)
        {
            if (!fromPreset)
            {
                m_PresetViewTracker.OnUserManualViewToggle();
            }

            bool changed = m_Underground != underground;
            m_Underground = underground;
            if (underground && IsEditingRoadFacilities)
            {
                FacilityMode = RoadFacilityMode.None;
                ClearPending();
            }
            if (m_BulldozeToolSystem != null)
            {
                m_BulldozeToolSystem.underground = underground;
            }

            if (IsToolActive)
            {
                SyncElevationRequirements();
            }

            if (changed)
            {
                ReevaluatePending();
            }
        }

        private void SyncElevationRequirements()
        {
            requireUnderground = m_Underground;
            requirePipelines = m_Underground;
            requireNet = m_Underground ? Game.Net.Layer.None : Game.Net.Layer.Waterway;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();

            if (m_Dragging || m_Session.HasPolygonDraft)
            {
                m_ToolRaycastSystem.typeMask = TypeMask.Terrain | TypeMask.Water;
                m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
                m_ToolRaycastSystem.netLayerMask = Layer.None;
                m_ToolRaycastSystem.areaTypeMask = Game.Areas.AreaTypeMask.None;
                m_ToolRaycastSystem.utilityTypeMask = UtilityTypes.None;
                return;
            }

            SelectionFilterMask mask = Mod.Settings != null
                ? Mod.Settings.BuildFilterMask()
                : SelectionFilterMask.All;
            SelectionBreakdown pendingBd = m_PendingResult != null
                ? m_PendingResult.Breakdown
                : default(SelectionBreakdown);

            bool needStatic = m_Session.PickSimilarArmed || (mask & (SelectionFilterMask.Buildings |
                                       SelectionFilterMask.AllZones |
                                       SelectionFilterMask.Trees |
                                       SelectionFilterMask.Props |
                                       SelectionFilterMask.Decals)) != 0 ||
                              (pendingBd.Buildings + pendingBd.Trees + pendingBd.Props + pendingBd.Decals) > 0;
            bool needMoving = m_Session.PickSimilarArmed || (mask & SelectionFilterMask.Moving) != 0 || pendingBd.Moving > 0;
            bool needNet = m_Session.PickSimilarArmed || (mask & SelectionFilterMask.Networks) != 0 || pendingBd.Networks > 0;
            bool needArea = !m_Underground && (m_Session.PickSimilarArmed || (mask & SelectionFilterMask.Areas) != 0 || pendingBd.Areas > 0);

            TypeMask typeMask = TypeMask.Terrain | TypeMask.Water;
            if (needStatic) typeMask |= TypeMask.StaticObjects;
            if (needMoving) typeMask |= TypeMask.MovingObjects;
            if (needNet) typeMask |= TypeMask.Net;
            if (needArea)
            {
                typeMask |= TypeMask.Areas;
                m_ToolRaycastSystem.areaTypeMask =
                    Game.Areas.AreaTypeMask.Lots |
                    Game.Areas.AreaTypeMask.Spaces |
                    Game.Areas.AreaTypeMask.Surfaces;
            }

            m_ToolRaycastSystem.typeMask = typeMask;
            m_ToolRaycastSystem.netLayerMask = needNet ? Layer.All : Layer.None;
            m_ToolRaycastSystem.raycastFlags |= RaycastFlags.BuildingLots | RaycastFlags.Decals | RaycastFlags.PartialSurface;
            m_ToolRaycastSystem.collisionMask = m_Underground
                ? CollisionMask.Underground
                : (CollisionMask.OnGround | CollisionMask.Overground);
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_Session.CancelActiveInteraction();
            m_Session.PickSimilarArmed = false;
            m_PresetViewTracker.Reset();
            if (Mod.Settings != null)
            {
                m_Session.SetMode(Mod.Settings.GetSelectionMode());
            }
            m_PressHitEntity = Entity.Null;
            m_CursorHitEntity = Entity.Null;
            m_PolygonSnapActive = false;
            m_LastClosedShapeInvalid = false;
            ClearPending();
            ClearBuildingOverlay();
            ClearAssetOverlay();
            applyMode = ApplyMode.None;
            SyncElevationRequirements();
            SyncToolActionStates(toolRunning: true, hasPending: false);
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            FacilityMode = RoadFacilityMode.None;
            ColorModalOpen = false;
            m_EscapeGate.Reset();
            m_Session.CancelActiveInteraction();
            m_Session.PickSimilarArmed = false;
            m_PresetViewTracker.Reset();
            m_PressHitEntity = Entity.Null;
            m_CursorHitEntity = Entity.Null;
            m_PolygonSnapActive = false;
            m_LastClosedShapeInvalid = false;
            FinishDeletionBatches("stopped");
            ClearPending();
            ClearBuildingOverlay();
            ClearAssetOverlay();
            m_PendingAuditLog = null;
            m_SelectionHistory.Clear();
            m_InterruptedDeletion = null;
            SelectionCollector.ClearPrefabCache();
            m_UndoRefreshDelayFrames = 0;
            m_UndoRefreshBuildingPositions.Clear();
            m_UndoRefreshNetPositions.Clear();
            m_UndoDiagDelayFrames = 0;
            m_UndoDiagBuildingPositions.Clear();
            m_UndoManager.ClearHistory();
            base.OnGamePreload(purpose, mode);
        }

        protected override void OnDestroy()
        {
            ColorModalOpen = false;
            m_EscapeGate.Reset();
            m_Session.CancelActiveInteraction();
            ClearAssetOverlay();
            m_PendingAuditLog = null;
            m_UndoRefreshDelayFrames = 0;
            m_UndoRefreshBuildingPositions.Clear();
            m_UndoRefreshNetPositions.Clear();
            m_UndoDiagDelayFrames = 0;
            m_UndoDiagBuildingPositions.Clear();
            m_UndoManager.ClearHistory();
            base.OnDestroy();
        }

        protected override void OnStopRunning()
        {
            FacilityMode = RoadFacilityMode.None;
            m_Session.CancelActiveInteraction();
            m_Session.PickSimilarArmed = false;
            m_PresetViewTracker.Reset();
            m_PressHitEntity = Entity.Null;
            m_CursorHitEntity = Entity.Null;
            m_PolygonSnapActive = false;
            m_LastClosedShapeInvalid = false;
            ClearPending();
            FlushPendingAuditLog();
            ClearBuildingOverlay();
            ClearAssetOverlay();
            requireUnderground = false;
            requirePipelines = false;
            requireNet = Game.Net.Layer.None;
            SyncToolActionStates(toolRunning: false, hasPending: false);
            applyMode = ApplyMode.None;
            base.OnStopRunning();
        }

        public void SetToolActive(bool active)
        {
            if (m_ToolSystem == null)
            {
                return;
            }

            if (active && (!Mod.RuntimeActive || (Mod.Settings != null && !Mod.Settings.Enabled)))
            {
                return;
            }

            if (active && m_BulldozeToolSystem != null && m_ToolSystem.activeTool == m_BulldozeToolSystem)
            {
                m_Underground = m_BulldozeToolSystem.underground;
            }

            ToolBaseSystem target = active ? (ToolBaseSystem)this : m_DefaultToolSystem;
            if (m_ToolSystem.activeTool != target)
            {
                m_ToolSystem.activeTool = target;
            }
        }

        public void SwitchToBulldoze()
        {
            if (m_ToolSystem == null || m_BulldozeToolSystem == null)
            {
                SetToolActive(false);
                return;
            }

            m_BulldozeToolSystem.underground = m_Underground;
            if (m_ToolSystem.activeTool != m_BulldozeToolSystem)
            {
                m_ToolSystem.activeTool = m_BulldozeToolSystem;
            }
        }

        public void ToggleTool()
        {
            SetToolActive(!IsToolActive);
        }

        public bool ConfirmPendingDeletion() => ConfirmPendingDeletion(false);

        public bool ConfirmPendingDeletion(bool forceSkipRevalidation)
        {
            if (HasActiveUndo || ColorModalOpen || m_Session.IsMouseButtonActive || HasQueuedDeletion || !CanConfirmCurrentSelection ||
                !HasPending || m_PendingResult.AffectsProtectedObject)
                return false;
            m_QueuedSkipRevalidation = forceSkipRevalidation;
            if (m_PendingResult.Breakdown.ImportantBuildings > 0)
            {
                m_ImportantModalOpen = true;
                return false;
            }
            return QueueDeletion(forceSkipRevalidation);
        }

        public bool ForceConfirmPendingDeletion()
        {
            if (HasActiveUndo || m_Session.IsMouseButtonActive || HasQueuedDeletion || !CanConfirmCurrentSelection ||
                !HasPending || !m_ImportantModalOpen || m_PendingResult.AffectsProtectedObject)
                return false;
            return QueueDeletion(m_QueuedSkipRevalidation);
        }

        private bool QueueDeletion(bool skipRevalidation = false)
        {
            m_QueuedDeletion = m_PendingResult;
            m_QueuedSkipRevalidation = skipRevalidation;
            m_ImportantModalOpen = false;
            return true;
        }

        public void CancelImportantModal()
        {
            m_ImportantModalOpen = false;
            m_QueuedSkipRevalidation = false;
        }

        public bool ExcludeImportantBuildings()
        {
            if (HasActiveUndo || m_Session.IsMouseButtonActive || m_QueuedDeletion != null || !HasPending)
            {
                return false;
            }

            SelectionResult next = SelectionCollector.ExcludeImportantBuildings(EntityManager, m_PendingResult, out int excludedCount);
            if (excludedCount <= 0)
            {
                return false;
            }

            m_ImportantModalOpen = false;
            m_QueuedDeletion = null;

            if (next != null && next.Count > 0)
            {
                // Even if ConfirmBeforeDelete is false, keep the remaining items in pending
                // review state so the player sees what remains before confirming.
                bool reviewing = IsReviewingInterruptedDeletion;
                SetPending(next, changed: false, openImportantModal: false);
                if (reviewing) m_InterruptedReview = m_PendingResult;
                if (!next.AffectsProtectedObject)
                {
                    SelectionNotice = $"excluded|{excludedCount}";
                }
                PlayUiSound(selectEntity: true);
            }
            else
            {
                SetPending(null);
                SelectionNotice = $"excluded_all|{excludedCount}";
                PlayUiSound(selectEntity: false);
            }

            return true;
        }

        public void ClearPending()
        {
            m_FacilityBatch = null;
            PauseDeletion("stopped");
            m_PreviewResult = null;
            m_QueuedDeletion = null;
            m_PreviewInterval = 0.10f;
            m_LastClosedShapeInvalid = false;
            m_PolygonVertexCapWarned = false;
            m_CachedImpactReport = string.Empty;
            m_CachedImpactRevision = -1;
            SetPending(null);
        }

        private void SetPendingResultAndBumpRevision(SelectionResult result)
        {
            m_InterruptedReview = null;
            m_PendingResult = result;
            m_ResultRevision++;
            m_CachedImpactReport = string.Empty;
            m_CachedImpactRevision = -1;
        }

        private void SetPending(
            SelectionResult result,
            bool changed = false,
            bool openImportantModal = false,
            bool retainIfEmpty = false)
        {
            bool retain = result != null && (result.Count > 0 || (retainIfEmpty && result.HasRetainedEdits));
            SetPendingResultAndBumpRevision(retain ? result : null);
            if (!m_SuppressHistoryPush && retain && result != null && result.Shape.IsUsable())
            {
                PushSelectionHistory(result.Shape);
            }
            m_Session.PickSimilarArmed = false;
            m_ClosedShapeVersion++;
            m_ShapeRevision++;
            bool hasVisible = m_PendingResult != null && m_PendingResult.Count > 0;
            if (hasVisible)
            {
                m_LastClosedShapeInvalid = false;
                m_UndoManager.ClearNonAlertFinishedNotice();
            }
            bool blocked = hasVisible && m_PendingResult.AffectsProtectedObject;
            m_ImportantListRaw = hasVisible ? BuildImportantListRaw(m_PendingResult) : string.Empty;
            m_ImportantModalOpen = openImportantModal && hasVisible && !blocked && m_PendingResult.Breakdown.ImportantBuildings > 0;
            if (m_ImportantModalOpen)
            {
                ColorModalOpen = false;
            }
            if (blocked)
            {
                SelectionNotice = "protected";
            }
            else if (changed)
            {
                SelectionNotice = "changed";
            }
            else if (retain && !hasVisible)
            {
                SelectionNotice = MarqueePointerLogic.ComputeHiddenRetainedNotice(
                    m_PendingResult.Count,
                    m_PendingResult.Shape.IsUsable(),
                    m_PendingResult.ManuallyAdded.Count);
            }
            else
            {
                SelectionNotice = string.Empty;
            }
            SyncHighlights(hasVisible ? m_PendingResult : null);
        }

        public void ReevaluatePending()
        {
            if (Mod.Settings == null || IsEditingRoadFacilities) return;
            if (m_BatchedDeletion != null)
            {
                PauseDeletion("stopped");
                SelectionNotice = "deletion_stopped";
            }
            if (IsReviewingInterruptedDeletion)
            {
                m_QueuedDeletion = null;
                SetPending(null);
                return;
            }
            bool wasModalOpen = m_ImportantModalOpen;
            m_QueuedDeletion = null;
            if (m_PendingResult != null)
            {
                // Re-apply current filters to both box-collected and manually clicked items,
                // preserving manually added and manually excluded records even when 0 items match right now.
                var result = SelectionCollector.ReevaluateWithCurrentFilters(
                    World,
                    EntityManager,
                    m_PendingResult,
                    Mod.Settings.BuildFilterMask(),
                    Mod.Settings.BuildSafetyOptions(),
                    m_Underground);
                bool keepModal = wasModalOpen && result.Count > 0 && !result.AffectsProtectedObject && result.Breakdown.ImportantBuildings > 0;
                if (m_Dragging)
                {
                    SetPendingResultAndBumpRevision(result.HasRetainedEdits ? result : null);
                    bool hasVisible = m_PendingResult != null && m_PendingResult.Count > 0;
                    m_ImportantListRaw = hasVisible ? BuildImportantListRaw(m_PendingResult) : string.Empty;
                    m_ImportantModalOpen = keepModal;
                    SelectionNotice = (hasVisible && result.AffectsProtectedObject)
                        ? "protected"
                        : (m_PendingResult != null && !hasVisible
                            ? MarqueePointerLogic.ComputeHiddenRetainedNotice(
                                m_PendingResult.Count,
                                m_PendingResult.Shape.IsUsable(),
                                m_PendingResult.ManuallyAdded.Count)
                            : string.Empty);
                }
                else
                {
                    SetPending(result, changed: false, openImportantModal: keepModal, retainIfEmpty: true);
                }
            }
            if (m_Dragging) m_PreviewDirty = true;
        }

        private SelectionResult Collect(MarqueeShapeXZ shape, bool includeCascade, IReadOnlyCollection<Entity> excluded = null) =>
            IsEditingRoadFacilities
                ? RoadFacilities.Collect(World, EntityManager, in shape, FacilityMode)
                : SelectionCollector.Collect(World, EntityManager, shape, Mod.Settings.BuildFilterMask(),
                    Mod.Settings.BuildSafetyOptions(), m_Underground, includeCascade, excluded);

        private string BuildImportantListRaw(SelectionResult res)
        {
            if (res == null || res.ImportantEntities.Count == 0)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            int added = 0;
            const int maxListed = 10;

            foreach (var item in res.ImportantEntities)
            {
                if (added >= maxListed) break;
                var kind = item.Value;
                string kindTag = (kind & ImportantBuildingKind.Signature) != 0
                    ? "L"
                    : ((kind & ImportantBuildingKind.Unique) != 0 ? "U" : "S");

                string name = ResolveEntityDisplayName(item.Key);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(kindTag).Append('|').Append(name);
                added++;
            }

            int remaining = res.ImportantEntities.Count - added;
            if (remaining > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("MORE|").Append(remaining);
            }

            return sb.ToString();
        }

        public bool CanRestoreSelectionHistory()
        {
            return RestoreSelectionGate.CanRestore(
                isToolActive: IsToolActive,
                hasActiveUndo: HasActiveUndo,
                isPointerBusy: IsPointerBusy,
                isMouseButtonBusy: IsMouseButtonBusy,
                importantModalOpen: m_ImportantModalOpen,
                colorModalOpen: ColorModalOpen,
                hasActiveDraft: m_Session.HasPolygonDraft || m_Session.FreehandDrawing,
                hasQueuedDeletion: HasQueuedDeletion,
                historyCount: m_SelectionHistory.Count);
        }

        public void PushSelectionHistory(MarqueeShapeXZ shape)
        {
            if (m_SuppressHistoryPush || !shape.IsUsable()) return;
            SelectionHistoryPolicy.PushHistory(m_SelectionHistory, in shape, SelectionHistoryPolicy.MaxHistoryCount);
        }

        internal string RestoreLastSelection()
        {
            if (!CanRestoreSelectionHistory())
            {
                return m_SelectionHistory.Count == 0 ? "history_empty" : "restore_blocked";
            }

            int lastIndex = m_SelectionHistory.Count - 1;
            MarqueeShapeXZ shape = m_SelectionHistory[lastIndex];
            m_SelectionHistory.RemoveAt(lastIndex);

            ClearPending();
            m_SuppressHistoryPush = true;
            try
            {
                InitializePendingFromShape(shape, allowAutoDelete: false);
            }
            finally
            {
                m_SuppressHistoryPush = false;
            }
            return string.Empty;
        }

        private string BuildImpactReportRaw(SelectionResult res)
        {
            if (res == null || res.Items.Count == 0)
            {
                return string.Empty;
            }

            var prefabCounts = new Dictionary<string, int>(64);
            foreach (var item in res.Items)
            {
                string name = null;
                if (item.Prefab != Entity.Null && EntityManager.Exists(item.Prefab) && m_PrefabSystem != null)
                {
                    name = m_PrefabSystem.GetPrefabName(item.Prefab);
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = item.Category.ToString();
                }
                name = name.Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (prefabCounts.TryGetValue(name, out int cur))
                {
                    prefabCounts[name] = cur + 1;
                }
                else
                {
                    prefabCounts[name] = 1;
                }
            }

            return ImpactReportLogic.BuildReport(prefabCounts, ImpactReportLogic.DefaultTopCount);
        }

        public string ImpactReportRaw
        {
            get
            {
                if (m_PendingResult == null || m_PendingResult.Items.Count == 0)
                {
                    return string.Empty;
                }

                if (m_CachedImpactRevision == m_ResultRevision && m_CachedImpactReport != null)
                {
                    return m_CachedImpactReport;
                }

                m_CachedImpactReport = BuildImpactReportRaw(m_PendingResult);
                m_CachedImpactRevision = m_ResultRevision;
                return m_CachedImpactReport;
            }
        }

        private string ResolveEntityDisplayName(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                return "Building";
            }

            string name = null;
            if (m_NameSystem != null)
            {
                name = m_NameSystem.GetRenderedLabelName(entity);
            }

            if (string.IsNullOrWhiteSpace(name) && EntityManager.HasComponent<PrefabRef>(entity) && m_PrefabSystem != null)
            {
                Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if (prefab != Entity.Null && EntityManager.Exists(prefab))
                {
                    name = m_PrefabSystem.GetPrefabName(prefab);
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"#{entity.Index}";
            }

            return name.Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            m_InToolUpdate = true;
            try
            {
                return OnUpdateCore(inputDeps);
            }
            finally
            {
                m_InToolUpdate = false;
            }
        }

        private JobHandle OnUpdateCore(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            SyncElevationRequirements();

            if (!Mod.RuntimeActive || Mod.Settings == null || !Mod.Settings.Enabled)
            {
                ClearPending();
                SetToolActive(false);
                return inputDeps;
            }

            if (m_PendingAuditLog != null && m_BatchedDeletion == null)
            {
                m_PendingAuditLog.FramesRemaining--;
                if (m_PendingAuditLog.FramesRemaining <= 0)
                {
                    inputDeps.Complete();
                    FlushPendingAuditLog();
                }
            }

            if (RoadFacilitiesBusy)
            {
                inputDeps.Complete();
                AdvanceRoadFacilities();
                return inputDeps;
            }

            if (m_BatchedDeletion != null)
            {
                if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                {
                    PauseDeletion("stopped");
                    SelectionNotice = "deletion_stopped";
                }
                else
                {
                    // Advance on observation, not on a fixed delay: the previous batch only counts
                    // as done once the game actually stopped reporting its entities as live.
                    if (m_LastBatchOutstanding > 0)
                    {
                        inputDeps.Complete();
                    }

                    BatchAdvance advance = BatchWindowPolicy.Evaluate(
                        m_LastBatchOutstanding,
                        BatchObservedAll(),
                        UnityEngine.Time.realtimeSinceStartup,
                        m_BatchObserveDeadline);

                    if (advance != BatchAdvance.Wait)
                    {
                        inputDeps.Complete();
                        SubmitDeletionBatch(advance);
                    }
                }
                return inputDeps;
            }

            if (m_QueuedDeletion != null)
            {
                inputDeps.Complete();
                SelectionResult queued = m_QueuedDeletion;
                bool skipReview = m_QueuedSkipRevalidation;
                m_QueuedDeletion = null;
                m_QueuedSkipRevalidation = false;
                SubmitConfirmedDeletion(queued, skipReview);
                return inputDeps;
            }

            if (Mod.Settings != null && !m_Session.IsBusy && Mod.Settings.GetSelectionMode() != m_Session.Mode)
            {
                m_Session.SetMode(Mod.Settings.GetSelectionMode());
            }

            m_HasCursorWorld = TryResolveCursor(out m_CursorHitEntity, out m_CursorWorld);
            float2 curScreenPos = ReadMouseScreenPosition();
            UpdatePolygonSnapState(curScreenPos, out bool hasStartScreen, out float2 startScreenPos);
            UpdateLabelViewportPositions();

            bool mouseOverUi = InputManager.instance != null && InputManager.instance.mouseOverUI;
            bool allowInput = MarqueePointerLogic.IsInputAllowed(
                UnityEngine.Application.isFocused,
                InputManager.instance != null && InputManager.instance.hasInputFieldFocus);
            bool hasRetainedEdits = !m_Dragging && m_PendingResult != null && m_PendingResult.HasRetainedEdits;
            bool leftPressedThisFrame = applyAction.WasPressedThisFrame();
            bool shouldSamplePressGround = !HasActiveUndo && allowInput && !ColorModalOpen && !mouseOverUi &&
                                           !m_ImportantModalOpen && m_QueuedDeletion == null &&
                                           !m_Session.IsMouseButtonActive && leftPressedThisFrame && m_HasCursorWorld;
            float3 pressGroundPoint = shouldSamplePressGround
                ? ResolveGroundPointForPress(curScreenPos, m_CursorHitEntity, m_CursorWorld)
                : m_CursorWorld;

            var stepInput = new ToolStepInput
            {
                EscapeGate = m_EscapeGate,
                FrameCount = UnityEngine.Time.frameCount,
                TimeSeconds = UnityEngine.Time.realtimeSinceStartup,
                AllowInput = allowInput,
                MouseOverUi = mouseOverUi,
                ImportantModalOpen = m_ImportantModalOpen,
                ColorModalOpen = ColorModalOpen,
                HasActiveUndo = HasActiveUndo,
                HasQueuedDeletion = m_QueuedDeletion != null,
                HasPending = HasPending,
                PendingAffectsProtected = HasPending && m_PendingResult.AffectsProtectedObject,
                HasRetainedEdits = hasRetainedEdits,
                ExcludeImportantPressed = m_ExcludeImportantAction != null && m_ExcludeImportantAction.WasPressedThisFrame(),
                ForceConfirmImportantPressed = m_ForceConfirmImportantAction != null && m_ForceConfirmImportantAction.WasPressedThisFrame(),
                ClearSelectionPressed = m_ClearSelectionAction != null && m_ClearSelectionAction.WasPressedThisFrame(),
                ConfirmActionPressed = m_ConfirmAction != null && m_ConfirmAction.WasPressedThisFrame(),
                EnterKeyPressed = Keyboard.current != null &&
                                  (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame),
                EscapePressedThisFrame = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame,
                RightPressedThisFrame = cancelAction.WasPressedThisFrame(),
                LeftPressedThisFrame = leftPressedThisFrame,
                LeftStillPressed = applyAction.IsPressed() && !applyAction.WasReleasedThisFrame(),
                HasCursorWorld = m_HasCursorWorld,
                CursorWorldXZ = new CoordXZ(m_CursorWorld.x, m_CursorWorld.z),
                CursorWorldY = m_CursorWorld.y,
                PressGroundWorldXZ = new CoordXZ(pressGroundPoint.x, pressGroundPoint.z),
                PressGroundWorldY = pressGroundPoint.y,
                CursorScreenX = curScreenPos.x,
                CursorScreenY = curScreenPos.y,
                HasStartScreen = hasStartScreen,
                StartScreenX = startScreenPos.x,
                StartScreenY = startScreenPos.y,
            };

            if (Mod.Settings != null)
            {
                m_Session.PolygonDraft.MaxVertices = System.Math.Min(128, System.Math.Max(16, Mod.Settings.MaxPolygonVertices));
            }

            ToolStepResult step = m_Session.StepFrame(in stepInput);

            SyncToolActionStates(
                toolRunning: step.ToolHotkeysAllowed,
                hasPending: step.CanConfirmPending,
                canForceConfirm: step.CanForceConfirm,
                canExcludeImportant: step.CanExcludeImportant,
                canClear: step.CanClear,
                canClosePolygon: step.CanClosePolygonByEnter);

            if (step.ShouldResetHighlights)
            {
                ResetInteractionVisuals();
            }

            if (step.ShouldClearEmptyNotice)
            {
                SelectionNotice = string.Empty;
            }

            switch (step.Outcome)
            {
                case ToolStepOutcome.ExcludeImportant:
                    ExcludeImportantBuildings();
                    break;
                case ToolStepOutcome.ForceConfirmImportant:
                    ForceConfirmPendingDeletion();
                    return inputDeps;
                case ToolStepOutcome.CloseImportantModalWithSound:
                    CancelImportantModal();
                    PlayUiSound(selectEntity: false);
                    break;
                case ToolStepOutcome.ClearSelectionWithSound:
                    ClearPending();
                    PlayUiSound(selectEntity: false);
                    break;
                case ToolStepOutcome.ConfirmPending:
                    ConfirmPendingDeletion();
                    return inputDeps;
                case ToolStepOutcome.DeferredToUiColorModalEscape:
                    return inputDeps;
                case ToolStepOutcome.CancelledActiveInteraction:
                    PlayUiSound(selectEntity: false);
                    break;
                case ToolStepOutcome.RemovedPolygonVertex:
                    m_PolygonVertexCapWarned = false;
                    UpdatePolygonSnapState(curScreenPos);
                    PlayUiSound(selectEntity: false);
                    break;
                case ToolStepOutcome.RemoveEntityAtCursor:
                    HandleSingleClickRemove(m_CursorHitEntity);
                    break;
                case ToolStepOutcome.ExitTool:
                    SetToolActive(false);
                    break;
                case ToolStepOutcome.PolygonAddedVertex:
                    m_LastClosedShapeInvalid = false;
                    m_PolygonVertexCapWarned = false;
                    UpdatePolygonSnapState(curScreenPos);
                    PlayUiSound(selectEntity: true);
                    break;
                case ToolStepOutcome.PolygonVertexLimitReached:
                    m_PolygonVertexCapWarned = true;
                    PlayUiSound(selectEntity: false);
                    break;
                case ToolStepOutcome.CompletePolygonShape:
                    m_PolygonSnapActive = false;
                    m_PolygonVertexCapWarned = false;
                    FinalizeCompletedShape(step.ClosedShape);
                    break;
                case ToolStepOutcome.FreehandStrokeStarted:
                    m_LastClosedShapeInvalid = false;
                    PlayUiSound(selectEntity: true);
                    break;
                case ToolStepOutcome.FreehandStrokeContinue:
                    break;
                case ToolStepOutcome.CompleteFreehandShape:
                    FinalizeCompletedShape(step.ClosedShape);
                    break;
                case ToolStepOutcome.RectanglePressStarted:
                    m_PressHitEntity = m_CursorHitEntity;
                    break;
                case ToolStepOutcome.RectanglePressCleared:
                    m_PressHitEntity = Entity.Null;
                    break;
                case ToolStepOutcome.RectanglePromotedToDrag:
                    m_LastClosedShapeInvalid = false;
                    m_PressHitEntity = Entity.Null;
                    m_PreviewDirty = true;
                    m_PreviewInterval = 0.10f;
                    m_LastPreviewTime = UnityEngine.Time.realtimeSinceStartup;
                    break;
                case ToolStepOutcome.RectangleContinueDrag:
                    {
                        float now = UnityEngine.Time.realtimeSinceStartup;
                        if (now - m_LastPreviewTime >= m_PreviewInterval)
                            UpdateDragPreview(now);
                        break;
                    }
                case ToolStepOutcome.RectangleCompleteDrag:
                    m_PressHitEntity = Entity.Null;
                    CompleteDrag();
                    break;
                case ToolStepOutcome.RectangleSingleClickAdd:
                    {
                        Entity pickTarget = m_CursorHitEntity != Entity.Null ? m_CursorHitEntity : m_PressHitEntity;
                        m_PressHitEntity = Entity.Null;
                        HandleSingleClickAdd(pickTarget);
                        break;
                    }
                case ToolStepOutcome.PickSimilarAtCursor:
                    HandlePickSimilarAtCursor(m_CursorHitEntity);
                    break;
                case ToolStepOutcome.CancelPickSimilarWithSound:
                    PlayUiSound(selectEntity: false);
                    break;
            }

            RenderOverlays();
            return inputDeps;
        }

        private void ResetInteractionVisuals()
        {
            m_PressHitEntity = Entity.Null;
            m_PolygonSnapActive = false;
            m_PolygonVertexCapWarned = false;
            m_PreviewResult = null;
            m_PreviewDirty = false;
            m_PreviewInterval = 0.10f;
            m_LastPreviewShape = default(MarqueeShapeXZ);
            if (!HasPending && m_PendingResult != null && m_PendingResult.HasRetainedEdits)
            {
                SelectionNotice = MarqueePointerLogic.ComputeHiddenRetainedNotice(
                    m_PendingResult.Count,
                    m_PendingResult.Shape.IsUsable(),
                    m_PendingResult.ManuallyAdded.Count);
            }
            SyncHighlights(m_PendingResult);
        }

        private void CancelDrag()
        {
            m_Session.CancelActiveInteraction();
            ResetInteractionVisuals();
        }

        private void UpdateLabelViewportPositions()
        {
            MarqueeShapeXZ shape = ActiveShape;
            if (!shape.IsUsable())
            {
                return;
            }

            Camera cam = ResolveActiveCamera();
            if (cam == null)
            {
                return;
            }

            float y = m_Dragging ? 0.5f * (m_DragStartWorld.y + m_DragEndWorld.y) : m_CursorWorld.y;
            if (!shape.IsRectangleFastPath)
            {
                CoordXZ c = shape.Centroid;
                Vector3 vpC = cam.WorldToViewportPoint(new Vector3(c.X, y, c.Z));
                if (vpC.z > 0f && !float.IsNaN(vpC.x) && !float.IsNaN(vpC.y))
                {
                    float2 pos = new float2(math.clamp(vpC.x, 0.08f, 0.90f), math.clamp(1f - vpC.y, 0.08f, 0.90f));
                    m_WidthLabelViewportPos = pos;
                    m_DepthLabelViewportPos = pos;
                }
                return;
            }

            float cx = 0.25f * (shape.P0.X + shape.P1.X + shape.P2.X + shape.P3.X);
            float cz = 0.25f * (shape.P0.Z + shape.P1.Z + shape.P2.Z + shape.P3.Z);

            // 两条 Width 边 (P0-P1 与 P3-P2) 的中点，沿远离中心方向偏移
            float wOffset = math.clamp(shape.Depth * 0.06f, 2.5f, 10f);
            float2 wDir0 = math.normalizesafe(new float2(0.5f * (shape.P0.X + shape.P1.X) - cx, 0.5f * (shape.P0.Z + shape.P1.Z) - cz));
            float2 wDir1 = math.normalizesafe(new float2(0.5f * (shape.P3.X + shape.P2.X) - cx, 0.5f * (shape.P3.Z + shape.P2.Z) - cz));
            CoordXZ wMid0 = new CoordXZ(
                0.5f * (shape.P0.X + shape.P1.X) + wDir0.x * wOffset,
                0.5f * (shape.P0.Z + shape.P1.Z) + wDir0.y * wOffset);
            CoordXZ wMid1 = new CoordXZ(
                0.5f * (shape.P3.X + shape.P2.X) + wDir1.x * wOffset,
                0.5f * (shape.P3.Z + shape.P2.Z) + wDir1.y * wOffset);

            // 两条 Depth 边 (P0-P3 与 P1-P2) 的中点，沿远离中心方向偏移
            float dOffset = math.clamp(shape.Width * 0.06f, 2.5f, 10f);
            float2 dDir0 = math.normalizesafe(new float2(0.5f * (shape.P0.X + shape.P3.X) - cx, 0.5f * (shape.P0.Z + shape.P3.Z) - cz));
            float2 dDir1 = math.normalizesafe(new float2(0.5f * (shape.P1.X + shape.P2.X) - cx, 0.5f * (shape.P1.Z + shape.P2.Z) - cz));
            CoordXZ dMid0 = new CoordXZ(
                0.5f * (shape.P0.X + shape.P3.X) + dDir0.x * dOffset,
                0.5f * (shape.P0.Z + shape.P3.Z) + dDir0.y * dOffset);
            CoordXZ dMid1 = new CoordXZ(
                0.5f * (shape.P1.X + shape.P2.X) + dDir1.x * dOffset,
                0.5f * (shape.P1.Z + shape.P2.Z) + dDir1.y * dOffset);

            Vector3 vpW0 = cam.WorldToViewportPoint(new Vector3(wMid0.X, y, wMid0.Z));
            Vector3 vpW1 = cam.WorldToViewportPoint(new Vector3(wMid1.X, y, wMid1.Z));
            // 选择屏幕下方（1 - vp.y 更大，即更靠近玩家视角底部）且在视野内的 Width 边框中点
            Vector3 chosenW = (vpW0.z > 0f && (1f - vpW0.y) >= (1f - vpW1.y)) ? vpW0 : (vpW1.z > 0f ? vpW1 : vpW0);
            if (chosenW.z > 0f && !float.IsNaN(chosenW.x) && !float.IsNaN(chosenW.y))
            {
                m_WidthLabelViewportPos = new float2(
                    math.clamp(chosenW.x, 0.06f, 0.92f),
                    math.clamp(1f - chosenW.y, 0.06f, 0.92f));
            }

            Vector3 vpD0 = cam.WorldToViewportPoint(new Vector3(dMid0.X, y, dMid0.Z));
            Vector3 vpD1 = cam.WorldToViewportPoint(new Vector3(dMid1.X, y, dMid1.Z));
            // 选择屏幕右侧（vp.x 更大）且在视野内的 Depth 边框中点
            Vector3 chosenD = (vpD1.z > 0f && vpD1.x >= vpD0.x) ? vpD1 : (vpD0.z > 0f ? vpD0 : vpD1);
            if (chosenD.z > 0f && !float.IsNaN(chosenD.x) && !float.IsNaN(chosenD.y))
            {
                m_DepthLabelViewportPos = new float2(
                    math.clamp(chosenD.x, 0.06f, 0.92f),
                    math.clamp(1f - chosenD.y, 0.06f, 0.92f));
            }
        }

        private void UpdateDragPreview(float now)
        {
            MarqueeShapeXZ shape = BuildRectShape(m_DragStartWorld, m_DragEndWorld);
            if (!shape.IsUsable())
            {
                if (m_PreviewResult != null)
                {
                    m_PreviewResult = null;
                    m_ResultRevision++;
                    SyncHighlights(m_PendingResult);
                }
                m_PreviewDirty = true;
                return;
            }
            if (!m_PreviewDirty && !shape.HasMovedFrom(in m_LastPreviewShape, 0.5f)) return;

            m_PreviewDirty = false;
            m_LastPreviewShape = shape;
            m_LastPreviewTime = now;
            float started = UnityEngine.Time.realtimeSinceStartup;
            m_PreviewResult = Collect(shape, includeCascade: false);
            m_ResultRevision++;
            m_ShapeRevision++;
            float elapsed = UnityEngine.Time.realtimeSinceStartup - started;
            int rawHits = m_PreviewResult.RawHits;
            if (elapsed > 0.025f || rawHits > 6000)
            {
                m_PreviewInterval = math.clamp(math.max(elapsed * 5f, 0.30f), 0.25f, 0.50f);
            }
            else if (elapsed > 0.010f || rawHits > 2500)
            {
                m_PreviewInterval = 0.20f;
            }
            else
            {
                m_PreviewInterval = 0.12f;
            }

            SyncHighlights(m_PreviewResult);
        }

        private bool TryProjectPolygonStartScreen(out float2 startScreen)
        {
            startScreen = float2.zero;
            if (m_Session.PolygonDraft.Count < 3)
            {
                return false;
            }

            Camera cam = ResolveActiveCamera();
            if (cam == null)
            {
                return false;
            }

            CoordXZ xz = m_Session.PolygonDraft.StartVertexXZ;
            float y = m_Session.PolygonDraft.StartVertexY;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(xz.X, y, xz.Z));
            if (sp.z <= 0f || float.IsNaN(sp.x) || float.IsNaN(sp.y))
            {
                return false;
            }

            startScreen = new float2(sp.x, sp.y);
            return true;
        }

        private void UpdatePolygonSnapState(float2 curScreenPos)
        {
            UpdatePolygonSnapState(curScreenPos, out _, out _);
        }

        private void UpdatePolygonSnapState(float2 curScreenPos, out bool hasStartScreen, out float2 startScreen)
        {
            hasStartScreen = false;
            startScreen = float2.zero;
            if (m_Session.Mode != MarqueeSelectionMode.Polygon || m_Session.PolygonDraft.Count < 3)
            {
                m_PolygonSnapActive = false;
                return;
            }

            hasStartScreen = TryProjectPolygonStartScreen(out startScreen);
            m_PolygonSnapActive = m_Session.PolygonDraft.IsSnapToStart(
                curScreenPos.x,
                curScreenPos.y,
                hasStartScreen,
                startScreen.x,
                startScreen.y);
        }

        private void CompleteDrag()
        {
            m_Dragging = false;
            m_PreviewResult = null;
            m_PreviewInterval = 0.10f;
            MarqueeShapeXZ shape = BuildRectShape(m_DragStartWorld, m_DragEndWorld);
            FinalizeCompletedShape(shape);
        }

        private void FinalizeCompletedShape(MarqueeShapeXZ shape)
        {
            InitializePendingFromShape(shape, allowAutoDelete: true);
        }

        private bool InitializePendingFromShape(MarqueeShapeXZ shape, bool allowAutoDelete)
        {
            if (Mod.Settings == null)
            {
                return false;
            }

            if (!shape.IsUsable())
            {
                if (!shape.IsRectangleFastPath)
                {
                    m_LastClosedShapeInvalid = true;
                    m_InvalidShapeMode = shape.Mode;
                    PlayUiSound(selectEntity: false);
                }
                SyncHighlights(m_PendingResult);
                return false;
            }

            m_LastClosedShapeInvalid = false;
            SelectionResult finalResult = Collect(shape, includeCascade: true);
            if (finalResult.FacilityTargets != null)
            {
                SetPending(finalResult);
                if (HasPending) PlayUiSound(selectEntity: true);
                return true;
            }

            if (SelectionRestoreExecutionPolicy.ShouldExecuteAutoDelete(
                    allowAutoDelete,
                    finalResult.EffectiveCount,
                    finalResult.Breakdown.ImportantBuildings,
                    Mod.Settings.ConfirmBeforeDelete,
                    Mod.Settings.LargeSelectionThreshold) &&
                m_InterruptedDeletion == null && !finalResult.AffectsProtectedObject)
            {
                // A refused write must not swallow the selection that was just drawn: keep it pending
                // and say why nothing was submitted instead of clearing it before the gate answers.
                if (!CanSubmitDeletionNow)
                {
                    AppendStatusLine("[State: deletion-blocked, Reason: DeletionSubmit " + TierFor(CompatibilityGroup.DeletionSubmit) + ", Stage: auto]");
                    SetPending(finalResult, changed: false, openImportantModal: false);
                    SelectionNotice = "deletion_blocked_compat";
                    if (HasPending) PlayUiSound(selectEntity: true);
                    return true;
                }

                SetPending(null);
                ExecuteDeletion(finalResult);
                return true;
            }

            bool openImportantModalImmediately = m_InterruptedDeletion == null && !Mod.Settings.ConfirmBeforeDelete &&
                                                 !finalResult.AffectsProtectedObject &&
                                                 finalResult.Breakdown.ImportantBuildings > 0;
            SetPending(finalResult, changed: false, openImportantModal: openImportantModalImmediately);
            if (HasPending) PlayUiSound(selectEntity: true);
            return true;
        }

        private void SubmitConfirmedDeletion(SelectionResult reviewed, bool forceSkipRevalidation = false)
        {
            if (reviewed.FacilityTargets != null)
            {
                ConfirmRoadFacilities(reviewed);
                return;
            }

            Settings settings = Mod.Settings;
            var current = SelectionCollector.Revalidate(World, EntityManager, reviewed, reviewed.Items,
                settings.BuildFilterMask(), settings.BuildSafetyOptions(), m_Underground, reviewed.DeferredNodes);
            bool reviewing = reviewed == m_InterruptedReview;
            bool skipReview = forceSkipRevalidation || (settings != null && settings.SkipBatchRevalidation);

            int threshold = settings.LargeSelectionThreshold;
            bool hasNewImportant = HasNewImportantBuilding(current, reviewed);
            bool planNeedsReview = !skipReview && (current.Plan.ChangesConfirmation(reviewed.Plan)
                || (!ConfirmationPolicy.NeedsLargeSelectionWarning(reviewed.EffectiveCount, threshold) &&
                      ConfirmationPolicy.NeedsLargeSelectionWarning(current.EffectiveCount, threshold)));
            bool needsReview = planNeedsReview || hasNewImportant;

            if (needsReview || current.AffectsProtectedObject)
            {
                bool keepImportantModal = needsReview && !current.AffectsProtectedObject && current.Breakdown.ImportantBuildings > 0;
                SetPending(current, changed: needsReview, openImportantModal: keepImportantModal);
                if (reviewing) m_InterruptedReview = m_PendingResult;
                return;
            }

            // Checked before any state is dropped: a refused write must not consume the preview the
            // player is looking at, nor the interrupted record they could resume from.
            if (!CanSubmitDeletionNow)
            {
                AppendStatusLine("[State: deletion-blocked, Reason: DeletionSubmit " + TierFor(CompatibilityGroup.DeletionSubmit) + ", Stage: confirm]");
                SetPending(current, changed: false, openImportantModal: false);

                // IsReviewingInterruptedDeletion is a reference comparison, so the review marker has to
                // follow the pending result that was just installed.
                if (reviewing) m_InterruptedReview = m_PendingResult;

                SelectionNotice = "deletion_blocked_compat";
                return;
            }

            if (reviewing && current.Plan.Submitted.Count > 0) m_InterruptedDeletion = null;
            SetPending(null);
            ExecuteDeletion(current, skipReview);
        }

        private void ExecuteDeletion(SelectionResult result, bool skipRevalidation = false)
        {
            if (HasActiveUndo) return;

            // Hard gate: the deletion write path is only used when this game build was verified for it.
            // Automatic deletion paths (confirmations the player already accepted) come through here
            // too, so a gate here cannot be bypassed by disabling the prompt.
            if (!CanSubmitDeletionNow)
            {
                AppendStatusLine("[State: deletion-blocked, Reason: DeletionSubmit " + TierFor(CompatibilityGroup.DeletionSubmit) + "]");
                SelectionNotice = "deletion_blocked_compat";
                return;
            }

            if (result.Plan.Submitted.Count == 0) return;

            int entitiesBefore = EntityManager.UniversalQuery.CalculateEntityCount();
            m_PendingAuditLog = new DeletionAuditLog
            {
                Selected = result.Count,
                Affected = result.Plan.Affected.Count,
                EntitiesBefore = entitiesBefore,
                FramesRemaining = 2
            };

            m_UndoManager.ClearFinishedNotice();
            if (result.EffectiveCount > MaxBatchImpact || result.Plan.Submitted.Count > MaxBatchRoots)
            {
                m_UndoManager.PrepareBatchedCapture(EntityManager, result.Plan);
                m_BatchedDeletion = result;
                m_DeletionItems = result.Items
                    .Where(item => !EntityManager.HasComponent<Game.Net.Node>(item.Entity))
                    .OrderBy(DeletionPhase)
                    .ThenBy(item => item.Category == SelectionCategory.Building ? 0 : 1)
                    .ThenBy(item => item.Entity, EntityOrder.ByIndex)
                    .ToList();
                m_DeferredDeletionNodes = Cascade.GetApprovedNodes(EntityManager, result.Plan);
                m_BlockedDeletionNodes = null;
                m_BatchedDeletionUnderground = m_Underground;
                m_NextDeletionItem = 0;
                m_BatchLength = ResolveBatchSize();
                m_LastBatchCostMs = 0;
                m_TaskSkipBatchRevalidation = skipRevalidation || (Mod.Settings != null && Mod.Settings.SkipBatchRevalidation);
                m_LastBatchSubmitted.Clear();
                m_OversizedRoots.Clear();
                m_LastBatchOutstanding = 0;
                m_BatchObserveDeadline = 0f;
                m_TaskRecord = new BatchTaskRecord();
                RecordUnits(result.Plan);
                SelectionNotice = "deleting|" + RemainingText();
            }
            else
            {
                if (!SubmitDeletionPlan(result.Plan, deferUndoCommit: false))
                {
                    SelectionNotice = "deletion_blocked_compat";
                    return;
                }
            }
            bool hasBuildingsOrNets = result.Breakdown.Buildings > 0 || result.Breakdown.Networks > 0 || result.Breakdown.Areas > 0;
            PlayBulldozeSound(hasBuildingsOrNets);
        }

        private int DeletionPhase(SelectedEntityItem item)
        {
            if (item.Category == SelectionCategory.Moving) return 0;
            // Remove buildings/props/trees/decals BEFORE road edges so road-adjacent buildings don't
            // trigger unnecessary orphan cascades or re-review pauses before their own batch runs.
            if (EntityManager.HasComponent<Edge>(item.Entity)) return 2;
            return item.Category == SelectionCategory.Area ? 3 : 1;
        }

        /// <summary>Remaining targets of the running task, for the progress notice.</summary>
        private string RemainingText()
        {
            if (m_DeletionItems == null) return "0";
            int nodes = m_DeferredDeletionNodes != null ? m_DeferredDeletionNodes.Count : 0;
            int remaining = m_DeletionItems.Count + nodes - m_NextDeletionItem;
            return remaining < 0 ? "0" : remaining.ToString();
        }

        /// <summary>Batch size the player configured, clamped to what the setting offers.</summary>
        private static int ResolveBatchSize()
        {
            int configured = Mod.Settings != null ? Mod.Settings.DeletionBatchSize : MaxBatchRoots;
            if (configured < 8) configured = 8;
            if (configured > MaxBatchRoots) configured = MaxBatchRoots;
            return configured;
        }

        /// <summary>Main-thread time a single batch may spend on revalidation and command creation.</summary>
        private static int ResolveBatchBudgetMs()
        {
            int configured = Mod.Settings != null ? Mod.Settings.DeletionBatchBudgetMs : BatchWindowPolicy.DefaultBudgetMs;
            if (configured < 2) configured = 2;
            if (configured > 33) configured = 33;
            return configured;
        }

        /// <summary>True once the game stopped reporting every entity of the previous batch as live.</summary>
        private bool BatchObservedAll()
        {
            if (m_LastBatchOutstanding <= 0) return true;
            for (int i = 0; i < m_LastBatchSubmitted.Count; i++)
            {
                if (IsLiveDeletionTarget(m_LastBatchSubmitted[i])) return false;
            }
            return true;
        }

        private void RecordObservation(DeletionPlan plan)
        {
            m_LastBatchSubmitted.Clear();
            m_LastBatchSubmitted.AddRange(EntityOrder.Sorted(plan.Submitted));
            m_LastBatchOutstanding = m_LastBatchSubmitted.Count;
        }

        private void FinishBatchObservation()
        {
            // The oversized set belongs to the whole task, not to one batch: clearing it here would let
            // a later batch mix the same oversized facility in with unrelated roots.
            m_LastBatchSubmitted.Clear();
            m_LastBatchOutstanding = 0;
            m_BatchObserveDeadline = 0f;
        }

        /// <summary>
        /// Records what the facility units of a plan are: a unit is what has to go together, and a
        /// unit larger than one batch cannot be submitted atomically. That case is flagged for review
        /// instead of being split between batches without the player knowing.
        /// </summary>
        private void RecordUnits(DeletionPlan plan)
        {
            List<DeletionUnit> units = Cascade.BuildUnits(EntityManager, plan);
            int largest = 0;
            int oversized = 0;
            int threshold = Math.Max(64, MaxBatchImpact / 2);
            for (int i = 0; i < units.Count; i++)
            {
                int count = units[i].Count;
                if (count > largest) largest = count;
                if (count > threshold) oversized++;
            }

            m_TaskRecord.LargestUnit = largest;
            m_TaskRecord.OversizedUnits = oversized;
            if (oversized > 0)
            {
                // A single facility larger than a whole batch cannot be split without breaking its
                // connections, so it is submitted on its own and reported separately.
                m_OversizedRoots.Clear();
                for (int i = 0; i < units.Count; i++)
                {
                    if (units[i].Count > threshold) m_OversizedRoots.Add(units[i].Root);
                }

                m_TaskRecord.Review += oversized;
                LogBatchRecord("oversized-unit", m_TaskRecord);
            }
        }

        private void SubmitDeletionBatch(BatchAdvance advance)
        {
            if (m_LastBatchOutstanding > 0)
            {
                int gone = 0;
                for (int i = 0; i < m_LastBatchSubmitted.Count; i++)
                {
                    if (!IsLiveDeletionTarget(m_LastBatchSubmitted[i])) gone++;
                }

                m_TaskRecord.Observed += gone;
                m_TaskRecord.Kept += m_LastBatchSubmitted.Count - gone;
                if (advance == BatchAdvance.TimedOut && gone == 0)
                {
                    if (!m_TaskSkipBatchRevalidation)
                    {
                        // The observation window closed with nothing removed yet. That is a conservative
                        // stop, not proof the game rejected the batch: deletions are deferred and can be
                        // slower than the window. The remainder goes back to the player instead of the tool
                        // deleting on top of a state it has not confirmed.
                        m_TaskRecord.Review += m_LastBatchSubmitted.Count;
                        m_TaskRecord.Unconfirmed += m_LastBatchSubmitted.Count;
                        LogBatchRecord("observe-timeout", m_TaskRecord);
                        FinishBatchObservation();
                        PauseDeletion("changed");
                        SelectionNotice = "deletion_observe_timeout";
                        return;
                    }

                    m_TaskRecord.Unconfirmed += m_LastBatchSubmitted.Count;
                    LogBatchRecord("observe-timeout-skip", m_TaskRecord);
                }

                // A partial result is normal: occupants and deferred removals outlive the batch. Only
                // record it; the next batch revalidates the remainder against live state.
                if (gone < m_LastBatchSubmitted.Count)
                {
                    LogBatchRecord("observe-partial", m_TaskRecord);
                }

                FinishBatchObservation();
            }

            SelectionResult approved = m_BatchedDeletion;
            int rootCount = m_DeletionItems.Count;
            int remaining = rootCount + m_DeferredDeletionNodes.Count - m_NextDeletionItem;
            if (remaining == 0)
            {
                if (m_BlockedDeletionNodes != null)
                {
                    PauseDeletion("changed");
                    SelectionNotice = "deletion_changed";
                }
                else
                {
                    FinishDeletionBatches("complete");
                    SelectionNotice = string.Empty;
                }
                return;
            }

            // A junction may only be eligible once all its approved edges and owners are gone.
            // Retry the original plan's junctions after submitting every ordinary batch.
            bool cleaningNodes = m_NextDeletionItem >= rootCount;
            int nodeOffset = m_NextDeletionItem - rootCount;
            int phase = cleaningNodes ? 4 : DeletionPhase(m_DeletionItems[m_NextDeletionItem]);

            // Size the next batch from the measured cost of the previous one, bounded by the player's
            // batch size and main-thread budget. The cap is the configured size, never the hard maximum,
            // so a small setting cannot be doubled away by the speed probe.
            int configuredCap = ResolveBatchSize();
            if (m_LastBatchCostMs > 0)
            {
                m_BatchLength = BatchWindowPolicy.NextLength(configuredCap, m_BatchLength, m_LastBatchCostMs, ResolveBatchBudgetMs());
            }

            int batchCap = m_BatchLength < 1 ? 1 : m_BatchLength;
            if (batchCap > configuredCap) batchCap = configuredCap;
            int length = cleaningNodes ? Math.Min(batchCap, remaining) : 1;
            while (phase < 3 && length < batchCap && m_NextDeletionItem + length < rootCount &&
                   DeletionPhase(m_DeletionItems[m_NextDeletionItem + length]) == phase)
                length++;

            // An oversized facility goes through on its own, so its cost is measured by itself and a
            // failure cannot take unrelated roots down with it. Everything queued before it still
            // goes first: only the oversized root starts a batch of its own.
            if (!cleaningNodes && m_OversizedRoots.Count > 0)
            {
                for (int i = 0; i < length && m_NextDeletionItem + i < rootCount; i++)
                {
                    if (m_OversizedRoots.Contains(m_DeletionItems[m_NextDeletionItem + i].Entity))
                    {
                        length = i > 0 ? i : 1;
                        break;
                    }
                }
            }

            float batchStarted = UnityEngine.Time.realtimeSinceStartup;

            Settings settings = Mod.Settings;
            SelectionFilterMask filter = settings.BuildFilterMask();
            SafetyOptions safety = settings.BuildSafetyOptions();
            SelectionResult batch;
            while (true)
            {
                batch = cleaningNodes
                    ? SelectionCollector.Revalidate(World, EntityManager, approved, Array.Empty<SelectedEntityItem>(),
                        filter, safety, m_Underground, m_DeferredDeletionNodes.GetRange(nodeOffset, length))
                    : SelectionCollector.Revalidate(World, EntityManager, approved,
                        m_DeletionItems.GetRange(m_NextDeletionItem, length), filter, safety, m_Underground);
                if (batch.EffectiveCount <= MaxBatchImpact || length == 1) break;
                length /= 2;
            }

            if (batch.AffectsProtectedObject ||
                HasNewImportantBuilding(batch, approved) ||
                (!m_TaskSkipBatchRevalidation &&
                 (batch.Plan.ExpandsConfirmation(approved.Plan) ||
                  (!cleaningNodes && MissesLiveStaticTarget(batch, length)))))
            {
                PauseDeletion("changed");
                SelectionNotice = "deletion_changed";
                return;
            }

            if (cleaningNodes)
            {
                for (int i = nodeOffset; i < nodeOffset + length; i++)
                {
                    Entity node = m_DeferredDeletionNodes[i];
                    if (!batch.Plan.Submitted.Contains(node) && IsLiveDeletionTarget(node))
                    {
                        (m_BlockedDeletionNodes ??= new List<Entity>()).Add(node);
                    }
                }
            }

            if (!SubmitDeletionPlan(batch.Plan, deferUndoCommit: true))
            {
                // The gate refused this batch, so nothing was written and the cursor must not move: the
                // batch task stops here and the remainder goes back to the player.
                PauseDeletion("stopped");
                SelectionNotice = "deletion_blocked_compat";
                return;
            }
            m_NextDeletionItem += length;
            m_TaskRecord.Batches++;
            m_TaskRecord.Submitted += batch.Plan.Submitted.Count;
            m_TaskRecord.Pending = Math.Max(0, remaining - length);
            RecordObservation(batch.Plan);

            // Measured cost of the batch itself (revalidation plus command creation), not of the
            // game processing it; that is what the next batch length is derived from.
            int costMs = (int)Math.Round((UnityEngine.Time.realtimeSinceStartup - batchStarted) * 1000f);
            m_LastBatchCostMs = costMs < 1 ? 1 : costMs;
            m_BatchObserveDeadline = UnityEngine.Time.realtimeSinceStartup + (cleaningNodes ? NodeObserveTimeout : BatchObserveTimeout);
            SelectionNotice = "deleting|" + Math.Max(0, remaining - length);
        }

        private bool SubmitDeletionPlan(DeletionPlan plan, bool deferUndoCommit)
        {
            if (plan.Submitted.Count == 0) return true;

            // Final write boundary: every path that can delete an entity goes through here, so the
            // compatibility gate is checked again immediately before the command buffer is filled.
            if (!CanSubmitDeletionNow)
            {
                AppendStatusLine("[State: deletion-blocked, Reason: DeletionSubmit " + TierFor(CompatibilityGroup.DeletionSubmit) + ", Stage: submit]");
                return false;
            }

            // The gateway re-checks the tier right before commands are written. A refusal leaves the
            // barrier's buffer empty, so the cursor must not advance.
            if (!DeletionGate.Submit(TierFor(CompatibilityGroup.DeletionSubmit), EntityManager,
                    m_DeletionBarrier.CreateCommandBuffer(), plan, out _, out string gateReason))
            {
                AppendStatusLine("[State: deletion-blocked, Reason: " + gateReason + ", Stage: gate]");
                return false;
            }

            if (deferUndoCommit)
                m_UndoManager.RecordSubmittedBatch(EntityManager, plan);
            else
                m_UndoManager.CaptureBeforeDelete(EntityManager, plan, deferCommit: false);

            m_PendingAuditLog.Submitted += plan.Submitted.Count;
            return true;
        }

        private bool MissesLiveStaticTarget(SelectionResult batch, int length)
        {
            if (batch.Items.Count == length) return false;

            var kept = new HashSet<Entity>(batch.Entities);
            for (int i = m_NextDeletionItem; i < m_NextDeletionItem + length; i++)
            {
                SelectedEntityItem item = m_DeletionItems[i];
                if (item.Category != SelectionCategory.Moving && IsLiveDeletionTarget(item.Entity) &&
                    !kept.Contains(item.Entity))
                    return true;
            }
            return false;
        }

        private bool IsLiveDeletionTarget(Entity entity) =>
            EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);

        /// <summary>
        /// Counts the facility units the remaining items belong to. Units come from the approved plan,
        /// so the number shown to the player is the same grouping the batch used, not a re-guess from
        /// the leftover list.
        /// </summary>
        private void CountRemainingUnits(List<SelectedEntityItem> roots, out int unitCount, out int largestUnit)
        {
            unitCount = 0;
            largestUnit = 0;
            if (roots == null || roots.Count == 0) return;

            List<DeletionUnit> units = m_BatchedDeletion != null ? m_BatchedDeletion.Plan.Units : null;
            if (units == null || units.Count == 0) return;

            var unitOf = new Dictionary<Entity, int>();
            var unitSize = new int[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                unitSize[i] = units[i].Count;
                List<Entity> members = units[i].Members;
                for (int j = 0; j < members.Count; j++)
                {
                    unitOf[members[j]] = i;
                }
            }

            var itemUnit = new int[roots.Count];
            for (int i = 0; i < roots.Count; i++)
            {
                itemUnit[i] = unitOf.TryGetValue(roots[i].Entity, out int unit) ? unit : -1;
            }

            UnitRemainder.Count(itemUnit, itemUnit.Length, unitSize, unitSize.Length, out unitCount, out largestUnit);
        }

        private void PauseDeletion(string reason)
        {
            if (m_BatchedDeletion == null) return;

            int rootsTotal = m_DeletionItems.Count;
            var unitAlive = new bool[rootsTotal];
            for (int i = 0; i < rootsTotal; i++)
            {
                unitAlive[i] = IsLiveDeletionTarget(m_DeletionItems[i].Entity);
            }

            var deferred = m_DeferredDeletionNodes;
            var nodeAlive = new bool[deferred.Count];
            for (int i = 0; i < nodeAlive.Length; i++)
            {
                nodeAlive[i] = IsLiveDeletionTarget(deferred[i]);
            }

            // Collected by index, so the same pause point always yields the same remainder list no
            // matter in which order the game happens to report entities.
            var unitIndices = new List<int>();
            var nodeIndices = new List<int>();
            InterruptedCapture.Capture(m_NextDeletionItem, rootsTotal, unitAlive, nodeAlive,
                unitIndices, nodeIndices, out int unitRemainder, out int nodeRemainder);

            var roots = new List<SelectedEntityItem>(unitIndices.Count);
            for (int i = 0; i < unitIndices.Count; i++)
            {
                roots.Add(m_DeletionItems[unitIndices[i]]);
            }

            var nodes = new List<Entity>(nodeIndices.Count + (m_BlockedDeletionNodes != null ? m_BlockedDeletionNodes.Count : 0));
            var seen = new HashSet<Entity>();
            if (m_BlockedDeletionNodes != null)
            {
                for (int i = 0; i < m_BlockedDeletionNodes.Count; i++)
                {
                    Entity node = m_BlockedDeletionNodes[i];
                    if (IsLiveDeletionTarget(node) && seen.Add(node)) nodes.Add(node);
                }
            }

            for (int i = 0; i < nodeIndices.Count; i++)
            {
                Entity node = deferred[nodeIndices[i]];
                if (seen.Add(node)) nodes.Add(node);
            }

            if (roots.Count + nodes.Count > 0)
            {
                CountRemainingUnits(roots, out int unitCount, out int largestUnit);
                m_InterruptedDeletion = new InterruptedDeletion
                {
                    Approved = m_BatchedDeletion,
                    Items = roots,
                    Nodes = nodes,
                    Underground = m_BatchedDeletionUnderground,
                    UnitCount = unitCount,
                    LargestUnit = largestUnit
                };
            }

            if (m_TaskRecord != null)
            {
                m_TaskRecord.Pending = unitRemainder + nodeRemainder + (m_BlockedDeletionNodes != null ? m_BlockedDeletionNodes.Count : 0);

                // The cursor already moved past everything this task submitted, so those entities are
                // not in the returned selection. A pause is not a cancellation: count what was written
                // but never observed gone, separately from what was never submitted.
                int unconfirmed = 0;
                for (int i = 0; i < m_LastBatchSubmitted.Count; i++)
                {
                    if (IsLiveDeletionTarget(m_LastBatchSubmitted[i])) unconfirmed++;
                }
                if (unconfirmed > 0) m_TaskRecord.Unconfirmed = unconfirmed;

                LogBatchRecord(reason, m_TaskRecord);
            }

            FinishDeletionBatches(reason);
        }

        public void ReviewInterruptedDeletion()
        {
            if (m_InterruptedDeletion == null || HasQueuedDeletion || HasActiveUndo ||
                IsPointerBusy || IsMouseButtonBusy || ImportantModalOpen || ColorModalOpen) return;

            InterruptedDeletion interrupted = m_InterruptedDeletion;
            Settings settings = Mod.Settings;
            var result = SelectionCollector.Revalidate(World, EntityManager, interrupted.Approved, interrupted.Items,
                settings.BuildFilterMask(), settings.BuildSafetyOptions(), interrupted.Underground, interrupted.Nodes);

            if (result.Plan.Submitted.Count == 0)
            {
                SetPending(null);
                SelectionNotice = "resume_unavailable";
                return;
            }

            SetUndergroundState(interrupted.Underground);
            SetPending(result);
            m_InterruptedReview = m_PendingResult;
        }

        public void ContinueInterruptedDeletion(bool skipRevalidation = true)
        {
            if (m_InterruptedDeletion == null || HasQueuedDeletion || HasActiveUndo ||
                IsPointerBusy || IsMouseButtonBusy || ImportantModalOpen || ColorModalOpen) return;

            InterruptedDeletion interrupted = m_InterruptedDeletion;
            Settings settings = Mod.Settings;
            var result = SelectionCollector.Revalidate(World, EntityManager, interrupted.Approved, interrupted.Items,
                settings.BuildFilterMask(), settings.BuildSafetyOptions(), interrupted.Underground, interrupted.Nodes);

            if (result.Plan.Submitted.Count == 0)
            {
                m_InterruptedDeletion = null;
                m_InterruptedReview = null;
                SetPending(null);
                SelectionNotice = "resume_unavailable";
                return;
            }

            bool hasNewImportant = HasNewImportantBuilding(result, interrupted.Approved);
            if (result.AffectsProtectedObject || hasNewImportant)
            {
                SetUndergroundState(interrupted.Underground);
                SetPending(
                    result,
                    changed: hasNewImportant && !result.AffectsProtectedObject,
                    openImportantModal: hasNewImportant && !result.AffectsProtectedObject && result.Breakdown.ImportantBuildings > 0);
                m_InterruptedReview = m_PendingResult;
                return;
            }

            if (!CanSubmitDeletionNow)
            {
                AppendStatusLine("[State: deletion-blocked, Reason: DeletionSubmit " + TierFor(CompatibilityGroup.DeletionSubmit) + ", Stage: continue]");
                SetUndergroundState(interrupted.Underground);
                SetPending(result);
                m_InterruptedReview = m_PendingResult;
                SelectionNotice = "deletion_blocked_compat";
                return;
            }

            m_InterruptedDeletion = null;
            m_InterruptedReview = null;
            SetPending(null);
            ExecuteDeletion(result, skipRevalidation);
            PlayUiSound(selectEntity: true);
        }

        public void DiscardInterruptedDeletion()
        {
            if (m_InterruptedDeletion == null || HasQueuedDeletion || ImportantModalOpen) return;

            if (IsReviewingInterruptedDeletion) SetPending(null);
            m_InterruptedDeletion = null;
            if (SelectionNotice == "resume_unavailable" || SelectionNotice == "deletion_stopped" ||
                SelectionNotice == "deletion_changed" || SelectionNotice == "deletion_observe_timeout")
                SelectionNotice = string.Empty;
        }

        private void FinishDeletionBatches(string state)
        {
            FinishBatchObservation();
            if (m_BatchedDeletion == null) return;

            m_BatchedDeletion = null;
            m_DeletionItems = null;
            m_DeferredDeletionNodes = null;
            m_BlockedDeletionNodes = null;
            m_UndoManager.CommitDeletionCapture();
            if (m_PendingAuditLog != null)
            {
                if (m_TaskRecord != null)
                {
                    m_PendingAuditLog.Extra = DescribeRecord(m_TaskRecord);
                }
                m_PendingAuditLog.State = state;
                m_PendingAuditLog.FramesRemaining = 2;
            }
        }

        private void FlushPendingAuditLog()
        {
            if (m_PendingAuditLog == null || m_BatchedDeletion != null)
            {
                return;
            }

            DeletionAuditLog pending = m_PendingAuditLog;
            m_PendingAuditLog = null;
            if (!Mod.RuntimeActive || World == null || !World.IsCreated)
            {
                return;
            }

            try
            {
                int entitiesAfter = EntityManager.UniversalQuery.CalculateEntityCount();
                LogDeletionStatus(
                    pending.Selected,
                    pending.Affected,
                    pending.Submitted,
                    pending.EntitiesBefore,
                    entitiesAfter,
                    pending.State,
                    pending.Extra);
            }
            catch
            {
                // Ignore world teardown races when stopping the tool during scene/game exit.
            }
        }

        private static string DescribeRecord(BatchTaskRecord record)
        {
            // Pending is what was never submitted; Unconfirmed is what was submitted but not observed
            // gone. A pause is not a cancellation, so the two must never be reported as one number.
            return $"TaskPending: {record.Pending}, TaskUnconfirmed: {record.Unconfirmed}, TaskSubmitted: {record.Submitted}, " +
                   $"TaskObserved: {record.Observed}, TaskKept: {record.Kept}, TaskReview: {record.Review}, " +
                   $"Batches: {record.Batches}, OversizedUnits: {record.OversizedUnits}, LargestUnit: {record.LargestUnit}";
        }

        private static void LogBatchRecord(string state, BatchTaskRecord record)
        {
            if (record == null) return;
            AppendStatusLine($"[State: {state}, {DescribeRecord(record)}]");
        }

        internal static void LogDeletionStatus(int selected, int affected, int submitted, int entitiesBefore, int entitiesAfter, string state, string extra = null)
        {
            int reducedApprox = entitiesBefore - entitiesAfter;
            string entry = $"[State: {state}, Selected: {selected}, EstimatedAffected: {affected}, Submitted: {submitted}, EntitiesBefore: {entitiesBefore}, EntitiesAfter: {entitiesAfter}, ReducedApprox: {reducedApprox}";
            if (!string.IsNullOrEmpty(extra))
            {
                entry += ", " + extra;
            }
            AppendStatusLine(entry + "]");
        }

        /// <summary>
        /// Destructive deletion is only written when the build is verified; a preview-only detection
        /// keeps the tool read-only. The predicate itself is the one covered by the offline regression.
        /// </summary>
        internal static bool CanSubmitDeletionNow =>
            DeletionGate.TryClaimSubmit(TierFor(CompatibilityGroup.DeletionSubmit), out _);

        /// <summary>
        /// Rebuilding entities is a write too, so it needs the same verified build as a delete: a
        /// preview-only detection may inspect what an undo would restore but never emit it.
        /// </summary>
        internal static bool CanRebuildNow =>
            DeletionGate.TryClaimRebuild(TierFor(CompatibilityGroup.UndoRebuild), out _);

        // Test seam. The offline harness cannot reach Unity's reflection layer, so a test pins the tier
        // here and exercises the same predicates production asks. Production never sets it.
        private static bool s_TierOverrideActive;
        private static int s_TierSearchAndPick;
        private static int s_TierDeletionSubmit;
        private static int s_TierUndoRebuild;
        private static int s_TierUiAccess;

        internal static int TierFor(CompatibilityGroup group)
        {
            if (s_TierOverrideActive)
            {
                switch (group)
                {
                    case CompatibilityGroup.SearchAndPick: return s_TierSearchAndPick;
                    case CompatibilityGroup.DeletionSubmit: return s_TierDeletionSubmit;
                    case CompatibilityGroup.UndoRebuild: return s_TierUndoRebuild;
                    default: return s_TierUiAccess;
                }
            }

            return (int)Compatibility.GetTier(group);
        }

        internal static void OverrideTierForTest(CompatibilityGroup group, CompatibilityTier tier)
        {
            s_TierOverrideActive = true;
            switch (group)
            {
                case CompatibilityGroup.SearchAndPick: s_TierSearchAndPick = (int)tier; break;
                case CompatibilityGroup.DeletionSubmit: s_TierDeletionSubmit = (int)tier; break;
                case CompatibilityGroup.UndoRebuild: s_TierUndoRebuild = (int)tier; break;
                default: s_TierUiAccess = (int)tier; break;
            }
        }

        internal static void ResetTierOverrideForTest()
        {
            s_TierOverrideActive = false;
            s_TierSearchAndPick = 0;
            s_TierDeletionSubmit = 0;
            s_TierUndoRebuild = 0;
            s_TierUiAccess = 0;
        }

        private const long MaxStatusLogBytes = 512 * 1024;

        internal static void AppendStatusLine(string entry)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {entry}";

            // Primary path: %LOCALAPPDATA%Low/Colossal Order/Cities Skylines II/Logs/MarqueeDelete.status.log
            // Secondary fallback: Application.persistentDataPath/Logs/MarqueeDelete.status.log
            // Tertiary fallback: ./MarqueeDelete.status.log
            string[] candidatePaths = GetLogCandidatePaths();
            foreach (string path in candidatePaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    string full = System.IO.Path.GetFullPath(path);
                    string dir = System.IO.Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                    }
                    if (System.IO.File.Exists(full))
                    {
                        var info = new System.IO.FileInfo(full);
                        if (info.Length >= MaxStatusLogBytes)
                        {
                            string backup = full + ".old";
                            if (System.IO.File.Exists(backup))
                            {
                                System.IO.File.Delete(backup);
                            }
                            System.IO.File.Move(full, backup);
                        }
                    }
                    System.IO.File.AppendAllText(full, line + Environment.NewLine);
                    // Fixed write to primary path, return immediately on success
                    return;
                }
                catch
                {
                    // Fall back to next candidate if writing to current path fails
                }
            }
        }

        private static string[] GetLogCandidatePaths()
        {
            var list = new List<string>(3);
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    list.Add(System.IO.Path.Combine(localAppData + "Low", "Colossal Order", "Cities Skylines II", "Logs", "MarqueeDelete.status.log"));
                }
            }
            catch {}
            try
            {
                if (!string.IsNullOrEmpty(Application.persistentDataPath))
                {
                    list.Add(System.IO.Path.Combine(Application.persistentDataPath, "Logs", "MarqueeDelete.status.log"));
                }
            }
            catch {}
            list.Add("MarqueeDelete.status.log");
            return list.ToArray();
        }

        private static bool HasNewImportantBuilding(SelectionResult current, SelectionResult reviewed)
        {
            foreach (var item in current.ImportantEntities)
            {
                if (!reviewed.ImportantEntities.TryGetValue(item.Key, out var reviewedKind) ||
                    (item.Value & ~reviewedKind) != 0)
                    return true;
            }
            return false;
        }

        public bool RequestUndo()
        {
            if (m_Dragging || HasQueuedDeletion || IsEditingRoadFacilities)
            {
                return false;
            }

            if (!Mod.RuntimeActive || (Mod.Settings != null && !Mod.Settings.Enabled))
            {
                return false;
            }

            // Hard gate: rebuild writes are only used when this game build still has the interfaces.
            if (!CanRebuildNow)
            {
                AppendStatusLine("[State: undo-blocked, Reason: UndoRebuild " + TierFor(CompatibilityGroup.UndoRebuild) + "]");
                return false;
            }

            if (!m_UndoManager.StartUndo(CurrentUndoMask))
            {
                return false;
            }

            ClearPending();
            PlayUiSound(selectEntity: true);
            return true;
        }

        public bool CancelActiveUndo()
        {
            if (!m_UndoManager.CancelActiveUndo())
            {
                return false;
            }

            PlayUiSound(selectEntity: false);
            return true;
        }

        public bool StopUndo()
        {
            if (!m_UndoManager.StopActiveUndo())
            {
                return false;
            }

            PlayUiSound(selectEntity: false);
            return true;
        }

        public bool DismissUndoFinishedNotice()
        {
            return m_UndoManager.DismissFinishedNotice();
        }

        public void TickUndo(ToolOutputBarrier barrier)
        {
            if (barrier == null)
            {
                return;
            }

            if (!Mod.RuntimeActive)
            {
                m_UndoRefreshDelayFrames = 0;
                m_UndoRefreshBuildingPositions.Clear();
                m_UndoRefreshNetPositions.Clear();
                m_UndoDiagDelayFrames = 0;
                m_UndoDiagBuildingPositions.Clear();
                if (m_UndoManager.IsInProgress)
                {
                    m_UndoManager.ClearHistory();
                }
                return;
            }

            if (m_UndoRefreshDelayFrames > 0)
            {
                m_UndoRefreshDelayFrames--;
                if (m_UndoRefreshDelayFrames == 0)
                {
                    FlushUndoConnectionRefresh(barrier);
                }
            }

            if (m_UndoDiagDelayFrames > 0)
            {
                m_UndoDiagDelayFrames--;
                if (m_UndoDiagDelayFrames == 0)
                {
                    LogUndoRefreshDiagnostics();
                }
            }

            if (!m_UndoManager.IsInProgress)
            {
                return;
            }

            if (Mod.Settings != null && !Mod.Settings.Enabled)
            {
                m_UndoManager.CancelActiveUndo();
                return;
            }

            m_UndoManager.Step(EntityManager, barrier.CreateCommandBuffer(), out bool completed);
            if (completed)
            {
                PlayUiSound(selectEntity: true);
                if (m_UndoManager.TryConsumeConnectionRefresh(m_UndoRefreshBuildingPositions, m_UndoRefreshNetPositions))
                {
                    m_UndoRefreshDelayFrames = 4;
                }
            }
        }

        private struct EntityBoundsEntry
        {
            public Entity Entity;
            public Bounds3 Bounds;
        }

        private struct AabbBoundsCollectIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public float MinX, MaxX, MinZ, MaxZ;
            public List<EntityBoundsEntry> Results;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return bounds.m_Bounds.max.x >= MinX && bounds.m_Bounds.min.x <= MaxX &&
                       bounds.m_Bounds.max.z >= MinZ && bounds.m_Bounds.min.z <= MaxZ;
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (Intersect(bounds))
                {
                    Results.Add(new EntityBoundsEntry { Entity = item, Bounds = bounds.m_Bounds });
                }
            }
        }

        private struct RestoredRoadCurve
        {
            public Bezier4x3 Curve;
            public Bounds3 Bounds;
        }

        private struct AabbEntityCollectIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public float MinX, MaxX, MinZ, MaxZ;
            public List<Entity> Results;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return bounds.m_Bounds.max.x >= MinX && bounds.m_Bounds.min.x <= MaxX &&
                       bounds.m_Bounds.max.z >= MinZ && bounds.m_Bounds.min.z <= MaxZ;
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (Intersect(bounds))
                {
                    Results.Add(item);
                }
            }
        }

        private static long RefreshCellKey(float x, float z, float cellSize)
        {
            int cx = (int)math.floor(x / cellSize);
            int cz = (int)math.floor(z / cellSize);
            return ((long)cx << 32) ^ (uint)cz;
        }

        private static long RefreshCellKeyInt(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        private void MarkEntityUpdated(EntityCommandBuffer ecb, HashSet<Entity> marked, Entity entity)
        {
            if (entity != Entity.Null &&
                EntityManager.Exists(entity) &&
                !EntityManager.HasComponent<Deleted>(entity) &&
                !EntityManager.HasComponent<Temp>(entity) &&
                marked.Add(entity))
            {
                ecb.AddComponent<Updated>(entity);
            }
        }

        private void MarkBuildingGraphUpdated(EntityCommandBuffer ecb, HashSet<Entity> marked, Entity root, bool includeUpgrades, int depth = 0)
        {
            MarkEntityUpdated(ecb, marked, root);

            if (EntityManager.HasBuffer<Game.Buildings.SpawnLocationElement>(root))
            {
                var spawns = EntityManager.GetBuffer<Game.Buildings.SpawnLocationElement>(root, true);
                for (int i = 0; i < spawns.Length; i++)
                {
                    MarkEntityUpdated(ecb, marked, spawns[i].m_SpawnLocation);
                }
            }

            if (EntityManager.HasBuffer<Game.Net.SubNet>(root))
            {
                var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(root, true);
                for (int i = 0; i < subNets.Length; i++)
                {
                    Entity subNet = subNets[i].m_SubNet;
                    MarkEntityUpdated(ecb, marked, subNet);
                    if (subNet != Entity.Null && EntityManager.Exists(subNet) &&
                        !EntityManager.HasComponent<Deleted>(subNet) &&
                        EntityManager.HasComponent<Edge>(subNet))
                    {
                        Edge subEdge = EntityManager.GetComponentData<Edge>(subNet);
                        MarkEntityUpdated(ecb, marked, subEdge.m_Start);
                        MarkEntityUpdated(ecb, marked, subEdge.m_End);
                    }
                }
            }

            if (EntityManager.HasBuffer<Game.Areas.SubArea>(root))
            {
                var subAreas = EntityManager.GetBuffer<Game.Areas.SubArea>(root, true);
                for (int i = 0; i < subAreas.Length; i++)
                {
                    MarkEntityUpdated(ecb, marked, subAreas[i].m_Area);
                }
            }

            if (depth < 2 && EntityManager.HasBuffer<Game.Objects.SubObject>(root))
            {
                var subObjs = EntityManager.GetBuffer<Game.Objects.SubObject>(root, true);
                for (int i = 0; i < subObjs.Length; i++)
                {
                    Entity subObj = subObjs[i].m_SubObject;
                    if (subObj == Entity.Null || !EntityManager.Exists(subObj) ||
                        EntityManager.HasComponent<Deleted>(subObj) ||
                        EntityManager.HasComponent<Temp>(subObj) ||
                        EntityManager.HasComponent<Game.Buildings.Building>(subObj))
                    {
                        continue;
                    }
                    MarkBuildingGraphUpdated(ecb, marked, subObj, includeUpgrades: false, depth: depth + 1);
                }
            }

            if (includeUpgrades && EntityManager.HasBuffer<Game.Buildings.InstalledUpgrade>(root))
            {
                var upgrades = EntityManager.GetBuffer<Game.Buildings.InstalledUpgrade>(root, true);
                for (int i = 0; i < upgrades.Length; i++)
                {
                    Entity upg = upgrades[i].m_Upgrade;
                    if (upg != Entity.Null && EntityManager.Exists(upg) && !EntityManager.HasComponent<Deleted>(upg))
                    {
                        MarkBuildingGraphUpdated(ecb, marked, upg, includeUpgrades: false, depth: depth + 1);
                    }
                }
            }
        }

        private void MarkConnectedBoundaryEdges(EntityCommandBuffer ecb, HashSet<Entity> marked, Entity node)
        {
            if (node == Entity.Null || !EntityManager.Exists(node) ||
                EntityManager.HasComponent<Deleted>(node) ||
                !EntityManager.HasBuffer<ConnectedEdge>(node))
            {
                return;
            }

            var connected = EntityManager.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < connected.Length; i++)
            {
                Entity adj = connected[i].m_Edge;
                if (adj == Entity.Null || !EntityManager.Exists(adj) ||
                    EntityManager.HasComponent<Deleted>(adj) ||
                    EntityManager.HasComponent<Temp>(adj) ||
                    EntityManager.HasComponent<Owner>(adj))
                {
                    continue;
                }
                MarkEntityUpdated(ecb, marked, adj);
            }
        }

        private void FlushUndoConnectionRefresh(ToolOutputBarrier barrier = null)
        {
            if (m_UndoRefreshBuildingPositions.Count == 0 && m_UndoRefreshNetPositions.Count == 0)
            {
                return;
            }

            barrier ??= m_DeletionBarrier ?? World.GetExistingSystemManaged<ToolOutputBarrier>();
            if (barrier == null)
            {
                m_UndoRefreshBuildingPositions.Clear();
                m_UndoRefreshNetPositions.Clear();
                return;
            }

            // Compute tight world AABB around restored positions and bucket targets spatially so
            // refresh runs in O(log N + K) over the undone block instead of scanning the entire city.
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            const float bldCellSize = 32f;
            var bldGrid = new Dictionary<long, List<float3>>();
            for (int i = 0; i < m_UndoRefreshBuildingPositions.Count; i++)
            {
                float3 p = m_UndoRefreshBuildingPositions[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
                long key = RefreshCellKey(p.x, p.z, bldCellSize);
                if (!bldGrid.TryGetValue(key, out var bucket)) bldGrid[key] = bucket = new List<float3>(4);
                bucket.Add(p);
            }

            const float netCellSize = 16f;
            var netGrid = new Dictionary<long, List<float3>>();
            for (int i = 0; i < m_UndoRefreshNetPositions.Count; i++)
            {
                float3 p = m_UndoRefreshNetPositions[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
                long key = RefreshCellKey(p.x, p.z, netCellSize);
                if (!netGrid.TryGetValue(key, out var bucket)) netGrid[key] = bucket = new List<float3>(4);
                bucket.Add(p);
            }

            EntityCommandBuffer ecb = barrier.CreateCommandBuffer();
            var marked = new HashSet<Entity>(128);
            var netCandidates = new List<Entity>(128);
            var restoredRoadCurves = new List<RestoredRoadCurve>(Math.Max(16, m_UndoRefreshNetPositions.Count));
            const float roadCurveCellSize = 64f;
            var roadCurveGrid = new Dictionary<long, List<int>>();
            float objMinX = minX, objMaxX = maxX, objMinZ = minZ, objMaxZ = maxZ;

            // Stage 1: Resolve live restored road edges + roads adjacent to restored buildings first,
            // collecting the exact live Bezier curves/bounds of every restored road so Stage 2 can
            // reconnect surviving roadside buildings that were NOT deleted along with the road.
            var netSearch = World.GetExistingSystemManaged<Game.Net.SearchSystem>();
            if (netSearch != null)
            {
                var tree = netSearch.GetNetSearchTree(true, out JobHandle deps);
                deps.Complete();
                var iter = new AabbEntityCollectIterator
                {
                    MinX = minX - 64f,
                    MaxX = maxX + 64f,
                    MinZ = minZ - 64f,
                    MaxZ = maxZ + 64f,
                    Results = netCandidates,
                };
                tree.Iterate(ref iter);
                netSearch.AddNetSearchTreeReader(default(JobHandle));

                for (int i = 0; i < netCandidates.Count; i++)
                {
                    Entity edge = netCandidates[i];
                    if (!EntityManager.Exists(edge) ||
                        EntityManager.HasComponent<Deleted>(edge) ||
                        EntityManager.HasComponent<Temp>(edge) ||
                        !EntityManager.HasComponent<Edge>(edge) ||
                        !EntityManager.HasComponent<Curve>(edge))
                    {
                        continue;
                    }

                    Bezier4x3 c = EntityManager.GetComponentData<Curve>(edge).m_Bezier;
                    float3 mid = 0.125f * c.a + 0.375f * c.b + 0.375f * c.c + 0.125f * c.d;
                    bool matchedRestoredNet = false;
                    bool matchedNearBuilding = false;

                    if (netGrid.Count > 0)
                    {
                        int ncx = (int)math.floor(mid.x / netCellSize);
                        int ncz = (int)math.floor(mid.z / netCellSize);
                        for (int dx = -1; dx <= 1 && !matchedRestoredNet; dx++)
                        {
                            for (int dz = -1; dz <= 1 && !matchedRestoredNet; dz++)
                            {
                                if (!netGrid.TryGetValue(RefreshCellKeyInt(ncx + dx, ncz + dz), out var bucket)) continue;
                                for (int j = 0; j < bucket.Count; j++)
                                {
                                    if (math.distancesq(mid, bucket[j]) <= 9.0f)
                                    {
                                        matchedRestoredNet = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!matchedRestoredNet && bldGrid.Count > 0)
                    {
                        int bcx = (int)math.floor(mid.x / bldCellSize);
                        int bcz = (int)math.floor(mid.z / bldCellSize);
                        for (int dx = -1; dx <= 1 && !matchedNearBuilding; dx++)
                        {
                            for (int dz = -1; dz <= 1 && !matchedNearBuilding; dz++)
                            {
                                if (!bldGrid.TryGetValue(RefreshCellKeyInt(bcx + dx, bcz + dz), out var bucket)) continue;
                                for (int j = 0; j < bucket.Count; j++)
                                {
                                    float3 target = bucket[j];
                                    if (math.distancesq(mid, target) <= 1024f ||
                                        math.distancesq(c.a, target) <= 1024f ||
                                        math.distancesq(c.d, target) <= 1024f)
                                    {
                                        matchedNearBuilding = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (matchedRestoredNet || matchedNearBuilding)
                    {
                        MarkEntityUpdated(ecb, marked, edge);
                        Edge edgeComp = EntityManager.GetComponentData<Edge>(edge);
                        MarkEntityUpdated(ecb, marked, edgeComp.m_Start);
                        MarkEntityUpdated(ecb, marked, edgeComp.m_End);
                        if (matchedRestoredNet)
                        {
                            MarkConnectedBoundaryEdges(ecb, marked, edgeComp.m_Start);
                            MarkConnectedBoundaryEdges(ecb, marked, edgeComp.m_End);

                            Bounds3 roadBounds = MathUtils.Bounds(c);
                            int curveIdx = restoredRoadCurves.Count;
                            restoredRoadCurves.Add(new RestoredRoadCurve { Curve = c, Bounds = roadBounds });
                            if (roadBounds.min.x < objMinX) objMinX = roadBounds.min.x;
                            if (roadBounds.max.x > objMaxX) objMaxX = roadBounds.max.x;
                            if (roadBounds.min.z < objMinZ) objMinZ = roadBounds.min.z;
                            if (roadBounds.max.z > objMaxZ) objMaxZ = roadBounds.max.z;

                            int rMinX = (int)math.floor((roadBounds.min.x - UndoRefreshLogic.RoadNeighborCenterRadius) / roadCurveCellSize);
                            int rMaxX = (int)math.floor((roadBounds.max.x + UndoRefreshLogic.RoadNeighborCenterRadius) / roadCurveCellSize);
                            int rMinZ = (int)math.floor((roadBounds.min.z - UndoRefreshLogic.RoadNeighborCenterRadius) / roadCurveCellSize);
                            int rMaxZ = (int)math.floor((roadBounds.max.z + UndoRefreshLogic.RoadNeighborCenterRadius) / roadCurveCellSize);
                            for (int rx = rMinX; rx <= rMaxX; rx++)
                            {
                                for (int rz = rMinZ; rz <= rMaxZ; rz++)
                                {
                                    long rKey = RefreshCellKeyInt(rx, rz);
                                    if (!roadCurveGrid.TryGetValue(rKey, out var rBucket)) roadCurveGrid[rKey] = rBucket = new List<int>(4);
                                    rBucket.Add(curveIdx);
                                }
                            }
                        }
                    }
                }
            }

            // Stage 2: Refresh ALL restored buildings AND all surviving buildings/SubNet hosts adjacent
            // to restored roads so RoadConnectionSystem, GenerateEdgesSystem (LocalConnect),
            // ReferencesSystem, LaneConnectionSystem, SpawnLocationConnectionSystem, and
            // ConnectionWarningSystem reconnect them in the same Modification cycle.
            m_UndoDiagBuildingPositions.Clear();
            m_UndoDiagRestoredCount = 0;
            m_UndoDiagNeighborCount = 0;

            if (m_UndoRefreshBuildingPositions.Count > 0 || restoredRoadCurves.Count > 0)
            {
                var objSearch = World.GetExistingSystemManaged<Game.Objects.SearchSystem>();
                if (objSearch != null)
                {
                    var objCandidates = new List<EntityBoundsEntry>(128);
                    float objPad = restoredRoadCurves.Count > 0 ? UndoRefreshLogic.RoadNeighborCenterRadius : 4f;
                    var tree = objSearch.GetStaticSearchTree(true, out JobHandle deps);
                    deps.Complete();
                    var iter = new AabbBoundsCollectIterator
                    {
                        MinX = objMinX - objPad,
                        MaxX = objMaxX + objPad,
                        MinZ = objMinZ - objPad,
                        MaxZ = objMaxZ + objPad,
                        Results = objCandidates,
                    };
                    tree.Iterate(ref iter);
                    objSearch.AddStaticSearchTreeReader(default(JobHandle));

                    var testedRoadIndices = new HashSet<int>();
                    for (int i = 0; i < objCandidates.Count; i++)
                    {
                        EntityBoundsEntry entry = objCandidates[i];
                        Entity entity = entry.Entity;
                        if (!EntityManager.Exists(entity) ||
                            EntityManager.HasComponent<Deleted>(entity) ||
                            EntityManager.HasComponent<Temp>(entity) ||
                            !EntityManager.HasComponent<Game.Objects.Transform>(entity))
                        {
                            continue;
                        }

                        bool isBuilding = EntityManager.HasComponent<Game.Buildings.Building>(entity);
                        bool isRootHost = !EntityManager.HasComponent<Owner>(entity) &&
                                          (EntityManager.HasBuffer<Game.Net.SubNet>(entity) ||
                                           EntityManager.HasComponent<Game.Objects.SpawnLocation>(entity));
                        if (!isBuilding && !isRootHost)
                        {
                            continue;
                        }

                        float3 pos = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
                        bool matchedRestored = false;
                        if (isBuilding && bldGrid.Count > 0)
                        {
                            int cx = (int)math.floor(pos.x / bldCellSize);
                            int cz = (int)math.floor(pos.z / bldCellSize);
                            for (int dx = -1; dx <= 1 && !matchedRestored; dx++)
                            {
                                for (int dz = -1; dz <= 1 && !matchedRestored; dz++)
                                {
                                    if (!bldGrid.TryGetValue(RefreshCellKeyInt(cx + dx, cz + dz), out var bucket)) continue;
                                    for (int j = 0; j < bucket.Count; j++)
                                    {
                                        float3 target = bucket[j];
                                        float ddx = pos.x - target.x;
                                        float ddz = pos.z - target.z;
                                        if (ddx * ddx + ddz * ddz <= 1.0f)
                                        {
                                            matchedRestored = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        bool matchedRoadNeighbor = false;
                        if (!matchedRestored && restoredRoadCurves.Count > 0)
                        {
                            bool hasNoRoadEdge = isBuilding && EntityManager.GetComponentData<Game.Buildings.Building>(entity).m_RoadEdge == Entity.Null;
                            int rcx = (int)math.floor(pos.x / roadCurveCellSize);
                            int rcz = (int)math.floor(pos.z / roadCurveCellSize);
                            int bMinX = (int)math.floor(entry.Bounds.min.x / roadCurveCellSize);
                            int bMaxX = (int)math.floor(entry.Bounds.max.x / roadCurveCellSize);
                            int bMinZ = (int)math.floor(entry.Bounds.min.z / roadCurveCellSize);
                            int bMaxZ = (int)math.floor(entry.Bounds.max.z / roadCurveCellSize);
                            int cellMinX = Math.Min(rcx, bMinX);
                            int cellMaxX = Math.Max(rcx, bMaxX);
                            int cellMinZ = Math.Min(rcz, bMinZ);
                            int cellMaxZ = Math.Max(rcz, bMaxZ);

                            testedRoadIndices.Clear();
                            for (int gx = cellMinX; gx <= cellMaxX && !matchedRoadNeighbor; gx++)
                            {
                                for (int gz = cellMinZ; gz <= cellMaxZ && !matchedRoadNeighbor; gz++)
                                {
                                    if (!roadCurveGrid.TryGetValue(RefreshCellKeyInt(gx, gz), out var rBucket)) continue;
                                    for (int r = 0; r < rBucket.Count; r++)
                                    {
                                        int curveIdx = rBucket[r];
                                        if (!testedRoadIndices.Add(curveIdx)) continue;
                                        RestoredRoadCurve rc = restoredRoadCurves[curveIdx];
                                        if (!UndoRefreshLogic.BoundsOverlapXZ(
                                                entry.Bounds.min.x, entry.Bounds.max.x, entry.Bounds.min.z, entry.Bounds.max.z,
                                                rc.Bounds.min.x, rc.Bounds.max.x, rc.Bounds.min.z, rc.Bounds.max.z,
                                                UndoRefreshLogic.RoadNeighborCenterRadius))
                                        {
                                            continue;
                                        }

                                        float centerDist = MathUtils.Distance(rc.Curve.xz, pos.xz, out float tCenter);
                                        float3 closestOnRoad = MathUtils.Position(rc.Curve, tCenter);
                                        float2 clampedBounds = math.clamp(closestOnRoad.xz, entry.Bounds.min.xz, entry.Bounds.max.xz);
                                        float boundsDist = MathUtils.Distance(rc.Curve.xz, clampedBounds, out _);
                                        if (UndoRefreshLogic.IsBuildingAdjacentToRoad(centerDist, boundsDist, hasNoRoadEdge))
                                        {
                                            matchedRoadNeighbor = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (matchedRestored || matchedRoadNeighbor)
                        {
                            MarkBuildingGraphUpdated(ecb, marked, entity, includeUpgrades: true);
                            if (isBuilding)
                            {
                                m_UndoDiagBuildingPositions.Add(pos);
                                if (matchedRestored) m_UndoDiagRestoredCount++;
                                else m_UndoDiagNeighborCount++;
                            }
                        }
                    }
                }
            }

            if (m_UndoDiagBuildingPositions.Count > 0)
            {
                m_UndoDiagDelayFrames = 4;
            }

            m_UndoRefreshBuildingPositions.Clear();
            m_UndoRefreshNetPositions.Clear();
        }

        private void LogUndoRefreshDiagnostics()
        {
            if (m_UndoDiagBuildingPositions.Count == 0) return;

            try
            {
                var objSearch = World.GetExistingSystemManaged<Game.Objects.SearchSystem>();
                if (objSearch == null) return;

                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                const float cellSize = 32f;
                var grid = new Dictionary<long, List<float3>>();
                for (int i = 0; i < m_UndoDiagBuildingPositions.Count; i++)
                {
                    float3 p = m_UndoDiagBuildingPositions[i];
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.z < minZ) minZ = p.z;
                    if (p.z > maxZ) maxZ = p.z;
                    long key = RefreshCellKey(p.x, p.z, cellSize);
                    if (!grid.TryGetValue(key, out var bucket)) grid[key] = bucket = new List<float3>(4);
                    bucket.Add(p);
                }

                Entity carWarnPrefab = Entity.Null;
                Entity pedWarnPrefab = Entity.Null;
                Entity roadWarnPrefab = Entity.Null;
                using (var cfgQ = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Game.Prefabs.TrafficConfigurationData>()))
                {
                    if (!cfgQ.IsEmptyIgnoreFilter)
                    {
                        var cfg = cfgQ.GetSingleton<Game.Prefabs.TrafficConfigurationData>();
                        carWarnPrefab = cfg.m_CarConnectionNotification;
                        pedWarnPrefab = cfg.m_PedestrianConnectionNotification;
                        roadWarnPrefab = cfg.m_RoadConnectionNotification;
                    }
                }

                var candidates = new List<Entity>(128);
                var tree = objSearch.GetStaticSearchTree(true, out JobHandle deps);
                deps.Complete();
                var iter = new AabbEntityCollectIterator
                {
                    MinX = minX - 4f,
                    MaxX = maxX + 4f,
                    MinZ = minZ - 4f,
                    MaxZ = maxZ + 4f,
                    Results = candidates,
                };
                tree.Iterate(ref iter);
                objSearch.AddStaticSearchTreeReader(default(JobHandle));

                int matchedBuildings = 0;
                int noRoadEdge = 0;
                int unlinkedSpawns = 0;
                int carWarns = 0;
                int pedWarns = 0;
                int roadWarns = 0;

                for (int i = 0; i < candidates.Count; i++)
                {
                    Entity entity = candidates[i];
                    if (!EntityManager.Exists(entity) ||
                        EntityManager.HasComponent<Deleted>(entity) ||
                        EntityManager.HasComponent<Temp>(entity) ||
                        !EntityManager.HasComponent<Game.Buildings.Building>(entity) ||
                        !EntityManager.HasComponent<Game.Objects.Transform>(entity))
                    {
                        continue;
                    }

                    float3 pos = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
                    int cx = (int)math.floor(pos.x / cellSize);
                    int cz = (int)math.floor(pos.z / cellSize);
                    bool matched = false;
                    for (int dx = -1; dx <= 1 && !matched; dx++)
                    {
                        for (int dz = -1; dz <= 1 && !matched; dz++)
                        {
                            if (!grid.TryGetValue(RefreshCellKeyInt(cx + dx, cz + dz), out var bucket)) continue;
                            for (int j = 0; j < bucket.Count; j++)
                            {
                                float3 t = bucket[j];
                                float ddx = pos.x - t.x;
                                float ddz = pos.z - t.z;
                                if (ddx * ddx + ddz * ddz <= 1.0f) { matched = true; break; }
                            }
                        }
                    }
                    if (!matched) continue;

                    matchedBuildings++;
                    if (EntityManager.GetComponentData<Game.Buildings.Building>(entity).m_RoadEdge == Entity.Null)
                    {
                        noRoadEdge++;
                    }
                    if (EntityManager.HasBuffer<Game.Buildings.SpawnLocationElement>(entity))
                    {
                        var spawns = EntityManager.GetBuffer<Game.Buildings.SpawnLocationElement>(entity, true);
                        for (int s = 0; s < spawns.Length; s++)
                        {
                            Entity sl = spawns[s].m_SpawnLocation;
                            if (sl != Entity.Null && EntityManager.Exists(sl) &&
                                EntityManager.HasComponent<Game.Objects.SpawnLocation>(sl) &&
                                EntityManager.GetComponentData<Game.Objects.SpawnLocation>(sl).m_ConnectedLane1 == Entity.Null)
                            {
                                unlinkedSpawns++;
                            }
                        }
                    }
                    CountConnectionIcons(entity, carWarnPrefab, pedWarnPrefab, roadWarnPrefab, ref carWarns, ref pedWarns, ref roadWarns);
                }

                AppendStatusLine(
                    $"[State: undo-refresh-check, Buildings: {matchedBuildings}, RestoredBld: {m_UndoDiagRestoredCount}, NeighborBld: {m_UndoDiagNeighborCount}, " +
                    $"NoRoadEdge: {noRoadEdge}, UnlinkedSpawns: {unlinkedSpawns}, CarWarn: {carWarns}, PedWarn: {pedWarns}, RoadWarn: {roadWarns}]");
            }
            catch (Exception ex)
            {
                AppendStatusLine($"[State: undo-refresh-check-error, Message: {ex.Message}]");
            }
            finally
            {
                m_UndoDiagBuildingPositions.Clear();
            }
        }

        private void CountConnectionIcons(
            Entity root,
            Entity carWarnPrefab,
            Entity pedWarnPrefab,
            Entity roadWarnPrefab,
            ref int carWarns,
            ref int pedWarns,
            ref int roadWarns)
        {
            if (EntityManager.HasBuffer<Game.Notifications.IconElement>(root))
            {
                var icons = EntityManager.GetBuffer<Game.Notifications.IconElement>(root, true);
                for (int i = 0; i < icons.Length; i++)
                {
                    Entity icon = icons[i].m_Icon;
                    if (icon == Entity.Null || !EntityManager.Exists(icon) || !EntityManager.HasComponent<PrefabRef>(icon)) continue;
                    Entity p = EntityManager.GetComponentData<PrefabRef>(icon).m_Prefab;
                    if (p == carWarnPrefab) carWarns++;
                    else if (p == pedWarnPrefab) pedWarns++;
                    else if (p == roadWarnPrefab) roadWarns++;
                }
            }
            if (EntityManager.HasBuffer<Game.Net.SubNet>(root))
            {
                var subNets = EntityManager.GetBuffer<Game.Net.SubNet>(root, true);
                for (int i = 0; i < subNets.Length; i++)
                {
                    Entity sn = subNets[i].m_SubNet;
                    if (sn == Entity.Null || !EntityManager.Exists(sn) || !EntityManager.HasBuffer<Game.Notifications.IconElement>(sn)) continue;
                    var icons = EntityManager.GetBuffer<Game.Notifications.IconElement>(sn, true);
                    for (int k = 0; k < icons.Length; k++)
                    {
                        Entity icon = icons[k].m_Icon;
                        if (icon == Entity.Null || !EntityManager.Exists(icon) || !EntityManager.HasComponent<PrefabRef>(icon)) continue;
                        Entity p = EntityManager.GetComponentData<PrefabRef>(icon).m_Prefab;
                        if (p == carWarnPrefab) carWarns++;
                        else if (p == pedWarnPrefab) pedWarns++;
                        else if (p == roadWarnPrefab) roadWarns++;
                    }
                }
            }
        }

        private Camera ResolveActiveCamera()
        {
            if (m_CameraUpdateSystem != null && m_CameraUpdateSystem.activeCamera != null)
            {
                return m_CameraUpdateSystem.activeCamera;
            }
            return Camera.main;
        }

        private static bool HasCameraMovedSignificantly(Camera cam, ref float3 lastPos, ref float3 lastFwd, ref bool hasLast)
        {
            if (cam == null) return false;
            float3 pos = cam.transform.position;
            float3 fwd = cam.transform.forward;
            if (!hasLast)
            {
                lastPos = pos;
                lastFwd = fwd;
                hasLast = true;
                return true;
            }

            if (math.distancesq(pos, lastPos) > 0.0625f || math.dot(fwd, lastFwd) < 0.9995f)
            {
                lastPos = pos;
                lastFwd = fwd;
                return true;
            }

            return false;
        }

        private MarqueeShapeXZ BuildRectShape(float3 startWorld, float3 endWorld)
        {
            bool cameraAligned = Mod.Settings != null && Mod.Settings.CameraAlignedMarquee;
            CoordXZ camFwd = new CoordXZ(0f, 1f);
            Camera cam = ResolveActiveCamera();
            if (cam != null)
            {
                UnityEngine.Transform tr = cam.transform;
                Vector3 right = tr.right;
                Vector3 fwd = tr.forward;
                Vector3 up = tr.up;
                camFwd = MarqueeShapeXZ.ResolveCameraForwardXZ(right.x, right.z, fwd.x, fwd.z, up.x, up.z);
            }

            return MarqueeShapeXZ.FromPoints(
                new CoordXZ(startWorld.x, startWorld.z),
                new CoordXZ(endWorld.x, endWorld.z),
                camFwd,
                cameraAligned);
        }

        private static float2 ReadMouseScreenPosition()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return float2.zero;
            }
            Vector2 pos = mouse.position.ReadValue();
            return new float2(pos.x, pos.y);
        }

        private float3 ResolveGroundPointForPress(float2 screenPos, Entity hitEntity, float3 fallbackWorld)
        {
            if (hitEntity == Entity.Null || m_TerrainSystem == null)
            {
                return fallbackWorld;
            }

            Camera cam = ResolveActiveCamera();
            if (cam == null)
            {
                return fallbackWorld;
            }

            TerrainHeightData heightData = default(TerrainHeightData);
            bool hasHeightData = false;
            try
            {
                heightData = m_TerrainSystem.GetHeightData(waitForPending: false);
                hasHeightData = heightData.isCreated;
                if (!hasHeightData)
                {
                    return fallbackWorld;
                }

                Ray ray = cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
                float prevT = 1f;
                float step = 3f;
                for (int i = 0; i < 160 && prevT < 15000f; i++)
                {
                    float nextT = prevT + step;
                    Vector3 sample = ray.origin + ray.direction * nextT;
                    float3 pos = new float3(sample.x, sample.y, sample.z);
                    float terrainY = TerrainUtils.SampleHeight(ref heightData, pos);
                    if (!float.IsNaN(terrainY) && !float.IsInfinity(terrainY) && pos.y <= terrainY)
                    {
                        float lo = prevT;
                        float hi = nextT;
                        for (int b = 0; b < 10; b++)
                        {
                            float mid = 0.5f * (lo + hi);
                            Vector3 midVec = ray.origin + ray.direction * mid;
                            float midTerrain = TerrainUtils.SampleHeight(ref heightData, new float3(midVec.x, midVec.y, midVec.z));
                            if (midVec.y <= midTerrain) hi = mid;
                            else lo = mid;
                        }

                        Vector3 finalVec = ray.origin + ray.direction * hi;
                        float finalY = TerrainUtils.SampleHeight(ref heightData, new float3(finalVec.x, finalVec.y, finalVec.z));
                        float3 resolved = new float3(finalVec.x, float.IsFinite(finalY) ? finalY : fallbackWorld.y, finalVec.z);
                        if (IsFinite(resolved))
                        {
                            return resolved;
                        }
                        break;
                    }

                    prevT = nextT;
                    step = math.min(250f, step * 1.06f);
                }
            }
            finally
            {
                if (hasHeightData)
                {
                    m_TerrainSystem.AddCPUHeightReader(default(JobHandle));
                }
            }

            return fallbackWorld;
        }

        private void HandlePickSimilarAtCursor(Entity pickedEntity)
        {
            if (HasActiveUndo || m_ImportantModalOpen || !HasPending)
            {
                SelectionNotice = "similar_no_target";
                PlayUiSound(selectEntity: false);
                return;
            }

            SelectionResult next = SelectionCollector.FilterPendingBySimilarEntity(
                EntityManager,
                m_PendingResult,
                pickedEntity,
                out MatchSimilarOutcome outcome,
                out int matchedCount);

            switch (outcome)
            {
                case MatchSimilarOutcome.Matched:
                    SetPending(next, changed: false, openImportantModal: false);
                    SelectionNotice = $"similar_matched|{matchedCount}";
                    PlayUiSound(selectEntity: true);
                    break;
                case MatchSimilarOutcome.NoPrefab:
                    SelectionNotice = "similar_no_prefab";
                    PlayUiSound(selectEntity: false);
                    break;
                case MatchSimilarOutcome.NoMatchesInSelection:
                    SelectionNotice = "similar_no_match";
                    PlayUiSound(selectEntity: false);
                    break;
                default:
                    SelectionNotice = "similar_no_target";
                    PlayUiSound(selectEntity: false);
                    break;
            }
        }

        private void HandleSingleClickAdd(Entity pickedEntity)
        {
            if (IsEditingRoadFacilities || Mod.Settings == null || HasActiveUndo || m_ImportantModalOpen ||
                IsReviewingInterruptedDeletion || pickedEntity == Entity.Null)
            {
                return;
            }

            SelectionResult next = SelectionCollector.AddSingleEntity(
                World,
                EntityManager,
                m_PendingResult,
                pickedEntity,
                Mod.Settings.BuildFilterMask(),
                Mod.Settings.BuildSafetyOptions(),
                m_Underground,
                out bool added,
                out _);

            if (!added || next == null)
            {
                return;
            }

            SetPending(next, changed: false, openImportantModal: false);
            PlayUiSound(selectEntity: true);
        }

        private void HandleSingleClickRemove(Entity pickedEntity)
        {
            if (IsEditingRoadFacilities || HasActiveUndo || m_ImportantModalOpen || !HasPending ||
                IsReviewingInterruptedDeletion || pickedEntity == Entity.Null)
            {
                return;
            }

            SelectionResult next = SelectionCollector.RemoveSingleEntity(
                EntityManager,
                m_PendingResult,
                pickedEntity,
                out bool removed);

            if (!removed)
            {
                return;
            }

            SetPending(next);
            PlayUiSound(selectEntity: false);
        }

        private bool TryResolveCursor(out Entity hitEntity, out float3 worldPos)
        {
            hitEntity = Entity.Null;
            if (GetRaycastResult(out Entity entity, out RaycastHit hit))
            {
                if (IsFinite(hit.m_HitPosition))
                {
                    hitEntity = entity;
                    worldPos = hit.m_HitPosition;
                    return true;
                }
            }

            Camera cam = ResolveActiveCamera();
            Mouse mouse = Mouse.current;
            if (cam != null && mouse != null)
            {
                Vector2 screenPos = mouse.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
                float fallbackY = m_Dragging ? m_DragStartWorld.y : (m_HasCursorWorld ? m_CursorWorld.y : 0f);
                if (math.abs(ray.direction.y) > 1e-4f)
                {
                    float t = (fallbackY - ray.origin.y) / ray.direction.y;
                    if (t > 0.1f && t < 50000f)
                    {
                        Vector3 hitVec = ray.origin + ray.direction * t;
                        worldPos = new float3(hitVec.x, fallbackY, hitVec.z);
                        return true;
                    }
                }
            }

            worldPos = default(float3);
            return false;
        }

        private void SyncHighlights(SelectionResult selection)
        {
            PrepareAssetOverlays(selection);

            m_DesiredHighlights.Clear();
            if (selection != null)
            {
                foreach (var item in selection.Items)
                {
                    Entity e = item.Entity;
                    bool alive = EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);
                    if (AssetOverlayLogic.ShouldApplyNativeHighlight(
                            item.Category,
                            m_OverflowFallbackEntities.Contains(e),
                            alive))
                    {
                        m_DesiredHighlights.Add(e);
                    }
                }
            }

            m_ToRemoveHighlights.Clear();
            foreach (Entity existing in m_Highlighted)
            {
                if (!m_DesiredHighlights.Contains(existing))
                {
                    m_ToRemoveHighlights.Add(existing);
                }
            }

            if (m_ToRemoveHighlights.Count == 0 && m_DesiredHighlights.Count == 0)
            {
                return;
            }

            // ToolOutputBarrier.CreateCommandBuffer() throws outside ToolSystem update phase
            // (e.g. OnGamePreload / UI triggers), so only batch via ECB during OnUpdate.
            bool useEcb = m_InToolUpdate && m_DeletionBarrier != null && World != null && World.IsCreated;
            bool hasEcb = false;
            EntityCommandBuffer ecb = default(EntityCommandBuffer);

            for (int i = 0; i < m_ToRemoveHighlights.Count; i++)
            {
                Entity e = m_ToRemoveHighlights[i];
                m_Highlighted.Remove(e);
                if (m_OwnedHighlights.Remove(e) && EntityManager.Exists(e))
                {
                    if (EntityManager.HasComponent<Highlighted>(e))
                    {
                        if (useEcb)
                        {
                            if (!hasEcb) { ecb = m_DeletionBarrier.CreateCommandBuffer(); hasEcb = true; }
                            ecb.RemoveComponent<Highlighted>(e);
                        }
                        else EntityManager.RemoveComponent<Highlighted>(e);
                    }
                    if (!EntityManager.HasComponent<Deleted>(e) && !EntityManager.HasComponent<BatchesUpdated>(e))
                    {
                        if (useEcb)
                        {
                            if (!hasEcb) { ecb = m_DeletionBarrier.CreateCommandBuffer(); hasEcb = true; }
                            ecb.AddComponent<BatchesUpdated>(e);
                        }
                        else EntityManager.AddComponent<BatchesUpdated>(e);
                    }
                }
            }

            foreach (Entity next in m_DesiredHighlights)
            {
                if (m_Highlighted.Add(next))
                {
                    if (!EntityManager.HasComponent<Highlighted>(next))
                    {
                        if (useEcb)
                        {
                            if (!hasEcb) { ecb = m_DeletionBarrier.CreateCommandBuffer(); hasEcb = true; }
                            ecb.AddComponent<Highlighted>(next);
                        }
                        else EntityManager.AddComponent<Highlighted>(next);
                        m_OwnedHighlights.Add(next);
                    }
                    if (!EntityManager.HasComponent<BatchesUpdated>(next))
                    {
                        if (useEcb)
                        {
                            if (!hasEcb) { ecb = m_DeletionBarrier.CreateCommandBuffer(); hasEcb = true; }
                            ecb.AddComponent<BatchesUpdated>(next);
                        }
                        else EntityManager.AddComponent<BatchesUpdated>(next);
                    }
                }
            }
        }

        private void RenderOverlays()
        {
            if (m_OverlayRenderSystem == null)
            {
                return;
            }

            bool drawBox = m_Dragging || (m_PendingResult != null && m_PendingResult.Shape.IsUsable());
            bool drawPolyDraft = m_Session.Mode == MarqueeSelectionMode.Polygon && m_Session.HasPolygonDraft;
            bool drawFreehandDraft = m_Session.FreehandDrawing && m_Session.FreehandDraft.HasDraft;
            if (!drawBox && !drawPolyDraft && !drawFreehandDraft && !m_HasCursorWorld && !HasPending)
            {
                return;
            }

            // Refresh 1600-primitive viewport/proximity split BEFORE acquiring OverlayRenderSystem.Buffer
            // so any Highlighted fallback component additions happen cleanly outside buffer lock.
            if (PrepareAssetOverlays(CurrentResult))
            {
                SyncHighlights(CurrentResult);
            }

            TerrainHeightData heightData = default(TerrainHeightData);
            bool hasHeightData = false;
            if (m_TerrainSystem != null)
            {
                heightData = m_TerrainSystem.GetHeightData(waitForPending: false);
                hasHeightData = heightData.isCreated;
            }

            OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle deps);
            deps.Complete();

            try
            {
                ResolvedOverlayColors colors = GetResolvedColors();
                float time = UnityEngine.Time.realtimeSinceStartup;
                bool terrainDirtyHint = ComputeTerrainDirtyHint();
                float pulse = 0.5f + 0.5f * math.sin(time * 6f);
                float borderOutlineWidth = colors.OutlineAlpha <= 0.001f ? 0f : 0.2f;
                UnityEngine.Color baseBorderRgb = m_Underground ? colors.MarqueeUndergroundBorder : colors.MarqueeGroundBorder;
                UnityEngine.Color baseFillRgb = m_Underground ? colors.MarqueeUndergroundFill : colors.MarqueeGroundFill;

                if (drawBox)
                {
                    MarqueeShapeXZ shape = m_Dragging
                        ? BuildRectShape(m_DragStartWorld, m_DragEndWorld)
                        : m_PendingResult.Shape;

                    if (shape.IsFinite())
                    {
                        float fallbackY = m_Dragging ? m_DragStartWorld.y : (m_HasCursorWorld ? m_CursorWorld.y : 0f);
                        float span = shape.IsRectangleFastPath
                            ? math.max(shape.Width, shape.Depth)
                            : math.max(shape.MaxX - shape.MinX, shape.MaxZ - shape.MinZ);
                        float lineWidth = math.clamp(span * 0.015f, 0.45f, 2.2f);
                        float cornerDiameter = math.clamp(lineWidth * 2.1f, 0.9f, 4.0f);

                        UnityEngine.Color borderColor = new UnityEngine.Color(
                            baseBorderRgb.r,
                            baseBorderRgb.g,
                            baseBorderRgb.b,
                            m_Dragging ? 0.93f : 0.98f);
                        UnityEngine.Color glowColor = new UnityEngine.Color(borderColor.r, borderColor.g, borderColor.b, 0.26f);
                        UnityEngine.Color outlineColor = colors.DarkOutline;

                        if (shape.IsRectangleFastPath)
                        {
                            float3 c0 = SampleCorner(ref heightData, hasHeightData, shape.P0, fallbackY);
                            float3 c1 = SampleCorner(ref heightData, hasHeightData, shape.P1, fallbackY);
                            float3 c2 = SampleCorner(ref heightData, hasHeightData, shape.P2, fallbackY);
                            float3 c3 = SampleCorner(ref heightData, hasHeightData, shape.P3, fallbackY);

                            // Soft Semi-Transparent Fill (dial back red wash when building zone overlays are shown)
                            float3 leftMid = (c0 + c3) * 0.5f;
                            float3 rightMid = (c1 + c2) * 0.5f;
                            bool hasZoneBuildings = !m_Underground && CurrentResult != null && CurrentResult.Breakdown.Buildings > 0;
                            float fillScale = m_Underground
                                ? (m_Dragging ? 1.11f : 1.55f)
                                : (hasZoneBuildings
                                    ? (m_Dragging ? 0.28f : 0.45f)
                                    : (m_Dragging ? 0.67f : 1.0f));
                            float boxFillAlpha = math.clamp(colors.MarqueeFillAlpha * fillScale, 0f, 0.85f);
                            UnityEngine.Color boxFillTint = new UnityEngine.Color(baseFillRgb.r, baseFillRgb.g, baseFillRgb.b, boxFillAlpha);
                            if (shape.Depth > 0.25f && shape.Width > 0.25f && boxFillAlpha > 0.001f)
                            {
                                buffer.DrawLine(
                                    boxFillTint,
                                    boxFillTint,
                                    0f,
                                    OverlayRenderSystem.StyleFlags.Projected,
                                    new Line3.Segment(leftMid, rightMid),
                                    shape.Depth,
                                    new float2(0f, 0f));
                            }

                            // 8m Cell Grid Lines inside the box (derived from border color)
                            float stepW = MarqueeShapeXZ.CellSizeMeters;
                            while (shape.Width / stepW > 14f) stepW *= 2f;
                            float stepD = MarqueeShapeXZ.CellSizeMeters;
                            while (shape.Depth / stepD > 14f) stepD *= 2f;

                            UnityEngine.Color gridRgb = UnityEngine.Color.Lerp(baseBorderRgb, UnityEngine.Color.white, 0.28f);
                            UnityEngine.Color gridColor = new UnityEngine.Color(gridRgb.r, gridRgb.g, gridRgb.b, 0.18f);
                            float gridLineWidth = math.clamp(lineWidth * 0.26f, 0.12f, 0.32f);

                            for (float d = stepW; d < shape.Width - 0.5f; d += stepW)
                            {
                                float t = d / shape.Width;
                                float3 gA = math.lerp(c0, c1, t);
                                float3 gB = math.lerp(c3, c2, t);
                                buffer.DrawLine(gridColor, gridColor, 0f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(gA, gB), gridLineWidth, new float2(0f, 0f));
                            }
                            for (float d = stepD; d < shape.Depth - 0.5f; d += stepD)
                            {
                                float t = d / shape.Depth;
                                float3 gA = math.lerp(c0, c3, t);
                                float3 gB = math.lerp(c1, c2, t);
                                buffer.DrawLine(gridColor, gridColor, 0f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(gA, gB), gridLineWidth, new float2(0f, 0f));
                            }

                            // Dual-Layer Smooth Border & Edges (glow derived from border color)
                            DrawProjectedEdge(ref buffer, glowColor, glowColor, lineWidth * 2.1f, c0, c1, 0f);
                            DrawProjectedEdge(ref buffer, glowColor, glowColor, lineWidth * 2.1f, c1, c2, 0f);
                            DrawProjectedEdge(ref buffer, glowColor, glowColor, lineWidth * 2.1f, c2, c3, 0f);
                            DrawProjectedEdge(ref buffer, glowColor, glowColor, lineWidth * 2.1f, c3, c0, 0f);

                            DrawProjectedEdge(ref buffer, outlineColor, borderColor, lineWidth, c0, c1, borderOutlineWidth);
                            DrawProjectedEdge(ref buffer, outlineColor, borderColor, lineWidth, c1, c2, borderOutlineWidth);
                            DrawProjectedEdge(ref buffer, outlineColor, borderColor, lineWidth, c2, c3, borderOutlineWidth);
                            DrawProjectedEdge(ref buffer, outlineColor, borderColor, lineWidth, c3, c0, borderOutlineWidth);

                            DrawCornerMarker(ref buffer, outlineColor, borderColor, c0, cornerDiameter, borderOutlineWidth);
                            DrawCornerMarker(ref buffer, outlineColor, borderColor, c1, cornerDiameter, borderOutlineWidth);
                            DrawCornerMarker(ref buffer, outlineColor, borderColor, c2, cornerDiameter, borderOutlineWidth);
                            DrawCornerMarker(ref buffer, outlineColor, borderColor, c3, cornerDiameter, borderOutlineWidth);
                        }
                        else if (shape.VertexCount >= 3)
                        {
                            m_ClosedOverlayCache.EnsureForShape(
                                in shape,
                                m_ClosedShapeVersion,
                                ref heightData,
                                hasHeightData,
                                terrainDirtyHint,
                                time,
                                fallbackY);

                            float fillAlpha = ComputeShapeFillAlpha(m_Dragging, in colors);
                            UnityEngine.Color fillTint = new UnityEngine.Color(baseFillRgb.r, baseFillRgb.g, baseFillRgb.b, fillAlpha);
                            UnityEngine.Color gridRgb = UnityEngine.Color.Lerp(baseBorderRgb, UnityEngine.Color.white, 0.28f);
                            UnityEngine.Color gridColor = new UnityEngine.Color(gridRgb.r, gridRgb.g, gridRgb.b, 0.18f);
                            float gridLineWidth = math.clamp(lineWidth * 0.26f, 0.12f, 0.32f);

                            DrawCachedShapeFillAndGrid(ref buffer, m_ClosedOverlayCache, fillTint, gridColor, gridLineWidth);
                            DrawCachedOutlineSegments(ref buffer, m_ClosedOverlayCache, outlineColor, glowColor, borderColor, lineWidth, borderOutlineWidth);

                            int vCount = m_ClosedOverlayCache.VertexPositions3D.Count;
                            bool drawAllMarkers = shape.Mode == MarqueeSelectionMode.Polygon || vCount <= 24;
                            for (int i = 0; i < vCount; i++)
                            {
                                if (drawAllMarkers || (i & 1) == 0)
                                {
                                    DrawCornerMarker(
                                        ref buffer,
                                        outlineColor,
                                        borderColor,
                                        m_ClosedOverlayCache.VertexPositions3D[i],
                                        cornerDiameter * 0.85f,
                                        borderOutlineWidth);
                                }
                            }
                        }
                    }
                }

                if (drawPolyDraft)
                {
                    DrawLivePolygonDraft(
                        ref buffer,
                        ref heightData,
                        hasHeightData,
                        terrainDirtyHint,
                        time,
                        in colors,
                        baseBorderRgb,
                        baseFillRgb,
                        borderOutlineWidth,
                        pulse);
                }
                else if (drawFreehandDraft)
                {
                    DrawLiveFreehandDraft(
                        ref buffer,
                        ref heightData,
                        hasHeightData,
                        terrainDirtyHint,
                        time,
                        in colors,
                        baseBorderRgb,
                        baseFillRgb,
                        borderOutlineWidth);
                }
                else if (!drawBox && m_HasCursorWorld)
                {
                    float y = SampleHeight(ref heightData, hasHeightData, m_CursorWorld) + 0.1f;
                    float3 p = new float3(m_CursorWorld.x, y, m_CursorWorld.z);
                    UnityEngine.Color cursorFill = new UnityEngine.Color(baseBorderRgb.r, baseBorderRgb.g, baseBorderRgb.b, 0.85f);
                    float cursorOutlineWidth = colors.OutlineAlpha <= 0.001f ? 0f : 0.15f;
                    buffer.DrawCircle(
                        colors.DarkOutline,
                        cursorFill,
                        cursorOutlineWidth,
                        OverlayRenderSystem.StyleFlags.Projected,
                        new float2(0f, 1f),
                        p,
                        3.2f);
                }

                DrawBuildingOverlays(ref buffer, CurrentResult, pulse, in colors);
                DrawAssetOverlays(ref buffer, in colors);
                DrawRoadFacilityMarkers(ref buffer, CurrentResult, in colors);
            }
            finally
            {
                m_OverlayRenderSystem.AddBufferWriter(default(JobHandle));
                if (hasHeightData && m_TerrainSystem != null)
                {
                    m_TerrainSystem.AddCPUHeightReader(default(JobHandle));
                }
            }
        }

        private static void DrawProjectedEdge(
            ref OverlayRenderSystem.Buffer buffer,
            UnityEngine.Color outlineColor,
            UnityEngine.Color fillColor,
            float width,
            float3 a,
            float3 b,
            float outlineWidth = 0.2f)
        {
            buffer.DrawLine(
                outlineColor,
                fillColor,
                outlineWidth,
                OverlayRenderSystem.StyleFlags.Projected,
                new Line3.Segment(a, b),
                width,
                new float2(0f, 0f));
        }

        private static void DrawCornerMarker(
            ref OverlayRenderSystem.Buffer buffer,
            UnityEngine.Color outlineColor,
            UnityEngine.Color fillColor,
            float3 corner,
            float diameter,
            float outlineWidth = 0.2f)
        {
            buffer.DrawCircle(
                outlineColor,
                fillColor,
                outlineWidth,
                OverlayRenderSystem.StyleFlags.Projected,
                new float2(0f, 1f),
                corner,
                diameter);
        }

        private static float3 SampleCorner(ref TerrainHeightData heightData, bool hasHeightData, CoordXZ pt, float fallbackY)
        {
            float3 probe = new float3(pt.X, fallbackY, pt.Z);
            float y = SampleHeight(ref heightData, hasHeightData, probe);
            return new float3(pt.X, y, pt.Z);
        }

        private static float SampleHeight(ref TerrainHeightData heightData, bool hasHeightData, float3 pos)
        {
            if (hasHeightData)
            {
                float sampled = TerrainUtils.SampleHeight(ref heightData, pos);
                if (!float.IsNaN(sampled) && !float.IsInfinity(sampled))
                {
                    return sampled + 0.15f;
                }
            }
            return pos.y + 0.15f;
        }

        private void SyncToolActionStates(
            bool toolRunning,
            bool hasPending,
            bool canForceConfirm = false,
            bool canExcludeImportant = false,
            bool canClear = false,
            bool canClosePolygon = false)
        {
            applyAction.shouldBeEnabled = toolRunning;
            cancelAction.shouldBeEnabled = toolRunning;
            secondaryApplyAction.shouldBeEnabled = false;

            SetActionEnabled(m_ConfirmAction, toolRunning && (hasPending || canClosePolygon));
            SetActionEnabled(m_ForceConfirmImportantAction, toolRunning && canForceConfirm);
            SetActionEnabled(m_ExcludeImportantAction, toolRunning && canExcludeImportant);
            SetActionEnabled(m_ClearSelectionAction, toolRunning && canClear);
        }

        private static void SetActionEnabled(ProxyAction action, bool enabled)
        {
            if (action != null)
            {
                action.shouldBeEnabled = enabled;
            }
        }

        private void PlayUiSound(bool selectEntity)
        {
            if (!CanPlaySounds) return;

            var sounds = m_SoundQuery.GetSingleton<ToolUXSoundSettingsData>();
            Entity clip = selectEntity ? sounds.m_SelectEntitySound : sounds.m_NetCancelSound;
            if (clip != Entity.Null) m_AudioManager.PlayUISound(clip);
        }

        private void PlayBulldozeSound(bool heavyBulldoze)
        {
            if (!CanPlaySounds) return;

            var sounds = m_SoundQuery.GetSingleton<ToolUXSoundSettingsData>();
            Entity clip = heavyBulldoze ? sounds.m_BulldozeSound : sounds.m_PropPlantBulldozeSound;
            if (clip == Entity.Null) clip = sounds.m_DeletetEntitySound;
            if (clip != Entity.Null) m_AudioManager.PlayUISound(clip);
        }

        private bool CanPlaySounds =>
            Mod.Settings?.PlaySounds == true && m_AudioManager != null && !m_SoundQuery.IsEmptyIgnoreFilter;

        private static bool IsFinite(float3 v)
        {
            return math.all(math.isfinite(v));
        }

        private float ComputeShapeFillAlpha(bool isDraftPreview, in ResolvedOverlayColors colors)
        {
            bool hasZoneBuildings = !m_Underground && CurrentResult != null && CurrentResult.Breakdown.Buildings > 0;
            float fillScale = m_Underground
                ? (isDraftPreview ? 1.11f : 1.55f)
                : (hasZoneBuildings
                    ? (isDraftPreview ? 0.28f : 0.45f)
                    : (isDraftPreview ? 0.67f : 1.0f));
            return math.clamp(colors.MarqueeFillAlpha * fillScale, 0f, 0.85f);
        }

        private void DrawLivePolygonDraft(
            ref OverlayRenderSystem.Buffer buffer,
            ref TerrainHeightData heightData,
            bool hasHeightData,
            bool terrainDirtyHint,
            double timeSeconds,
            in ResolvedOverlayColors colors,
            UnityEngine.Color baseBorderRgb,
            UnityEngine.Color baseFillRgb,
            float borderOutlineWidth,
            float pulse)
        {
            var draft = m_Session.PolygonDraft;
            int count = draft.Count;
            if (count <= 0)
            {
                return;
            }

            float fallbackY = m_HasCursorWorld ? m_CursorWorld.y : draft.StartVertexY;
            float span = ComputeVertexListSpan(draft.Vertices, m_HasCursorWorld, new CoordXZ(m_CursorWorld.x, m_CursorWorld.z));
            float lineWidth = math.clamp(span * 0.015f, 0.45f, 2.2f);
            float cornerDiameter = math.clamp(lineWidth * 2.1f, 0.9f, 4.0f);

            UnityEngine.Color borderColor = new UnityEngine.Color(baseBorderRgb.r, baseBorderRgb.g, baseBorderRgb.b, 0.93f);
            UnityEngine.Color glowColor = new UnityEngine.Color(borderColor.r, borderColor.g, borderColor.b, 0.26f);
            UnityEngine.Color outlineColor = colors.DarkOutline;

            m_DraftOverlayCache.EnsureForDraft(
                MarqueeSelectionMode.Polygon,
                draft.Revision,
                draft.Vertices,
                ref heightData,
                hasHeightData,
                terrainDirtyHint,
                timeSeconds,
                fallbackY);

            if (count >= 3)
            {
                float fillAlpha = ComputeShapeFillAlpha(isDraftPreview: true, in colors);
                UnityEngine.Color fillTint = new UnityEngine.Color(baseFillRgb.r, baseFillRgb.g, baseFillRgb.b, fillAlpha);
                UnityEngine.Color gridRgb = UnityEngine.Color.Lerp(baseBorderRgb, UnityEngine.Color.white, 0.28f);
                UnityEngine.Color gridColor = new UnityEngine.Color(gridRgb.r, gridRgb.g, gridRgb.b, 0.16f);
                float gridLineWidth = math.clamp(lineWidth * 0.26f, 0.12f, 0.32f);
                DrawCachedShapeFillAndGrid(ref buffer, m_DraftOverlayCache, fillTint, gridColor, gridLineWidth);
            }

            DrawCachedOutlineSegments(ref buffer, m_DraftOverlayCache, outlineColor, glowColor, borderColor, lineWidth, borderOutlineWidth);

            CoordXZ lastXz = draft.Vertices[count - 1];
            CoordXZ startXz = draft.Vertices[0];
            CoordXZ cursorXz = new CoordXZ(m_CursorWorld.x, m_CursorWorld.z);
            UnityEngine.Color previewColor = UnityEngine.Color.Lerp(borderColor, UnityEngine.Color.white, 0.32f);
            previewColor.a = 0.85f;
            float previewWidth = math.max(0.32f, lineWidth * 0.72f);

            if (count >= 3 && m_PolygonSnapActive)
            {
                DrawProjectedDashedLine(
                    ref buffer, ref heightData, hasHeightData, outlineColor, previewColor,
                    previewWidth, borderOutlineWidth, lastXz, startXz, fallbackY);
            }
            else if (m_HasCursorWorld)
            {
                DrawProjectedDashedLine(
                    ref buffer, ref heightData, hasHeightData, outlineColor, previewColor,
                    previewWidth, borderOutlineWidth, lastXz, cursorXz, fallbackY);
                if (count >= 2)
                {
                    UnityEngine.Color closePreviewColor = new UnityEngine.Color(previewColor.r, previewColor.g, previewColor.b, 0.58f);
                    DrawProjectedDashedLine(
                        ref buffer, ref heightData, hasHeightData, outlineColor, closePreviewColor,
                        previewWidth * 0.85f, borderOutlineWidth, cursorXz, startXz, fallbackY);
                }
            }

            var verts3D = m_DraftOverlayCache.VertexPositions3D;
            for (int i = 1; i < verts3D.Count; i++)
            {
                DrawCornerMarker(ref buffer, outlineColor, borderColor, verts3D[i], cornerDiameter * 0.85f, borderOutlineWidth);
            }

            if (verts3D.Count > 0)
            {
                UnityEngine.Color startFill = count >= 3
                    ? UnityEngine.Color.Lerp(borderColor, UnityEngine.Color.white, m_PolygonSnapActive ? 0.55f : 0.25f)
                    : borderColor;
                float startDiameter = cornerDiameter * (m_PolygonSnapActive ? 1.45f : 1.25f);
                DrawCornerMarker(ref buffer, outlineColor, startFill, verts3D[0], startDiameter, borderOutlineWidth);

                if (m_PolygonSnapActive && count >= 3)
                {
                    UnityEngine.Color snapRingOutline = UnityEngine.Color.Lerp(borderColor, UnityEngine.Color.white, 0.60f);
                    UnityEngine.Color snapRingFill = new UnityEngine.Color(snapRingOutline.r, snapRingOutline.g, snapRingOutline.b, 0.20f);
                    float snapRingDiameter = cornerDiameter * (2.35f + 0.30f * pulse);
                    float snapRingWidth = math.max(0.30f, lineWidth * 0.45f);
                    buffer.DrawCircle(
                        snapRingOutline,
                        snapRingFill,
                        snapRingWidth,
                        OverlayRenderSystem.StyleFlags.Projected,
                        new float2(0f, 1f),
                        verts3D[0],
                        snapRingDiameter);
                }
            }
        }

        private void DrawLiveFreehandDraft(
            ref OverlayRenderSystem.Buffer buffer,
            ref TerrainHeightData heightData,
            bool hasHeightData,
            bool terrainDirtyHint,
            double timeSeconds,
            in ResolvedOverlayColors colors,
            UnityEngine.Color baseBorderRgb,
            UnityEngine.Color baseFillRgb,
            float borderOutlineWidth)
        {
            var draft = m_Session.FreehandDraft;
            int count = draft.Count;
            if (count <= 0)
            {
                return;
            }

            float fallbackY = m_HasCursorWorld ? m_CursorWorld.y : draft.StartPointY;
            CoordXZ cursorXz = new CoordXZ(m_CursorWorld.x, m_CursorWorld.z);
            float span = ComputeVertexListSpan(draft.Points, m_HasCursorWorld, cursorXz);
            float lineWidth = math.clamp(span * 0.015f, 0.45f, 2.2f);
            float cornerDiameter = math.clamp(lineWidth * 2.1f, 0.9f, 4.0f);

            UnityEngine.Color borderColor = new UnityEngine.Color(baseBorderRgb.r, baseBorderRgb.g, baseBorderRgb.b, 0.93f);
            UnityEngine.Color glowColor = new UnityEngine.Color(borderColor.r, borderColor.g, borderColor.b, 0.26f);
            UnityEngine.Color outlineColor = colors.DarkOutline;

            m_DraftOverlayCache.EnsureForDraft(
                MarqueeSelectionMode.Freehand,
                draft.Revision,
                draft.Points,
                ref heightData,
                hasHeightData,
                terrainDirtyHint,
                timeSeconds,
                fallbackY);

            if (count >= 3)
            {
                float fillAlpha = ComputeShapeFillAlpha(isDraftPreview: true, in colors);
                UnityEngine.Color fillTint = new UnityEngine.Color(baseFillRgb.r, baseFillRgb.g, baseFillRgb.b, fillAlpha);
                UnityEngine.Color gridRgb = UnityEngine.Color.Lerp(baseBorderRgb, UnityEngine.Color.white, 0.28f);
                UnityEngine.Color gridColor = new UnityEngine.Color(gridRgb.r, gridRgb.g, gridRgb.b, 0.16f);
                float gridLineWidth = math.clamp(lineWidth * 0.26f, 0.12f, 0.32f);
                DrawCachedShapeFillAndGrid(ref buffer, m_DraftOverlayCache, fillTint, gridColor, gridLineWidth);
            }

            DrawCachedOutlineSegments(ref buffer, m_DraftOverlayCache, outlineColor, glowColor, borderColor, lineWidth, borderOutlineWidth);

            CoordXZ startXz = draft.Points[0];
            CoordXZ tailStartXz = draft.Points[count - 1];
            CoordXZ closeStartXz = m_HasCursorWorld ? cursorXz : tailStartXz;

            if (m_HasCursorWorld)
            {
                DrawTerrainSubdividedSegment(
                    ref buffer, ref heightData, hasHeightData,
                    outlineColor, glowColor, borderColor,
                    lineWidth, borderOutlineWidth,
                    tailStartXz, cursorXz, fallbackY);
            }

            UnityEngine.Color previewColor = UnityEngine.Color.Lerp(borderColor, UnityEngine.Color.white, 0.30f);
            previewColor.a = 0.78f;
            float closeDistSq = (closeStartXz - startXz).LengthSquared();
            if (closeDistSq > 0.5f)
            {
                DrawProjectedDashedLine(
                    ref buffer, ref heightData, hasHeightData,
                    outlineColor, previewColor,
                    math.max(0.32f, lineWidth * 0.72f), borderOutlineWidth,
                    closeStartXz, startXz, fallbackY);
            }

            if (m_DraftOverlayCache.VertexPositions3D.Count > 0)
            {
                DrawCornerMarker(
                    ref buffer,
                    outlineColor,
                    UnityEngine.Color.Lerp(borderColor, UnityEngine.Color.white, 0.30f),
                    m_DraftOverlayCache.VertexPositions3D[0],
                    cornerDiameter * 1.1f,
                    borderOutlineWidth);
            }
        }

        private static float ComputeVertexListSpan(IReadOnlyList<CoordXZ> pts, bool includeExtra, CoordXZ extra)
        {
            if (pts == null || pts.Count == 0)
            {
                return 0f;
            }

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                CoordXZ p = pts[i];
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }
            if (includeExtra)
            {
                if (extra.X < minX) minX = extra.X;
                if (extra.X > maxX) maxX = extra.X;
                if (extra.Z < minZ) minZ = extra.Z;
                if (extra.Z > maxZ) maxZ = extra.Z;
            }
            return math.max(maxX - minX, maxZ - minZ);
        }

        private static void DrawCachedShapeFillAndGrid(
            ref OverlayRenderSystem.Buffer buffer,
            CachedShapeOverlay cache,
            UnityEngine.Color fillTint,
            UnityEngine.Color gridColor,
            float gridLineWidth)
        {
            if (fillTint.a > 0.001f && cache.Geometry.FillBandWidth > 0.05f)
            {
                var fills = cache.FillSegments3D;
                float bandWidth = cache.Geometry.FillBandWidth;
                for (int i = 0; i < fills.Count; i++)
                {
                    buffer.DrawLine(
                        fillTint,
                        fillTint,
                        0f,
                        OverlayRenderSystem.StyleFlags.Projected,
                        fills[i],
                        bandWidth,
                        new float2(0f, 0f));
                }
            }

            var grid = cache.GridSegments3D;
            for (int i = 0; i < grid.Count; i++)
            {
                buffer.DrawLine(
                    gridColor,
                    gridColor,
                    0f,
                    OverlayRenderSystem.StyleFlags.Projected,
                    grid[i],
                    gridLineWidth,
                    new float2(0f, 0f));
            }
        }

        private static void DrawCachedOutlineSegments(
            ref OverlayRenderSystem.Buffer buffer,
            CachedShapeOverlay cache,
            UnityEngine.Color outlineColor,
            UnityEngine.Color glowColor,
            UnityEngine.Color borderColor,
            float lineWidth,
            float borderOutlineWidth)
        {
            var edges = cache.OutlineSegments3D;
            float glowWidth = lineWidth * 2.1f;
            for (int i = 0; i < edges.Count; i++)
            {
                Line3.Segment seg = edges[i];
                DrawProjectedEdge(ref buffer, glowColor, glowColor, glowWidth, seg.a, seg.b, 0f);
                DrawProjectedEdge(ref buffer, outlineColor, borderColor, lineWidth, seg.a, seg.b, borderOutlineWidth);
            }
        }

        private static void DrawTerrainSubdividedSegment(
            ref OverlayRenderSystem.Buffer buffer,
            ref TerrainHeightData heightData,
            bool hasHeightData,
            UnityEngine.Color outlineColor,
            UnityEngine.Color glowColor,
            UnityEngine.Color borderColor,
            float lineWidth,
            float borderOutlineWidth,
            CoordXZ a,
            CoordXZ b,
            float fallbackY)
        {
            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            if (lenSq <= 0.01f)
            {
                return;
            }

            float len = math.sqrt(lenSq);
            int steps = math.clamp((int)math.ceil(len / ShapeOverlayBuilder.DefaultMaxTerrainSampleStepMeters), 1, 16);
            float3 prev = SampleCorner(ref heightData, hasHeightData, a, fallbackY);
            float glowWidth = lineWidth * 2.1f;
            for (int s = 1; s <= steps; s++)
            {
                float t = (float)s / steps;
                CoordXZ currXz = (s == steps) ? b : new CoordXZ(a.X + dx * t, a.Z + dz * t);
                float3 curr = SampleCorner(ref heightData, hasHeightData, currXz, fallbackY);
                DrawProjectedEdge(ref buffer, glowColor, glowColor, glowWidth, prev, curr, 0f);
                DrawProjectedEdge(ref buffer, outlineColor, borderColor, lineWidth, prev, curr, borderOutlineWidth);
                prev = curr;
            }
        }

        private static void DrawProjectedDashedLine(
            ref OverlayRenderSystem.Buffer buffer,
            ref TerrainHeightData heightData,
            bool hasHeightData,
            UnityEngine.Color outlineColor,
            UnityEngine.Color fillColor,
            float width,
            float borderOutlineWidth,
            CoordXZ a,
            CoordXZ b,
            float fallbackY,
            float dashMeters = 2.4f,
            float gapMeters = 1.6f)
        {
            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            if (lenSq <= 0.06f)
            {
                return;
            }

            float len = math.sqrt(lenSq);
            float cycle = math.max(0.5f, dashMeters + gapMeters);
            int maxDashes = 48;
            if (len / cycle > maxDashes)
            {
                cycle = len / maxDashes;
                dashMeters = cycle * 0.6f;
            }

            for (float d = 0f; d < len; d += cycle)
            {
                float dEnd = math.min(len, d + dashMeters);
                if (dEnd - d <= 0.1f)
                {
                    break;
                }

                float t0 = d / len;
                float t1 = dEnd / len;
                CoordXZ p0 = new CoordXZ(a.X + dx * t0, a.Z + dz * t0);
                CoordXZ p1 = new CoordXZ(a.X + dx * t1, a.Z + dz * t1);
                float3 w0 = SampleCorner(ref heightData, hasHeightData, p0, fallbackY);
                float3 w1 = SampleCorner(ref heightData, hasHeightData, p1, fallbackY);
                DrawProjectedEdge(ref buffer, outlineColor, fillColor, width, w0, w1, borderOutlineWidth);
            }
        }

        private bool ComputeTerrainDirtyHint()
        {
            if (m_TerrainSystem == null)
            {
                return false;
            }

            bool dirty = m_TerrainSystem.NewMap || m_TerrainSystem.heightMapRenderRequired;
            float2 curScaleOffset = m_TerrainSystem.heightScaleOffset;
            if (!m_HasLastTerrainHeightScaleOffset || !math.all(curScaleOffset == m_LastTerrainHeightScaleOffset))
            {
                m_HasLastTerrainHeightScaleOffset = true;
                m_LastTerrainHeightScaleOffset = curScaleOffset;
                dirty = true;
            }

            if (!dirty)
            {
                bool[] slices = m_TerrainSystem.heightMapSliceUpdated;
                if (slices != null)
                {
                    for (int i = 0; i < slices.Length; i++)
                    {
                        if (slices[i])
                        {
                            dirty = true;
                            break;
                        }
                    }
                }
            }

            return dirty;
        }

        private sealed class CachedShapeOverlay
        {
            public readonly ShapeFillOverlayGeometry Geometry = new ShapeFillOverlayGeometry();
            public readonly List<Line3.Segment> FillSegments3D = new List<Line3.Segment>(96);
            public readonly List<Line3.Segment> GridSegments3D = new List<Line3.Segment>(64);
            public readonly List<Line3.Segment> OutlineSegments3D = new List<Line3.Segment>(128);
            public readonly List<float3> VertexPositions3D = new List<float3>(64);
            private readonly List<CoordXZ> m_CachedVerts = new List<CoordXZ>(64);

            private uint m_LastClosedVersion = uint.MaxValue;
            private MarqueeSelectionMode m_LastMode;
            private int m_LastRevision = -1;
            private bool m_LastHasHeightData;
            private double m_LastHeightSampleTime = double.NegativeInfinity;

            public void EnsureForShape(
                in MarqueeShapeXZ shape,
                uint closedVersion,
                ref TerrainHeightData heightData,
                bool hasHeightData,
                bool terrainDirtyHint,
                double nowSeconds,
                float fallbackY)
            {
                bool geomChanged = m_LastClosedVersion != closedVersion;
                OverlayCacheRefreshDecision refresh = ShapeOverlayCachePolicy.EvaluateRefresh(
                    geomChanged,
                    m_LastHasHeightData != hasHeightData,
                    terrainDirtyHint,
                    nowSeconds,
                    m_LastHeightSampleTime);
                if (!refresh.Rebuild2DGeometry && !refresh.Resample3DHeights)
                {
                    return;
                }

                if (refresh.Rebuild2DGeometry)
                {
                    m_LastClosedVersion = closedVersion;
                    m_CachedVerts.Clear();
                    int n = shape.VertexCount;
                    for (int i = 0; i < n; i++)
                    {
                        m_CachedVerts.Add(shape.GetVertex(i));
                    }

                    ShapeOverlayBuilder.BuildFillAndGridSegments(m_CachedVerts, Geometry, closedOutline: true);
                }

                m_LastHasHeightData = hasHeightData;
                m_LastHeightSampleTime = nowSeconds;
                Rebuild3DSegments(m_CachedVerts, ref heightData, hasHeightData, fallbackY);
            }

            public void EnsureForDraft(
                MarqueeSelectionMode mode,
                int revision,
                IReadOnlyList<CoordXZ> verts,
                ref TerrainHeightData heightData,
                bool hasHeightData,
                bool terrainDirtyHint,
                double nowSeconds,
                float fallbackY)
            {
                bool geomChanged = m_LastMode != mode || m_LastRevision != revision;
                OverlayCacheRefreshDecision refresh = ShapeOverlayCachePolicy.EvaluateRefresh(
                    geomChanged,
                    m_LastHasHeightData != hasHeightData,
                    terrainDirtyHint,
                    nowSeconds,
                    m_LastHeightSampleTime);
                if (!refresh.Rebuild2DGeometry && !refresh.Resample3DHeights)
                {
                    return;
                }

                if (refresh.Rebuild2DGeometry)
                {
                    m_LastMode = mode;
                    m_LastRevision = revision;
                    m_CachedVerts.Clear();
                    if (verts != null)
                    {
                        for (int i = 0; i < verts.Count; i++)
                        {
                            m_CachedVerts.Add(verts[i]);
                        }
                    }

                    ShapeOverlayBuilder.BuildFillAndGridSegments(m_CachedVerts, Geometry, closedOutline: false);
                }

                m_LastHasHeightData = hasHeightData;
                m_LastHeightSampleTime = nowSeconds;
                Rebuild3DSegments(m_CachedVerts, ref heightData, hasHeightData, fallbackY);
            }

            private void Rebuild3DSegments(
                IReadOnlyList<CoordXZ> verts,
                ref TerrainHeightData heightData,
                bool hasHeightData,
                float fallbackY)
            {
                FillSegments3D.Clear();
                for (int i = 0; i < Geometry.FillSegments.Count; i++)
                {
                    SegmentXZ s = Geometry.FillSegments[i];
                    FillSegments3D.Add(new Line3.Segment(
                        SampleCorner(ref heightData, hasHeightData, s.A, fallbackY),
                        SampleCorner(ref heightData, hasHeightData, s.B, fallbackY)));
                }

                GridSegments3D.Clear();
                for (int i = 0; i < Geometry.GridSegments.Count; i++)
                {
                    SegmentXZ s = Geometry.GridSegments[i];
                    GridSegments3D.Add(new Line3.Segment(
                        SampleCorner(ref heightData, hasHeightData, s.A, fallbackY),
                        SampleCorner(ref heightData, hasHeightData, s.B, fallbackY)));
                }

                OutlineSegments3D.Clear();
                for (int i = 0; i < Geometry.OutlineSegments.Count; i++)
                {
                    SegmentXZ s = Geometry.OutlineSegments[i];
                    OutlineSegments3D.Add(new Line3.Segment(
                        SampleCorner(ref heightData, hasHeightData, s.A, fallbackY),
                        SampleCorner(ref heightData, hasHeightData, s.B, fallbackY)));
                }

                VertexPositions3D.Clear();
                if (verts != null)
                {
                    for (int i = 0; i < verts.Count; i++)
                    {
                        VertexPositions3D.Add(SampleCorner(ref heightData, hasHeightData, verts[i], fallbackY));
                    }
                }
            }
        }
    }
}
