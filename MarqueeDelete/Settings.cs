using System;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace MarqueeDelete
{
    public enum InterfaceLanguage
    {
        FollowGame = 0,
        SimplifiedChinese = 1,
        English = 2,
    }

    public enum OverlayDetailLevel
    {
        Minimal = 0,
        Standard = 1,
        Detailed = 2,
    }

    [FileLocation("MarqueeDelete")]
    [SettingsUITabOrder(TabGeneral, TabFilters, TabHotkeys)]
    [SettingsUIGroupOrder(GroupBehavior, GroupGlobalUndo, GroupCategories, GroupUndo, GroupSafety, GroupHotkeys, GroupHotkeysUndo, GroupHotkeysFilters, GroupAbout)]
    [SettingsUIShowGroupName(GroupBehavior, GroupGlobalUndo, GroupCategories, GroupUndo, GroupSafety, GroupHotkeys, GroupHotkeysUndo, GroupHotkeysFilters)]
    [SettingsUIKeyboardAction(ToggleActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ConfirmActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(SwitchModeActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(CycleSelectionModeActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ToggleUndergroundActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ForceConfirmImportantActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ExcludeImportantActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ClearSelectionActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ToggleCameraAlignActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ToggleConfirmModeActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ToggleProtectOutsideActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(PickSimilarActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(RestoreSelectionHistoryActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(UndoActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(TogglePauseUndoActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(StopUndoActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(SelectAllFiltersActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ResetDefaultFiltersActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(ClearAllFiltersActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterBuildingsActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterResidentialActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterCommercialActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterIndustrialActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterOfficeActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterTreesActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterPropsActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterDecalsActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterMovingActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterNetworksActionName, ActionType.Button, canBeEmpty: true)]
    [SettingsUIKeyboardAction(FilterAreasActionName, ActionType.Button, canBeEmpty: true)]
    public class Settings : ModSetting
    {
        public const string TabGeneral = "General";
        public const string TabFilters = "Filters";
        public const string TabHotkeys = "Hotkeys";

        public const string GroupBehavior = "Behavior";
        public const string GroupGlobalUndo = "GlobalUndo";
        public const string GroupCategories = "Categories";
        public const string GroupUndo = "Undo";
        public const string GroupSafety = "Safety";
        public const string GroupHotkeys = "Hotkeys";
        public const string GroupHotkeysUndo = "HotkeysUndo";
        public const string GroupHotkeysFilters = "HotkeysFilters";
        public const string GroupAbout = "About";

        public const string ToggleActionName = "ToggleMarqueeDelete";
        public const string ConfirmActionName = "ConfirmMarqueeDelete";
        public const string SwitchModeActionName = "SwitchMarqueeOrBulldoze";
        public const string CycleSelectionModeActionName = "CycleSelectionMode";
        public const string ToggleUndergroundActionName = "ToggleUndergroundView";
        public const string ForceConfirmImportantActionName = "ForceConfirmImportant";
        public const string ExcludeImportantActionName = "ExcludeImportantBuildings";
        public const string ClearSelectionActionName = "ClearMarqueeSelection";
        public const string ToggleCameraAlignActionName = "ToggleCameraAligned";
        public const string ToggleConfirmModeActionName = "ToggleConfirmBeforeDelete";
        public const string ToggleProtectOutsideActionName = "ToggleProtectOutside";
        public const string PickSimilarActionName = "PickSimilarInSelection";
        public const string RestoreSelectionHistoryActionName = "RestoreSelectionHistory";

        public const string UndoActionName = "UndoMarqueeDelete";
        public const string TogglePauseUndoActionName = "TogglePauseUndo";
        public const string StopUndoActionName = "StopMarqueeUndo";

        public const string SelectAllFiltersActionName = "SelectAllDeleteFilters";
        public const string ResetDefaultFiltersActionName = "ResetDefaultDeleteFilters";
        public const string ClearAllFiltersActionName = "ClearAllDeleteFilters";
        public const string FilterBuildingsActionName = "ToggleFilterBuildings";
        public const string FilterResidentialActionName = "ToggleFilterResidential";
        public const string FilterCommercialActionName = "ToggleFilterCommercial";
        public const string FilterIndustrialActionName = "ToggleFilterIndustrial";
        public const string FilterOfficeActionName = "ToggleFilterOffice";
        public const string FilterTreesActionName = "ToggleFilterTrees";
        public const string FilterPropsActionName = "ToggleFilterProps";
        public const string FilterDecalsActionName = "ToggleFilterDecals";
        public const string FilterMovingActionName = "ToggleFilterMoving";
        public const string FilterNetworksActionName = "ToggleFilterNetworks";
        public const string FilterAreasActionName = "ToggleFilterAreas";

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public bool Enabled { get; set; }

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public InterfaceLanguage Language { get; set; } = InterfaceLanguage.FollowGame;

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public bool ConfirmBeforeDelete { get; set; }

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public bool SkipBatchRevalidation { get; set; }

        [SettingsUISection(TabGeneral, GroupBehavior)]
        [SettingsUISlider(min = 10f, max = 500f, step = 10f)]
        public int LargeSelectionThreshold { get; set; }

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public bool CameraAlignedMarquee { get; set; }

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public bool PlaySounds { get; set; }

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public bool EnablePanelGlow { get; set; } = false;

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public bool EnableUndoProgressFx { get; set; } = false;

        [SettingsUISection(TabGeneral, GroupBehavior)]
        public OverlayDetailLevel OverlayDetail { get; set; } = OverlayDetailLevel.Standard;

        [SettingsUISection(TabGeneral, GroupBehavior)]
        [SettingsUISlider(min = 16f, max = 128f, step = 8f)]
        public int MaxPolygonVertices { get; set; } = 64;

        [SettingsUISection(TabGeneral, GroupGlobalUndo)]
        public bool EnableGlobalUndo { get; set; } = true;

        [SettingsUISection(TabGeneral, GroupGlobalUndo)]
        public bool UndoTrackBuild { get; set; } = true;

        [SettingsUISection(TabGeneral, GroupGlobalUndo)]
        public bool UndoTrackBulldoze { get; set; } = true;

        [SettingsUISection(TabGeneral, GroupGlobalUndo)]
        public bool UndoTrackUpgrade { get; set; } = true;

        [SettingsUISection(TabGeneral, GroupGlobalUndo)]
        public bool UndoTrackZone { get; set; } = true;

        [SettingsUISection(TabGeneral, GroupGlobalUndo)]
        public bool UndoTrackTerrain { get; set; } = true;

        [SettingsUISection(TabGeneral, GroupGlobalUndo)]
        [SettingsUISlider(min = 1f, max = 20f, step = 1f)]
        public int MaxUndoHistorySteps { get; set; } = 10;

        [SettingsUISection(TabGeneral, GroupBehavior)]
        [SettingsUISlider(min = 400f, max = 5000f, step = 200f)]
        public int MaxAssetOverlayBudget { get; set; } = 1600;

        [SettingsUISection(TabGeneral, GroupBehavior)]
        [SettingsUISlider(min = 8f, max = 128f, step = 8f)]
        public int DeletionBatchSize { get; set; } = 128;

        [SettingsUISection(TabGeneral, GroupBehavior)]
        [SettingsUISlider(min = 2f, max = 33f, step = 1f)]
        public int DeletionBatchBudgetMs { get; set; } = 8;

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteBuildings { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteResidential { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteCommercial { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteIndustrial { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteOffice { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteTrees { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteProps { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteDecals { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteMoving { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteNetworks { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteNetRoads { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteNetTracks { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteNetPipelines { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteNetPowerLines { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteNetOther { get; set; }

        [SettingsUISection(TabFilters, GroupCategories)]
        public bool DeleteAreas { get; set; }

        [SettingsUISection(TabFilters, GroupUndo)]
        public bool UndoBuildings { get; set; }

        [SettingsUISection(TabFilters, GroupUndo)]
        public bool UndoNetworks { get; set; }

        [SettingsUISection(TabFilters, GroupUndo)]
        public bool UndoAreas { get; set; }

        [SettingsUISection(TabFilters, GroupUndo)]
        public bool UndoTrees { get; set; }

        [SettingsUISection(TabFilters, GroupUndo)]
        public bool UndoProps { get; set; }

        [SettingsUISection(TabFilters, GroupUndo)]
        public bool UndoDecals { get; set; }

        [SettingsUISection(TabFilters, GroupSafety)]
        public bool ProtectOutsideConnections { get; set; }

        [SettingsUISection(TabFilters, GroupSafety)]
        public bool ProtectUniqueBuildings { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(BindingKeyboard.X, ToggleActionName, ctrl: true, shift: true)]
        public ProxyBinding ToggleToolBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(BindingKeyboard.Enter, ConfirmActionName)]
        public ProxyBinding ConfirmDeleteBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(SwitchModeActionName)]
        public ProxyBinding SwitchModeBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(CycleSelectionModeActionName)]
        public ProxyBinding CycleSelectionModeBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(ToggleUndergroundActionName)]
        public ProxyBinding ToggleUndergroundBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(ForceConfirmImportantActionName)]
        public ProxyBinding ForceConfirmImportantBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(ExcludeImportantActionName)]
        public ProxyBinding ExcludeImportantBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(ClearSelectionActionName)]
        public ProxyBinding ClearSelectionBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(ToggleCameraAlignActionName)]
        public ProxyBinding ToggleCameraAlignBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(ToggleConfirmModeActionName)]
        public ProxyBinding ToggleConfirmModeBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(ToggleProtectOutsideActionName)]
        public ProxyBinding ToggleProtectOutsideBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(PickSimilarActionName)]
        public ProxyBinding PickSimilarBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeys)]
        [SettingsUIKeyboardBinding(RestoreSelectionHistoryActionName)]
        public ProxyBinding RestoreSelectionHistoryBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysUndo)]
        [SettingsUIKeyboardBinding(BindingKeyboard.Z, UndoActionName, ctrl: true)]
        public ProxyBinding UndoBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysUndo)]
        [SettingsUIKeyboardBinding(TogglePauseUndoActionName)]
        public ProxyBinding TogglePauseUndoBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysUndo)]
        [SettingsUIKeyboardBinding(StopUndoActionName)]
        public ProxyBinding StopUndoBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(SelectAllFiltersActionName)]
        public ProxyBinding SelectAllFiltersBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(ResetDefaultFiltersActionName)]
        public ProxyBinding ResetDefaultFiltersBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(ClearAllFiltersActionName)]
        public ProxyBinding ClearAllFiltersBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterBuildingsActionName)]
        public ProxyBinding FilterBuildingsBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterResidentialActionName)]
        public ProxyBinding FilterResidentialBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterCommercialActionName)]
        public ProxyBinding FilterCommercialBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterIndustrialActionName)]
        public ProxyBinding FilterIndustrialBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterOfficeActionName)]
        public ProxyBinding FilterOfficeBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterTreesActionName)]
        public ProxyBinding FilterTreesBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterPropsActionName)]
        public ProxyBinding FilterPropsBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterDecalsActionName)]
        public ProxyBinding FilterDecalsBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterMovingActionName)]
        public ProxyBinding FilterMovingBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterNetworksActionName)]
        public ProxyBinding FilterNetworksBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIKeyboardBinding(FilterAreasActionName)]
        public ProxyBinding FilterAreasBinding { get; set; }

        [SettingsUISection(TabHotkeys, GroupHotkeysFilters)]
        [SettingsUIButton]
        public bool ResetHotkeysBtn
        {
            set
            {
                if (!value)
                {
                    return;
                }

                ResetKeyBindings();
                ApplyAndSave();
            }
        }

        [SettingsUISection(TabGeneral, GroupAbout)]
        public string Version => Mod.ModVersion;

        [SettingsUISection(TabFilters, GroupSafety)]
        [SettingsUIButton]
        public bool ResetSafeDefaultsBtn
        {
            set
            {
                SetDefaults();
                ApplyAndSave();
            }
        }

        // Hidden color customization settings (edited only via in-game mod React palette modal).
        [SettingsUIHidden]
        public int SelectionMode { get; set; }

        [SettingsUIHidden]
        public string CustomFilterPreset1 { get; set; }

        [SettingsUIHidden]
        public string CustomFilterPreset2 { get; set; }

        [SettingsUIHidden]
        public string CustomFilterPreset3 { get; set; }

        [SettingsUIHidden]
        public string ColorMarqueeGroundBorder { get; set; }

        [SettingsUIHidden]
        public string ColorMarqueeGroundFill { get; set; }

        [SettingsUIHidden]
        public string ColorMarqueeUndergroundBorder { get; set; }

        [SettingsUIHidden]
        public string ColorMarqueeUndergroundFill { get; set; }

        [SettingsUIHidden]
        public string ColorBuildingResidential { get; set; }

        [SettingsUIHidden]
        public string ColorBuildingCommercial { get; set; }

        [SettingsUIHidden]
        public string ColorBuildingIndustrial { get; set; }

        [SettingsUIHidden]
        public string ColorBuildingOffice { get; set; }

        [SettingsUIHidden]
        public string ColorBuildingService { get; set; }

        [SettingsUIHidden]
        public string ColorWarningPulseStart { get; set; }

        [SettingsUIHidden]
        public string ColorWarningPulseEnd { get; set; }

        [SettingsUIHidden]
        public string ColorAssetNetwork { get; set; }

        [SettingsUIHidden]
        public string ColorAssetTree { get; set; }

        [SettingsUIHidden]
        public string ColorAssetProp { get; set; }

        [SettingsUIHidden]
        public string ColorAssetDecal { get; set; }

        [SettingsUIHidden]
        public string ColorAssetArea { get; set; }

        [SettingsUIHidden]
        public string ColorAssetMoving { get; set; }

        [SettingsUIHidden]
        public string ColorDarkOutline { get; set; }

        [SettingsUIHidden]
        public float AlphaMarqueeFill { get; set; }

        [SettingsUIHidden]
        public float AlphaBuildingFill { get; set; }

        [SettingsUIHidden]
        public float AlphaOutline { get; set; }

        private int m_ColorRevision;

        public event Action OnChanged;

        public Settings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        public int GetColorRevision() => m_ColorRevision;

        public sealed override void SetDefaults()
        {
            Enabled = true;
            ConfirmBeforeDelete = true;
            SkipBatchRevalidation = false;
            LargeSelectionThreshold = 50;
            CameraAlignedMarquee = true;
            PlaySounds = true;
            EnablePanelGlow = false;
            EnableUndoProgressFx = false;
            SelectionMode = (int)MarqueeSelectionMode.Rectangle;
            OverlayDetail = OverlayDetailLevel.Standard;
            MaxPolygonVertices = 64;
            EnableGlobalUndo = true;
            UndoTrackBuild = true;
            UndoTrackBulldoze = true;
            UndoTrackUpgrade = true;
            UndoTrackZone = true;
            UndoTrackTerrain = true;
            MaxUndoHistorySteps = 10;
            MaxAssetOverlayBudget = 1600;
            DeletionBatchSize = 128;
            DeletionBatchBudgetMs = 8;

            DeleteBuildings = true;
            DeleteResidential = true;
            DeleteCommercial = true;
            DeleteIndustrial = true;
            DeleteOffice = true;
            DeleteTrees = true;
            DeleteProps = true;
            DeleteDecals = true;
            DeleteMoving = false;
            DeleteNetworks = false;
            DeleteNetRoads = true;
            DeleteNetTracks = true;
            DeleteNetPipelines = true;
            DeleteNetPowerLines = true;
            DeleteNetOther = true;
            DeleteAreas = false;

            UndoBuildings = true;
            UndoNetworks = true;
            UndoAreas = true;
            UndoTrees = true;
            UndoProps = true;
            UndoDecals = true;

            ProtectOutsideConnections = true;
            ProtectUniqueBuildings = false;

            SetDefaultColors();
        }

        public void SetDefaultColors()
        {
            ApplyColorPalette(ColorLibrary.CreateDefaultPalette());
        }

        public void SanitizeColors()
        {
            ColorPaletteData clean = BuildColorPalette();
            ApplyColorPalette(clean);
        }

        public ColorPaletteData BuildColorPalette()
        {
            var p = new ColorPaletteData
            {
                MarqueeGroundBorder = ColorMarqueeGroundBorder,
                MarqueeGroundFill = ColorMarqueeGroundFill,
                MarqueeUndergroundBorder = ColorMarqueeUndergroundBorder,
                MarqueeUndergroundFill = ColorMarqueeUndergroundFill,
                BuildingResidential = ColorBuildingResidential,
                BuildingCommercial = ColorBuildingCommercial,
                BuildingIndustrial = ColorBuildingIndustrial,
                BuildingOffice = ColorBuildingOffice,
                BuildingService = ColorBuildingService,
                WarningPulseStart = ColorWarningPulseStart,
                WarningPulseEnd = ColorWarningPulseEnd,
                AssetNetwork = ColorAssetNetwork,
                AssetTree = ColorAssetTree,
                AssetProp = ColorAssetProp,
                AssetDecal = ColorAssetDecal,
                AssetArea = ColorAssetArea,
                AssetMoving = ColorAssetMoving,
                DarkOutline = ColorDarkOutline,
                AlphaMarqueeFill = AlphaMarqueeFill,
                AlphaBuildingFill = AlphaBuildingFill,
                AlphaOutline = AlphaOutline,
            };
            ColorLibrary.Sanitize(p);
            return p;
        }

        public void ApplyColorPalette(ColorPaletteData palette)
        {
            ColorPaletteData clean = palette != null ? palette.Clone() : ColorLibrary.CreateDefaultPalette();
            ColorLibrary.Sanitize(clean);

            ColorMarqueeGroundBorder = clean.MarqueeGroundBorder;
            ColorMarqueeGroundFill = clean.MarqueeGroundFill;
            ColorMarqueeUndergroundBorder = clean.MarqueeUndergroundBorder;
            ColorMarqueeUndergroundFill = clean.MarqueeUndergroundFill;

            ColorBuildingResidential = clean.BuildingResidential;
            ColorBuildingCommercial = clean.BuildingCommercial;
            ColorBuildingIndustrial = clean.BuildingIndustrial;
            ColorBuildingOffice = clean.BuildingOffice;
            ColorBuildingService = clean.BuildingService;

            ColorWarningPulseStart = clean.WarningPulseStart;
            ColorWarningPulseEnd = clean.WarningPulseEnd;

            ColorAssetNetwork = clean.AssetNetwork;
            ColorAssetTree = clean.AssetTree;
            ColorAssetProp = clean.AssetProp;
            ColorAssetDecal = clean.AssetDecal;
            ColorAssetArea = clean.AssetArea;
            ColorAssetMoving = clean.AssetMoving;

            ColorDarkOutline = clean.DarkOutline;

            AlphaMarqueeFill = clean.AlphaMarqueeFill;
            AlphaBuildingFill = clean.AlphaBuildingFill;
            AlphaOutline = clean.AlphaOutline;

            m_ColorRevision++;
        }

        public bool SetColorByKey(string key, string hex)
        {
            ColorPaletteData palette = BuildColorPalette();
            if (!ColorLibrary.SetColorByKey(palette, key, hex))
            {
                return false;
            }

            ApplyColorPalette(palette);
            return true;
        }

        public bool SetAlphaByKey(string key, float value)
        {
            ColorPaletteData palette = BuildColorPalette();
            if (!ColorLibrary.SetAlphaByKey(palette, key, value))
            {
                return false;
            }

            ApplyColorPalette(palette);
            return true;
        }

        public override void Apply()
        {
            SanitizeColors();
            base.Apply();
            Mod.ApplyInterfaceLanguage(this);
            OnChanged?.Invoke();
        }

        public UndoAssetMask BuildUndoMask()
        {
            UndoAssetMask mask = UndoAssetMask.None;
            if (UndoBuildings) mask |= UndoAssetMask.Buildings;
            if (UndoNetworks) mask |= UndoAssetMask.Networks;
            if (UndoAreas) mask |= UndoAssetMask.Areas;
            if (UndoTrees) mask |= UndoAssetMask.Trees;
            if (UndoProps) mask |= UndoAssetMask.Props;
            if (UndoDecals) mask |= UndoAssetMask.Decals;
            return mask;
        }

        public SelectionFilterMask BuildFilterMask()
        {
            return CaptureFilterPresetValues().BuildFilterMask();
        }

        public FilterPresetValues CaptureFilterPresetValues()
        {
            return new FilterPresetValues
            {
                Buildings = DeleteBuildings,
                Residential = DeleteResidential,
                Commercial = DeleteCommercial,
                Industrial = DeleteIndustrial,
                Office = DeleteOffice,
                Trees = DeleteTrees,
                Props = DeleteProps,
                Decals = DeleteDecals,
                Moving = DeleteMoving,
                Networks = DeleteNetworks,
                Areas = DeleteAreas,
                NetRoads = DeleteNetRoads,
                NetTracks = DeleteNetTracks,
                NetPipelines = DeleteNetPipelines,
                NetPowerLines = DeleteNetPowerLines,
                NetOther = DeleteNetOther,
            };
        }

        public void ApplyFilterPresetValues(in FilterPresetValues preset)
        {
            DeleteBuildings = preset.Buildings;
            DeleteResidential = preset.Residential;
            DeleteCommercial = preset.Commercial;
            DeleteIndustrial = preset.Industrial;
            DeleteOffice = preset.Office;
            DeleteTrees = preset.Trees;
            DeleteProps = preset.Props;
            DeleteDecals = preset.Decals;
            DeleteMoving = preset.Moving;
            DeleteNetworks = preset.Networks;
            DeleteAreas = preset.Areas;
            DeleteNetRoads = preset.NetRoads;
            DeleteNetTracks = preset.NetTracks;
            DeleteNetPipelines = preset.NetPipelines;
            DeleteNetPowerLines = preset.NetPowerLines;
            DeleteNetOther = preset.NetOther;
        }

        public bool TryGetCustomFilterPreset(int slot, out FilterPresetValues preset)
        {
            string raw = slot switch
            {
                1 => CustomFilterPreset1,
                2 => CustomFilterPreset2,
                3 => CustomFilterPreset3,
                _ => null,
            };
            return FilterPresetValues.TryDeserialize(raw, out preset);
        }

        public bool SaveCustomFilterPreset(int slot)
        {
            string serialized = CaptureFilterPresetValues().Serialize();
            switch (slot)
            {
                case 1: CustomFilterPreset1 = serialized; return true;
                case 2: CustomFilterPreset2 = serialized; return true;
                case 3: CustomFilterPreset3 = serialized; return true;
                default: return false;
            }
        }

        public bool ClearCustomFilterPreset(int slot)
        {
            switch (slot)
            {
                case 1: CustomFilterPreset1 = string.Empty; return true;
                case 2: CustomFilterPreset2 = string.Empty; return true;
                case 3: CustomFilterPreset3 = string.Empty; return true;
                default: return false;
            }
        }

        public SafetyOptions BuildSafetyOptions()
        {
            return new SafetyOptions
            {
                ProtectOutsideConnections = ProtectOutsideConnections,
                ProtectUniqueBuildings = ProtectUniqueBuildings,
            };
        }

        public MarqueeSelectionMode GetSelectionMode()
        {
            return MarqueePointerLogic.SanitizeSelectionMode(SelectionMode);
        }

        public void SetSelectionModeValue(MarqueeSelectionMode mode)
        {
            SelectionMode = (int)MarqueePointerLogic.SanitizeSelectionMode((int)mode);
        }

        public void NotifyChanged()
        {
            OnChanged?.Invoke();
        }
    }
}
