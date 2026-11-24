import React from "react";
import { Button, Tooltip } from "cs2/ui";
import {
    cancelUndo,
    setColorModalOpen,
    setSelectionMode,
    switchFromBulldoze,
    undoLastDelete,
} from "./bindings";
import {
    COLOR_PALETTE_ICON,
    INFO_ICON,
    MARQUEE_ICON,
    ModeToolButton,
    renderRichTooltip,
    UNDO_ICON,
    UndoUiState,
} from "./marquee-shared";
import { vanillaRefs } from "./vanilla-refs";
import styles from "./bulldoze-marquee-section.module.scss";

export type PanelTabId = "targets" | "presets" | "options";

export interface MarqueeHeaderProps {
    active: boolean;
    dragging: boolean;
    selectionMode: number;
    compatCanUndo: boolean;
    colorModalOpen: boolean;
    modeInfoOpen: boolean;
    activeTab: PanelTabId;
    undo: UndoUiState;
    isZh: boolean;
    onToggleModeInfo: () => void;
    onChangeTab: (tab: PanelTabId) => void;
    t: (id: string, fallback: string) => string;
}

export function MarqueeHeader(props: MarqueeHeaderProps): JSX.Element {
    const {
        active,
        dragging,
        selectionMode,
        compatCanUndo,
        colorModalOpen,
        modeInfoOpen,
        activeTab,
        undo,
        isZh,
        onToggleModeInfo,
        onChangeTab,
        t,
    } = props;

    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const undoProcessed = undo.restoredCount + undo.skippedCount;

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

    return (
        <div className={styles.panelHeader}>
            <div className={styles.headerTopRow}>
                <div className={styles.headerLeftGroup}>
                    <ModeToolButton
                        selected={active}
                        src={MARQUEE_ICON}
                        tooltipTitle={t("MarqueeDelete.Title", "Bulk Delete")}
                        tooltipDesc={t(
                            "MarqueeDelete.DescModeToggle",
                            "Toggle between Marquee drag-box deletion and standard single-click Bulldozer (Hotkey: Ctrl+Shift+X)."
                        )}
                        onSelect={switchFromBulldoze}
                    />
                    <div className={styles.modeSwitcherGroup}>
                        {selectionModes.map((item) => {
                            const isSelected = selectionMode === item.mode;
                            return (
                                <Tooltip
                                    key={item.mode}
                                    tooltip={renderRichTooltip(
                                        t(item.labelId, item.fallback),
                                        t(item.descId, item.descFallback)
                                    )}
                                >
                                    <Button
                                        variant="flat"
                                        focusKey={focusDisabledKey}
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
                </div>

                <div className={styles.headerRightGroup}>
                    <ModeToolButton
                        selected={undo.canUndo || undo.inProgress}
                        disabled={dragging || !compatCanUndo || (!undo.inProgress && !undo.canUndo)}
                        src={UNDO_ICON}
                        tooltipTitle={
                            !compatCanUndo
                                ? t("MarqueeDelete.UndoDisabledCompatTitle", isZh ? "撤回（已禁用）" : "Undo (Disabled)")
                                : undo.inProgress
                                ? `${t("MarqueeDelete.UndoProgressLabel", "Creation requests submitted")} (${undo.restoredCount}/${undo.totalCount})`
                                : undo.canUndo
                                ? t("MarqueeDelete.UndoTooltip", "Undo Delete ({0})").replace("{0}", String(undo.nextCount))
                                : t("MarqueeDelete.UndoBtn", "Undo")
                        }
                        tooltipDesc={
                            !compatCanUndo
                                ? t(
                                      "MarqueeDelete.UndoDisabledCompatDesc",
                                      isZh
                                          ? "当前游戏版本未验证，撤回已禁用。"
                                          : "Undo is disabled: target game version is unverified or rebuild components are missing."
                                  )
                                : undo.inProgress
                                ? `${t("MarqueeDelete.UndoPauseBtn", "Pause")} — ${t(
                                      "MarqueeDelete.DescUndo",
                                      "Restore selected asset types from the latest deletion snapshot in small batches (Hotkey: Ctrl+Z)."
                                  )}`
                                : undo.zeroMatch
                                ? t(
                                      "MarqueeDelete.UndoZeroMatchHint",
                                      "Top undo snapshot has no records matching the selected undo categories ({0} snapshot(s) queued)."
                                  ).replace("{0}", String(undo.historyDepth))
                                : undo.canUndo
                                ? t(
                                      "MarqueeDelete.DescUndo",
                                      "Restore selected asset types from the latest deletion snapshot in small batches (Hotkey: Ctrl+Z)."
                                  )
                                : t("MarqueeDelete.UndoNothingTooltip", "Nothing to undo yet — delete objects with Bulk Delete first.")
                        }
                        onSelect={() => {
                            if (dragging || !compatCanUndo) {
                                return;
                            }
                            if (undo.inProgress) {
                                cancelUndo();
                            } else if (undo.canUndo) {
                                undoLastDelete();
                            }
                        }}
                    >
                        {undo.inProgress && undo.totalCount > 0 && (
                            <div className={styles.undoActiveBadge}>
                                {Math.min(100, Math.round((undoProcessed / undo.totalCount) * 100))}%
                            </div>
                        )}
                        {!undo.inProgress && undo.canUndo && undo.nextCount > 0 && (
                            <div className={styles.undoBadge}>
                                {undo.nextCount > 999 ? "999+" : undo.nextCount}
                            </div>
                        )}
                    </ModeToolButton>

                    <ModeToolButton
                        selected={colorModalOpen}
                        src={COLOR_PALETTE_ICON}
                        tooltipTitle={t("MarqueeDelete.ColorBtn", "Colors")}
                        tooltipDesc={t("MarqueeDelete.ColorTooltip", "Customize Marquee & Highlight Colors")}
                        onSelect={() => setColorModalOpen(!colorModalOpen)}
                    />

                    <Button
                        variant="flat"
                        focusKey={focusDisabledKey}
                        className={
                            modeInfoOpen
                                ? `${styles.modeInfoToggleBtn} ${styles.modeInfoToggleBtnActive}`
                                : styles.modeInfoToggleBtn
                        }
                        onSelect={onToggleModeInfo}
                    >
                        <img src={INFO_ICON} className={styles.inlineBtnIcon} alt="" />
                        <span>{t("MarqueeDelete.ModeInfoBtn", "Details")}</span>
                    </Button>
                </div>
            </div>

            <div className={styles.navTabRow}>
                <Button
                    variant="flat"
                    focusKey={focusDisabledKey}
                    className={`${styles.navTabBtn} ${activeTab === "targets" ? styles.navTabBtnActive : ""}`}
                    onSelect={() => onChangeTab("targets")}
                >
                    {t("MarqueeDelete.TabTargets", "Targets")}
                </Button>
                <Button
                    variant="flat"
                    focusKey={focusDisabledKey}
                    className={`${styles.navTabBtn} ${activeTab === "presets" ? styles.navTabBtnActive : ""}`}
                    onSelect={() => onChangeTab("presets")}
                >
                    {t("MarqueeDelete.PresetLabel", "Presets")}
                </Button>
                <Button
                    variant="flat"
                    focusKey={focusDisabledKey}
                    className={`${styles.navTabBtn} ${activeTab === "options" ? styles.navTabBtnActive : ""}`}
                    onSelect={() => onChangeTab("options")}
                >
                    {t("MarqueeDelete.TabOptions", "Options")}
                </Button>
            </div>
        </div>
    );
}
