import { bindValue, trigger, useValue } from "cs2/api";
import { ColorPaletteData, DEFAULT_COLOR_PALETTE } from "./color-palette";

const GROUP = "marqueedelete";

export const canUse$ = bindValue<boolean>(GROUP, "canUse", false);
export const roadFacilityMode$ = bindValue<number>(GROUP, "roadFacilityMode", 0);
export const roadFacilitiesBusy$ = bindValue<boolean>(GROUP, "roadFacilitiesBusy", false);
export const active$ = bindValue<boolean>(GROUP, "active", false);
export const dragging$ = bindValue<boolean>(GROUP, "dragging", false);
export const pending$ = bindValue<boolean>(GROUP, "pending", false);
export const count$ = bindValue<number>(GROUP, "count", 0);
export const cascadeCount$ = bindValue<number>(GROUP, "cascadeCount", 0);
export const threshold$ = bindValue<number>(GROUP, "threshold", 50);
export const overThreshold$ = bindValue<boolean>(GROUP, "overThreshold", false);
export const selectionNotice$ = bindValue<string>(GROUP, "selectionNotice", "");
export const restoreNotice$ = bindValue<string>(GROUP, "restoreNotice", "");

export const boxWidthCells$ = bindValue<number>(GROUP, "boxWidthCells", 0);
export const boxDepthCells$ = bindValue<number>(GROUP, "boxDepthCells", 0);
export const boxWidthMeters$ = bindValue<number>(GROUP, "boxWidthMeters", 0);
export const boxDepthMeters$ = bindValue<number>(GROUP, "boxDepthMeters", 0);
export const selectionMode$ = bindValue<number>(GROUP, "selectionMode", 0);
export const polygonVertexCount$ = bindValue<number>(GROUP, "polygonVertexCount", 0);
export const polygonSnapActive$ = bindValue<boolean>(GROUP, "polygonSnapActive", false);
export const polygonVertexLimitReached$ = bindValue<boolean>(GROUP, "polygonVertexLimitReached", false);
export const freehandDrawing$ = bindValue<boolean>(GROUP, "freehandDrawing", false);
export const shapeInvalid$ = bindValue<boolean>(GROUP, "shapeInvalid", false);
export const shapeMode$ = bindValue<number>(GROUP, "shapeMode", 0);
export const shapeAreaSqMeters$ = bindValue<number>(GROUP, "shapeAreaSqMeters", 0);
export const shapePerimeterMeters$ = bindValue<number>(GROUP, "shapePerimeterMeters", 0);
export const shapeVertexCount$ = bindValue<number>(GROUP, "shapeVertexCount", 0);
export const shapeEquivalentCells$ = bindValue<number>(GROUP, "shapeEquivalentCells", 0);
export const shapeOriginalVertexCount$ = bindValue<number>(GROUP, "shapeOriginalVertexCount", 0);
export const shapeSimplified$ = bindValue<boolean>(GROUP, "shapeSimplified", false);

export const widthLabelX$ = bindValue<number>(GROUP, "widthLabelX", 0.5);
export const widthLabelY$ = bindValue<number>(GROUP, "widthLabelY", 0.5);
export const depthLabelX$ = bindValue<number>(GROUP, "depthLabelX", 0.5);
export const depthLabelY$ = bindValue<number>(GROUP, "depthLabelY", 0.5);

export const countResidential$ = bindValue<number>(GROUP, "countResidential", 0);
export const countCommercial$ = bindValue<number>(GROUP, "countCommercial", 0);
export const countIndustrial$ = bindValue<number>(GROUP, "countIndustrial", 0);
export const countOffice$ = bindValue<number>(GROUP, "countOffice", 0);

export const countImportant$ = bindValue<number>(GROUP, "countImportant", 0);
export const countService$ = bindValue<number>(GROUP, "countService", 0);
export const countUnique$ = bindValue<number>(GROUP, "countUnique", 0);
export const countSignature$ = bindValue<number>(GROUP, "countSignature", 0);
export const importantModalOpen$ = bindValue<boolean>(GROUP, "importantModalOpen", false);
export const importantListRaw$ = bindValue<string>(GROUP, "importantListRaw", "");

