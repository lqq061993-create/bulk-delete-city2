import React from "react";
import { Button, Tooltip } from "cs2/ui";
import {
    cancelUndo,
    clearSelection,
    confirmDelete,
    confirmSkipReviewDelete,
    continueInterruptedDeletion,
    discardInterruptedDeletion,
    dismissUndoNotice,
    restoreLastSelection,
    setImpactReportOpen,
    stopUndo,
    switchFromBulldoze,
    togglePickSimilar,
    undoLastDelete,
    useMarqueeCardState,
} from "./bindings";
import {
    hslToRgbaCss,
    MarqueeDimensionPill,
    renderRichTooltip,
    roadFacilityLabel,
    SKIP_REVIEW_ICON,
    STAR_ALL_ICON,
    translateUndoNoticeKey,
    UndoUiState,
} from "./marquee-shared";
import { vanillaRefs } from "./vanilla-refs";
import styles from "./bulldoze-marquee-section.module.scss";

interface ParticleSpec {
    xPct: number;
    yBase: number;
    vx: number;
    wavePhase: number;
    waveSpeed: number;
    waveAmp: number;
    twinklePhase: number;
    twinkleSpeed: number;
    sizeRem: number;
    hueOffset: number;
}

const FILL_PARTICLE_COUNT = 14;
const TRACK_PARTICLE_COUNT = 6;

function createParticleSpecs(count: number, minSize: number, maxSize: number, minSpeed: number, maxSpeed: number): ParticleSpec[] {
    const list: ParticleSpec[] = [];
    for (let i = 0; i < count; i++) {
        list.push({
            xPct: ((i + Math.random() * 0.85) / count) * 100,
            yBase: 22 + Math.random() * 56,
            vx: minSpeed + Math.random() * (maxSpeed - minSpeed),
            wavePhase: Math.random() * Math.PI * 2,
            waveSpeed: 0.10 + Math.random() * 0.14,
            waveAmp: 8 + Math.random() * 14,
            twinklePhase: Math.random() * Math.PI * 2,
            twinkleSpeed: 0.14 + Math.random() * 0.18,
            sizeRem: minSize + Math.random() * (maxSize - minSize),
            hueOffset: (i * (360 / count) + Math.random() * 30) % 360,
        });
    }
    return list;
}

