import React from "react";
import ReactDOM from "react-dom";
import { useValue } from "cs2/api";
import { tool } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { Button, Tooltip } from "cs2/ui";
import {
    active$,
    applyFilterPreset,
    cancelImportantModal,
    cancelUndo,
    canUse$,
    clearAllFilters,
    clearCustomPreset,
    clearSelection,
    confirmDelete,
    confirmImportantDelete,
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
    dismissUndoNotice,
    excludeImportant,
    importantModalOpen$,
    saveCustomPreset,
    setAllFilters,
    setColorModalOpen,
    setOption,
    soloFilter,
    setSelectionMode,
    stopUndo,
    switchFromBulldoze,
    togglePickSimilar,
    undoCountAreas$,
    undoCountBuildings$,
    undoCountDecals$,
    undoCountNetworks$,
    undoCountProps$,
    undoCountTrees$,
    undoLastDelete,
    restoreLastSelection,
    reviewInterruptedDeletion,
    discardInterruptedDeletion,
    setImpactReportOpen,
    shapeMode$,
    shapeAreaSqMeters$,
    shapePerimeterMeters$,
    shapeVertexCount$,
    boxWidthCells$,
    boxDepthCells$,
    boxWidthMeters$,
    boxDepthMeters$,
    useColorModalOpen,
    useColorPalette,
    useMarqueeCardState,
    useMarqueeDimensionsState,
    useMarqueeModalState,
    useMarqueeToolbarState,
} from "./bindings";
import clearFiltersIconUrl from "./clear-filters-icon.svg";
import colorPaletteIconUrl from "./color-palette-icon.svg";
import confirmIconUrl from "./confirm-icon.svg";
import lockIconUrl from "./lock-icon.svg";
import marqueeIconUrl from "./marquee-delete-icon.svg";
import starAllIconUrl from "./star-all-icon.svg";
import undoIconUrl from "./undo-icon.svg";
import { ColorPaletteModal } from "./color-palette-modal";
import { vanillaRefs } from "./vanilla-refs";
import styles from "./bulldoze-marquee-section.module.scss";

const CAMERA_ICON = "Media/Game/Icons/Camera.svg";
const CONFIRM_ICON = confirmIconUrl;
const LOCK_ICON = lockIconUrl;
const RESET_ICON = "Media/Game/Icons/NewUI/Reset_Button.svg";
const STAR_ALL_ICON = starAllIconUrl;
const ICON_RESIDENTIAL = "Media/Game/Icons/ZoneResidential.svg";
const ICON_COMMERCIAL = "Media/Game/Icons/ZoneCommercial.svg";
const ICON_INDUSTRIAL = "Media/Game/Icons/ZoneIndustrial.svg";
const ICON_OFFICE = "Media/Game/Icons/ZoneOffice.svg";
const ICON_SERVICES = "Media/Game/Icons/Services.svg";
const ICON_SIGNATURE = "Media/Game/Icons/ZoneSignature.svg";
const ICON_BUILDINGS = "Media/Game/Icons/Services.svg";
const ICON_NETWORKS = "Media/Game/Icons/Roads.svg";
const ICON_TREES = "Media/Game/Icons/Trees.svg";
const ICON_PROPS = "Media/Game/Icons/Props.svg";
const ICON_DECALS = "Media/Game/Icons/PropsDecals.svg";
const ICON_AREAS = "Media/Game/Icons/SurfaceAreaTool.svg";
const ICON_MOVING = "Media/Game/Icons/GenericVehicle.svg";

export const ALL_PRELOAD_ICONS: string[] = [
    marqueeIconUrl,
    clearFiltersIconUrl,
    colorPaletteIconUrl,
    undoIconUrl,

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
    ICON_SIGNATURE,
    ICON_BUILDINGS,
    ICON_NETWORKS,
    ICON_TREES,
    ICON_PROPS,
    ICON_DECALS,
    ICON_AREAS,
    ICON_MOVING,
];

interface ZoneFilterSpec {
    key: "residential" | "commercial" | "industrial" | "office" | "buildings";
    labelId: string;
    fallback: string;
    descId: string;
    descFallback: string;
    icon: string;
    colorBarClass: string;
    colorHex: string;
    enabled: boolean;
    countBinding: typeof countBuildings$;
}

interface AssetFilterSpec {
    key: "networks" | "trees" | "props" | "decals" | "areas" | "moving";
    labelId: string;
    fallback: string;
    descId: string;
    descFallback: string;
    icon: string;
    enabled: boolean;
    countBinding: typeof countBuildings$;
}

function renderRichTooltip(title: string, desc: string): JSX.Element {
    const theme = vanillaRefs.descriptionTooltipTheme;
    return (
        <div className={styles.tooltipContainer}>
            <div className={theme?.title ?? styles.tooltipTitle}>{title}</div>
            <div className={theme?.content ?? styles.tooltipDesc}>{desc}</div>
        </div>
    );
}

function SectionWrapper(props: { title: string; children: React.ReactNode }): JSX.Element {
    const FocusDisabledComp = vanillaRefs.FocusDisabled;
    const inner = FocusDisabledComp ? (
        <FocusDisabledComp disabled={true}>{props.children}</FocusDisabledComp>
    ) : (
        props.children
    );

    return (
        <div className={styles.panelSection}>
            <div className={styles.sectionTitle}>{props.title}</div>
            <div className={styles.sectionControls}>{inner}</div>
        </div>
    );
}

function ModeToolButton(props: {
    selected: boolean;
    disabled?: boolean;
    src: string;
    tooltipTitle: string;
    tooltipDesc: string;
    onSelect: (e?: any) => void;
    children?: React.ReactNode;
}): JSX.Element {
    const VanillaToolButton = vanillaRefs.ToolButton;
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const tooltipNode = renderRichTooltip(props.tooltipTitle, props.tooltipDesc);
    const btnClass = props.disabled
        ? styles.toolBtnDisabled
        : props.selected
        ? styles.toolBtnRelative
        : styles.toolBtnUnselected;
    const handleSelect = (e?: any) => {
        if (!props.disabled) {
            props.onSelect(e);
        }
    };

    if (VanillaToolButton) {
        return (
            <VanillaToolButton
                selected={props.selected}
                disabled={props.disabled}
                src={props.src}
                tooltip={tooltipNode}
                onSelect={handleSelect}
                className={btnClass}
                focusKey={focusDisabledKey}
            >
                {props.children}
            </VanillaToolButton>
        );
    }

    return (
        <Tooltip tooltip={tooltipNode} delayTime={0}>
            <Button
                variant="icon"
                src={props.src}
                selected={props.selected}
                disabled={props.disabled}
                focusKey={focusDisabledKey}
                className={btnClass}
                onSelect={handleSelect}
            >
                {props.children}
            </Button>
        </Tooltip>
    );
}

function FilterCountBadge(props: { binding: typeof countBuildings$ }): JSX.Element | null {
    const count = useValue(props.binding);
    if (count <= 0) {
        return null;
    }
    return <div className={styles.countBadge}>{count > 999 ? "999+" : count}</div>;
}

function MarqueeDimensionPill(props: {
    unitCells: string;
    unitSqM: string;
    unitVertices: string;
}): JSX.Element | null {
    const shapeMode = useValue(shapeMode$);
    const shapeAreaSqMeters = useValue(shapeAreaSqMeters$);
    const shapePerimeterMeters = useValue(shapePerimeterMeters$);
    const shapeVertexCount = useValue(shapeVertexCount$);
    const boxWidthCells = useValue(boxWidthCells$);
    const boxDepthCells = useValue(boxDepthCells$);
    const boxWidthMeters = useValue(boxWidthMeters$);
    const boxDepthMeters = useValue(boxDepthMeters$);

    if ((shapeMode === 1 || shapeMode === 2) && shapeAreaSqMeters > 0) {
        return (
            <span className={styles.dimensionPill}>
                {shapeAreaSqMeters} {props.unitSqM} · {shapePerimeterMeters}m · {shapeVertexCount} {props.unitVertices}
            </span>
        );
    }
    if (boxWidthCells > 0) {
        return (
            <span className={styles.dimensionPill}>
                {boxWidthCells}×{boxDepthCells} {props.unitCells} ({boxWidthMeters}×{boxDepthMeters}m)
            </span>
        );
    }
    return null;
}

function BorderDimensionLabels(props: {
    underground: boolean;
    t: (id: string, fallback: string) => string;
}): JSX.Element | null {
    const sel = useMarqueeDimensionsState();
    const { underground, t } = props;

    const unitCells = t("MarqueeDelete.UnitCells", "cells");
    const unitSqM = t("MarqueeDelete.UnitSqMeters", "m²");
    const unitVertices = t("MarqueeDelete.UnitVertices", "pts");
    const simplifiedBadge = t("MarqueeDelete.EdgeSimplifiedBadge", "Simplified");
    const tagClass = underground ? styles.edgeDimTagUnderground : styles.edgeDimTag;
    const metersClass = underground ? styles.edgeDimMetersUnderground : styles.edgeDimMeters;

    const isNonRectShape = sel.shapeMode === 1 || sel.shapeMode === 2;
    let overlayNode: JSX.Element;

    if (isNonRectShape) {
        const showShapeLabel =
            (sel.dragging || sel.pending || sel.freehandDrawing || sel.polygonVertexCount >= 3) &&
            sel.shapeAreaSqMeters > 0;
        if (!showShapeLabel) {
            return null;
        }
        overlayNode = (
            <div className={styles.edgeLabelsOverlay}>
                <div
                    className={tagClass}
                    style={{
                        left: `${(sel.widthLabelX * 100).toFixed(2)}%`,
                        top: `${(sel.widthLabelY * 100).toFixed(2)}%`,
                    }}
                >
                    {sel.shapeAreaSqMeters} {unitSqM} · {sel.shapePerimeterMeters}m
                    <span className={metersClass}>
                        ({sel.shapeVertexCount} {unitVertices}
                        {sel.shapeSimplified ? ` · ${simplifiedBadge}` : ""})
                    </span>
                </div>
            </div>
        );
    } else {
        if ((!sel.dragging && !sel.pending) || sel.boxWidthCells <= 0 || sel.boxDepthCells <= 0) {
            return null;
        }
        overlayNode = (
            <div className={styles.edgeLabelsOverlay}>
                <div
                    className={tagClass}
                    style={{
                        left: `${(sel.widthLabelX * 100).toFixed(2)}%`,
                        top: `${(sel.widthLabelY * 100).toFixed(2)}%`,
                    }}
                >
                    {sel.boxWidthCells} {unitCells}
                    <span className={metersClass}>({sel.boxWidthMeters}m)</span>
                </div>
                <div
                    className={tagClass}
                    style={{
                        left: `${(sel.depthLabelX * 100).toFixed(2)}%`,
                        top: `${(sel.depthLabelY * 100).toFixed(2)}%`,
                    }}
                >
                    {sel.boxDepthCells} {unitCells}
                    <span className={metersClass}>({sel.boxDepthMeters}m)</span>
                </div>
            </div>
        );
    }

    if (typeof document !== "undefined" && document.body) {
        return ReactDOM.createPortal(overlayNode, document.body);
    }
    return overlayNode;
}

