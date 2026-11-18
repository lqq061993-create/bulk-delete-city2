export interface ColorPaletteData {
    marqueeGroundBorder: string;
    marqueeGroundFill: string;
    marqueeUndergroundBorder: string;
    marqueeUndergroundFill: string;
    buildingResidential: string;
    buildingCommercial: string;
    buildingIndustrial: string;
    buildingOffice: string;
    buildingService: string;
    warningPulseStart: string;
    warningPulseEnd: string;
    assetNetwork: string;
    assetTree: string;
    assetProp: string;
    assetDecal: string;
    assetArea: string;
    assetMoving: string;
    darkOutline: string;
    alphaMarqueeFill: number;
    alphaBuildingFill: number;
    alphaOutline: number;
}

export type ColorKey =
    | "marqueeGroundBorder"
    | "marqueeGroundFill"
    | "marqueeUndergroundBorder"
    | "marqueeUndergroundFill"
    | "buildingResidential"
    | "buildingCommercial"
    | "buildingIndustrial"
    | "buildingOffice"
    | "buildingService"
    | "warningPulseStart"
    | "warningPulseEnd"
    | "assetNetwork"
    | "assetTree"
    | "assetProp"
    | "assetDecal"
    | "assetArea"
    | "assetMoving"
    | "darkOutline";

export type AlphaKey = "alphaMarqueeFill" | "alphaBuildingFill" | "alphaOutline";

export type ColorPresetId = "classic" | "neon" | "colorblind" | "pastel";

export const DEFAULT_COLOR_PALETTE: ColorPaletteData = {
    marqueeGroundBorder: "#FF2929",
    marqueeGroundFill: "#F23D38",
    marqueeUndergroundBorder: "#26B8FF",
    marqueeUndergroundFill: "#24C7F5",
    buildingResidential: "#22F56A",
    buildingCommercial: "#26A6FF",
    buildingIndustrial: "#FFCC12",
    buildingOffice: "#B656FF",
    buildingService: "#FF7236",
    warningPulseStart: "#FFDE48",
    warningPulseEnd: "#FF5091",
    assetNetwork: "#38D4FF",
    assetTree: "#38E05A",
    assetProp: "#FFAA33",
    assetDecal: "#E86DFF",
    assetArea: "#2BE3C6",
    assetMoving: "#FFE84D",
    darkOutline: "#080A12",
    alphaMarqueeFill: 0.09,
    alphaBuildingFill: 0.56,
    alphaOutline: 0.90,
};

export interface ColorGroupSpec {
    id: string;
    titleKey: string;
    titleFallback: string;
    items: Array<{
        key: ColorKey;
        labelKey: string;
        fallback: string;
    }>;
}

export const COLOR_GROUPS: ColorGroupSpec[] = [
    {
        id: "marquee",
        titleKey: "MarqueeDelete.ColorGroupMarquee",
        titleFallback: "Marquee Box",
        items: [
            { key: "marqueeGroundBorder", labelKey: "MarqueeDelete.ColorKey_marqueeGroundBorder", fallback: "Ground Border" },
            { key: "marqueeGroundFill", labelKey: "MarqueeDelete.ColorKey_marqueeGroundFill", fallback: "Ground Fill" },
            { key: "marqueeUndergroundBorder", labelKey: "MarqueeDelete.ColorKey_marqueeUndergroundBorder", fallback: "Underground Border" },
            { key: "marqueeUndergroundFill", labelKey: "MarqueeDelete.ColorKey_marqueeUndergroundFill", fallback: "Underground Fill" },
        ],
    },
    {
        id: "buildings",
        titleKey: "MarqueeDelete.ColorGroupBuildings",
        titleFallback: "Buildings",
        items: [
            { key: "buildingResidential", labelKey: "MarqueeDelete.ColorKey_buildingResidential", fallback: "Residential" },
            { key: "buildingCommercial", labelKey: "MarqueeDelete.ColorKey_buildingCommercial", fallback: "Commercial" },
            { key: "buildingIndustrial", labelKey: "MarqueeDelete.ColorKey_buildingIndustrial", fallback: "Industrial" },
            { key: "buildingOffice", labelKey: "MarqueeDelete.ColorKey_buildingOffice", fallback: "Office" },
            { key: "buildingService", labelKey: "MarqueeDelete.ColorKey_buildingService", fallback: "Public / Service" },
        ],
    },
    {
        id: "warning",
        titleKey: "MarqueeDelete.ColorGroupWarning",
        titleFallback: "Threshold Pulse",
        items: [
            { key: "warningPulseStart", labelKey: "MarqueeDelete.ColorKey_warningPulseStart", fallback: "Pulse Warm" },
            { key: "warningPulseEnd", labelKey: "MarqueeDelete.ColorKey_warningPulseEnd", fallback: "Pulse Alert" },
        ],
    },
    {
        id: "assets",
        titleKey: "MarqueeDelete.ColorGroupAssets",
        titleFallback: "Non-Building Assets",
        items: [
            { key: "assetNetwork", labelKey: "MarqueeDelete.ColorKey_assetNetwork", fallback: "Networks / Roads" },
            { key: "assetTree", labelKey: "MarqueeDelete.ColorKey_assetTree", fallback: "Trees" },
            { key: "assetProp", labelKey: "MarqueeDelete.ColorKey_assetProp", fallback: "Props" },
            { key: "assetDecal", labelKey: "MarqueeDelete.ColorKey_assetDecal", fallback: "Decals" },
            { key: "assetArea", labelKey: "MarqueeDelete.ColorKey_assetArea", fallback: "Areas / Surfaces" },
            { key: "assetMoving", labelKey: "MarqueeDelete.ColorKey_assetMoving", fallback: "Vehicles / Citizens" },
        ],
    },
    {
        id: "outline",
        titleKey: "MarqueeDelete.ColorGroupOutline",
        titleFallback: "Dark Outline",
        items: [
            { key: "darkOutline", labelKey: "MarqueeDelete.ColorKey_darkOutline", fallback: "Outline Base" },
        ],
    },
];