function UndoProgressBanner(props: {
    restoredCount: number;
    skippedCount: number;
    totalCount: number;
    undoFx: boolean;
    t: (id: string, fallback: string) => string;
}): JSX.Element {
    const { restoredCount, skippedCount, totalCount, undoFx, t } = props;
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const safeTotal = totalCount > 0 ? totalCount : 1;
    const processedCount = restoredCount + skippedCount;
    const pct = Math.min(100, Math.max(0, Math.round((processedCount / safeTotal) * 100)));

    // Flowing rainbow gradient + sweeping light beam + streaming glowing particles via DOM refs (zero React re-renders).
    const trackRef = React.useRef<HTMLDivElement | null>(null);
    const fillRef = React.useRef<HTMLDivElement | null>(null);
    const flowRef = React.useRef<HTMLDivElement | null>(null);
    const tipRef = React.useRef<HTMLDivElement | null>(null);
    const particleRefs = React.useRef<(HTMLDivElement | null)[]>([]);
    const trackParticleRefs = React.useRef<(HTMLDivElement | null)[]>([]);
    const fxStateRef = React.useRef<{
        huePhase: number;
        flowPos: number;
        particles: ParticleSpec[];
        trackParticles: ParticleSpec[];
    } | null>(null);

    if (!fxStateRef.current) {
        fxStateRef.current = {
            huePhase: Math.random() * 360,
            flowPos: -42,
            particles: createParticleSpecs(FILL_PARTICLE_COUNT, 2.6, 5.2, 1.0, 2.3),
            trackParticles: createParticleSpecs(TRACK_PARTICLE_COUNT, 2.0, 3.6, 0.45, 1.05),
        };
    }

    React.useEffect(() => {
        const trackEl = trackRef.current;
        const fillEl = fillRef.current;
        const flowEl = flowRef.current;
        const tipEl = tipRef.current;
        if (!undoFx) {
            if (trackEl) {
                trackEl.style.borderColor = "";
                trackEl.style.boxShadow = "";
            }
            if (fillEl) {
                fillEl.style.backgroundImage = "";
                fillEl.style.boxShadow = "";
            }
            if (flowEl) {
                flowEl.style.left = "";
            }
            if (tipEl) {
                tipEl.style.boxShadow = "";
                tipEl.style.opacity = "";
            }
            return;
        }

        const applyFrame = () => {
            const st = fxStateRef.current!;
            const base = st.huePhase;
            const h0 = base % 360;
            const h1 = (base + 60) % 360;
            const h2 = (base + 120) % 360;
            const h3 = (base + 180) % 360;
            const h4 = (base + 240) % 360;
            const h5 = (base + 300) % 360;

            if (fillEl) {
                fillEl.style.backgroundImage = `linear-gradient(90deg, ${hslToRgbaCss(h0, 96, 64)} 0%, ${hslToRgbaCss(h1, 95, 64)} 20%, ${hslToRgbaCss(h2, 92, 62)} 40%, ${hslToRgbaCss(h3, 96, 65)} 60%, ${hslToRgbaCss(h4, 95, 66)} 80%, ${hslToRgbaCss(h5, 96, 64)} 100%)`;
                fillEl.style.boxShadow = `0 0 16rem ${hslToRgbaCss(h2, 95, 66, 0.80)}, 0 0 8rem ${hslToRgbaCss(h4, 95, 70, 0.65)}`;
            }
            if (trackEl) {
                trackEl.style.borderColor = hslToRgbaCss(h1, 90, 74, 0.78);
                trackEl.style.boxShadow = `inset 0 2rem 5rem rgba(0, 0, 0, 0.60), 0 0 14rem ${hslToRgbaCss(h3, 90, 68, 0.44)}`;
            }
            if (flowEl) {
                flowEl.style.left = `${st.flowPos.toFixed(2)}%`;
            }
            if (tipEl) {
                const flarePulse = 0.72 + 0.28 * Math.sin((base * Math.PI) / 45);
                tipEl.style.opacity = flarePulse.toFixed(3);
                tipEl.style.boxShadow = `0 0 10rem rgba(255, 255, 255, 0.95), 0 0 18rem ${hslToRgbaCss(h4, 98, 72, 0.92)}`;
            }

            for (let i = 0; i < st.particles.length; i++) {
                const el = particleRefs.current[i];
                if (!el) continue;
                const p = st.particles[i];
                const y = Math.max(12, Math.min(86, p.yBase + Math.sin(p.wavePhase) * p.waveAmp));
                const twinkle = 0.42 + 0.58 * (0.5 + 0.5 * Math.sin(p.twinklePhase));
                const ph = (base + p.hueOffset) % 360;
                const sz = (p.sizeRem * (0.82 + 0.36 * twinkle)).toFixed(2);
                el.style.left = `${p.xPct.toFixed(2)}%`;
                el.style.top = `${y.toFixed(2)}%`;
                el.style.width = `${sz}rem`;
                el.style.height = `${sz}rem`;
                el.style.opacity = twinkle.toFixed(3);
                el.style.backgroundColor = hslToRgbaCss(ph, 100, 84, 0.96);
                el.style.boxShadow = `0 0 7rem ${hslToRgbaCss(ph, 98, 70, 0.95)}, 0 0 2.5rem rgba(255, 255, 255, 0.92)`;
            }

            for (let i = 0; i < st.trackParticles.length; i++) {
                const el = trackParticleRefs.current[i];
                if (!el) continue;
                const p = st.trackParticles[i];
                const y = Math.max(15, Math.min(84, p.yBase + Math.sin(p.wavePhase) * (p.waveAmp * 0.7)));
                const twinkle = 0.18 + 0.36 * (0.5 + 0.5 * Math.sin(p.twinklePhase));
                const ph = (base + p.hueOffset) % 360;
                const sz = p.sizeRem.toFixed(2);
                el.style.left = `${p.xPct.toFixed(2)}%`;
                el.style.top = `${y.toFixed(2)}%`;
                el.style.width = `${sz}rem`;
                el.style.height = `${sz}rem`;
                el.style.opacity = twinkle.toFixed(3);
                el.style.backgroundColor = hslToRgbaCss(ph, 96, 78, 0.85);
                el.style.boxShadow = `0 0 5rem ${hslToRgbaCss(ph, 96, 68, 0.72)}`;
            }
        };

        applyFrame();
        const timer = window.setInterval(() => {
            const st = fxStateRef.current!;
            st.huePhase = (st.huePhase + 3.0) % 360;
            st.flowPos += 2.6;
            if (st.flowPos > 116) st.flowPos = -44;

            for (let i = 0; i < st.particles.length; i++) {
                const p = st.particles[i];
                p.xPct += p.vx;
                if (p.xPct > 104) {
                    p.xPct = -5;
                    p.yBase = 20 + Math.random() * 60;
                }
                p.wavePhase += p.waveSpeed;
                p.twinklePhase += p.twinkleSpeed;
            }
            for (let i = 0; i < st.trackParticles.length; i++) {
                const p = st.trackParticles[i];
                p.xPct += p.vx;
                if (p.xPct > 103) {
                    p.xPct = -4;
                    p.yBase = 20 + Math.random() * 60;
                }
                p.wavePhase += p.waveSpeed;
                p.twinklePhase += p.twinkleSpeed;
            }
            applyFrame();
        }, 40);
        return () => window.clearInterval(timer);
    }, [undoFx]);

    const fillWidthStyle: React.CSSProperties = {
        width: `${pct}%`,
        minWidth: pct > 0 ? "18rem" : "0",
    };

    return (
        <div className={styles.undoProgressBox}>
            <div className={styles.undoProgressTop}>
                <div className={styles.summaryLeft}>
                    <span className={styles.undoProgressLabel}>
                        {t("MarqueeDelete.UndoProgressLabel", "Creation requests submitted")}: {restoredCount}/{totalCount}
                    </span>
                    <span className={styles.undoProgressPct}>{pct}%</span>
                </div>
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
                    {t("MarqueeDelete.UndoSkippedLabel", "Skipped")}: {skippedCount} ({processedCount}/{totalCount})
                </div>
            )}
            <div
                ref={trackRef}
                className={undoFx ? styles.undoProgressTrack : styles.undoProgressTrackPlain}
            >
                {undoFx &&
                    fxStateRef.current.trackParticles.map((_, idx) => (
                        <div
                            key={`tp-${idx}`}
                            ref={(el) => {
                                trackParticleRefs.current[idx] = el;
                            }}
                            className={styles.undoProgressTrackParticle}
                        />
                    ))}
                <div
                    ref={fillRef}
                    className={undoFx ? styles.undoProgressFill : styles.undoProgressFillPlain}
                    style={fillWidthStyle}
                >
                    {pct > 0 && <div className={styles.undoProgressSheen} />}
                    {undoFx && pct > 0 && (
                        <React.Fragment>
                            <div ref={flowRef} className={styles.undoProgressFlowBeam} />
                            {fxStateRef.current.particles.map((_, idx) => (
                                <div
                                    key={`fp-${idx}`}
                                    ref={(el) => {
                                        particleRefs.current[idx] = el;
                                    }}
                                    className={styles.undoProgressParticle}
                                />
                            ))}
                            <div ref={tipRef} className={styles.undoProgressTipFlare} />
                        </React.Fragment>
                    )}
                </div>
            </div>
        </div>
    );
}