function ImportantConfirmModal(props: {
    t: (id: string, fallback: string) => string;
}): JSX.Element {
    const sel = useMarqueeModalState();
    const { t } = props;
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;

    React.useEffect(() => {
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === "Enter" || e.code === "Enter" || e.code === "NumpadEnter") {
                e.preventDefault();
                e.stopPropagation();
                if (!e.repeat && sel.compatCanSubmit) {
                    confirmImportantDelete();
                }
            } else if (e.key === "Escape" || e.code === "Escape") {
                e.preventDefault();
                e.stopPropagation();
                if (!e.repeat) {
                    cancelImportantModal();
                }
            }
        };
        window.addEventListener("keydown", onKeyDown, true);
        return () => window.removeEventListener("keydown", onKeyDown, true);
    }, [sel.compatCanSubmit]);

    let overflowCount = 0;
    const parsedItems: { key: string; kindLabel: string; name: string }[] = [];
    if (sel.importantListRaw) {
        const lines = sel.importantListRaw.split("\n");
        for (let idx = 0; idx < lines.length; idx++) {
            const line = lines[idx];
            if (!line || line.trim().length === 0) {
                continue;
            }
            const parts = line.split("|");
            const kindCode = parts[0] ?? "S";
            if (kindCode === "MORE") {
                overflowCount = Number(parts[1]) || 0;
                continue;
            }
            const name = parts.slice(1).join("|") || "Building";
            let kindLabel = t("MarqueeDelete.ImportantService", "Service");
            if (kindCode === "U" || kindCode === "unique") {
                kindLabel = t("MarqueeDelete.ImportantUnique", "Unique");
            } else if (kindCode === "L" || kindCode === "signature") {
                kindLabel = t("MarqueeDelete.ImportantSignature", "Signature");
            }
            parsedItems.push({ key: `${idx}-${name}`, kindLabel, name });
        }
    }

    const modalNode = (
        <div className={styles.modalBackdrop}>
            <div className={styles.modalCard}>
                <div className={styles.modalHeader}>
                    <div className={styles.modalWarnBadge}>
                        ★ {t("MarqueeDelete.ImportantTag", "Important")} ({sel.countImportant})
                    </div>
                    <div className={styles.modalTitle}>
                        {t("MarqueeDelete.ModalTitle", "Important Buildings Detected")}
                    </div>
                </div>
                {sel.notice === "changed" && (
                    <div className={styles.importantWarnBanner}>
                        {t("MarqueeDelete.SelectionChanged", "The affected objects changed. Review the selection before confirming again.")}
                    </div>
                )}
                {!sel.compatCanSubmit && (
                    <div className={styles.importantWarnBanner}>
                        {sel.compatTier === "preview_only"
                            ? t("MarqueeDelete.PreviewOnlyNotice", "Current game build is in preview mode — deletion is disabled pending write verification.")
                            : t("MarqueeDelete.CompatUnavailableNotice", "Deletion is unavailable in this build.")}
                    </div>
                )}
                <div className={styles.modalSubtitle}>
                    {t(
                        "MarqueeDelete.ModalSubtitle",
                        "The selection contains critical city buildings. Please confirm before demolishing:"
                    )}
                </div>

                <div className={styles.modalStatsRow}>
                    {sel.countService > 0 && (
                        <div className={styles.modalStatPill}>
                            {t("MarqueeDelete.ImportantService", "Service")}: {sel.countService}
                        </div>
                    )}
                    {sel.countUnique > 0 && (
                        <div className={styles.modalStatPill}>
                            {t("MarqueeDelete.ImportantUnique", "Unique")}: {sel.countUnique}
                        </div>
                    )}
                    {sel.countSignature > 0 && (
                        <div className={styles.modalStatPill}>
                            {t("MarqueeDelete.ImportantSignature", "Signature")}: {sel.countSignature}
                        </div>
                    )}
                </div>

                {(parsedItems.length > 0 || overflowCount > 0) && (
                    <div className={styles.modalListBox}>
                        {parsedItems.map((item) => (
                            <div key={item.key} className={styles.modalListItem}>
                                <span className={styles.modalItemName}>{item.name}</span>
                                <span className={styles.modalItemKind}>{item.kindLabel}</span>
                            </div>
                        ))}
                        {overflowCount > 0 && (
                            <div className={styles.modalMoreRow}>
                                +{overflowCount} {t("MarqueeDelete.MoreImportantItems", "more important buildings")}
                            </div>
                        )}
                    </div>
                )}

                <div className={styles.modalActions}>
                    <Button
                        variant="flat"
                        focusKey={focusDisabledKey}
                        className={styles.modalCancelBtn}
                        onSelect={cancelImportantModal}
                    >
                        {t("MarqueeDelete.ModalCancelBtn", "Cancel")} [Esc]
                    </Button>
                    <Button
                        variant="flat"
                        focusKey={focusDisabledKey}
                        className={styles.modalExcludeBtn}
                        onSelect={excludeImportant}
                    >
                        {t("MarqueeDelete.ModalExcludeBtn", "Exclude Important")}
                    </Button>
                    {!sel.compatCanSubmit ? (
                        <Button
                            variant="flat"
                            disabled={true}
                            focusKey={focusDisabledKey}
                            className={styles.confirmBtnPreviewOnly}
                        >
                            {sel.compatTier === "preview_only"
                                ? t("MarqueeDelete.PreviewOnlyNotice", "Preview only")
                                : t("MarqueeDelete.CompatUnavailableNotice", "Deletion unavailable")}
                        </Button>
                    ) : (
                        <Button
                            variant="flat"
                            focusKey={focusDisabledKey}
                            className={styles.modalDangerBtn}
                            onSelect={confirmImportantDelete}
                        >
                            {t("MarqueeDelete.ModalConfirmBtn", "Confirm Demolish")} ({sel.count}) [Enter]
                        </Button>
                    )}
                </div>
            </div>
        </div>
    );

    if (typeof document !== "undefined" && document.body) {
        return ReactDOM.createPortal(modalNode, document.body);
    }
    return modalNode;
}

function UndoProgressBanner(props: {
    restoredCount: number;
    skippedCount: number;
    totalCount: number;
    t: (id: string, fallback: string) => string;
}): JSX.Element {
    const { restoredCount, skippedCount, totalCount, t } = props;
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const safeTotal = totalCount > 0 ? totalCount : 1;
    const processedCount = restoredCount + skippedCount;
    const pct = Math.min(100, Math.max(0, Math.round((processedCount / safeTotal) * 100)));

    return (
        <div className={styles.undoProgressBox}>
            <div className={styles.undoProgressTop}>
                <span className={styles.undoProgressLabel}>
                    {t("MarqueeDelete.UndoProgressLabel", "Creation requests submitted")}: {restoredCount}/{totalCount}
                </span>
                <div className={styles.undoProgressActions}>
                    <Button
                        variant="flat"
                        focusKey={focusDisabledKey}
                        className={styles.undoPauseBtn}
                        onSelect={cancelUndo}
                    >
                        {t("MarqueeDelete.UndoPauseBtn", "Pause")}
                    </Button>
                    <Button
                        variant="flat"
                        focusKey={focusDisabledKey}
                        className={styles.undoStopBtn}
                        onSelect={stopUndo}
                    >
                        {t("MarqueeDelete.UndoStopBtn", "Stop")}
                    </Button>
                </div>
            </div>
            {skippedCount > 0 && (
                <div className={styles.undoSkippedNote}>
                    {t("MarqueeDelete.UndoSkippedLabel", "Skipped")}: {skippedCount} ({processedCount}/{totalCount}, {pct}%)
                </div>
            )}
            <div className={styles.undoProgressTrack}>
                <div className={styles.undoProgressFill} style={{ width: `${pct}%` }} />
            </div>
        </div>
    );
}

interface UndoUiState {
    busy: boolean;
    notice: string;
    finishedNotice: string;
    canUndo: boolean;
    inProgress: boolean;
    nextCount: number;
    restoredCount: number;
    skippedCount: number;
    totalCount: number;
    notCaptured: number;
    historyDepth: number;
    zeroMatch: boolean;
    finishedRestoredCount: number;
    finishedSkippedCount: number;
    finishedTotalCount: number;
    finishedNotCaptured: number;
    captureFailedCount: number;
    include: {
        buildings: boolean;
        networks: boolean;
        areas: boolean;
        trees: boolean;
        props: boolean;
        decals: boolean;
    };
    counts: {
        buildings: number;
        networks: number;
        areas: number;
        trees: number;
        props: number;
        decals: number;
    };
}

const UNDO_NOTICE_FALLBACKS: Record<string, string> = {
    UndoGeometryOnly: "Undo rebuilds geometry, not households or simulation state.",
    UndoPartial: "Only part of this deletion was recorded (8,000-record snapshot limit).",
    UndoPaused: "Undo paused; deletion is locked until resumed. Click Resume or Undo to continue.",
    UndoWaitingOwner: "Waiting for parent buildings to become ready...",
    UndoError: "Undo stopped due to an error; remaining objects were not rebuilt. Reload your save to recover.",
    UndoStalled: "Creation requests submitted, but some attached objects were skipped after waiting too long for parent buildings and remain unbuilt. Reload your save to recover; actual rebuilding is not verified.",
    UndoSubmitted: "All creation requests have been submitted. Whether the game rebuilds them depends on simulation and is not verified.",
    UndoSubmittedPartial: "Creation requests submitted with omissions (some items were skipped or not recorded). Whether the game rebuilds them depends on simulation and is not verified.",
    UndoStopped: "Undo stopped. The remaining items were not submitted for rebuild.",
};