export interface AlphaItemSpec {
    key: AlphaKey;
    labelKey: string;
    fallback: string;
    hasHiddenZeroHint?: boolean;
}

export const ALPHA_ITEMS: AlphaItemSpec[] = [
    { key: "alphaMarqueeFill", labelKey: "MarqueeDelete.ColorAlphaMarqueeFill", fallback: "Marquee Fill" },
    { key: "alphaBuildingFill", labelKey: "MarqueeDelete.ColorAlphaBuildingFill", fallback: "Building Lot Fill" },
    { key: "alphaOutline", labelKey: "MarqueeDelete.ColorAlphaOutline", fallback: "Outline", hasHiddenZeroHint: true },
];

export interface PresetThemeSpec {
    id: ColorPresetId;
    labelKey: string;
    fallback: string;
    previewDots: [string, string, string, string];
}

export const PRESET_THEMES: PresetThemeSpec[] = [
    {
        id: "classic",
        labelKey: "MarqueeDelete.ColorPresetClassic",
        fallback: "Classic",
        previewDots: ["#FF2929", "#22F56A", "#26A6FF", "#FFCC12"],
    },
    {
        id: "neon",
        labelKey: "MarqueeDelete.ColorPresetNeon",
        fallback: "High-Contrast Neon",
        previewDots: ["#FF3366", "#39FF14", "#00E5FF", "#D500F9"],
    },
    {
        id: "colorblind",
        labelKey: "MarqueeDelete.ColorPresetColorblind",
        fallback: "Colorblind Safe",
        previewDots: ["#E69F00", "#56B4E9", "#0072B2", "#CC79A7"],
    },
    {
        id: "pastel",
        labelKey: "MarqueeDelete.ColorPresetPastel",
        fallback: "Soft Translucent",
        previewDots: ["#F28482", "#8CE0A8", "#84C7F2", "#C8A8F5"],
    },
];

export const QUICK_SWATCHES: string[] = [
    "#FF2929",
    "#FF5091",
    "#FF7236",
    "#E69F00",
    "#FFCC12",
    "#F0E442",
    "#22F56A",
    "#2BE3C6",
    "#38D4FF",
    "#56B4E9",
    "#26A6FF",
    "#0072B2",
    "#B656FF",
    "#CC79A7",
    "#D55E00",
    "#FFFFFF",
    "#1A202C",
    "#080A12",
];

const COLOR_KEYS: ColorKey[] = [
    "marqueeGroundBorder",
    "marqueeGroundFill",
    "marqueeUndergroundBorder",
    "marqueeUndergroundFill",
    "buildingResidential",
    "buildingCommercial",
    "buildingIndustrial",
    "buildingOffice",
    "buildingService",
    "warningPulseStart",
    "warningPulseEnd",
    "assetNetwork",
    "assetTree",
    "assetProp",
    "assetDecal",
    "assetArea",
    "assetMoving",
    "darkOutline",
];

const ALPHA_KEYS: AlphaKey[] = [
    "alphaMarqueeFill",
    "alphaBuildingFill",
    "alphaOutline",
];

export function normalizeHex(raw: unknown): string | null {
    if (typeof raw !== "string") {
        return null;
    }

    const trimmed = raw.trim();
    if (!/^#[0-9A-Fa-f]{6}$/.test(trimmed)) {
        return null;
    }

    return trimmed.toUpperCase();
}

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
    const normalized = normalizeHex(hex) ?? "#FFFFFF";
    return {
        r: parseInt(normalized.slice(1, 3), 16),
        g: parseInt(normalized.slice(3, 5), 16),
        b: parseInt(normalized.slice(5, 7), 16),
    };
}

function byteToHex(n: number): string {
    const clamped = Math.max(0, Math.min(255, Math.round(n)));
    return clamped.toString(16).toUpperCase().padStart(2, "0");
}

export function rgbToHex(r: number, g: number, b: number): string {
    return `#${byteToHex(r)}${byteToHex(g)}${byteToHex(b)}`;
}