function UndoScopeSummaryChipBar(props: {
    undo: UndoUiState;
    onOpenUndoOptions: () => void;
    t: (id: string, fallback: string) => string;
}): JSX.Element | null {
    const { undo, onOpenUndoOptions, t } = props;
    if (undo.historyDepth <= 0 && !undo.busy && !undo.inProgress) {
        return null;
    }

    const activeScopes: string[] = [];
    if (undo.include.buildings) activeScopes.push(t("MarqueeDelete.FilterBuildings", "Buildings"));
    if (undo.include.networks) activeScopes.push(t("MarqueeDelete.FilterNetworks", "Roads/Nets"));
    if (undo.include.areas) activeScopes.push(t("MarqueeDelete.FilterAreas", "Surfaces"));
    if (undo.include.trees) activeScopes.push(t("MarqueeDelete.FilterTrees", "Trees"));
    if (undo.include.props) activeScopes.push(t("MarqueeDelete.FilterProps", "Props"));
    if (undo.include.decals) activeScopes.push(t("MarqueeDelete.FilterDecals", "Decals"));

    return (
        <Tooltip
            tooltip={renderRichTooltip(
                t("MarqueeDelete.UndoScopeLabel", "Undo Scope"),
                t("MarqueeDelete.UndoScopeQuickEditHint", "Click to adjust Undo Scope in Options")
            )}
            delayTime={0}
        >
            <div className={styles.undoScopeSummaryBar} onClick={onOpenUndoOptions}>
                <span className={styles.undoScopeSummaryLabel}>
                    {t("MarqueeDelete.UndoScopeLabel", "Undo Scope")} ({activeScopes.length}/6)
                </span>
                <div className={styles.undoScopeSummaryChips}>
                    {activeScopes.length > 0 ? (
                        activeScopes.map((label) => (
                            <span key={label} className={styles.undoScopeMiniChip}>
                                {label}
                            </span>
                        ))
                    ) : (
                        <span className={styles.compactHint}>{t("MarqueeDelete.OptOff", "OFF")}</span>
                    )}
                </div>
            </div>
        </Tooltip>
    );
}

