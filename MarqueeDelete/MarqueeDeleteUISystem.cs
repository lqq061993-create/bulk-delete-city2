using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Game;
using Game.Input;
using Game.SceneFlow;
using Game.UI;

namespace MarqueeDelete
{
    public sealed partial class MarqueeDeleteUISystem : UISystemBase
    {
        private const string Group = "marqueedelete";

        private struct OptionHotkeyEntry
        {
            public ProxyAction Action;
            public string Key;
            public Func<Settings, bool> Getter;
        }

        private MarqueeDeleteToolSystem m_Tool;
        private ProxyAction m_ToggleAction;
        private ProxyAction m_SwitchModeAction;
        private ProxyAction m_CycleSelectionModeAction;
        private ProxyAction m_ToggleUndergroundAction;
        private ProxyAction m_TogglePauseUndoAction;
        private ProxyAction m_StopUndoAction;
        private ProxyAction m_UndoAction;
        private ProxyAction m_SelectAllFiltersAction;
        private ProxyAction m_ResetDefaultFiltersAction;
        private ProxyAction m_ClearAllFiltersAction;
        private ProxyAction m_PickSimilarAction;
        private readonly List<OptionHotkeyEntry> m_OptionHotkeys = new List<OptionHotkeyEntry>(14);
        private Settings m_SubscribedSettings;
        private bool m_Dirty = true;
        private bool m_LastCanUse;
        private bool m_LastActive;
        private bool m_LastBulldozeActive;
        private bool m_LastUnderground;
        private bool m_LastColorModalOpen;
        private int m_LastSelectionMode;
        private int m_LastPolygonVertexCount;
        private bool m_LastPolygonSnapActive;
        private bool m_LastPolygonVertexLimitReached;
        private bool m_LastFreehandDrawing;
        private bool m_LastShapeInvalid;
        private bool m_LastPickSimilarArmed;
        private bool m_ColorPaletteDirty;
        private int m_LastPushedColorRevision = int.MinValue;
        private bool m_HasLastUndoUi;
        private SelectiveUndoUiSnapshot m_LastUndoUi;

        private float m_LastWidthLabelX = -999f;
        private float m_LastWidthLabelY = -999f;
        private float m_LastDepthLabelX = -999f;
        private float m_LastDepthLabelY = -999f;
        private int m_LastBoxWidthMeters = -1;
        private int m_LastBoxDepthMeters = -1;
        private bool m_LastDragging;
        private bool m_LastPending;
        private bool m_LastDrawingOrDrafting;
        private int m_LastCount = -1;
        private int m_LastCascade = -1;
        private string m_LastNotice = string.Empty;
        private bool m_LastImportantModalOpen;
        private int m_LastSelectionHistoryCount = -1;
        private int m_LastInterruptedDeletionCount;
        private int m_LastInterruptedUnitCount;
        private int m_LastInterruptedLargestUnit;
        private bool m_LastReviewingInterruptedDeletion;
        private int m_LastResultRevision = -1;
        private int m_LastShapeRevision = -1;

        private ValueBinding<bool> m_CanUseBinding;
        private ValueBinding<bool> m_ActiveBinding;
        private ValueBinding<bool> m_DraggingBinding;
        private ValueBinding<bool> m_PendingBinding;
        private ValueBinding<int> m_CountBinding;
        private ValueBinding<int> m_CascadeCountBinding;
        private ValueBinding<int> m_ThresholdBinding;
        private ValueBinding<bool> m_OverThresholdBinding;
        private ValueBinding<string> m_SelectionNoticeBinding;

        private ValueBinding<int> m_CountBuildingsBinding;
        private ValueBinding<int> m_CountTreesBinding;
        private ValueBinding<int> m_CountPropsBinding;
        private ValueBinding<int> m_CountDecalsBinding;
        private ValueBinding<int> m_CountMovingBinding;
        private ValueBinding<int> m_CountNetworksBinding;
        private ValueBinding<int> m_CountAreasBinding;

        private ValueBinding<bool> m_FilterBuildingsBinding;
        private ValueBinding<bool> m_FilterTreesBinding;
        private ValueBinding<bool> m_FilterPropsBinding;
        private ValueBinding<bool> m_FilterDecalsBinding;
        private ValueBinding<bool> m_FilterMovingBinding;
        private ValueBinding<bool> m_FilterNetworksBinding;
        private ValueBinding<bool> m_FilterNetRoadsBinding;
        private ValueBinding<bool> m_FilterNetTracksBinding;
        private ValueBinding<bool> m_FilterNetPipelinesBinding;
        private ValueBinding<bool> m_FilterNetPowerLinesBinding;
        private ValueBinding<bool> m_FilterNetOtherBinding;
        private ValueBinding<bool> m_FilterAreasBinding;
        private ValueBinding<string> m_ActiveFilterPresetBinding;
        private ValueBinding<bool> m_CustomPreset1SavedBinding;
        private ValueBinding<bool> m_CustomPreset2SavedBinding;
        private ValueBinding<bool> m_CustomPreset3SavedBinding;
        private ValueBinding<bool> m_PickSimilarArmedBinding;

        private ValueBinding<bool> m_OptConfirmBinding;
        private ValueBinding<bool> m_OptCameraAlignBinding;
        private ValueBinding<bool> m_OptProtectOutsideBinding;
        private ValueBinding<bool> m_UndergroundBinding;
        private ValueBinding<string> m_CompatTierBinding;
        private ValueBinding<bool> m_CompatCanSubmitBinding;
        private ValueBinding<bool> m_CompatCanUndoBinding;
        private ValueBinding<string> m_CompatMessageBinding;
        private ValueBinding<bool> m_ImpactCalculatingBinding;

        private ValueBinding<int> m_BoxWidthCellsBinding;
        private ValueBinding<int> m_BoxDepthCellsBinding;
        private ValueBinding<int> m_BoxWidthMetersBinding;
        private ValueBinding<int> m_BoxDepthMetersBinding;
        private ValueBinding<int> m_ShapeModeBinding;
        private ValueBinding<int> m_ShapeAreaSqMetersBinding;
        private ValueBinding<int> m_ShapePerimeterMetersBinding;
        private ValueBinding<int> m_ShapeVertexCountBinding;
        private ValueBinding<int> m_ShapeEquivalentCellsBinding;
        private ValueBinding<int> m_ShapeOriginalVertexCountBinding;
        private ValueBinding<bool> m_ShapeSimplifiedBinding;
        private ValueBinding<int> m_SelectionModeBinding;
        private ValueBinding<int> m_PolygonVertexCountBinding;
        private ValueBinding<bool> m_PolygonSnapActiveBinding;
        private ValueBinding<bool> m_PolygonVertexLimitReachedBinding;
        private ValueBinding<bool> m_FreehandDrawingBinding;
        private ValueBinding<bool> m_ShapeInvalidBinding;
        private ValueBinding<int> m_CountResidentialBinding;
        private ValueBinding<int> m_CountCommercialBinding;
        private ValueBinding<int> m_CountIndustrialBinding;
        private ValueBinding<int> m_CountOfficeBinding;
        private ValueBinding<int> m_CountImportantBinding;
        private ValueBinding<int> m_CountServiceBinding;
        private ValueBinding<int> m_CountUniqueBinding;
        private ValueBinding<int> m_CountSignatureBinding;
        private ValueBinding<bool> m_ImportantModalOpenBinding;
        private ValueBinding<string> m_ImportantListRawBinding;
        private ValueBinding<float> m_WidthLabelXBinding;
        private ValueBinding<float> m_WidthLabelYBinding;
        private ValueBinding<float> m_DepthLabelXBinding;
        private ValueBinding<float> m_DepthLabelYBinding;
        private ValueBinding<bool> m_FilterResidentialBinding;
        private ValueBinding<bool> m_FilterCommercialBinding;
        private ValueBinding<bool> m_FilterIndustrialBinding;
        private ValueBinding<bool> m_FilterOfficeBinding;
        private ValueBinding<bool> m_UndoBusyBinding;
        private ValueBinding<string> m_UndoNoticeBinding;
        private ValueBinding<string> m_UndoFinishedNoticeBinding;
        private ValueBinding<bool> m_CanUndoBinding;
        private ValueBinding<bool> m_UndoInProgressBinding;
        private ValueBinding<int> m_UndoNextCountBinding;
        private ValueBinding<int> m_UndoRestoredCountBinding;
        private ValueBinding<int> m_UndoTotalCountBinding;
        private ValueBinding<int> m_UndoNotCapturedCountBinding;
        private ValueBinding<bool> m_UndoIncludeBuildingsBinding;
        private ValueBinding<bool> m_UndoIncludeNetworksBinding;
        private ValueBinding<bool> m_UndoIncludeAreasBinding;
        private ValueBinding<bool> m_UndoIncludeTreesBinding;
        private ValueBinding<bool> m_UndoIncludePropsBinding;
        private ValueBinding<bool> m_UndoIncludeDecalsBinding;
        private ValueBinding<int> m_UndoCountBuildingsBinding;
        private ValueBinding<int> m_UndoCountNetworksBinding;
        private ValueBinding<int> m_UndoCountAreasBinding;
        private ValueBinding<int> m_UndoCountTreesBinding;
        private ValueBinding<int> m_UndoCountPropsBinding;
        private ValueBinding<int> m_UndoCountDecalsBinding;
        private ValueBinding<int> m_UndoHistoryDepthBinding;
        private ValueBinding<int> m_UndoSkippedCountBinding;
        private ValueBinding<bool> m_UndoZeroMatchBinding;
        private ValueBinding<int> m_UndoFinishedRestoredCountBinding;
        private ValueBinding<int> m_UndoFinishedSkippedCountBinding;
        private ValueBinding<bool> m_ColorModalOpenBinding;
        private ValueBinding<string> m_ColorPaletteJsonBinding;
        private ValueBinding<int> m_UndoFinishedTotalCountBinding;
        private ValueBinding<int> m_UndoFinishedNotCapturedCountBinding;
        private ValueBinding<int> m_UndoCaptureFailedCountBinding;
        private ValueBinding<string> m_ImpactReportRawBinding;
        private bool m_ImpactReportOpen;
        private ValueBinding<string> m_RestoreNoticeBinding;
        private float m_RestoreNoticeUntil;
        private ValueBinding<int> m_SelectionHistoryCountBinding;
        private ValueBinding<int> m_InterruptedDeletionCountBinding;
        private ValueBinding<int> m_InterruptedUnitCountBinding;
        private ValueBinding<int> m_InterruptedLargestUnitBinding;
        private ValueBinding<bool> m_ReviewingInterruptedDeletionBinding;
        private ValueBinding<int> m_PolygonVertexLimitBinding;
        private ProxyAction m_RestoreSelectionHistoryAction;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Tool = World.GetOrCreateSystemManaged<MarqueeDeleteToolSystem>();

