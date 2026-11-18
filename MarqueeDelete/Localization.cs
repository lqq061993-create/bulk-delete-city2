using System;
using System.Collections.Generic;
using Colossal;
using Game.Modding;

namespace MarqueeDelete
{
    internal sealed class ModLocaleSource : IDictionarySource
    {
        private readonly Settings m_Settings;
        private readonly string m_Locale;
        private readonly LocaleEN m_English;
        private readonly LocaleZH m_Chinese;

        internal ModLocaleSource(Settings settings, string locale)
        {
            m_Settings = settings;
            m_Locale = locale;
            m_English = new LocaleEN(settings);
            m_Chinese = new LocaleZH(settings);
        }

        private bool IsChineseGameLocale => m_Locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        private bool UsesChinese =>
            m_Settings.Language == InterfaceLanguage.SimplifiedChinese ||
            (m_Settings.Language != InterfaceLanguage.English && IsChineseGameLocale);

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var entries = UsesChinese
                ? m_Chinese.ReadEntries(errors, indexCounts)
                : m_English.ReadEntries(errors, indexCounts);
            foreach (var entry in entries)
                yield return entry;

            // Keep language names in the game locale so they remain recognizable after a switch.
            yield return new KeyValuePair<string, string>(
                m_Settings.GetEnumValueLocaleID(InterfaceLanguage.FollowGame), IsChineseGameLocale ? "跟随游戏" : "Follow Game");
            yield return new KeyValuePair<string, string>(
                m_Settings.GetEnumValueLocaleID(InterfaceLanguage.SimplifiedChinese), IsChineseGameLocale ? "汉语" : "Chinese");
            yield return new KeyValuePair<string, string>(
                m_Settings.GetEnumValueLocaleID(InterfaceLanguage.English), IsChineseGameLocale ? "英语" : "English");
        }

