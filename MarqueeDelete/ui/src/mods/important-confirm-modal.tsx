import React from "react";
import ReactDOM from "react-dom";
import { Button } from "cs2/ui";
import {
    cancelImportantModal,
    confirmImportantDelete,
    excludeImportant,
    useMarqueeModalState,
} from "./bindings";
import { vanillaRefs } from "./vanilla-refs";
import { STAR_ALL_ICON } from "./marquee-shared";
import styles from "./bulldoze-marquee-section.module.scss";

export function ImportantConfirmModal(props: {
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
    const parsedItems: {
        key: string;
        kind: "service" | "unique" | "signature";
        kindLabel: string;
        name: string;
    }[] = [];

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
            let kind: "service" | "unique" | "signature" = "service";
            let kindLabel = t("MarqueeDelete.ImportantService", "Service");
            if (kindCode === "U" || kindCode === "unique") {
                kind = "unique";
                kindLabel = t("MarqueeDelete.ImportantUnique", "Unique");
            } else if (kindCode === "L" || kindCode === "signature") {
                kind = "signature";
                kindLabel = t("MarqueeDelete.ImportantSignature", "Signature");
            }
            parsedItems.push({ key: `${idx}-${name}`, kind, kindLabel, name });
        }
    }

    const modalNode = (
        <div className={styles.modalBackdrop}>
            <div className={styles.modalCard}>
                <div className={styles.modalHeader}>
                    <div className={styles.modalWarnBadge}>
                        <img src={STAR_ALL_ICON} className={styles.inlineWarnIcon} alt="" />
                        <span>{t("MarqueeDelete.ImportantTag", "Important")} ({sel.countImportant})</span>
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
                        <div className={`${styles.modalStatPill} ${styles.modalStatPillService}`}>
                            {t("MarqueeDelete.ImportantService", "Service")}: {sel.countService}
                        </div>
                    )}
                    {sel.countUnique > 0 && (
                        <div className={`${styles.modalStatPill} ${styles.modalStatPillUnique}`}>
                            {t("MarqueeDelete.ImportantUnique", "Unique")}: {sel.countUnique}
                        </div>
                    )}
                    {sel.countSignature > 0 && (
                        <div className={`${styles.modalStatPill} ${styles.modalStatPillSignature}`}>
                            {t("MarqueeDelete.ImportantSignature", "Signature")}: {sel.countSignature}
                        </div>
                    )}
                </div>

                {(parsedItems.length > 0 || overflowCount > 0) && (
                    <div className={styles.modalListBox}>
                        {parsedItems.map((item, idx) => {
                            const kindClass =
                                item.kind === "unique"
                                    ? styles.modalItemKindUnique
                                    : item.kind === "signature"
                                    ? styles.modalItemKindSignature
                                    : styles.modalItemKindService;
                            const rowClass =
                                idx % 2 === 1
                                    ? `${styles.modalListItem} ${styles.modalListItemAlt}`
                                    : styles.modalListItem;
                            return (
                                <div key={item.key} className={rowClass}>
                                    <span className={styles.modalItemName}>{item.name}</span>
                                    <span className={`${styles.modalItemKind} ${kindClass}`}>{item.kindLabel}</span>
                                </div>
                            );
                        })}
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
