import React from "react";
import ReactDOM from "react-dom";
import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import {
    boxDepthCells$,
    boxDepthMeters$,
    boxWidthCells$,
    boxWidthMeters$,
    countBuildings$,
    shapeAreaSqMeters$,
    shapeMode$,
    shapePerimeterMeters$,
    shapeVertexCount$,
    useMarqueeDimensionsState,
} from "./bindings";
import clearFiltersIconUrl from "./clear-filters-icon.svg";
import chevronDownIconUrl from "./chevron-down.svg";
import chevronUpIconUrl from "./chevron-up.svg";
import colorPaletteIconUrl from "./color-palette-icon.svg";
import confirmIconUrl from "./confirm-icon.svg";
import geminiUndoIconUrl from "./gemini-undo-icon.svg";
import glowIconUrl from "./glow-icon.svg";
import infoIconUrl from "./info-icon.svg";
import lockIconUrl from "./lock-icon.svg";
import marqueeIconUrl from "./marquee-delete-icon.svg";
import skipReviewIconUrl from "./skip-review-icon.svg";
import starAllIconUrl from "./star-all-icon.svg";
import undoFxIconUrl from "./undo-fx-icon.svg";
import undoIconUrl from "./undo-icon.svg";
import { vanillaRefs } from "./vanilla-refs";
import styles from "./bulldoze-marquee-section.module.scss";

export const CAMERA_ICON = "Media/Game/Icons/Camera.svg";
export const CONFIRM_ICON = confirmIconUrl;
export const LOCK_ICON = lockIconUrl;
export const MARQUEE_ICON = marqueeIconUrl;
export const CLEAR_FILTERS_ICON = clearFiltersIconUrl;
export const COLOR_PALETTE_ICON = colorPaletteIconUrl;
export const UNDO_ICON = undoIconUrl;
export const GEMINI_UNDO_ICON = geminiUndoIconUrl;
export const UNDO_FX_ICON = undoFxIconUrl;
export const GLOW_ICON = glowIconUrl;
export const INFO_ICON = infoIconUrl;
export const CHEVRON_DOWN_ICON = chevronDownIconUrl;
export const CHEVRON_UP_ICON = chevronUpIconUrl;
export const SKIP_REVIEW_ICON = skipReviewIconUrl;
export const RESET_ICON = "Media/Game/Icons/NewUI/Reset_Button.svg";
export const STAR_ALL_ICON = starAllIconUrl;
export const ICON_RESIDENTIAL = "Media/Game/Icons/ZoneResidential.svg";
export const ICON_COMMERCIAL = "Media/Game/Icons/ZoneCommercial.svg";
export const ICON_INDUSTRIAL = "Media/Game/Icons/ZoneIndustrial.svg";
export const ICON_OFFICE = "Media/Game/Icons/ZoneOffice.svg";
export const ICON_SERVICES = "Media/Game/Icons/Services.svg";
export const ICON_BUILDINGS = "Media/Game/Icons/Services.svg";
export const ICON_NETWORKS = "Media/Game/Icons/Roads.svg";
export const ICON_TREES = "Media/Game/Icons/Trees.svg";
export const ICON_PROPS = "Media/Game/Icons/Props.svg";
export const ICON_DECALS = "Media/Game/Icons/PropsDecals.svg";
export const ICON_AREAS = "Media/Game/Icons/SurfaceAreaTool.svg";
export const ICON_MOVING = "Media/Game/Icons/GenericVehicle.svg";

/** Cohtml-safe HSL to rgba(...) converter (avoids hsl() quirks in Cohtml CSS engine). */
export function hslToRgbaCss(hDeg: number, sPct: number, lPct: number, alpha = 1): string {
    const h = ((hDeg % 360) + 360) % 360;
    const s = Math.max(0, Math.min(100, sPct)) / 100;
    const l = Math.max(0, Math.min(100, lPct)) / 100;
    const c = (1 - Math.abs(2 * l - 1)) * s;
    const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
    const m = l - c / 2;
    let r = 0;
    let g = 0;
    let b = 0;
    if (h < 60) { r = c; g = x; }
    else if (h < 120) { r = x; g = c; }
    else if (h < 180) { g = c; b = x; }
    else if (h < 240) { g = x; b = c; }
    else if (h < 300) { r = x; b = c; }
    else { r = c; b = x; }
    const R = Math.round((r + m) * 255);
    const G = Math.round((g + m) * 255);
    const B = Math.round((b + m) * 255);
    return alpha >= 0.999 ? `rgb(${R}, ${G}, ${B})` : `rgba(${R}, ${G}, ${B}, ${alpha.toFixed(3)})`;
}

