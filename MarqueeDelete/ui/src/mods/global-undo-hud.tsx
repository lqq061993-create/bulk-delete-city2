import React from "react";
import { useValue } from "cs2/api";
import { tool } from "cs2/bindings";
import { useLocalization } from "cs2/l10n";
import { Button, Tooltip } from "cs2/ui";
import {
    active$,
    canUse$,
    clearGlobalUndoHistory,
    dismissUndoNotice,
    GlobalUndoHistoryEntry,
    importantModalOpen$,
    optUndoFx$,
    setGlobalUndoPanelOpen,
    setOption,
    stopUndo,
    toggleGlobalUndoPanel,
    undoLastDelete,
    useColorModalOpen,
    useGlobalUndoState,
    useUndoState,
} from "./bindings";
import { UndoProgressBanner } from "./marquee-footer";
import {
    GEMINI_UNDO_ICON,
    GLOW_ICON,
    renderRichTooltip,
    translateUndoNoticeKey,
    UNDO_ICON,
} from "./marquee-shared";
import { vanillaRefs } from "./vanilla-refs";
import styles from "./global-undo-hud.module.scss";

function formatKindLabel(kind: string, t: (id: string, fallback: string) => string): string {
    switch (kind) {
        case "bulk_delete":
            return t("MarqueeDelete.GlobalUndoKindBulkDelete", "Bulk Delete");
        case "bulldoze":
            return t("MarqueeDelete.GlobalUndoKindBulldoze", "Bulldoze");
        case "build":
            return t("MarqueeDelete.GlobalUndoKindBuild", "Build");
        case "upgrade":
            return t("MarqueeDelete.GlobalUndoKindUpgrade", "Upgrade / Replace");
        case "zone":
            return t("MarqueeDelete.GlobalUndoKindZone", "Zoning");
        case "terrain":
            return t("MarqueeDelete.GlobalUndoKindTerrain", "Terrain");
        case "mixed":
            return t("MarqueeDelete.GlobalUndoKindMixed", "Mixed Action");
        default:
            return t("MarqueeDelete.GlobalUndoTitle", "Global Undo");
    }
}

function formatEntryDetail(entry: GlobalUndoHistoryEntry, t: (id: string, fallback: string) => string): string {
    const parts: string[] = [];
    const entityCount = entry.created + entry.deleted + entry.inPlace;
    if (entityCount > 0) {
        parts.push(`${entityCount} ${t("MarqueeDelete.GlobalUndoItemsUnit", "items")}`);
    }
    if (entry.zones > 0) {
        parts.push(`${entry.zones} ${t("MarqueeDelete.GlobalUndoZonesUnit", "blocks")}`);
    }
    if (entry.tiles > 0) {
        parts.push(`${entry.tiles} ${t("MarqueeDelete.GlobalUndoTilesUnit", "tiles")}`);
    }
    if (entry.terrainEvicted) {
        parts.push(t("MarqueeDelete.GlobalUndoTerrainEvictedTag", "Terrain trimmed"));
    }
    if (parts.length === 0) {
        parts.push(`${entry.count} ${t("MarqueeDelete.GlobalUndoItemsUnit", "items")}`);
    }
    return parts.join(" · ");
}

function formatTerrainMemory(kb: number): string {
    if (kb >= 1024) {
        return `${(kb / 1024).toFixed(1)} MB`;
    }
    return `${kb} KB`;
}

function isEditableElement(el: EventTarget | null): boolean {
    const node = el as HTMLElement | null;
    if (!node) {
        return false;
    }
    const tag = node.tagName ? node.tagName.toUpperCase() : "";
    return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || !!node.isContentEditable;
}