            AddBinding(m_ImpactReportRawBinding = new ValueBinding<string>(Group, "impactReportRaw", string.Empty));
            AddBinding(new TriggerBinding<bool>(Group, "setImpactReportOpen", OnSetImpactReportOpen));
            AddBinding(m_RestoreNoticeBinding = new ValueBinding<string>(Group, "restoreNotice", string.Empty));
            AddBinding(m_SelectionHistoryCountBinding = new ValueBinding<int>(Group, "selectionHistoryCount", 0));
            AddBinding(m_InterruptedDeletionCountBinding = new ValueBinding<int>(Group, "interruptedDeletionCount", 0));
            AddBinding(m_InterruptedUnitCountBinding = new ValueBinding<int>(Group, "interruptedUnitCount", 0));
            AddBinding(m_InterruptedLargestUnitBinding = new ValueBinding<int>(Group, "interruptedLargestUnit", 0));
            AddBinding(m_ReviewingInterruptedDeletionBinding = new ValueBinding<bool>(Group, "reviewingInterruptedDeletion", false));
            AddBinding(new TriggerBinding(Group, "reviewInterruptedDeletion", OnReviewInterruptedDeletion));
            AddBinding(new TriggerBinding(Group, "discardInterruptedDeletion", OnDiscardInterruptedDeletion));
            AddBinding(m_PolygonVertexLimitBinding = new ValueBinding<int>(Group, "polygonVertexLimit", 64));
            AddBinding(new TriggerBinding(Group, "restoreLastSelection", OnRestoreLastSelection));

            AddBinding(m_CanUseBinding = new ValueBinding<bool>(Group, "canUse", false));
            AddBinding(m_ActiveBinding = new ValueBinding<bool>(Group, "active", false));
            AddBinding(m_DraggingBinding = new ValueBinding<bool>(Group, "dragging", false));
            AddBinding(m_PendingBinding = new ValueBinding<bool>(Group, "pending", false));
            AddBinding(m_CountBinding = new ValueBinding<int>(Group, "count", 0));
            AddBinding(m_CascadeCountBinding = new ValueBinding<int>(Group, "cascadeCount", 0));
            AddBinding(m_ThresholdBinding = new ValueBinding<int>(Group, "threshold", 50));
            AddBinding(m_OverThresholdBinding = new ValueBinding<bool>(Group, "overThreshold", false));
            AddBinding(m_SelectionNoticeBinding = new ValueBinding<string>(Group, "selectionNotice", string.Empty));

            AddBinding(m_CountBuildingsBinding = new ValueBinding<int>(Group, "countBuildings", 0));
            AddBinding(m_CountTreesBinding = new ValueBinding<int>(Group, "countTrees", 0));
            AddBinding(m_CountPropsBinding = new ValueBinding<int>(Group, "countProps", 0));
            AddBinding(m_CountDecalsBinding = new ValueBinding<int>(Group, "countDecals", 0));
            AddBinding(m_CountMovingBinding = new ValueBinding<int>(Group, "countMoving", 0));
            AddBinding(m_CountNetworksBinding = new ValueBinding<int>(Group, "countNetworks", 0));
            AddBinding(m_CountAreasBinding = new ValueBinding<int>(Group, "countAreas", 0));

            AddBinding(m_FilterBuildingsBinding = new ValueBinding<bool>(Group, "filterBuildings", true));
            AddBinding(m_FilterTreesBinding = new ValueBinding<bool>(Group, "filterTrees", true));
            AddBinding(m_FilterPropsBinding = new ValueBinding<bool>(Group, "filterProps", true));
            AddBinding(m_FilterDecalsBinding = new ValueBinding<bool>(Group, "filterDecals", true));
            AddBinding(m_FilterMovingBinding = new ValueBinding<bool>(Group, "filterMoving", false));
            AddBinding(m_FilterNetworksBinding = new ValueBinding<bool>(Group, "filterNetworks", false));
            AddBinding(m_FilterNetRoadsBinding = new ValueBinding<bool>(Group, "filterNetRoads", true));
            AddBinding(m_FilterNetTracksBinding = new ValueBinding<bool>(Group, "filterNetTracks", true));
            AddBinding(m_FilterNetPipelinesBinding = new ValueBinding<bool>(Group, "filterNetPipelines", true));
            AddBinding(m_FilterNetPowerLinesBinding = new ValueBinding<bool>(Group, "filterNetPowerLines", true));
            AddBinding(m_FilterNetOtherBinding = new ValueBinding<bool>(Group, "filterNetOther", true));
            AddBinding(m_FilterAreasBinding = new ValueBinding<bool>(Group, "filterAreas", false));
            AddBinding(m_ActiveFilterPresetBinding = new ValueBinding<string>(Group, "activeFilterPreset", string.Empty));
            AddBinding(m_CustomPreset1SavedBinding = new ValueBinding<bool>(Group, "customPreset1Saved", false));
            AddBinding(m_CustomPreset2SavedBinding = new ValueBinding<bool>(Group, "customPreset2Saved", false));
            AddBinding(m_CustomPreset3SavedBinding = new ValueBinding<bool>(Group, "customPreset3Saved", false));
            AddBinding(m_PickSimilarArmedBinding = new ValueBinding<bool>(Group, "pickSimilarArmed", false));

            AddBinding(m_OptConfirmBinding = new ValueBinding<bool>(Group, "optConfirm", true));
            AddBinding(m_OptCameraAlignBinding = new ValueBinding<bool>(Group, "optCameraAlign", true));
            AddBinding(m_OptProtectOutsideBinding = new ValueBinding<bool>(Group, "optProtectOutside", true));
            AddBinding(m_UndergroundBinding = new ValueBinding<bool>(Group, "underground", false));
            AddBinding(m_CompatTierBinding = new ValueBinding<string>(Group, "compatTier", "verified"));
            AddBinding(m_CompatCanSubmitBinding = new ValueBinding<bool>(Group, "compatCanSubmit", true));
            AddBinding(m_CompatCanUndoBinding = new ValueBinding<bool>(Group, "compatCanUndo", true));
            AddBinding(m_CompatMessageBinding = new ValueBinding<string>(Group, "compatMessage", string.Empty));
            AddBinding(m_ImpactCalculatingBinding = new ValueBinding<bool>(Group, "impactCalculating", false));