function translateUndoNoticeKey(key: string, t: (id: string, fallback: string) => string): string {
    return t("MarqueeDelete." + key, UNDO_NOTICE_FALLBACKS[key] ?? key);
}

function UndoStatusArea(props: {
    undo: UndoUiState;
    compatCanUndo?: boolean;
    t: (id: string, fallback: string) => string;
}): JSX.Element | null {
    const { undo, compatCanUndo = true, t } = props;
    const isZh = t("MarqueeDelete.UndoBtn", "Undo") !== "Undo";
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const isPaused = undo.busy && !undo.inProgress;
    const hasAnything =
        undo.inProgress ||
        undo.busy ||
        !!undo.notice ||
        !!undo.finishedNotice ||
        undo.notCaptured > 0 ||
        undo.zeroMatch ||
        undo.captureFailedCount > 0 ||
        (undo.counts.buildings > 0 && undo.historyDepth > 0);

    if (!hasAnything) {
        return null;
    }

    const finishedIsAlert = undo.finishedNotice === "UndoError" || undo.finishedNotice === "UndoStalled";
    const activeNoticeIsWarn = undo.notice === "UndoWaitingOwner" || undo.notice === "UndoPaused";
    const finishedParts: string[] = [];
    if (undo.finishedNotice) {
        finishedParts.push(
            t("MarqueeDelete.UndoFinishedSummary", "Submitted {0} / {1}")
                .replace("{0}", String(undo.finishedRestoredCount))
                .replace("{1}", String(undo.finishedTotalCount))
        );
        if (undo.finishedNotice === "UndoStopped") {
            const remainingNotRebuilt = Math.max(
                0,
                undo.finishedTotalCount - undo.finishedRestoredCount - undo.finishedSkippedCount
            );
            finishedParts.push(
                `${t("MarqueeDelete.UndoRemainingNotRebuiltLabel", "Remaining not rebuilt")}: ${remainingNotRebuilt}`
            );
        }
        if (undo.finishedSkippedCount > 0) {
            finishedParts.push(`${t("MarqueeDelete.UndoSkippedLabel", "Skipped")}: ${undo.finishedSkippedCount}`);
        }
        if (undo.finishedNotCaptured > 0) {
            finishedParts.push(`${t("MarqueeDelete.UndoNotCapturedLabel", "Not captured")}: ${undo.finishedNotCaptured}`);
        }
    }

    return (
        <React.Fragment>
            {undo.finishedNotice && (
                <div className={finishedIsAlert ? styles.importantWarnBanner : styles.infoHintBanner}>
                    <div className={styles.undoNoticeRow}>
                        <div className={styles.undoNoticeTextBlock}>
                            <span className={styles.undoNoticeText}>{translateUndoNoticeKey(undo.finishedNotice, t)}</span>
                            <span className={styles.undoNoticeSub}>{finishedParts.join(" · ")}</span>
                        </div>
                        <Button
                            variant="flat"
                            focusKey={focusDisabledKey}
                            className={styles.undoDismissBtn}
                            onSelect={dismissUndoNotice}
                        >
                            {t("MarqueeDelete.UndoDismissBtn", "Dismiss")}
                        </Button>
                    </div>
                </div>
            )}

            {undo.captureFailedCount > 0 && (
                <div className={styles.importantWarnBanner}>
                    <div className={styles.undoNoticeRow}>
                        <span className={styles.undoNoticeText}>
                            {t(
                                "MarqueeDelete.UndoNothingCaptured",
                                "No undo snapshot saved for this deletion (all {0} supported object(s) failed to capture)."
                            ).replace("{0}", String(undo.captureFailedCount))}
                        </span>
                        <Button
                            variant="flat"
                            focusKey={focusDisabledKey}
                            className={styles.undoDismissBtn}
                            onSelect={dismissUndoNotice}
                        >
                            {t("MarqueeDelete.UndoDismissBtn", "Dismiss")}
                        </Button>
                    </div>
                </div>
            )}

            {!undo.busy && !undo.inProgress && undo.zeroMatch && (
                <div className={styles.importantWarnBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.UndoZeroMatchHint",
                            "Top undo snapshot has no records matching the selected undo categories ({0} snapshot(s) queued)."
                        ).replace("{0}", String(undo.historyDepth))}
                    </span>
                </div>
            )}

            {isPaused && (
                <div className={styles.undoPausedBox}>
                    <span>{translateUndoNoticeKey(undo.notice || "UndoPaused", t)}</span>
                    <div className={styles.undoPausedBottom}>
                        <span className={styles.undoPausedRemaining}>
                            {t("MarqueeDelete.UndoRemainingLabel", "Remaining requests")}: {undo.nextCount} ·{" "}
                            {t("MarqueeDelete.UndoFinishedSummary", "Submitted {0} / {1}")
                                .replace("{0}", String(undo.restoredCount))
                                .replace("{1}", String(undo.totalCount))}
                            {undo.skippedCount > 0
                                ? ` · ${t("MarqueeDelete.UndoSkippedLabel", "Skipped")}: ${undo.skippedCount}`
                                : ""}
                        </span>
                        <div className={styles.undoPausedActions}>
                            {undo.canUndo && (
                                !compatCanUndo ? (
                                    <Tooltip
                                        tooltip={renderRichTooltip(
                                            t("MarqueeDelete.UndoDisabledCompatTitle", isZh ? "撤回（已禁用）" : "Undo (Disabled)"),
                                            t("MarqueeDelete.UndoDisabledCompatDesc", isZh ? "当前游戏版本未验证，撤回已禁用。" : "Undo is disabled: target game version is unverified or rebuild components are missing.")
                                        )}
                                        delayTime={0}
                                    >
                                        <Button
                                            variant="flat"
                                            disabled={true}
                                            focusKey={focusDisabledKey}
                                            className={styles.undoBtnDisabled}
                                        >
                                            {t("MarqueeDelete.UndoResumeBtn", "Resume")}
                                        </Button>
                                    </Tooltip>
                                ) : (
                                    <Button
                                        variant="flat"
                                        focusKey={focusDisabledKey}
                                        className={styles.undoResumeBtn}
                                        onSelect={undoLastDelete}
                                    >
                                        {t("MarqueeDelete.UndoResumeBtn", "Resume")}
                                    </Button>
                                )
                            )}
                            <Button
                                variant="flat"
                                focusKey={focusDisabledKey}
                                className={styles.undoStopBtn}
                                onSelect={stopUndo}
                            >
                                {t("MarqueeDelete.UndoStopBtn", "Stop")}
                            </Button>
                        </div>
                    </div>
                </div>
            )}

            {!compatCanUndo && (
                <div className={styles.importantWarnBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.UndoDisabledCompatDesc",
                            isZh
                                ? "当前游戏版本未验证，撤回已禁用。"
                                : "Undo is disabled: target game version is unverified or rebuild components are missing."
                        )}
                    </span>
                </div>
            )}
            {!isPaused && undo.notice && undo.notice !== "UndoNothingCaptured" && (
                <div className={activeNoticeIsWarn ? styles.importantWarnBanner : styles.infoHintBanner}>
                    <span>{translateUndoNoticeKey(undo.notice, t)}</span>
                </div>
            )}

            {undo.notCaptured > 0 && (
                <div className={styles.importantWarnBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.UndoNotCapturedHint",
                            "Selected undo categories had {0} object(s) not captured in this snapshot."
                        ).replace("{0}", String(undo.notCaptured))}
                    </span>
                </div>
            )}

            {undo.counts.buildings > 0 && (undo.historyDepth > 0 || undo.busy || undo.inProgress) && (
                <div className={styles.undoScopeNote}>
                    {t(
                        "MarqueeDelete.UndoBuildingsCountNote",
                        "Building count shows total rebuild records (includes upgrades, attached areas & sub-nets), not building count alone."
                    )}
                </div>
            )}

            {undo.inProgress && (
                <UndoProgressBanner
                    restoredCount={undo.restoredCount}
                    skippedCount={undo.skippedCount}
                    totalCount={undo.totalCount}
                    t={t}
                />
            )}
        </React.Fragment>
    );
}