function formatRelativeTime(
    ts: number,
    nowSec: number,
    stepId: number,
    t: (id: string, fallback: string) => string
): string {
    if (!ts || ts <= 0) {
        return `#${stepId}`;
    }
    const diff = Math.max(0, nowSec - ts);
    if (diff < 5) {
        return t("MarqueeDelete.GlobalUndoTimeJustNow", "Just now");
    }
    if (diff < 60) {
        return t("MarqueeDelete.GlobalUndoTimeSeconds", "{0}s ago").replace("{0}", String(diff));
    }
    if (diff < 3600) {
        return t("MarqueeDelete.GlobalUndoTimeMinutes", "{0}m ago").replace("{0}", String(Math.floor(diff / 60)));
    }
    return t("MarqueeDelete.GlobalUndoTimeHours", "{0}h ago").replace("{0}", String(Math.floor(diff / 3600)));
}

export function GlobalUndoTopButton(): JSX.Element | null {
    const canUse = useValue(canUse$);
    const undo = useUndoState();
    const globalUndo = useGlobalUndoState();
    const { translate } = useLocalization();

    if (!canUse) {
        return null;
    }

    const t = (id: string, fallback: string): string => translate(id, fallback) ?? fallback;
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const isPaused = undo.busy && !undo.inProgress;
    const safeTotal = undo.totalCount > 0 ? undo.totalCount : 1;
    const pct = Math.min(100, Math.max(0, Math.round(((undo.restoredCount + undo.skippedCount) / safeTotal) * 100)));

    const tooltipTitle = t("MarqueeDelete.GlobalUndoTitle", "Global Undo");
    const baseDesc = t(
        "MarqueeDelete.GlobalUndoTopBtnTooltip",
        "Left-click: Open undo history panel • Right-click: Undo last action (Ctrl+Z)"
    );
    const tooltipDesc = globalUndo.yielding
        ? `${baseDesc}\n${t("MarqueeDelete.GlobalUndoYieldingBanner", "Ctrl+Z yielded to {0}").replace(
              "{0}",
              globalUndo.yieldingTool || "External Tool"
          )}`
        : baseDesc;

    const isActive = globalUndo.panelOpen || undo.busy;
    const btnClass = globalUndo.yielding
        ? styles.topBtnYielding
        : isActive
        ? styles.topBtnActive
        : styles.topBtn;

    const handleContextMenu = (e: React.MouseEvent) => {
        e.preventDefault();
        e.stopPropagation();
        if (undo.canUndo && undo.compatCanUndo && !globalUndo.yielding) {
            undoLastDelete();
        }
    };

    return (
        <div className={styles.topBtnWrap} onContextMenu={handleContextMenu}>
            <Tooltip tooltip={renderRichTooltip(tooltipTitle, tooltipDesc)} delayTime={0}>
                <Button
                    variant="floating"
                    src={GEMINI_UNDO_ICON}
                    selected={isActive}
                    focusKey={focusDisabledKey}
                    className={btnClass}
                    onSelect={toggleGlobalUndoPanel}
                />
            </Tooltip>
            {undo.inProgress ? (
                <div className={styles.topBtnBadgeBusy}>{pct}%</div>
            ) : isPaused ? (
                <div className={styles.topBtnBadgePaused}>||</div>
            ) : undo.historyDepth > 0 ? (
                <div className={styles.topBtnBadge}>{undo.historyDepth}</div>
            ) : null}
        </div>
    );
}