            AddBinding(m_BoxWidthCellsBinding = new ValueBinding<int>(Group, "boxWidthCells", 0));
            AddBinding(m_BoxDepthCellsBinding = new ValueBinding<int>(Group, "boxDepthCells", 0));
            AddBinding(m_BoxWidthMetersBinding = new ValueBinding<int>(Group, "boxWidthMeters", 0));
            AddBinding(m_BoxDepthMetersBinding = new ValueBinding<int>(Group, "boxDepthMeters", 0));
            AddBinding(m_ShapeModeBinding = new ValueBinding<int>(Group, "shapeMode", 0));
            AddBinding(m_ShapeAreaSqMetersBinding = new ValueBinding<int>(Group, "shapeAreaSqMeters", 0));
            AddBinding(m_ShapePerimeterMetersBinding = new ValueBinding<int>(Group, "shapePerimeterMeters", 0));
            AddBinding(m_ShapeVertexCountBinding = new ValueBinding<int>(Group, "shapeVertexCount", 0));
            AddBinding(m_ShapeEquivalentCellsBinding = new ValueBinding<int>(Group, "shapeEquivalentCells", 0));
            AddBinding(m_ShapeOriginalVertexCountBinding = new ValueBinding<int>(Group, "shapeOriginalVertexCount", 0));
            AddBinding(m_ShapeSimplifiedBinding = new ValueBinding<bool>(Group, "shapeSimplified", false));
            AddBinding(m_SelectionModeBinding = new ValueBinding<int>(Group, "selectionMode", 0));
            AddBinding(m_PolygonVertexCountBinding = new ValueBinding<int>(Group, "polygonVertexCount", 0));
            AddBinding(m_PolygonSnapActiveBinding = new ValueBinding<bool>(Group, "polygonSnapActive", false));
            AddBinding(m_PolygonVertexLimitReachedBinding = new ValueBinding<bool>(Group, "polygonVertexLimitReached", false));
            AddBinding(m_FreehandDrawingBinding = new ValueBinding<bool>(Group, "freehandDrawing", false));
            AddBinding(m_ShapeInvalidBinding = new ValueBinding<bool>(Group, "shapeInvalid", false));
            AddBinding(m_CountResidentialBinding = new ValueBinding<int>(Group, "countResidential", 0));
            AddBinding(m_CountCommercialBinding = new ValueBinding<int>(Group, "countCommercial", 0));
            AddBinding(m_CountIndustrialBinding = new ValueBinding<int>(Group, "countIndustrial", 0));
            AddBinding(m_CountOfficeBinding = new ValueBinding<int>(Group, "countOffice", 0));
            AddBinding(m_CountImportantBinding = new ValueBinding<int>(Group, "countImportant", 0));
            AddBinding(m_CountServiceBinding = new ValueBinding<int>(Group, "countService", 0));
            AddBinding(m_CountUniqueBinding = new ValueBinding<int>(Group, "countUnique", 0));
            AddBinding(m_CountSignatureBinding = new ValueBinding<int>(Group, "countSignature", 0));
            AddBinding(m_ImportantModalOpenBinding = new ValueBinding<bool>(Group, "importantModalOpen", false));
            AddBinding(m_ImportantListRawBinding = new ValueBinding<string>(Group, "importantListRaw", string.Empty));
            AddBinding(m_WidthLabelXBinding = new ValueBinding<float>(Group, "widthLabelX", 0.5f));
            AddBinding(m_WidthLabelYBinding = new ValueBinding<float>(Group, "widthLabelY", 0.5f));
            AddBinding(m_DepthLabelXBinding = new ValueBinding<float>(Group, "depthLabelX", 0.5f));
            AddBinding(m_DepthLabelYBinding = new ValueBinding<float>(Group, "depthLabelY", 0.5f));
            AddBinding(m_FilterResidentialBinding = new ValueBinding<bool>(Group, "filterResidential", true));
            AddBinding(m_FilterCommercialBinding = new ValueBinding<bool>(Group, "filterCommercial", true));
            AddBinding(m_FilterIndustrialBinding = new ValueBinding<bool>(Group, "filterIndustrial", true));
            AddBinding(m_FilterOfficeBinding = new ValueBinding<bool>(Group, "filterOffice", true));
            AddBinding(m_UndoBusyBinding = new ValueBinding<bool>(Group, "undoBusy", false));
            AddBinding(m_UndoNoticeBinding = new ValueBinding<string>(Group, "undoNotice", ""));
            AddBinding(m_UndoFinishedNoticeBinding = new ValueBinding<string>(Group, "undoFinishedNotice", ""));
            AddBinding(m_CanUndoBinding = new ValueBinding<bool>(Group, "canUndo", false));
            AddBinding(m_UndoInProgressBinding = new ValueBinding<bool>(Group, "undoInProgress", false));
            AddBinding(m_UndoNextCountBinding = new ValueBinding<int>(Group, "undoNextCount", 0));
            AddBinding(m_UndoRestoredCountBinding = new ValueBinding<int>(Group, "undoRestoredCount", 0));
            AddBinding(m_UndoTotalCountBinding = new ValueBinding<int>(Group, "undoTotalCount", 0));
            AddBinding(m_UndoNotCapturedCountBinding = new ValueBinding<int>(Group, "undoNotCapturedCount", 0));
            AddBinding(m_UndoIncludeBuildingsBinding = new ValueBinding<bool>(Group, "undoIncludeBuildings", true));
            AddBinding(m_UndoIncludeNetworksBinding = new ValueBinding<bool>(Group, "undoIncludeNetworks", true));
            AddBinding(m_UndoIncludeAreasBinding = new ValueBinding<bool>(Group, "undoIncludeAreas", true));
            AddBinding(m_UndoIncludeTreesBinding = new ValueBinding<bool>(Group, "undoIncludeTrees", true));
            AddBinding(m_UndoIncludePropsBinding = new ValueBinding<bool>(Group, "undoIncludeProps", true));
            AddBinding(m_UndoIncludeDecalsBinding = new ValueBinding<bool>(Group, "undoIncludeDecals", true));
            AddBinding(m_UndoCountBuildingsBinding = new ValueBinding<int>(Group, "undoCountBuildings", 0));
            AddBinding(m_UndoCountNetworksBinding = new ValueBinding<int>(Group, "undoCountNetworks", 0));
            AddBinding(m_UndoCountAreasBinding = new ValueBinding<int>(Group, "undoCountAreas", 0));
            AddBinding(m_UndoCountTreesBinding = new ValueBinding<int>(Group, "undoCountTrees", 0));
            AddBinding(m_UndoCountPropsBinding = new ValueBinding<int>(Group, "undoCountProps", 0));
            AddBinding(m_UndoCountDecalsBinding = new ValueBinding<int>(Group, "undoCountDecals", 0));
            AddBinding(m_UndoHistoryDepthBinding = new ValueBinding<int>(Group, "undoHistoryDepth", 0));
            AddBinding(m_UndoSkippedCountBinding = new ValueBinding<int>(Group, "undoSkippedCount", 0));
            AddBinding(m_UndoZeroMatchBinding = new ValueBinding<bool>(Group, "undoZeroMatch", false));
            AddBinding(m_UndoFinishedRestoredCountBinding = new ValueBinding<int>(Group, "undoFinishedRestoredCount", 0));
            AddBinding(m_UndoFinishedSkippedCountBinding = new ValueBinding<int>(Group, "undoFinishedSkippedCount", 0));
            AddBinding(m_UndoFinishedTotalCountBinding = new ValueBinding<int>(Group, "undoFinishedTotalCount", 0));
            AddBinding(m_UndoFinishedNotCapturedCountBinding = new ValueBinding<int>(Group, "undoFinishedNotCapturedCount", 0));
            AddBinding(m_UndoCaptureFailedCountBinding = new ValueBinding<int>(Group, "undoCaptureFailedCount", 0));
            AddBinding(m_ColorModalOpenBinding = new ValueBinding<bool>(Group, "colorModalOpen", false));
            AddBinding(m_ColorPaletteJsonBinding = new ValueBinding<string>(Group, "colorPaletteJson", BuildCurrentColorPaletteJson()));
            m_LastPushedColorRevision = Mod.Settings != null ? Mod.Settings.GetColorRevision() : int.MinValue;

            AddBinding(new TriggerBinding(Group, "toggle", OnToggle));
            AddBinding(new TriggerBinding(Group, "switchFromBulldoze", OnSwitchFromBulldoze));
            AddBinding(new TriggerBinding(Group, "toggleUnderground", OnToggleUnderground));
            AddBinding(new TriggerBinding<int>(Group, "setSelectionMode", OnSetSelectionMode));
            AddBinding(new TriggerBinding(Group, "cycleSelectionMode", OnCycleSelectionMode));
            AddBinding(new TriggerBinding(Group, "confirm", OnConfirm));
            AddBinding(new TriggerBinding(Group, "clear", OnClear));
            AddBinding(new TriggerBinding(Group, "exit", OnExit));
            AddBinding(new TriggerBinding(Group, "confirmImportant", OnConfirmImportant));
            AddBinding(new TriggerBinding(Group, "excludeImportant", OnExcludeImportant));
            AddBinding(new TriggerBinding(Group, "cancelImportantModal", OnCancelImportantModal));
            AddBinding(new TriggerBinding<bool>(Group, "setAllFilters", OnSetAllFilters));
            AddBinding(new TriggerBinding(Group, "clearAllFilters", OnClearAllFilters));
            AddBinding(new TriggerBinding<string>(Group, "applyFilterPreset", OnApplyFilterPreset));
            AddBinding(new TriggerBinding<int>(Group, "saveCustomPreset", OnSaveCustomPreset));
            AddBinding(new TriggerBinding<int>(Group, "clearCustomPreset", OnClearCustomPreset));
            AddBinding(new TriggerBinding(Group, "togglePickSimilar", OnTogglePickSimilar));
            AddBinding(new TriggerBinding<string, bool>(Group, "setOption", OnSetOption));
            AddBinding(new TriggerBinding(Group, "undo", OnUndo));
            AddBinding(new TriggerBinding(Group, "cancelUndo", OnCancelUndo));
            AddBinding(new TriggerBinding(Group, "stopUndo", OnStopUndo));
            AddBinding(new TriggerBinding(Group, "dismissUndoNotice", OnDismissUndoNotice));
            AddBinding(new TriggerBinding<bool>(Group, "setColorModalOpen", OnSetColorModalOpen));
            AddBinding(new TriggerBinding<string, string>(Group, "setColor", OnSetColor));
            AddBinding(new TriggerBinding<string, float>(Group, "setAlpha", OnSetAlpha));
            AddBinding(new TriggerBinding(Group, "resetColors", OnResetColors));
            AddBinding(new TriggerBinding<string>(Group, "applyColorPreset", OnApplyColorPreset));
            AddBinding(new TriggerBinding<string>(Group, "soloFilter", OnSoloFilter));

            EnsureSettingsSubscription();
            CacheHotkeyActions();
            SyncBindings();
        }