export const countBuildings$ = bindValue<number>(GROUP, "countBuildings", 0);
export const countTrees$ = bindValue<number>(GROUP, "countTrees", 0);
export const countProps$ = bindValue<number>(GROUP, "countProps", 0);
export const countDecals$ = bindValue<number>(GROUP, "countDecals", 0);
export const countMoving$ = bindValue<number>(GROUP, "countMoving", 0);
export const countNetworks$ = bindValue<number>(GROUP, "countNetworks", 0);
export const countAreas$ = bindValue<number>(GROUP, "countAreas", 0);

export const filterResidential$ = bindValue<boolean>(GROUP, "filterResidential", true);
export const filterCommercial$ = bindValue<boolean>(GROUP, "filterCommercial", true);
export const filterIndustrial$ = bindValue<boolean>(GROUP, "filterIndustrial", true);
export const filterOffice$ = bindValue<boolean>(GROUP, "filterOffice", true);

export const filterBuildings$ = bindValue<boolean>(GROUP, "filterBuildings", true);
export const filterTrees$ = bindValue<boolean>(GROUP, "filterTrees", true);
export const filterProps$ = bindValue<boolean>(GROUP, "filterProps", true);
export const filterDecals$ = bindValue<boolean>(GROUP, "filterDecals", true);
export const filterMoving$ = bindValue<boolean>(GROUP, "filterMoving", false);
export const filterNetworks$ = bindValue<boolean>(GROUP, "filterNetworks", false);
export const filterAreas$ = bindValue<boolean>(GROUP, "filterAreas", false);
export const filterNetRoads$ = bindValue<boolean>(GROUP, "filterNetRoads", true);
export const filterNetTracks$ = bindValue<boolean>(GROUP, "filterNetTracks", true);
export const filterNetPipelines$ = bindValue<boolean>(GROUP, "filterNetPipelines", true);
export const filterNetPowerLines$ = bindValue<boolean>(GROUP, "filterNetPowerLines", true);
export const filterNetOther$ = bindValue<boolean>(GROUP, "filterNetOther", true);
export const activeFilterPreset$ = bindValue<string>(GROUP, "activeFilterPreset", "");
export const customPreset1Saved$ = bindValue<boolean>(GROUP, "customPreset1Saved", false);
export const customPreset2Saved$ = bindValue<boolean>(GROUP, "customPreset2Saved", false);
export const customPreset3Saved$ = bindValue<boolean>(GROUP, "customPreset3Saved", false);
export const pickSimilarArmed$ = bindValue<boolean>(GROUP, "pickSimilarArmed", false);
export const impactReportRaw$ = bindValue<string>(GROUP, "impactReportRaw", "");
export const selectionHistoryCount$ = bindValue<number>(GROUP, "selectionHistoryCount", 0);
export const interruptedDeletionCount$ = bindValue<number>(GROUP, "interruptedDeletionCount", 0);
export const interruptedUnitCount$ = bindValue<number>(GROUP, "interruptedUnitCount", 0);
export const interruptedLargestUnit$ = bindValue<number>(GROUP, "interruptedLargestUnit", 0);
export const interruptedDeletionUnitCount$ = interruptedUnitCount$;
export const interruptedDeletionLargestUnit$ = interruptedLargestUnit$;
export const reviewingInterruptedDeletion$ = bindValue<boolean>(GROUP, "reviewingInterruptedDeletion", false);
export const polygonVertexLimit$ = bindValue<number>(GROUP, "polygonVertexLimit", 64);

export const optConfirm$ = bindValue<boolean>(GROUP, "optConfirm", true);
export const optCameraAlign$ = bindValue<boolean>(GROUP, "optCameraAlign", true);
export const optProtectOutside$ = bindValue<boolean>(GROUP, "optProtectOutside", true);
export const underground$ = bindValue<boolean>(GROUP, "underground", false);
export const compatTier$ = bindValue<string>(GROUP, "compatTier", "verified");
export const compatCanSubmit$ = bindValue<boolean>(GROUP, "compatCanSubmit", true);
export const compatCanUndo$ = bindValue<boolean>(GROUP, "compatCanUndo", true);
export const impactCalculating$ = bindValue<boolean>(GROUP, "impactCalculating", false);