export interface ZoneFilterSpec {
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

export interface AssetFilterSpec {
    key: "networks" | "trees" | "props" | "decals" | "areas" | "moving";
    labelId: string;
    fallback: string;
    descId: string;
    descFallback: string;
    icon: string;
    enabled: boolean;
    countBinding: typeof countBuildings$;
}

export interface UndoUiState {
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

export function renderRichTooltip(title: string, desc: string): JSX.Element {
    const theme = vanillaRefs.descriptionTooltipTheme;
    return (
        <div className={styles.tooltipContainer}>
            <div className={theme?.title ?? styles.tooltipTitle}>{title}</div>
            <div className={theme?.content ?? styles.tooltipDesc}>{desc}</div>
        </div>
    );
}

export function SectionWrapper(props: {
    title: string;
    headerAccessory?: React.ReactNode;
    children: React.ReactNode;
}): JSX.Element {
    const FocusDisabledComp = vanillaRefs.FocusDisabled;
    const inner = FocusDisabledComp ? (
        <FocusDisabledComp disabled={true}>{props.children}</FocusDisabledComp>
    ) : (
        props.children
    );

    return (
        <div className={styles.panelSection}>
            <div className={styles.sectionHeaderRow}>
                <div className={styles.sectionTitle}>{props.title}</div>
                {props.headerAccessory && (
                    <div className={styles.sectionHeaderAccessory}>{props.headerAccessory}</div>
                )}
            </div>
            <div className={styles.sectionControls}>{inner}</div>
        </div>
    );
}

export function ModeToolButton(props: {
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

export function FilterCountBadge(props: { binding: typeof countBuildings$ }): JSX.Element | null {
    const count = useValue(props.binding);
    if (count <= 0) {
        return null;
    }
    return <div className={styles.countBadge}>{count > 999 ? "999+" : count}</div>;
}

export function MarqueeDimensionPill(props: {
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

export function BorderDimensionLabels(props: {
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

const UNDO_NOTICE_FALLBACKS: Record<string, string> = {
    UndoGeometryOnly: "Undo rebuilds geometry, not households or simulation state.",
    UndoPartial: "Only part of this deletion was recorded (8,000-record snapshot limit).",
    UndoPaused: "Undo paused; deletion is locked until resumed. Click Resume or Undo to continue.",
    UndoWaitingOwner: "Waiting for parent buildings to become ready...",
    UndoError: "Undo stopped due to an error; remaining objects were not rebuilt. Reload your save to recover.",
    UndoStalled:
        "Creation requests submitted, but some attached objects were skipped after waiting too long for parent buildings and remain unbuilt. Reload your save to recover; actual rebuilding is not verified.",
    UndoSubmitted:
        "All creation requests have been submitted. Whether the game rebuilds them depends on simulation and is not verified.",
    UndoSubmittedPartial:
        "Creation requests submitted with omissions (some items were skipped or not recorded). Whether the game rebuilds them depends on simulation and is not verified.",
    UndoStopped: "Undo stopped. The remaining items were not submitted for rebuild.",
};

export function translateUndoNoticeKey(key: string, t: (id: string, fallback: string) => string): string {
    return t("MarqueeDelete." + key, UNDO_NOTICE_FALLBACKS[key] ?? key);
}

export function roadFacilityLabel(mode: number, t: (id: string, fallback: string) => string): string {
    return mode === 1
        ? t("MarqueeDelete.RoadTrafficLights", "Traffic lights")
        : t("MarqueeDelete.RoadCrosswalks", "Crosswalks");
}