        private void CacheHotkeyActions()
        {
            Settings s = Mod.Settings;
            if (s == null)
            {
                return;
            }

            m_ToggleAction = s.GetAction(Settings.ToggleActionName);
            m_SwitchModeAction = s.GetAction(Settings.SwitchModeActionName);
            m_CycleSelectionModeAction = s.GetAction(Settings.CycleSelectionModeActionName);
            m_ToggleUndergroundAction = s.GetAction(Settings.ToggleUndergroundActionName);
            m_TogglePauseUndoAction = s.GetAction(Settings.TogglePauseUndoActionName);
            m_StopUndoAction = s.GetAction(Settings.StopUndoActionName);
            m_UndoAction = s.GetAction(Settings.UndoActionName);
            m_SelectAllFiltersAction = s.GetAction(Settings.SelectAllFiltersActionName);
            m_ResetDefaultFiltersAction = s.GetAction(Settings.ResetDefaultFiltersActionName);
            m_ClearAllFiltersAction = s.GetAction(Settings.ClearAllFiltersActionName);
            m_PickSimilarAction = s.GetAction(Settings.PickSimilarActionName);
            m_RestoreSelectionHistoryAction = s.GetAction(Settings.RestoreSelectionHistoryActionName);

            m_OptionHotkeys.Clear();
            AddOptionHotkey(s, Settings.ToggleCameraAlignActionName, "cameraAlign", x => x.CameraAlignedMarquee);
            AddOptionHotkey(s, Settings.ToggleConfirmModeActionName, "confirm", x => x.ConfirmBeforeDelete);
            AddOptionHotkey(s, Settings.ToggleProtectOutsideActionName, "protectOutside", x => x.ProtectOutsideConnections);
            AddOptionHotkey(s, Settings.FilterBuildingsActionName, "buildings", x => x.DeleteBuildings);
            AddOptionHotkey(s, Settings.FilterResidentialActionName, "residential", x => x.DeleteResidential);
            AddOptionHotkey(s, Settings.FilterCommercialActionName, "commercial", x => x.DeleteCommercial);
            AddOptionHotkey(s, Settings.FilterIndustrialActionName, "industrial", x => x.DeleteIndustrial);
            AddOptionHotkey(s, Settings.FilterOfficeActionName, "office", x => x.DeleteOffice);
            AddOptionHotkey(s, Settings.FilterTreesActionName, "trees", x => x.DeleteTrees);
            AddOptionHotkey(s, Settings.FilterPropsActionName, "props", x => x.DeleteProps);
            AddOptionHotkey(s, Settings.FilterDecalsActionName, "decals", x => x.DeleteDecals);
            AddOptionHotkey(s, Settings.FilterMovingActionName, "moving", x => x.DeleteMoving);
            AddOptionHotkey(s, Settings.FilterNetworksActionName, "networks", x => x.DeleteNetworks);
            AddOptionHotkey(s, Settings.FilterAreasActionName, "areas", x => x.DeleteAreas);
        }

        private void AddOptionHotkey(Settings settings, string actionName, string key, Func<Settings, bool> getter)
        {
            ProxyAction action = settings.GetAction(actionName);
            if (action != null)
            {
                m_OptionHotkeys.Add(new OptionHotkeyEntry { Action = action, Key = key, Getter = getter });
            }
        }

        protected override void OnDestroy()
        {
            SetActionEnabled(m_UndoAction, false);
            FlushPendingColorSave();
            if (m_SubscribedSettings != null)
            {
                m_SubscribedSettings.OnChanged -= OnSettingsChanged;
                m_SubscribedSettings = null;
            }
            base.OnDestroy();
        }

        private void EnsureSettingsSubscription()
        {
            Settings current = Mod.Settings;
            if (current == m_SubscribedSettings)
            {
                return;
            }

            if (m_SubscribedSettings != null)
            {
                m_SubscribedSettings.OnChanged -= OnSettingsChanged;
            }

            m_SubscribedSettings = current;
            if (m_SubscribedSettings != null)
            {
                m_SubscribedSettings.OnChanged += OnSettingsChanged;
                m_Dirty = true;
            }
        }

        private void OnSettingsChanged()
        {
            m_Dirty = true;
            m_ColorPaletteDirty = true;
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            EnsureSettingsSubscription();

            Mod.ColorSaveCoordinator.Tick(UnityEngine.Time.unscaledDeltaTime);

            bool canUse = CanUseTool();
            if (!canUse && m_Tool != null && m_Tool.ColorModalOpen)
            {
                m_Tool.SetColorModalOpen(false);
                m_Dirty = true;
            }

            if (m_ColorPaletteDirty)
            {
                SyncColorPaletteBinding(force: false);
            }

            bool allowInput = canUse &&
                              InputManager.instance != null &&
                              MarqueePointerLogic.IsInputAllowed(
                                  UnityEngine.Application.isFocused,
                                  InputManager.instance.hasInputFieldFocus);
            bool escapePressedThisFrame = UnityEngine.InputSystem.Keyboard.current != null &&
                                          UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
            if (m_Tool != null && MarqueePointerLogic.TryConsumeUiColorModalEscape(
                    m_Tool.EscapeGate,
                    UnityEngine.Time.frameCount,
                    canUse: canUse,
                    colorModalOpen: m_Tool.ColorModalOpen,
                    importantModalOpen: m_Tool.ImportantModalOpen,
                    allowInput: allowInput,
                    escapePressedThisFrame: escapePressedThisFrame))
            {
                m_Tool.CloseColorModalFromUi();
                m_Dirty = true;
            }
            bool colorModalOpen = canUse && m_Tool != null && m_Tool.ColorModalOpen;
            if (m_ToggleAction != null)
            {
                bool canToggleTool = allowInput && !colorModalOpen;
                m_ToggleAction.shouldBeEnabled = canToggleTool;
                if (canToggleTool && m_ToggleAction.WasPressedThisFrame() && m_Tool != null)
                {
                    m_Tool.ToggleTool();
                    m_Dirty = true;
                }
            }

            bool active = canUse && m_Tool != null && m_Tool.IsToolActive;
            bool bulldozeActive = canUse && m_Tool != null && m_Tool.IsBulldozeActive;
            bool pointerBusy = active && m_Tool.IsPointerBusy;
            bool mouseButtonBusy = active && m_Tool.IsMouseButtonBusy;
            bool underground = m_Tool != null && m_Tool.EffectiveUnderground;
            int selectionMode = (canUse && Mod.Settings != null) ? (int)Mod.Settings.GetSelectionMode() : 0;
            int polygonVertexCount = active ? m_Tool.PolygonDraftVertexCount : 0;
            bool polygonSnapActive = active && m_Tool.IsPolygonSnapActive;
            bool polygonVertexLimitReached = active && m_Tool.IsPolygonVertexLimitReached;
            bool freehandDrawing = active && m_Tool.IsFreehandDrawing;
            bool shapeInvalid = active && m_Tool.LastClosedShapeInvalid;
            bool pickSimilarArmed = active && m_Tool.PickSimilarArmed;
            colorModalOpen = canUse && m_Tool != null && m_Tool.ColorModalOpen;
            SelectiveUndoUiSnapshot undoUi = m_Tool != null ? m_Tool.GetUndoUiSnapshot() : default(SelectiveUndoUiSnapshot);

            bool canTriggerUndo = m_Tool != null && MarqueePointerLogic.CanTriggerUndo(
                canUseMod: canUse,
                allowInput: allowInput,
                pointerIdle: !pointerBusy,
                importantModalOpen: m_Tool.ImportantModalOpen,
                colorModalOpen: colorModalOpen,
                queuedDeletion: m_Tool.HasQueuedDeletion,
                undoInProgress: undoUi.IsInProgress,
                canUndoForMask: undoUi.CanUndo);
            SetActionEnabled(m_UndoAction, canTriggerUndo);
            if (canTriggerUndo && m_UndoAction != null && m_UndoAction.WasPressedThisFrame())
            {
                OnUndo();
                undoUi = m_Tool.GetUndoUiSnapshot();
            }

            bool canCycleSelectionMode = m_Tool != null && MarqueePointerLogic.CanCycleSelectionMode(
                allowInput: allowInput,
                toolOrBulldozeActive: active || bulldozeActive,
                mouseButtonBusy: mouseButtonBusy,
                colorModalOpen: colorModalOpen,
                importantModalOpen: m_Tool.ImportantModalOpen,
                hasActiveUndo: undoUi.HasActiveUndo);
            SetActionEnabled(m_CycleSelectionModeAction, canCycleSelectionMode);
            if (canCycleSelectionMode && m_CycleSelectionModeAction != null && m_CycleSelectionModeAction.WasPressedThisFrame())
            {
                OnCycleSelectionMode();
            }

            bool canPickSimilar = m_Tool != null && allowInput && active && !mouseButtonBusy &&
                                  !colorModalOpen && !m_Tool.ImportantModalOpen &&
                                  !undoUi.HasActiveUndo && m_Tool.HasPending;
            SetActionEnabled(m_PickSimilarAction, canPickSimilar);
            if (canPickSimilar && m_PickSimilarAction != null && m_PickSimilarAction.WasPressedThisFrame())
            {
                OnTogglePickSimilar();
            }

            if (m_RestoreNoticeUntil > 0f && (!active || UnityEngine.Time.unscaledTime >= m_RestoreNoticeUntil))
            {
                m_RestoreNoticeUntil = 0f;
                m_RestoreNoticeBinding.Update(string.Empty);
            }

            bool canAttemptRestore = allowInput && active && !colorModalOpen && !m_Tool.ImportantModalOpen;
            SetActionEnabled(m_RestoreSelectionHistoryAction, canAttemptRestore);
            if (canAttemptRestore && m_RestoreSelectionHistoryAction != null && m_RestoreSelectionHistoryAction.WasPressedThisFrame())
            {
                OnRestoreLastSelection();
            }

            ProcessPanelHotkeys(
                MarqueePointerLogic.CanUsePanelHotkeys(allowInput, active || bulldozeActive, !pointerBusy, colorModalOpen),
                undoUi);

            bool dragging = active && m_Tool.IsDragging;
            bool drawingOrDrafting = active && m_Tool.IsDrawingOrDraftingShape;
            bool pending = active && m_Tool.HasPending;
            int resultRevision = active && m_Tool != null ? m_Tool.ResultRevision : 0;
            int shapeRevision = active && m_Tool != null ? m_Tool.ShapeRevision : 0;
            string notice = active && !dragging ? (m_Tool.SelectionNotice ?? string.Empty) : string.Empty;
            bool importantModalOpen = active && m_Tool.ImportantModalOpen;
            int historyCount = active ? m_Tool.SelectionHistoryCount : 0;
            int interruptedCount = active ? m_Tool.InterruptedDeletionCount : 0;
            int interruptedUnitCount = active && m_Tool != null ? m_Tool.InterruptedDeletionUnitCount : 0;
            int interruptedLargestUnit = active && m_Tool != null ? m_Tool.InterruptedDeletionLargestUnit : 0;
            bool reviewingInterrupted = active && m_Tool.IsReviewingInterruptedDeletion;

            ShapeMetricsSnapshot metrics = (active && m_Tool != null)
                ? m_Tool.ActiveShape.GetMetricsSnapshot()
                : default(ShapeMetricsSnapshot);
            bool showShapeSize = (drawingOrDrafting || pending) && metrics.IsUsable;
            Unity.Mathematics.float2 wPos = showShapeSize ? m_Tool.WidthLabelViewportPos : new Unity.Mathematics.float2(0.5f, 0.5f);
            Unity.Mathematics.float2 dPos = showShapeSize ? m_Tool.DepthLabelViewportPos : new Unity.Mathematics.float2(0.5f, 0.5f);
            float screenW = UnityEngine.Screen.width > 0 ? UnityEngine.Screen.width : 1920f;
            float screenH = UnityEngine.Screen.height > 0 ? UnityEngine.Screen.height : 1080f;
            float deadzoneX = 1.5f / screenW;
            float deadzoneY = 1.5f / screenH;
            bool coordsExceedDeadzone = (System.Math.Abs(wPos.x - m_LastWidthLabelX) > deadzoneX) ||
                                       (System.Math.Abs(wPos.y - m_LastWidthLabelY) > deadzoneY) ||
                                       (System.Math.Abs(dPos.x - m_LastDepthLabelX) > deadzoneX) ||
                                       (System.Math.Abs(dPos.y - m_LastDepthLabelY) > deadzoneY);

            int widthM = showShapeSize ? metrics.WidthMeters : 0;
            int depthM = showShapeSize ? metrics.DepthMeters : 0;
            bool sizeExceedDeadzone = (widthM == 0 != (m_LastBoxWidthMeters == 0)) ||
                                      (depthM == 0 != (m_LastBoxDepthMeters == 0)) ||
                                      System.Math.Abs(widthM - m_LastBoxWidthMeters) >= (int)MarqueeShapeXZ.CellSizeMeters ||
                                      System.Math.Abs(depthM - m_LastBoxDepthMeters) >= (int)MarqueeShapeXZ.CellSizeMeters;

            bool finishedDrawingOrDragging = (m_LastDragging && !dragging) || (m_LastDrawingOrDrafting && !drawingOrDrafting);

            bool stateChanged = m_Dirty ||
                !m_HasLastUndoUi ||
                undoUi.IsInProgress ||
                canUse != m_LastCanUse ||
                active != m_LastActive ||
                bulldozeActive != m_LastBulldozeActive ||
                underground != m_LastUnderground ||
                colorModalOpen != m_LastColorModalOpen ||
                importantModalOpen != m_LastImportantModalOpen ||
                selectionMode != m_LastSelectionMode ||
                polygonVertexCount != m_LastPolygonVertexCount ||
                polygonSnapActive != m_LastPolygonSnapActive ||
                polygonVertexLimitReached != m_LastPolygonVertexLimitReached ||
                freehandDrawing != m_LastFreehandDrawing ||
                shapeInvalid != m_LastShapeInvalid ||
                pickSimilarArmed != m_LastPickSimilarArmed ||
                dragging != m_LastDragging ||
                drawingOrDrafting != m_LastDrawingOrDrafting ||
                pending != m_LastPending ||
                notice != m_LastNotice ||
                historyCount != m_LastSelectionHistoryCount ||
                interruptedCount != m_LastInterruptedDeletionCount ||
                interruptedUnitCount != m_LastInterruptedUnitCount ||
                interruptedLargestUnit != m_LastInterruptedLargestUnit ||
                reviewingInterrupted != m_LastReviewingInterruptedDeletion ||
                resultRevision != m_LastResultRevision ||
                shapeRevision != m_LastShapeRevision ||
                finishedDrawingOrDragging ||
                undoUi != m_LastUndoUi;

            if (!stateChanged && !coordsExceedDeadzone && !sizeExceedDeadzone)
            {
                return;
            }

            SyncBindings();
        }

