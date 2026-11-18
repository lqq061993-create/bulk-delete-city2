import { ModRegistrar, ModuleRegistryExtend } from "cs2/modding";
import {
    ALL_PRELOAD_ICONS,
    extendMouseToolOptions,
    extendToolOptionsVisible,
} from "mods/bulldoze-marquee-section";
import {
    initVanillaRefs,
    MOUSE_TOOL_OPTIONS_PATH,
    preloadIcons,
    TOOL_OPTIONS_VISIBILITY_PATH,
} from "mods/vanilla-refs";

const register: ModRegistrar = (moduleRegistry) => {
    const modules = moduleRegistry.registry;
    if (!modules?.get(MOUSE_TOOL_OPTIONS_PATH)?.MouseToolOptions) {
        return;
    }
    initVanillaRefs(moduleRegistry);
    preloadIcons(ALL_PRELOAD_ICONS);
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