export function GlobalUndoOverlay(): JSX.Element | null {
    const canUse = useValue(canUse$);
    const marqueeActive = useValue(active$);
    const activeTool = useValue(tool.activeTool$);
    const undoFx = useValue(optUndoFx$);
    const importantModalOpen = useValue(importantModalOpen$);
    const colorModalOpen = useColorModalOpen();
    const undo = useUndoState();
    const globalUndo = useGlobalUndoState();
    const { translate } = useLocalization();

    const t = (id: string, fallback: string): string => translate(id, fallback) ?? fallback;
    const isZh = t("MarqueeDelete.UndoBtn", "Undo") !== "Undo";
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const isPaused = undo.busy && !undo.inProgress;
    const bulkPanelShowingUndo = marqueeActive || activeTool?.id === "Bulldoze Tool";
    const showStandaloneFloating =
        canUse &&
        !bulkPanelShowingUndo &&
        !globalUndo.panelOpen &&
        (undo.inProgress || isPaused || !!undo.finishedNotice);

    const [nowSec, setNowSec] = React.useState<number>(() => Math.floor(Date.now() / 1000));

    React.useEffect(() => {
        if (!globalUndo.panelOpen) {
            return;
        }
        setNowSec(Math.floor(Date.now() / 1000));
        const timer = window.setInterval(() => {
            setNowSec(Math.floor(Date.now() / 1000));
        }, 5000);
        return () => {
            window.clearInterval(timer);
        };
    }, [globalUndo.panelOpen]);

    React.useEffect(() => {
        if (!canUse || !globalUndo.panelOpen || importantModalOpen || colorModalOpen) {
            return;
        }
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.defaultPrevented || e.repeat) {
                return;
            }
            if (
                isEditableElement(e.target) ||
                (typeof document !== "undefined" && isEditableElement(document.activeElement))
            ) {
                return;
            }
            if (e.key === "Escape" || e.key === "Esc" || e.code === "Escape" || e.keyCode === 27) {
                e.preventDefault();
                e.stopPropagation();
                setGlobalUndoPanelOpen(false);
            }
        };
        window.addEventListener("keydown", onKeyDown);
        return () => {
            window.removeEventListener("keydown", onKeyDown);
        };
    }, [canUse, globalUndo.panelOpen, importantModalOpen, colorModalOpen]);

    React.useEffect(() => {
        if (!showStandaloneFloating || undo.inProgress || isPaused || undo.finishedNotice !== "UndoSubmitted") {
            return;
        }
        const timer = window.setTimeout(() => {
            dismissUndoNotice();
        }, 3500);
        return () => window.clearTimeout(timer);
    }, [showStandaloneFloating, undo.inProgress, isPaused, undo.finishedNotice]);

    if (!canUse) {
        return null;
    }

    if (!globalUndo.panelOpen && !showStandaloneFloating) {
        return null;
    }

    const categoryToggleSpecs: { key: string; label: string; enabled: boolean; raw: boolean }[] = [
        {
            key: "undoTrackBuild",
            label: t("MarqueeDelete.GlobalUndoTrackBuild", "Build"),
            enabled: globalUndo.enabled && globalUndo.trackBuild,
            raw: globalUndo.trackBuild,
        },
        {
            key: "undoTrackBulldoze",
            label: t("MarqueeDelete.GlobalUndoTrackBulldoze", "Bulldoze"),
            enabled: globalUndo.enabled && globalUndo.trackBulldoze,
            raw: globalUndo.trackBulldoze,
        },
        {
            key: "undoTrackUpgrade",
            label: t("MarqueeDelete.GlobalUndoTrackUpgrade", "Upgrade"),
            enabled: globalUndo.enabled && globalUndo.trackUpgrade,
            raw: globalUndo.trackUpgrade,
        },
        {
            key: "undoTrackZone",
            label: t("MarqueeDelete.GlobalUndoTrackZone", "Zone"),
            enabled: globalUndo.enabled && globalUndo.trackZone,
            raw: globalUndo.trackZone,
        },
        {
            key: "undoTrackTerrain",
            label: t("MarqueeDelete.GlobalUndoTrackTerrain", "Terrain"),
            enabled: globalUndo.enabled && globalUndo.trackTerrain,
            raw: globalUndo.trackTerrain,
        },
    ];

    const finishedSummary = undo.finishedNotice
        ? t("MarqueeDelete.UndoFinishedSummary", "Submitted {0} / {1}")
              .replace("{0}", String(undo.finishedRestoredCount))
              .replace("{1}", String(undo.finishedTotalCount))
        : "";

    return (
        <React.Fragment>
            {globalUndo.panelOpen && (
                <div className={styles.popoverAnchor}>
                    <div className={styles.popoverCard}>
                        <div className={styles.auroraStrip} />
                        <div className={styles.popoverHeader}>
                            <div className={styles.popoverTitleRow}>
                                <div className={styles.headerMedallion}>
                                    <img src={GEMINI_UNDO_ICON} className={styles.popoverTitleIcon} alt="" />
                                </div>
                                <span className={styles.popoverTitleText}>
                                    {t("MarqueeDelete.GlobalUndoTitle", "Global Undo")}
                                </span>
                                <img src={GLOW_ICON} className={styles.titleSparkle} alt="" />
                                <span className={styles.popoverCountPill}>{undo.historyDepth}</span>
                            </div>
                            <Button
                                variant="flat"
                                focusKey={focusDisabledKey}
                                className={styles.popoverCloseBtn}
                                onSelect={() => setGlobalUndoPanelOpen(false)}
                            >
                                ×
                            </Button>
                        </div>

                        <div className={styles.popoverBody}>
                            {globalUndo.yielding && (
                                <div className={styles.warnBanner}>
                                    {t("MarqueeDelete.GlobalUndoYieldingBanner", "Ctrl+Z yielded to {0}").replace(
                                        "{0}",
                                        globalUndo.yieldingTool || "Move It"
                                    )}
                                </div>
                            )}

                            {!undo.compatCanUndo && (
                                <div className={styles.warnBanner}>
                                    {t(
                                        "MarqueeDelete.UndoDisabledCompatDesc",
                                        "Undo is disabled on this game build: it has not been verified, or the rebuild components are missing."
                                    )}
                                </div>
                            )}

                            {undo.finishedNotice && (
                                <div className={styles.infoBanner}>
                                    <div className={styles.bannerTextCol}>
                                        <div className={styles.bannerMainText}>{translateUndoNoticeKey(undo.finishedNotice, t)}</div>
                                        <div className={styles.floatingSub}>{finishedSummary}</div>
                                    </div>
                                    <Button
                                        variant="flat"
                                        focusKey={focusDisabledKey}
                                        className={styles.dismissSmallBtn}
                                        onSelect={dismissUndoNotice}
                                    >
                                        {t("MarqueeDelete.UndoDismissBtn", "Dismiss")}
                                    </Button>
                                </div>
                            )}

                            {undo.inProgress && (
                                <div className={styles.popoverProgressWrap}>
                                    <UndoProgressBanner
                                        restoredCount={undo.restoredCount}
                                        skippedCount={undo.skippedCount}
                                        totalCount={undo.totalCount}
                                        undoFx={undoFx}
                                        t={t}
                                    />
                                </div>
                            )}

                            {!undo.inProgress && (
                                <div className={styles.actionRow}>
                                    {isPaused ? (
                                        <React.Fragment>
                                            <Button
                                                variant="flat"
                                                focusKey={focusDisabledKey}
                                                className={styles.primaryUndoBtn}
                                                onSelect={undoLastDelete}
                                            >
                                                <img src={UNDO_ICON} className={styles.actionBtnIcon} alt="" />
                                                <span>{t("MarqueeDelete.UndoResumeBtn", "Resume")}</span>
                                            </Button>
                                            <Button
                                                variant="flat"
                                                focusKey={focusDisabledKey}
                                                className={styles.dangerBtn}
                                                onSelect={stopUndo}
                                            >
                                                {t("MarqueeDelete.UndoStopBtn", "Stop")}
                                            </Button>
                                        </React.Fragment>
                                    ) : (
                                        <React.Fragment>
                                            <Button
                                                variant="flat"
                                                disabled={!undo.canUndo || !undo.compatCanUndo}
                                                focusKey={focusDisabledKey}
                                                className={
                                                    undo.canUndo && undo.compatCanUndo
                                                        ? styles.primaryUndoBtn
                                                        : styles.primaryUndoBtnDisabled
                                                }
                                                onSelect={() => {
                                                    if (undo.canUndo && undo.compatCanUndo) {
                                                        undoLastDelete();
                                                    }
                                                }}
                                            >
                                                <img src={UNDO_ICON} className={styles.actionBtnIcon} alt="" />
                                                <span>
                                                    {t("MarqueeDelete.GlobalUndoBtn", "Undo Last")}
                                                    {undo.nextCount > 0 ? ` (${undo.nextCount})` : ""}
                                                </span>
                                            </Button>
                                            <Button
                                                variant="flat"
                                                disabled={undo.historyDepth === 0 || undo.busy}
                                                focusKey={focusDisabledKey}
                                                className={
                                                    undo.historyDepth > 0 && !undo.busy
                                                        ? styles.secondaryBtn
                                                        : styles.secondaryBtnDisabled
                                                }
                                                onSelect={() => {
                                                    if (undo.historyDepth > 0 && !undo.busy) {
                                                        clearGlobalUndoHistory();
                                                    }
                                                }}
                                            >
                                                {t("MarqueeDelete.GlobalUndoClearBtn", "Clear")}
                                            </Button>
                                        </React.Fragment>
                                    )}
                                </div>
                            )}

                            <div className={styles.historyListWrap}>
                                <div className={styles.historyList}>
                                    {globalUndo.history.length === 0 ? (
                                        <div className={styles.historyEmpty}>
                                            <div className={styles.emptyStarRing}>
                                                <img src={GLOW_ICON} className={styles.emptyStarIcon} alt="" />
                                            </div>
                                            <span className={styles.emptyTitleText}>
                                                {t("MarqueeDelete.GlobalUndoEmpty", "No undo steps recorded yet.")}
                                            </span>
                                            <span className={styles.emptySubText}>
                                                {isZh
                                                    ? "建造、拆除、升级、分区或地形操作会自动记录"
                                                    : "Build, bulldoze, upgrade, zone, or terrain actions appear here"}
                                            </span>
                                        </div>
                                    ) : (
                                        globalUndo.history.map((entry, idx) => (
                                            <div
                                                key={entry.id}
                                                className={idx === 0 ? styles.historyRowTop : styles.historyRow}
                                            >
                                                <div className={styles.historyLeft}>
                                                    <span
                                                        className={
                                                            idx === 0 ? styles.historyIndexTagTop : styles.historyIndexTag
                                                        }
                                                    >
                                                        {idx === 0
                                                            ? t("MarqueeDelete.GlobalUndoLatestTag", "Next")
                                                            : `#${idx + 1}`}
                                                    </span>
                                                    <div className={styles.historyInfo}>
                                                        <span className={styles.historyKindText}>
                                                            {formatKindLabel(entry.kind, t)}
                                                        </span>
                                                        <span className={styles.historyMetaText}>
                                                            {formatEntryDetail(entry, t)}
                                                        </span>
                                                    </div>
                                                </div>
                                                <span className={styles.historyRight}>
                                                    {formatRelativeTime(entry.ts, nowSec, entry.id, t)}
                                                </span>
                                            </div>
                                        ))
                                    )}
                                </div>
                            </div>

                            <div className={styles.trackingSection}>
                                <div className={styles.trackingHeader}>
                                    <div className={styles.trackingTitleGroup}>
                                        <img src={GLOW_ICON} className={styles.trackingStarIcon} alt="" />
                                        <span className={styles.trackingLabel}>
                                            {t("MarqueeDelete.GlobalUndoTrackingLabel", "Tracking")}
                                        </span>
                                    </div>
                                    <div className={styles.trackingRightGroup}>
                                        {globalUndo.terrainMemKB > 0 && (
                                            <span className={styles.terrainMemNote}>
                                                {t("MarqueeDelete.GlobalUndoTerrainMemLabel", "Terrain cache")}:{" "}
                                                {formatTerrainMemory(globalUndo.terrainMemKB)}
                                            </span>
                                        )}
                                        <Button
                                            variant="flat"
                                            focusKey={focusDisabledKey}
                                            className={globalUndo.enabled ? styles.masterToggleOn : styles.masterToggleOff}
                                            onSelect={() => setOption("globalUndoEnabled", !globalUndo.enabled)}
                                        >
                                            <span className={globalUndo.enabled ? styles.chipDotOn : styles.chipDotOff} />
                                            <span>{t("MarqueeDelete.GlobalUndoMasterToggle", "Global Undo")}</span>
                                        </Button>
                                    </div>
                                </div>
                                <div className={styles.chipRow}>
                                    {categoryToggleSpecs.map((spec) => (
                                        <Button
                                            key={spec.key}
                                            variant="flat"
                                            focusKey={focusDisabledKey}
                                            className={spec.enabled ? styles.toggleChipOn : styles.toggleChipOff}
                                            onSelect={() => setOption(spec.key, !spec.raw)}
                                        >
                                            <span className={spec.enabled ? styles.chipDotOn : styles.chipDotOff} />
                                            <span>{spec.label}</span>
                                        </Button>
                                    ))}
                                </div>
                            </div>
                        </div>

                        <div className={styles.popoverFooter}>
                            <span className={styles.hotkeyHintFooter}>
                                {isZh ? "右键顶部图标快速撤回" : "Right-click top icon to undo"}
                            </span>
                            <span className={styles.keycapPill}>Ctrl+Z</span>
                        </div>
                    </div>
                </div>
            )}

            {showStandaloneFloating && (
                <div className={styles.floatingHost}>
                    <div className={styles.floatingCard}>
                        <div className={styles.auroraStrip} />
                        <div className={styles.floatingBody}>
                            {undo.inProgress ? (
                                <React.Fragment>
                                    <div className={styles.floatingHeader}>
                                        <div className={styles.floatingTitleGroup}>
                                            <img src={GLOW_ICON} className={styles.titleSparkle} alt="" />
                                            <span className={styles.floatingTitle}>
                                                {t("MarqueeDelete.GlobalUndoTitle", "Global Undo")} ·{" "}
                                                {formatKindLabel(globalUndo.activeKind, t)}
                                            </span>
                                        </div>
                                    </div>
                                    <UndoProgressBanner
                                        restoredCount={undo.restoredCount}
                                        skippedCount={undo.skippedCount}
                                        totalCount={undo.totalCount}
                                        undoFx={undoFx}
                                        t={t}
                                    />
                                </React.Fragment>
                            ) : isPaused ? (
                                <React.Fragment>
                                    <div className={styles.floatingHeader}>
                                        <div className={styles.floatingTitleGroup}>
                                            <img src={GLOW_ICON} className={styles.titleSparkle} alt="" />
                                            <span className={styles.floatingTitle}>
                                                {translateUndoNoticeKey(undo.notice || "UndoPaused", t)}
                                            </span>
                                        </div>
                                    </div>
                                    <div className={styles.actionRow}>
                                        <Button
                                            variant="flat"
                                            focusKey={focusDisabledKey}
                                            className={styles.primaryUndoBtn}
                                            onSelect={undoLastDelete}
                                        >
                                            <img src={UNDO_ICON} className={styles.actionBtnIcon} alt="" />
                                            <span>{t("MarqueeDelete.UndoResumeBtn", "Resume")}</span>
                                        </Button>
                                        <Button
                                            variant="flat"
                                            focusKey={focusDisabledKey}
                                            className={styles.dangerBtn}
                                            onSelect={stopUndo}
                                        >
                                            {t("MarqueeDelete.UndoStopBtn", "Stop")}
                                        </Button>
                                    </div>
                                </React.Fragment>
                            ) : (
                                <div className={styles.infoBanner} style={{ marginTop: 0 }}>
                                    <div className={styles.bannerTextCol}>
                                        <div className={styles.bannerMainText}>{translateUndoNoticeKey(undo.finishedNotice, t)}</div>
                                        <div className={styles.floatingSub}>{finishedSummary}</div>
                                    </div>
                                    <Button
                                        variant="flat"
                                        focusKey={focusDisabledKey}
                                        className={styles.dismissSmallBtn}
                                        onSelect={dismissUndoNotice}
                                    >
                                        {t("MarqueeDelete.UndoDismissBtn", "Dismiss")}
                                    </Button>
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            )}
        </React.Fragment>
    );
}