export const undoBusy$ = bindValue<boolean>(GROUP, "undoBusy", false);
export const undoNotice$ = bindValue<string>(GROUP, "undoNotice", "");
export const undoFinishedNotice$ = bindValue<string>(GROUP, "undoFinishedNotice", "");
export const canUndo$ = bindValue<boolean>(GROUP, "canUndo", false);
export const undoInProgress$ = bindValue<boolean>(GROUP, "undoInProgress", false);
export const undoNextCount$ = bindValue<number>(GROUP, "undoNextCount", 0);
export const undoRestoredCount$ = bindValue<number>(GROUP, "undoRestoredCount", 0);
export const undoTotalCount$ = bindValue<number>(GROUP, "undoTotalCount", 0);
export const undoNotCapturedCount$ = bindValue<number>(GROUP, "undoNotCapturedCount", 0);
export const undoIncludeBuildings$ = bindValue<boolean>(GROUP, "undoIncludeBuildings", true);
export const undoIncludeNetworks$ = bindValue<boolean>(GROUP, "undoIncludeNetworks", true);
export const undoIncludeAreas$ = bindValue<boolean>(GROUP, "undoIncludeAreas", true);
export const undoIncludeTrees$ = bindValue<boolean>(GROUP, "undoIncludeTrees", true);
export const undoIncludeProps$ = bindValue<boolean>(GROUP, "undoIncludeProps", true);
export const undoIncludeDecals$ = bindValue<boolean>(GROUP, "undoIncludeDecals", true);
export const undoCountBuildings$ = bindValue<number>(GROUP, "undoCountBuildings", 0);
export const undoCountNetworks$ = bindValue<number>(GROUP, "undoCountNetworks", 0);
export const undoCountAreas$ = bindValue<number>(GROUP, "undoCountAreas", 0);
export const undoCountTrees$ = bindValue<number>(GROUP, "undoCountTrees", 0);
export const undoCountProps$ = bindValue<number>(GROUP, "undoCountProps", 0);
export const undoCountDecals$ = bindValue<number>(GROUP, "undoCountDecals", 0);
export const undoHistoryDepth$ = bindValue<number>(GROUP, "undoHistoryDepth", 0);
export const undoSkippedCount$ = bindValue<number>(GROUP, "undoSkippedCount", 0);
export const undoZeroMatch$ = bindValue<boolean>(GROUP, "undoZeroMatch", false);
export const undoFinishedRestoredCount$ = bindValue<number>(GROUP, "undoFinishedRestoredCount", 0);
export const undoFinishedSkippedCount$ = bindValue<number>(GROUP, "undoFinishedSkippedCount", 0);
export const undoFinishedTotalCount$ = bindValue<number>(GROUP, "undoFinishedTotalCount", 0);
export const undoFinishedNotCapturedCount$ = bindValue<number>(GROUP, "undoFinishedNotCapturedCount", 0);
export const undoCaptureFailedCount$ = bindValue<number>(GROUP, "undoCaptureFailedCount", 0);
export const colorModalOpen$ = bindValue<boolean>(GROUP, "colorModalOpen", false);
export const colorPaletteJson$ = bindValue<string>(GROUP, "colorPaletteJson", "");

export function toggleTool(): void {
    trigger(GROUP, "toggle");
}

export function setSelectionMode(mode: number): void {
    trigger(GROUP, "setSelectionMode", mode);
}

export function toggleRoadFacilityMode(mode: number): void {
    trigger(GROUP, "toggleRoadFacilityMode", mode);
}

export function cycleSelectionMode(): void {
    trigger(GROUP, "cycleSelectionMode");
}

export function switchFromBulldoze(): void {
    trigger(GROUP, "switchFromBulldoze");
}

export function setAllFilters(enableAll: boolean): void {
    trigger(GROUP, "setAllFilters", enableAll);
}

export function clearAllFilters(): void {
    trigger(GROUP, "clearAllFilters");
}

export function applyFilterPreset(presetId: string): void {
    trigger(GROUP, "applyFilterPreset", presetId);
}

export function saveCustomPreset(slot: number): void {
    trigger(GROUP, "saveCustomPreset", slot);
}

export function clearCustomPreset(slot: number): void {
    trigger(GROUP, "clearCustomPreset", slot);
}

export function togglePickSimilar(): void {
    trigger(GROUP, "togglePickSimilar");
}

export function confirmDelete(): void {
    trigger(GROUP, "confirm");
}

export function confirmImportantDelete(): void {
    trigger(GROUP, "confirmImportant");
}

export function excludeImportant(): void {
    trigger(GROUP, "excludeImportant");
}

export function cancelImportantModal(): void {
    trigger(GROUP, "cancelImportantModal");
}

export function clearSelection(): void {
    trigger(GROUP, "clear");
}

export function restoreLastSelection(): void {
    trigger(GROUP, "restoreLastSelection");
}

