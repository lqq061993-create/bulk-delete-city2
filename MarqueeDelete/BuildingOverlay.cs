using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MarqueeDelete
{
    public sealed partial class MarqueeDeleteToolSystem
    {
        private const int MaxDetailedBuildings = 900;
        private const int MaxVisibleBuildings = 8192;
        private const int ZoneCount = 5;

        private struct ResolvedOverlayColors
        {
            public Color MarqueeGroundBorder;
            public Color MarqueeGroundFill;
            public Color MarqueeUndergroundBorder;
            public Color MarqueeUndergroundFill;

            public Color BuildingResidential;
            public Color BuildingCommercial;
            public Color BuildingIndustrial;
            public Color BuildingOffice;
            public Color BuildingService;

            public Color WarningPulseStart;
            public Color WarningPulseEnd;

            public Color AssetNetwork;
            public Color AssetTree;
            public Color AssetProp;
            public Color AssetDecal;
            public Color AssetArea;
            public Color AssetMoving;

            public Color DarkOutline;
            public float MarqueeFillAlpha;
            public float BuildingFillAlpha;
            public float OutlineAlpha;

            public Color GetBuildingColor(int zone)
            {
                switch (zone)
                {
                    case 0: return BuildingResidential;
                    case 1: return BuildingCommercial;
                    case 2: return BuildingIndustrial;
                    case 3: return BuildingOffice;
                    default: return BuildingService;
                }
            }

            public static ResolvedOverlayColors FromSettings(Settings settings)
            {
                ColorPaletteData p = settings != null
                    ? settings.BuildColorPalette()
                    : ColorLibrary.CreateDefaultPalette();

                float outlineAlpha = ColorLibrary.NormalizeAlpha(p.AlphaOutline, ColorLibrary.DefaultAlphaOutline);
                return new ResolvedOverlayColors
                {
                    MarqueeGroundBorder = ToColor(p.MarqueeGroundBorder, ColorLibrary.DefaultMarqueeGroundBorder, 1f),
                    MarqueeGroundFill = ToColor(p.MarqueeGroundFill, ColorLibrary.DefaultMarqueeGroundFill, 1f),
                    MarqueeUndergroundBorder = ToColor(p.MarqueeUndergroundBorder, ColorLibrary.DefaultMarqueeUndergroundBorder, 1f),
                    MarqueeUndergroundFill = ToColor(p.MarqueeUndergroundFill, ColorLibrary.DefaultMarqueeUndergroundFill, 1f),

                    BuildingResidential = ToColor(p.BuildingResidential, ColorLibrary.DefaultBuildingResidential, 1f),
                    BuildingCommercial = ToColor(p.BuildingCommercial, ColorLibrary.DefaultBuildingCommercial, 1f),
                    BuildingIndustrial = ToColor(p.BuildingIndustrial, ColorLibrary.DefaultBuildingIndustrial, 1f),
                    BuildingOffice = ToColor(p.BuildingOffice, ColorLibrary.DefaultBuildingOffice, 1f),
                    BuildingService = ToColor(p.BuildingService, ColorLibrary.DefaultBuildingService, 1f),

                    WarningPulseStart = ToColor(p.WarningPulseStart, ColorLibrary.DefaultWarningPulseStart, 1f),
                    WarningPulseEnd = ToColor(p.WarningPulseEnd, ColorLibrary.DefaultWarningPulseEnd, 1f),

                    AssetNetwork = ToColor(p.AssetNetwork, ColorLibrary.DefaultAssetNetwork, 1f),
                    AssetTree = ToColor(p.AssetTree, ColorLibrary.DefaultAssetTree, 1f),
                    AssetProp = ToColor(p.AssetProp, ColorLibrary.DefaultAssetProp, 1f),
                    AssetDecal = ToColor(p.AssetDecal, ColorLibrary.DefaultAssetDecal, 1f),
                    AssetArea = ToColor(p.AssetArea, ColorLibrary.DefaultAssetArea, 1f),
                    AssetMoving = ToColor(p.AssetMoving, ColorLibrary.DefaultAssetMoving, 1f),

                    DarkOutline = ToColor(p.DarkOutline, ColorLibrary.DefaultDarkOutline, outlineAlpha),
                    MarqueeFillAlpha = ColorLibrary.NormalizeAlpha(p.AlphaMarqueeFill, ColorLibrary.DefaultAlphaMarqueeFill),
                    BuildingFillAlpha = ColorLibrary.NormalizeAlpha(p.AlphaBuildingFill, ColorLibrary.DefaultAlphaBuildingFill),
                    OutlineAlpha = outlineAlpha,
                };
            }

            private static Color ToColor(string rawHex, string fallbackHex, float alpha)
            {
                Rgb24 rgb = ColorLibrary.ResolveRgb(rawHex, fallbackHex);
                return new Color(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f, alpha);
            }
        }

        private int m_CachedColorRevision = -1;
        private ResolvedOverlayColors m_ResolvedColors;
        private readonly List<BuildingMarker> m_BuildingMarkers = new List<BuildingMarker>();
        private readonly List<int> m_VisibleBuildings = new List<int>(MaxDetailedBuildings);
        private SelectionResult m_OverlaySelection;
        private float m_NextBuildingRefresh;

        private ResolvedOverlayColors GetResolvedColors()
        {
            Settings s = Mod.Settings;
            int rev = s != null ? s.GetColorRevision() : -2;
            if (m_CachedColorRevision != rev)
            {
                m_ResolvedColors = ResolvedOverlayColors.FromSettings(s);
                m_CachedColorRevision = rev;
            }
            return m_ResolvedColors;
        }

        private struct BuildingMarker
        {
            public Entity Entity;
            public float3 Center;
            public float3 Corner0, Corner1, Corner2, Corner3;
            public float Width;
            public float Depth;
            public int Zone;
            public bool Important;
        }

        private void ClearBuildingOverlay()
        {
            m_OverlaySelection = null;
            m_BuildingMarkers.Clear();
            m_VisibleBuildings.Clear();
            m_NextBuildingRefresh = 0;
        }

        private void DrawBuildingOverlays(
            ref OverlayRenderSystem.Buffer buffer,
            SelectionResult selection,
            float pulse,
            in ResolvedOverlayColors colors)
        {
            if (selection == null || selection.Breakdown.Buildings == 0)
            {
                if (m_OverlaySelection != null) ClearBuildingOverlay();
                return;
            }

            if (Mod.Settings != null && Mod.Settings.OverlayDetail == OverlayDetailLevel.Minimal)
            {
                if (m_OverlaySelection != null) ClearBuildingOverlay();
                return;
            }

            bool selectionChanged = m_OverlaySelection != selection;
            if (selectionChanged)
            {
                m_OverlaySelection = selection;
                BuildBuildingMarkers(selection);
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (selectionChanged || now >= m_NextBuildingRefresh)
            {
                RefreshVisibleBuildings(Camera.main);
                m_NextBuildingRefresh = now + 0.15f;
            }

            bool isStandard = Mod.Settings == null || Mod.Settings.OverlayDetail == OverlayDetailLevel.Standard;
            bool compactMode = isStandard || m_VisibleBuildings.Count > MaxDetailedBuildings;
            for (int i = 0; i < m_VisibleBuildings.Count; i++)
            {
                BuildingMarker marker = m_BuildingMarkers[m_VisibleBuildings[i]];
                if (compactMode && !marker.Important)
                {
                    DrawBuildingLotCompact(ref buffer, marker, in colors);
                }
                else
                {
                    DrawBuildingLot(ref buffer, marker, pulse, in colors);
                }
            }
        }

        private void BuildBuildingMarkers(SelectionResult selection)
        {
            m_BuildingMarkers.Clear();
            foreach (var item in selection.Items)
            {
                if (item.Category != SelectionCategory.Building ||
                    !EntityManager.Exists(item.Entity) || EntityManager.HasComponent<Deleted>(item.Entity) ||
                    !EntityManager.HasComponent<Game.Objects.Transform>(item.Entity))
                    continue;

                var transform = EntityManager.GetComponentData<Game.Objects.Transform>(item.Entity);
                float3 center = transform.m_Position;
                if (!math.all(math.isfinite(center)) || !math.all(math.isfinite(transform.m_Rotation.value)))
                    continue;

                float2 size = item.FootprintXZ;
                if (!math.all(math.isfinite(size))) size = new float2(4f);
                size = math.select(new float2(4f), size, size >= 1f);
                float2 half = size * 0.5f;
                int zone = (int)item.ZoneType - (int)BuildingZoneType.Residential;
                m_BuildingMarkers.Add(new BuildingMarker
                {
                    Entity = item.Entity,
                    Center = center,
                    Corner0 = center + math.mul(transform.m_Rotation, new float3(-half.x, 0.1f, -half.y)),
                    Corner1 = center + math.mul(transform.m_Rotation, new float3(half.x, 0.1f, -half.y)),
                    Corner2 = center + math.mul(transform.m_Rotation, new float3(half.x, 0.1f, half.y)),
                    Corner3 = center + math.mul(transform.m_Rotation, new float3(-half.x, 0.1f, half.y)),
                    Width = size.x,
                    Depth = size.y,
                    Zone = zone >= 0 && zone < ZoneCount ? zone : ZoneCount - 1,
                    Important = item.ImportantKind != ImportantBuildingKind.None,
                });
            }
        }

        private void RefreshVisibleBuildings(Camera camera)
        {
            m_VisibleBuildings.Clear();
            bool checkViewport = m_BuildingMarkers.Count > MaxDetailedBuildings && camera != null;
            for (int i = 0; i < m_BuildingMarkers.Count; i++)
            {
                BuildingMarker marker = m_BuildingMarkers[i];
                if (!IsBuildingMarkerAlive(marker.Entity) || (checkViewport && !IsInBuildingViewport(camera, marker.Center)))
                    continue;

                if (marker.Important || m_VisibleBuildings.Count < MaxVisibleBuildings)
                {
                    m_VisibleBuildings.Add(i);
                }
            }
        }

        private static bool IsInBuildingViewport(Camera camera, float3 world)
        {
            Vector3 projected = camera.WorldToViewportPoint(new Vector3(world.x, world.y, world.z));
            return projected.z > 0f && projected.x >= -0.15f && projected.x <= 1.15f &&
                projected.y >= -0.15f && projected.y <= 1.15f;
        }

        private bool IsBuildingMarkerAlive(Entity entity) =>
            EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity);

        private void DrawBuildingLot(
            ref OverlayRenderSystem.Buffer buffer,
            BuildingMarker marker,
            float pulse,
            in ResolvedOverlayColors colors)
        {
            Color color = colors.GetBuildingColor(marker.Zone);
            Color fill = new Color(color.r, color.g, color.b, colors.BuildingFillAlpha);
            float3 left = (marker.Corner0 + marker.Corner3) * 0.5f;
            float3 right = (marker.Corner1 + marker.Corner2) * 0.5f;
            buffer.DrawLine(fill, fill, 0f, OverlayRenderSystem.StyleFlags.Projected,
                new Line3.Segment(left, right), marker.Depth, float2.zero);

            float width = math.clamp(math.min(marker.Width, marker.Depth) * 0.06f, 0.65f, 1.75f);
            float edgeOutlineWidth = colors.OutlineAlpha <= 0.001f ? 0f : 0.2f;
            DrawProjectedEdge(ref buffer, colors.DarkOutline, color, width, marker.Corner0, marker.Corner1, edgeOutlineWidth);
            DrawProjectedEdge(ref buffer, colors.DarkOutline, color, width, marker.Corner1, marker.Corner2, edgeOutlineWidth);
            DrawProjectedEdge(ref buffer, colors.DarkOutline, color, width, marker.Corner2, marker.Corner3, edgeOutlineWidth);
            DrawProjectedEdge(ref buffer, colors.DarkOutline, color, width, marker.Corner3, marker.Corner0, edgeOutlineWidth);

            if (marker.Important)
            {
                Color warning = Color.Lerp(colors.WarningPulseStart, colors.WarningPulseEnd, pulse);
                float diameter = math.clamp(marker.Depth * 0.27f, 3f, 10f);
                float circleOutlineWidth = colors.OutlineAlpha <= 0.001f ? 0f : 0.35f;
                buffer.DrawCircle(colors.DarkOutline, warning, circleOutlineWidth, OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f), marker.Center + new float3(0f, 0.2f, 0f), diameter);
            }
        }

        private void DrawBuildingLotCompact(
            ref OverlayRenderSystem.Buffer buffer,
            BuildingMarker marker,
            in ResolvedOverlayColors colors)
        {
            Color color = colors.GetBuildingColor(marker.Zone);
            float compactFillAlpha = math.clamp(colors.BuildingFillAlpha * 1.18f, colors.BuildingFillAlpha, 0.95f);
            Color fill = new Color(color.r, color.g, color.b, compactFillAlpha);
            Color outline = new Color(color.r, color.g, color.b, 0.96f);
            float3 left = (marker.Corner0 + marker.Corner3) * 0.5f;
            float3 right = (marker.Corner1 + marker.Corner2) * 0.5f;
            float outlineWidth = colors.OutlineAlpha <= 0.001f
                ? 0f
                : math.clamp(math.min(marker.Width, marker.Depth) * 0.08f, 0.5f, 1.5f);
            buffer.DrawLine(outline, fill, outlineWidth, OverlayRenderSystem.StyleFlags.Projected,
                new Line3.Segment(left, right), marker.Depth, float2.zero);
        }
    }
}
