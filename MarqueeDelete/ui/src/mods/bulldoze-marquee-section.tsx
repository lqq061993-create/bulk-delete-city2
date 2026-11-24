import React from "react";
import { useValue } from "cs2/api";
import { tool } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { Button, Tooltip } from "cs2/ui";
import {
    active$,
    applyFilterPreset,
    canUse$,
    clearAllFilters,
    clearCustomPreset,
    compatCanSubmit$,
    countAreas$,
    countBuildings$,
    countCommercial$,
    countDecals$,
    countIndustrial$,
    countMoving$,
    countNetworks$,
    countOffice$,
    countProps$,
    countResidential$,
    countTrees$,
    importantModalOpen$,
    optPanelGlow$,
    roadFacilitiesBusy$,
    roadFacilityMode$,
    saveCustomPreset,
    setAllFilters,
    setOption,
    soloFilter,
    toggleRoadFacilityMode,
    undoCountAreas$,
    undoCountBuildings$,
    undoCountDecals$,
    undoCountNetworks$,
    undoCountProps$,
    undoCountTrees$,
    useColorModalOpen,
    useColorPalette,
    useMarqueeToolbarState,
} from "./bindings";
import { ColorPaletteModal } from "./color-palette-modal";
import { ImportantConfirmModal } from "./important-confirm-modal";
import { MarqueeFooter } from "./marquee-footer";
import { MarqueeHeader, PanelTabId } from "./marquee-header";
import {
    AssetFilterSpec,
    BorderDimensionLabels,
    CAMERA_ICON,
    CHEVRON_DOWN_ICON,
    CHEVRON_UP_ICON,
    CLEAR_FILTERS_ICON,
    COLOR_PALETTE_ICON,
    CONFIRM_ICON,
    FilterCountBadge,
    GLOW_ICON,
    hslToRgbaCss,
    ICON_AREAS,
    ICON_BUILDINGS,
    ICON_COMMERCIAL,
    ICON_DECALS,
    ICON_INDUSTRIAL,
    ICON_MOVING,
    ICON_NETWORKS,
    ICON_OFFICE,
    ICON_PROPS,
    ICON_RESIDENTIAL,
    ICON_SERVICES,
    ICON_TREES,
    INFO_ICON,
    LOCK_ICON,
    MARQUEE_ICON,
    ModeToolButton,
    renderRichTooltip,
    RESET_ICON,
    roadFacilityLabel,
    SectionWrapper,
    SKIP_REVIEW_ICON,
    STAR_ALL_ICON,
    UNDO_FX_ICON,
    UNDO_ICON,
    ZoneFilterSpec,
} from "./marquee-shared";
import { vanillaRefs } from "./vanilla-refs";
import styles from "./bulldoze-marquee-section.module.scss";

export const ALL_PRELOAD_ICONS: string[] = [
    MARQUEE_ICON,
    CLEAR_FILTERS_ICON,
    COLOR_PALETTE_ICON,
    UNDO_ICON,
    UNDO_FX_ICON,
    INFO_ICON,
    GLOW_ICON,
    CHEVRON_DOWN_ICON,
    CHEVRON_UP_ICON,
    SKIP_REVIEW_ICON,
    CAMERA_ICON,
    CONFIRM_ICON,
    LOCK_ICON,
    RESET_ICON,
    STAR_ALL_ICON,
    ICON_RESIDENTIAL,
    ICON_COMMERCIAL,
    ICON_INDUSTRIAL,
    ICON_OFFICE,
    ICON_SERVICES,
    "Media/Game/Icons/ZoneSignature.svg",
    ICON_BUILDINGS,
    ICON_NETWORKS,
    ICON_TREES,
    ICON_PROPS,
    ICON_DECALS,
    ICON_AREAS,
    ICON_MOVING,
];