        private void ProcessPanelHotkeys(bool panelAllowInput, SelectiveUndoUiSnapshot undoUi)
        {
            SetActionEnabled(m_SwitchModeAction, panelAllowInput);
            if (panelAllowInput && m_SwitchModeAction != null && m_SwitchModeAction.WasPressedThisFrame())
            {
                OnSwitchFromBulldoze();
            }

            SetActionEnabled(m_ToggleUndergroundAction, panelAllowInput);
            if (panelAllowInput && m_ToggleUndergroundAction != null && m_ToggleUndergroundAction.WasPressedThisFrame() && m_Tool != null)
            {
                m_Tool.SetUnderground(!m_Tool.EffectiveUnderground);
                m_Dirty = true;
            }

            bool canTogglePauseUndo = panelAllowInput && undoUi.HasActiveUndo;
            SetActionEnabled(m_TogglePauseUndoAction, canTogglePauseUndo);
            if (canTogglePauseUndo && m_TogglePauseUndoAction != null && m_TogglePauseUndoAction.WasPressedThisFrame())
            {
                if (undoUi.IsInProgress)
                {
                    OnCancelUndo();
                }
                else if (undoUi.IsPaused)
                {
                    OnUndo();
                }
            }

            SetActionEnabled(m_StopUndoAction, canTogglePauseUndo);
            if (canTogglePauseUndo && m_StopUndoAction != null && m_StopUndoAction.WasPressedThisFrame())
            {
                OnStopUndo();
            }

            SetActionEnabled(m_SelectAllFiltersAction, panelAllowInput);
            if (panelAllowInput && m_SelectAllFiltersAction != null && m_SelectAllFiltersAction.WasPressedThisFrame())
            {
                OnSetAllFilters(true);
            }

            SetActionEnabled(m_ResetDefaultFiltersAction, panelAllowInput);
            if (panelAllowInput && m_ResetDefaultFiltersAction != null && m_ResetDefaultFiltersAction.WasPressedThisFrame())
            {
                OnSetAllFilters(false);
            }

            SetActionEnabled(m_ClearAllFiltersAction, panelAllowInput);
            if (panelAllowInput && m_ClearAllFiltersAction != null && m_ClearAllFiltersAction.WasPressedThisFrame())
            {
                OnClearAllFilters();
            }

            Settings s = Mod.Settings;
            for (int i = 0; i < m_OptionHotkeys.Count; i++)
            {
                OptionHotkeyEntry entry = m_OptionHotkeys[i];
                SetActionEnabled(entry.Action, panelAllowInput);
                if (panelAllowInput && s != null && entry.Action.WasPressedThisFrame())
                {
                    OnSetOption(entry.Key, !entry.Getter(s));
                }
            }
        }

        private static void SetActionEnabled(ProxyAction action, bool enabled)
        {
            if (action != null)
            {
                action.shouldBeEnabled = enabled;
            }
        }

        private bool CanUseTool()
        {
            if (!Mod.RuntimeActive || Mod.Settings == null || !Mod.Settings.Enabled)
            {
                return false;
            }

            GameMode mode = GameManager.instance != null ? GameManager.instance.gameMode : GameMode.None;
            return (mode & GameMode.GameOrEditor) != 0;
        }

        private void OnToggle()
        {
            if (m_Tool == null || !CanUseTool())
            {
                return;
            }

            m_Tool.ToggleTool();
            SyncBindings();
        }

        private void OnSwitchFromBulldoze()
        {
            if (m_Tool == null || !CanUseTool())
            {
                return;
            }

            if (m_Tool.IsToolActive)
            {
                m_Tool.SwitchToBulldoze();
            }
            else
            {
                m_Tool.SetToolActive(true);
            }
            SyncBindings();
        }

        private void OnToggleUnderground()
        {
            if (m_Tool == null || !CanUseTool())
            {
                return;
            }

            m_Tool.SetUnderground(!m_Tool.EffectiveUnderground);
            m_Dirty = true;
            SyncBindings();
        }

        private void OnSetSelectionMode(int rawMode)
        {
            if (m_Tool == null || !CanUseTool() || m_Tool.HasActiveUndo || m_Tool.ImportantModalOpen || m_Tool.ColorModalOpen)
            {
                return;
            }

            m_Tool.SetSelectionMode(MarqueePointerLogic.SanitizeSelectionMode(rawMode));
            m_Dirty = true;
            SyncBindings();
        }

        private void OnCycleSelectionMode()
        {
            if (m_Tool == null || !CanUseTool() || m_Tool.HasActiveUndo || m_Tool.ImportantModalOpen || m_Tool.ColorModalOpen)
            {
                return;
            }

            m_Tool.CycleSelectionMode();
            m_Dirty = true;
            SyncBindings();
        }

        private void OnSetAllFilters(bool enableAll)
        {
            Settings settings = Mod.Settings;
            if (settings == null) return;

            var preset = FilterPresetValues.CreateBuildingsOnly();
            preset.Trees = preset.Props = preset.Decals = true;
            preset.Moving = preset.Networks = preset.Areas = enableAll;
            ApplyGlobalFilters(settings, in preset);
        }

        private void OnClearAllFilters()
        {
            Settings settings = Mod.Settings;
            if (settings == null) return;

            var preset = settings.CaptureFilterPresetValues();
            preset.Buildings = preset.Residential = preset.Commercial = preset.Industrial = preset.Office = false;
            preset.Trees = preset.Props = preset.Decals = preset.Moving = preset.Networks = preset.Areas = false;
            ApplyGlobalFilters(settings, in preset);
        }

        private void ApplyGlobalFilters(Settings settings, in FilterPresetValues preset)
        {
            settings.ApplyFilterPresetValues(in preset);
            settings.ApplyAndSave();
            m_Tool?.OnFilterResetOrAllChanged();
            m_Dirty = true;
            SyncBindings();
        }

