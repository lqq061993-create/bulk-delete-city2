import React from "react";
import { ModuleRegistry } from "cs2/modding";

export interface ToolButtonProps {
    focusKey?: any;
    src?: string;
    selected?: boolean;
    multiSelect?: boolean;
    disabled?: boolean;
    tooltip?: React.ReactNode;
    className?: string;
    children?: React.ReactNode;
    onSelect?: () => void;
}

export interface FocusDisabledProps {
    disabled?: boolean;
    children?: React.ReactNode;
}

export const TOOL_OPTIONS_VISIBILITY_PATH = "game-ui/game/components/tool-options/tool-options-panel.tsx";
export const MOUSE_TOOL_OPTIONS_PATH = "game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx";
export const TOOL_BUTTON_PATH = "game-ui/game/components/tool-options/tool-button/tool-button.tsx";
export const FOCUS_KEY_PATH = "game-ui/common/focus/focus-key.ts";
export const FOCUS_DISABLED_COMPONENT_PATH = "game-ui/common/focus/focus-disabled.tsx";
export const DESCRIPTION_TOOLTIP_THEME_PATH = "game-ui/common/tooltip/description-tooltip/description-tooltip.module.scss";

type VanillaRefs = {
    ToolButton: React.ComponentType<ToolButtonProps> | null;
    descriptionTooltipTheme: { title?: string; content?: string } | null;
    FOCUS_DISABLED: any;
    FocusDisabled: React.ComponentType<FocusDisabledProps> | null;
};

const cs2Input = (window as unknown as Record<string, any>)["cs2/input"];

export const vanillaRefs: VanillaRefs = {
    ToolButton: null,
    descriptionTooltipTheme: null,
    FOCUS_DISABLED: cs2Input?.FOCUS_DISABLED ?? null,
    FocusDisabled: cs2Input?.FocusDisabled ?? null,
};

const preloadedImages: HTMLImageElement[] = [];

export function preloadIcons(urls: string[]): void {
    for (const url of urls) {
        const img = new Image();
        img.src = url;
        preloadedImages.push(img);
    }
}

export function initVanillaRefs(registry: ModuleRegistry): void {
    const modules = registry.registry;
    vanillaRefs.ToolButton = modules.get(TOOL_BUTTON_PATH)?.ToolButton ?? null;
    vanillaRefs.descriptionTooltipTheme = modules.get(DESCRIPTION_TOOLTIP_THEME_PATH)?.classes ?? null;
    vanillaRefs.FOCUS_DISABLED = modules.get(FOCUS_KEY_PATH)?.FOCUS_DISABLED ?? cs2Input?.FOCUS_DISABLED ?? null;
    vanillaRefs.FocusDisabled = modules.get(FOCUS_DISABLED_COMPONENT_PATH)?.FocusDisabled ?? cs2Input?.FocusDisabled ?? null;
}