export function BulldozeMarqueeSection(): JSX.Element {
    const state = useMarqueeToolbarState();
    const palette = useColorPalette();
    const colorModalOpen = useColorModalOpen();
    const importantModalOpen = useValue(importantModalOpen$);
    const roadFacilityMode = useValue(roadFacilityMode$);
    const roadFacilitiesBusy = useValue(roadFacilitiesBusy$);
    const editingRoadFacilities = roadFacilityMode !== 0;
    const compatCanSubmit = useValue(compatCanSubmit$);
    const canChooseRoadFacilities = state.active && !state.underground && compatCanSubmit;
    const { translate } = useLocalization();
    const [netSubOpen, setNetSubOpen] = React.useState(false);
    const [modeInfoOpen, setModeInfoOpen] = React.useState(false);
    const [activeTab, setActiveTab] = React.useState<PanelTabId>("targets");

    const handleCategoryClick = (key: string, enabled: boolean, e?: any) => {
        if (editingRoadFacilities) return;
        const isAlt =
            (e && (e.altKey || e.nativeEvent?.altKey)) ||
            (typeof window !== "undefined" && (window.event as any)?.altKey);
        if (isAlt) {
            soloFilter(key);
        } else {
            setOption(key, !enabled);
        }
    };

    const t = (id: string, fallback: string): string => translate(id, fallback) ?? fallback;
    const isZh = t("MarqueeDelete.UndoBtn", "Undo") !== "Undo";

    const allEnabled =
        state.filters.residential &&
        state.filters.commercial &&
        state.filters.industrial &&
        state.filters.office &&
        state.filters.buildings &&
        state.filters.networks &&
        state.filters.trees &&
        state.filters.props &&
        state.filters.decals &&
        state.filters.areas &&
        state.filters.moving;

    const noneEnabled =
        !state.filters.residential &&
        !state.filters.commercial &&
        !state.filters.industrial &&
        !state.filters.office &&
        !state.filters.buildings &&
        !state.filters.networks &&
        !state.filters.trees &&
        !state.filters.props &&
        !state.filters.decals &&
        !state.filters.areas &&
        !state.filters.moving;

    const hasAnyFilter = editingRoadFacilities || !noneEnabled;
    const hasUndergroundFilter = state.filters.networks || state.filters.moving;
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const allNetSubEnabled =
        state.filters.netRoads &&
        state.filters.netTracks &&
        state.filters.netPipelines &&
        state.filters.netPowerLines &&
        state.filters.netOther;

    const builtInPresetSpecs = [
        {
            id: "greenery",
            labelId: "MarqueeDelete.PresetGreenery",
            fallback: "Greenery",
            descId: "MarqueeDelete.DescPresetGreenery",
            descFallback: "Select only trees and greenery.",
        },
        {
            id: "buildings",
            labelId: "MarqueeDelete.PresetBuildings",
            fallback: "Buildings",
            descId: "MarqueeDelete.DescPresetBuildings",
            descFallback: "Select all zoned and civic/unique buildings.",
        },
        {
            id: "surfaces",
            labelId: "MarqueeDelete.PresetSurfaces",
            fallback: "Surfaces",
            descId: "MarqueeDelete.DescPresetSurfaces",
            descFallback: "Target painted surfaces, spaces, and custom lots only.",
        },
        {
            id: "underground_utilities",
            labelId: "MarqueeDelete.PresetUndergroundUtilities",
            fallback: "Underground Utils",
            descId: "MarqueeDelete.DescPresetUndergroundUtilities",
            descFallback: "Select underground pipes and power lines and switch to underground view.",
        },
    ];

    const customPresetSlots = [
        { slot: 1, presetId: "custom1", saved: state.customPresets.slot1 },
        { slot: 2, presetId: "custom2", saved: state.customPresets.slot2 },
        { slot: 3, presetId: "custom3", saved: state.customPresets.slot3 },
    ];

    const netSubSpecs = [
        { key: "netRoads", labelId: "MarqueeDelete.NetSubRoads", fallback: "Roads", descId: "MarqueeDelete.DescNetSubRoads", descFallback: "Include roads and pedestrian pathways.", enabled: state.filters.netRoads },
        { key: "netTracks", labelId: "MarqueeDelete.NetSubTracks", fallback: "Tracks", descId: "MarqueeDelete.DescNetSubTracks", descFallback: "Include rail, tram, and subway tracks.", enabled: state.filters.netTracks },
        { key: "netPipelines", labelId: "MarqueeDelete.NetSubPipelines", fallback: "Pipes", descId: "MarqueeDelete.DescNetSubPipelines", descFallback: "Include water and sewage pipelines.", enabled: state.filters.netPipelines },
        { key: "netPowerLines", labelId: "MarqueeDelete.NetSubPowerLines", fallback: "Power", descId: "MarqueeDelete.DescNetSubPowerLines", descFallback: "Include overhead and underground power lines.", enabled: state.filters.netPowerLines },
        { key: "netOther", labelId: "MarqueeDelete.NetSubOther", fallback: "Other Networks", descId: "MarqueeDelete.DescNetSubOther", descFallback: "Toggle waterways, fences, and unclassified networks inside the Networks filter.", enabled: state.filters.netOther },
    ];

    const zoneSpecs: ZoneFilterSpec[] = [
        { key: "residential", labelId: "MarqueeDelete.FilterResidential", fallback: "Residential", descId: "MarqueeDelete.DescResidential", descFallback: "Include residential buildings (highlighted in green).", icon: ICON_RESIDENTIAL, colorBarClass: styles.zoneBarRes, colorHex: palette.buildingResidential, enabled: state.filters.residential, countBinding: countResidential$ },
        { key: "commercial", labelId: "MarqueeDelete.FilterCommercial", fallback: "Commercial", descId: "MarqueeDelete.DescCommercial", descFallback: "Include commercial buildings (highlighted in blue).", icon: ICON_COMMERCIAL, colorBarClass: styles.zoneBarCom, colorHex: palette.buildingCommercial, enabled: state.filters.commercial, countBinding: countCommercial$ },
        { key: "industrial", labelId: "MarqueeDelete.FilterIndustrial", fallback: "Industrial", descId: "MarqueeDelete.DescIndustrial", descFallback: "Include industrial, warehouse, and extractor buildings (highlighted in yellow).", icon: ICON_INDUSTRIAL, colorBarClass: styles.zoneBarInd, colorHex: palette.buildingIndustrial, enabled: state.filters.industrial, countBinding: countIndustrial$ },
        { key: "office", labelId: "MarqueeDelete.FilterOffice", fallback: "Office", descId: "MarqueeDelete.DescOffice", descFallback: "Include office buildings (highlighted in purple).", icon: ICON_OFFICE, colorBarClass: styles.zoneBarOff, colorHex: palette.buildingOffice, enabled: state.filters.office, countBinding: countOffice$ },
        { key: "buildings", labelId: "MarqueeDelete.FilterOtherBuildings", fallback: "Service/Other", descId: "MarqueeDelete.DescBuildings", descFallback: "Include standalone service, unique, signature, and other non-zoned buildings.", icon: ICON_SERVICES, colorBarClass: styles.zoneBarOther, colorHex: palette.buildingService, enabled: state.filters.buildings, countBinding: countBuildings$ },
    ];

    const assetSpecs: AssetFilterSpec[] = [
        { key: "networks", labelId: "MarqueeDelete.FilterNetworks", fallback: "Roads/Nets", descId: "MarqueeDelete.DescNetworks", descFallback: "Include roads, bridges, tunnels, tracks, pathways, pipes, and powerlines.", icon: ICON_NETWORKS, enabled: state.filters.networks, countBinding: countNetworks$ },
        { key: "trees", labelId: "MarqueeDelete.FilterTrees", fallback: "Trees", descId: "MarqueeDelete.DescTrees", descFallback: "Include standalone trees, bushes, and wild vegetation.", icon: ICON_TREES, enabled: state.filters.trees, countBinding: countTrees$ },
        { key: "props", labelId: "MarqueeDelete.FilterProps", fallback: "Props", descId: "MarqueeDelete.DescProps", descFallback: "Include standalone props, street furniture, benches, lamps, and boulders.", icon: ICON_PROPS, enabled: state.filters.props, countBinding: countProps$ },
        { key: "decals", labelId: "MarqueeDelete.FilterDecals", fallback: "Decals", descId: "MarqueeDelete.DescDecals", descFallback: "Include standalone ground decals, stains, and surface markings.", icon: ICON_DECALS, enabled: state.filters.decals, countBinding: countDecals$ },
        { key: "areas", labelId: "MarqueeDelete.FilterAreas", fallback: "Surfaces", descId: "MarqueeDelete.DescAreas", descFallback: "Include painted surfaces, spaces, and custom lots (map tiles & districts are always safe).", icon: ICON_AREAS, enabled: state.filters.areas, countBinding: countAreas$ },
        { key: "moving", labelId: "MarqueeDelete.FilterMoving", fallback: "Vehicles", descId: "MarqueeDelete.DescMoving", descFallback: "Include moving or parked vehicles, citizens, and animals inside the box.", icon: ICON_MOVING, enabled: state.filters.moving, countBinding: countMoving$ },
    ];

    const undoScopeSpecs: {
        key: "undoBuildings" | "undoNetworks" | "undoAreas" | "undoTrees" | "undoProps" | "undoDecals";
        labelId: string;
        fallback: string;
        descId: string;
        descFallback: string;
        icon: string;
        enabled: boolean;
        countBinding: typeof countBuildings$;
    }[] = [
        { key: "undoBuildings", labelId: "MarqueeDelete.FilterBuildings", fallback: "Buildings", descId: "MarqueeDelete.UndoDescBuildings", descFallback: "Restore buildings together with their upgrades, attached areas, and internal networks. Badge shows total building-related records, not building count alone.", icon: ICON_BUILDINGS, enabled: state.undo.include.buildings, countBinding: undoCountBuildings$ },
        { key: "undoNetworks", labelId: "MarqueeDelete.FilterNetworks", fallback: "Roads/Nets", descId: "MarqueeDelete.UndoDescNetworks", descFallback: "Restore standalone roads, bridges, tunnels, tracks, pipes, and powerlines.", icon: ICON_NETWORKS, enabled: state.undo.include.networks, countBinding: undoCountNetworks$ },
        { key: "undoAreas", labelId: "MarqueeDelete.FilterAreas", fallback: "Surfaces", descId: "MarqueeDelete.UndoDescAreas", descFallback: "Restore standalone painted surfaces, spaces, and custom lots.", icon: ICON_AREAS, enabled: state.undo.include.areas, countBinding: undoCountAreas$ },
        { key: "undoTrees", labelId: "MarqueeDelete.FilterTrees", fallback: "Trees", descId: "MarqueeDelete.UndoDescTrees", descFallback: "Restore standalone trees and plants.", icon: ICON_TREES, enabled: state.undo.include.trees, countBinding: undoCountTrees$ },
        { key: "undoProps", labelId: "MarqueeDelete.FilterProps", fallback: "Props", descId: "MarqueeDelete.UndoDescProps", descFallback: "Restore standalone props and street furniture.", icon: ICON_PROPS, enabled: state.undo.include.props, countBinding: undoCountProps$ },
        { key: "undoDecals", labelId: "MarqueeDelete.FilterDecals", fallback: "Decals", descId: "MarqueeDelete.UndoDescDecals", descFallback: "Restore standalone decals and surface markings.", icon: ICON_DECALS, enabled: state.undo.include.decals, countBinding: undoCountDecals$ },
    ];

    const undoScopeLocked = state.undo.busy || state.undo.inProgress;
    const showUndoScope = state.undo.historyDepth > 0 || undoScopeLocked;

    const netTypesAccessory = (
        <Tooltip
            tooltip={renderRichTooltip(
                t("MarqueeDelete.NetSubExpandBtn", "Net Types"),
                t("MarqueeDelete.DescNetworks", "Include roads, railways, and utility networks.")
            )}
            delayTime={0}
        >
            <Button
                variant="flat"
                focusKey={focusDisabledKey}
                className={`${styles.netSubToggleBtn} ${!allNetSubEnabled ? styles.netSubToggleFiltered : ""}`}
                onSelect={() => setNetSubOpen((open) => !open)}
            >
                <span>{t("MarqueeDelete.NetSubExpandBtn", "Net Types")}</span>
                <img src={netSubOpen ? CHEVRON_UP_ICON : CHEVRON_DOWN_ICON} className={styles.inlineBtnIconSm} alt="" />
            </Button>
        </Tooltip>
    );

    const content = (
        <div className={styles.panelBody} onWheel={(event) => event.stopPropagation()}>
            {modeInfoOpen && (
                <div className={styles.modeInfoPanel}>
                    <div className={styles.modeInfoDesc}>
                        {state.selectionMode === 0 && t("MarqueeDelete.DescSelectionModeRect", "Rectangle selection: drag to box-select, or click once to quick-pick a cell.")}
                        {state.selectionMode === 1 && t("MarqueeDelete.DescSelectionModePolygon", "Polygon selection: click to add vertices, click start or press Enter to close (max 64).")}
                        {state.selectionMode === 2 && t("MarqueeDelete.DescSelectionModeFreehand", "Freehand stroke selection: hold left mouse button to draw, release to auto-close.")}
                    </div>
                    <div className={styles.modeInfoHotkeys}>
                        <span className={styles.modeInfoHotkeyTag}>Ctrl+Shift+X {t("MarqueeDelete.HotkeySwitchTool", "Switch Mode")}</span>
                        <span className={styles.modeInfoHotkeyTag}>Shift+U {t("MarqueeDelete.HotkeyUnderground", "Underground")}</span>
                        <span className={styles.modeInfoHotkeyTag}>Enter {t("MarqueeDelete.HotkeyConfirm", "Confirm/Close")}</span>
                        <span className={styles.modeInfoHotkeyTag}>Esc {t("MarqueeDelete.HotkeyCancel", "Cancel/Clear")}</span>
                        <span className={styles.modeInfoHotkeyTag}>Ctrl+Z {t("MarqueeDelete.HotkeyUndo", "Undo")}</span>
                        <span className={styles.modeInfoHotkeyTag}>Alt+Click {t("MarqueeDelete.HotkeySolo", "Solo Filter")}</span>
                    </div>
                    <div className={styles.modeInfoOptionRow}>
                        <span>{t("MarqueeDelete.OptFollowCamera", "Follow camera direction")}</span>
                        <Button
                            variant="flat"
                            focusKey={focusDisabledKey}
                            className={styles.secondaryBtn}
                            onSelect={() => setOption("cameraAlign", !state.options.cameraAlign)}
                        >
                            {state.options.cameraAlign ? t("MarqueeDelete.OptOn", "ON") : t("MarqueeDelete.OptOff", "OFF")}
                        </Button>
                    </div>
                </div>
            )}

            {activeTab === "targets" && (
                <React.Fragment>
                    <SectionWrapper title={t("MarqueeDelete.ZonesLabel", "Zones")}>
                        {zoneSpecs.map((spec) => (
                            <ModeToolButton
                                key={spec.key}
                                selected={spec.enabled}
                                src={spec.icon}
                                tooltipTitle={t(spec.labelId, spec.fallback)}
                                tooltipDesc={`${t(spec.descId, spec.descFallback)} (${t("MarqueeDelete.SoloCategoryHint", "Alt+Click: Solo this category")})`}
                                onSelect={(e?: any) => handleCategoryClick(spec.key, spec.enabled, e)}
                            >
                                {spec.enabled && (
                                    <div
                                        className={`${styles.zoneColorBar} ${spec.colorBarClass}`}
                                        style={{ backgroundColor: spec.colorHex }}
                                    />
                                )}
                                {state.active && <FilterCountBadge binding={spec.countBinding} />}
                            </ModeToolButton>
                        ))}
                    </SectionWrapper>

                    <SectionWrapper
                        title={t("MarqueeDelete.FiltersLabel", "Assets")}
                        headerAccessory={netTypesAccessory}
                    >
                        <ModeToolButton
                            selected={allEnabled}
                            disabled={editingRoadFacilities}
                            src={STAR_ALL_ICON}
                            tooltipTitle={t("MarqueeDelete.SelectAllBtn", "All")}
                            tooltipDesc={t("MarqueeDelete.DescSelectAll", "Toggle all target asset categories on or restore defaults.")}
                            onSelect={() => setAllFilters(!allEnabled)}
                        />
                        <ModeToolButton
                            selected={noneEnabled}
                            disabled={editingRoadFacilities}
                            src={CLEAR_FILTERS_ICON}
                            tooltipTitle={t("MarqueeDelete.ClearAllFiltersBtn", "Clear All")}
                            tooltipDesc={t("MarqueeDelete.DescClearAllFilters", "Disable all building zone and asset category filters so you can pick a single category cleanly.")}
                            onSelect={clearAllFilters}
                        />
                        {assetSpecs.map((spec) => (
                            <ModeToolButton
                                key={spec.key}
                                selected={spec.enabled}
                                disabled={editingRoadFacilities}
                                src={spec.icon}
                                tooltipTitle={t(spec.labelId, spec.fallback)}
                                tooltipDesc={`${t(spec.descId, spec.descFallback)} (${t("MarqueeDelete.SoloCategoryHint", "Alt+Click: Solo this category")})`}
                                onSelect={(e?: any) => handleCategoryClick(spec.key, spec.enabled, e)}
                            >
                                {state.active && <FilterCountBadge binding={spec.countBinding} />}
                            </ModeToolButton>
                        ))}
                    </SectionWrapper>

                    {netSubOpen && (
                        <div className={styles.netSubPanel}>
                            <div className={`${styles.netSubRow} ${!state.filters.networks || editingRoadFacilities ? styles.netSubDimmed : ""}`}>
                                {netSubSpecs.map((spec) => (
                                    <Tooltip
                                        key={spec.key}
                                        tooltip={renderRichTooltip(t(spec.labelId, spec.fallback), t(spec.descId, spec.descFallback))}
                                        delayTime={0}
                                    >
                                        <Button
                                            variant="flat"
                                            focusKey={focusDisabledKey}
                                            disabled={editingRoadFacilities}
                                            className={`${styles.netSubChip} ${spec.enabled ? styles.netSubChipActive : ""}`}
                                            onSelect={() => setOption(spec.key, !spec.enabled)}
                                        >
                                            {t(spec.labelId, spec.fallback)}
                                        </Button>
                                    </Tooltip>
                                ))}
                            </div>
                            {!state.filters.networks && !editingRoadFacilities && (
                                <div className={styles.netSubMasterOffHint}>
                                    {t("MarqueeDelete.NetSubMasterOffHint", "Master Networks filter is off — enable Networks above for sub-filters to match.")}
                                </div>
                            )}
                            <div className={`${styles.netSubRow} ${styles.roadFacilityRow}`}>
                                <span className={styles.roadFacilityLabel}>{t("MarqueeDelete.RoadFacilities", "Junction features")}</span>
                                {[1, 2].map((mode) => (
                                    <Tooltip
                                        key={mode}
                                        tooltip={renderRichTooltip(
                                            roadFacilityLabel(mode, t),
                                            mode === 1
                                                ? t("MarqueeDelete.RoadTrafficLightsTip", "Select signalized junctions and remove their traffic lights.")
                                                : t("MarqueeDelete.RoadCrosswalksTip", "Select independently editable pedestrian crossings at ordinary junctions.")
                                        )}
                                        delayTime={0}
                                    >
                                        <Button
                                            variant="flat"
                                            focusKey={focusDisabledKey}
                                            disabled={!canChooseRoadFacilities || state.dragging || state.undo.busy || roadFacilitiesBusy}
                                            className={`${styles.netSubChip} ${roadFacilityMode === mode ? styles.netSubChipActive : ""}`}
                                            onSelect={() => toggleRoadFacilityMode(mode)}
                                        >
                                            {roadFacilityLabel(mode, t)}
                                        </Button>
                                    </Tooltip>
                                ))}
                            </div>
                            {!canChooseRoadFacilities && (
                                <div className={styles.netSubMasterOffHint}>
                                    {t("MarqueeDelete.RoadFacilityAvailability", "Junction features require Bulk Delete in surface view on a verified game build.")}
                                </div>
                            )}
                        </div>
                    )}
                </React.Fragment>
            )}

            {activeTab === "presets" && (
                <SectionWrapper title={t("MarqueeDelete.PresetLabel", "Presets")}>
                    <div className={styles.presetBar}>
                        {builtInPresetSpecs.map((preset) => {
                            const isActive = state.activeFilterPreset === preset.id;
                            return (
                                <Tooltip
                                    key={preset.id}
                                    tooltip={renderRichTooltip(t(preset.labelId, preset.fallback), t(preset.descId, preset.descFallback))}
                                    delayTime={0}
                                >
                                    <Button
                                        variant="flat"
                                        focusKey={focusDisabledKey}
                                        className={`${styles.presetBtn} ${isActive ? styles.presetBtnActive : ""}`}
                                        onSelect={() => applyFilterPreset(preset.id)}
                                    >
                                        {t(preset.labelId, preset.fallback)}
                                    </Button>
                                </Tooltip>
                            );
                        })}
                        {customPresetSlots.map((slotSpec) => {
                            const isActive = state.activeFilterPreset === slotSpec.presetId;
                            const slotLabel = t("MarqueeDelete.CustomPresetSlot", "C{0}").replace("{0}", String(slotSpec.slot));
                            return (
                                <div key={slotSpec.presetId} className={styles.customSlotGroup}>
                                    <Tooltip
                                        tooltip={renderRichTooltip(
                                            slotLabel,
                                            slotSpec.saved
                                                ? t("MarqueeDelete.CustomPresetApplyHint", "Click to apply saved custom preset.")
                                                : t("MarqueeDelete.CustomPresetEmpty", "Empty")
                                        )}
                                        delayTime={0}
                                    >
                                        <Button
                                            variant="flat"
                                            disabled={!slotSpec.saved}
                                            focusKey={focusDisabledKey}
                                            className={`${styles.presetBtn} ${isActive ? styles.presetBtnActive : ""} ${!slotSpec.saved ? styles.presetBtnEmpty : ""}`}
                                            onSelect={() => {
                                                if (slotSpec.saved) {
                                                    applyFilterPreset(slotSpec.presetId);
                                                }
                                            }}
                                        >
                                            {slotLabel}
                                        </Button>
                                    </Tooltip>
                                    <Tooltip
                                        tooltip={renderRichTooltip(
                                            t("MarqueeDelete.CustomPresetSaveBtn", "Save"),
                                            t("MarqueeDelete.CustomPresetSaveHint", "Save current filters into this preset slot.")
                                        )}
                                        delayTime={0}
                                    >
                                        <Button
                                            variant="flat"
                                            focusKey={focusDisabledKey}
                                            className={styles.customSlotActionBtn}
                                            onSelect={() => saveCustomPreset(slotSpec.slot)}
                                        >
                                            +
                                        </Button>
                                    </Tooltip>
                                    {slotSpec.saved && (
                                        <Tooltip
                                            tooltip={renderRichTooltip(
                                                t("MarqueeDelete.CustomPresetClearBtn", "Clear"),
                                                t("MarqueeDelete.CustomPresetClearBtn", "Clear")
                                            )}
                                            delayTime={0}
                                        >
                                            <Button
                                                variant="flat"
                                                focusKey={focusDisabledKey}
                                                className={styles.customSlotClearBtn}
                                                onSelect={() => clearCustomPreset(slotSpec.slot)}
                                            >
                                                ×
                                            </Button>
                                        </Tooltip>
                                    )}
                                </div>
                            );
                        })}
                    </div>
                </SectionWrapper>
            )}

            {activeTab === "options" && (
                <React.Fragment>
                    <SectionWrapper title={t("MarqueeDelete.TabOptions", "Options")}>
                        <ModeToolButton
                            selected={state.options.cameraAlign}
                            src={CAMERA_ICON}
                            tooltipTitle={t("MarqueeDelete.OptionCameraAlign", "Cam Align")}
                            tooltipDesc={t("MarqueeDelete.DescCameraAlign", "Align the selection rectangle with your current camera angle instead of world North-South.")}
                            onSelect={() => setOption("cameraAlign", !state.options.cameraAlign)}
                        />
                        <ModeToolButton
                            selected={state.options.confirm}
                            src={CONFIRM_ICON}
                            tooltipTitle={t("MarqueeDelete.OptionConfirm", "Confirm")}
                            tooltipDesc={t("MarqueeDelete.DescConfirm", "Highlight targets after releasing the mouse and require confirmation before demolishing.")}
                            onSelect={() => setOption("confirm", !state.options.confirm)}
                        />
                        <ModeToolButton
                            selected={state.options.skipReview}
                            src={SKIP_REVIEW_ICON}
                            tooltipTitle={t("MarqueeDelete.OptionSkipReview", "Skip Review")}
                            tooltipDesc={t("MarqueeDelete.DescSkipReview", "Skip mid-batch target-change re-review pauses and execute batch demolition directly (map tiles, districts, and outside connections remain protected).")}
                            onSelect={() => setOption("skipReview", !state.options.skipReview)}
                        />
                        <ModeToolButton
                            selected={state.options.protectOutside}
                            src={LOCK_ICON}
                            tooltipTitle={t("MarqueeDelete.OptionProtectOutside", "Safe Edge")}
                            tooltipDesc={t("MarqueeDelete.DescProtectOutside", "Automatically protect map-edge highway, rail, water, and power outside connections.")}
                            onSelect={() => setOption("protectOutside", !state.options.protectOutside)}
                        />
                        <ModeToolButton
                            selected={state.options.panelGlow}
                            src={GLOW_ICON}
                            tooltipTitle={t("MarqueeDelete.OptionPanelGlow", "Prism Glow")}
                            tooltipDesc={t("MarqueeDelete.DescPanelGlow", "Toggle the multi-color gradient breathing glow across the panel and border (off by default).")}
                            onSelect={() => setOption("panelGlow", !state.options.panelGlow)}
                        />
                        <ModeToolButton
                            selected={state.options.undoFx}
                            src={UNDO_FX_ICON}
                            tooltipTitle={t("MarqueeDelete.OptionUndoFx", "Undo FX")}
                            tooltipDesc={t("MarqueeDelete.DescUndoFx", "Toggle the flowing rainbow particle gradient effect on the Undo progress bar (off by default).")}
                            onSelect={() => setOption("undoFx", !state.options.undoFx)}
                        />
                        <ModeToolButton
                            selected={false}
                            src={RESET_ICON}
                            tooltipTitle={t("MarqueeDelete.ResetFiltersBtn", "Reset")}
                            tooltipDesc={t("MarqueeDelete.DescResetFilters", "Restore target category filters back to recommended safe defaults.")}
                            onSelect={() => setAllFilters(false)}
                        />
                    </SectionWrapper>

                    {showUndoScope && (
                        <React.Fragment>
                            <SectionWrapper title={t("MarqueeDelete.UndoScopeLabel", "Undo Scope")}>
                                {undoScopeSpecs.map((spec) => {
                                    const baseDesc = t(spec.descId, spec.descFallback);
                                    const consumedHint = t(
                                        "MarqueeDelete.UndoScopeConsumedHint",
                                        "An undo consumes the whole snapshot: categories you leave unchecked are dropped and cannot be restored from that step later."
                                    );
                                    return (
                                        <ModeToolButton
                                            key={spec.key}
                                            selected={spec.enabled}
                                            disabled={undoScopeLocked}
                                            src={spec.icon}
                                            tooltipTitle={t(spec.labelId, spec.fallback)}
                                            tooltipDesc={
                                                undoScopeLocked
                                                    ? `${baseDesc} (${t("MarqueeDelete.UndoLockedScopeHint", "Scope locked while current undo is active or paused.")})`
                                                    : `${baseDesc} (${consumedHint})`
                                            }
                                            onSelect={() => {
                                                if (!undoScopeLocked) {
                                                    setOption(spec.key, !spec.enabled);
                                                }
                                            }}
                                        >
                                            <FilterCountBadge binding={spec.countBinding} />
                                        </ModeToolButton>
                                    );
                                })}
                            </SectionWrapper>
                            <div className={styles.undoScopeConsumedHint}>
                                {t(
                                    "MarqueeDelete.UndoScopeConsumedHint",
                                    "An undo consumes the whole snapshot: categories you leave unchecked are dropped and cannot be restored from that step later."
                                )}
                            </div>
                        </React.Fragment>
                    )}
                </React.Fragment>
            )}
        </div>
    );

    return (
        <React.Fragment>
            <div className={state.options.panelGlow ? styles.marqueePanelGlow : styles.marqueePanel}>
                <MarqueeHeader
                    active={state.active}
                    dragging={state.dragging}
                    selectionMode={state.selectionMode}
                    compatCanUndo={state.compatCanUndo}
                    colorModalOpen={colorModalOpen}
                    modeInfoOpen={modeInfoOpen}
                    activeTab={activeTab}
                    undo={state.undo}
                    isZh={isZh}
                    onToggleModeInfo={() => setModeInfoOpen((prev) => !prev)}
                    onChangeTab={setActiveTab}
                    t={t}
                />
                {content}
                <MarqueeFooter
                    active={state.active}
                    underground={state.underground}
                    hasAnyFilter={hasAnyFilter}
                    hasUndergroundFilter={hasUndergroundFilter}
                    modalOpen={colorModalOpen || importantModalOpen}
                    roadFacilityMode={roadFacilityMode}
                    undo={state.undo}
                    compatCanUndo={state.compatCanUndo}
                    onOpenUndoOptions={() => setActiveTab("options")}
                    t={t}
                />
            </div>
            {state.active && <BorderDimensionLabels underground={state.underground} t={t} />}
            {state.active && importantModalOpen && <ImportantConfirmModal t={t} />}
            {colorModalOpen && !importantModalOpen && <ColorPaletteModal t={t} />}
        </React.Fragment>
    );
}

