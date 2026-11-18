import React, { useEffect, useRef, useState } from "react";
import ReactDOM from "react-dom";
import { Button } from "cs2/ui";
import {
    applyColorPreset,
    resetColors,
    setAlpha,
    setColor,
    setColorModalOpen,
    useColorPalette,
} from "./bindings";
import {
    ALPHA_ITEMS,
    AlphaKey,
    COLOR_GROUPS,
    ColorKey,
    hexToRgb,
    PRESET_THEMES,
    QUICK_SWATCHES,
    rgbToHex,
} from "./color-palette";
import { vanillaRefs } from "./vanilla-refs";
import styles from "./bulldoze-marquee-section.module.scss";

interface DivSliderProps {
    label: string;
    valueText: string;
    ratio: number;
    fillGradient: string;
    thumbColor?: string;
    onChangeRatio: (nextRatio: number) => void;
    onStep: (delta: number) => void;
    accentBadge?: string;
}

function clamp01(value: number): number {
    if (!Number.isFinite(value)) {
        return 0;
    }
    return Math.max(0, Math.min(1, value));
}

function onPrimaryMouseDown(e: React.MouseEvent, action: () => void): void {
    e.stopPropagation();
    if (e.button !== 0) {
        return;
    }
    e.preventDefault();
    action();
}

/**
 * Pure pointer-tracked <div> slider (zero <input> tags so Cohtml never acquires
 * text-input focus or leaks WASD/A-F keys to the camera).
 */
function DivSlider(props: DivSliderProps): JSX.Element {
    const {
        label,
        valueText,
        ratio,
        fillGradient,
        thumbColor,
        onChangeRatio,
        onStep,
        accentBadge,
    } = props;
    const trackRef = useRef<HTMLDivElement | null>(null);
    const draggingRef = useRef(false);
    const onChangeRef = useRef(onChangeRatio);
    onChangeRef.current = onChangeRatio;
    const safeRatio = clamp01(ratio);

    const computeAndEmit = (clientX: number) => {
        const track = trackRef.current;
        if (!track) {
            return;
        }
        const rect = track.getBoundingClientRect();
        if (rect.width <= 0) {
            return;
        }
        const next = clamp01((clientX - rect.left) / rect.width);
        onChangeRef.current(next);
    };

    useEffect(() => {
        const stopSliderDrag = () => {
            draggingRef.current = false;
        };
        const handleMove = (e: MouseEvent) => {
            if (!draggingRef.current) {
                return;
            }
            if ((e.buttons & 1) === 0) {
                draggingRef.current = false;
                return;
            }
            e.stopPropagation();
            e.preventDefault();
            computeAndEmit(e.clientX);
        };
        const handleVisibility = () => {
            if (typeof document !== "undefined" && document.hidden) {
                draggingRef.current = false;
            }
        };

        window.addEventListener("mousemove", handleMove);
        window.addEventListener("mouseup", stopSliderDrag);
        window.addEventListener("blur", stopSliderDrag);
        document.addEventListener("visibilitychange", handleVisibility);
        return () => {
            draggingRef.current = false;
            window.removeEventListener("mousemove", handleMove);
            window.removeEventListener("mouseup", stopSliderDrag);
            window.removeEventListener("blur", stopSliderDrag);
            document.removeEventListener("visibilitychange", handleVisibility);
        };
    }, []);

    const handleMouseDown = (e: React.MouseEvent<HTMLDivElement>) => {
        if (e.button !== 0) {
            return;
        }
        e.preventDefault();
        e.stopPropagation();
        draggingRef.current = true;
        computeAndEmit(e.clientX);
    };

    return (
        <div className={styles.colorSliderRow}>
            <div className={styles.colorSliderMeta}>
                <div className={styles.colorSliderLabelGroup}>
                    <span className={styles.colorSliderLabel}>{label}</span>
                    {accentBadge && <span className={styles.colorZeroBadge}>{accentBadge}</span>}
                </div>
                <span className={styles.colorSliderValue}>{valueText}</span>
            </div>
            <div className={styles.colorSliderControlRow}>
                <div
                    role="button"
                    className={styles.colorStepBtn}
                    onMouseDown={(e) => onPrimaryMouseDown(e, () => onStep(-1))}
                >
                    −
                </div>
                <div
                    ref={trackRef}
                    className={styles.colorSliderTrack}
                    style={{ backgroundImage: fillGradient }}
                    onMouseDown={handleMouseDown}
                >
                    <div
                        className={styles.colorSliderFill}
                        style={{ width: `${Math.round(safeRatio * 1000) / 10}%` }}
                    />
                    <div
                        className={styles.colorSliderThumb}
                        style={{
                            left: `${Math.round(safeRatio * 1000) / 10}%`,
                            backgroundColor: thumbColor || "#ffffff",
                        }}
                    />
                </div>
                <div
                    role="button"
                    className={styles.colorStepBtn}
                    onMouseDown={(e) => onPrimaryMouseDown(e, () => onStep(1))}
                >
                    +
                </div>
            </div>
        </div>
    );
}

