import { ModRegistrar, ModuleRegistryExtend } from "cs2/modding";
import {
    ALL_PRELOAD_ICONS,
    extendMouseToolOptions,
    extendToolOptionsVisible,
} from "mods/bulldoze-marquee-section";
import { GlobalUndoOverlay, GlobalUndoTopButton } from "mods/global-undo-hud";
import {
    initVanillaRefs,
    MOUSE_TOOL_OPTIONS_PATH,
    preloadIcons,
    TOOL_OPTIONS_VISIBILITY_PATH,
} from "mods/vanilla-refs";

const register: ModRegistrar = (moduleRegistry) => {
    const modules = moduleRegistry.registry;
    initVanillaRefs(moduleRegistry);
    preloadIcons(ALL_PRELOAD_ICONS);
    moduleRegistry.append("GameTopLeft", GlobalUndoTopButton);
    moduleRegistry.append("Game", GlobalUndoOverlay);
    if (!modules?.get(MOUSE_TOOL_OPTIONS_PATH)?.MouseToolOptions) {
        return;
    }
    moduleRegistry.extend(
        MOUSE_TOOL_OPTIONS_PATH,
        "MouseToolOptions",
        extendMouseToolOptions
    );
    if (modules.get(TOOL_OPTIONS_VISIBILITY_PATH)?.useToolOptionsVisible) {
        moduleRegistry.extend(
            TOOL_OPTIONS_VISIBILITY_PATH,
            "useToolOptionsVisible",
            extendToolOptionsVisible as unknown as ModuleRegistryExtend
        );
    }
};

export default register;