export function reviewInterruptedDeletion(): void {
    trigger(GROUP, "reviewInterruptedDeletion");
}

export function discardInterruptedDeletion(): void {
    trigger(GROUP, "discardInterruptedDeletion");
}

export function setImpactReportOpen(open: boolean): void {
    trigger(GROUP, "setImpactReportOpen", open);
}

export function exitTool(): void {
    trigger(GROUP, "exit");
}

export function setOption(key: string, value: boolean): void {
    trigger(GROUP, "setOption", key, value);
}

export function soloFilter(key: string): void {
    trigger(GROUP, "soloFilter", key);
}

export function undoLastDelete(): void {
    trigger(GROUP, "undo");
}

export function cancelUndo(): void {
    trigger(GROUP, "cancelUndo");
}

export function stopUndo(): void {
    trigger(GROUP, "stopUndo");
}

export function dismissUndoNotice(): void {
    trigger(GROUP, "dismissUndoNotice");
}

export function setColorModalOpen(open: boolean): void {
    trigger(GROUP, "setColorModalOpen", open);
}

export function setColor(key: string, hex: string): void {
    trigger(GROUP, "setColor", key, hex);
}

export function setAlpha(key: string, value: number): void {
    trigger(GROUP, "setAlpha", key, value);
}

export function resetColors(): void {
    trigger(GROUP, "resetColors");
}

export function applyColorPreset(presetId: string): void {
    trigger(GROUP, "applyColorPreset", presetId);
}

export function useColorPalette(): ColorPaletteData {
    const raw = useValue(colorPaletteJson$);
    return raw ? JSON.parse(raw) as ColorPaletteData : DEFAULT_COLOR_PALETTE;
}

export function useColorModalOpen(): boolean {
    return useValue(colorModalOpen$);
}

export function useUndoState() {
    return {
        busy: useValue(undoBusy$),
        notice: useValue(undoNotice$),
        finishedNotice: useValue(undoFinishedNotice$),
        canUndo: useValue(canUndo$),
        compatCanUndo: useValue(compatCanUndo$),
        compatTier: useValue(compatTier$),
        inProgress: useValue(undoInProgress$),
        nextCount: useValue(undoNextCount$),
        restoredCount: useValue(undoRestoredCount$),
        skippedCount: useValue(undoSkippedCount$),
        totalCount: useValue(undoTotalCount$),
        notCaptured: useValue(undoNotCapturedCount$),
        historyDepth: useValue(undoHistoryDepth$),
        zeroMatch: useValue(undoZeroMatch$),
        finishedRestoredCount: useValue(undoFinishedRestoredCount$),
        finishedSkippedCount: useValue(undoFinishedSkippedCount$),
        finishedTotalCount: useValue(undoFinishedTotalCount$),
        finishedNotCaptured: useValue(undoFinishedNotCapturedCount$),
        captureFailedCount: useValue(undoCaptureFailedCount$),
        include: {
            buildings: useValue(undoIncludeBuildings$),
            networks: useValue(undoIncludeNetworks$),
            areas: useValue(undoIncludeAreas$),
            trees: useValue(undoIncludeTrees$),
            props: useValue(undoIncludeProps$),
            decals: useValue(undoIncludeDecals$),
        },
        counts: {
            buildings: useValue(undoCountBuildings$),
            networks: useValue(undoCountNetworks$),
            areas: useValue(undoCountAreas$),
            trees: useValue(undoCountTrees$),
            props: useValue(undoCountProps$),
            decals: useValue(undoCountDecals$),
        },
    };
}

export function useMarqueeToolbarState() {
    return {
        active: useValue(active$),
        dragging: useValue(dragging$),
        underground: useValue(underground$),
        selectionMode: useValue(selectionMode$),
        filters: {
            residential: useValue(filterResidential$),
            commercial: useValue(filterCommercial$),
            industrial: useValue(filterIndustrial$),
            office: useValue(filterOffice$),
            buildings: useValue(filterBuildings$),
            trees: useValue(filterTrees$),
            props: useValue(filterProps$),
            decals: useValue(filterDecals$),
            moving: useValue(filterMoving$),
            networks: useValue(filterNetworks$),
            areas: useValue(filterAreas$),
            netRoads: useValue(filterNetRoads$),
            netTracks: useValue(filterNetTracks$),
            netPipelines: useValue(filterNetPipelines$),
            netPowerLines: useValue(filterNetPowerLines$),
            netOther: useValue(filterNetOther$),
        },
        activeFilterPreset: useValue(activeFilterPreset$),
        customPresets: {
            slot1: useValue(customPreset1Saved$),
            slot2: useValue(customPreset2Saved$),
            slot3: useValue(customPreset3Saved$),
        },
        options: {
            confirm: useValue(optConfirm$),
            cameraAlign: useValue(optCameraAlign$),
            protectOutside: useValue(optProtectOutside$),
        },
        compatCanUndo: useValue(compatCanUndo$),
        undo: useUndoState(),
    };
}