function ImpactReportCard(props: {
    raw: string;
    t: (id: string, fallback: string) => string;
    onClose: () => void;
}): JSX.Element | null {
    const { raw, t, onClose } = props;
    if (!raw) {
        return null;
    }
    const lines = raw.split("\n").filter(Boolean);
    return (
        <div className={styles.impactReportCard}>
            <div className={styles.impactReportHeader}>
                <span>{t("MarqueeDelete.ImpactReportTitle", "Demolition Impact Breakdown")}</span>
                <Button
                    variant="flat"
                    focusKey={vanillaRefs.FOCUS_DISABLED}
                    className={styles.customSlotClearBtn}
                    onSelect={onClose}
                >
                    ×
                </Button>
            </div>
            <div className={styles.impactReportList}>
                {lines.map((line, idx) => {
                    const parts = line.split("|");
                    const name = parts[0] || "";
                    const count = parts[1] || "0";
                    const isMore = name === "MORE";
                    return (
                        <div key={idx} className={styles.impactReportItem}>
                            <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                                {isMore ? t("MarqueeDelete.ImpactReportMore", "+{0} other prefabs").replace("{0}", count) : name}
                            </span>
                            <span className={styles.impactReportItemCount}>{count}</span>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}

function MarqueeActiveCard(props: {
    underground: boolean;
    hasAnyFilter: boolean;
    hasUndergroundFilter: boolean;
    modalOpen: boolean;
    t: (id: string, fallback: string) => string;
}): JSX.Element {
    const sel = useMarqueeCardState();
    const { t, underground, hasAnyFilter, hasUndergroundFilter, modalOpen } = props;
    const isZh = t("MarqueeDelete.UndoBtn", "Undo") !== "Undo";
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const FocusDisabledComp = vanillaRefs.FocusDisabled;

    const breakdownItems: { key: string; label: string; count: number; dotClass?: string }[] = [];
    if (sel.counts.residential > 0) {
        breakdownItems.push({
            key: "residential",
            label: t("MarqueeDelete.FilterResidential", "Residential"),
            count: sel.counts.residential,
            dotClass: styles.tagDotRes,
        });
    }
    if (sel.counts.commercial > 0) {
        breakdownItems.push({
            key: "commercial",
            label: t("MarqueeDelete.FilterCommercial", "Commercial"),
            count: sel.counts.commercial,
            dotClass: styles.tagDotCom,
        });
    }
    if (sel.counts.industrial > 0) {
        breakdownItems.push({
            key: "industrial",
            label: t("MarqueeDelete.FilterIndustrial", "Industrial"),
            count: sel.counts.industrial,
            dotClass: styles.tagDotInd,
        });
    }
    if (sel.counts.office > 0) {
        breakdownItems.push({
            key: "office",
            label: t("MarqueeDelete.FilterOffice", "Office"),
            count: sel.counts.office,
            dotClass: styles.tagDotOff,
        });
    }
    if (sel.counts.buildings > 0) {
        breakdownItems.push({
            key: "buildings",
            label: t("MarqueeDelete.FilterOtherBuildings", "Service/Other"),
            count: sel.counts.buildings,
            dotClass: styles.tagDotOther,
        });
    }
    if (sel.counts.networks > 0) {
        breakdownItems.push({
            key: "networks",
            label: t("MarqueeDelete.FilterNetworks", "Roads/Nets"),
            count: sel.counts.networks,
        });
    }
    if (sel.counts.trees > 0) {
        breakdownItems.push({
            key: "trees",
            label: t("MarqueeDelete.FilterTrees", "Trees"),
            count: sel.counts.trees,
        });
    }
    if (sel.counts.props > 0) {
        breakdownItems.push({
            key: "props",
            label: t("MarqueeDelete.FilterProps", "Props"),
            count: sel.counts.props,
        });
    }
    if (sel.counts.decals > 0) {
        breakdownItems.push({
            key: "decals",
            label: t("MarqueeDelete.FilterDecals", "Decals"),
            count: sel.counts.decals,
        });
    }
    if (sel.counts.areas > 0) {
        breakdownItems.push({
            key: "areas",
            label: t("MarqueeDelete.FilterAreas", "Surfaces"),
            count: sel.counts.areas,
        });
    }
    if (sel.counts.moving > 0) {
        breakdownItems.push({
            key: "moving",
            label: t("MarqueeDelete.FilterMoving", "Vehicles"),
            count: sel.counts.moving,
        });
    }

    const hasPending = !sel.dragging && sel.pending && sel.count > 0;
    const unitCells = t("MarqueeDelete.UnitCells", "cells");

    const filterHintBanner = !hasAnyFilter ? (
        <div className={styles.importantWarnBanner}>
            <span>{t("MarqueeDelete.WarnNoFilters", "All filters are off — drag will select nothing.")}</span>
        </div>
    ) : underground && !hasUndergroundFilter ? (
        <div className={styles.infoHintBanner}>
            <span>
                {t(
                    "MarqueeDelete.WarnUndergroundNoFilter",
                    "Underground view: enable Roads/Nets or Vehicles to target pipes and tunnels."
                )}
            </span>
        </div>
    ) : null;

    const selectionNoticeBanner = (() => {
        if (!sel.notice) {
            return null;
        }
        if (sel.notice === "protected") {
            return (
                <div className={styles.importantWarnBanner}>
                    {t("MarqueeDelete.SelectionProtected", "This selection would remove a protected object. Adjust the area or filters.")}
                </div>
            );
        }
        if (sel.notice === "changed") {
            return (
                <div className={styles.importantWarnBanner}>
                    {t("MarqueeDelete.SelectionChanged", "The affected objects changed. Review the selection before confirming again.")}
                </div>
            );
        }
        if (sel.notice.startsWith("deleting|")) {
            const remaining = sel.notice.split("|")[1];
            return (
                <div className={styles.infoHintBanner}>
                    {t("MarqueeDelete.DeletionProgress", "Deleting in batches. {0} targets left. Esc stops.").replace("{0}", remaining)}
                </div>
            );
        }
        if (sel.notice === "resume_unavailable") {
            return (
                <div className={styles.infoHintBanner}>
                    {t("MarqueeDelete.ResumeUnavailable", "No recorded targets can be removed under the current filters and protections.")}
                </div>
            );
        }
        if (sel.notice === "deletion_blocked_compat") {
            return (
                <div className={styles.importantWarnBanner}>
                    {t(
                        "MarqueeDelete.DeletionBlockedCompat",
                        isZh
                            ? "当前游戏构建未验证，拆除不可用。"
                            : "This game build is not verified; demolition is unavailable."
                    )}
                </div>
            );
        }
        if (sel.notice === "deletion_observe_timeout") {
            return (
                <div className={styles.importantWarnBanner}>
                    {t(
                        "MarqueeDelete.DeletionObserveTimeout",
                        isZh
                            ? "等待拆除结果超时，已暂停后续提交。已提交的命令仍可能执行，请检查后再继续。"
                            : "Timed out waiting for demolition results; further submissions are paused. Already-submitted commands may still execute — please check before continuing."
                    )}
                </div>
            );
        }
        if (sel.notice === "deletion_stopped" || sel.notice === "deletion_changed") {
            return (
                <div className={styles.importantWarnBanner}>
                    {sel.notice === "deletion_changed"
                        ? t("MarqueeDelete.DeletionChanged", "The remaining targets changed. Review the area before continuing; already submitted deletions still apply.")
                        : t("MarqueeDelete.DeletionStopped", "Batch demolition stopped. You can undo submitted static assets.")}
                </div>
            );
        }
        if (sel.notice.startsWith("excluded|")) {
            const excludedCount = sel.notice.split("|")[1] || "0";
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.SelectionExcludedImportant",
                            "Excluded {0} important building(s). Review the remaining {1} object(s) and confirm to delete."
                        )
                            .replace("{0}", excludedCount)
                            .replace("{1}", String(sel.count))}
                    </span>
                </div>
            );
        }
        if (sel.notice.startsWith("excluded_all|")) {
            const excludedCount = sel.notice.split("|")[1] || "0";
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.SelectionExcludedAllImportant",
                            "Excluded all {0} important building(s); no remaining objects in selection."
                        ).replace("{0}", excludedCount)}
                    </span>
                </div>
            );
        }
        if (sel.notice.startsWith("hidden_retained|")) {
            const retainedCount = sel.notice.split("|")[1] || "0";
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.SelectionHiddenRetained",
                            "No matches under current filters, but {0} manually added object(s) are retained. Re-enable filters to restore, or use ESC / Clear to reset."
                        ).replace("{0}", retainedCount)}
                    </span>
                </div>
            );
        }
        if (sel.notice === "hidden_box") {
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.SelectionHiddenBox",
                            "No matches inside the locked marquee under current filters. Adjust filters to restore, or use ESC / Clear to reset."
                        )}
                    </span>
                </div>
            );
        }
        if (sel.notice.startsWith("similar_matched|")) {
            const matchedCount = sel.notice.slice("similar_matched|".length);
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.PickSimilarMatched",
                            "Kept {0} matching object(s) of the same type."
                        ).replace("{0}", matchedCount)}
                    </span>
                </div>
            );
        }
        if (sel.notice === "similar_no_target") {
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.PickSimilarNoTarget",
                            "No valid object under cursor. Click an object in the city."
                        )}
                    </span>
                </div>
            );
        }
        if (sel.notice === "similar_no_prefab") {
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.PickSimilarNoPrefab",
                            "Target object has no prefab metadata; cannot match same type."
                        )}
                    </span>
                </div>
            );
        }
        if (sel.notice === "similar_no_match") {
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.PickSimilarNoMatch",
                            "No matching objects of that type found in the current selection; selection preserved."
                        )}
                    </span>
                </div>
            );
        }
        if (sel.notice === "similar_blocked_polygon") {
            return (
                <div className={styles.infoHintBanner}>
                    <span>
                        {t(
                            "MarqueeDelete.PickSimilarBlockedPolygon",
                            "Match Same is unavailable while drawing a polygon — finish or cancel the polygon draft first."
                        )}
                    </span>
                </div>
            );
        }
        return null;
    })();

    const hasHiddenRetainedEdits =
        !sel.dragging && !hasPending && (sel.notice.startsWith("hidden_retained|") || sel.notice === "hidden_box");

    const isPolyDraft = sel.selectionMode === 1 && sel.polygonVertexCount > 0;
    const isFreehandLive = sel.freehandDrawing;
    const unitSqM = t("MarqueeDelete.UnitSqMeters", "m²");
    const unitVertices = t("MarqueeDelete.UnitVertices", "pts");

    const dimensionPillNode = (
        <MarqueeDimensionPill unitCells={unitCells} unitSqM={unitSqM} unitVertices={unitVertices} />
    );

    const polygonCapBanner = sel.polygonVertexLimitReached ? (
        <div className={styles.vertexCapBanner}>
            <span>
                {t(
                    "MarqueeDelete.PolygonVertexCapWarn",
                    "Polygon vertex cap ({0}) reached — close shape or undo a vertex."
                ).replace("{0}", String(sel.polygonVertexLimit || 64))}
            </span>
        </div>
    ) : null;

    const polygonSnapBanner =
        sel.polygonSnapActive && !sel.dragging ? (
            <div className={styles.polygonSnapBanner}>
                <span>
                    {t("MarqueeDelete.PolygonSnapReady", "Snapped to start — click or press Enter to close polygon")}
                </span>
            </div>
        ) : null;

    const shapeInvalidBanner =
        sel.shapeInvalid && !sel.dragging && !sel.freehandDrawing ? (
            <div className={styles.shapeInvalidBanner}>
                <span>
                    {t(
                        "MarqueeDelete.ShapeInvalidWarn",
                        "Selection area too small (< 1 m²) or degenerate — shape discarded."
                    )}
                </span>
            </div>
        ) : null;

    const shapeSimplifiedBanner =
        sel.shapeSimplified && sel.shapeOriginalVertexCount > 0 ? (
            <div className={styles.infoHintBanner}>
                <span>
                    {t("MarqueeDelete.ShapeSimplifiedNotice", "Simplified: {0} raw pts → {1} vertices")
                        .replace("{0}", String(sel.shapeOriginalVertexCount))
                        .replace("{1}", String(sel.polygonVertexCount))}
                </span>
            </div>
        ) : null;

    const hasInterruptedDeletion = sel.interruptedDeletionCount > 0;
    const directDelete = !sel.confirm && !hasInterruptedDeletion;

    React.useEffect(() => {
        if (!hasPending || sel.dragging || modalOpen) {
            return;
        }
        const onKeyDown = (e: KeyboardEvent) => {
            const target = e.target as HTMLElement;
            if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA")) {
                return;
            }
            if (e.key === "Enter" || e.code === "Enter" || e.code === "NumpadEnter") {
                if (sel.compatCanSubmit && !directDelete && !sel.undo.busy && sel.notice !== "protected") {
                    e.preventDefault();
                    e.stopPropagation();
                    if (!e.repeat) {
                        confirmDelete();
                    }
                }
            } else if (e.key === "Escape" || e.code === "Escape") {
                e.preventDefault();
                e.stopPropagation();
                if (!e.repeat) {
                    clearSelection();
                }
            }
        };
        window.addEventListener("keydown", onKeyDown, true);
        return () => window.removeEventListener("keydown", onKeyDown, true);
    }, [hasPending, sel.dragging, modalOpen, sel.compatCanSubmit, directDelete, sel.undo.busy, sel.notice]);
    const idleCompactHint = directDelete
        ? t(
              "MarqueeDelete.HintConfirmOffIdle",
              "Confirm OFF: Drag and release to delete directly (no secondary confirmation)"
          )
        : sel.selectionMode === 1
        ? t(
              "MarqueeDelete.HintCompactPolygonIdle",
              "Click to add vertices · Click start / Enter to close · Right-click/Backspace undo"
          )
        : sel.selectionMode === 2
        ? t(
              "MarqueeDelete.HintCompactFreehandIdle",
              "Hold left mouse button and drag to draw stroke · Release to close"
          )
        : t("MarqueeDelete.HintCompactIdle", "LMB Drag • RMB Exit");

    const cardBody = (
        <div className={styles.activeFooter}>
            <UndoStatusArea undo={sel.undo} compatCanUndo={sel.compatCanUndo} t={t} />
            {polygonCapBanner}
            {polygonSnapBanner}
            {shapeInvalidBanner}
            {shapeSimplifiedBanner}
            {hasInterruptedDeletion && !sel.dragging && !isFreehandLive && !isPolyDraft && (
                <div className={styles.interruptedCard}>
                    <span>
                        {sel.reviewingInterruptedDeletion
                            ? t("MarqueeDelete.ReviewingRemaining", "Review the remaining targets below. Only the targets shown below will be submitted when you confirm.")
                            : t("MarqueeDelete.InterruptedSummary", "Demolition paused. {0} recorded targets remain to review.").replace("{0}", String(sel.interruptedDeletionCount))}
                    </span>
                    <span className={styles.interruptedUnits}>
                        {t(
                            "MarqueeDelete.InterruptedUnitsSummary",
                            isZh
                                ? "剩余目标涉及 {0} 个删除单元，最大单元原含 {1} 个实体"
                                : "Remaining targets span {0} deletion units; the largest holds {1} entities",
                        )
                            .replace("{0}", String(sel.interruptedUnitCount))
                            .replace("{1}", String(sel.interruptedLargestUnit))}
                    </span>
                    <div className={styles.actionsRow}>
                        {!sel.reviewingInterruptedDeletion && (
                            <Button variant="flat" className={styles.secondaryBtn}
                                disabled={sel.undo.busy} focusKey={focusDisabledKey} onSelect={reviewInterruptedDeletion}>
                                {t("MarqueeDelete.ReviewRemainingBtn", "Review remaining")}
                            </Button>
                        )}
                        <Button variant="flat" className={styles.secondaryBtn}
                            focusKey={focusDisabledKey} onSelect={discardInterruptedDeletion}>
                            {t("MarqueeDelete.DiscardRemainingBtn", "Discard remainder")}
                        </Button>
                    </div>
                </div>
            )}
            {sel.restoreNotice && (
                <div
                    className={sel.restoreNotice === "restore_blocked" ? styles.importantWarnBanner : styles.infoHintBanner}
                    role="status"
                >
                    {sel.restoreNotice === "restore_blocked"
                        ? t("MarqueeDelete.SelectionRestoreBlocked", "Cannot restore boundary while busy, modal is open, or another operation is active.")
                        : t("MarqueeDelete.SelectionHistoryEmpty", "No saved marquee boundary in history.")}
                </div>
            )}

            {isPolyDraft && (
                <div className={styles.polygonDraftRow}>
                    <div className={styles.polygonDraftLeft}>
                        <span
                            className={
                                sel.polygonVertexLimitReached
                                    ? `${styles.polygonVertexPill} ${styles.polygonVertexPillCap}`
                                    : styles.polygonVertexPill
                            }
                        >
                            {t("MarqueeDelete.PolygonVerticesProgress", "Vertices: {0} / {1}")
                                .replace("{0}", String(sel.polygonVertexCount))
                                .replace("{1}", String(sel.polygonVertexLimit))}
                        </span>
                    </div>
                    <span className={styles.compactHint}>
                        {sel.polygonVertexCount < 3
                            ? t("MarqueeDelete.HintPolygonNeedMore", "Add at least {0} more vertex(es) before closing").replace(
                                  "{0}",
                                  String(3 - sel.polygonVertexCount)
                              )
                            : t(
                                  "MarqueeDelete.HintPolygonCloseReady",
                                  "Click start or press Enter to close · Right-click/Backspace undoes last vertex"
                              )}
                    </span>
                </div>
            )}

            {isFreehandLive && (
                <div className={styles.freehandDrawBar}>
                    <div className={styles.freehandBadge}>
                        <div className={styles.freehandDot} />
                        <span>{t("MarqueeDelete.StatusFreehandDrawing", "Drawing")}</span>
                        {dimensionPillNode}
                    </div>
                    <span className={styles.compactHint}>
                        {t(
                            "MarqueeDelete.HintCompactFreehandDraw",
                            "Keep dragging to outline the area · Release to close · Esc cancels"
                        )}
                    </span>
                </div>
            )}

            {!sel.dragging && !isFreehandLive && !hasPending && (
                <div>
                    {filterHintBanner}
                    {selectionNoticeBanner}
                    {!isPolyDraft && (
                        <div className={styles.idleBar}>
                            <div className={underground ? styles.layerBadgeUnderground : styles.layerBadge}>
                                <div className={underground ? styles.statusDotUnderground : styles.statusDot} />
                                <span>
                                    {underground
                                        ? t("MarqueeDelete.StatusReadyUnderground", "Underground Marquee Ready")
                                        : t("MarqueeDelete.StatusReadyGround", "Ground Marquee Ready")}
                                </span>
                            </div>
                            <span className={styles.compactHint}>{idleCompactHint}</span>
                        </div>
                    )}
                    {(hasHiddenRetainedEdits || (!sel.undo.inProgress && !sel.undo.busy && sel.undo.canUndo) || sel.selectionHistoryCount > 0) && (
                        <div className={styles.undoQuickRow}>
                            {hasHiddenRetainedEdits && (
                                <Button
                                    variant="flat"
                                    focusKey={focusDisabledKey}
                                    className={styles.secondaryBtn}
                                    onSelect={clearSelection}
                                >
                                    {t("MarqueeDelete.ClearBtn", "Clear")}
                                </Button>
                            )}
                            {!sel.undo.inProgress && !sel.undo.busy && sel.undo.canUndo && (
                                !sel.compatCanUndo ? (
                                    <Tooltip
                                        tooltip={renderRichTooltip(
                                            t("MarqueeDelete.UndoDisabledCompatTitle", isZh ? "撤回（已禁用）" : "Undo (Disabled)"),
                                            t("MarqueeDelete.UndoDisabledCompatDesc", isZh ? "当前游戏版本未验证，撤回已禁用。" : "Undo is disabled: target game version is unverified or rebuild components are missing.")
                                        )}
                                        delayTime={0}
                                    >
                                        <Button
                                            variant="flat"
                                            disabled={true}
                                            focusKey={focusDisabledKey}
                                            className={styles.undoQuickBtnDisabled}
                                        >
                                            {t("MarqueeDelete.UndoBtn", "Undo")} ({sel.undo.nextCount})
                                        </Button>
                                    </Tooltip>
                                ) : (
                                    <Button
                                        variant="flat"
                                        focusKey={focusDisabledKey}
                                        className={styles.undoQuickBtn}
                                        onSelect={undoLastDelete}
                                    >
                                        {t("MarqueeDelete.UndoBtn", "Undo")} ({sel.undo.nextCount})
                                    </Button>
                                )
                            )}
                            {sel.selectionHistoryCount > 0 && (
                                <Tooltip
                                    tooltip={renderRichTooltip(
                                        t("MarqueeDelete.RestoreHistoryBtn", "Restore"),
                                        t("MarqueeDelete.DescRestoreHistory", "Restore recent marquee boundary from history (up to 5 saved).")
                                    )}
                                    delayTime={0}
                                >
                                    <Button
                                        variant="flat"
                                        focusKey={focusDisabledKey}
                                        className={styles.secondaryBtn}
                                        onSelect={restoreLastSelection}
                                    >
                                        {t("MarqueeDelete.RestoreHistoryBtn", "Restore")} ({sel.selectionHistoryCount})
                                    </Button>
                                </Tooltip>
                            )}
                        </div>
                    )}
                </div>
            )}

            {sel.dragging && (
                <div>
                    {filterHintBanner}
                    <div className={styles.summaryHeader}>
                        <div className={styles.summaryLeft}>
                            <div className={underground ? styles.statusDotUnderground : styles.statusDot} />
                            <span className={styles.summaryMainText}>
                                {t("MarqueeDelete.StatusDragging", "Selecting")}: {sel.count}
                            </span>
                            {dimensionPillNode}
                            {sel.cascadeCount > sel.count && (
                                <span className={styles.cascadePill}>
                                    {sel.cascadeCount} {t("MarqueeDelete.WithChildrenSuffix", "total w/ children")}
                                </span>
                            )}
                        </div>
                        <span className={styles.compactHint}>
                            {directDelete
                                ? t(
                                      "MarqueeDelete.HintCompactDragDirect",
                                      "Release to delete immediately (Confirm OFF) • RMB Cancel"
                                  )
                                : t("MarqueeDelete.HintCompactDrag", "Release Lock • RMB Cancel")}
                        </span>
                    </div>
                    {sel.countImportant > 0 && (
                        <div className={styles.importantWarnBanner}>
                            <span>★ {t("MarqueeDelete.ImportantTag", "Important")}</span>
                            <span>{sel.countImportant}</span>
                        </div>
                    )}
                    {breakdownItems.length > 0 && (
                        <div className={styles.breakdownRow}>
                            {breakdownItems.map((item) => (
                                <div key={item.key} className={styles.breakdownTag}>
                                    {item.dotClass && <div className={item.dotClass} />}
                                    <span>{item.label}</span>
                                    <span className={styles.breakdownTagCount}>{item.count}</span>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            )}

            {hasPending && (
                <div>
                    <div className={styles.summaryHeader}>
                        <div className={styles.summaryLeft}>
                            <div className={styles.statusDotPending} />
                            <span className={styles.summaryMainText}>
                                {t("MarqueeDelete.SelectedLabel", "Selected")}: {sel.count}
                            </span>
                            {dimensionPillNode}
                            {sel.cascadeCount > sel.count && (
                                <span className={styles.cascadePill}>
                                    {sel.cascadeCount} {t("MarqueeDelete.WithChildrenSuffix", "total w/ children")}
                                </span>
                            )}
                            {sel.impactCalculating && (
                                <span className={styles.calculatingBadge}>
                                    {t("MarqueeDelete.CalculatingImpact", "Calculating impact...")}
                                </span>
                            )}
                        </div>
                        {sel.overThreshold && (
                            <span className={styles.warnPill}>
                                {directDelete
                                    ? t("MarqueeDelete.LargeDirectNotice", "Large selection (Direct delete)")
                                    : t("MarqueeDelete.LargeWarning", "Large selection")}
                            </span>
                        )}
                    </div>

                    {sel.countImportant > 0 && (
                        <div className={styles.statusAmberBadge}>
                            <span>
                                ★ {t("MarqueeDelete.ImportantProtectedSummary", "Contains {0} critical/protected building(s) (Confirm required)").replace("{0}", String(sel.countImportant))}
                            </span>
                        </div>
                    )}

                    {breakdownItems.length > 0 && (
                        <div className={styles.breakdownRow}>
                            {breakdownItems.map((item) => (
                                <div key={item.key} className={styles.breakdownTag}>
                                    {item.dotClass && <div className={item.dotClass} />}
                                    <span>{item.label}</span>
                                    <span className={styles.breakdownTagCount}>{item.count}</span>
                                </div>
                            ))}
                        </div>
                    )}

                    {sel.pickSimilarArmed && (
                        <div className={styles.pickSimilarBanner}>
                            {t(
                                "MarqueeDelete.PickSimilarArmedHint",
                                "Click an object in the world to keep only the same type inside selection (ESC / RMB to cancel)."
                            )}
                        </div>
                    )}
                    {selectionNoticeBanner}
                    {!sel.undo.busy && sel.notice !== "protected" &&
                        (!hasInterruptedDeletion || sel.reviewingInterruptedDeletion) && (
                        <div className={styles.primaryAction}>
                            {!sel.compatCanSubmit || sel.compatTier === "preview_only" ? (
                                <Button
                                    variant="flat"
                                    disabled={true}
                                    focusKey={focusDisabledKey}
                                    className={styles.confirmBtnPreviewOnly}
                                >
                                    {sel.compatTier === "preview_only"
                                        ? t("MarqueeDelete.PreviewOnlyNotice", "Preview only (Target game version pending verification)")
                                        : t("MarqueeDelete.CompatUnavailableNotice", "Deletion unavailable in this build")}
                                </Button>
                            ) : (
                                <Button
                                    variant="flat"
                                    focusKey={focusDisabledKey}
                                    className={styles.confirmBtn}
                                    onSelect={confirmDelete}
                                >
                                    {t("MarqueeDelete.ConfirmBtn", "Confirm deletion")} ({sel.count}) [Enter]
                                </Button>
                            )}
                        </div>
                    )}
                    <div className={styles.actionsRow}>
                        {!sel.undo.busy && !sel.reviewingInterruptedDeletion && (
                            <Tooltip
                                tooltip={renderRichTooltip(
                                    t("MarqueeDelete.PickSimilarBtn", "Match Same"),
                                    isPolyDraft
                                        ? t(
                                              "MarqueeDelete.PickSimilarBlockedPolygon",
                                              "Match Same is unavailable while drawing a polygon — finish or cancel the polygon draft first."
                                          )
                                        : t(
                                              "MarqueeDelete.DescPickSimilarBtn",
                                              "Arm one-shot pick mode, then click any object in the world to keep only objects of the same prefab and category inside the current selection."
                                          )
                                )}
                                delayTime={0}
                            >
                                <Button
                                    variant="flat"
                                    disabled={isPolyDraft}
                                    focusKey={focusDisabledKey}
                                    className={`${sel.pickSimilarArmed ? styles.pickSimilarBtnArmed : styles.pickSimilarBtn} ${isPolyDraft ? styles.pickSimilarBtnDisabled : ""}`}
                                    onSelect={() => {
                                        if (!isPolyDraft) {
                                            togglePickSimilar();
                                        }
                                    }}
                                >
                                    {t("MarqueeDelete.PickSimilarBtn", "Match Same")}
                                </Button>
                            </Tooltip>
                        )}
                        <Button
                            variant="flat"
                            focusKey={focusDisabledKey}
                            className={styles.secondaryBtn}
                            onSelect={clearSelection}
                        >
                            {t("MarqueeDelete.ClearBtn", "Clear")} [Esc]
                        </Button>
                        <Tooltip
                            tooltip={renderRichTooltip(
                                t("MarqueeDelete.ImpactReportBtn", "Impact Report"),
                                t("MarqueeDelete.DescImpactReport", "View grouped prefab impact breakdown before deleting.")
                            )}
                            delayTime={0}
                        >
                            <Button
                                variant="flat"
                                focusKey={focusDisabledKey}
                                className={styles.secondaryBtn}
                                onSelect={() => setImpactReportOpen(!sel.impactReportRaw)}
                            >
                                {t("MarqueeDelete.ImpactReportBtn", "Impact Report")}
                            </Button>
                        </Tooltip>
                        {!sel.undo.inProgress && !sel.undo.busy && sel.undo.canUndo && (
                            !sel.compatCanUndo ? (
                                <Tooltip
                                    tooltip={renderRichTooltip(
                                        t("MarqueeDelete.UndoDisabledCompatTitle", isZh ? "撤回（已禁用）" : "Undo (Disabled)"),
                                        t("MarqueeDelete.UndoDisabledCompatDesc", isZh ? "当前游戏版本未验证，撤回已禁用。" : "Undo is disabled: target game version is unverified or rebuild components are missing.")
                                    )}
                                    delayTime={0}
                                >
                                    <Button
                                        variant="flat"
                                        disabled={true}
                                        focusKey={focusDisabledKey}
                                        className={styles.undoBtnDisabled}
                                    >
                                        {t("MarqueeDelete.UndoBtn", "Undo")} ({sel.undo.nextCount})
                                    </Button>
                                </Tooltip>
                            ) : (
                                <Button
                                    variant="flat"
                                    focusKey={focusDisabledKey}
                                    className={styles.undoBtn}
                                    onSelect={undoLastDelete}
                                >
                                    {t("MarqueeDelete.UndoBtn", "Undo")} ({sel.undo.nextCount})
                                </Button>
                            )
                        )}
                        <Button
                            variant="flat"
                            focusKey={focusDisabledKey}
                            className={styles.secondaryBtn}
                            onSelect={switchFromBulldoze}
                        >
                            {t("MarqueeDelete.CloseBtn", "Single")}
                        </Button>
                    </div>
                    {!!sel.impactReportRaw && (
                        <ImpactReportCard
                            raw={sel.impactReportRaw}
                            t={t}
                            onClose={() => setImpactReportOpen(false)}
                        />
                    )}
                </div>
            )}
        </div>
    );

    if (FocusDisabledComp) {
        return <FocusDisabledComp disabled={true}>{cardBody}</FocusDisabledComp>;
    }
    return cardBody;
}

export function BulldozeMarqueeSection(): JSX.Element {
    const state = useMarqueeToolbarState();
    const palette = useColorPalette();
    const colorModalOpen = useColorModalOpen();
    const importantModalOpen = useValue(importantModalOpen$);
    const { translate } = useLocalization();
    const [netSubOpen, setNetSubOpen] = React.useState(false);
    const [modeInfoOpen, setModeInfoOpen] = React.useState(false);
    const [activeTab, setActiveTab] = React.useState<"targets" | "presets" | "options">("targets");

    const handleCategoryClick = (key: string, enabled: boolean, e?: any) => {
        const isAlt = (e && (e.altKey || e.nativeEvent?.altKey)) || (typeof window !== "undefined" && (window.event as any)?.altKey);
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

    const hasAnyFilter = !noneEnabled;
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
        {
            key: "netRoads",
            labelId: "MarqueeDelete.NetSubRoads",
            fallback: "Roads",
            descId: "MarqueeDelete.DescNetSubRoads",
            descFallback: "Include roads and pedestrian pathways.",
            enabled: state.filters.netRoads,
        },
        {
            key: "netTracks",
            labelId: "MarqueeDelete.NetSubTracks",
            fallback: "Tracks",
            descId: "MarqueeDelete.DescNetSubTracks",
            descFallback: "Include rail, tram, and subway tracks.",
            enabled: state.filters.netTracks,
        },
        {
            key: "netPipelines",
            labelId: "MarqueeDelete.NetSubPipelines",
            fallback: "Pipes",
            descId: "MarqueeDelete.DescNetSubPipelines",
            descFallback: "Include water and sewage pipelines.",
            enabled: state.filters.netPipelines,
        },
        {
            key: "netPowerLines",
            labelId: "MarqueeDelete.NetSubPowerLines",
            fallback: "Power",
            descId: "MarqueeDelete.DescNetSubPowerLines",
            descFallback: "Include overhead and underground power lines.",
            enabled: state.filters.netPowerLines,
        },
        {
            key: "netOther",
            labelId: "MarqueeDelete.NetSubOther",
            fallback: "Other Networks",
            descId: "MarqueeDelete.DescNetSubOther",
            descFallback: "Toggle waterways, fences, and unclassified networks inside the Networks filter.",
            enabled: state.filters.netOther,
        },
    ];

    const zoneSpecs: ZoneFilterSpec[] = [
        {
            key: "residential",
            labelId: "MarqueeDelete.FilterResidential",
            fallback: "Residential",
            descId: "MarqueeDelete.DescResidential",
            descFallback: "Include residential buildings (highlighted in green).",
            icon: ICON_RESIDENTIAL,
            colorBarClass: styles.zoneBarRes,
            colorHex: palette.buildingResidential,
            enabled: state.filters.residential,
            countBinding: countResidential$,
        },
        {
            key: "commercial",
            labelId: "MarqueeDelete.FilterCommercial",
            fallback: "Commercial",
            descId: "MarqueeDelete.DescCommercial",
            descFallback: "Include commercial buildings (highlighted in blue).",
            icon: ICON_COMMERCIAL,
            colorBarClass: styles.zoneBarCom,
            colorHex: palette.buildingCommercial,
            enabled: state.filters.commercial,
            countBinding: countCommercial$,
        },
        {
            key: "industrial",
            labelId: "MarqueeDelete.FilterIndustrial",
            fallback: "Industrial",
            descId: "MarqueeDelete.DescIndustrial",
            descFallback: "Include industrial, warehouse, and extractor buildings (highlighted in yellow).",
            icon: ICON_INDUSTRIAL,
            colorBarClass: styles.zoneBarInd,
            colorHex: palette.buildingIndustrial,
            enabled: state.filters.industrial,
            countBinding: countIndustrial$,
        },
        {
            key: "office",
            labelId: "MarqueeDelete.FilterOffice",
            fallback: "Office",
            descId: "MarqueeDelete.DescOffice",
            descFallback: "Include office buildings (highlighted in purple).",
            icon: ICON_OFFICE,
            colorBarClass: styles.zoneBarOff,
            colorHex: palette.buildingOffice,
            enabled: state.filters.office,
            countBinding: countOffice$,
        },
        {
            key: "buildings",
            labelId: "MarqueeDelete.FilterOtherBuildings",
            fallback: "Service/Other",
            descId: "MarqueeDelete.DescBuildings",
            descFallback: "Include standalone service, unique, signature, and other non-zoned buildings.",
            icon: ICON_SERVICES,
            colorBarClass: styles.zoneBarOther,
            colorHex: palette.buildingService,
            enabled: state.filters.buildings,
            countBinding: countBuildings$,
        },
    ];

    const assetSpecs: AssetFilterSpec[] = [
        {
            key: "networks",
            labelId: "MarqueeDelete.FilterNetworks",
            fallback: "Roads/Nets",
            descId: "MarqueeDelete.DescNetworks",
            descFallback: "Include roads, bridges, tunnels, tracks, pathways, pipes, and powerlines.",
            icon: ICON_NETWORKS,
            enabled: state.filters.networks,
            countBinding: countNetworks$,
        },
        {
            key: "trees",
            labelId: "MarqueeDelete.FilterTrees",
            fallback: "Trees",
            descId: "MarqueeDelete.DescTrees",
            descFallback: "Include standalone trees, bushes, and wild vegetation.",
            icon: ICON_TREES,
            enabled: state.filters.trees,
            countBinding: countTrees$,
        },
        {
            key: "props",
            labelId: "MarqueeDelete.FilterProps",
            fallback: "Props",
            descId: "MarqueeDelete.DescProps",
            descFallback: "Include standalone props, street furniture, benches, lamps, and boulders.",
            icon: ICON_PROPS,
            enabled: state.filters.props,
            countBinding: countProps$,
        },
        {
            key: "decals",
            labelId: "MarqueeDelete.FilterDecals",
            fallback: "Decals",
            descId: "MarqueeDelete.DescDecals",
            descFallback: "Include standalone ground decals, stains, and surface markings.",
            icon: ICON_DECALS,
            enabled: state.filters.decals,
            countBinding: countDecals$,
        },
        {
            key: "areas",
            labelId: "MarqueeDelete.FilterAreas",
            fallback: "Surfaces",
            descId: "MarqueeDelete.DescAreas",
            descFallback: "Include painted surfaces, spaces, and custom lots (map tiles & districts are always safe).",
            icon: ICON_AREAS,
            enabled: state.filters.areas,
            countBinding: countAreas$,
        },
        {
            key: "moving",
            labelId: "MarqueeDelete.FilterMoving",
            fallback: "Vehicles",
            descId: "MarqueeDelete.DescMoving",
            descFallback: "Include moving or parked vehicles, citizens, and animals inside the box.",
            icon: ICON_MOVING,
            enabled: state.filters.moving,
            countBinding: countMoving$,
        },
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
        {
            key: "undoBuildings",
            labelId: "MarqueeDelete.FilterBuildings",
            fallback: "Buildings",
            descId: "MarqueeDelete.UndoDescBuildings",
            descFallback: "Restore buildings together with their upgrades, attached areas, and internal networks. Badge shows total building-related records, not building count alone.",
            icon: ICON_BUILDINGS,
            enabled: state.undo.include.buildings,
            countBinding: undoCountBuildings$,
        },
        {
            key: "undoNetworks",
            labelId: "MarqueeDelete.FilterNetworks",
            fallback: "Roads/Nets",
            descId: "MarqueeDelete.UndoDescNetworks",
            descFallback: "Restore standalone roads, bridges, tunnels, tracks, pipes, and powerlines.",
            icon: ICON_NETWORKS,
            enabled: state.undo.include.networks,
            countBinding: undoCountNetworks$,
        },
        {
            key: "undoAreas",
            labelId: "MarqueeDelete.FilterAreas",
            fallback: "Surfaces",
            descId: "MarqueeDelete.UndoDescAreas",
            descFallback: "Restore standalone painted surfaces, spaces, and custom lots.",
            icon: ICON_AREAS,
            enabled: state.undo.include.areas,
            countBinding: undoCountAreas$,
        },
        {
            key: "undoTrees",
            labelId: "MarqueeDelete.FilterTrees",
            fallback: "Trees",
            descId: "MarqueeDelete.UndoDescTrees",
            descFallback: "Restore standalone trees and plants.",
            icon: ICON_TREES,
            enabled: state.undo.include.trees,
            countBinding: undoCountTrees$,
        },
        {
            key: "undoProps",
            labelId: "MarqueeDelete.FilterProps",
            fallback: "Props",
            descId: "MarqueeDelete.UndoDescProps",
            descFallback: "Restore standalone props and street furniture.",
            icon: ICON_PROPS,
            enabled: state.undo.include.props,
            countBinding: undoCountProps$,
        },
        {
            key: "undoDecals",
            labelId: "MarqueeDelete.FilterDecals",
            fallback: "Decals",
            descId: "MarqueeDelete.UndoDescDecals",
            descFallback: "Restore standalone decals and surface markings.",
            icon: ICON_DECALS,
            enabled: state.undo.include.decals,
            countBinding: undoCountDecals$,
        },
    ];

    const undoScopeLocked = state.undo.busy || state.undo.inProgress;
    const showUndoScope = state.undo.historyDepth > 0 || undoScopeLocked;
    const undoProcessed = state.undo.restoredCount + state.undo.skippedCount;

    const selectionModes: { mode: number; labelId: string; fallback: string; descId: string; descFallback: string }[] = [
        {
            mode: 0,
            labelId: "MarqueeDelete.SelectionModeRect",
            fallback: "Rect",
            descId: "MarqueeDelete.DescSelectionModeRect",
            descFallback: "Rectangle selection: drag to box-select, or click once to quick-pick a cell.",
        },
        {
            mode: 1,
            labelId: "MarqueeDelete.SelectionModePolygon",
            fallback: "Polygon",
            descId: "MarqueeDelete.DescSelectionModePolygon",
            descFallback: "Polygon selection: click to add vertices, click start or press Enter to close (max 64).",
        },
        {
            mode: 2,
            labelId: "MarqueeDelete.SelectionModeFreehand",
            fallback: "Stroke",
            descId: "MarqueeDelete.DescSelectionModeFreehand",
            descFallback: "Freehand stroke selection: hold left mouse button to draw, release to auto-close.",
        },
    ];

    const header = (
        <div className={styles.panelHeader}>
            <SectionWrapper title={t("MarqueeDelete.SelectionModeLabel", "Mode")}>
                <div className={styles.topToolbarRow}>
                    <div className={styles.modeSwitcherGroup}>
                        {selectionModes.map((item) => {
                            const isSelected = state.selectionMode === item.mode;
                            return (
                                <Tooltip
                                    key={item.mode}
                                    tooltip={renderRichTooltip(t(item.labelId, item.fallback), t(item.descId, item.descFallback))}
                                >
                                    <Button
                                        variant="flat"
                                        focusKey={vanillaRefs.FOCUS_DISABLED}
                                        selected={isSelected}
                                        className={
                                            isSelected
                                                ? `${styles.modeSwitcherBtn} ${styles.modeSwitcherBtnActive}`
                                                : styles.modeSwitcherBtn
                                        }
                                        onSelect={() => setSelectionMode(item.mode)}
                                    >
                                        {t(item.labelId, item.fallback)}
                                    </Button>
                                </Tooltip>
                            );
                        })}
                    </div>

                    <Button
                        variant="flat"
                        focusKey={vanillaRefs.FOCUS_DISABLED}
                        className={
                            modeInfoOpen
                                ? `${styles.modeInfoToggleBtn} ${styles.modeInfoToggleBtnActive}`
                                : styles.modeInfoToggleBtn
                        }
                        onSelect={() => setModeInfoOpen(!modeInfoOpen)}
                    >
                        ℹ {t("MarqueeDelete.ModeInfoBtn", "Details")}
                    </Button>
                </div>
            </SectionWrapper>

            <SectionWrapper title={t("MarqueeDelete.SectionTitle", "Bulk Delete")}>
                <ModeToolButton
                    selected={state.active}
                    src={marqueeIconUrl}
                    tooltipTitle={t("MarqueeDelete.Title", "Bulk Delete")}
                    tooltipDesc={t("MarqueeDelete.DescModeToggle", "Toggle between Marquee drag-box deletion and standard single-click Bulldozer (Hotkey: Ctrl+Shift+X).")}
                    onSelect={switchFromBulldoze}
                />
                <ModeToolButton
                    selected={state.undo.canUndo || state.undo.inProgress}
                    disabled={state.dragging || !state.compatCanUndo || (!state.undo.inProgress && !state.undo.canUndo)}
                    src={undoIconUrl}
                    tooltipTitle={
                        !state.compatCanUndo
                            ? t("MarqueeDelete.UndoDisabledCompatTitle", isZh ? "撤回（已禁用）" : "Undo (Disabled)")
                            : state.undo.inProgress
                            ? `${t("MarqueeDelete.UndoProgressLabel", "Creation requests submitted")} (${state.undo.restoredCount}/${state.undo.totalCount})`
                            : state.undo.canUndo
                            ? t("MarqueeDelete.UndoTooltip", "Undo Delete ({0})").replace("{0}", String(state.undo.nextCount))
                            : t("MarqueeDelete.UndoBtn", "Undo")
                    }
                    tooltipDesc={
                        !state.compatCanUndo
                            ? t("MarqueeDelete.UndoDisabledCompatDesc", isZh ? "当前游戏版本未验证，撤回已禁用。" : "Undo is disabled: target game version is unverified or rebuild components are missing.")
                            : state.undo.inProgress
                            ? `${t("MarqueeDelete.UndoPauseBtn", "Pause")} — ${t("MarqueeDelete.DescUndo", "Restore selected asset types from the latest deletion snapshot in small batches (Hotkey: Ctrl+Z).")}`
                            : state.undo.zeroMatch
                            ? t("MarqueeDelete.UndoZeroMatchHint", "Top undo snapshot has no records matching the selected undo categories ({0} snapshot(s) queued).").replace("{0}", String(state.undo.historyDepth))
                            : state.undo.canUndo
                            ? t("MarqueeDelete.DescUndo", "Restore selected asset types from the latest deletion snapshot in small batches (Hotkey: Ctrl+Z).")
                            : t("MarqueeDelete.UndoNothingTooltip", "Nothing to undo yet — delete objects with Bulk Delete first.")
                    }
                    onSelect={() => {
                        if (state.dragging || !state.compatCanUndo) {
                            return;
                        }
                        if (state.undo.inProgress) {
                            cancelUndo();
                        } else if (state.undo.canUndo) {
                            undoLastDelete();
                        }
                    }}
                >
                    {state.undo.inProgress && state.undo.totalCount > 0 && (
                        <div className={styles.undoActiveBadge}>
                            {Math.min(100, Math.round((undoProcessed / state.undo.totalCount) * 100))}%
                        </div>
                    )}
                    {!state.undo.inProgress && state.undo.canUndo && state.undo.nextCount > 0 && (
                        <div className={styles.undoBadge}>
                            {state.undo.nextCount > 999 ? "999+" : state.undo.nextCount}
                        </div>
                    )}
                </ModeToolButton>
                <ModeToolButton
                    selected={colorModalOpen}
                    src={colorPaletteIconUrl}
                    tooltipTitle={t("MarqueeDelete.ColorBtn", "Colors")}
                    tooltipDesc={t("MarqueeDelete.ColorTooltip", "Customize Marquee & Highlight Colors")}
                    onSelect={() => setColorModalOpen(!colorModalOpen)}
                />
            </SectionWrapper>

            <div className={styles.navTabRow}>
                <Button
                    variant="flat"
                    focusKey={focusDisabledKey}
                    className={`${styles.navTabBtn} ${activeTab === "targets" ? styles.navTabBtnActive : ""}`}
                    onSelect={() => setActiveTab("targets")}
                >
                    {t("MarqueeDelete.TabTargets", "Targets")}
                </Button>
                <Button
                    variant="flat"
                    focusKey={focusDisabledKey}
                    className={`${styles.navTabBtn} ${activeTab === "presets" ? styles.navTabBtnActive : ""}`}
                    onSelect={() => setActiveTab("presets")}
                >
                    {t("MarqueeDelete.PresetLabel", "Presets")}
                </Button>
                <Button
                    variant="flat"
                    focusKey={focusDisabledKey}
                    className={`${styles.navTabBtn} ${activeTab === "options" ? styles.navTabBtnActive : ""}`}
                    onSelect={() => setActiveTab("options")}
                >
                    {t("MarqueeDelete.TabOptions", "Options")}
                </Button>
            </div>
        </div>
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
                            focusKey={vanillaRefs.FOCUS_DISABLED}
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

                    <SectionWrapper title={t("MarqueeDelete.FiltersLabel", "Assets")}>
                        <ModeToolButton
                            selected={allEnabled}
                            src={STAR_ALL_ICON}
                            tooltipTitle={t("MarqueeDelete.SelectAllBtn", "All")}
                            tooltipDesc={t("MarqueeDelete.DescSelectAll", "Toggle all target asset categories on or restore defaults.")}
                            onSelect={() => setAllFilters(!allEnabled)}
                        />
                        <ModeToolButton
                            selected={noneEnabled}
                            src={clearFiltersIconUrl}
                            tooltipTitle={t("MarqueeDelete.ClearAllFiltersBtn", "Clear All")}
                            tooltipDesc={t("MarqueeDelete.DescClearAllFilters", "Disable all building zone and asset category filters so you can pick a single category cleanly.")}
                            onSelect={clearAllFilters}
                        />
                        {assetSpecs.map((spec) => (
                            <ModeToolButton
                                key={spec.key}
                                selected={spec.enabled}
                                src={spec.icon}
                                tooltipTitle={t(spec.labelId, spec.fallback)}
                                tooltipDesc={`${t(spec.descId, spec.descFallback)} (${t("MarqueeDelete.SoloCategoryHint", "Alt+Click: Solo this category")})`}
                                onSelect={(e?: any) => handleCategoryClick(spec.key, spec.enabled, e)}
                            >
                                {state.active && <FilterCountBadge binding={spec.countBinding} />}
                            </ModeToolButton>
                        ))}
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
                                {t("MarqueeDelete.NetSubExpandBtn", "Net Types")} {netSubOpen ? "▴" : "▾"}
                            </Button>
                        </Tooltip>
                    </SectionWrapper>

                    {netSubOpen && (
                        <div className={styles.netSubPanel}>
                            <div className={`${styles.netSubRow} ${!state.filters.networks ? styles.netSubDimmed : ""}`}>
                                {netSubSpecs.map((spec) => (
                                    <Tooltip
                                        key={spec.key}
                                        tooltip={renderRichTooltip(t(spec.labelId, spec.fallback), t(spec.descId, spec.descFallback))}
                                        delayTime={0}
                                    >
                                        <Button
                                            variant="flat"
                                            focusKey={focusDisabledKey}
                                            className={`${styles.netSubChip} ${spec.enabled ? styles.netSubChipActive : ""}`}
                                            onSelect={() => setOption(spec.key, !spec.enabled)}
                                        >
                                            {t(spec.labelId, spec.fallback)}
                                        </Button>
                                    </Tooltip>
                                ))}
                            </div>
                            {!state.filters.networks && (
                                <div className={styles.netSubMasterOffHint}>
                                    {t(
                                        "MarqueeDelete.NetSubMasterOffHint",
                                        "Master Networks filter is off — enable Networks above for sub-filters to match."
                                    )}
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
                            selected={state.options.protectOutside}
                            src={LOCK_ICON}
                            tooltipTitle={t("MarqueeDelete.OptionProtectOutside", "Safe Edge")}
                            tooltipDesc={t("MarqueeDelete.DescProtectOutside", "Automatically protect map-edge highway, rail, water, and power outside connections.")}
                            onSelect={() => setOption("protectOutside", !state.options.protectOutside)}
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

            {!state.active &&
                (state.undo.inProgress ||
                    state.undo.busy ||
                    !!state.undo.finishedNotice ||
                    state.undo.zeroMatch ||
                    state.undo.captureFailedCount > 0 ||
                    state.undo.notCaptured > 0 ||
                    (state.undo.counts.buildings > 0 && state.undo.historyDepth > 0)) && (
                <div className={styles.activeFooter}>
                    <UndoStatusArea undo={state.undo} compatCanUndo={state.compatCanUndo} t={t} />
                </div>
            )}
            {state.active && (
                <MarqueeActiveCard
                    underground={state.underground}
                    hasAnyFilter={hasAnyFilter}
                    hasUndergroundFilter={hasUndergroundFilter}
                    modalOpen={colorModalOpen || importantModalOpen}
                    t={t}
                />
            )}
        </div>
    );

    return (
        <React.Fragment>
            <div className={styles.marqueePanel}>
                {header}
                {content}
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

export function extendMouseToolOptions(Component: React.ComponentType<any>): (props: any) => JSX.Element {
    return function MarqueeMouseToolOptions(props: any): JSX.Element {
        const canUse = useValue(canUse$);
        const active = useValue(active$);
        const activeTool = useValue(tool.activeTool$);
        const id = activeTool ? activeTool.id : "";
        if (!canUse || (!active && id !== tool.BULLDOZE_TOOL && id !== "Marquee Delete")) {
            return <Component {...props} />;
        }

        return (
            <div className={styles.toolOptionsStack}>
                <div className={styles.originalToolOptions} onWheel={(event) => event.stopPropagation()}>
                    <Component {...props} />
                </div>
                <BulldozeMarqueeSection />
            </div>
        );
    };
}