export function extendToolOptionsVisible(baseHook: () => boolean): () => boolean {
    return function MarqueeToolOptionsVisible(): boolean {
        const baseVisible = baseHook();
        const canUse = useValue(canUse$);
        const active = useValue(active$);
        const activeTool = useValue(tool.activeTool$);

        if (!canUse) {
            return baseVisible;
        }

        const id = activeTool ? activeTool.id : "";
        return baseVisible || active || id === tool.BULLDOZE_TOOL || id === "Marquee Delete";
    };
}

// Smooth full-spectrum multi-color breathing glow (continuous 360-degree rainbow + sinusoidal pulse)
function wrapHueDeg(deg: number): number {
    return ((deg % 360) + 360) % 360;
}

const MemoBulldozeMarqueeSection = React.memo(BulldozeMarqueeSection);

export function extendMouseToolOptions(Component: React.ComponentType<any>): (props: any) => JSX.Element {
    const MemoOriginalOptions = React.memo(Component);

    return function MarqueeMouseToolOptions(props: any): JSX.Element {
        const canUse = useValue(canUse$);
        const active = useValue(active$);
        const panelGlow = useValue(optPanelGlow$);
        const activeTool = useValue(tool.activeTool$);
        const id = activeTool ? activeTool.id : "";
        const isBulkToolContext = canUse && (active || id === tool.BULLDOZE_TOOL || id === "Marquee Delete");

        const stackRef = React.useRef<HTMLDivElement | null>(null);
        const topRibbonRef = React.useRef<HTMLDivElement | null>(null);
        const bottomRibbonRef = React.useRef<HTMLDivElement | null>(null);
        const animRef = React.useRef({ huePhase: 0, breathPhase: 0 });

        React.useEffect(() => {
            const stackEl = stackRef.current;
            const topEl = topRibbonRef.current;
            const botEl = bottomRibbonRef.current;
            if (!isBulkToolContext || !panelGlow) {
                if (stackEl) {
                    stackEl.style.borderTopColor = "";
                    stackEl.style.borderRightColor = "";
                    stackEl.style.borderBottomColor = "";
                    stackEl.style.borderLeftColor = "";
                    stackEl.style.backgroundImage = "";
                    stackEl.style.boxShadow = "";
                }
                if (topEl) {
                    topEl.style.backgroundImage = "";
                    topEl.style.boxShadow = "";
                    topEl.style.opacity = "";
                }
                if (botEl) {
                    botEl.style.backgroundImage = "";
                    botEl.style.boxShadow = "";
                    botEl.style.opacity = "";
                }
                return;
            }

            const applyFrame = () => {
                const { huePhase, breathPhase } = animRef.current;
                // Smooth breathing wave 0..1..0 (~3.6s full breath cycle)
                const breath = 0.5 - 0.5 * Math.cos((breathPhase * Math.PI) / 180);

                // Six evenly spaced hues covering the full rainbow spectrum at all times
                const h0 = wrapHueDeg(huePhase);
                const h1 = wrapHueDeg(huePhase + 60);
                const h2 = wrapHueDeg(huePhase + 120);
                const h3 = wrapHueDeg(huePhase + 180);
                const h4 = wrapHueDeg(huePhase + 240);
                const h5 = wrapHueDeg(huePhase + 300);

                const borderL = 64 + breath * 12;
                const borderA = 0.68 + breath * 0.30;
                const bgA = 0.13 + breath * 0.17;
                const outer1R = (12 + breath * 15).toFixed(1);
                const outer2R = (24 + breath * 26).toFixed(1);
                const insetR = (14 + breath * 14).toFixed(1);
                const outer1A = 0.24 + breath * 0.44;
                const outer2A = 0.14 + breath * 0.28;
                const insetA = 0.10 + breath * 0.20;

                if (stackEl) {
                    stackEl.style.borderTopColor = hslToRgbaCss(h0, 96, borderL, borderA);
                    stackEl.style.borderRightColor = hslToRgbaCss(h1, 94, borderL, borderA);
                    stackEl.style.borderBottomColor = hslToRgbaCss(h3, 94, borderL, borderA);
                    stackEl.style.borderLeftColor = hslToRgbaCss(h4, 96, borderL, borderA);
                    stackEl.style.backgroundImage = `linear-gradient(135deg, ${hslToRgbaCss(h0, 92, 64, bgA)} 0%, ${hslToRgbaCss(h1, 90, 64, bgA * 0.92)} 25%, ${hslToRgbaCss(h2, 88, 62, bgA * 0.88)} 50%, ${hslToRgbaCss(h3, 92, 64, bgA * 0.92)} 75%, ${hslToRgbaCss(h5, 92, 64, bgA)} 100%)`;
                    stackEl.style.boxShadow = `0 0 ${outer1R}rem ${hslToRgbaCss(h0, 96, 66, outer1A)}, 0 0 ${outer2R}rem ${hslToRgbaCss(h3, 94, 66, outer2A)}, inset 0 0 ${insetR}rem ${hslToRgbaCss(h2, 92, 70, insetA)}`;
                }

                const ribbonAlpha = 0.78 + breath * 0.22;
                const ribbonBg = `linear-gradient(90deg, ${hslToRgbaCss(h0, 98, 66, ribbonAlpha)} 0%, ${hslToRgbaCss(h1, 96, 66, ribbonAlpha)} 20%, ${hslToRgbaCss(h2, 94, 64, ribbonAlpha)} 40%, ${hslToRgbaCss(h3, 96, 66, ribbonAlpha)} 60%, ${hslToRgbaCss(h4, 96, 68, ribbonAlpha)} 80%, ${hslToRgbaCss(h5, 98, 66, ribbonAlpha)} 100%)`;
                const ribbonShadowR = (7 + breath * 13).toFixed(1);
                const ribbonShadowA = 0.36 + breath * 0.48;
                const ribbonShadow = `0 0 ${ribbonShadowR}rem ${hslToRgbaCss(h1, 96, 68, ribbonShadowA)}`;
                const ribbonOpacity = (0.72 + breath * 0.28).toFixed(3);
                if (topEl) {
                    topEl.style.backgroundImage = ribbonBg;
                    topEl.style.boxShadow = ribbonShadow;
                    topEl.style.opacity = ribbonOpacity;
                }
                if (botEl) {
                    botEl.style.backgroundImage = ribbonBg;
                    botEl.style.boxShadow = ribbonShadow;
                    botEl.style.opacity = ribbonOpacity;
                }
            };

            applyFrame();
            const timer = window.setInterval(() => {
                animRef.current.huePhase = (animRef.current.huePhase + 2.2) % 360;
                animRef.current.breathPhase = (animRef.current.breathPhase + 4.5) % 360;
                applyFrame();
            }, 45);
            return () => window.clearInterval(timer);
        }, [isBulkToolContext, panelGlow]);

        if (!isBulkToolContext) {
            return <Component {...props} />;
        }

        return (
            <div
                ref={stackRef}
                className={panelGlow ? styles.toolOptionsStackGlow : styles.toolOptionsStack}
            >
                {panelGlow && <div ref={topRibbonRef} className={styles.geminiTopGlow} />}
                <div
                    className={panelGlow ? styles.originalToolOptionsGlow : styles.originalToolOptions}
                    onWheel={(event) => event.stopPropagation()}
                >
                    <MemoOriginalOptions {...props} />
                </div>
                <MemoBulldozeMarqueeSection />
                {panelGlow && <div ref={bottomRibbonRef} className={styles.geminiBottomGlow} />}
            </div>
        );
    };
}