const VIEWPORT_MARGIN_PX = 8;

function positionCard(
    card: HTMLDivElement,
    left: number,
    top: number,
    width: number,
    height: number
): void {
    const margin = VIEWPORT_MARGIN_PX;
    card.style.left = `${Math.round(Math.max(margin, Math.min(window.innerWidth - width - margin, left)))}px`;
    card.style.top = `${Math.round(Math.max(margin, Math.min(window.innerHeight - height - margin, top)))}px`;
    card.style.right = "auto";
}

export function ColorPaletteModal(props: {
    t: (id: string, fallback: string) => string;
}): JSX.Element {
    const { t } = props;
    const palette = useColorPalette();
    const [selectedKey, setSelectedKey] = useState<ColorKey>("marqueeGroundBorder");
    const [isHeaderDragging, setIsHeaderDragging] = useState(false);
    const cardRef = useRef<HTMLDivElement | null>(null);
    const dragRef = useRef<{
        offsetX: number;
        offsetY: number;
        width: number;
        height: number;
    } | null>(null);
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;

    useEffect(() => {
        const card = cardRef.current!;
        let frame = 0;
        let nextLeft = 0;
        let nextTop = 0;
        const endDrag = () => {
            if (!dragRef.current) {
                return;
            }
            dragRef.current = null;
            setIsHeaderDragging(false);
        };

        const handleWindowMove = (e: MouseEvent) => {
            const drag = dragRef.current;
            if (!drag) {
                return;
            }
            if ((e.buttons & 1) === 0) {
                endDrag();
                return;
            }
            e.stopPropagation();
            e.preventDefault();
            nextLeft = e.clientX - drag.offsetX;
            nextTop = e.clientY - drag.offsetY;
            if (!frame) {
                frame = window.requestAnimationFrame(() => {
                    frame = 0;
                    positionCard(card, nextLeft, nextTop, drag.width, drag.height);
                });
            }
        };

        const handleResize = () => {
            if (!card.style.left) {
                return;
            }
            endDrag();
            window.cancelAnimationFrame(frame);
            frame = 0;
            const rect = card.getBoundingClientRect();
            positionCard(card, rect.left, rect.top, rect.width, rect.height);
        };

        const handleVisibilityChange = () => {
            if (typeof document !== "undefined" && document.hidden) {
                endDrag();
            }
        };

        window.addEventListener("mousemove", handleWindowMove);
        window.addEventListener("mouseup", endDrag);
        window.addEventListener("blur", endDrag);
        window.addEventListener("resize", handleResize);
        document.addEventListener("visibilitychange", handleVisibilityChange);
        return () => {
            dragRef.current = null;
            window.cancelAnimationFrame(frame);
            window.removeEventListener("mousemove", handleWindowMove);
            window.removeEventListener("mouseup", endDrag);
            window.removeEventListener("blur", endDrag);
            window.removeEventListener("resize", handleResize);
            document.removeEventListener("visibilitychange", handleVisibilityChange);
        };
    }, []);

    const handleHeaderMouseDown = (e: React.MouseEvent<HTMLDivElement>) => {
        if (e.button !== 0) {
            return;
        }
        e.preventDefault();
        e.stopPropagation();
        const card = cardRef.current;
        if (!card) {
            return;
        }
        const rect = card.getBoundingClientRect();
        dragRef.current = {
            offsetX: e.clientX - rect.left,
            offsetY: e.clientY - rect.top,
            width: rect.width,
            height: rect.height,
        };
        setIsHeaderDragging(true);
        positionCard(card, rect.left, rect.top, rect.width, rect.height);
    };

    const activeHex = palette[selectedKey];
    const rgb = hexToRgb(activeHex);

    let activeLabel = selectedKey as string;
    for (let g = 0; g < COLOR_GROUPS.length; g++) {
        const group = COLOR_GROUPS[g];
        for (let i = 0; i < group.items.length; i++) {
            const item = group.items[i];
            if (item.key === selectedKey) {
                activeLabel = t(item.labelKey, item.fallback);
                break;
            }
        }
    }

    const emitRgbChannel = (channel: "r" | "g" | "b", nextVal: number) => {
        const clamped = Math.max(0, Math.min(255, Math.round(nextVal)));
        const r = channel === "r" ? clamped : rgb.r;
        const g = channel === "g" ? clamped : rgb.g;
        const b = channel === "b" ? clamped : rgb.b;
        setColor(selectedKey, rgbToHex(r, g, b));
    };

    const emitAlphaRatio = (key: AlphaKey, nextRatio: number) => {
        const pct = Math.max(0, Math.min(100, Math.round(nextRatio * 100)));
        setAlpha(key, pct / 100);
    };

    const modalNode = (
        <div
            ref={cardRef}
            className={styles.colorDockedCard}
            onMouseDown={(e) => e.stopPropagation()}
        >
            <div
                className={`${styles.colorHeader} ${isHeaderDragging ? styles.colorHeaderDragging : ""}`}
                onMouseDown={handleHeaderMouseDown}
            >
                <div className={styles.colorHeaderText}>
                    <div className={styles.colorTitle}>
                        {t("MarqueeDelete.ColorModalTitle", "Color Palette")}
                    </div>
                    <div className={styles.colorSubtitle}>
                        {t("MarqueeDelete.ColorModalSubtitle", "Live preview on ground — changes apply immediately.")}
                    </div>
                </div>
                <div
                    role="button"
                    className={styles.colorCloseIconBtn}
                    onMouseDown={(e) => onPrimaryMouseDown(e, () => setColorModalOpen(false))}
                >
                    ×
                </div>
            </div>

            <div className={styles.colorScrollBody}>
                {/* Block 1: Preset Themes */}
                <div className={styles.colorSection}>
                    <div className={styles.colorSectionTitle}>
                        {t("MarqueeDelete.ColorPresetTitle", "Presets")}
                    </div>
                    <div className={styles.colorPresetGrid}>
                        {PRESET_THEMES.map((preset) => (
                            <div
                                key={preset.id}
                                role="button"
                                className={styles.colorPresetBtn}
                                onMouseDown={(e) => onPrimaryMouseDown(e, () => applyColorPreset(preset.id))}
                            >
                                <div className={styles.colorPresetDots}>
                                    {preset.previewDots.map((dot, idx) => (
                                        <span
                                            key={idx}
                                            className={styles.colorPresetDot}
                                            style={{ backgroundColor: dot }}
                                        />
                                    ))}
                                </div>
                                <span className={styles.colorPresetName}>
                                    {t(preset.labelKey, preset.fallback)}
                                </span>
                            </div>
                        ))}
                    </div>
                </div>

                {/* Block 2: 18 Color Items in 5 Groups + Single Item Editor */}
                <div className={styles.colorSection}>
                    <div className={styles.colorSectionTitle}>
                        {t("MarqueeDelete.ColorItemsTitle", "Highlight & Marquee Colors (18)")}
                    </div>

                    <div className={styles.colorGroupsContainer}>
                        {COLOR_GROUPS.map((group) => (
                            <div key={group.id} className={styles.colorGroupBlock}>
                                <div className={styles.colorGroupLabel}>
                                    {t(group.titleKey, group.titleFallback)}
                                </div>
                                <div className={styles.colorChipsWrap}>
                                    {group.items.map((item) => {
                                        const isSelected = item.key === selectedKey;
                                        const itemHex = palette[item.key];
                                        return (
                                            <div
                                                key={item.key}
                                                role="button"
                                                className={`${styles.colorItemChip} ${isSelected ? styles.colorItemChipSelected : ""}`}
                                                onMouseDown={(e) => onPrimaryMouseDown(e, () => setSelectedKey(item.key))}
                                            >
                                                <span
                                                    className={styles.colorItemSwatch}
                                                    style={{ backgroundColor: itemHex }}
                                                />
                                                <span className={styles.colorItemName}>
                                                    {t(item.labelKey, item.fallback)}
                                                </span>
                                            </div>
                                        );
                                    })}
                                </div>
                            </div>
                        ))}
                    </div>

                    {/* Active Item Editor: Preview + Quick Swatches + Div RGB Sliders */}
                    <div className={styles.colorEditorBox}>
                        <div className={styles.colorEditorHeader}>
                            <div className={styles.colorEditorIdentity}>
                                <span
                                    className={styles.colorEditorPreviewSwatch}
                                    style={{ backgroundColor: activeHex }}
                                />
                                <span className={styles.colorEditorItemName}>{activeLabel}</span>
                            </div>
                            <span className={styles.colorEditorHexCode}>{activeHex}</span>
                        </div>

                        <div className={styles.colorSubHeading}>
                            {t("MarqueeDelete.ColorSwatchesLabel", "Quick Swatches")}
                        </div>
                        <div className={styles.colorSwatchesGrid}>
                            {QUICK_SWATCHES.map((swatchHex) => {
                                const swatchActive = swatchHex.toUpperCase() === activeHex.toUpperCase();
                                return (
                                    <div
                                        key={swatchHex}
                                        role="button"
                                        className={`${styles.colorQuickSwatch} ${swatchActive ? styles.colorQuickSwatchActive : ""}`}
                                        style={{ backgroundColor: swatchHex }}
                                        onMouseDown={(e) => onPrimaryMouseDown(e, () => setColor(selectedKey, swatchHex))}
                                    />
                                );
                            })}
                        </div>

                        <div className={styles.colorSubHeading}>
                            {t("MarqueeDelete.ColorRgbEditorLabel", "Custom RGB")}
                        </div>
                        <DivSlider
                            label="R"
                            valueText={String(rgb.r)}
                            ratio={rgb.r / 255}
                            fillGradient={`linear-gradient(90deg, rgb(0,${rgb.g},${rgb.b}), rgb(255,${rgb.g},${rgb.b}))`}
                            thumbColor="#ff6b6b"
                            onChangeRatio={(next) => emitRgbChannel("r", next * 255)}
                            onStep={(delta) => emitRgbChannel("r", rgb.r + delta * 5)}
                        />
                        <DivSlider
                            label="G"
                            valueText={String(rgb.g)}
                            ratio={rgb.g / 255}
                            fillGradient={`linear-gradient(90deg, rgb(${rgb.r},0,${rgb.b}), rgb(${rgb.r},255,${rgb.b}))`}
                            thumbColor="#51cf66"
                            onChangeRatio={(next) => emitRgbChannel("g", next * 255)}
                            onStep={(delta) => emitRgbChannel("g", rgb.g + delta * 5)}
                        />
                        <DivSlider
                            label="B"
                            valueText={String(rgb.b)}
                            ratio={rgb.b / 255}
                            fillGradient={`linear-gradient(90deg, rgb(${rgb.r},${rgb.g},0), rgb(${rgb.r},${rgb.g},255))`}
                            thumbColor="#4dabf7"
                            onChangeRatio={(next) => emitRgbChannel("b", next * 255)}
                            onStep={(delta) => emitRgbChannel("b", rgb.b + delta * 5)}
                        />
                    </div>
                </div>

                {/* Block 3: 3 Opacity Sliders */}
                <div className={styles.colorSection}>
                    <div className={styles.colorSectionTitle}>
                        {t("MarqueeDelete.ColorAlphaTitle", "Opacity")}
                    </div>
                    {ALPHA_ITEMS.map((item) => {
                        const val = palette[item.key];
                        const pct = Math.max(0, Math.min(100, Math.round(val * 100)));
                        const zeroTag = item.hasHiddenZeroHint
                            ? t("MarqueeDelete.ColorOutlineHiddenTag", "0% = Hidden")
                            : undefined;
                        return (
                            <DivSlider
                                key={item.key}
                                label={t(item.labelKey, item.fallback)}
                                valueText={`${pct}%`}
                                ratio={pct / 100}
                                fillGradient="linear-gradient(90deg, rgba(16, 22, 32, 0.9), rgba(90, 178, 255, 0.85))"
                                thumbColor="#cfe8ff"
                                accentBadge={zeroTag}
                                onChangeRatio={(next) => emitAlphaRatio(item.key, next)}
                                onStep={(delta) => setAlpha(item.key, Math.max(0, Math.min(100, pct + delta * 5)) / 100)}
                            />
                        );
                    })}
                </div>
            </div>

            {/* Block 4: Footer */}
            <div className={styles.colorFooter}>
                <span className={styles.colorFooterHint}>
                    {t("MarqueeDelete.ColorEscHint", "ESC closes palette")}
                </span>
                <div className={styles.colorFooterActions}>
                    <Button
                        variant="flat"
                        focusKey={focusDisabledKey}
                        className={styles.modalBtnSecondary}
                        onSelect={resetColors}
                    >
                        {t("MarqueeDelete.ColorResetBtn", "Reset Defaults")}
                    </Button>
                    <Button
                        variant="flat"
                        focusKey={focusDisabledKey}
                        className={styles.colorCloseBtn}
                        onSelect={() => setColorModalOpen(false)}
                    >
                        {t("MarqueeDelete.ColorCloseBtn", "Close")}
                    </Button>
                </div>
            </div>
        </div>
    );

    if (typeof document !== "undefined" && document.body) {
        return ReactDOM.createPortal(modalNode, document.body);
    }
    return modalNode;
}