        public void Unload() { }
    }

    public sealed class LocaleEN : IDictionarySource
    {
        private readonly Settings m_Settings;

        public LocaleEN(Settings settings)
        {
            m_Settings = settings;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Settings.GetSettingsLocaleID(), "Bulk Delete" },
                { m_Settings.GetBindingMapLocaleID(), "Bulk Delete" },

                { m_Settings.GetOptionTabLocaleID(Settings.TabGeneral), "General" },
                { m_Settings.GetOptionTabLocaleID(Settings.TabFilters), "Filters & Safety" },
                { m_Settings.GetOptionTabLocaleID(Settings.TabHotkeys), "Keybindings" },

                { m_Settings.GetOptionGroupLocaleID(Settings.GroupBehavior), "Tool Behavior" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupCategories), "Target Categories" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupUndo), "Selective Undo Scope" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupSafety), "Safety & Protections" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupHotkeys), "Core Tool Hotkeys" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupHotkeysUndo), "Undo Control Hotkeys" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupHotkeysFilters), "Asset Filter Hotkeys" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupAbout), "About" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.Language)), "Mod Language" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.Language)), "Choose the language for this mod’s tool and settings." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.Enabled)), "Enable Bulk Delete" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.Enabled)), "Master switch to enable or disable the Bulk Delete tool and hotkey." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ConfirmBeforeDelete)), "Confirm Before Delete" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ConfirmBeforeDelete)), "When ON: highlights targets first on release and requires Enter or clicking Delete to demolish. When OFF: release mouse to delete immediately without secondary confirmation even for large selections (unique/signature buildings are still safely protected)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.LargeSelectionThreshold)), "Large Selection Warning Threshold" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.LargeSelectionThreshold)), "Visual count threshold for displaying the large selection badge and warning pulse in the UI. (Note: when Confirm Before Delete is OFF, release deletes directly without confirmation)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.CameraAlignedMarquee)), "Camera-Aligned Box" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.CameraAlignedMarquee)), "Rotate the drag rectangle to match your camera angle instead of aligning strictly North-South." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.PlaySounds)), "Sound Effects" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.PlaySounds)), "Play standard bulldoze and UI sound feedback when deleting targets." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.OverlayDetail)), "Overlay Detail Level" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.OverlayDetail)), "Set overlay rendering quality: Minimal (boundary & count only, zero interior primitives), Standard (compact building blocks & budget 800, recommended), or Detailed (full graphics & custom budget)." },
                { m_Settings.GetEnumValueLocaleID(OverlayDetailLevel.Minimal), "Minimal (Zero Interior Primitives)" },
                { m_Settings.GetEnumValueLocaleID(OverlayDetailLevel.Standard), "Standard (Compact Blocks, Budget 800)" },
                { m_Settings.GetEnumValueLocaleID(OverlayDetailLevel.Detailed), "Detailed (Full Graphics)" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.MaxPolygonVertices)), "Max Polygon Vertices" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.MaxPolygonVertices)), "Maximum allowed vertices for polygon selection (16 to 128, default 64)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.MaxUndoHistorySteps)), "Undo History Depth" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.MaxUndoHistorySteps)), "Number of deletion steps remembered for selective undo (1 to 10, default 5)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.MaxAssetOverlayBudget)), "Asset Overlay Primitive Budget" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.MaxAssetOverlayBudget)), "Maximum projected overlay primitives rendered simultaneously before distance decimation (400 to 5000, default 1600)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeletionBatchSize)), "Large Deletion Batch Size" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeletionBatchSize)), "Maximum targets handed to the game per batch when a selection is too large to delete at once (8 to 128, default 128)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeletionBatchBudgetMs)), "Large Deletion Frame Budget" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeletionBatchBudgetMs)), "Main-thread milliseconds one batch may spend preparing commands; the batch size shrinks to stay inside it (2 to 33, default 8)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteBuildings)), "Service & Other Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteBuildings)), "Include standalone service, unique, signature, and other non-zoned buildings (zoned buildings have separate filters below)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteResidential)), "Residential Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteResidential)), "Include residential buildings when marquee selecting." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteCommercial)), "Commercial Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteCommercial)), "Include commercial buildings when marquee selecting." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteIndustrial)), "Industrial Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteIndustrial)), "Include industrial and warehouse buildings when marquee selecting." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteOffice)), "Office Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteOffice)), "Include office buildings when marquee selecting." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteTrees)), "Trees & Plants" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteTrees)), "Include standalone trees, bushes, and vegetation." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteProps)), "Props & Objects" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteProps)), "Include standalone static props, benches, planters, boulders, and standalone fixtures." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteDecals)), "Decals & Overlays" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteDecals)), "Include standalone decals and lightweight ground geometry." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteMoving)), "Vehicles & Citizens" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteMoving)), "Include vehicles, pedestrians, and animals inside the marquee box (off by default)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetworks)), "Networks (Use With Caution)" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetworks)), "Allow marquee deletion of roads, tracks, pathways, pipes, and powerlines (disabled by default). Deleting active intersections can disrupt routes." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetRoads)), "Networks: Roads & Pathways" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetRoads)), "Include roads, streets, highways, and pedestrian pathways when Networks filter is enabled." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetTracks)), "Networks: Tracks" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetTracks)), "Include train, tram, and subway tracks when Networks filter is enabled." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetPipelines)), "Networks: Water & Sewage Pipes" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetPipelines)), "Include standalone water, sewage, and stormwater pipes when Networks filter is enabled." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetPowerLines)), "Networks: Power Lines" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetPowerLines)), "Include standalone low- and high-voltage power lines when Networks filter is enabled." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetOther)), "Networks: Other Networks" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetOther)), "Include waterways, fences, and unclassified network types when Networks filter is enabled." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteAreas)), "Surfaces & Lots" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteAreas)), "Include custom painted surfaces, spaces, and lots (map tiles and districts are always protected)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoBuildings)), "Undo Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoBuildings)), "Include buildings (together with their upgrades, attached areas, and internal networks) when undoing." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoNetworks)), "Undo Roads & Networks" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoNetworks)), "Include standalone roads, bridges, tunnels, tracks, pipes, and powerlines when undoing." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoAreas)), "Undo Surfaces" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoAreas)), "Include standalone painted surfaces, spaces, and lots when undoing." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoTrees)), "Undo Trees" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoTrees)), "Include standalone trees and plants when undoing." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoProps)), "Undo Props" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoProps)), "Include standalone props and street furniture when undoing." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoDecals)), "Undo Decals" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoDecals)), "Include standalone decals and ground markings when undoing." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ProtectOutsideConnections)), "Protect Outside Connections" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ProtectOutsideConnections)), "Always skip map-edge outside connections (highways, rails, power/water links)." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ProtectUniqueBuildings)), "Protect Unique Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ProtectUniqueBuildings)), "Skip signature and unique landmarks unless explicitly disabled." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ResetSafeDefaultsBtn)), "Reset to Safe Defaults" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ResetSafeDefaultsBtn)), "Restore all filters and safety options back to recommended defaults." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.Version)), "Mod Version" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.Version)), "Installed version: built and tested against Cities: Skylines II 1.6.4f1. Other 1.6.x builds load in compatibility mode." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleToolBinding)), "Toggle Bulk Delete Tool" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleActionName), "Toggle Bulk Delete Tool" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleToolBinding)), "Open or exit the Bulk Delete tool." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ConfirmDeleteBinding)), "Confirm Delete Selection" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ConfirmActionName), "Confirm Delete Selection" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ConfirmDeleteBinding)), "Delete the currently highlighted marquee targets." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.SwitchModeBinding)), "Switch Marquee / Single Bulldoze" },
                { m_Settings.GetBindingKeyLocaleID(Settings.SwitchModeActionName), "Switch Marquee / Single Bulldoze" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.SwitchModeBinding)), "Switch between Bulk Delete and standard single-click Bulldoze." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.CycleSelectionModeBinding)), "Cycle Selection Mode" },
                { m_Settings.GetBindingKeyLocaleID(Settings.CycleSelectionModeActionName), "Cycle Selection Mode" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.CycleSelectionModeBinding)), "Cycle between Rectangle, Polygon, and Freehand selection modes." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleUndergroundBinding)), "Toggle Ground / Underground View" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleUndergroundActionName), "Toggle Ground / Underground View" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleUndergroundBinding)), "Switch between Ground and Underground layer views while using Bulk Delete." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ForceConfirmImportantBinding)), "Confirm Including Important Buildings" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ForceConfirmImportantActionName), "Confirm Including Important Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ForceConfirmImportantBinding)), "Confirm demolition in the important buildings prompt and delete everything selected." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ExcludeImportantBinding)), "Exclude Important Buildings" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ExcludeImportantActionName), "Exclude Important Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ExcludeImportantBinding)), "Remove important buildings and any targets that cascade into them from the selection in the prompt, while keeping remaining selections." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ClearSelectionBinding)), "Clear Current Selection / Close Prompt" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ClearSelectionActionName), "Clear Current Selection / Close Prompt" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ClearSelectionBinding)), "Clear the current pending selection or cancel the important buildings prompt." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleCameraAlignBinding)), "Toggle Camera-Aligned Box" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleCameraAlignActionName), "Toggle Camera-Aligned Box" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleCameraAlignBinding)), "Toggle aligning the selection box with the camera angle." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleConfirmModeBinding)), "Toggle Confirm Before Delete" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleConfirmModeActionName), "Toggle Confirm Before Delete" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleConfirmModeBinding)), "Toggle whether selections wait for confirmation before deleting." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleProtectOutsideBinding)), "Toggle Protect Outside Connections" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleProtectOutsideActionName), "Toggle Protect Outside Connections" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleProtectOutsideBinding)), "Toggle protection for map-edge outside connections." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.PickSimilarBinding)), "Match Similar in Selection" },
                { m_Settings.GetBindingKeyLocaleID(Settings.PickSimilarActionName), "Match Similar in Selection" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.PickSimilarBinding)), "Arm one-shot Match Similar picking on a pending selection, then click an object to keep only matching items." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.RestoreSelectionHistoryBinding)), "Restore Last Selection" },
                { m_Settings.GetBindingKeyLocaleID(Settings.RestoreSelectionHistoryActionName), "Restore Last Selection" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.RestoreSelectionHistoryBinding)), "Restore the most recent marquee selection boundary from history." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoBinding)), "Undo Last Bulk Delete" },
                { m_Settings.GetBindingKeyLocaleID(Settings.UndoActionName), "Undo Last Bulk Delete" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoBinding)), "Start or resume undoing the latest marquee deletion snapshot." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.TogglePauseUndoBinding)), "Pause / Resume Undo" },
                { m_Settings.GetBindingKeyLocaleID(Settings.TogglePauseUndoActionName), "Pause / Resume Undo" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.TogglePauseUndoBinding)), "Pause an active undo or resume a paused undo." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.StopUndoBinding)), "Stop Undo" },
                { m_Settings.GetBindingKeyLocaleID(Settings.StopUndoActionName), "Stop Undo" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.StopUndoBinding)), "Stop the current undo while keeping already submitted items and unlocking the tool." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.SelectAllFiltersBinding)), "Select All Asset Filters" },
                { m_Settings.GetBindingKeyLocaleID(Settings.SelectAllFiltersActionName), "Select All Asset Filters" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.SelectAllFiltersBinding)), "Enable all target asset category and zone filters." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ResetDefaultFiltersBinding)), "Reset Filters to Safe Defaults" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ResetDefaultFiltersActionName), "Reset Filters to Safe Defaults" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ResetDefaultFiltersBinding)), "Restore target asset category filters to safe defaults." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ClearAllFiltersBinding)), "Clear All Asset Filters" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ClearAllFiltersActionName), "Clear All Asset Filters" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ClearAllFiltersBinding)), "Disable all target asset category and zone filters." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterBuildingsBinding)), "Toggle Filter: Service & Other Buildings" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterBuildingsActionName), "Toggle Filter: Service & Other Buildings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterBuildingsBinding)), "Toggle including service, unique, and other non-zoned buildings." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterResidentialBinding)), "Toggle Filter: Residential" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterResidentialActionName), "Toggle Filter: Residential" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterResidentialBinding)), "Toggle including residential buildings." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterCommercialBinding)), "Toggle Filter: Commercial" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterCommercialActionName), "Toggle Filter: Commercial" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterCommercialBinding)), "Toggle including commercial buildings." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterIndustrialBinding)), "Toggle Filter: Industrial" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterIndustrialActionName), "Toggle Filter: Industrial" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterIndustrialBinding)), "Toggle including industrial buildings." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterOfficeBinding)), "Toggle Filter: Office" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterOfficeActionName), "Toggle Filter: Office" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterOfficeBinding)), "Toggle including office buildings." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterTreesBinding)), "Toggle Filter: Trees" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterTreesActionName), "Toggle Filter: Trees" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterTreesBinding)), "Toggle including standalone trees and vegetation." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterPropsBinding)), "Toggle Filter: Props" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterPropsActionName), "Toggle Filter: Props" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterPropsBinding)), "Toggle including standalone props and street furniture." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterDecalsBinding)), "Toggle Filter: Decals" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterDecalsActionName), "Toggle Filter: Decals" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterDecalsBinding)), "Toggle including standalone decals and markings." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterMovingBinding)), "Toggle Filter: Vehicles / Citizens / Animals" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterMovingActionName), "Toggle Filter: Vehicles / Citizens / Animals" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterMovingBinding)), "Toggle including moving or parked vehicles, citizens, and animals." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterNetworksBinding)), "Toggle Filter: Roads & Networks" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterNetworksActionName), "Toggle Filter: Roads & Networks" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterNetworksBinding)), "Toggle including roads, bridges, tunnels, tracks, pipes, and powerlines." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterAreasBinding)), "Toggle Filter: Surfaces & Areas" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterAreasActionName), "Toggle Filter: Surfaces & Areas" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterAreasBinding)), "Toggle including painted surfaces, spaces, and custom lots." },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ResetHotkeysBtn)), "Reset All Keybindings" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ResetHotkeysBtn)), "Restore default keybindings (keeps Ctrl+Shift+X, Enter, and Ctrl+Z, clears all others)." },

                // HUD UI strings
                { "MarqueeDelete.Title", "Bulk Delete" },
                { "MarqueeDelete.SectionTitle", "Bulk Delete" },
                { "MarqueeDelete.ModeLabel", "Marquee" },
                { "MarqueeDelete.ViewModeTitle", "View" },
                { "MarqueeDelete.SurfaceView", "Ground" },
                { "MarqueeDelete.UndergroundView", "Underground" },
                { "MarqueeDelete.ModeToggleTooltip", "Toggle Marquee Drag Delete inside Bulldoze (Ctrl+Shift+X)" },
                { "MarqueeDelete.ViewToggleTooltip", "Switch Ground / Underground layer view & filter" },
                { "MarqueeDelete.TooltipActive", "Bulk Delete Active (LMB Drag/Click Add | RMB Remove Single/Exit | ESC or Clear Button Resets)" },
                { "MarqueeDelete.TooltipIdle", "Bulk Delete" },
                { "MarqueeDelete.HintIdle", "Drag Left Mouse to box-select, or click an object to add. Right-click exits." },
                { "MarqueeDelete.HintDragging", "Release Left Mouse to lock selection, Right-click or ESC to cancel drag." },
                { "MarqueeDelete.HintPending", "LMB click adds • RMB removes single (won't clear all) • Use ESC or Clear button to clear all • Enter/Delete confirms." },
                { "MarqueeDelete.FiltersLabel", "Assets" },
                { "MarqueeDelete.FilterBuildings", "Buildings" },
                { "MarqueeDelete.FilterTrees", "Trees" },
                { "MarqueeDelete.FilterProps", "Props" },
                { "MarqueeDelete.FilterDecals", "Decals" },
                { "MarqueeDelete.FilterMoving", "Vehicles" },
                { "MarqueeDelete.FilterNetworks", "Roads/Nets" },
                { "MarqueeDelete.FilterAreas", "Surfaces" },
                { "MarqueeDelete.SelectAllBtn", "All" },
                { "MarqueeDelete.ClearAllFiltersBtn", "Clear All" },
                { "MarqueeDelete.DescClearAllFilters", "Disable all building zone and asset category filters so you can pick a single category cleanly." },
                { "MarqueeDelete.ResetFiltersBtn", "Reset" },
                { "MarqueeDelete.OptionConfirm", "Confirm" },
                { "MarqueeDelete.OptionCameraAlign", "Cam Align" },
                { "MarqueeDelete.OptionProtectOutside", "Safe Edge" },
                { "MarqueeDelete.SelectedLabel", "Selected" },
                { "MarqueeDelete.WithChildrenSuffix", "total w/ children" },
                { "MarqueeDelete.LargeWarning", "Large selection" },
                { "MarqueeDelete.LargeDirectNotice", "Large selection (Direct delete on release)" },
                { "MarqueeDelete.HintCompactDragDirect", "Release to delete immediately (Confirm OFF, no confirmation) • RMB Cancel" },
                { "MarqueeDelete.HintConfirmOffIdle", "Confirm OFF: Drag and release to delete directly without confirmation" },
                { "MarqueeDelete.DeletionProgress", "Deleting in batches. {0} targets left. Esc stops." },
                { "MarqueeDelete.DeletionChanged", "The remaining targets changed. Review the area before continuing; already submitted deletions still apply." },
                { "MarqueeDelete.DeletionStopped", "Batch demolition stopped. You can undo submitted static assets." },
                { "MarqueeDelete.DeletionObserveTimeout", "Waiting for the demolition result timed out, so no further batches were submitted. Commands already submitted may still run; check the city before continuing." },
                { "MarqueeDelete.DeletionBlockedCompat", "This game build is not verified, so demolition is unavailable. The selection is kept; you can adjust it or discard the task." },
                { "MarqueeDelete.InterruptedUnitsSummary", "Remaining targets span {0} deletion units; the largest holds {1} entities" },
                { "MarqueeDelete.UndoDisabledCompatTitle", "Undo (Unavailable)" },
                { "MarqueeDelete.UndoDisabledCompatDesc", "Undo is disabled on this game build: it has not been verified, or the rebuild components are missing." },
                { "MarqueeDelete.CalculatingImpact", "Calculating impact..." },
                { "MarqueeDelete.CompatUnavailableNotice", "Deletion is unavailable in this build." },
                { "MarqueeDelete.HotkeyCancel", "Cancel/Clear" },
                { "MarqueeDelete.HotkeyConfirm", "Confirm/Close" },
                { "MarqueeDelete.HotkeySolo", "Solo Filter" },
                { "MarqueeDelete.HotkeySwitchTool", "Switch Mode" },
                { "MarqueeDelete.HotkeyUnderground", "Underground" },
                { "MarqueeDelete.HotkeyUndo", "Undo" },
                { "MarqueeDelete.ImportantProtectedSummary", "Contains {0} important building(s) (extra confirmation required)" },
                { "MarqueeDelete.ModeInfoBtn", "Details" },
                { "MarqueeDelete.OptFollowCamera", "Follow camera direction" },
                { "MarqueeDelete.OptOff", "OFF" },
                { "MarqueeDelete.OptOn", "ON" },
                { "MarqueeDelete.PreviewOnlyNotice", "Current game build is in preview mode - deletion is disabled pending write verification." },
                { "MarqueeDelete.UndergroundBtn", "Underground" },
                { "MarqueeDelete.InterruptedSummary", "Demolition paused. {0} recorded targets remain to review." },
                { "MarqueeDelete.ReviewingRemaining", "Review the remaining targets below. Only the targets shown below will be submitted when you confirm." },
                { "MarqueeDelete.ReviewRemainingBtn", "Review remaining" },
                { "MarqueeDelete.DiscardRemainingBtn", "Discard remainder" },
                { "MarqueeDelete.ResumeUnavailable", "No recorded targets can be removed under the current filters and protections." },
                { "MarqueeDelete.SelectionChanged", "The affected objects changed. Review the selection before confirming again." },
                { "MarqueeDelete.SelectionProtected", "This selection would remove a protected object. Adjust the area or filters." },
                { "MarqueeDelete.SelectionHiddenRetained", "No matches under current filters, but {0} manually added object(s) are retained. Re-enable filters to restore, or use ESC / Clear to reset." },
                { "MarqueeDelete.SelectionHiddenBox", "No matches inside the locked marquee under current filters. Adjust filters to restore, or use ESC / Clear to reset." },
                { "MarqueeDelete.SelectionRestoreBlocked", "Cannot restore boundary while busy, modal is open, or another operation is active." },
                { "MarqueeDelete.SelectionHistoryEmpty", "No saved marquee boundary in history." },
                { "MarqueeDelete.ConfirmBtn", "Confirm deletion" },
                { "MarqueeDelete.ClearBtn", "Clear" },
                { "MarqueeDelete.CloseBtn", "Single" },
                { "MarqueeDelete.StatusReadyGround", "Ground Marquee Ready" },
                { "MarqueeDelete.StatusReadyUnderground", "Underground Marquee Ready" },
                { "MarqueeDelete.StatusDragging", "Selecting" },
                { "MarqueeDelete.HintCompactIdle", "LMB Drag • RMB Exit" },
                { "MarqueeDelete.HintCompactDrag", "Release Lock • RMB Cancel" },
                { "MarqueeDelete.DescModeToggle", "Toggle between Marquee drag-box deletion and standard single-click Bulldozer (Hotkey: Ctrl+Shift+X)." },
                { "MarqueeDelete.DescUnderground", "Toggle between Ground and Underground layer views to isolate surface assets or underground pipes/tunnels." },
                { "MarqueeDelete.DescCameraAlign", "Align the selection rectangle with your current camera angle instead of world North-South." },
                { "MarqueeDelete.DescConfirm", "When ON: highlight targets first and require Enter / Delete click before demolishing. When OFF: release mouse to delete immediately — large selections will NOT prompt secondary confirmation either." },
                { "MarqueeDelete.DescProtectOutside", "Automatically protect map-edge highway, rail, water, and power outside connections." },
                { "MarqueeDelete.DescResetFilters", "Restore target category filters back to recommended safe defaults." },
                { "MarqueeDelete.DescSelectAll", "Toggle all target asset categories on or restore defaults." },
                { "MarqueeDelete.DescBuildings", "Include standalone buildings and their attached lots, fences, and sub-elements." },
                { "MarqueeDelete.DescNetworks", "Include roads, bridges, tunnels, tracks, pathways, pipes, and powerlines." },
                { "MarqueeDelete.DescTrees", "Include standalone trees, bushes, and wild vegetation." },
                { "MarqueeDelete.DescProps", "Include standalone props, street furniture, benches, lamps, and boulders." },
                { "MarqueeDelete.DescDecals", "Include standalone ground decals, stains, and surface markings." },
                { "MarqueeDelete.DescAreas", "Include painted surfaces, spaces, and custom lots (map tiles & districts are always safe)." },
                { "MarqueeDelete.DescMoving", "Include moving or parked vehicles, citizens, and animals inside the box." },
                { "MarqueeDelete.ZonesLabel", "Zones" },
                { "MarqueeDelete.FilterResidential", "Residential" },
                { "MarqueeDelete.FilterCommercial", "Commercial" },
                { "MarqueeDelete.FilterIndustrial", "Industrial" },
                { "MarqueeDelete.FilterOffice", "Office" },
                { "MarqueeDelete.FilterOtherBuildings", "Service/Other" },
                { "MarqueeDelete.DescResidential", "Include residential buildings (highlighted in green)." },
                { "MarqueeDelete.DescCommercial", "Include commercial buildings (highlighted in blue)." },
                { "MarqueeDelete.DescIndustrial", "Include industrial, warehouse, and extractor buildings (highlighted in yellow)." },
                { "MarqueeDelete.DescOffice", "Include office buildings (highlighted in purple)." },
                { "MarqueeDelete.UnitCells", "cells" },
                { "MarqueeDelete.SizeLabel", "Size" },
                { "MarqueeDelete.ImportantTag", "Important" },
                { "MarqueeDelete.ImportantService", "Service" },
                { "MarqueeDelete.ImportantUnique", "Unique" },
                { "MarqueeDelete.ImportantSignature", "Signature" },
                { "MarqueeDelete.ModalTitle", "Important Buildings Detected" },
                { "MarqueeDelete.ModalSubtitle", "The selection contains critical city buildings. Please confirm before demolishing:" },
                { "MarqueeDelete.ModalConfirmBtn", "Confirm Demolish" },
                { "MarqueeDelete.ModalExcludeBtn", "Exclude Important" },
                { "MarqueeDelete.ModalCancelBtn", "Cancel" },
                { "MarqueeDelete.ImportantConfirmNote", "(Confirm required)" },
                { "MarqueeDelete.MoreImportantItems", "more important buildings" },
                { "MarqueeDelete.SelectionExcludedImportant", "Excluded {0} important building(s). Review the remaining {1} object(s) and confirm to delete." },
                { "MarqueeDelete.SelectionExcludedAllImportant", "Excluded all {0} important building(s); no remaining objects in selection." },
                { "MarqueeDelete.WarnNoFilters", "All filters are off — drag will select nothing." },
                { "MarqueeDelete.WarnUndergroundNoFilter", "Underground view: enable Roads/Nets or Vehicles to target pipes and tunnels." },
                { "MarqueeDelete.UndoGeometryOnly", "Undo rebuilds geometry, not households or simulation state." },
                { "MarqueeDelete.UndoPartial", "Only part of this deletion was recorded (8,000-record snapshot limit)." },
                { "MarqueeDelete.UndoPaused", "Undo paused; deletion is locked until resumed. Click Resume or Undo to continue." },
                { "MarqueeDelete.UndoWaitingOwner", "Waiting for parent buildings to become ready..." },
                { "MarqueeDelete.UndoError", "Undo stopped due to an error; remaining objects were not rebuilt. Reload your save to recover." },
                { "MarqueeDelete.UndoStalled", "Creation requests submitted, but some attached objects were skipped after waiting too long for parent buildings and remain unbuilt. Reload your save to recover; actual rebuilding is not verified." },
                { "MarqueeDelete.UndoSubmitted", "All creation requests have been submitted. Whether the game rebuilds them depends on simulation and is not verified." },
                { "MarqueeDelete.UndoSubmittedPartial", "Creation requests submitted with omissions (some items were skipped or not recorded). Whether the game rebuilds them depends on simulation and is not verified." },
                { "MarqueeDelete.UndoStopped", "Undo stopped. The remaining items were not submitted for rebuild." },
                { "MarqueeDelete.UndoNothingCaptured", "No undo snapshot saved for this deletion (all {0} supported object(s) failed to capture)." },
                { "MarqueeDelete.UndoNotCapturedHint", "Selected undo categories had {0} object(s) not captured in this snapshot." },
                { "MarqueeDelete.UndoBtn", "Undo" },
                { "MarqueeDelete.UndoPauseBtn", "Pause" },
                { "MarqueeDelete.UndoStopBtn", "Stop" },
                { "MarqueeDelete.UndoResumeBtn", "Resume" },
                { "MarqueeDelete.UndoDismissBtn", "Dismiss" },
                { "MarqueeDelete.UndoRemainingLabel", "Remaining requests" },
                { "MarqueeDelete.UndoRemainingNotRebuiltLabel", "Remaining not rebuilt" },
                { "MarqueeDelete.UndoTooltip", "Undo Delete ({0})" },
                { "MarqueeDelete.UndoProgressLabel", "Creation requests submitted" },
                { "MarqueeDelete.UndoSkippedLabel", "Skipped" },
                { "MarqueeDelete.UndoNotCapturedLabel", "Not captured" },
                { "MarqueeDelete.UndoFinishedSummary", "Submitted {0} / {1}" },
                { "MarqueeDelete.UndoScopeLabel", "Undo Scope" },
                { "MarqueeDelete.UndoBuildingsCountNote", "Building count shows total rebuild records (includes upgrades, attached areas & sub-nets), not building count alone." },
                { "MarqueeDelete.UndoZeroMatchHint", "Top undo snapshot has no records matching the selected undo categories ({0} snapshot(s) queued)." },
                { "MarqueeDelete.UndoLockedScopeHint", "Scope locked while current undo is active or paused." },
                { "MarqueeDelete.UndoDescBuildings", "Restore buildings together with their upgrades, attached areas, and internal networks. Badge shows total building-related records, not building count alone." },
                { "MarqueeDelete.UndoDescNetworks", "Restore standalone roads, bridges, tunnels, tracks, pipes, and powerlines." },
                { "MarqueeDelete.UndoDescAreas", "Restore standalone painted surfaces, spaces, and custom lots." },
                { "MarqueeDelete.UndoDescTrees", "Restore standalone trees and plants." },
                { "MarqueeDelete.UndoDescProps", "Restore standalone props and street furniture." },
                { "MarqueeDelete.UndoDescDecals", "Restore standalone decals and surface markings." },
                { "MarqueeDelete.UndoNothingTooltip", "Nothing to undo yet — delete objects with Bulk Delete first." },
                { "MarqueeDelete.DescUndo", "Restore selected asset types from the latest deletion snapshot in small batches (Hotkey: Ctrl+Z)." },
                { "MarqueeDelete.UndoScopeConsumedHint", "An undo consumes the whole snapshot: categories you leave unchecked are dropped and cannot be restored from that step later." },
                { "MarqueeDelete.SelectionModeLabel", "Mode" },
                { "MarqueeDelete.SelectionModeRect", "Rect" },
                { "MarqueeDelete.SelectionModePolygon", "Polygon" },
                { "MarqueeDelete.SelectionModeFreehand", "Stroke" },
                { "MarqueeDelete.DescSelectionModeRect", "Rectangle selection: drag to box-select, or click once to quick-pick a cell." },
                { "MarqueeDelete.DescSelectionModePolygon", "Polygon selection: click to add vertices, click start or press Enter to close (max 64)." },
                { "MarqueeDelete.DescSelectionModeFreehand", "Freehand stroke selection: hold left mouse button to draw, release to auto-close." },
                { "MarqueeDelete.StatusPolygonDraft", "Drafting" },
                { "MarqueeDelete.StatusFreehandDrawing", "Drawing" },
                { "MarqueeDelete.HintCompactPolygonIdle", "Click to add vertices · Click start / Enter to close · Right-click/Backspace undo" },
                { "MarqueeDelete.HintCompactFreehandIdle", "Hold left mouse button and drag to draw stroke · Release to close" },
                { "MarqueeDelete.HintCompactFreehandDraw", "Keep dragging to outline the area · Release to close · Esc cancels" },
                { "MarqueeDelete.PolygonVerticesProgress", "Vertices: {0} / {1}" },
                { "MarqueeDelete.PolygonSnapReady", "Snapped to start — click or press Enter to close polygon" },
                { "MarqueeDelete.HintPolygonNeedMore", "Add at least {0} more vertex(es) before closing" },
                { "MarqueeDelete.HintPolygonCloseReady", "Click start or press Enter to close · Right-click/Backspace undoes last vertex" },
                { "MarqueeDelete.PolygonVertexCapWarn", "Polygon vertex cap (64) reached — close shape or undo a vertex." },
                { "MarqueeDelete.ShapeInvalidWarn", "Selection area too small (< 1 m²) or degenerate — shape discarded." },
                { "MarqueeDelete.ShapeSimplifiedNotice", "Simplified: {0} raw pts → {1} vertices" },
                { "MarqueeDelete.UnitSqMeters", "m²" },
                { "MarqueeDelete.UnitVertices", "pts" },
                { "MarqueeDelete.EdgeSimplifiedBadge", "Simplified" },

                { "MarqueeDelete.PresetLabel", "Presets" },
                { "MarqueeDelete.PresetGreenery", "Greenery" },
                { "MarqueeDelete.PresetBuildings", "Buildings" },
                { "MarqueeDelete.PresetSurfaces", "Surfaces" },
                { "MarqueeDelete.PresetUndergroundUtilities", "UG Utilities" },
                { "MarqueeDelete.DescPresetGreenery", "Target trees & vegetation only." },
                { "MarqueeDelete.DescPresetBuildings", "Target all residential, commercial, industrial, office, and service buildings only." },
                { "MarqueeDelete.DescPresetSurfaces", "Target painted surfaces, spaces, and custom lots only." },
                { "MarqueeDelete.DescPresetUndergroundUtilities", "Target underground pipes & power lines and switch to Underground view (restores previous view when switching away unless manually changed)." },
                { "MarqueeDelete.CustomPresetSlot", "Custom {0}" },
                { "MarqueeDelete.CustomPresetSaveBtn", "Save" },
                { "MarqueeDelete.CustomPresetClearBtn", "Del" },
                { "MarqueeDelete.CustomPresetEmpty", "Empty" },
                { "MarqueeDelete.CustomPresetApplyHint", "Click to apply saved custom filter slot {0}." },
                { "MarqueeDelete.CustomPresetSaveHint", "Save current asset & network filters into slot {0}." },
                { "MarqueeDelete.NetSubExpandBtn", "Net Types" },
                { "MarqueeDelete.NetSubRoads", "Roads" },
                { "MarqueeDelete.NetSubTracks", "Tracks" },
                { "MarqueeDelete.NetSubPipelines", "Pipes" },
                { "MarqueeDelete.NetSubPowerLines", "Power" },
                { "MarqueeDelete.NetSubOther", "Other Networks" },
                { "MarqueeDelete.DescNetSubRoads", "Toggle roads and pathways inside the Networks filter." },
                { "MarqueeDelete.DescNetSubTracks", "Toggle train, tram, and subway tracks inside the Networks filter." },
                { "MarqueeDelete.DescNetSubPipelines", "Toggle standalone water/sewage pipes inside the Networks filter." },
                { "MarqueeDelete.DescNetSubPowerLines", "Toggle standalone power lines inside the Networks filter." },
                { "MarqueeDelete.DescNetSubOther", "Toggle waterways, fences, and unclassified networks inside the Networks filter." },
                { "MarqueeDelete.NetSubMasterOffHint", "Enable Roads/Nets above for these network sub-filters to apply." },
                { "MarqueeDelete.PickSimilarBtn", "Match Same" },
                { "MarqueeDelete.DescPickSimilarBtn", "One-shot pick: click an object to keep only matching prefab & category items inside the current selection." },
                { "MarqueeDelete.PickSimilarArmedHint", "Match Same armed — click any object to keep only matching items (ESC / RMB cancels)." },
                { "MarqueeDelete.PickSimilarBlockedPolygon", "Match Same is unavailable while drawing a polygon — finish or cancel the polygon draft first." },
                { "MarqueeDelete.PickSimilarMatched", "Kept {0} matching item(s) of the selected type." },
                { "MarqueeDelete.PickSimilarNoTarget", "No valid object clicked — Match Same cancelled." },
                { "MarqueeDelete.PickSimilarNoPrefab", "Clicked target has no reusable prefab identity — selection unchanged." },
                { "MarqueeDelete.PickSimilarNoMatch", "No items matching that object exist inside the current selection — selection unchanged." },

                { "MarqueeDelete.ColorBtn", "Colors" },
                { "MarqueeDelete.ColorTooltip", "Customize Marquee & Highlight Colors" },
                { "MarqueeDelete.ColorModalTitle", "Color Palette" },
                { "MarqueeDelete.ColorModalSubtitle", "Live preview on ground — changes apply immediately." },
                { "MarqueeDelete.ColorPresetTitle", "Presets" },
                { "MarqueeDelete.ColorPresetClassic", "Classic" },
                { "MarqueeDelete.ColorPresetNeon", "High-Contrast Neon" },
                { "MarqueeDelete.ColorPresetColorblind", "Colorblind Safe" },
                { "MarqueeDelete.ColorPresetPastel", "Soft Translucent" },
                { "MarqueeDelete.ColorItemsTitle", "Highlight & Marquee Colors (18)" },
                { "MarqueeDelete.ColorGroupMarquee", "Marquee Box" },
                { "MarqueeDelete.ColorGroupBuildings", "Buildings" },
                { "MarqueeDelete.ColorGroupWarning", "Threshold Pulse" },
                { "MarqueeDelete.ColorGroupAssets", "Non-Building Assets" },
                { "MarqueeDelete.ColorGroupOutline", "Dark Outline" },
                { "MarqueeDelete.ColorKey_marqueeGroundBorder", "Ground Border" },
                { "MarqueeDelete.ColorKey_marqueeGroundFill", "Ground Fill" },
                { "MarqueeDelete.ColorKey_marqueeUndergroundBorder", "Underground Border" },
                { "MarqueeDelete.ColorKey_marqueeUndergroundFill", "Underground Fill" },
                { "MarqueeDelete.ColorKey_buildingResidential", "Residential" },
                { "MarqueeDelete.ColorKey_buildingCommercial", "Commercial" },
                { "MarqueeDelete.ColorKey_buildingIndustrial", "Industrial" },
                { "MarqueeDelete.ColorKey_buildingOffice", "Office" },
                { "MarqueeDelete.ColorKey_buildingService", "Public / Service" },
                { "MarqueeDelete.ColorKey_warningPulseStart", "Pulse Warm" },
                { "MarqueeDelete.ColorKey_warningPulseEnd", "Pulse Alert" },
                { "MarqueeDelete.ColorKey_assetNetwork", "Networks / Roads" },
                { "MarqueeDelete.ColorKey_assetTree", "Trees" },
                { "MarqueeDelete.ColorKey_assetProp", "Props" },
                { "MarqueeDelete.ColorKey_assetDecal", "Decals" },
                { "MarqueeDelete.ColorKey_assetArea", "Areas / Surfaces" },
                { "MarqueeDelete.ColorKey_assetMoving", "Vehicles / Citizens" },
                { "MarqueeDelete.ColorKey_darkOutline", "Outline Base" },
                { "MarqueeDelete.ColorSwatchesLabel", "Quick Swatches" },
                { "MarqueeDelete.ColorRgbEditorLabel", "Custom RGB" },
                { "MarqueeDelete.ColorAlphaTitle", "Opacity" },
                { "MarqueeDelete.ColorAlphaMarqueeFill", "Marquee Fill" },
                { "MarqueeDelete.ColorAlphaBuildingFill", "Building Lot Fill" },
                { "MarqueeDelete.ColorAlphaOutline", "Outline" },
                { "MarqueeDelete.ColorOutlineHiddenTag", "0% = Hidden" },
                { "MarqueeDelete.ColorResetBtn", "Reset Defaults" },
                { "MarqueeDelete.ColorCloseBtn", "Close" },
                { "MarqueeDelete.ColorEscHint", "ESC closes palette" },

                { "MarqueeDelete.RestoreHistoryBtn", "Restore" },
                { "MarqueeDelete.DescRestoreHistory", "Restore recent marquee boundary from history (up to 5 saved)." },
                { "MarqueeDelete.ImpactReportBtn", "Impact Report" },
                { "MarqueeDelete.DescImpactReport", "View grouped prefab impact breakdown before deleting." },
                { "MarqueeDelete.ImpactReportTitle", "Demolition Impact Breakdown" },
                { "MarqueeDelete.ImpactReportMore", "+{0} other prefabs" },
                { "MarqueeDelete.SoloCategoryHint", "Alt+Click: Solo this category" },
                { "MarqueeDelete.CloseImpactReportBtn", "Close" },
                { "MarqueeDelete.TabTargets", "Targets" },
                { "MarqueeDelete.TabOptions", "Options" },
            };
        }

        public void Unload()
        {
        }
    }

    public sealed class LocaleZH : IDictionarySource
    {
        private readonly Settings m_Settings;

        public LocaleZH(Settings settings)
        {
            m_Settings = settings;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Settings.GetSettingsLocaleID(), "Bulk Delete · 批量删除" },
                { m_Settings.GetBindingMapLocaleID(), "Bulk Delete · 批量删除" },

                { m_Settings.GetOptionTabLocaleID(Settings.TabGeneral), "常规设置" },
                { m_Settings.GetOptionTabLocaleID(Settings.TabFilters), "目标过滤与安全" },
                { m_Settings.GetOptionTabLocaleID(Settings.TabHotkeys), "快捷键" },

                { m_Settings.GetOptionGroupLocaleID(Settings.GroupBehavior), "工具行为" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupCategories), "可删除目标类别" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupUndo), "可选撤回资产范围" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupSafety), "安全保护" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupHotkeys), "核心工具快捷键" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupHotkeysUndo), "撤回控制快捷键" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupHotkeysFilters), "资产筛选快捷键" },
                { m_Settings.GetOptionGroupLocaleID(Settings.GroupAbout), "关于" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.Language)), "模组语言" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.Language)), "选择本模组的工具面板及设置语言。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.Enabled)), "启用 Bulk Delete · 批量删除" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.Enabled)), "启用或停用批量删除工具及其快捷键。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ConfirmBeforeDelete)), "删除前确认" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ConfirmBeforeDelete)), "开启时：松开鼠标后先高亮预览目标，需按 Enter 或点击按钮确认后再删除。关闭时：松手即删，大选区也不二次确认（特殊/地标等重要建筑仍保留安全底线保护）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.LargeSelectionThreshold)), "大批量警示阈值" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.LargeSelectionThreshold)), "界面大选区警示标签与视觉脉冲的数量阈值。（注意：当“删除前确认”关闭时，松手即删，大选区也不会二次确认）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.CameraAlignedMarquee)), "随镜头方向框选" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.CameraAlignedMarquee)), "框选矩形自动跟随当前视角朝向旋转，而非强制固定为正南正北方向。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.PlaySounds)), "操作音效" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.PlaySounds)), "框选确认与批量删除时播放游戏原版拆除音效。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.OverlayDetail)), "覆盖层精细度" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.OverlayDetail)), "设置覆盖层渲染精细度：轻量（仅画选区外框与数字，零内部图元）、标准（建筑紧凑色块·预算 800，推荐默认）、全细节（完整效果·自定义预算）。" },
                { m_Settings.GetEnumValueLocaleID(OverlayDetailLevel.Minimal), "轻量（零内部图元·极速）" },
                { m_Settings.GetEnumValueLocaleID(OverlayDetailLevel.Standard), "标准（紧凑色块·预算 800）" },
                { m_Settings.GetEnumValueLocaleID(OverlayDetailLevel.Detailed), "全细节（完整效果·自定义预算）" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.MaxPolygonVertices)), "多边形顶点上限" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.MaxPolygonVertices)), "多边形框选允许绘制的最大顶点数（16至128，默认64）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.MaxUndoHistorySteps)), "撤销历史步数" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.MaxUndoHistorySteps)), "保留的批量删除撤销快照步数（1至10步，默认5步）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.MaxAssetOverlayBudget)), "资产覆盖层图元预算" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.MaxAssetOverlayBudget)), "同时渲染的最大资产投影图元数量上限（400至5000，默认1600）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeletionBatchSize)), "大批量删除批次规模" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeletionBatchSize)), "一次删除过大时，每批交给游戏处理的目标数量上限（8至128，默认128）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeletionBatchBudgetMs)), "大批量删除帧预算" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeletionBatchBudgetMs)), "单批用于准备删除命令的主线程毫秒预算，超出时自动缩小批次（2至33，默认8）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteBuildings)), "公共/其他建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteBuildings)), "允许框选删除市政服务、独特、重点及其他非分区建筑（住宅/商业/工业/办公另有独立开关）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteResidential)), "住宅建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteResidential)), "框选时包含各类住宅建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteCommercial)), "商业建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteCommercial)), "框选时包含各类商业建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteIndustrial)), "工业建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteIndustrial)), "框选时包含工业与仓储建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteOffice)), "办公建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteOffice)), "框选时包含各类办公建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteTrees)), "树木与植被" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteTrees)), "允许框选删除独立种植的树木、灌木与花草。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteProps)), "摆件与道具 (Props)" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteProps)), "允许框选删除独立摆放的静态摆件、长椅、石块与街具等。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteDecals)), "贴花与地面修饰" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteDecals)), "允许框选删除独立的路面贴花（Decals）等轻量装饰对象。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteMoving)), "车辆与市民/动物" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteMoving)), "允许框选删除框内的车辆、市民与动物（可用于清理拥堵，默认关闭）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetworks)), "道路与管网（慎用）" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetworks)), "允许框选删除道路、轨道、人行道、水管与电线（默认关闭）。删除路口可能影响路线。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetRoads)), "管网细分：道路与人行道" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetRoads)), "在开启「道路与管网」时，允许选中道路、公路与人行道。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetTracks)), "管网细分：轨道" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetTracks)), "在开启「道路与管网」时，允许选中火车、有轨电车与地铁轨道。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetPipelines)), "管网细分：管道" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetPipelines)), "在开启「道路与管网」时，允许选中独立供水与排污管道。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetPowerLines)), "管网细分：电力线" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetPowerLines)), "在开启「道路与管网」时，允许选中独立高压与低压电力线。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteNetOther)), "管网细分：其他网络" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteNetOther)), "在开启「道路与管网」时，允许选中航道、围栏及未分类网络。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.DeleteAreas)), "自定义地表与地块" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.DeleteAreas)), "允许框选删除地表铺装（Surface/Space/Lot）；地图边界地块(MapTile)与行政区(District)始终受保护。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoBuildings)), "撤回建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoBuildings)), "撤回时包含建筑（连同其扩建、附属院落与内部通道一起恢复）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoNetworks)), "撤回道路与管网" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoNetworks)), "撤回时包含独立道路、桥梁、隧道、轨道、管道与电力线。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoAreas)), "撤回地表" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoAreas)), "撤回时包含独立涂装地表（Surface/Space/Lot）。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoTrees)), "撤回树木" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoTrees)), "撤回时包含独立树木与灌木植被。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoProps)), "撤回摆件" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoProps)), "撤回时包含独立摆件与街景道具。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoDecals)), "撤回贴花" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoDecals)), "撤回时包含独立路面贴花与地面标线。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ProtectOutsideConnections)), "保护外部连接" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ProtectOutsideConnections)), "始终跳过地图边缘的高速公路、铁路、水电外部连接节点，避免存档损坏。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ProtectUniqueBuildings)), "保护标志性/独特建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ProtectUniqueBuildings)), "开启后将自动跳过标志性建筑与独特建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ResetSafeDefaultsBtn)), "恢复安全默认设置" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ResetSafeDefaultsBtn)), "将所有类别过滤器和保护选项重置为推荐默认值。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.Version)), "模组版本" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.Version)), "适用于《城市：天际线 2》1.6.4f1（已测试）；1.6 系列其他版本以兼容模式加载。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleToolBinding)), "开启/退出批量删除工具" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleActionName), "开启/退出批量删除工具" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleToolBinding)), "开启或退出批量删除工具。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ConfirmDeleteBinding)), "确认删除所选目标" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ConfirmActionName), "确认删除所选目标" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ConfirmDeleteBinding)), "执行删除当前高亮预览中的目标。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.SwitchModeBinding)), "切换批量删除 / 单个拆除" },
                { m_Settings.GetBindingKeyLocaleID(Settings.SwitchModeActionName), "切换批量删除 / 单个拆除" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.SwitchModeBinding)), "在批量删除与官方单个拆除模式之间切换。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.CycleSelectionModeBinding)), "切换框选模式" },
                { m_Settings.GetBindingKeyLocaleID(Settings.CycleSelectionModeActionName), "切换框选模式" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.CycleSelectionModeBinding)), "在矩形、多边形与自由笔画框选模式之间切换。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleUndergroundBinding)), "切换地上 / 地下视图" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleUndergroundActionName), "切换地上 / 地下视图" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleUndergroundBinding)), "在地上视图与地下管网/隧道视图之间切换。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ForceConfirmImportantBinding)), "确认删除（含重要建筑）" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ForceConfirmImportantActionName), "确认删除（含重要建筑）" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ForceConfirmImportantBinding)), "在重要建筑二次确认弹窗中确认删除全部已选目标。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ExcludeImportantBinding)), "取消选择重要建筑" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ExcludeImportantActionName), "取消选择重要建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ExcludeImportantBinding)), "在重要建筑提示弹窗中排除重要建筑及会连带删除它们的目标，保留其余选择。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ClearSelectionBinding)), "清空当前选区 / 关闭弹窗" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ClearSelectionActionName), "清空当前选区 / 关闭弹窗" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ClearSelectionBinding)), "清空当前待确认选区，或在弹窗中取消删除。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleCameraAlignBinding)), "切换视角对齐框选" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleCameraAlignActionName), "切换视角对齐框选" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleCameraAlignBinding)), "开关按当前相机视角旋转框选矩形。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleConfirmModeBinding)), "切换删除前确认" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleConfirmModeActionName), "切换删除前确认" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleConfirmModeBinding)), "开关删除前是否先高亮预览并等待确认。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ToggleProtectOutsideBinding)), "切换保护外部连接" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ToggleProtectOutsideActionName), "切换保护外部连接" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ToggleProtectOutsideBinding)), "开关是否自动跳过地图边缘外部连接节点。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.PickSimilarBinding)), "框内只选同款" },
                { m_Settings.GetBindingKeyLocaleID(Settings.PickSimilarActionName), "框内只选同款" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.PickSimilarBinding)), "在待确认选区状态下开启一次性同款拾取，点击对象后仅保留选区内同款项目。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.RestoreSelectionHistoryBinding)), "恢复上次选区" },
                { m_Settings.GetBindingKeyLocaleID(Settings.RestoreSelectionHistoryActionName), "恢复上次选区" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.RestoreSelectionHistoryBinding)), "从最近选区历史中一键恢复上一次框选边界。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.UndoBinding)), "撤回上次批量删除" },
                { m_Settings.GetBindingKeyLocaleID(Settings.UndoActionName), "撤回上次批量删除" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.UndoBinding)), "开始或继续撤回最近一次批量删除的快照。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.TogglePauseUndoBinding)), "暂停 / 继续撤回" },
                { m_Settings.GetBindingKeyLocaleID(Settings.TogglePauseUndoActionName), "暂停 / 继续撤回" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.TogglePauseUndoBinding)), "暂停正在进行的撤回，或继续已暂停的撤回。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.StopUndoBinding)), "停止撤回" },
                { m_Settings.GetBindingKeyLocaleID(Settings.StopUndoActionName), "停止撤回" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.StopUndoBinding)), "停止当前撤回，保留已提交重建的部分并解除工具锁定。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.SelectAllFiltersBinding)), "全选资产筛选" },
                { m_Settings.GetBindingKeyLocaleID(Settings.SelectAllFiltersActionName), "全选资产筛选" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.SelectAllFiltersBinding)), "启用所有目标资产类别和建筑分区筛选。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ResetDefaultFiltersBinding)), "重置筛选为安全默认" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ResetDefaultFiltersActionName), "重置筛选为安全默认" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ResetDefaultFiltersBinding)), "将目标资产类别筛选重置为推荐安全默认值。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ClearAllFiltersBinding)), "清空所有资产筛选" },
                { m_Settings.GetBindingKeyLocaleID(Settings.ClearAllFiltersActionName), "清空所有资产筛选" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ClearAllFiltersBinding)), "关闭所有目标资产类别和建筑分区筛选。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterBuildingsBinding)), "切换筛选：公共/其他建筑" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterBuildingsActionName), "切换筛选：公共/其他建筑" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterBuildingsBinding)), "开关是否包含公共服务、独特及其他非分区建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterResidentialBinding)), "切换筛选：住宅" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterResidentialActionName), "切换筛选：住宅" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterResidentialBinding)), "开关是否包含住宅建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterCommercialBinding)), "切换筛选：商业" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterCommercialActionName), "切换筛选：商业" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterCommercialBinding)), "开关是否包含商业建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterIndustrialBinding)), "切换筛选：工业" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterIndustrialActionName), "切换筛选：工业" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterIndustrialBinding)), "开关是否包含工业、仓储及开采建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterOfficeBinding)), "切换筛选：办公" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterOfficeActionName), "切换筛选：办公" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterOfficeBinding)), "开关是否包含办公建筑。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterTreesBinding)), "切换筛选：树木" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterTreesActionName), "切换筛选：树木" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterTreesBinding)), "开关是否包含独立树木与植被。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterPropsBinding)), "切换筛选：摆件" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterPropsActionName), "切换筛选：摆件" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterPropsBinding)), "开关是否包含独立摆件与街景道具。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterDecalsBinding)), "切换筛选：贴花" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterDecalsActionName), "切换筛选：贴花" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterDecalsBinding)), "开关是否包含独立路面贴花与标线。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterMovingBinding)), "切换筛选：车辆／市民／动物" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterMovingActionName), "切换筛选：车辆／市民／动物" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterMovingBinding)), "开关是否包含行驶或停放的车辆、市民和动物。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterNetworksBinding)), "切换筛选：道路与管网" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterNetworksActionName), "切换筛选：道路与管网" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterNetworksBinding)), "开关是否包含道路、桥梁、隧道、轨道、管道与电力线。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.FilterAreasBinding)), "切换筛选：地表" },
                { m_Settings.GetBindingKeyLocaleID(Settings.FilterAreasActionName), "切换筛选：地表" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.FilterAreasBinding)), "开关是否包含涂装地表、空间与自定义地块。" },

                { m_Settings.GetOptionLabelLocaleID(nameof(Settings.ResetHotkeysBtn)), "重置所有快捷键" },
                { m_Settings.GetOptionDescLocaleID(nameof(Settings.ResetHotkeysBtn)), "恢复默认快捷键设置（保留 Ctrl+Shift+X、Enter、Ctrl+Z，其余恢复为空）。" },

                // HUD UI strings
                { "MarqueeDelete.Title", "批量删除" },
                { "MarqueeDelete.SectionTitle", "批量删除" },
                { "MarqueeDelete.ModeLabel", "框选" },
                { "MarqueeDelete.ViewModeTitle", "视图" },
                { "MarqueeDelete.SurfaceView", "地上" },
                { "MarqueeDelete.UndergroundView", "地下" },
                { "MarqueeDelete.ModeToggleTooltip", "切换「批量删除 / 官方单个拆除」(Ctrl+Shift+X)" },
                { "MarqueeDelete.ViewToggleTooltip", "切换地上 / 地下视图（分别过滤地上资产或地下管网隧道）" },
                { "MarqueeDelete.TooltipActive", "框选删除已激活（左键拖框/点选添加 | 右键移除单个/退出 | ESC 或「重选」按钮清空）" },
                { "MarqueeDelete.TooltipIdle", "批量删除" },
                { "MarqueeDelete.HintIdle", "左键拖动框选或单击添加单个对象；右键退出工具。" },
                { "MarqueeDelete.HintDragging", "松开左键锁定范围；右键或 ESC 取消当前拖拽。" },
                { "MarqueeDelete.HintPending", "左键单击添加 • 右键仅移除单个（不会清空全部），清空请用 ESC 或「重选」按钮 • Enter/确认删除。" },
                { "MarqueeDelete.FiltersLabel", "资产筛选" },
                { "MarqueeDelete.FilterBuildings", "建筑" },
                { "MarqueeDelete.FilterTrees", "树木" },
                { "MarqueeDelete.FilterProps", "摆件" },
                { "MarqueeDelete.FilterDecals", "贴花" },
                { "MarqueeDelete.FilterMoving", "车辆市民" },
                { "MarqueeDelete.FilterNetworks", "道路路面" },
                { "MarqueeDelete.FilterAreas", "地表" },
                { "MarqueeDelete.SelectAllBtn", "全选" },
                { "MarqueeDelete.ClearAllFiltersBtn", "全不选" },
                { "MarqueeDelete.DescClearAllFilters", "清空全部建筑分区与资产筛选开关，方便只勾选某一类目标进行清理。" },
                { "MarqueeDelete.ResetFiltersBtn", "重置" },
                { "MarqueeDelete.OptionConfirm", "先确认" },
                { "MarqueeDelete.OptionCameraAlign", "随视角" },
                { "MarqueeDelete.OptionProtectOutside", "护边界" },
                { "MarqueeDelete.SelectedLabel", "已框选" },
                { "MarqueeDelete.WithChildrenSuffix", "含附属" },
                { "MarqueeDelete.LargeWarning", "大选区" },
                { "MarqueeDelete.LargeDirectNotice", "大选区（松手即删，不二次确认）" },
                { "MarqueeDelete.HintCompactDragDirect", "松手即删，大选区也不二次确认 • 右键取消" },
                { "MarqueeDelete.HintConfirmOffIdle", "删除确认已关闭：松手即删，大选区也不二次确认" },
                { "MarqueeDelete.DeletionProgress", "正在分批拆除，还剩 {0} 个目标。按 Esc 停止。" },
                { "MarqueeDelete.DeletionChanged", "剩余目标发生变化，本次拆除已停止。已提交的拆除仍会执行，请检查选区后再继续。" },
                { "MarqueeDelete.DeletionStopped", "已停止分批拆除。已提交的静态资产可以撤回。" },
                { "MarqueeDelete.DeletionObserveTimeout", "等待拆除结果超时，已暂停后续提交。已提交的命令仍可能执行，请检查后再继续。" },
                { "MarqueeDelete.DeletionBlockedCompat", "当前游戏构建未验证，拆除不可用。选区已保留，你可以调整选区或放弃本次任务。" },
                { "MarqueeDelete.InterruptedUnitsSummary", "剩余目标涉及 {0} 个删除单元，最大单元原含 {1} 个实体" },
                { "MarqueeDelete.UndoDisabledCompatTitle", "撤回（不可用）" },
                { "MarqueeDelete.UndoDisabledCompatDesc", "当前游戏版本未验证或重建组件缺失，撤回已禁用。" },
                { "MarqueeDelete.CalculatingImpact", "正在计算影响…" },
                { "MarqueeDelete.CompatUnavailableNotice", "当前游戏构建下拆除不可用。" },
                { "MarqueeDelete.HotkeyCancel", "取消/清除" },
                { "MarqueeDelete.HotkeyConfirm", "确认/关闭" },
                { "MarqueeDelete.HotkeySolo", "单类筛选" },
                { "MarqueeDelete.HotkeySwitchTool", "切换模式" },
                { "MarqueeDelete.HotkeyUnderground", "地下视图" },
                { "MarqueeDelete.HotkeyUndo", "撤回" },
                { "MarqueeDelete.ImportantProtectedSummary", "含 {0} 个重要建筑（需额外确认）" },
                { "MarqueeDelete.ModeInfoBtn", "详情" },
                { "MarqueeDelete.OptFollowCamera", "跟随相机方向" },
                { "MarqueeDelete.OptOff", "关" },
                { "MarqueeDelete.OptOn", "开" },
                { "MarqueeDelete.PreviewOnlyNotice", "当前游戏构建处于预览模式 — 待验证写入，拆除已禁用。" },
                { "MarqueeDelete.UndergroundBtn", "地下视图" },
                { "MarqueeDelete.InterruptedSummary", "拆除已暂停，已记录 {0} 个待复核目标。" },
                { "MarqueeDelete.ReviewingRemaining", "正在复核剩余目标。确认后将拆除下方列出的目标及其附属物。" },
                { "MarqueeDelete.ReviewRemainingBtn", "复核剩余目标" },
                { "MarqueeDelete.DiscardRemainingBtn", "放弃剩余目标" },
                { "MarqueeDelete.ResumeUnavailable", "按当前筛选和保护设置，没有可继续拆除的已记录目标。" },
                { "MarqueeDelete.SelectionChanged", "受影响对象已变化，请核对选区后重新确认。" },
                { "MarqueeDelete.SelectionProtected", "此选区会连带删除受保护对象，请调整范围或筛选项。" },
                { "MarqueeDelete.SelectionHiddenRetained", "当前筛选下无匹配项，但保留了 {0} 项手动选择；重新开启筛选可恢复，或按 ESC / 点「重选」清空。" },
                { "MarqueeDelete.SelectionHiddenBox", "当前筛选下选框内无匹配项；调整筛选可恢复，或按 ESC / 点「重选」清空。" },
                { "MarqueeDelete.SelectionRestoreBlocked", "当前正忙、弹窗开启或有正在进行的操作，无法恢复选区。" },
                { "MarqueeDelete.SelectionHistoryEmpty", "历史记录中没有已保存的选区边界。" },
                { "MarqueeDelete.ConfirmBtn", "确认删除" },
                { "MarqueeDelete.ClearBtn", "重选" },
                { "MarqueeDelete.CloseBtn", "单删" },
                { "MarqueeDelete.StatusReadyGround", "地面框选就绪" },
                { "MarqueeDelete.StatusReadyUnderground", "地下框选就绪" },
                { "MarqueeDelete.StatusDragging", "正在框选" },
                { "MarqueeDelete.HintCompactIdle", "左键拖框 · 右键返回" },
                { "MarqueeDelete.HintCompactDrag", "松手锁定 · 右键取消" },
                { "MarqueeDelete.DescModeToggle", "在「批量删除」与「官方单点推土机」之间切换（快捷键 Ctrl+Shift+X）。" },
                { "MarqueeDelete.DescUnderground", "切换地上 / 地下视图，自动隔离地面建筑道路与地下管网隧道。" },
                { "MarqueeDelete.DescCameraAlign", "开启后框选矩形自动跟随当前镜头水平朝向旋转；关闭则按世界南北方向对齐。" },
                { "MarqueeDelete.DescConfirm", "开启时：松开鼠标先高亮锁定目标，按 Enter 或点击确认后再删除。关闭时：松手即删，大选区也不二次确认（重要建筑除外）。" },
                { "MarqueeDelete.DescProtectOutside", "自动跳过地图边缘的高速公路、铁路、航道与水电外部连接点，防止误删断联。" },
                { "MarqueeDelete.DescResetFilters", "将所有资产筛选开关一键恢复为推荐安全默认组合。" },
                { "MarqueeDelete.DescSelectAll", "一键开启全部 7 类资产筛选，或恢复推荐默认筛选。" },
                { "MarqueeDelete.DescBuildings", "包含独立建筑及其附属院落、围栏与子物体。" },
                { "MarqueeDelete.DescNetworks", "包含地面道路、桥梁、隧道、铁轨、人行道、地下水管与高压电线。" },
                { "MarqueeDelete.DescTrees", "包含独立种植的树木、灌木与野生植被。" },
                { "MarqueeDelete.DescProps", "包含独立摆放的长椅、路灯、花坛、石块与街景摆件。" },
                { "MarqueeDelete.DescDecals", "包含独立铺设的路面贴花、污渍与地面标线。" },
                { "MarqueeDelete.DescAreas", "包含自定义涂装地表（Surface/Space/Lot），地图地块与行政区始终受保护。" },
                { "MarqueeDelete.DescMoving", "包含选框内的行驶/停放车辆、市民与动物（可用于快速疏通拥堵）。" },
                { "MarqueeDelete.ZonesLabel", "建筑分区" },
                { "MarqueeDelete.FilterResidential", "住宅" },
                { "MarqueeDelete.FilterCommercial", "商业" },
                { "MarqueeDelete.FilterIndustrial", "工业" },
                { "MarqueeDelete.FilterOffice", "办公" },
                { "MarqueeDelete.FilterOtherBuildings", "公共/其他" },
                { "MarqueeDelete.DescResidential", "包含住宅区建筑（框选时以绿色高亮显示）。" },
                { "MarqueeDelete.DescCommercial", "包含商业区建筑（框选时以蓝色高亮显示）。" },
                { "MarqueeDelete.DescIndustrial", "包含工业、仓储与开采建筑（框选时以黄色高亮显示）。" },
                { "MarqueeDelete.DescOffice", "包含办公区建筑（框选时以紫色高亮显示）。" },
                { "MarqueeDelete.UnitCells", "格" },
                { "MarqueeDelete.SizeLabel", "尺寸" },
                { "MarqueeDelete.ImportantTag", "重要建筑" },
                { "MarqueeDelete.ImportantService", "服务建筑" },
                { "MarqueeDelete.ImportantUnique", "独特建筑" },
                { "MarqueeDelete.ImportantSignature", "重点建筑" },
                { "MarqueeDelete.ModalTitle", "检测到重要建筑" },
                { "MarqueeDelete.ModalSubtitle", "当前框选区域包含以下重要建筑（服务 / 独特 / 重点建筑），确认后才可执行删除：" },
                { "MarqueeDelete.ModalConfirmBtn", "确认强制删除" },
                { "MarqueeDelete.ModalExcludeBtn", "排除重要建筑" },
                { "MarqueeDelete.ModalCancelBtn", "取消并保留" },
                { "MarqueeDelete.ImportantConfirmNote", "（需二次确认）" },
                { "MarqueeDelete.MoreImportantItems", "个重要建筑未全部列出" },
                { "MarqueeDelete.SelectionExcludedImportant", "已排除 {0} 座重要建筑，请核对剩余 {1} 项后再确认删除。" },
                { "MarqueeDelete.SelectionExcludedAllImportant", "已排除全部 {0} 座重要建筑，当前选区已无剩余可删对象。" },
                { "MarqueeDelete.WarnNoFilters", "当前未启用任何筛选开关，框选将不会选中任何目标。" },
                { "MarqueeDelete.WarnUndergroundNoFilter", "当前处于地下视图：请开启「道路路面」或「车辆市民」以选中地下管网或隧道。" },
                { "MarqueeDelete.UndoGeometryOnly", "撤回仅重建几何与部分属性，不恢复住户或模拟状态。" },
                { "MarqueeDelete.UndoPartial", "此次删除仅记录了一部分（每份快照最多 8,000 条记录）。" },
                { "MarqueeDelete.UndoPaused", "撤回已暂停，完成前无法继续删除；请点击继续或撤回按钮以恢复推进。" },
                { "MarqueeDelete.UndoWaitingOwner", "正在等待所属建筑就绪…" },
                { "MarqueeDelete.UndoError", "撤回异常已停止，剩余对象未重建，可读档恢复。" },
                { "MarqueeDelete.UndoStalled", "创建请求已提交，但部分附属对象因等待主建筑超时被跳过且未重建，可读档恢复；其余已提交项是否重建由模拟决定，未作校验。" },
                { "MarqueeDelete.UndoSubmitted", "创建请求已全部提交，游戏是否真正重建由模拟决定，本工具未作校验。" },
                { "MarqueeDelete.UndoSubmittedPartial", "创建请求已全部提交，但存在未记录或被跳过的对象；游戏是否真正重建由模拟决定，本工具未作校验。" },
                { "MarqueeDelete.UndoStopped", "撤回已停止，其余对象未提交重建。" },
                { "MarqueeDelete.UndoNothingCaptured", "本次删除没有可撤回记录（{0} 个受支持对象均捕获失败，未保存新快照）。" },
                { "MarqueeDelete.UndoNotCapturedHint", "当前所选撤回类别中，有 {0} 个对象未纳入本次快照。" },
                { "MarqueeDelete.UndoBtn", "撤回上步" },
                { "MarqueeDelete.UndoPauseBtn", "暂停" },
                { "MarqueeDelete.UndoStopBtn", "停止" },
                { "MarqueeDelete.UndoResumeBtn", "继续" },
                { "MarqueeDelete.UndoDismissBtn", "关闭" },
                { "MarqueeDelete.UndoRemainingLabel", "剩余请求" },
                { "MarqueeDelete.UndoRemainingNotRebuiltLabel", "剩余未重建" },
                { "MarqueeDelete.UndoTooltip", "分步撤回（共 {0} 项）" },
                { "MarqueeDelete.UndoProgressLabel", "已提交重建请求" },
                { "MarqueeDelete.UndoSkippedLabel", "已跳过" },
                { "MarqueeDelete.UndoNotCapturedLabel", "未捕获" },
                { "MarqueeDelete.UndoFinishedSummary", "已提交 {0} / {1}" },
                { "MarqueeDelete.UndoScopeLabel", "撤回范围" },
                { "MarqueeDelete.UndoBuildingsCountNote", "建筑数字为重建记录总数（含扩建、附属院落与内部通道），非单纯建筑栋数。" },
                { "MarqueeDelete.UndoZeroMatchHint", "栈顶快照中没有匹配当前所选撤回类别的记录（队列剩余 {0} 份）。" },
                { "MarqueeDelete.UndoLockedScopeHint", "撤回进行中或暂停期间，当前任务范围已锁定。" },
                { "MarqueeDelete.UndoDescBuildings", "恢复建筑及其扩建、附属院落与内部通道。角标显示的是建筑关联记录总数，非单纯栋数。" },
                { "MarqueeDelete.UndoDescNetworks", "恢复独立道路、桥梁、隧道、轨道、管道与电力线。" },
                { "MarqueeDelete.UndoDescAreas", "恢复独立涂装地表（Surface/Space/Lot）。" },
                { "MarqueeDelete.UndoDescTrees", "恢复独立树木与灌木植被。" },
                { "MarqueeDelete.UndoDescProps", "恢复独立摆件与街景道具。" },
                { "MarqueeDelete.UndoDescDecals", "恢复独立路面贴花与地面标线。" },
                { "MarqueeDelete.UndoNothingTooltip", "暂无可撤回的框选删除记录。" },
                { "MarqueeDelete.DescUndo", "分批恢复最近一份删除快照中已选类型的资产，降低瞬时性能开销（快捷键 Ctrl+Z）。" },
                { "MarqueeDelete.UndoScopeConsumedHint", "撤回会整份消费该快照：未勾选的类别将被放弃，之后无法再从这一步恢复。" },
                { "MarqueeDelete.SelectionModeLabel", "选区模式" },
                { "MarqueeDelete.SelectionModeRect", "矩形" },
                { "MarqueeDelete.SelectionModePolygon", "多边形" },
                { "MarqueeDelete.SelectionModeFreehand", "笔画" },
                { "MarqueeDelete.DescSelectionModeRect", "矩形选区：按住拖动框选，或单击快速选中单个单元格。" },
                { "MarqueeDelete.DescSelectionModePolygon", "多边形选区：左键逐点添加顶点，点击起点或按 Enter 闭合（最多 64 个顶点）。" },
                { "MarqueeDelete.DescSelectionModeFreehand", "自由笔画选区：按住左键绘制轨迹，松开自动闭合。" },
                { "MarqueeDelete.StatusPolygonDraft", "点选中" },
                { "MarqueeDelete.StatusFreehandDrawing", "绘制中" },
                { "MarqueeDelete.HintCompactPolygonIdle", "左键加点 · 点起点/Enter 闭合 · 右键/Backspace 撤销点" },
                { "MarqueeDelete.HintCompactFreehandIdle", "按住左键拖拽画线 · 松开自动闭合" },
                { "MarqueeDelete.HintCompactFreehandDraw", "继续拖动勾勒区域 · 松开闭合 · Esc 取消" },
                { "MarqueeDelete.PolygonVerticesProgress", "顶点数：{0} / {1}" },
                { "MarqueeDelete.PolygonSnapReady", "已吸附起点 — 点击或按 Enter 闭合多边形" },
                { "MarqueeDelete.HintPolygonNeedMore", "还需至少 {0} 个顶点才可闭合" },
                { "MarqueeDelete.HintPolygonCloseReady", "点击起点或按 Enter 闭合 · 右键/Backspace 撤销上一点" },
                { "MarqueeDelete.PolygonVertexCapWarn", "多边形顶点已达上限（64 点）— 请闭合选区或撤销顶点。" },
                { "MarqueeDelete.ShapeInvalidWarn", "选区面积过小（< 1 ㎡）或形状无效，已自动舍弃。" },
                { "MarqueeDelete.ShapeSimplifiedNotice", "已简化：原始 {0} 点 → 当前 {1} 点" },
                { "MarqueeDelete.UnitSqMeters", "㎡" },
                { "MarqueeDelete.UnitVertices", "点" },
                { "MarqueeDelete.EdgeSimplifiedBadge", "已简化" },

                { "MarqueeDelete.PresetLabel", "筛选预设" },
                { "MarqueeDelete.PresetGreenery", "只清绿化" },
                { "MarqueeDelete.PresetBuildings", "只拆建筑" },
                { "MarqueeDelete.PresetSurfaces", "只清地表" },
                { "MarqueeDelete.PresetUndergroundUtilities", "只拆地下管网" },
                { "MarqueeDelete.DescPresetGreenery", "仅保留树木与植被筛选。" },
                { "MarqueeDelete.DescPresetBuildings", "仅保留住宅、商业、工业、办公与公共/其他建筑筛选。" },
                { "MarqueeDelete.DescPresetSurfaces", "仅保留涂装地表、空间与自定义地块筛选。" },
                { "MarqueeDelete.DescPresetUndergroundUtilities", "仅保留管道与电力线并自动切至地下视图（切走预设时若未手动改过视图则自动还原）。" },
                { "MarqueeDelete.CustomPresetSlot", "自定义 {0}" },
                { "MarqueeDelete.CustomPresetSaveBtn", "保存" },
                { "MarqueeDelete.CustomPresetClearBtn", "清" },
                { "MarqueeDelete.CustomPresetEmpty", "空" },
                { "MarqueeDelete.CustomPresetApplyHint", "点击应用自定义筛选槽位 {0}。" },
                { "MarqueeDelete.CustomPresetSaveHint", "将当前资产与管网细分筛选保存到槽位 {0}。" },
                { "MarqueeDelete.NetSubExpandBtn", "管网细分" },
                { "MarqueeDelete.NetSubRoads", "道路" },
                { "MarqueeDelete.NetSubTracks", "轨道" },
                { "MarqueeDelete.NetSubPipelines", "管道" },
                { "MarqueeDelete.NetSubPowerLines", "电力线" },
                { "MarqueeDelete.NetSubOther", "其他网络" },
                { "MarqueeDelete.DescNetSubRoads", "在「道路与管网」开启时开关道路与人行道。" },
                { "MarqueeDelete.DescNetSubTracks", "在「道路与管网」开启时开关火车、电车与地铁轨道。" },
                { "MarqueeDelete.DescNetSubPipelines", "在「道路与管网」开启时开关独立供水与排污管道。" },
                { "MarqueeDelete.DescNetSubPowerLines", "在「道路与管网」开启时开关独立电力线。" },
                { "MarqueeDelete.DescNetSubOther", "在「道路与管网」开启时开关航道、围栏及未分类网络。" },
                { "MarqueeDelete.NetSubMasterOffHint", "需开启上方「道路与管网」总开关，下列细分才会生效。" },
                { "MarqueeDelete.PickSimilarBtn", "只选同款" },
                { "MarqueeDelete.DescPickSimilarBtn", "一次性拾取：开启后左键点击目标，仅保留当前选区内的同款同类别对象。" },
                { "MarqueeDelete.PickSimilarArmedHint", "同款拾取已开启 — 左键点击任意对象即可仅保留同款（按 ESC 或右键取消）。" },
                { "MarqueeDelete.PickSimilarBlockedPolygon", "多边形草稿绘制中暂不可用「只选同款」，请先闭合或取消多边形草稿。" },
                { "MarqueeDelete.PickSimilarMatched", "已过滤，当前选区仅保留 {0} 项同款目标。" },
                { "MarqueeDelete.PickSimilarNoTarget", "未命中有效实体 — 已退出同款拾取。" },
                { "MarqueeDelete.PickSimilarNoPrefab", "点击目标没有可用的 Prefab 标识，已保持原选区不变。" },
                { "MarqueeDelete.PickSimilarNoMatch", "当前选区内没有与该对象同款的目标，已保持原选区不变。" },

                { "MarqueeDelete.ColorBtn", "配色" },
                { "MarqueeDelete.ColorTooltip", "自定义框选与高亮颜色" },
                { "MarqueeDelete.ColorModalTitle", "颜色库与高亮配色" },
                { "MarqueeDelete.ColorModalSubtitle", "地面高亮实时联动预览，修改即时生效。" },
                { "MarqueeDelete.ColorPresetTitle", "预设主题" },
                { "MarqueeDelete.ColorPresetClassic", "经典红绿" },
                { "MarqueeDelete.ColorPresetNeon", "高对比霓虹" },
                { "MarqueeDelete.ColorPresetColorblind", "色盲友好" },
                { "MarqueeDelete.ColorPresetPastel", "柔和半透明" },
                { "MarqueeDelete.ColorItemsTitle", "框选与高亮颜色（18 项）" },
                { "MarqueeDelete.ColorGroupMarquee", "框选矩形" },
                { "MarqueeDelete.ColorGroupBuildings", "建筑地块" },
                { "MarqueeDelete.ColorGroupWarning", "超阈值脉冲" },
                { "MarqueeDelete.ColorGroupAssets", "非建筑资产" },
                { "MarqueeDelete.ColorGroupOutline", "深色描边" },
                { "MarqueeDelete.ColorKey_marqueeGroundBorder", "地面边框" },
                { "MarqueeDelete.ColorKey_marqueeGroundFill", "地面填充" },
                { "MarqueeDelete.ColorKey_marqueeUndergroundBorder", "地下边框" },
                { "MarqueeDelete.ColorKey_marqueeUndergroundFill", "地下填充" },
                { "MarqueeDelete.ColorKey_buildingResidential", "住宅" },
                { "MarqueeDelete.ColorKey_buildingCommercial", "商业" },
                { "MarqueeDelete.ColorKey_buildingIndustrial", "工业" },
                { "MarqueeDelete.ColorKey_buildingOffice", "办公" },
                { "MarqueeDelete.ColorKey_buildingService", "公共与服务" },
                { "MarqueeDelete.ColorKey_warningPulseStart", "警告起始色" },
                { "MarqueeDelete.ColorKey_warningPulseEnd", "警告峰值色" },
                { "MarqueeDelete.ColorKey_assetNetwork", "道路与管网" },
                { "MarqueeDelete.ColorKey_assetTree", "树木植被" },
                { "MarqueeDelete.ColorKey_assetProp", "街景摆件" },
                { "MarqueeDelete.ColorKey_assetDecal", "路面贴花" },
                { "MarqueeDelete.ColorKey_assetArea", "地表区域" },
                { "MarqueeDelete.ColorKey_assetMoving", "移动实体" },
                { "MarqueeDelete.ColorKey_darkOutline", "描边基准色" },
                { "MarqueeDelete.ColorSwatchesLabel", "常用色块" },
                { "MarqueeDelete.ColorRgbEditorLabel", "自定义 RGB" },
                { "MarqueeDelete.ColorAlphaTitle", "透明度" },
                { "MarqueeDelete.ColorAlphaMarqueeFill", "框选底色透明度" },
                { "MarqueeDelete.ColorAlphaBuildingFill", "建筑填充透明度" },
                { "MarqueeDelete.ColorAlphaOutline", "描边不透明度" },
                { "MarqueeDelete.ColorOutlineHiddenTag", "0% = 隐藏" },
                { "MarqueeDelete.ColorResetBtn", "恢复默认" },
                { "MarqueeDelete.ColorCloseBtn", "关闭" },
                { "MarqueeDelete.ColorEscHint", "按 ESC 关闭配色窗口" },

                { "MarqueeDelete.RestoreHistoryBtn", "恢复选区" },
                { "MarqueeDelete.DescRestoreHistory", "从历史记录中一键恢复最近的框选边界（最多保存5次）。" },
                { "MarqueeDelete.ImpactReportBtn", "影响明细" },
                { "MarqueeDelete.DescImpactReport", "删除前查看按具体预制件/资产分组的影响明细清单。" },
                { "MarqueeDelete.ImpactReportTitle", "拆除影响明细" },
                { "MarqueeDelete.ImpactReportMore", "另有 {0} 种资产" },
                { "MarqueeDelete.SoloCategoryHint", "Alt+点击：仅保留此类别" },
                { "MarqueeDelete.CloseImpactReportBtn", "关闭" },
                { "MarqueeDelete.TabTargets", "目标" },
                { "MarqueeDelete.TabOptions", "选项" },
            };
        }

        public void Unload()
        {
        }
    }
}
