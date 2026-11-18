using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MarqueeDelete
{
    public struct Rgb24 : IEquatable<Rgb24>
    {
        public byte R;
        public byte G;
        public byte B;

        public Rgb24(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public bool Equals(Rgb24 other) => R == other.R && G == other.G && B == other.B;
        public override bool Equals(object obj) => obj is Rgb24 other && Equals(other);
        public override int GetHashCode() => (R << 16) | (G << 8) | B;
        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    }

    public sealed class ColorPaletteData
    {
        // Marquee (4)
        public string MarqueeGroundBorder = ColorLibrary.DefaultMarqueeGroundBorder;
        public string MarqueeGroundFill = ColorLibrary.DefaultMarqueeGroundFill;
        public string MarqueeUndergroundBorder = ColorLibrary.DefaultMarqueeUndergroundBorder;
        public string MarqueeUndergroundFill = ColorLibrary.DefaultMarqueeUndergroundFill;

        // Buildings (5)
        public string BuildingResidential = ColorLibrary.DefaultBuildingResidential;
        public string BuildingCommercial = ColorLibrary.DefaultBuildingCommercial;
        public string BuildingIndustrial = ColorLibrary.DefaultBuildingIndustrial;
        public string BuildingOffice = ColorLibrary.DefaultBuildingOffice;
        public string BuildingService = ColorLibrary.DefaultBuildingService;

        // Warning pulse (2)
        public string WarningPulseStart = ColorLibrary.DefaultWarningPulseStart;
        public string WarningPulseEnd = ColorLibrary.DefaultWarningPulseEnd;

        // Non-building assets (6)
        public string AssetNetwork = ColorLibrary.DefaultAssetNetwork;
        public string AssetTree = ColorLibrary.DefaultAssetTree;
        public string AssetProp = ColorLibrary.DefaultAssetProp;
        public string AssetDecal = ColorLibrary.DefaultAssetDecal;
        public string AssetArea = ColorLibrary.DefaultAssetArea;
        public string AssetMoving = ColorLibrary.DefaultAssetMoving;

        // Outline (1)
        public string DarkOutline = ColorLibrary.DefaultDarkOutline;

        // Opacities (3)
        public float AlphaMarqueeFill = ColorLibrary.DefaultAlphaMarqueeFill;
        public float AlphaBuildingFill = ColorLibrary.DefaultAlphaBuildingFill;
        public float AlphaOutline = ColorLibrary.DefaultAlphaOutline;

        public ColorPaletteData Clone()
        {
            return (ColorPaletteData)MemberwiseClone();
        }
    }

    public static class ColorLibrary
    {
        // Canonical defaults matching existing MarqueeDeleteToolSystem & BuildingOverlay colors
        public const string DefaultMarqueeGroundBorder = "#FF2929";
        public const string DefaultMarqueeGroundFill = "#F23D38";
        public const string DefaultMarqueeUndergroundBorder = "#26B8FF";
        public const string DefaultMarqueeUndergroundFill = "#24C7F5";

        public const string DefaultBuildingResidential = "#22F56A";
        public const string DefaultBuildingCommercial = "#26A6FF";
        public const string DefaultBuildingIndustrial = "#FFCC12";
        public const string DefaultBuildingOffice = "#B656FF";
        public const string DefaultBuildingService = "#FF7236";

        public const string DefaultWarningPulseStart = "#FFDE48";
        public const string DefaultWarningPulseEnd = "#FF5091";

        public const string DefaultAssetNetwork = "#38D4FF";
        public const string DefaultAssetTree = "#38E05A";
        public const string DefaultAssetProp = "#FFAA33";
        public const string DefaultAssetDecal = "#E86DFF";
        public const string DefaultAssetArea = "#2BE3C6";
        public const string DefaultAssetMoving = "#FFE84D";

        public const string DefaultDarkOutline = "#080A12";

        public const float DefaultAlphaMarqueeFill = 0.09f;
        public const float DefaultAlphaBuildingFill = 0.56f;
        public const float DefaultAlphaOutline = 0.90f;

        public static ColorPaletteData CreateDefaultPalette()
        {
            return new ColorPaletteData();
        }

        public static bool TryParseHexRgb(string raw, out Rgb24 rgb)
        {
            rgb = default(Rgb24);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string trimmed = raw.Trim();
            if (trimmed.Length != 7 || trimmed[0] != '#')
            {
                return false;
            }

            if (!TryParseHexByte(trimmed[1], trimmed[2], out byte r) ||
                !TryParseHexByte(trimmed[3], trimmed[4], out byte g) ||
                !TryParseHexByte(trimmed[5], trimmed[6], out byte b))
            {
                return false;
            }

            rgb = new Rgb24(r, g, b);
            return true;
        }

        public static string NormalizeHex(string raw, string fallbackHex)
        {
            if (TryParseHexRgb(raw, out Rgb24 parsed))
            {
                return parsed.ToHex();
            }

            return TryParseHexRgb(fallbackHex, out Rgb24 fallbackRgb)
                ? fallbackRgb.ToHex()
                : "#FFFFFF";
        }

        public static Rgb24 ResolveRgb(string raw, string fallbackHex)
        {
            if (TryParseHexRgb(raw, out Rgb24 parsed))
            {
                return parsed;
            }

            return TryParseHexRgb(fallbackHex, out Rgb24 fallbackRgb)
                ? fallbackRgb
                : new Rgb24(255, 255, 255);
        }

        public static float NormalizeAlpha(float raw, float fallbackAlpha)
        {
            if (float.IsNaN(raw) || float.IsInfinity(raw) || raw < 0f)
            {
                return Clamp01OrDefault(fallbackAlpha, 0.5f);
            }

            if (raw > 1f)
            {
                // Treat wildly out-of-range garbage (e.g. legacy percentage/corruption > 1.0001) as invalid -> fallback.
                if (raw > 1.0001f)
                {
                    return Clamp01OrDefault(fallbackAlpha, 0.5f);
                }
                return 1f;
            }

            return raw;
        }

        public static void Sanitize(ColorPaletteData palette)
        {
            if (palette == null)
            {
                return;
            }

            palette.MarqueeGroundBorder = NormalizeHex(palette.MarqueeGroundBorder, DefaultMarqueeGroundBorder);
            palette.MarqueeGroundFill = NormalizeHex(palette.MarqueeGroundFill, DefaultMarqueeGroundFill);
            palette.MarqueeUndergroundBorder = NormalizeHex(palette.MarqueeUndergroundBorder, DefaultMarqueeUndergroundBorder);
            palette.MarqueeUndergroundFill = NormalizeHex(palette.MarqueeUndergroundFill, DefaultMarqueeUndergroundFill);

            palette.BuildingResidential = NormalizeHex(palette.BuildingResidential, DefaultBuildingResidential);
            palette.BuildingCommercial = NormalizeHex(palette.BuildingCommercial, DefaultBuildingCommercial);
            palette.BuildingIndustrial = NormalizeHex(palette.BuildingIndustrial, DefaultBuildingIndustrial);
            palette.BuildingOffice = NormalizeHex(palette.BuildingOffice, DefaultBuildingOffice);
            palette.BuildingService = NormalizeHex(palette.BuildingService, DefaultBuildingService);

            palette.WarningPulseStart = NormalizeHex(palette.WarningPulseStart, DefaultWarningPulseStart);
            palette.WarningPulseEnd = NormalizeHex(palette.WarningPulseEnd, DefaultWarningPulseEnd);

            palette.AssetNetwork = NormalizeHex(palette.AssetNetwork, DefaultAssetNetwork);
            palette.AssetTree = NormalizeHex(palette.AssetTree, DefaultAssetTree);
            palette.AssetProp = NormalizeHex(palette.AssetProp, DefaultAssetProp);
            palette.AssetDecal = NormalizeHex(palette.AssetDecal, DefaultAssetDecal);
            palette.AssetArea = NormalizeHex(palette.AssetArea, DefaultAssetArea);
            palette.AssetMoving = NormalizeHex(palette.AssetMoving, DefaultAssetMoving);

            palette.DarkOutline = NormalizeHex(palette.DarkOutline, DefaultDarkOutline);

            palette.AlphaMarqueeFill = NormalizeAlpha(palette.AlphaMarqueeFill, DefaultAlphaMarqueeFill);
            palette.AlphaBuildingFill = NormalizeAlpha(palette.AlphaBuildingFill, DefaultAlphaBuildingFill);
            palette.AlphaOutline = NormalizeAlpha(palette.AlphaOutline, DefaultAlphaOutline);
        }

        public static bool SetColorByKey(ColorPaletteData palette, string key, string hex)
        {
            if (palette == null || string.IsNullOrEmpty(key) || !TryParseHexRgb(hex, out Rgb24 rgb))
            {
                return false;
            }

            string normalized = rgb.ToHex();
            switch (key)
            {
                case "marqueeGroundBorder": palette.MarqueeGroundBorder = normalized; return true;
                case "marqueeGroundFill": palette.MarqueeGroundFill = normalized; return true;
                case "marqueeUndergroundBorder": palette.MarqueeUndergroundBorder = normalized; return true;
                case "marqueeUndergroundFill": palette.MarqueeUndergroundFill = normalized; return true;
                case "buildingResidential": palette.BuildingResidential = normalized; return true;
                case "buildingCommercial": palette.BuildingCommercial = normalized; return true;
                case "buildingIndustrial": palette.BuildingIndustrial = normalized; return true;
                case "buildingOffice": palette.BuildingOffice = normalized; return true;
                case "buildingService": palette.BuildingService = normalized; return true;
                case "warningPulseStart": palette.WarningPulseStart = normalized; return true;
                case "warningPulseEnd": palette.WarningPulseEnd = normalized; return true;
                case "assetNetwork": palette.AssetNetwork = normalized; return true;
                case "assetTree": palette.AssetTree = normalized; return true;
                case "assetProp": palette.AssetProp = normalized; return true;
                case "assetDecal": palette.AssetDecal = normalized; return true;
                case "assetArea": palette.AssetArea = normalized; return true;
                case "assetMoving": palette.AssetMoving = normalized; return true;
                case "darkOutline": palette.DarkOutline = normalized; return true;
                default: return false;
            }
        }

        public static bool SetAlphaByKey(ColorPaletteData palette, string key, float value)
        {
            if (palette == null || string.IsNullOrEmpty(key) || float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }

            float clamped = value < 0f ? 0f : (value > 1f ? 1f : value);
            switch (key)
            {
                case "alphaMarqueeFill": palette.AlphaMarqueeFill = clamped; return true;
                case "alphaBuildingFill": palette.AlphaBuildingFill = clamped; return true;
                case "alphaOutline": palette.AlphaOutline = clamped; return true;
                default: return false;
            }
        }

        public static string ToJson(ColorPaletteData palette)
        {
            ColorPaletteData p = palette != null ? palette.Clone() : CreateDefaultPalette();
            Sanitize(p);

            var sb = new StringBuilder(640);
            sb.Append('{');
            AppendStringProp(sb, "marqueeGroundBorder", p.MarqueeGroundBorder, true);
            AppendStringProp(sb, "marqueeGroundFill", p.MarqueeGroundFill, false);
            AppendStringProp(sb, "marqueeUndergroundBorder", p.MarqueeUndergroundBorder, false);
            AppendStringProp(sb, "marqueeUndergroundFill", p.MarqueeUndergroundFill, false);

            AppendStringProp(sb, "buildingResidential", p.BuildingResidential, false);
            AppendStringProp(sb, "buildingCommercial", p.BuildingCommercial, false);
            AppendStringProp(sb, "buildingIndustrial", p.BuildingIndustrial, false);
            AppendStringProp(sb, "buildingOffice", p.BuildingOffice, false);
            AppendStringProp(sb, "buildingService", p.BuildingService, false);

            AppendStringProp(sb, "warningPulseStart", p.WarningPulseStart, false);
            AppendStringProp(sb, "warningPulseEnd", p.WarningPulseEnd, false);

            AppendStringProp(sb, "assetNetwork", p.AssetNetwork, false);
            AppendStringProp(sb, "assetTree", p.AssetTree, false);
            AppendStringProp(sb, "assetProp", p.AssetProp, false);
            AppendStringProp(sb, "assetDecal", p.AssetDecal, false);
            AppendStringProp(sb, "assetArea", p.AssetArea, false);
            AppendStringProp(sb, "assetMoving", p.AssetMoving, false);

            AppendStringProp(sb, "darkOutline", p.DarkOutline, false);

            AppendFloatProp(sb, "alphaMarqueeFill", p.AlphaMarqueeFill);
            AppendFloatProp(sb, "alphaBuildingFill", p.AlphaBuildingFill);
            AppendFloatProp(sb, "alphaOutline", p.AlphaOutline);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendStringProp(StringBuilder sb, string key, string value, bool first)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(key).Append("\":\"").Append(value).Append('"');
        }

        private static void AppendFloatProp(StringBuilder sb, string key, float value)
        {
            sb.Append(",\"").Append(key).Append("\":").Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static float Clamp01OrDefault(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f || v > 1f)
            {
                return fallback;
            }
            return v;
        }

        private static bool TryParseHexByte(char hi, char lo, out byte value)
        {
            value = 0;
            int a = HexNibble(hi);
            int b = HexNibble(lo);
            if (a < 0 || b < 0)
            {
                return false;
            }

            value = (byte)((a << 4) | b);
            return true;
        }

        private static int HexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }

        public static bool TryGetPreset(string presetId, out ColorPaletteData palette)
        {
            switch (presetId)
            {
                case "classic":
                    palette = CreateDefaultPalette();
                    return true;
                case "neon":
                    palette = new ColorPaletteData
                    {
                        MarqueeGroundBorder = "#FF3366",
                        MarqueeGroundFill = "#FF2E63",
                        MarqueeUndergroundBorder = "#00F0FF",
                        MarqueeUndergroundFill = "#08D9D6",
                        BuildingResidential = "#39FF14",
                        BuildingCommercial = "#00E5FF",
                        BuildingIndustrial = "#FFE600",
                        BuildingOffice = "#D500F9",
                        BuildingService = "#FF6D00",
                        WarningPulseStart = "#FFF200",
                        WarningPulseEnd = "#FF1744",
                        AssetNetwork = "#00E5FF",
                        AssetTree = "#2BFF88",
                        AssetProp = "#FFB300",
                        AssetDecal = "#F542FF",
                        AssetArea = "#00FFC8",
                        AssetMoving = "#FFEE00",
                        DarkOutline = "#05070C",
                        AlphaMarqueeFill = 0.11f,
                        AlphaBuildingFill = 0.62f,
                        AlphaOutline = 0.95f,
                    };
                    return true;
                case "colorblind":
                    // Okabe-Ito inspired palette: separates categories via Blue / Amber / Yellow / Plum / Cream
                    // and luminance contrast without relying on Red-vs-Green distinction.
                    palette = new ColorPaletteData
                    {
                        MarqueeGroundBorder = "#E69F00",
                        MarqueeGroundFill = "#F0E442",
                        MarqueeUndergroundBorder = "#56B4E9",
                        MarqueeUndergroundFill = "#0072B2",
                        BuildingResidential = "#56B4E9",
                        BuildingCommercial = "#0072B2",
                        BuildingIndustrial = "#E69F00",
                        BuildingOffice = "#CC79A7",
                        BuildingService = "#D55E00",
                        WarningPulseStart = "#F0E442",
                        WarningPulseEnd = "#FFF7D6",
                        AssetNetwork = "#56B4E9",
                        AssetTree = "#F0E442",
                        AssetProp = "#E69F00",
                        AssetDecal = "#CC79A7",
                        AssetArea = "#88CCEE",
                        AssetMoving = "#FFFFFF",
                        DarkOutline = "#070B14",
                        AlphaMarqueeFill = 0.11f,
                        AlphaBuildingFill = 0.62f,
                        AlphaOutline = 0.95f,
                    };
                    return true;
                case "pastel":
                    palette = new ColorPaletteData
                    {
                        MarqueeGroundBorder = "#F28482",
                        MarqueeGroundFill = "#F5CAC3",
                        MarqueeUndergroundBorder = "#84A59D",
                        MarqueeUndergroundFill = "#A8DADC",
                        BuildingResidential = "#8CE0A8",
                        BuildingCommercial = "#84C7F2",
                        BuildingIndustrial = "#F6D785",
                        BuildingOffice = "#C8A8F5",
                        BuildingService = "#F7AE88",
                        WarningPulseStart = "#FCE588",
                        WarningPulseEnd = "#F48C9B",
                        AssetNetwork = "#8CD9F0",
                        AssetTree = "#8FE0A4",
                        AssetProp = "#F5C77E",
                        AssetDecal = "#E0A6F5",
                        AssetArea = "#8CE6D5",
                        AssetMoving = "#F7E88B",
                        DarkOutline = "#1A202C",
                        AlphaMarqueeFill = 0.06f,
                        AlphaBuildingFill = 0.36f,
                        AlphaOutline = 0.72f,
                    };
                    return true;
                default:
                    palette = null;
                    return false;
            }
        }
    }

    /// <summary>
    /// Single-target coordinator for delayed settings saves during UI color/alpha slider drags.
    /// Captures the target Settings instance at schedule time so pending edits still persist
    /// even if global Settings references are cleared before teardown flush.
    /// Contract: tracks one active target at a time; scheduling a different target instance flushes
    /// any prior pending target first rather than silently dropping it, whereas rescheduling the
    /// same instance only resets the debounce timer to coalesce edits.
    /// </summary>
    public sealed class DeferredSaveCoordinator
    {
        private object m_PendingTarget;
        private Action<object> m_PendingSaveAction;
        private float m_DelayRemaining = -1f;

        public bool HasPending => m_DelayRemaining >= 0f && m_PendingTarget != null && m_PendingSaveAction != null;

        public void Schedule<T>(T target, Action<T> saveAction, float delaySeconds) where T : class
        {
            if (target == null || saveAction == null)
            {
                return;
            }

            if (HasPending && !ReferenceEquals(m_PendingTarget, target))
            {
                Flush();
            }

            m_PendingTarget = target;
            m_PendingSaveAction = obj => saveAction((T)obj);
            m_DelayRemaining = delaySeconds < 0f ? 0f : delaySeconds;
        }

        public void SaveNow<T>(T target, Action<T> saveAction) where T : class
        {
            if (HasPending && !ReferenceEquals(m_PendingTarget, target))
            {
                Flush();
            }
            else
            {
                Cancel();
            }

            if (target != null && saveAction != null)
            {
                saveAction(target);
            }
        }

        public bool Tick(float deltaSeconds)
        {
            if (!HasPending)
            {
                return false;
            }

            if (!float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds) && deltaSeconds > 0f)
            {
                m_DelayRemaining -= deltaSeconds;
            }

            if (m_DelayRemaining > 0f)
            {
                return false;
            }

            return Flush();
        }

        public bool Flush()
        {
            if (!HasPending)
            {
                Cancel();
                return false;
            }

            object target = m_PendingTarget;
            Action<object> action = m_PendingSaveAction;
            Cancel();
            action(target);
            return true;
        }

        public void Cancel()
        {
            m_DelayRemaining = -1f;
            m_PendingTarget = null;
            m_PendingSaveAction = null;
        }
    }
}