export function UndoStatusArea(props: {
    undo: UndoUiState;
    compatCanUndo?: boolean;
    hideIdleDetails?: boolean;
    undoFx?: boolean;
    onOpenUndoOptions: () => void;
    t: (id: string, fallback: string) => string;
}): JSX.Element | null {
    const { undo, compatCanUndo = true, hideIdleDetails = false, undoFx = false, onOpenUndoOptions, t } = props;
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
        undo.historyDepth > 0;

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

            {!(hideIdleDetails && !undo.inProgress && !undo.busy) && (
                <UndoScopeSummaryChipBar undo={undo} onOpenUndoOptions={onOpenUndoOptions} t={t} />
            )}

            {!(hideIdleDetails && !undo.inProgress && !undo.busy) &&
                undo.counts.buildings > 0 &&
                (undo.historyDepth > 0 || undo.busy || undo.inProgress) && (
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
                    undoFx={undoFx}
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

export function MarqueeFooter(props: {
    active: boolean;
    underground: boolean;
    hasAnyFilter: boolean;
    hasUndergroundFilter: boolean;
    modalOpen: boolean;
    roadFacilityMode: number;
    undo: UndoUiState;
    compatCanUndo: boolean;
    onOpenUndoOptions: () => void;
    t: (id: string, fallback: string) => string;
}): JSX.Element | null {
    const sel = useMarqueeCardState();
    const {
        active,
        t,
        underground,
        hasAnyFilter,
        hasUndergroundFilter,
        modalOpen,
        roadFacilityMode,
        undo,
        compatCanUndo,
        onOpenUndoOptions,
    } = props;

    const editingRoadFacilities = roadFacilityMode !== 0;
    const isZh = t("MarqueeDelete.UndoBtn", "Undo") !== "Undo";
    const focusDisabledKey = vanillaRefs.FOCUS_DISABLED;
    const FocusDisabledComp = vanillaRefs.FocusDisabled;

    const hasPending = !sel.dragging && sel.pending && sel.count > 0;
    const hasInterruptedDeletion = sel.interruptedDeletionCount > 0;
    const directDelete = !editingRoadFacilities && !sel.confirm && !hasInterruptedDeletion;

    React.useEffect(() => {
        if (!active || !hasPending || sel.dragging || modalOpen) {
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
    }, [active, hasPending, sel.dragging, modalOpen, sel.compatCanSubmit, directDelete, sel.undo.busy, sel.notice]);

    if (!active) {
        const showIdleUndo =
            undo.inProgress ||
            undo.busy ||
            !!undo.finishedNotice ||
            undo.zeroMatch ||
            undo.captureFailedCount > 0 ||
            undo.notCaptured > 0 ||
            undo.historyDepth > 0;
        if (!showIdleUndo) {
            return null;
        }
        return (
            <div className={styles.panelFooter}>
                <div className={styles.activeFooter}>
                    <UndoStatusArea
                        undo={undo}
                        compatCanUndo={compatCanUndo}
                        undoFx={sel.undoFx}
                        onOpenUndoOptions={onOpenUndoOptions}
                        t={t}
                    />
                </div>
            </div>
        );
    }

    const breakdownItems: { key: string; label: string; count: number; dotClass?: string }[] = [];
    if (sel.counts.residential > 0) {
        breakdownItems.push({ key: "residential", label: t("MarqueeDelete.FilterResidential", "Residential"), count: sel.counts.residential, dotClass: styles.tagDotRes });
    }
    if (sel.counts.commercial > 0) {
        breakdownItems.push({ key: "commercial", label: t("MarqueeDelete.FilterCommercial", "Commercial"), count: sel.counts.commercial, dotClass: styles.tagDotCom });
    }
    if (sel.counts.industrial > 0) {
        breakdownItems.push({ key: "industrial", label: t("MarqueeDelete.FilterIndustrial", "Industrial"), count: sel.counts.industrial, dotClass: styles.tagDotInd });
    }
    if (sel.counts.office > 0) {
        breakdownItems.push({ key: "office", label: t("MarqueeDelete.FilterOffice", "Office"), count: sel.counts.office, dotClass: styles.tagDotOff });
    }
    if (sel.counts.buildings > 0) {
        breakdownItems.push({ key: "buildings", label: t("MarqueeDelete.FilterOtherBuildings", "Service/Other"), count: sel.counts.buildings, dotClass: styles.tagDotOther });
    }
    if (sel.counts.networks > 0) {
        breakdownItems.push({
            key: "networks",
            label: editingRoadFacilities ? roadFacilityLabel(roadFacilityMode, t) : t("MarqueeDelete.FilterNetworks", "Roads/Nets"),
            count: sel.counts.networks,
        });
    }
    if (sel.counts.trees > 0) {
        breakdownItems.push({ key: "trees", label: t("MarqueeDelete.FilterTrees", "Trees"), count: sel.counts.trees });
    }
    if (sel.counts.props > 0) {
        breakdownItems.push({ key: "props", label: t("MarqueeDelete.FilterProps", "Props"), count: sel.counts.props });
    }
    if (sel.counts.decals > 0) {
        breakdownItems.push({ key: "decals", label: t("MarqueeDelete.FilterDecals", "Decals"), count: sel.counts.decals });
    }
    if (sel.counts.areas > 0) {
        breakdownItems.push({ key: "areas", label: t("MarqueeDelete.FilterAreas", "Surfaces"), count: sel.counts.areas });
    }
    if (sel.counts.moving > 0) {
        breakdownItems.push({ key: "moving", label: t("MarqueeDelete.FilterMoving", "Vehicles"), count: sel.counts.moving });
    }

    const unitCells = t("MarqueeDelete.UnitCells", "cells");
    const unitSqM = t("MarqueeDelete.UnitSqMeters", "m²");
    const unitVertices = t("MarqueeDelete.UnitVertices", "pts");
    const dimensionPillNode = (
        <MarqueeDimensionPill unitCells={unitCells} unitSqM={unitSqM} unitVertices={unitVertices} />
    );

    const filterHintBanner = !hasAnyFilter ? (
        <div className={styles.importantWarnBanner}>
            <span>{t("MarqueeDelete.WarnNoFilters", "All filters are off — drag will select nothing.")}</span>
        </div>
    ) : underground && !hasUndergroundFilter ? (
        <div className={styles.infoHintBanner}>
            <span>
                {t("MarqueeDelete.WarnUndergroundNoFilter", "Underground view: enable Roads/Nets or Vehicles to target pipes and tunnels.")}
            </span>
        </div>
    ) : null;

    const selectionNoticeBanner = (() => {
        if (!sel.notice) return null;
        if (sel.notice === "facility_running") {
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.RoadFacilityRunning", "Applying junction changes. Press Esc to stop.")}</span></div>;
        }
        if (sel.notice.startsWith("facility_result|")) {
            const [, submitted, skipped] = sel.notice.split("|");
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.RoadFacilityResult", "Submitted {0} changes. {1} were skipped or stopped.").replace("{0}", submitted).replace("{1}", skipped)}</span></div>;
        }
        if (sel.notice === "protected") {
            return <div className={styles.importantWarnBanner}>{t("MarqueeDelete.SelectionProtected", "This selection would remove a protected object. Adjust the area or filters.")}</div>;
        }
        if (sel.notice === "changed") {
            return <div className={styles.importantWarnBanner}>{t("MarqueeDelete.SelectionChanged", "The affected objects changed. Review the selection before confirming again.")}</div>;
        }
        if (sel.notice.startsWith("deleting|")) {
            const remaining = sel.notice.split("|")[1];
            return <div className={styles.infoHintBanner}>{t("MarqueeDelete.DeletionProgress", "Deleting in batches. {0} targets left. Esc stops.").replace("{0}", remaining)}</div>;
        }
        if (sel.notice === "resume_unavailable") {
            return <div className={styles.infoHintBanner}>{t("MarqueeDelete.ResumeUnavailable", "No recorded targets can be removed under the current filters and protections.")}</div>;
        }
        if (sel.notice === "deletion_blocked_compat") {
            return <div className={styles.importantWarnBanner}>{t("MarqueeDelete.DeletionBlockedCompat", isZh ? "当前游戏构建未验证，拆除不可用。" : "This game build is not verified; demolition is unavailable.")}</div>;
        }
        if (sel.notice === "deletion_observe_timeout") {
            return <div className={styles.importantWarnBanner}>{t("MarqueeDelete.DeletionObserveTimeout", isZh ? "等待拆除结果超时，已暂停后续提交。已提交的命令仍可能执行，请检查后再继续。" : "Timed out waiting for demolition results; further submissions are paused.")}</div>;
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
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.SelectionExcludedImportant", "Excluded {0} important building(s). Review the remaining {1} object(s) and confirm to delete.").replace("{0}", excludedCount).replace("{1}", String(sel.count))}</span></div>;
        }
        if (sel.notice.startsWith("excluded_all|")) {
            const excludedCount = sel.notice.split("|")[1] || "0";
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.SelectionExcludedAllImportant", "Excluded all {0} important building(s); no remaining objects in selection.").replace("{0}", excludedCount)}</span></div>;
        }
        if (sel.notice.startsWith("hidden_retained|")) {
            const retainedCount = sel.notice.split("|")[1] || "0";
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.SelectionHiddenRetained", "No matches under current filters, but {0} manually added object(s) are retained. Re-enable filters to restore, or use ESC / Clear to reset.").replace("{0}", retainedCount)}</span></div>;
        }
        if (sel.notice === "hidden_box") {
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.SelectionHiddenBox", "No matches inside the locked marquee under current filters. Adjust filters to restore, or use ESC / Clear to reset.")}</span></div>;
        }
        if (sel.notice.startsWith("similar_matched|")) {
            const matchedCount = sel.notice.slice("similar_matched|".length);
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.PickSimilarMatched", "Kept {0} matching object(s) of the selected type.").replace("{0}", matchedCount)}</span></div>;
        }
        if (sel.notice === "similar_no_target") {
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.PickSimilarNoTarget", "No valid object under cursor. Click an object in the city.")}</span></div>;
        }
        if (sel.notice === "similar_no_prefab") {
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.PickSimilarNoPrefab", "Target object has no prefab metadata; cannot match same type.")}</span></div>;
        }
        if (sel.notice === "similar_no_match") {
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.PickSimilarNoMatch", "No matching objects of that type found in the current selection; selection preserved.")}</span></div>;
        }
        if (sel.notice === "similar_blocked_polygon") {
            return <div className={styles.infoHintBanner}><span>{t("MarqueeDelete.PickSimilarBlockedPolygon", "Match Same is unavailable while drawing a polygon — finish or cancel the polygon draft first.")}</span></div>;
        }
        return null;
    })();

    const hasHiddenRetainedEdits =
        !sel.dragging && !hasPending && (sel.notice.startsWith("hidden_retained|") || sel.notice === "hidden_box");

    const isPolyDraft = sel.selectionMode === 1 && sel.polygonVertexCount > 0;
    const isFreehandLive = sel.freehandDrawing;

    const polygonCapBanner = sel.polygonVertexLimitReached ? (
        <div className={styles.vertexCapBanner}>
            <span>
                {t("MarqueeDelete.PolygonVertexCapWarn", "Polygon vertex cap ({0}) reached — close shape or undo a vertex.")
                    .replace("{0}", String(sel.polygonVertexLimit || 64))}
            </span>
        </div>
    ) : null;

    const polygonSnapBanner =
        sel.polygonSnapActive && !sel.dragging ? (
            <div className={styles.polygonSnapBanner}>
                <span>{t("MarqueeDelete.PolygonSnapReady", "Snapped to start — click or press Enter to close polygon")}</span>
            </div>
        ) : null;

    const shapeInvalidBanner =
        sel.shapeInvalid && !sel.dragging && !sel.freehandDrawing ? (
            <div className={styles.shapeInvalidBanner}>
                <span>{t("MarqueeDelete.ShapeInvalidWarn", "Selection area too small (< 1 m²) or degenerate — shape discarded.")}</span>
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

    const idleCompactHint = editingRoadFacilities
        ? t("MarqueeDelete.RoadFacilitySelectHint", "Draw around the feature you want to remove.")
        : directDelete
        ? t("MarqueeDelete.HintConfirmOffIdle", "Confirm OFF: Drag and release to delete directly (no secondary confirmation)")
        : sel.selectionMode === 1
        ? t("MarqueeDelete.HintCompactPolygonIdle", "Click to add vertices · Click start / Enter to close · Right-click/Backspace undo")
        : sel.selectionMode === 2
        ? t("MarqueeDelete.HintCompactFreehandIdle", "Hold left mouse button and drag to draw stroke · Release to close")
        : t("MarqueeDelete.HintCompactIdle", "LMB Drag • RMB Exit");

    const footerContent = (
        <div className={styles.panelFooter}>
            <div className={styles.activeFooter}>
                <UndoStatusArea
                    undo={sel.undo}
                    compatCanUndo={sel.compatCanUndo}
                    hideIdleDetails={hasPending}
                    undoFx={sel.undoFx}
                    onOpenUndoOptions={onOpenUndoOptions}
                    t={t}
                />
                {polygonCapBanner}
                {polygonSnapBanner}
                {shapeInvalidBanner}
                {shapeSimplifiedBanner}
                {hasInterruptedDeletion && !sel.dragging && !isFreehandLive && !isPolyDraft && (
                    <div className={styles.interruptedCard}>
                        <span className={styles.interruptedSummaryText}>
                            {sel.reviewingInterruptedDeletion
                                ? t("MarqueeDelete.ReviewingRemaining", "Review the remaining targets below. Only the targets shown below will be submitted when you confirm.")
                                : t("MarqueeDelete.InterruptedSummary", "Demolition paused — {0} target(s) remaining.").replace("{0}", String(sel.interruptedDeletionCount))}
                        </span>
                        <div className={styles.actionsRow}>
                            {!sel.reviewingInterruptedDeletion && (
                                <Button
                                    variant="flat"
                                    className={styles.continueDeleteBtn}
                                    disabled={sel.undo.busy}
                                    focusKey={focusDisabledKey}
                                    onSelect={continueInterruptedDeletion}
                                >
                                    {t("MarqueeDelete.ContinueInterruptedBtn", "Delete Now ({0})").replace("{0}", String(sel.interruptedDeletionCount))}
                                </Button>
                            )}
                            <Button
                                variant="flat"
                                className={styles.keepBuildingsBtn}
                                focusKey={focusDisabledKey}
                                onSelect={discardInterruptedDeletion}
                            >
                                {t("MarqueeDelete.KeepBuildingsBtn", "Keep Buildings")}
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
                                ? t("MarqueeDelete.HintPolygonNeedMore", "Add at least {0} more vertex(es) before closing").replace("{0}", String(3 - sel.polygonVertexCount))
                                : t("MarqueeDelete.HintPolygonCloseReady", "Click start or press Enter to close · Right-click/Backspace undoes last vertex")}
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
                            {t("MarqueeDelete.HintCompactFreehandDraw", "Keep dragging to outline the area · Release to close · Esc cancels")}
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
                                    <Button variant="flat" focusKey={focusDisabledKey} className={styles.secondaryBtn} onSelect={clearSelection}>
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
                                            <Button variant="flat" disabled={true} focusKey={focusDisabledKey} className={styles.undoQuickBtnDisabled}>
                                                {t("MarqueeDelete.UndoBtn", "Undo")} ({sel.undo.nextCount})
                                            </Button>
                                        </Tooltip>
                                    ) : (
                                        <Button variant="flat" focusKey={focusDisabledKey} className={styles.undoQuickBtn} onSelect={undoLastDelete}>
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
                                        <Button variant="flat" focusKey={focusDisabledKey} className={styles.secondaryBtn} onSelect={restoreLastSelection}>
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
                                    ? t("MarqueeDelete.HintCompactDragDirect", "Release to delete immediately (Confirm OFF) • RMB Cancel")
                                    : t("MarqueeDelete.HintCompactDrag", "Release Lock • RMB Cancel")}
                            </span>
                        </div>
                        {sel.countImportant > 0 && (
                            <div className={styles.importantWarnBanner}>
                                <span className={styles.inlineIconText}>
                                    <img src={STAR_ALL_ICON} className={styles.inlineWarnIcon} alt="" />
                                    <span>{t("MarqueeDelete.ImportantTag", "Important")}</span>
                                </span>
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
                                <span className={styles.inlineIconText}>
                                    <img src={STAR_ALL_ICON} className={styles.inlineWarnIcon} alt="" />
                                    <span>{t("MarqueeDelete.ImportantProtectedSummary", "Contains {0} critical/protected building(s) (Confirm required)").replace("{0}", String(sel.countImportant))}</span>
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
                                {t("MarqueeDelete.PickSimilarArmedHint", "Click an object in the world to keep only the same type inside selection (ESC / RMB to cancel).")}
                            </div>
                        )}
                        {selectionNoticeBanner}
                        {editingRoadFacilities && (
                            <div className={styles.infoHintBanner}>
                                <span>{t("MarqueeDelete.RoadFacilityConfirmHint", "This changes road settings. Use Road Services to restore the feature.")}</span>
                            </div>
                        )}
                        {!sel.undo.busy && sel.notice !== "protected" && (!hasInterruptedDeletion || sel.reviewingInterruptedDeletion) && (
                            <div className={styles.primaryAction}>
                                {!sel.compatCanSubmit || sel.compatTier === "preview_only" ? (
                                    <Button variant="flat" disabled={true} focusKey={focusDisabledKey} className={styles.confirmBtnPreviewOnly}>
                                        {sel.compatTier === "preview_only"
                                            ? t("MarqueeDelete.PreviewOnlyNotice", "Preview only (Target game version pending verification)")
                                            : t("MarqueeDelete.CompatUnavailableNotice", "Deletion unavailable in this build")}
                                    </Button>
                                ) : (
                                    <div className={styles.primaryConfirmGroup}>
                                        <Button variant="flat" focusKey={focusDisabledKey} className={styles.confirmBtn} onSelect={confirmDelete}>
                                            {editingRoadFacilities
                                                ? t("MarqueeDelete.RoadFacilityConfirm", "Remove {0}").replace("{0}", roadFacilityLabel(roadFacilityMode, t))
                                                : sel.skipReview
                                                ? t("MarqueeDelete.ConfirmSkipReviewBtn", "Delete Directly (No Review)")
                                                : t("MarqueeDelete.ConfirmBtn", "Confirm deletion")} ({sel.count}) [Enter]
                                        </Button>
                                        {!editingRoadFacilities && !sel.skipReview && (
                                            <Tooltip
                                                tooltip={renderRichTooltip(
                                                    t("MarqueeDelete.ConfirmSkipReviewBtn", "Delete Directly (No Review)"),
                                                    t("MarqueeDelete.DescConfirmSkipReviewBtn", "Execute batch deletion without pausing mid-way for target-change re-review.")
                                                )}
                                                delayTime={0}
                                            >
                                                <Button
                                                    variant="flat"
                                                    focusKey={focusDisabledKey}
                                                    className={styles.skipReviewBtn}
                                                    onSelect={confirmSkipReviewDelete}
                                                >
                                                    <img src={SKIP_REVIEW_ICON} className={styles.inlineBtnIcon} alt="" />
                                                    <span>{t("MarqueeDelete.ConfirmSkipReviewBtn", "Delete Directly (No Review)")}</span>
                                                </Button>
                                            </Tooltip>
                                        )}
                                    </div>
                                )}
                            </div>
                        )}
                        <div className={styles.actionsRow}>
                            {!editingRoadFacilities && !sel.undo.busy && !sel.reviewingInterruptedDeletion && (
                                <Tooltip
                                    tooltip={renderRichTooltip(
                                        t("MarqueeDelete.PickSimilarBtn", "Match Same"),
                                        isPolyDraft
                                            ? t("MarqueeDelete.PickSimilarBlockedPolygon", "Match Same is unavailable while drawing a polygon — finish or cancel the polygon draft first.")
                                            : t("MarqueeDelete.DescPickSimilarBtn", "Arm one-shot pick mode, then click any object in the world to keep only objects of the same prefab and category inside the current selection.")
                                    )}
                                    delayTime={0}
                                >
                                    <Button
                                        variant="flat"
                                        disabled={isPolyDraft}
                                        focusKey={focusDisabledKey}
                                        className={`${sel.pickSimilarArmed ? styles.pickSimilarBtnArmed : styles.pickSimilarBtn} ${isPolyDraft ? styles.pickSimilarBtnDisabled : ""}`}
                                        onSelect={() => {
                                            if (!isPolyDraft) togglePickSimilar();
                                        }}
                                    >
                                        {t("MarqueeDelete.PickSimilarBtn", "Match Same")}
                                    </Button>
                                </Tooltip>
                            )}
                            <Button variant="flat" focusKey={focusDisabledKey} className={styles.secondaryBtn} onSelect={clearSelection}>
                                {t("MarqueeDelete.ClearBtn", "Clear")} [Esc]
                            </Button>
                            {!editingRoadFacilities && (
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
                                        <Button variant="flat" disabled={true} focusKey={focusDisabledKey} className={styles.undoBtnDisabled}>
                                            {t("MarqueeDelete.UndoBtn", "Undo")} ({sel.undo.nextCount})
                                        </Button>
                                    </Tooltip>
                                ) : (
                                    <Button variant="flat" focusKey={focusDisabledKey} className={styles.undoBtn} onSelect={undoLastDelete}>
                                        {t("MarqueeDelete.UndoBtn", "Undo")} ({sel.undo.nextCount})
                                    </Button>
                                )
                            )}
                            <Button variant="flat" focusKey={focusDisabledKey} className={styles.secondaryBtn} onSelect={switchFromBulldoze}>
                                {t("MarqueeDelete.CloseBtn", "Single")}
                            </Button>
                        </div>
                        {!!sel.impactReportRaw && (
                            <ImpactReportCard raw={sel.impactReportRaw} t={t} onClose={() => setImpactReportOpen(false)} />
                        )}
                    </div>
                )}
            </div>
        </div>
    );

    if (FocusDisabledComp) {
        return <FocusDisabledComp disabled={true}>{footerContent}</FocusDisabledComp>;
    }
    return footerContent;
}