        private void OnRestoreLastSelection()
        {
            if (!CanUseTool() || m_Tool == null || !m_Tool.IsToolActive) return;

            string notice = m_Tool.RestoreLastSelection();
            m_RestoreNoticeBinding.Update(notice);
            m_RestoreNoticeUntil = notice.Length == 0 ? 0f : UnityEngine.Time.unscaledTime + 3f;
            if (notice.Length == 0)
            {
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnReviewInterruptedDeletion()
        {
            if (!CanUseTool() || m_Tool == null || !m_Tool.IsToolActive) return;

            m_Tool.ReviewInterruptedDeletion();
            SyncBindings();
        }

        private void OnDiscardInterruptedDeletion()
        {
            if (!CanUseTool() || m_Tool == null || !m_Tool.IsToolActive) return;

            m_Tool.DiscardInterruptedDeletion();
            SyncBindings();
        }

        private void OnSetImpactReportOpen(bool open)
        {
            m_ImpactReportOpen = open;
            UpdateImpactReport(CanUseTool() && m_Tool != null && m_Tool.IsToolActive && m_Tool.HasPending);
        }

        private void UpdateImpactReport(bool pending)
        {
            m_ImpactReportOpen &= pending;
            m_ImpactReportRawBinding.Update(m_ImpactReportOpen ? m_Tool.ImpactReportRaw : string.Empty);
        }

        private void OnApplyFilterPreset(string presetId)
        {
            if (m_Tool == null || !CanUseTool() || string.IsNullOrEmpty(presetId))
            {
                return;
            }

            if (m_Tool.ApplyFilterPreset(presetId))
            {
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnSaveCustomPreset(int slot)
        {
            Settings s = Mod.Settings;
            if (s == null || !CanUseTool())
            {
                return;
            }

            if (s.SaveCustomFilterPreset(slot))
            {
                s.ApplyAndSave();
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnClearCustomPreset(int slot)
        {
            Settings s = Mod.Settings;
            if (s == null || !CanUseTool())
            {
                return;
            }

            if (s.ClearCustomFilterPreset(slot))
            {
                s.ApplyAndSave();
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnTogglePickSimilar()
        {
            if (m_Tool == null || !CanUseTool())
            {
                return;
            }

            if (m_Tool.TogglePickSimilar() ||
                string.Equals(m_Tool.SelectionNotice, "similar_blocked_polygon", StringComparison.Ordinal))
            {
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnConfirmImportant()
        {
            if (m_Tool != null && m_Tool.IsToolActive)
            {
                m_Tool.ForceConfirmPendingDeletion();
                SyncBindings();
            }
        }

        private void OnExcludeImportant()
        {
            if (m_Tool != null && m_Tool.IsToolActive && m_Tool.ExcludeImportantBuildings())
            {
                SyncBindings();
            }
        }

        private void OnCancelImportantModal()
        {
            if (m_Tool != null)
            {
                m_Tool.CancelImportantModal();
                SyncBindings();
            }
        }

        private void OnConfirm()
        {
            if (m_Tool == null || !m_Tool.IsToolActive)
            {
                return;
            }

            m_Tool.ConfirmPendingDeletion();
            SyncBindings();
        }

        private void OnClear()
        {
            if (m_Tool == null)
            {
                return;
            }

            m_Tool.ClearPending();
            SyncBindings();
        }

        private void OnExit()
        {
            if (m_Tool == null)
            {
                return;
            }

            m_Tool.ClearPending();
            m_Tool.SetToolActive(false);
            SyncBindings();
        }

        private void OnUndo()
        {
            if (m_Tool == null || !CanUseTool())
            {
                return;
            }

            if (m_Tool.IsPointerBusy || m_Tool.ImportantModalOpen || m_Tool.HasQueuedDeletion)
            {
                return;
            }

            if (m_Tool.RequestUndo())
            {
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnCancelUndo()
        {
            if (m_Tool == null)
            {
                return;
            }

            if (m_Tool.CancelActiveUndo())
            {
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnStopUndo()
        {
            if (m_Tool == null)
            {
                return;
            }

            if (m_Tool.StopUndo())
            {
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnDismissUndoNotice()
        {
            if (m_Tool != null && m_Tool.DismissUndoFinishedNotice())
            {
                m_Dirty = true;
                SyncBindings();
            }
        }

        private void OnSoloFilter(string key)
        {
            Settings s = Mod.Settings;
            if (s == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            s.DeleteResidential = false;
            s.DeleteCommercial = false;
            s.DeleteIndustrial = false;
            s.DeleteOffice = false;
            s.DeleteBuildings = false;
            s.DeleteTrees = false;
            s.DeleteProps = false;
            s.DeleteDecals = false;
            s.DeleteMoving = false;
            s.DeleteNetworks = false;
            s.DeleteAreas = false;

            switch (key)
            {
                case "residential": s.DeleteResidential = true; break;
                case "commercial": s.DeleteCommercial = true; break;
                case "industrial": s.DeleteIndustrial = true; break;
                case "office": s.DeleteOffice = true; break;
                case "buildings": s.DeleteBuildings = true; break;
                case "trees": s.DeleteTrees = true; break;
                case "props": s.DeleteProps = true; break;
                case "decals": s.DeleteDecals = true; break;
                case "moving": s.DeleteMoving = true; break;
                case "networks": s.DeleteNetworks = true; break;
                case "areas": s.DeleteAreas = true; break;
            }

            s.ApplyAndSave();
            if (m_Tool != null)
            {
                m_Tool.ReevaluatePending();
            }
            m_Dirty = true;
            SyncBindings();
        }

        private void OnSetOption(string key, bool value)
        {
            Settings s = Mod.Settings;
            if (s == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            bool affectsSelection = true;
            switch (key)
            {
                case "buildings": s.DeleteBuildings = value; break;
                case "trees": s.DeleteTrees = value; break;
                case "props": s.DeleteProps = value; break;
                case "decals": s.DeleteDecals = value; break;
                case "moving": s.DeleteMoving = value; break;
                case "networks": s.DeleteNetworks = value; break;
                case "netRoads": s.DeleteNetRoads = value; break;
                case "netTracks": s.DeleteNetTracks = value; break;
                case "netPipelines": s.DeleteNetPipelines = value; break;
                case "netPowerLines": s.DeleteNetPowerLines = value; break;
                case "netOther": s.DeleteNetOther = value; break;
                case "areas": s.DeleteAreas = value; break;
                case "residential": s.DeleteResidential = value; break;
                case "commercial": s.DeleteCommercial = value; break;
                case "industrial": s.DeleteIndustrial = value; break;
                case "office": s.DeleteOffice = value; break;
                case "confirm":
                    s.ConfirmBeforeDelete = value;
                    affectsSelection = false;
                    break;
                case "cameraAlign":
                    s.CameraAlignedMarquee = value;
                    affectsSelection = false;
                    break;
                case "protectOutside":
                    s.ProtectOutsideConnections = value;
                    break;
                case "undoBuildings":
                    s.UndoBuildings = value;
                    affectsSelection = false;
                    break;
                case "undoNetworks":
                    s.UndoNetworks = value;
                    affectsSelection = false;
                    break;
                case "undoAreas":
                    s.UndoAreas = value;
                    affectsSelection = false;
                    break;
                case "undoTrees":
                    s.UndoTrees = value;
                    affectsSelection = false;
                    break;
                case "undoProps":
                    s.UndoProps = value;
                    affectsSelection = false;
                    break;
                case "undoDecals":
                    s.UndoDecals = value;
                    affectsSelection = false;
                    break;
                default:
                    return;
            }

            s.ApplyAndSave();
            if (affectsSelection && m_Tool != null)
            {
                m_Tool.ReevaluatePending();
            }

            SyncBindings();
        }

        private string BuildCurrentColorPaletteJson()
        {
            ColorPaletteData palette = Mod.Settings != null
                ? Mod.Settings.BuildColorPalette()
                : ColorLibrary.CreateDefaultPalette();
            return ColorLibrary.ToJson(palette);
        }

        private void SyncColorPaletteBinding(bool force)
        {
            m_ColorPaletteDirty = false;
            if (m_ColorPaletteJsonBinding == null)
            {
                return;
            }

            int revision = Mod.Settings != null ? Mod.Settings.GetColorRevision() : int.MinValue;
            if (!force && revision == m_LastPushedColorRevision)
            {
                return;
            }

            m_LastPushedColorRevision = revision;
            m_ColorPaletteJsonBinding.Update(BuildCurrentColorPaletteJson());
        }

        private void FlushPendingColorSave()
        {
            Mod.ColorSaveCoordinator.Flush();
        }

        private void ScheduleColorSaveAndSync(Settings settings, bool saveImmediately = false)
        {
            if (settings == null)
            {
                return;
            }

            SyncColorPaletteBinding(force: true);
            settings.NotifyChanged();
            if (saveImmediately)
            {
                Mod.ColorSaveCoordinator.SaveNow(settings, s => s.ApplyAndSave());
            }
            else
            {
                Mod.ColorSaveCoordinator.Schedule(settings, s => s.ApplyAndSave(), 0.25f);
            }
        }

        private void OnSetColorModalOpen(bool open)
        {
            if (m_Tool == null)
            {
                return;
            }

            m_Tool.SetColorModalOpen(open && CanUseTool());
            SyncBindings();
        }

        private void OnSetColor(string key, string hex)
        {
            Settings s = Mod.Settings;
            if (s == null)
            {
                return;
            }

            if (s.SetColorByKey(key, hex))
            {
                ScheduleColorSaveAndSync(s);
            }
        }

        private void OnSetAlpha(string key, float value)
        {
            Settings s = Mod.Settings;
            if (s == null)
            {
                return;
            }

            if (s.SetAlphaByKey(key, value))
            {
                ScheduleColorSaveAndSync(s);
            }
        }

        private void OnResetColors()
        {
            Settings s = Mod.Settings;
            if (s == null)
            {
                return;
            }

            s.SetDefaultColors();
            ScheduleColorSaveAndSync(s, saveImmediately: true);
        }

        private void OnApplyColorPreset(string presetId)
        {
            Settings s = Mod.Settings;
            if (s == null)
            {
                return;
            }

            if (ColorLibrary.TryGetPreset(presetId, out ColorPaletteData preset))
            {
                s.ApplyColorPalette(preset);
                ScheduleColorSaveAndSync(s, saveImmediately: true);
            }
        }

        private void SyncBindings()
        {
            bool canUse = CanUseTool();
            bool active = canUse && m_Tool != null && m_Tool.IsToolActive;
            bool bulldozeActive = canUse && m_Tool != null && m_Tool.IsBulldozeActive;
            bool dragging = active && m_Tool.IsDragging;
            bool drawingOrDrafting = active && m_Tool.IsDrawingOrDraftingShape;
            bool pending = active && m_Tool.HasPending;

            ShapeMetricsSnapshot metrics = (active && m_Tool != null)
                ? m_Tool.ActiveShape.GetMetricsSnapshot()
                : default(ShapeMetricsSnapshot);
            bool showShapeSize = (drawingOrDrafting || pending) && metrics.IsUsable;

            bool forceExactDimensions = !dragging && !drawingOrDrafting;

            int newWidthMeters = showShapeSize ? metrics.WidthMeters : 0;
            int newDepthMeters = showShapeSize ? metrics.DepthMeters : 0;
            if (forceExactDimensions || m_LastBoxWidthMeters < 0 || (newWidthMeters == 0 != (m_LastBoxWidthMeters == 0)) ||
                System.Math.Abs(newWidthMeters - m_LastBoxWidthMeters) >= (int)MarqueeShapeXZ.CellSizeMeters)
            {
                m_LastBoxWidthMeters = newWidthMeters;
                m_BoxWidthMetersBinding.Update(newWidthMeters);
                m_BoxWidthCellsBinding.Update(showShapeSize ? metrics.WidthCells : 0);
            }
            if (forceExactDimensions || m_LastBoxDepthMeters < 0 || (newDepthMeters == 0 != (m_LastBoxDepthMeters == 0)) ||
                System.Math.Abs(newDepthMeters - m_LastBoxDepthMeters) >= (int)MarqueeShapeXZ.CellSizeMeters)
            {
                m_LastBoxDepthMeters = newDepthMeters;
                m_BoxDepthMetersBinding.Update(newDepthMeters);
                m_BoxDepthCellsBinding.Update(showShapeSize ? metrics.DepthCells : 0);
            }

            m_ShapeModeBinding.Update(showShapeSize ? (int)metrics.Mode : 0);
            m_ShapeAreaSqMetersBinding.Update(showShapeSize ? metrics.AreaSqMeters : 0);
            m_ShapePerimeterMetersBinding.Update(showShapeSize ? metrics.PerimeterMeters : 0);
            m_ShapeVertexCountBinding.Update(showShapeSize ? metrics.VertexCount : 0);
            m_ShapeEquivalentCellsBinding.Update(showShapeSize ? metrics.EquivalentCells : 0);
            m_ShapeOriginalVertexCountBinding.Update(showShapeSize ? metrics.OriginalVertexCount : 0);
            m_ShapeSimplifiedBinding.Update(showShapeSize && metrics.WasVertexLimitApplied);
            int selectionMode = (canUse && Mod.Settings != null) ? (int)Mod.Settings.GetSelectionMode() : 0;
            int polygonVertexCount = active ? m_Tool.PolygonDraftVertexCount : 0;
            bool polygonSnapActive = active && m_Tool.IsPolygonSnapActive;
            bool polygonVertexLimitReached = active && m_Tool.IsPolygonVertexLimitReached;
            bool freehandDrawing = active && m_Tool.IsFreehandDrawing;
            bool shapeInvalid = active && m_Tool.LastClosedShapeInvalid;
            m_SelectionModeBinding.Update(selectionMode);
            m_PolygonVertexCountBinding.Update(polygonVertexCount);
            m_PolygonSnapActiveBinding.Update(polygonSnapActive);
            m_PolygonVertexLimitReachedBinding.Update(polygonVertexLimitReached);
            m_FreehandDrawingBinding.Update(freehandDrawing);
            m_ShapeInvalidBinding.Update(shapeInvalid);

            Unity.Mathematics.float2 wPos = showShapeSize ? m_Tool.WidthLabelViewportPos : new Unity.Mathematics.float2(0.5f, 0.5f);
            Unity.Mathematics.float2 dPos = showShapeSize ? m_Tool.DepthLabelViewportPos : new Unity.Mathematics.float2(0.5f, 0.5f);
            float screenW = UnityEngine.Screen.width > 0 ? UnityEngine.Screen.width : 1920f;
            float screenH = UnityEngine.Screen.height > 0 ? UnityEngine.Screen.height : 1080f;
            float deadzoneX = 1.5f / screenW;
            float deadzoneY = 1.5f / screenH;

            if (forceExactDimensions || m_LastWidthLabelX < -900f || !showShapeSize ||
                System.Math.Abs(wPos.x - m_LastWidthLabelX) > deadzoneX ||
                System.Math.Abs(wPos.y - m_LastWidthLabelY) > deadzoneY)
            {
                m_LastWidthLabelX = wPos.x;
                m_LastWidthLabelY = wPos.y;
                m_WidthLabelXBinding.Update(wPos.x);
                m_WidthLabelYBinding.Update(wPos.y);
            }

            if (forceExactDimensions || m_LastDepthLabelX < -900f || !showShapeSize ||
                System.Math.Abs(dPos.x - m_LastDepthLabelX) > deadzoneX ||
                System.Math.Abs(dPos.y - m_LastDepthLabelY) > deadzoneY)
            {
                m_LastDepthLabelX = dPos.x;
                m_LastDepthLabelY = dPos.y;
                m_DepthLabelXBinding.Update(dPos.x);
                m_DepthLabelYBinding.Update(dPos.y);
            }

            SelectionResult res = active ? m_Tool.CurrentResult : null;
            int count = res != null ? res.Count : 0;
            int cascade = res != null ? res.CascadeTotalCount : 0;
            SelectionBreakdown bd = res != null ? res.Breakdown : default(SelectionBreakdown);

            int threshold = Mod.Settings != null ? Mod.Settings.LargeSelectionThreshold : 50;
            int effectiveCount = System.Math.Max(count, cascade);
            bool overThreshold = ConfirmationPolicy.NeedsLargeSelectionWarning(effectiveCount, threshold);

            m_CanUseBinding.Update(canUse);
            m_ActiveBinding.Update(active);
            m_DraggingBinding.Update(dragging);
            m_PendingBinding.Update(pending);
            m_CountBinding.Update(count);
            m_CascadeCountBinding.Update(cascade);
            m_ThresholdBinding.Update(threshold);
            m_OverThresholdBinding.Update(overThreshold);
            m_SelectionNoticeBinding.Update(active && !dragging ? m_Tool.SelectionNotice : string.Empty);

            m_CountResidentialBinding.Update(bd.Residential);
            m_CountCommercialBinding.Update(bd.Commercial);
            m_CountIndustrialBinding.Update(bd.Industrial);
            m_CountOfficeBinding.Update(bd.Office);
            m_CountBuildingsBinding.Update(bd.OtherBuildings);
            m_CountTreesBinding.Update(bd.Trees);
            m_CountPropsBinding.Update(bd.Props);
            m_CountDecalsBinding.Update(bd.Decals);
            m_CountMovingBinding.Update(bd.Moving);
            m_CountNetworksBinding.Update(bd.Networks);
            m_CountAreasBinding.Update(bd.Areas);

            m_CountImportantBinding.Update(bd.ImportantBuildings);
            m_CountServiceBinding.Update(bd.ServiceBuildings);
            m_CountUniqueBinding.Update(bd.UniqueBuildings);
            m_CountSignatureBinding.Update(bd.SignatureBuildings);

            m_ImportantModalOpenBinding.Update(active && m_Tool != null && m_Tool.ImportantModalOpen);
            m_ImportantListRawBinding.Update((active && m_Tool != null) ? m_Tool.ImportantListRaw : string.Empty);

            bool underground = m_Tool != null && m_Tool.EffectiveUnderground;
            bool colorModalOpen = canUse && m_Tool != null && m_Tool.ColorModalOpen;
            bool pickSimilarArmed = active && m_Tool.PickSimilarArmed;

            if (Mod.Settings != null)
            {
                m_FilterBuildingsBinding.Update(Mod.Settings.DeleteBuildings);
                m_FilterTreesBinding.Update(Mod.Settings.DeleteTrees);
                m_FilterPropsBinding.Update(Mod.Settings.DeleteProps);
                m_FilterDecalsBinding.Update(Mod.Settings.DeleteDecals);
                m_FilterMovingBinding.Update(Mod.Settings.DeleteMoving);
                m_FilterNetworksBinding.Update(Mod.Settings.DeleteNetworks);
                m_FilterNetRoadsBinding.Update(Mod.Settings.DeleteNetRoads);
                m_FilterNetTracksBinding.Update(Mod.Settings.DeleteNetTracks);
                m_FilterNetPipelinesBinding.Update(Mod.Settings.DeleteNetPipelines);
                m_FilterNetPowerLinesBinding.Update(Mod.Settings.DeleteNetPowerLines);
                m_FilterNetOtherBinding.Update(Mod.Settings.DeleteNetOther);
                m_FilterAreasBinding.Update(Mod.Settings.DeleteAreas);
                m_FilterResidentialBinding.Update(Mod.Settings.DeleteResidential);
                m_FilterCommercialBinding.Update(Mod.Settings.DeleteCommercial);
                m_FilterIndustrialBinding.Update(Mod.Settings.DeleteIndustrial);
                m_FilterOfficeBinding.Update(Mod.Settings.DeleteOffice);
                m_OptConfirmBinding.Update(Mod.Settings.ConfirmBeforeDelete);
                m_OptCameraAlignBinding.Update(Mod.Settings.CameraAlignedMarquee);
                m_OptProtectOutsideBinding.Update(Mod.Settings.ProtectOutsideConnections);

                m_ActiveFilterPresetBinding.Update(ResolveActivePresetId(Mod.Settings, underground));
                m_CustomPreset1SavedBinding.Update(Mod.Settings.TryGetCustomFilterPreset(1, out _));
                m_CustomPreset2SavedBinding.Update(Mod.Settings.TryGetCustomFilterPreset(2, out _));
                m_CustomPreset3SavedBinding.Update(Mod.Settings.TryGetCustomFilterPreset(3, out _));
            }

            m_UndergroundBinding.Update(underground);
            var submitTier = Compatibility.GetTier(CompatibilityGroup.DeletionSubmit);
            string tierStr = submitTier == CompatibilityTier.Verified ? "verified" : (submitTier == CompatibilityTier.PreviewOnly ? "preview_only" : "unavailable");
            m_CompatTierBinding.Update(tierStr);
            m_CompatCanSubmitBinding.Update(Compatibility.CanSubmitDeletion);
            m_CompatCanUndoBinding.Update(Compatibility.CanUndoRebuild);
            var submitRes = Compatibility.GetGroupResult(CompatibilityGroup.DeletionSubmit);
            m_CompatMessageBinding.Update(submitRes.StatusMessage ?? string.Empty);
            bool isCalculating = active && !dragging && !pending && m_Tool != null && m_Tool.IsDrawingOrDraftingShape;
            m_ImpactCalculatingBinding.Update(isCalculating);
            m_ColorModalOpenBinding?.Update(colorModalOpen);
            m_PickSimilarArmedBinding.Update(pickSimilarArmed);

            SelectiveUndoUiSnapshot undoUi = m_Tool != null ? m_Tool.GetUndoUiSnapshot() : default(SelectiveUndoUiSnapshot);

            // 有活动撤回时用冻结的 EffectiveMask，避免暂停期间去设置页改范围后
            // 复选框显示与实际恢复范围不一致；空闲时再回落到 Settings。
            UndoAssetMask undoMaskUi = (undoUi.IsInProgress || undoUi.IsPaused)
                ? undoUi.EffectiveMask
                : (Mod.Settings != null ? Mod.Settings.BuildUndoMask() : UndoAssetMask.All);
            m_UndoIncludeBuildingsBinding.Update((undoMaskUi & UndoAssetMask.Buildings) != 0);
            m_UndoIncludeNetworksBinding.Update((undoMaskUi & UndoAssetMask.Networks) != 0);
            m_UndoIncludeAreasBinding.Update((undoMaskUi & UndoAssetMask.Areas) != 0);
            m_UndoIncludeTreesBinding.Update((undoMaskUi & UndoAssetMask.Trees) != 0);
            m_UndoIncludePropsBinding.Update((undoMaskUi & UndoAssetMask.Props) != 0);
            m_UndoIncludeDecalsBinding.Update((undoMaskUi & UndoAssetMask.Decals) != 0);

            bool canUndo = canUse && undoUi.CanUndo && !m_Tool.HasQueuedDeletion;
            bool undoInProgress = canUse && undoUi.IsInProgress;
            bool undoBusy = canUse && undoUi.HasActiveUndo;
            int undoNext = canUse ? undoUi.NextCount : 0;
            int undoRestored = undoBusy ? undoUi.RestoredCount : 0;
            int undoSkipped = undoBusy ? undoUi.SkippedCount : 0;
            int undoTotal = undoBusy ? undoUi.ActiveTotalCount : 0;
            string undoNotice = undoUi.Notice ?? "";
            string undoFinishedNotice = undoUi.FinishedNotice ?? "";
            int undoNotCaptured = canUse ? undoUi.NotCapturedCount : 0;
            int undoHistoryDepth = canUse ? undoUi.HistoryDepth : 0;
            bool undoZeroMatch = canUse && undoUi.ZeroMatch;

            m_UndoBusyBinding.Update(undoBusy);
            m_UndoNoticeBinding.Update(undoNotice);
            m_UndoFinishedNoticeBinding.Update(undoFinishedNotice);
            m_CanUndoBinding.Update(canUndo);
            m_UndoInProgressBinding.Update(undoInProgress);
            m_UndoNextCountBinding.Update(undoNext);
            m_UndoRestoredCountBinding.Update(undoRestored);
            m_UndoTotalCountBinding.Update(undoTotal);
            m_UndoNotCapturedCountBinding.Update(undoNotCaptured);
            m_UndoCountBuildingsBinding.Update(canUse ? undoUi.Counts.Buildings : 0);
            m_UndoCountNetworksBinding.Update(canUse ? undoUi.Counts.Networks : 0);
            m_UndoCountAreasBinding.Update(canUse ? undoUi.Counts.Areas : 0);
            m_UndoCountTreesBinding.Update(canUse ? undoUi.Counts.Trees : 0);
            m_UndoCountPropsBinding.Update(canUse ? undoUi.Counts.Props : 0);
            m_UndoCountDecalsBinding.Update(canUse ? undoUi.Counts.Decals : 0);
            m_UndoHistoryDepthBinding.Update(undoHistoryDepth);
            m_UndoSkippedCountBinding.Update(undoSkipped);
            m_UndoZeroMatchBinding.Update(undoZeroMatch);
            m_UndoFinishedRestoredCountBinding.Update(undoUi.FinishedRestoredCount);
            m_UndoFinishedSkippedCountBinding.Update(undoUi.FinishedSkippedCount);
            m_UndoFinishedTotalCountBinding.Update(undoUi.FinishedTotalCount);
            m_UndoFinishedNotCapturedCountBinding.Update(undoUi.FinishedNotCapturedCount);
            m_UndoCaptureFailedCountBinding.Update(canUse ? undoUi.CaptureFailedCount : 0);

            UpdateImpactReport(pending);
            m_SelectionHistoryCountBinding.Update(active && m_Tool != null ? m_Tool.SelectionHistoryCount : 0);
            m_LastInterruptedDeletionCount = active ? m_Tool.InterruptedDeletionCount : 0;
            m_LastInterruptedUnitCount = active && m_Tool != null ? m_Tool.InterruptedDeletionUnitCount : 0;
            m_LastInterruptedLargestUnit = active && m_Tool != null ? m_Tool.InterruptedDeletionLargestUnit : 0;
            m_LastReviewingInterruptedDeletion = active && m_Tool.IsReviewingInterruptedDeletion;
            m_InterruptedDeletionCountBinding.Update(m_LastInterruptedDeletionCount);
            m_InterruptedUnitCountBinding.Update(m_LastInterruptedUnitCount);
            m_InterruptedLargestUnitBinding.Update(m_LastInterruptedLargestUnit);
            m_ReviewingInterruptedDeletionBinding.Update(m_LastReviewingInterruptedDeletion);
            m_PolygonVertexLimitBinding.Update(Mod.Settings != null ? System.Math.Min(128, System.Math.Max(16, Mod.Settings.MaxPolygonVertices)) : 64);

            m_LastResultRevision = active && m_Tool != null ? m_Tool.ResultRevision : 0;
            m_LastShapeRevision = active && m_Tool != null ? m_Tool.ShapeRevision : 0;

            m_LastDragging = dragging;
            m_LastDrawingOrDrafting = drawingOrDrafting;
            m_LastPending = pending;
            m_LastCount = count;
            m_LastCascade = cascade;
            m_LastNotice = active && !dragging ? (m_Tool.SelectionNotice ?? string.Empty) : string.Empty;
            m_LastImportantModalOpen = active && m_Tool != null && m_Tool.ImportantModalOpen;
            m_LastSelectionHistoryCount = active && m_Tool != null ? m_Tool.SelectionHistoryCount : 0;

            m_LastCanUse = canUse;
            m_LastActive = active;
            m_LastBulldozeActive = bulldozeActive;
            m_LastUnderground = underground;
            m_LastColorModalOpen = colorModalOpen;
            m_LastSelectionMode = selectionMode;
            m_LastPolygonVertexCount = polygonVertexCount;
            m_LastPolygonSnapActive = polygonSnapActive;
            m_LastPolygonVertexLimitReached = polygonVertexLimitReached;
            m_LastFreehandDrawing = freehandDrawing;
            m_LastShapeInvalid = shapeInvalid;
            m_LastPickSimilarArmed = pickSimilarArmed;
            m_LastUndoUi = undoUi;
            m_HasLastUndoUi = true;
            m_Dirty = false;
        }

        private static string ResolveActivePresetId(Settings settings, bool underground)
        {
            if (settings == null)
            {
                return string.Empty;
            }

            FilterPresetValues current = settings.CaptureFilterPresetValues();
            if (current.MatchesActiveMask(FilterPresetValues.CreateGreenery()))
            {
                return FilterPresetValues.PresetGreenery;
            }
            if (current.MatchesActiveMask(FilterPresetValues.CreateBuildingsOnly()))
            {
                return FilterPresetValues.PresetBuildings;
            }
            if (current.MatchesActiveMask(FilterPresetValues.CreateSurfacesOnly()))
            {
                return FilterPresetValues.PresetSurfaces;
            }
            if (underground && current.MatchesActiveMask(FilterPresetValues.CreateUndergroundUtilities()))
            {
                return FilterPresetValues.PresetUndergroundUtilities;
            }
            if (settings.TryGetCustomFilterPreset(1, out FilterPresetValues c1) && current.MatchesActiveMask(in c1))
            {
                return "custom1";
            }
            if (settings.TryGetCustomFilterPreset(2, out FilterPresetValues c2) && current.MatchesActiveMask(in c2))
            {
                return "custom2";
            }
            if (settings.TryGetCustomFilterPreset(3, out FilterPresetValues c3) && current.MatchesActiveMask(in c3))
            {
                return "custom3";
            }
            return string.Empty;
        }
    }
}