export function useMarqueeDimensionsState() {
    return {
        dragging: useValue(dragging$),
        pending: useValue(pending$),
        freehandDrawing: useValue(freehandDrawing$),
        polygonVertexCount: useValue(polygonVertexCount$),
        boxWidthCells: useValue(boxWidthCells$),
        boxDepthCells: useValue(boxDepthCells$),
        boxWidthMeters: useValue(boxWidthMeters$),
        boxDepthMeters: useValue(boxDepthMeters$),
        widthLabelX: useValue(widthLabelX$),
        widthLabelY: useValue(widthLabelY$),
        depthLabelX: useValue(depthLabelX$),
        depthLabelY: useValue(depthLabelY$),
        shapeMode: useValue(shapeMode$),
        shapeAreaSqMeters: useValue(shapeAreaSqMeters$),
        shapePerimeterMeters: useValue(shapePerimeterMeters$),
        shapeVertexCount: useValue(shapeVertexCount$),
        shapeEquivalentCells: useValue(shapeEquivalentCells$),
        shapeOriginalVertexCount: useValue(shapeOriginalVertexCount$),
        shapeSimplified: useValue(shapeSimplified$),
    };
}

export function useMarqueeModalState() {
    return {
        importantListRaw: useValue(importantListRaw$),
        notice: useValue(selectionNotice$),
        count: useValue(count$),
        countImportant: useValue(countImportant$),
        countService: useValue(countService$),
        countUnique: useValue(countUnique$),
        countSignature: useValue(countSignature$),
        compatCanSubmit: useValue(compatCanSubmit$),
        compatTier: useValue(compatTier$),
    };
}

export function useMarqueeCardState() {
    return {
        dragging: useValue(dragging$),
        pending: useValue(pending$),
        selectionMode: useValue(selectionMode$),
        polygonVertexCount: useValue(polygonVertexCount$),
        polygonSnapActive: useValue(polygonSnapActive$),
        polygonVertexLimitReached: useValue(polygonVertexLimitReached$),
        polygonVertexLimit: useValue(polygonVertexLimit$),
        freehandDrawing: useValue(freehandDrawing$),
        shapeInvalid: useValue(shapeInvalid$),
        shapeSimplified: useValue(shapeSimplified$),
        shapeOriginalVertexCount: useValue(shapeOriginalVertexCount$),
        count: useValue(count$),
        cascadeCount: useValue(cascadeCount$),
        overThreshold: useValue(overThreshold$),
        notice: useValue(selectionNotice$),
        countImportant: useValue(countImportant$),
        pickSimilarArmed: useValue(pickSimilarArmed$),
        selectionHistoryCount: useValue(selectionHistoryCount$),
        interruptedDeletionCount: useValue(interruptedDeletionCount$),
        interruptedUnitCount: useValue(interruptedUnitCount$),
        interruptedLargestUnit: useValue(interruptedLargestUnit$),
        reviewingInterruptedDeletion: useValue(reviewingInterruptedDeletion$),
        impactReportRaw: useValue(impactReportRaw$),
        restoreNotice: useValue(restoreNotice$),
        confirm: useValue(optConfirm$),
        compatTier: useValue(compatTier$),
        compatCanSubmit: useValue(compatCanSubmit$),
        compatCanUndo: useValue(compatCanUndo$),
        impactCalculating: useValue(impactCalculating$),
        counts: {
            residential: useValue(countResidential$),
            commercial: useValue(countCommercial$),
            industrial: useValue(countIndustrial$),
            office: useValue(countOffice$),
            buildings: useValue(countBuildings$),
            networks: useValue(countNetworks$),
            trees: useValue(countTrees$),
            props: useValue(countProps$),
            decals: useValue(countDecals$),
            areas: useValue(countAreas$),
            moving: useValue(countMoving$),
        },
        undo: useUndoState(),
    };
}
