using System;
using System.Collections.Generic;

namespace MarqueeDelete
{
    public enum SelectionCategory
    {
        None = 0,
        Building = 1,
        Tree = 2,
        Prop = 3,
        Decal = 4,
        Moving = 5,
        Network = 6,
        Area = 7,
    }

    public enum BuildingZoneType : byte
    {
        None = 0,
        Residential = 1,
        Commercial = 2,
        Industrial = 3,
        Office = 4,
        Other = 5,
    }

    [Flags]
    public enum ImportantBuildingKind : byte
    {
        None = 0,
        Service = 1 << 0,
        Unique = 1 << 1,
        Signature = 1 << 2,
    }

    public enum NetworkSubKind : byte
    {
        None = 0,
        Road = 1,
        Track = 2,
        Pipeline = 3,
        PowerLine = 4,
        Other = 5,
    }

    [Flags]
    public enum SelectionFilterMask
    {
        None = 0,
        Buildings = 1 << 0,
        Trees = 1 << 1,
        Props = 1 << 2,
        Decals = 1 << 3,
        Moving = 1 << 4,
        Networks = 1 << 5,
        Areas = 1 << 6,
        Residential = 1 << 7,
        Commercial = 1 << 8,
        Industrial = 1 << 9,
        Office = 1 << 10,
        NetworkRoads = 1 << 11,
        NetworkTracks = 1 << 12,
        NetworkPipelines = 1 << 13,
        NetworkPowerLines = 1 << 14,
        NetworkSubfiltersSpecified = 1 << 15,
        NetworkOther = 1 << 16,
        AllNetworkSubtypes = NetworkRoads | NetworkTracks | NetworkPipelines | NetworkPowerLines | NetworkOther,
        AllZones = Residential | Commercial | Industrial | Office,
        DefaultStatic = Buildings | AllZones | Trees | Props | Decals,
        All = Buildings | AllZones | Trees | Props | Decals | Moving | Networks | Areas,
    }

    public enum FilterSkipReason
    {
        Accepted = 0,
        InvalidOrInactive = 1,
        HasOwnerChild = 2,
        AttachedChild = 3,
        ProtectedOutsideConnection = 4,
        ProtectedUnique = 5,
        UnsafeAreaType = 6,
        FilteredOutByCategory = 7,
        UnknownCategory = 8,
        WrongElevationLayer = 9,
    }

    public struct SafetyOptions
    {
        public bool ProtectOutsideConnections;
        public bool ProtectUniqueBuildings;

        public static SafetyOptions Default => new SafetyOptions
        {
            ProtectOutsideConnections = true,
            ProtectUniqueBuildings = false,
        };
    }

    public struct EntityFlagsSnapshot
    {
        public bool IsNull;
        public bool IsDeleted;
        public bool IsHidden;
        public bool IsTemp;
        public bool IsOverridden;
        public bool IsTerrain;
        public bool IsPlaceholder;
        public bool IsMarker;

        public bool HasOwner;
        public bool HasAttachedParent;

        public bool IsMapTile;
        public bool IsDistrict;

        public bool IsOutsideConnection;
        public bool IsUniqueObject;

        public bool IsBuilding;
        public bool IsMoving;
        public bool IsPlant;
        public bool IsPillar;
        public bool IsObjectGeometry;
        public bool HasObjectSurface;
        public bool HasNetObject;
        public bool IsNetwork;
        public bool IsSafeArea;
        public bool IsUnderground;

        public BuildingZoneType ZoneType;
        public ImportantBuildingKind ImportantKind;
        public NetworkSubKind NetworkKind;
        public float FootprintWidth;
        public float FootprintDepth;
    }

    public struct SelectionBreakdown
    {
        public int Buildings;
        public int Trees;
        public int Props;
        public int Decals;
        public int Moving;
        public int Networks;
        public int Areas;

        public int Residential;
        public int Commercial;
        public int Industrial;
        public int Office;
        public int OtherBuildings;
        public int ImportantBuildings;
        public int ServiceBuildings;
        public int UniqueBuildings;
        public int SignatureBuildings;

        public int Total => Buildings + Trees + Props + Decals + Moving + Networks + Areas;

        public void Add(SelectionCategory category) => Add(category, BuildingZoneType.None, ImportantBuildingKind.None);

        public void Add(SelectionCategory category, BuildingZoneType zoneType, ImportantBuildingKind importantKind)
        {
            switch (category)
            {
                case SelectionCategory.Building:
                    Buildings++;
                    switch (zoneType)
                    {
                        case BuildingZoneType.Residential: Residential++; break;
                        case BuildingZoneType.Commercial: Commercial++; break;
                        case BuildingZoneType.Industrial: Industrial++; break;
                        case BuildingZoneType.Office: Office++; break;
                        default: OtherBuildings++; break;
                    }
                    AddImportant(importantKind);
                    break;
                case SelectionCategory.Tree: Trees++; break;
                case SelectionCategory.Prop: Props++; break;
                case SelectionCategory.Decal: Decals++; break;
                case SelectionCategory.Moving: Moving++; break;
                case SelectionCategory.Network: Networks++; break;
                case SelectionCategory.Area: Areas++; break;
            }
        }

        public void AddImportant(ImportantBuildingKind kind)
        {
            if (kind == ImportantBuildingKind.None) return;
            ImportantBuildings++;
            if ((kind & ImportantBuildingKind.Service) != 0) ServiceBuildings++;
            if ((kind & ImportantBuildingKind.Unique) != 0) UniqueBuildings++;
            if ((kind & ImportantBuildingKind.Signature) != 0) SignatureBuildings++;
        }
    }

    public static class SelectionClassifier
    {
        public static bool IsMaskEnabledForCategory(SelectionFilterMask mask, SelectionCategory category)
        {
            return category switch
            {
                SelectionCategory.Building => (mask & SelectionFilterMask.Buildings) != 0,
                SelectionCategory.Tree => (mask & SelectionFilterMask.Trees) != 0,
                SelectionCategory.Prop => (mask & SelectionFilterMask.Props) != 0,
                SelectionCategory.Decal => (mask & SelectionFilterMask.Decals) != 0,
                SelectionCategory.Moving => (mask & SelectionFilterMask.Moving) != 0,
                SelectionCategory.Network => (mask & SelectionFilterMask.Networks) != 0,
                SelectionCategory.Area => (mask & SelectionFilterMask.Areas) != 0,
                _ => false,
            };
        }

        public static NetworkSubKind ClassifyNetworkByPrefabFlags(
            bool hasRoadOrPathwayData,
            bool hasTrackData,
            bool hasPipelineData,
            bool hasPowerLineData)
        {
            if (hasRoadOrPathwayData)
            {
                return NetworkSubKind.Road;
            }
            if (hasTrackData)
            {
                return NetworkSubKind.Track;
            }
            if (hasPipelineData)
            {
                return NetworkSubKind.Pipeline;
            }
            if (hasPowerLineData)
            {
                return NetworkSubKind.PowerLine;
            }
            return NetworkSubKind.Other;
        }

        public static bool IsNetworkMaskEnabled(SelectionFilterMask mask, NetworkSubKind networkKind)
        {
            if ((mask & SelectionFilterMask.Networks) == 0)
            {
                return false;
            }

            if ((mask & SelectionFilterMask.NetworkSubfiltersSpecified) == 0)
            {
                return true;
            }

            switch (networkKind)
            {
                case NetworkSubKind.Road:
                    return (mask & SelectionFilterMask.NetworkRoads) != 0;
                case NetworkSubKind.Track:
                    return (mask & SelectionFilterMask.NetworkTracks) != 0;
                case NetworkSubKind.Pipeline:
                    return (mask & SelectionFilterMask.NetworkPipelines) != 0;
                case NetworkSubKind.PowerLine:
                    return (mask & SelectionFilterMask.NetworkPowerLines) != 0;
                default:
                    return (mask & SelectionFilterMask.NetworkOther) != 0;
            }
        }

        public static bool IsBuildingZoneEnabled(SelectionFilterMask mask, BuildingZoneType zoneType)
        {
            return zoneType switch
            {
                BuildingZoneType.Residential => (mask & SelectionFilterMask.Residential) != 0,
                BuildingZoneType.Commercial => (mask & SelectionFilterMask.Commercial) != 0,
                BuildingZoneType.Industrial => (mask & SelectionFilterMask.Industrial) != 0,
                BuildingZoneType.Office => (mask & SelectionFilterMask.Office) != 0,
                _ => (mask & SelectionFilterMask.Buildings) != 0,
            };
        }

        public static FilterSkipReason Evaluate(
            in EntityFlagsSnapshot s,
            SelectionFilterMask mask,
            in SafetyOptions safety,
            out SelectionCategory category)
        {
            return Evaluate(in s, mask, in safety, false, out category);
        }

        public static FilterSkipReason Evaluate(
            in EntityFlagsSnapshot s,
            SelectionFilterMask mask,
            in SafetyOptions safety,
            bool undergroundView,
            out SelectionCategory category)
        {
            category = SelectionCategory.None;

            if (s.IsNull || s.IsDeleted || s.IsHidden || s.IsTemp || s.IsOverridden ||
                s.IsTerrain || s.IsPlaceholder || s.IsMarker)
            {
                return FilterSkipReason.InvalidOrInactive;
            }

            if (s.IsMapTile || s.IsDistrict)
            {
                return FilterSkipReason.UnsafeAreaType;
            }

            if (safety.ProtectOutsideConnections && s.IsOutsideConnection)
            {
                return FilterSkipReason.ProtectedOutsideConnection;
            }

            if (undergroundView != s.IsUnderground)
            {
                return FilterSkipReason.WrongElevationLayer;
            }

            // Vehicles & creatures almost always have Owner (depot, citizen household, public line).
            // Allow moving entities to be selected even when owned, while skipping static/net/area sub-elements.
            if (s.IsMoving)
            {
                category = SelectionCategory.Moving;
                return IsMaskEnabledForCategory(mask, category)
                    ? FilterSkipReason.Accepted
                    : FilterSkipReason.FilteredOutByCategory;
            }

            if (s.HasOwner)
            {
                return FilterSkipReason.HasOwnerChild;
            }

            if (s.HasAttachedParent && !s.IsBuilding)
            {
                return FilterSkipReason.AttachedChild;
            }

            if (s.IsBuilding)
            {
                if (safety.ProtectUniqueBuildings &&
                    (s.IsUniqueObject || (s.ImportantKind & (ImportantBuildingKind.Unique | ImportantBuildingKind.Signature)) != 0))
                {
                    return FilterSkipReason.ProtectedUnique;
                }

                category = SelectionCategory.Building;
                return IsBuildingZoneEnabled(mask, s.ZoneType)
                    ? FilterSkipReason.Accepted
                    : FilterSkipReason.FilteredOutByCategory;
            }

            if (s.IsNetwork)
            {
                category = SelectionCategory.Network;
                return IsNetworkMaskEnabled(mask, s.NetworkKind)
                    ? FilterSkipReason.Accepted
                    : FilterSkipReason.FilteredOutByCategory;
            }

            if (s.IsSafeArea)
            {
                category = SelectionCategory.Area;
                return IsMaskEnabledForCategory(mask, category)
                    ? FilterSkipReason.Accepted
                    : FilterSkipReason.FilteredOutByCategory;
            }

            if (s.IsPlant && !s.IsPillar)
            {
                category = ClassifyNonBuildingObject(isPlant: true, isPillar: false, s.HasObjectSurface, s.HasNetObject);
                return IsMaskEnabledForCategory(mask, category)
                    ? FilterSkipReason.Accepted
                    : FilterSkipReason.FilteredOutByCategory;
            }

            if (s.IsObjectGeometry)
            {
                if (safety.ProtectUniqueBuildings && s.IsUniqueObject)
                {
                    return FilterSkipReason.ProtectedUnique;
                }

                category = ClassifyNonBuildingObject(isPlant: false, s.IsPillar, s.HasObjectSurface, s.HasNetObject);

                return IsMaskEnabledForCategory(mask, category)
                    ? FilterSkipReason.Accepted
                    : FilterSkipReason.FilteredOutByCategory;
            }

            return FilterSkipReason.UnknownCategory;
        }

        public static SelectionCategory ClassifyNonBuildingObject(
            bool isPlant,
            bool isPillar,
            bool hasObjectSurface,
            bool hasNetObject)
        {
            if (isPlant && !isPillar)
            {
                return SelectionCategory.Tree;
            }

            return (hasObjectSurface || isPillar || hasNetObject)
                ? SelectionCategory.Prop
                : SelectionCategory.Decal;
        }
    }

    public struct FilterPresetValues : IEquatable<FilterPresetValues>
    {
        public bool Buildings;
        public bool Residential;
        public bool Commercial;
        public bool Industrial;
        public bool Office;
        public bool Trees;
        public bool Props;
        public bool Decals;
        public bool Moving;
        public bool Networks;
        public bool Areas;
        public bool NetRoads;
        public bool NetTracks;
        public bool NetPipelines;
        public bool NetPowerLines;
        public bool NetOther;

        public const string PresetGreenery = "greenery";
        public const string PresetBuildings = "buildings";
        public const string PresetSurfaces = "surfaces";
        public const string PresetUndergroundUtilities = "underground_utilities";

        public static FilterPresetValues CreateGreenery() => new FilterPresetValues
        {
            Trees = true,
            NetRoads = true,
            NetTracks = true,
            NetPipelines = true,
            NetPowerLines = true,
            NetOther = true,
        };

        public static FilterPresetValues CreateBuildingsOnly() => new FilterPresetValues
        {
            Buildings = true,
            Residential = true,
            Commercial = true,
            Industrial = true,
            Office = true,
            NetRoads = true,
            NetTracks = true,
            NetPipelines = true,
            NetPowerLines = true,
            NetOther = true,
        };

        public static FilterPresetValues CreateSurfacesOnly() => new FilterPresetValues
        {
            Areas = true,
            NetRoads = true,
            NetTracks = true,
            NetPipelines = true,
            NetPowerLines = true,
            NetOther = true,
        };

        public static FilterPresetValues CreateUndergroundUtilities() => new FilterPresetValues
        {
            Networks = true,
            NetRoads = false,
            NetTracks = false,
            NetPipelines = true,
            NetPowerLines = true,
            NetOther = false,
        };

        public static bool TryGetBuiltIn(string presetId, out FilterPresetValues values, out bool prefersUnderground)
        {
            prefersUnderground = false;
            switch (presetId)
            {
                case PresetGreenery:
                    values = CreateGreenery();
                    return true;
                case PresetBuildings:
                    values = CreateBuildingsOnly();
                    return true;
                case PresetSurfaces:
                    values = CreateSurfacesOnly();
                    return true;
                case PresetUndergroundUtilities:
                    values = CreateUndergroundUtilities();
                    prefersUnderground = true;
                    return true;
                default:
                    values = default(FilterPresetValues);
                    return false;
            }
        }

        public SelectionFilterMask BuildFilterMask()
        {
            SelectionFilterMask mask = SelectionFilterMask.None;
            if (Buildings) mask |= SelectionFilterMask.Buildings;
            if (Residential) mask |= SelectionFilterMask.Residential;
            if (Commercial) mask |= SelectionFilterMask.Commercial;
            if (Industrial) mask |= SelectionFilterMask.Industrial;
            if (Office) mask |= SelectionFilterMask.Office;
            if (Trees) mask |= SelectionFilterMask.Trees;
            if (Props) mask |= SelectionFilterMask.Props;
            if (Decals) mask |= SelectionFilterMask.Decals;
            if (Moving) mask |= SelectionFilterMask.Moving;
            if (Networks)
            {
                mask |= SelectionFilterMask.Networks | SelectionFilterMask.NetworkSubfiltersSpecified;
                if (NetRoads) mask |= SelectionFilterMask.NetworkRoads;
                if (NetTracks) mask |= SelectionFilterMask.NetworkTracks;
                if (NetPipelines) mask |= SelectionFilterMask.NetworkPipelines;
                if (NetPowerLines) mask |= SelectionFilterMask.NetworkPowerLines;
                if (NetOther) mask |= SelectionFilterMask.NetworkOther;
            }
            if (Areas) mask |= SelectionFilterMask.Areas;
            return mask;
        }

        public bool MatchesActiveMask(in FilterPresetValues other)
        {
            return BuildFilterMask() == other.BuildFilterMask();
        }

        public string Serialize()
        {
            int bits = 0;
            if (Buildings) bits |= 1 << 0;
            if (Residential) bits |= 1 << 1;
            if (Commercial) bits |= 1 << 2;
            if (Industrial) bits |= 1 << 3;
            if (Office) bits |= 1 << 4;
            if (Trees) bits |= 1 << 5;
            if (Props) bits |= 1 << 6;
            if (Decals) bits |= 1 << 7;
            if (Moving) bits |= 1 << 8;
            if (Networks) bits |= 1 << 9;
            if (Areas) bits |= 1 << 10;
            if (NetRoads) bits |= 1 << 11;
            if (NetTracks) bits |= 1 << 12;
            if (NetPipelines) bits |= 1 << 13;
            if (NetPowerLines) bits |= 1 << 14;
            if (NetOther) bits |= 1 << 15;
            return "v2:" + bits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool TryDeserialize(string raw, out FilterPresetValues values)
        {
            values = default(FilterPresetValues);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string trimmed = raw.Trim();
            bool isV1 = trimmed.StartsWith("v1:", StringComparison.Ordinal);
            bool isV2 = trimmed.StartsWith("v2:", StringComparison.Ordinal);
            if (!isV1 && !isV2)
            {
                return false;
            }

            if (!int.TryParse(
                    trimmed.Substring(3),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int bits) || bits < 0)
            {
                return false;
            }

            values = new FilterPresetValues
            {
                Buildings = (bits & (1 << 0)) != 0,
                Residential = (bits & (1 << 1)) != 0,
                Commercial = (bits & (1 << 2)) != 0,
                Industrial = (bits & (1 << 3)) != 0,
                Office = (bits & (1 << 4)) != 0,
                Trees = (bits & (1 << 5)) != 0,
                Props = (bits & (1 << 6)) != 0,
                Decals = (bits & (1 << 7)) != 0,
                Moving = (bits & (1 << 8)) != 0,
                Networks = (bits & (1 << 9)) != 0,
                Areas = (bits & (1 << 10)) != 0,
                NetRoads = (bits & (1 << 11)) != 0,
                NetTracks = (bits & (1 << 12)) != 0,
                NetPipelines = (bits & (1 << 13)) != 0,
                NetPowerLines = (bits & (1 << 14)) != 0,
                // v1 archives predated NetworkOther; default to true so saved presets stay reachable for Other networks.
                NetOther = isV1 || (bits & (1 << 15)) != 0,
            };
            return true;
        }

        public bool Equals(FilterPresetValues other)
        {
            return Buildings == other.Buildings &&
                   Residential == other.Residential &&
                   Commercial == other.Commercial &&
                   Industrial == other.Industrial &&
                   Office == other.Office &&
                   Trees == other.Trees &&
                   Props == other.Props &&
                   Decals == other.Decals &&
                   Moving == other.Moving &&
                   Networks == other.Networks &&
                   Areas == other.Areas &&
                   NetRoads == other.NetRoads &&
                   NetTracks == other.NetTracks &&
                   NetPipelines == other.NetPipelines &&
                   NetPowerLines == other.NetPowerLines &&
                   NetOther == other.NetOther;
        }

        public override bool Equals(object obj) => obj is FilterPresetValues other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Buildings ? 1 : 0;
                h = (h * 31) ^ (Residential ? 1 : 0);
                h = (h * 31) ^ (Commercial ? 1 : 0);
                h = (h * 31) ^ (Industrial ? 1 : 0);
                h = (h * 31) ^ (Office ? 1 : 0);
                h = (h * 31) ^ (Trees ? 1 : 0);
                h = (h * 31) ^ (Props ? 1 : 0);
                h = (h * 31) ^ (Decals ? 1 : 0);
                h = (h * 31) ^ (Moving ? 1 : 0);
                h = (h * 31) ^ (Networks ? 1 : 0);
                h = (h * 31) ^ (Areas ? 1 : 0);
                h = (h * 31) ^ (NetRoads ? 1 : 0);
                h = (h * 31) ^ (NetTracks ? 1 : 0);
                h = (h * 31) ^ (NetPipelines ? 1 : 0);
                h = (h * 31) ^ (NetPowerLines ? 1 : 0);
                h = (h * 31) ^ (NetOther ? 1 : 0);
                return h;
            }
        }
    }

    /// <summary>
    /// Tracks whether a filter preset switched the tool to underground view so switching away
    /// from that preset restores the previous view ONLY if the user has not manually changed it.
    /// </summary>
    public sealed class PresetViewLinkTracker
    {
        private bool m_ChangedByPreset;
        private bool m_PreviousUnderground;

        public bool HasPresetViewChange => m_ChangedByPreset;
        public bool PreviousUnderground => m_PreviousUnderground;

        public void Reset()
        {
            m_ChangedByPreset = false;
            m_PreviousUnderground = false;
        }

        public void OnUserManualViewToggle()
        {
            m_ChangedByPreset = false;
        }

        public bool ResolveViewForPreset(bool prefersUnderground, bool currentUnderground)
        {
            if (prefersUnderground)
            {
                if (!currentUnderground)
                {
                    if (!m_ChangedByPreset)
                    {
                        m_PreviousUnderground = currentUnderground;
                    }
                    m_ChangedByPreset = true;
                    return true;
                }
                return currentUnderground;
            }

            if (m_ChangedByPreset)
            {
                bool restore = m_PreviousUnderground;
                m_ChangedByPreset = false;
                return restore;
            }

            return currentUnderground;
        }
    }

    public enum MatchSimilarOutcome : byte
    {
        None = 0,
        Matched = 1,
        NoTarget = 2,
        NoPrefab = 3,
        NoMatchesInSelection = 4,
        InvalidSelection = 5,
    }

    public struct SimilarKeyCandidate
    {
        public int PrefabId;
        public SelectionCategory Category;
    }

    public static class SelectionMatchLogic
    {
        public static List<int> FilterIndicesBySimilarKey(
            IReadOnlyList<SimilarKeyCandidate> candidates,
            int targetPrefabId,
            SelectionCategory targetCategory,
            out MatchSimilarOutcome outcome)
        {
            var matched = new List<int>();
            if (candidates == null || candidates.Count == 0)
            {
                outcome = MatchSimilarOutcome.InvalidSelection;
                return matched;
            }

            if (targetPrefabId < 0 || targetCategory == SelectionCategory.None)
            {
                outcome = MatchSimilarOutcome.NoPrefab;
                return matched;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                SimilarKeyCandidate c = candidates[i];
                if (c.PrefabId == targetPrefabId && c.Category == targetCategory)
                {
                    matched.Add(i);
                }
            }

            outcome = matched.Count > 0 ? MatchSimilarOutcome.Matched : MatchSimilarOutcome.NoMatchesInSelection;
            return matched;
        }
    }

    public static class ConfirmationPolicy
    {
        public static bool ShouldAutoDelete(int selectedCount, bool confirmBeforeDelete, int largeSelectionThreshold)
            => ShouldAutoDelete(selectedCount, 0, confirmBeforeDelete, largeSelectionThreshold);

        public static bool ShouldAutoDelete(
            int selectedCount,
            int importantBuildingCount,
            bool confirmBeforeDelete,
            int largeSelectionThreshold)
        {
            if (selectedCount <= 0 || importantBuildingCount > 0 || confirmBeforeDelete)
            {
                return false;
            }

            return true;
        }

        public static bool NeedsImportantConfirmation(int importantBuildingCount)
        {
            return importantBuildingCount > 0;
        }

        public static bool NeedsLargeSelectionWarning(int selectedCount, int largeSelectionThreshold)
        {
            return selectedCount > Math.Max(1, largeSelectionThreshold);
        }
    }

    public struct CoordXZ
    {
        public float X;
        public float Z;

        public CoordXZ(float x, float z)
        {
            X = x;
            Z = z;
        }

        public static CoordXZ operator +(CoordXZ a, CoordXZ b) => new CoordXZ(a.X + b.X, a.Z + b.Z);
        public static CoordXZ operator -(CoordXZ a, CoordXZ b) => new CoordXZ(a.X - b.X, a.Z - b.Z);
        public static CoordXZ operator *(CoordXZ a, float s) => new CoordXZ(a.X * s, a.Z * s);

        public float LengthSquared() => X * X + Z * Z;
        public float Length() => (float)Math.Sqrt(LengthSquared());
        public static float Dot(CoordXZ a, CoordXZ b) => a.X * b.X + a.Z * b.Z;
        public static float Cross(CoordXZ a, CoordXZ b) => a.X * b.Z - a.Z * b.X;
    }

    public struct ShapeMetricsSnapshot
    {
        public MarqueeSelectionMode Mode;
        public bool IsUsable;
        public bool IsRectangle;
        public int WidthCells;
        public int DepthCells;
        public int WidthMeters;
        public int DepthMeters;
        public int AreaSqMeters;
        public int PerimeterMeters;
        public int VertexCount;
        public int EquivalentCells;
        public int OriginalVertexCount;
        public bool WasVertexLimitApplied;
    }

    public enum MarqueeSelectionMode : byte
    {
        Rectangle = 0,
        Polygon = 1,
        Freehand = 2,
    }

    /// <summary>
    /// Unified 2D XZ marquee selection region supporting:
    ///   - Rectangle (allocation-free O(1) fast path matching MarqueeQuadXZ)
    ///   - Polygon (arbitrary concave or self-intersecting N-gon via Odd-Even ray casting)
    ///   - Freehand (Douglas-Peucker simplified closed stroke with Odd-Even ray casting)
    /// </summary>
    public struct MarqueeShapeXZ
    {
        public const float CellSizeMeters = 8f;
        public const float MinPolygonAreaSqMeters = 1.0f;
        public const int MaxPolygonVertices = 64;
        public const int DefaultFreehandMaxVertices = MaxPolygonVertices;

        public MarqueeSelectionMode Mode;
        public CoordXZ P0;
        public CoordXZ P1;
        public CoordXZ P2;
        public CoordXZ P3;
        public CoordXZ Right;
        public CoordXZ Forward;
        public float MinX;
        public float MaxX;
        public float MinZ;
        public float MaxZ;
        public float Width;
        public float Depth;
        public float AreaSqMeters;
        public float PerimeterMeters;
        public CoordXZ Centroid;
        public int OriginalVertexCount;
        public bool WasVertexLimitApplied;

        private CoordXZ[] m_Vertices;
        private bool m_NonFinitePolygon;

        public int WidthCells => Width <= 0.01f ? 0 : Math.Max(1, (int)Math.Round(Width / CellSizeMeters));
        public int DepthCells => Depth <= 0.01f ? 0 : Math.Max(1, (int)Math.Round(Depth / CellSizeMeters));
        public int EquivalentCells => AreaSqMeters <= 0.01f ? 0 : Math.Max(1, (int)Math.Round(AreaSqMeters / (CellSizeMeters * CellSizeMeters)));
        public int AreaCells => IsRectangleFastPath ? WidthCells * DepthCells : EquivalentCells;

        public ShapeMetricsSnapshot GetMetricsSnapshot()
        {
            bool usable = IsUsable();
            if (!usable)
            {
                return new ShapeMetricsSnapshot
                {
                    Mode = Mode,
                    OriginalVertexCount = OriginalVertexCount,
                    WasVertexLimitApplied = WasVertexLimitApplied,
                };
            }

            bool isRect = IsRectangleFastPath;
            return new ShapeMetricsSnapshot
            {
                Mode = Mode,
                IsUsable = true,
                IsRectangle = isRect,
                WidthCells = isRect ? WidthCells : 0,
                DepthCells = isRect ? DepthCells : 0,
                WidthMeters = isRect ? (int)Math.Round(Width) : 0,
                DepthMeters = isRect ? (int)Math.Round(Depth) : 0,
                AreaSqMeters = (int)Math.Round(AreaSqMeters),
                PerimeterMeters = (int)Math.Round(PerimeterMeters),
                VertexCount = VertexCount,
                EquivalentCells = isRect ? (WidthCells * DepthCells) : EquivalentCells,
                OriginalVertexCount = OriginalVertexCount,
                WasVertexLimitApplied = WasVertexLimitApplied,
            };
        }

        public bool IsRectangleFastPath => Mode == MarqueeSelectionMode.Rectangle && m_Vertices == null;
        public int VertexCount => IsRectangleFastPath ? 4 : (m_Vertices != null ? m_Vertices.Length : 0);

        public CoordXZ GetVertex(int index)
        {
            if (IsRectangleFastPath)
            {
                switch (index)
                {
                    case 0: return P0;
                    case 1: return P1;
                    case 2: return P2;
                    case 3: return P3;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }

            if (m_Vertices == null)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return m_Vertices[index];
        }

        public static MarqueeShapeXZ FromPoints(CoordXZ a, CoordXZ b, CoordXZ cameraForward, bool cameraAligned)
        {
            CoordXZ right = new CoordXZ(1f, 0f);
            CoordXZ forward = new CoordXZ(0f, 1f);

            if (cameraAligned)
            {
                float lenSq = cameraForward.LengthSquared();
                if (lenSq > 1e-6f && !float.IsNaN(lenSq) && !float.IsInfinity(lenSq))
                {
                    float inv = 1f / (float)Math.Sqrt(lenSq);
                    forward = new CoordXZ(cameraForward.X * inv, cameraForward.Z * inv);
                    // Unity left-handed ground basis: forward=(0,0,1) => right=(1,0,0)=(fwd.z, -fwd.x)
                    right = new CoordXZ(forward.Z, -forward.X);
                }
            }

            CoordXZ delta = b - a;
            float u = CoordXZ.Dot(delta, right);
            float v = CoordXZ.Dot(delta, forward);

            CoordXZ p0 = a;
            CoordXZ p1 = a + right * u;
            CoordXZ p2 = p1 + forward * v;
            CoordXZ p3 = a + forward * v;
            float w = Math.Abs(u);
            float d = Math.Abs(v);

            return new MarqueeShapeXZ
            {
                Mode = MarqueeSelectionMode.Rectangle,
                P0 = p0,
                P1 = p1,
                P2 = p2,
                P3 = p3,
                Right = right,
                Forward = forward,
                MinX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X)),
                MaxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X)),
                MinZ = Math.Min(Math.Min(p0.Z, p1.Z), Math.Min(p2.Z, p3.Z)),
                MaxZ = Math.Max(Math.Max(p0.Z, p1.Z), Math.Max(p2.Z, p3.Z)),
                Width = w,
                Depth = d,
                AreaSqMeters = w * d,
                PerimeterMeters = 2f * (w + d),
                Centroid = new CoordXZ((p0.X + p2.X) * 0.5f, (p0.Z + p2.Z) * 0.5f),
                OriginalVertexCount = 4,
                WasVertexLimitApplied = false,
                m_Vertices = null,
                m_NonFinitePolygon = false,
            };
        }

        public static MarqueeShapeXZ FromPolygon(
            IReadOnlyList<CoordXZ> vertices,
            MarqueeSelectionMode mode = MarqueeSelectionMode.Polygon,
            int maxVertices = MaxPolygonVertices)
        {
            if (mode == MarqueeSelectionMode.Rectangle)
            {
                mode = MarqueeSelectionMode.Polygon;
            }

            if (vertices == null || vertices.Count == 0)
            {
                return new MarqueeShapeXZ { Mode = mode };
            }

            var cleaned = new List<CoordXZ>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                CoordXZ v = vertices[i];
                if (!IsFiniteCoord(v))
                {
                    return new MarqueeShapeXZ
                    {
                        Mode = mode,
                        MinX = float.NaN,
                        MaxX = float.NaN,
                        MinZ = float.NaN,
                        MaxZ = float.NaN,
                        OriginalVertexCount = vertices.Count,
                        m_Vertices = null,
                        m_NonFinitePolygon = true,
                    };
                }

                if (cleaned.Count == 0 || (v - cleaned[cleaned.Count - 1]).LengthSquared() > 1e-6f)
                {
                    cleaned.Add(v);
                }
            }

            if (cleaned.Count > 1 && (cleaned[cleaned.Count - 1] - cleaned[0]).LengthSquared() <= 1e-6f)
            {
                cleaned.RemoveAt(cleaned.Count - 1);
            }

            int preCapCount = cleaned.Count;
            int effectiveMax = Math.Min(128, Math.Max(3, maxVertices));
            bool wasVertexLimitApplied = false;
            if (cleaned.Count > effectiveMax)
            {
                var simplified = new List<CoordXZ>(effectiveMax);
                SimplifyPolygonDouglasPeucker(cleaned, 0.05f, simplified, effectiveMax);
                cleaned = simplified;
                wasVertexLimitApplied = true;
            }

            if (cleaned.Count < 3)
            {
                CoordXZ first = cleaned.Count > 0 ? cleaned[0] : default(CoordXZ);
                return new MarqueeShapeXZ
                {
                    Mode = mode,
                    P0 = first,
                    Centroid = first,
                    OriginalVertexCount = preCapCount,
                    WasVertexLimitApplied = wasVertexLimitApplied,
                    m_Vertices = cleaned.ToArray(),
                    m_NonFinitePolygon = false,
                };
            }

            CoordXZ[] arr = cleaned.ToArray();
            float minX = arr[0].X;
            float maxX = arr[0].X;
            float minZ = arr[0].Z;
            float maxZ = arr[0].Z;
            float perimeter = 0f;
            double signedTwice = 0.0;
            double cxSum = 0.0;
            double czSum = 0.0;
            double avgX = 0.0;
            double avgZ = 0.0;

            int n = arr.Length;
            for (int i = 0; i < n; i++)
            {
                CoordXZ a = arr[i];
                CoordXZ b = arr[(i + 1) % n];

                if (a.X < minX) minX = a.X;
                if (a.X > maxX) maxX = a.X;
                if (a.Z < minZ) minZ = a.Z;
                if (a.Z > maxZ) maxZ = a.Z;

                perimeter += (b - a).Length();
                double cross = (double)a.X * b.Z - (double)b.X * a.Z;
                signedTwice += cross;
                cxSum += ((double)a.X + b.X) * cross;
                czSum += ((double)a.Z + b.Z) * cross;
                avgX += a.X;
                avgZ += a.Z;
            }

            float area = ComputePolygonOddEvenArea(arr, Math.Abs(signedTwice) * 0.5);
            CoordXZ centroid = Math.Abs(signedTwice) > 1e-4
                ? new CoordXZ((float)(cxSum / (3.0 * signedTwice)), (float)(czSum / (3.0 * signedTwice)))
                : new CoordXZ((float)(avgX / n), (float)(avgZ / n));
            if (!IsFiniteCoord(centroid))
            {
                centroid = new CoordXZ((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            }

            return new MarqueeShapeXZ
            {
                Mode = mode,
                P0 = arr[0],
                P1 = arr[1],
                P2 = arr[2],
                P3 = n > 3 ? arr[3] : arr[2],
                Right = new CoordXZ(1f, 0f),
                Forward = new CoordXZ(0f, 1f),
                MinX = minX,
                MaxX = maxX,
                MinZ = minZ,
                MaxZ = maxZ,
                Width = maxX - minX,
                Depth = maxZ - minZ,
                AreaSqMeters = area,
                PerimeterMeters = perimeter,
                Centroid = centroid,
                OriginalVertexCount = preCapCount,
                WasVertexLimitApplied = wasVertexLimitApplied,
                m_Vertices = arr,
                m_NonFinitePolygon = false,
            };
        }

        public static MarqueeShapeXZ FromFreehandStroke(
            IReadOnlyList<CoordXZ> rawPoints,
            float simplifyToleranceMeters = 1.0f,
            int maxVertices = DefaultFreehandMaxVertices)
        {
            if (rawPoints == null || rawPoints.Count == 0)
            {
                return new MarqueeShapeXZ { Mode = MarqueeSelectionMode.Freehand };
            }

            for (int i = 0; i < rawPoints.Count; i++)
            {
                if (!IsFiniteCoord(rawPoints[i]))
                {
                    return new MarqueeShapeXZ
                    {
                        Mode = MarqueeSelectionMode.Freehand,
                        MinX = float.NaN,
                        MaxX = float.NaN,
                        MinZ = float.NaN,
                        MaxZ = float.NaN,
                        OriginalVertexCount = rawPoints.Count,
                        m_NonFinitePolygon = true,
                    };
                }
            }

            int effectiveMax = Math.Min(128, Math.Max(3, maxVertices));
            var simplified = new List<CoordXZ>(Math.Min(effectiveMax, rawPoints.Count));
            SimplifyPolygonDouglasPeucker(rawPoints, simplifyToleranceMeters, simplified, effectiveMax);
            MarqueeShapeXZ shape = FromPolygon(simplified, MarqueeSelectionMode.Freehand, effectiveMax);
            shape.OriginalVertexCount = rawPoints.Count;
            shape.WasVertexLimitApplied = shape.WasVertexLimitApplied || rawPoints.Count > effectiveMax;
            return shape;
        }

        public static void SimplifyPolygonDouglasPeucker(
            IReadOnlyList<CoordXZ> rawPoints,
            float toleranceMeters,
            List<CoordXZ> outSimplified,
            int maxVertices = DefaultFreehandMaxVertices)
        {
            if (outSimplified == null)
            {
                return;
            }

            outSimplified.Clear();
            if (rawPoints == null || rawPoints.Count == 0)
            {
                return;
            }

            var cleaned = new List<CoordXZ>(rawPoints.Count);
            for (int i = 0; i < rawPoints.Count; i++)
            {
                CoordXZ p = rawPoints[i];
                if (!IsFiniteCoord(p))
                {
                    return;
                }

                if (cleaned.Count == 0 || (p - cleaned[cleaned.Count - 1]).LengthSquared() > 1e-4f)
                {
                    cleaned.Add(p);
                }
            }

            if (cleaned.Count > 1 && (cleaned[cleaned.Count - 1] - cleaned[0]).LengthSquared() <= 1e-4f)
            {
                cleaned.RemoveAt(cleaned.Count - 1);
            }

            if (cleaned.Count <= 3)
            {
                for (int i = 0; i < cleaned.Count; i++)
                {
                    outSimplified.Add(cleaned[i]);
                }
                return;
            }

            // Split closed ring at point farthest from cleaned[0] so DP treats both halves symmetrically.
            int anchorB = 1;
            float bestDistSq = -1f;
            CoordXZ anchorA = cleaned[0];
            for (int i = 1; i < cleaned.Count; i++)
            {
                float dSq = (cleaned[i] - anchorA).LengthSquared();
                if (dSq > bestDistSq)
                {
                    bestDistSq = dSq;
                    anchorB = i;
                }
            }

            var half1 = new List<CoordXZ>(anchorB + 1);
            for (int i = 0; i <= anchorB; i++)
            {
                half1.Add(cleaned[i]);
            }

            var half2 = new List<CoordXZ>(cleaned.Count - anchorB + 1);
            for (int i = anchorB; i < cleaned.Count; i++)
            {
                half2.Add(cleaned[i]);
            }
            half2.Add(cleaned[0]);

            int targetMax = Math.Max(3, maxVertices);
            float tol = Math.Max(0.05f, toleranceMeters);
            var keep1 = new List<CoordXZ>();
            var keep2 = new List<CoordXZ>();

            for (int attempt = 0; attempt < 24; attempt++)
            {
                keep1.Clear();
                keep2.Clear();
                SimplifyOpenChain(half1, tol, keep1);
                SimplifyOpenChain(half2, tol, keep2);

                int combinedCount = Math.Max(0, keep1.Count - 1) + Math.Max(0, keep2.Count - 1);
                if (combinedCount <= targetMax)
                {
                    break;
                }

                tol *= 1.45f;
            }

            for (int i = 0; i < keep1.Count - 1; i++)
            {
                outSimplified.Add(keep1[i]);
            }
            for (int i = 0; i < keep2.Count - 1; i++)
            {
                outSimplified.Add(keep2[i]);
            }

            if (outSimplified.Count > targetMax)
            {
                int strideCount = outSimplified.Count;
                var decimated = new List<CoordXZ>(targetMax);
                for (int i = 0; i < targetMax; i++)
                {
                    int idx = (int)((long)i * strideCount / targetMax);
                    decimated.Add(outSimplified[idx]);
                }
                outSimplified.Clear();
                outSimplified.AddRange(decimated);
            }
        }

        private static void SimplifyOpenChain(List<CoordXZ> chain, float tolMeters, List<CoordXZ> output)
        {
            int count = chain.Count;
            if (count <= 2)
            {
                output.AddRange(chain);
                return;
            }

            bool[] keep = new bool[count];
            keep[0] = true;
            keep[count - 1] = true;
            float tolSq = tolMeters * tolMeters;

            var stack = new Stack<(int start, int end)>();
            stack.Push((0, count - 1));
            while (stack.Count > 0)
            {
                var (start, end) = stack.Pop();
                if (end - start <= 1)
                {
                    continue;
                }

                CoordXZ a = chain[start];
                CoordXZ b = chain[end];
                float maxDistSq = -1f;
                int maxIndex = -1;
                for (int i = start + 1; i < end; i++)
                {
                    float dSq = PointToSegmentDistSq(a, b, chain[i]);
                    if (dSq > maxDistSq)
                    {
                        maxDistSq = dSq;
                        maxIndex = i;
                    }
                }

                if (maxIndex > 0 && maxDistSq > tolSq)
                {
                    keep[maxIndex] = true;
                    stack.Push((start, maxIndex));
                    stack.Push((maxIndex, end));
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (keep[i])
                {
                    output.Add(chain[i]);
                }
            }
        }

        public bool IsFinite()
        {
            if (m_NonFinitePolygon)
            {
                return false;
            }

            if (IsRectangleFastPath)
            {
                return IsFiniteCoord(P0) && IsFiniteCoord(P1) && IsFiniteCoord(P2) && IsFiniteCoord(P3)
                    && !float.IsNaN(Width) && !float.IsInfinity(Width)
                    && !float.IsNaN(Depth) && !float.IsInfinity(Depth);
            }

            return m_Vertices != null
                && !float.IsNaN(MinX) && !float.IsInfinity(MinX)
                && !float.IsNaN(MaxX) && !float.IsInfinity(MaxX)
                && !float.IsNaN(MinZ) && !float.IsInfinity(MinZ)
                && !float.IsNaN(MaxZ) && !float.IsInfinity(MaxZ);
        }

        public bool IsUsable(float minSpanMeters = 0.5f)
        {
            if (!IsFinite())
            {
                return false;
            }

            if (IsRectangleFastPath)
            {
                float minSide = Math.Max(0.25f, minSpanMeters * 0.6f);
                float diag = (float)Math.Sqrt(Width * Width + Depth * Depth);
                return Math.Min(Width, Depth) >= minSide && diag >= minSpanMeters;
            }

            return m_Vertices != null
                && m_Vertices.Length >= 3
                && Math.Max(Width, Depth) >= minSpanMeters
                && AreaSqMeters >= MinPolygonAreaSqMeters;
        }

        internal bool HasMovedFrom(in MarqueeShapeXZ previous, float minMeters)
        {
            if (Mode != previous.Mode || VertexCount != previous.VertexCount)
            {
                return true;
            }

            float limit = minMeters * minMeters;
            if (IsRectangleFastPath)
            {
                return (P0 - previous.P0).LengthSquared() >= limit ||
                       (P1 - previous.P1).LengthSquared() >= limit ||
                       (P2 - previous.P2).LengthSquared() >= limit ||
                       (P3 - previous.P3).LengthSquared() >= limit;
            }

            if (m_Vertices == null || previous.m_Vertices == null)
            {
                return m_Vertices != previous.m_Vertices;
            }

            for (int i = 0; i < m_Vertices.Length; i++)
            {
                if ((m_Vertices[i] - previous.m_Vertices[i]).LengthSquared() >= limit)
                {
                    return true;
                }
            }
            return false;
        }

        public bool ContainsPoint(CoordXZ p)
        {
            if (IsRectangleFastPath)
            {
                return ContainsPointFastRect(p);
            }

            return ContainsPointPolygon(p);
        }

        private bool ContainsPointFastRect(CoordXZ p)
        {
            if (!IsFinite())
            {
                return false;
            }

            if (p.X < MinX - 1e-4f || p.X > MaxX + 1e-4f || p.Z < MinZ - 1e-4f || p.Z > MaxZ + 1e-4f)
            {
                return false;
            }

            float areaTwice = Math.Abs(CoordXZ.Cross(P1 - P0, P3 - P0));
            if (areaTwice < 1e-5f)
            {
                return false;
            }

            float eps = Math.Max(1e-4f, areaTwice * 1e-5f);
            float c0 = CoordXZ.Cross(P1 - P0, p - P0);
            float c1 = CoordXZ.Cross(P2 - P1, p - P1);
            float c2 = CoordXZ.Cross(P3 - P2, p - P2);
            float c3 = CoordXZ.Cross(P0 - P3, p - P3);

            bool allNonNeg = c0 >= -eps && c1 >= -eps && c2 >= -eps && c3 >= -eps;
            bool allNonPos = c0 <= eps && c1 <= eps && c2 <= eps && c3 <= eps;
            return allNonNeg || allNonPos;
        }

        private bool ContainsPointPolygon(CoordXZ p)
        {
            if (!IsFinite() || m_Vertices == null || m_Vertices.Length < 3 || AreaSqMeters < 1e-4f)
            {
                return false;
            }

            if (p.X < MinX - 1e-4f || p.X > MaxX + 1e-4f || p.Z < MinZ - 1e-4f || p.Z > MaxZ + 1e-4f)
            {
                return false;
            }

            int n = m_Vertices.Length;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                CoordXZ a = m_Vertices[j];
                CoordXZ b = m_Vertices[i];

                if (PointToSegmentDistSq(a, b, p) <= 1e-6f)
                {
                    return true;
                }

                if ((a.Z > p.Z) != (b.Z > p.Z))
                {
                    float xCross = a.X + (p.Z - a.Z) * (b.X - a.X) / (b.Z - a.Z);
                    if (p.X < xCross)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        public bool IntersectsSegment(CoordXZ a, CoordXZ b)
        {
            if (!IsFinite())
            {
                return false;
            }

            if (Math.Max(a.X, b.X) < MinX - 1e-4f || Math.Min(a.X, b.X) > MaxX + 1e-4f ||
                Math.Max(a.Z, b.Z) < MinZ - 1e-4f || Math.Min(a.Z, b.Z) > MaxZ + 1e-4f)
            {
                return false;
            }

            if (IsRectangleFastPath)
            {
                if (ContainsPointFastRect(a) || ContainsPointFastRect(b) ||
                    ContainsPointFastRect(new CoordXZ((a.X + b.X) * 0.5f, (a.Z + b.Z) * 0.5f)))
                {
                    return true;
                }

                return SegmentsIntersect(a, b, P0, P1)
                    || SegmentsIntersect(a, b, P1, P2)
                    || SegmentsIntersect(a, b, P2, P3)
                    || SegmentsIntersect(a, b, P3, P0);
            }

            if (ContainsPointPolygon(a) || ContainsPointPolygon(b) ||
                ContainsPointPolygon(new CoordXZ((a.X + b.X) * 0.5f, (a.Z + b.Z) * 0.5f)))
            {
                return true;
            }

            if (m_Vertices == null || m_Vertices.Length < 3 || AreaSqMeters < 1e-4f)
            {
                return false;
            }

            int n = m_Vertices.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (SegmentsIntersect(a, b, m_Vertices[j], m_Vertices[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IntersectsAabb(float minX, float maxX, float minZ, float maxZ)
        {
            if (!IsFinite() || maxX < MinX || minX > MaxX || maxZ < MinZ || minZ > MaxZ)
            {
                return false;
            }

            if (IsRectangleFastPath)
            {
                if (ContainsPointFastRect(new CoordXZ(minX, minZ)) || ContainsPointFastRect(new CoordXZ(maxX, minZ))
                    || ContainsPointFastRect(new CoordXZ(maxX, maxZ)) || ContainsPointFastRect(new CoordXZ(minX, maxZ))
                    || ContainsPointFastRect(new CoordXZ((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f)))
                {
                    return true;
                }

                if (PointInAabb(P0, minX, maxX, minZ, maxZ) || PointInAabb(P1, minX, maxX, minZ, maxZ)
                    || PointInAabb(P2, minX, maxX, minZ, maxZ) || PointInAabb(P3, minX, maxX, minZ, maxZ))
                {
                    return true;
                }

                CoordXZ r0 = new CoordXZ(minX, minZ);
                CoordXZ r1 = new CoordXZ(maxX, minZ);
                CoordXZ r2 = new CoordXZ(maxX, maxZ);
                CoordXZ r3 = new CoordXZ(minX, maxZ);
                return IntersectsSegment(r0, r1) || IntersectsSegment(r1, r2)
                    || IntersectsSegment(r2, r3) || IntersectsSegment(r3, r0);
            }

            if (m_Vertices == null || m_Vertices.Length < 3 || AreaSqMeters < 1e-4f)
            {
                return false;
            }

            if (ContainsPointPolygon(new CoordXZ(minX, minZ)) || ContainsPointPolygon(new CoordXZ(maxX, minZ))
                || ContainsPointPolygon(new CoordXZ(maxX, maxZ)) || ContainsPointPolygon(new CoordXZ(minX, maxZ))
                || ContainsPointPolygon(new CoordXZ((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f)))
            {
                return true;
            }

            int n = m_Vertices.Length;
            for (int i = 0; i < n; i++)
            {
                if (PointInAabb(m_Vertices[i], minX, maxX, minZ, maxZ))
                {
                    return true;
                }
            }

            CoordXZ a0 = new CoordXZ(minX, minZ);
            CoordXZ a1 = new CoordXZ(maxX, minZ);
            CoordXZ a2 = new CoordXZ(maxX, maxZ);
            CoordXZ a3 = new CoordXZ(minX, maxZ);
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                CoordXZ va = m_Vertices[j];
                CoordXZ vb = m_Vertices[i];
                if (SegmentsIntersect(va, vb, a0, a1) ||
                    SegmentsIntersect(va, vb, a1, a2) ||
                    SegmentsIntersect(va, vb, a2, a3) ||
                    SegmentsIntersect(va, vb, a3, a0))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IntersectsTriangle(CoordXZ t0, CoordXZ t1, CoordXZ t2)
        {
            if (!IsFinite())
            {
                return false;
            }

            float tMinX = Math.Min(t0.X, Math.Min(t1.X, t2.X));
            float tMaxX = Math.Max(t0.X, Math.Max(t1.X, t2.X));
            float tMinZ = Math.Min(t0.Z, Math.Min(t1.Z, t2.Z));
            float tMaxZ = Math.Max(t0.Z, Math.Max(t1.Z, t2.Z));
            if (tMaxX < MinX || tMinX > MaxX || tMaxZ < MinZ || tMinZ > MaxZ)
            {
                return false;
            }

            if (IsRectangleFastPath)
            {
                if (ContainsPointFastRect(t0) || ContainsPointFastRect(t1) || ContainsPointFastRect(t2))
                {
                    return true;
                }

                if (PointInTriangle(P0, t0, t1, t2) || PointInTriangle(P1, t0, t1, t2)
                    || PointInTriangle(P2, t0, t1, t2) || PointInTriangle(P3, t0, t1, t2))
                {
                    return true;
                }

                return IntersectsSegment(t0, t1) || IntersectsSegment(t1, t2) || IntersectsSegment(t2, t0);
            }

            if (m_Vertices == null || m_Vertices.Length < 3 || AreaSqMeters < 1e-4f)
            {
                return false;
            }

            if (ContainsPointPolygon(t0) || ContainsPointPolygon(t1) || ContainsPointPolygon(t2))
            {
                return true;
            }

            int n = m_Vertices.Length;
            for (int i = 0; i < n; i++)
            {
                if (PointInTriangle(m_Vertices[i], t0, t1, t2))
                {
                    return true;
                }
            }

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                CoordXZ va = m_Vertices[j];
                CoordXZ vb = m_Vertices[i];
                if (SegmentsIntersect(va, vb, t0, t1) ||
                    SegmentsIntersect(va, vb, t1, t2) ||
                    SegmentsIntersect(va, vb, t2, t0))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tests overlap between this marquee shape and an oriented 2D box/parallelogram
        /// centered at <paramref name="center"/> with half-extent vectors <paramref name="halfU"/> and <paramref name="halfV"/>.
        /// Rectangle mode executes the exact O(1) 4-axis SAT fast path; general polygons use
        /// O(N) Odd-Even containment + boundary segment intersection (supports concave & figure-8 shapes).
        /// </summary>
        public bool IntersectsOrientedRect(CoordXZ center, CoordXZ halfU, CoordXZ halfV)
        {
            if (!IsFinite() || !IsFiniteCoord(center) || !IsFiniteCoord(halfU) || !IsFiniteCoord(halfV))
            {
                return false;
            }

            float extX = Math.Abs(halfU.X) + Math.Abs(halfV.X);
            float extZ = Math.Abs(halfU.Z) + Math.Abs(halfV.Z);
            if (center.X + extX < MinX - 1e-4f || center.X - extX > MaxX + 1e-4f ||
                center.Z + extZ < MinZ - 1e-4f || center.Z - extZ > MaxZ + 1e-4f)
            {
                return false;
            }

            if (IsRectangleFastPath)
            {
                float areaTwice = Math.Abs(CoordXZ.Cross(P1 - P0, P3 - P0));
                if (areaTwice < 1e-5f)
                {
                    return false;
                }

                CoordXZ qRight = Right;
                CoordXZ qForward = Forward;
                if (qRight.LengthSquared() < 1e-6f || qForward.LengthSquared() < 1e-6f)
                {
                    CoordXZ e0 = P1 - P0;
                    CoordXZ e1 = P3 - P0;
                    float l0 = e0.Length();
                    float l1 = e1.Length();
                    if (l0 < 1e-5f || l1 < 1e-5f)
                    {
                        return false;
                    }
                    qRight = e0 * (1f / l0);
                    qForward = e1 * (1f / l1);
                }

                CoordXZ qCenter = new CoordXZ((P0.X + P2.X) * 0.5f, (P0.Z + P2.Z) * 0.5f);
                CoordXZ diff = center - qCenter;
                float halfW = Width * 0.5f;
                float halfD = Depth * 0.5f;

                if (IsSeparatedOnAxis(diff, qRight, qRight, qForward, halfW, halfD, halfU, halfV)) return false;
                if (IsSeparatedOnAxis(diff, qForward, qRight, qForward, halfW, halfD, halfU, halfV)) return false;
                if (IsSeparatedOnAxis(diff, new CoordXZ(-halfU.Z, halfU.X), qRight, qForward, halfW, halfD, halfU, halfV)) return false;
                if (IsSeparatedOnAxis(diff, new CoordXZ(-halfV.Z, halfV.X), qRight, qForward, halfW, halfD, halfU, halfV)) return false;

                return true;
            }

            if (m_Vertices == null || m_Vertices.Length < 3 || AreaSqMeters < 1e-4f)
            {
                return false;
            }

            CoordXZ c0 = center - halfU - halfV;
            CoordXZ c1 = center + halfU - halfV;
            CoordXZ c2 = center + halfU + halfV;
            CoordXZ c3 = center - halfU + halfV;

            if (ContainsPointPolygon(center) ||
                ContainsPointPolygon(c0) ||
                ContainsPointPolygon(c1) ||
                ContainsPointPolygon(c2) ||
                ContainsPointPolygon(c3))
            {
                return true;
            }

            int n = m_Vertices.Length;
            for (int i = 0; i < n; i++)
            {
                CoordXZ v = m_Vertices[i];
                if (PointInTriangle(v, c0, c1, c2) || PointInTriangle(v, c0, c2, c3))
                {
                    return true;
                }
            }

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                CoordXZ va = m_Vertices[j];
                CoordXZ vb = m_Vertices[i];
                if (SegmentsIntersect(va, vb, c0, c1) ||
                    SegmentsIntersect(va, vb, c1, c2) ||
                    SegmentsIntersect(va, vb, c2, c3) ||
                    SegmentsIntersect(va, vb, c3, c0))
                {
                    return true;
                }
            }

            return false;
        }

        private static float ComputePolygonOddEvenArea(CoordXZ[] verts, double absSignedArea)
        {
            int n = verts.Length;
            if (n < 3)
            {
                return 0f;
            }

            // Detect any non-adjacent proper or touching segment crossings; simple polygons use fast shoelace directly.
            bool hasSelfIntersection = false;
            if (n <= 128)
            {
                for (int i = 0; i < n && !hasSelfIntersection; i++)
                {
                    CoordXZ a1 = verts[i];
                    CoordXZ a2 = verts[(i + 1) % n];
                    for (int k = i + 2; k < n; k++)
                    {
                        if (i == 0 && k == n - 1)
                        {
                            continue;
                        }

                        CoordXZ b1 = verts[k];
                        CoordXZ b2 = verts[(k + 1) % n];
                        if (SegmentsIntersect(a1, a2, b1, b2))
                        {
                            hasSelfIntersection = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                hasSelfIntersection = true;
            }

            if (!hasSelfIntersection)
            {
                return (float)absSignedArea;
            }

            // Exact Odd-Even slab integral over unique sorted Zs (vertices + crossings) for self-intersecting rings.
            var zLevels = new List<double>(n * 2);
            for (int i = 0; i < n; i++)
            {
                zLevels.Add(verts[i].Z);
            }

            if (n <= 128)
            {
                for (int i = 0; i < n; i++)
                {
                    CoordXZ a1 = verts[i];
                    CoordXZ a2 = verts[(i + 1) % n];
                    for (int k = i + 2; k < n; k++)
                    {
                        if (i == 0 && k == n - 1)
                        {
                            continue;
                        }

                        if (TrySegmentIntersectionZ(a1, a2, verts[k], verts[(k + 1) % n], out double zCross))
                        {
                            zLevels.Add(zCross);
                        }
                    }
                }
            }

            zLevels.Sort();
            var uniqueZ = new List<double>(zLevels.Count);
            for (int i = 0; i < zLevels.Count; i++)
            {
                if (uniqueZ.Count == 0 || zLevels[i] - uniqueZ[uniqueZ.Count - 1] > 1e-6)
                {
                    uniqueZ.Add(zLevels[i]);
                }
            }

            double totalArea = 0.0;
            var xCrossings = new List<double>(16);
            for (int s = 0; s + 1 < uniqueZ.Count; s++)
            {
                double dz = uniqueZ[s + 1] - uniqueZ[s];
                if (dz <= 1e-6)
                {
                    continue;
                }

                double zMid = 0.5 * (uniqueZ[s] + uniqueZ[s + 1]);
                xCrossings.Clear();
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double z0 = verts[j].Z;
                    double z1 = verts[i].Z;
                    if ((z0 > zMid) != (z1 > zMid))
                    {
                        double x = verts[j].X + (zMid - z0) * (verts[i].X - verts[j].X) / (z1 - z0);
                        xCrossings.Add(x);
                    }
                }

                xCrossings.Sort();
                for (int m = 0; m + 1 < xCrossings.Count; m += 2)
                {
                    totalArea += (xCrossings[m + 1] - xCrossings[m]) * dz;
                }
            }

            return (float)totalArea;
        }

        private static bool TrySegmentIntersectionZ(CoordXZ a, CoordXZ b, CoordXZ c, CoordXZ d, out double zCross)
        {
            zCross = 0.0;
            double bax = b.X - a.X;
            double baz = b.Z - a.Z;
            double dcx = d.X - c.X;
            double dcz = d.Z - c.Z;
            double denom = bax * dcz - baz * dcx;
            if (Math.Abs(denom) < 1e-9)
            {
                return false;
            }

            double cax = c.X - a.X;
            double caz = c.Z - a.Z;
            double t = (cax * dcz - caz * dcx) / denom;
            double u = (cax * baz - caz * bax) / denom;
            if (t >= -1e-6 && t <= 1.0 + 1e-6 && u >= -1e-6 && u <= 1.0 + 1e-6)
            {
                zCross = a.Z + t * baz;
                return !double.IsNaN(zCross) && !double.IsInfinity(zCross);
            }

            return false;
        }

        private static float PointToSegmentDistSq(CoordXZ a, CoordXZ b, CoordXZ p)
        {
            float abx = b.X - a.X;
            float abz = b.Z - a.Z;
            float lenSq = abx * abx + abz * abz;
            if (lenSq <= 1e-8f)
            {
                return (p - a).LengthSquared();
            }

            float t = ((p.X - a.X) * abx + (p.Z - a.Z) * abz) / lenSq;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            float dx = (a.X + t * abx) - p.X;
            float dz = (a.Z + t * abz) - p.Z;
            return dx * dx + dz * dz;
        }

        private static bool IsSeparatedOnAxis(
            CoordXZ diff,
            CoordXZ axis,
            CoordXZ qRight,
            CoordXZ qForward,
            float halfW,
            float halfD,
            CoordXZ halfU,
            CoordXZ halfV)
        {
            float lenSq = axis.LengthSquared();
            if (lenSq < 1e-8f)
            {
                return false;
            }

            CoordXZ n = axis * (1f / (float)Math.Sqrt(lenSq));
            float dist = Math.Abs(CoordXZ.Dot(diff, n));
            float qProj = halfW * Math.Abs(CoordXZ.Dot(qRight, n)) + halfD * Math.Abs(CoordXZ.Dot(qForward, n));
            float bProj = Math.Abs(CoordXZ.Dot(halfU, n)) + Math.Abs(CoordXZ.Dot(halfV, n));
            return dist > qProj + bProj + 1e-4f;
        }

        private static bool IsFiniteCoord(CoordXZ p)
        {
            return !float.IsNaN(p.X) && !float.IsInfinity(p.X) && !float.IsNaN(p.Z) && !float.IsInfinity(p.Z);
        }

        private static bool PointInAabb(CoordXZ p, float minX, float maxX, float minZ, float maxZ)
        {
            return p.X >= minX && p.X <= maxX && p.Z >= minZ && p.Z <= maxZ;
        }

        private static bool PointInTriangle(CoordXZ p, CoordXZ a, CoordXZ b, CoordXZ c)
        {
            float d0 = CoordXZ.Cross(b - a, p - a);
            float d1 = CoordXZ.Cross(c - b, p - b);
            float d2 = CoordXZ.Cross(a - c, p - c);
            bool hasNeg = d0 < -1e-5f || d1 < -1e-5f || d2 < -1e-5f;
            bool hasPos = d0 > 1e-5f || d1 > 1e-5f || d2 > 1e-5f;
            return !(hasNeg && hasPos);
        }

        private static bool SegmentsIntersect(CoordXZ a, CoordXZ b, CoordXZ c, CoordXZ d)
        {
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
                Math.Max(a.Z, b.Z) < Math.Min(c.Z, d.Z) || Math.Max(c.Z, d.Z) < Math.Min(a.Z, b.Z))
            {
                return false;
            }

            float d1 = CoordXZ.Cross(b - a, c - a);
            float d2 = CoordXZ.Cross(b - a, d - a);
            float d3 = CoordXZ.Cross(d - c, a - c);
            float d4 = CoordXZ.Cross(d - c, b - c);

            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            const float eps = 1e-5f;
            return (Math.Abs(d1) <= eps && OnSegment(a, b, c))
                || (Math.Abs(d2) <= eps && OnSegment(a, b, d))
                || (Math.Abs(d3) <= eps && OnSegment(c, d, a))
                || (Math.Abs(d4) <= eps && OnSegment(c, d, b));
        }

        private static bool OnSegment(CoordXZ a, CoordXZ b, CoordXZ p)
        {
            return p.X >= Math.Min(a.X, b.X) - 1e-5f && p.X <= Math.Max(a.X, b.X) + 1e-5f
                && p.Z >= Math.Min(a.Z, b.Z) - 1e-5f && p.Z <= Math.Max(a.Z, b.Z) + 1e-5f;
        }
    }

    public static class UndoMath
    {
        public const byte TreeStateTeen = 1;
        public const byte TreeStateAdult = 2;
        public const byte TreeStateElderly = 4;
        public const byte TreeStateDead = 8;
        public const byte TreeStateStump = 16;
        public const byte TreeStateCollected = 32;

        /// <summary>
        /// Inverts Game.Objects.ObjectUtils.InitializeTreeState(float age) so restored trees
        /// preserve their exact growth stage and byte progress.
        /// </summary>
        public static float TreeToDefinitionAge(byte stateMask, byte growth)
        {
            float g = growth + 0.5f;
            if ((stateMask & (TreeStateDead | TreeStateStump | TreeStateCollected)) != 0)
            {
                return 0.95f + Math.Min(0.04999f, g / 5120f);
            }
            if ((stateMask & TreeStateElderly) != 0)
            {
                return 0.60f + Math.Min(0.3499f, g / 731.4286f);
            }
            if ((stateMask & TreeStateAdult) != 0)
            {
                return 0.25f + Math.Min(0.3499f, g / 731.4286f);
            }
            if ((stateMask & TreeStateTeen) != 0)
            {
                return 0.10f + Math.Min(0.1499f, g / 1706.6666f);
            }
            return Math.Min(0.0999f, g / 2560f);
        }
    }

    public enum EscapeAction
    {
        None = 0,
        CloseImportantModal = 1,
        CloseColorModal = 2,
        CancelPointerOrDrag = 3,
        ClearSelection = 4,
    }

    /// <summary>
    /// Shared bidirectional gate guaranteeing a single ESC keypress is consumed at most
    /// once per frame across ToolUpdate and UIUpdate regardless of phase execution order.
    /// </summary>
    public sealed class EscapeFrameGate
    {
        public int ConsumedFrame { get; private set; } = -1;

        public bool IsConsumedThisFrame(int frameCount)
        {
            return ConsumedFrame == frameCount;
        }

        public bool TryConsume(int frameCount)
        {
            if (ConsumedFrame == frameCount)
            {
                return false;
            }

            ConsumedFrame = frameCount;
            return true;
        }

        public void Reset()
        {
            ConsumedFrame = -1;
        }
    }

    public enum PointerPressOutcome : byte
    {
        StayPressed = 0,
        PromoteToDrag = 1,
        CompleteDragOnRelease = 2,
        SingleClick = 3,
    }

    public enum DragUpdateOutcome : byte
    {
        Continue = 0,
        Complete = 1,
        Cancel = 2,
    }

    public enum RightCancelAction : byte
    {
        None = 0,
        CloseImportantModal = 1,
        CancelPointerOrDrag = 2,
        RemoveEntityAtCursor = 3,
        ExitTool = 4,
        RemoveLastPolygonVertex = 5,
    }

    public enum PolygonClickOutcome : byte
    {
        None = 0,
        AddedVertex = 1,
        ClosedBySnap = 2,
        ClosedByDoubleClick = 3,
        IgnoredTooClose = 4,
        VertexLimitReached = 5,
        IgnoredInvalid = 6,
    }

    /// <summary>
    /// Stateful in-progress polygon drafting session shared between ToolSystem and tests.
    /// Owns vertex accumulation, &lt;0.8m debounce, 14px start-vertex snap, double-click close,
    /// Enter close, and right-click vertex unwind.
    /// </summary>
    public sealed class PolygonDraftSession
    {
        public const float MinVertexDistanceMeters = 0.8f;
        public const float StartSnapRadiusPx = 14f;
        public const double DoubleClickMaxSeconds = 0.35;
        public const float DoubleClickMaxScreenPx = 12f;

        private readonly List<CoordXZ> m_Vertices = new List<CoordXZ>(16);
        private readonly List<float> m_Heights = new List<float>(16);
        private double m_LastClickTime = double.NegativeInfinity;
        private float m_LastClickScreenX;
        private float m_LastClickScreenY;
        private bool m_HasLastClick;

        public int Count => m_Vertices.Count;
        public bool HasDraft => m_Vertices.Count > 0;
        public int Revision { get; private set; }
        public int MaxVertices { get; set; } = MarqueeShapeXZ.MaxPolygonVertices;
        public IReadOnlyList<CoordXZ> Vertices => m_Vertices;
        public IReadOnlyList<float> VertexHeights => m_Heights;

        public CoordXZ StartVertexXZ => m_Vertices.Count > 0 ? m_Vertices[0] : default(CoordXZ);
        public float StartVertexY => m_Heights.Count > 0 ? m_Heights[0] : 0f;

        public void Reset()
        {
            if (m_Vertices.Count > 0)
            {
                m_Vertices.Clear();
                m_Heights.Clear();
                Revision++;
            }
            InvalidateDoubleClick();
        }

        public void InvalidateDoubleClick()
        {
            m_HasLastClick = false;
            m_LastClickTime = double.NegativeInfinity;
            m_LastClickScreenX = 0f;
            m_LastClickScreenY = 0f;
        }

        public bool IsSnapToStart(
            float cursorScreenX,
            float cursorScreenY,
            bool hasStartScreen,
            float startScreenX,
            float startScreenY)
        {
            if (m_Vertices.Count < 3 || !hasStartScreen)
            {
                return false;
            }

            if (float.IsNaN(cursorScreenX) || float.IsNaN(cursorScreenY) ||
                float.IsNaN(startScreenX) || float.IsNaN(startScreenY))
            {
                return false;
            }

            float dx = cursorScreenX - startScreenX;
            float dy = cursorScreenY - startScreenY;
            return (dx * dx + dy * dy) <= (StartSnapRadiusPx * StartSnapRadiusPx);
        }

        public PolygonClickOutcome HandleLeftClick(
            CoordXZ worldXZ,
            float worldY,
            float cursorScreenX,
            float cursorScreenY,
            bool hasStartScreen,
            float startScreenX,
            float startScreenY,
            double timeSeconds,
            out MarqueeShapeXZ closedShape)
        {
            closedShape = default(MarqueeShapeXZ);
            if (float.IsNaN(worldXZ.X) || float.IsInfinity(worldXZ.X) ||
                float.IsNaN(worldXZ.Z) || float.IsInfinity(worldXZ.Z))
            {
                return PolygonClickOutcome.IgnoredInvalid;
            }

            if (IsSnapToStart(cursorScreenX, cursorScreenY, hasStartScreen, startScreenX, startScreenY))
            {
                TryClose(out closedShape);
                return PolygonClickOutcome.ClosedBySnap;
            }

            if (m_Vertices.Count >= 3 && m_HasLastClick &&
                !double.IsNaN(timeSeconds) && !double.IsInfinity(timeSeconds))
            {
                double dt = timeSeconds - m_LastClickTime;
                if (dt >= 0.0 && dt <= DoubleClickMaxSeconds)
                {
                    float sdx = cursorScreenX - m_LastClickScreenX;
                    float sdy = cursorScreenY - m_LastClickScreenY;
                    if (!float.IsNaN(sdx) && !float.IsNaN(sdy) &&
                        (sdx * sdx + sdy * sdy) <= (DoubleClickMaxScreenPx * DoubleClickMaxScreenPx))
                    {
                        TryClose(out closedShape);
                        return PolygonClickOutcome.ClosedByDoubleClick;
                    }
                }
            }

            float minDistSq = MinVertexDistanceMeters * MinVertexDistanceMeters;
            if (m_Vertices.Count > 0)
            {
                CoordXZ prev = m_Vertices[m_Vertices.Count - 1];
                if ((worldXZ - prev).LengthSquared() < minDistSq)
                {
                    return PolygonClickOutcome.IgnoredTooClose;
                }

                // When only 2 points exist, avoid placing a 3rd point almost on top of vertex 0 before snap unlocks.
                if (m_Vertices.Count == 2 && (worldXZ - m_Vertices[0]).LengthSquared() < minDistSq)
                {
                    return PolygonClickOutcome.IgnoredTooClose;
                }
            }

            int effectiveCap = Math.Min(128, Math.Max(3, MaxVertices));
            if (m_Vertices.Count >= effectiveCap)
            {
                return PolygonClickOutcome.VertexLimitReached;
            }

            float safeY = (float.IsNaN(worldY) || float.IsInfinity(worldY)) ? 0f : worldY;
            m_Vertices.Add(worldXZ);
            m_Heights.Add(safeY);
            Revision++;
            if (!double.IsNaN(timeSeconds) && !double.IsInfinity(timeSeconds))
            {
                m_HasLastClick = true;
                m_LastClickTime = timeSeconds;
                m_LastClickScreenX = cursorScreenX;
                m_LastClickScreenY = cursorScreenY;
            }
            return PolygonClickOutcome.AddedVertex;
        }

        public bool RemoveLastVertex()
        {
            if (m_Vertices.Count == 0)
            {
                return false;
            }

            int last = m_Vertices.Count - 1;
            m_Vertices.RemoveAt(last);
            m_Heights.RemoveAt(last);
            Revision++;
            InvalidateDoubleClick();
            return true;
        }

        public bool TryClose(out MarqueeShapeXZ closedShape)
        {
            if (m_Vertices.Count < 3)
            {
                closedShape = new MarqueeShapeXZ { Mode = MarqueeSelectionMode.Polygon };
                return false;
            }

            closedShape = MarqueeShapeXZ.FromPolygon(m_Vertices, MarqueeSelectionMode.Polygon, MaxVertices);
            Reset();
            return true;
        }
    }

    /// <summary>
    /// Stateful freehand stroke capture session shared between ToolSystem and tests.
    /// Samples points along world step >= 2.5m and auto-closes + simplifies on release.
    /// </summary>
    public sealed class FreehandDraftSession
    {
        public const float MinStepMeters = 2.5f;
        public const float DefaultSimplifyToleranceMeters = 1.0f;

        private readonly List<CoordXZ> m_Points = new List<CoordXZ>(128);
        private readonly List<float> m_Heights = new List<float>(128);

        public int Count => m_Points.Count;
        public bool HasDraft => m_Points.Count > 0;
        public int Revision { get; private set; }
        public IReadOnlyList<CoordXZ> Points => m_Points;
        public IReadOnlyList<float> PointHeights => m_Heights;

        public CoordXZ StartPointXZ => m_Points.Count > 0 ? m_Points[0] : default(CoordXZ);
        public float StartPointY => m_Heights.Count > 0 ? m_Heights[0] : 0f;

        public void Reset()
        {
            if (m_Points.Count > 0)
            {
                m_Points.Clear();
                m_Heights.Clear();
                Revision++;
            }
        }

        public bool Start(CoordXZ worldXZ, float worldY)
        {
            if (float.IsNaN(worldXZ.X) || float.IsNaN(worldXZ.Z) ||
                float.IsInfinity(worldXZ.X) || float.IsInfinity(worldXZ.Z))
            {
                return false;
            }

            m_Points.Clear();
            m_Heights.Clear();
            m_Points.Add(worldXZ);
            float safeY = (float.IsNaN(worldY) || float.IsInfinity(worldY)) ? 0f : worldY;
            m_Heights.Add(safeY);
            Revision++;
            return true;
        }

        public bool AppendPointIfFarEnough(CoordXZ worldXZ, float worldY)
        {
            if (float.IsNaN(worldXZ.X) || float.IsNaN(worldXZ.Z) ||
                float.IsInfinity(worldXZ.X) || float.IsInfinity(worldXZ.Z))
            {
                return false;
            }

            if (m_Points.Count == 0)
            {
                return Start(worldXZ, worldY);
            }

            CoordXZ prev = m_Points[m_Points.Count - 1];
            if ((worldXZ - prev).LengthSquared() < (MinStepMeters * MinStepMeters))
            {
                return false;
            }

            float safeY = (float.IsNaN(worldY) || float.IsInfinity(worldY)) ? 0f : worldY;
            m_Points.Add(worldXZ);
            m_Heights.Add(safeY);
            Revision++;
            return true;
        }

        public bool FinishAndSimplify(out MarqueeShapeXZ closedShape)
        {
            closedShape = MarqueeShapeXZ.FromFreehandStroke(
                m_Points,
                DefaultSimplifyToleranceMeters,
                MarqueeShapeXZ.DefaultFreehandMaxVertices);
            Reset();
            return closedShape.IsUsable();
        }
    }

    public struct SegmentXZ
    {
        public CoordXZ A;
        public CoordXZ B;

        public SegmentXZ(CoordXZ a, CoordXZ b)
        {
            A = a;
            B = b;
        }
    }

    public sealed class ShapeFillOverlayGeometry
    {
        public float FillBandWidth;
        public float MaxTerrainSampleStepMeters = 12f;
        public readonly List<SegmentXZ> FillSegments = new List<SegmentXZ>(96);
        public readonly List<SegmentXZ> GridSegments = new List<SegmentXZ>(64);
        public readonly List<SegmentXZ> OutlineSegments = new List<SegmentXZ>(128);

        public void Clear()
        {
            FillBandWidth = 0f;
            FillSegments.Clear();
            GridSegments.Clear();
            OutlineSegments.Clear();
        }
    }

    /// <summary>
    /// Computes Odd-Even horizontal scanline fill strips, clipped 8m grid segments, and
    /// terrain-subdivided outline segments for arbitrary polygons/freehand shapes so callers
    /// compute geometry only on draft change or closure and cache segments across frames.
    /// </summary>
    public static class ShapeOverlayBuilder
    {
        [ThreadStatic]
        private static List<float> s_CrossingsBuffer;

        public const float DefaultMaxTerrainSampleStepMeters = 12f;
        public const int MaxScanlineBands = 80;
        public const int MinScanlineBands = 8;

        public static void AppendSubdividedEdgeXZ(
            CoordXZ a,
            CoordXZ b,
            float maxStepMeters,
            List<SegmentXZ> outSegments,
            int maxSegmentsPerEdge = 24)
        {
            if (outSegments == null)
            {
                return;
            }

            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            float lenSq = dx * dx + dz * dz;
            if (float.IsNaN(lenSq) || float.IsInfinity(lenSq) || lenSq <= 1e-6f)
            {
                return;
            }

            float step = maxStepMeters > 0.5f ? maxStepMeters : DefaultMaxTerrainSampleStepMeters;
            float len = (float)Math.Sqrt(lenSq);
            int segments = Math.Max(1, Math.Min(maxSegmentsPerEdge, (int)Math.Ceiling(len / step)));
            if (segments == 1)
            {
                outSegments.Add(new SegmentXZ(a, b));
                return;
            }

            CoordXZ prev = a;
            for (int s = 1; s <= segments; s++)
            {
                float t = (float)s / segments;
                CoordXZ curr = (s == segments) ? b : new CoordXZ(a.X + dx * t, a.Z + dz * t);
                outSegments.Add(new SegmentXZ(prev, curr));
                prev = curr;
            }
        }

        public static void BuildFillAndGridSegments(
            IReadOnlyList<CoordXZ> ringVertices,
            ShapeFillOverlayGeometry output,
            float maxTerrainStepMeters = DefaultMaxTerrainSampleStepMeters,
            bool closedOutline = true)
        {
            if (output == null)
            {
                return;
            }

            output.Clear();
            output.MaxTerrainSampleStepMeters = maxTerrainStepMeters > 0.5f ? maxTerrainStepMeters : DefaultMaxTerrainSampleStepMeters;
            if (ringVertices == null || ringVertices.Count < 2)
            {
                return;
            }

            int n = ringVertices.Count;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                CoordXZ v = ringVertices[i];
                if (float.IsNaN(v.X) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Z))
                {
                    return;
                }
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Z < minZ) minZ = v.Z;
                if (v.Z > maxZ) maxZ = v.Z;
            }

            int edgeLimit = closedOutline ? n : (n - 1);
            for (int i = 0; i < edgeLimit; i++)
            {
                CoordXZ a = ringVertices[i];
                CoordXZ b = ringVertices[(i + 1) % n];
                AppendSubdividedEdgeXZ(a, b, output.MaxTerrainSampleStepMeters, output.OutlineSegments);
            }

            if (n < 3)
            {
                return;
            }

            float spanX = maxX - minX;
            float spanZ = maxZ - minZ;
            if (spanX <= 0.1f || spanZ <= 0.1f)
            {
                return;
            }

            if (s_CrossingsBuffer == null)
            {
                s_CrossingsBuffer = new List<float>(32);
            }
            var crossings = s_CrossingsBuffer;
            crossings.Clear();

            // 1. Odd-Even horizontal Z-scanlines covering [minZ, maxZ]
            float targetStepZ = Math.Max(0.8f, Math.Min(4.0f, spanZ / 48f));
            int bandCount = Math.Max(MinScanlineBands, Math.Min(MaxScanlineBands, (int)Math.Ceiling(spanZ / targetStepZ)));
            float bandWidth = spanZ / bandCount;
            output.FillBandWidth = bandWidth;

            for (int b = 0; b < bandCount; b++)
            {
                float zMid = minZ + (b + 0.5f) * bandWidth;
                CollectHorizontalCrossings(ringVertices, zMid, crossings);
                for (int c = 0; c + 1 < crossings.Count; c += 2)
                {
                    float x0 = crossings[c];
                    float x1 = crossings[c + 1];
                    if (x1 - x0 > 0.05f)
                    {
                        output.FillSegments.Add(new SegmentXZ(new CoordXZ(x0, zMid), new CoordXZ(x1, zMid)));
                    }
                }
            }

            // 2. Clipped 8m cell grid lines aligned with scaled CellSizeMeters
            float stepX = MarqueeShapeXZ.CellSizeMeters;
            while (spanX / stepX > 14f) stepX *= 2f;
            float stepZ = MarqueeShapeXZ.CellSizeMeters;
            while (spanZ / stepZ > 14f) stepZ *= 2f;

            float firstX = (float)Math.Ceiling(minX / stepX) * stepX;
            for (float gx = firstX; gx < maxX - 0.1f; gx += stepX)
            {
                if (gx <= minX + 0.1f) continue;
                CollectVerticalCrossings(ringVertices, gx, crossings);
                for (int c = 0; c + 1 < crossings.Count; c += 2)
                {
                    float z0 = crossings[c];
                    float z1 = crossings[c + 1];
                    if (z1 - z0 > 0.1f)
                    {
                        output.GridSegments.Add(new SegmentXZ(new CoordXZ(gx, z0), new CoordXZ(gx, z1)));
                    }
                }
            }

            float firstZ = (float)Math.Ceiling(minZ / stepZ) * stepZ;
            for (float gz = firstZ; gz < maxZ - 0.1f; gz += stepZ)
            {
                if (gz <= minZ + 0.1f) continue;
                CollectHorizontalCrossings(ringVertices, gz, crossings);
                for (int c = 0; c + 1 < crossings.Count; c += 2)
                {
                    float x0 = crossings[c];
                    float x1 = crossings[c + 1];
                    if (x1 - x0 > 0.1f)
                    {
                        output.GridSegments.Add(new SegmentXZ(new CoordXZ(x0, gz), new CoordXZ(x1, gz)));
                    }
                }
            }
        }

        private static void CollectHorizontalCrossings(IReadOnlyList<CoordXZ> verts, float z, List<float> outX)
        {
            outX.Clear();
            int n = verts.Count;
            for (int i = 0; i < n; i++)
            {
                CoordXZ a = verts[i];
                CoordXZ b = verts[(i + 1) % n];
                bool crosses = (a.Z <= z && b.Z > z) || (b.Z <= z && a.Z > z);
                if (!crosses)
                {
                    continue;
                }

                float t = (z - a.Z) / (b.Z - a.Z);
                outX.Add(a.X + t * (b.X - a.X));
            }
            outX.Sort();
        }

        private static void CollectVerticalCrossings(IReadOnlyList<CoordXZ> verts, float x, List<float> outZ)
        {
            outZ.Clear();
            int n = verts.Count;
            for (int i = 0; i < n; i++)
            {
                CoordXZ a = verts[i];
                CoordXZ b = verts[(i + 1) % n];
                bool crosses = (a.X <= x && b.X > x) || (b.X <= x && a.X > x);
                if (!crosses)
                {
                    continue;
                }

                float t = (x - a.X) / (b.X - a.X);
                outZ.Add(a.Z + t * (b.Z - a.Z));
            }
            outZ.Sort();
        }
    }

    public struct OverlayCacheRefreshDecision
    {
        public bool Rebuild2DGeometry;
        public bool Resample3DHeights;
    }

    /// <summary>
    /// Decides when cached XZ scanline/grid geometry vs 3D terrain heights must refresh.
    /// 2D geometry depends solely on polygon vertices; 3D Y coordinates additionally refresh
    /// on terrain slice signals or periodic intervals to pick up async GPU terrain readbacks.
    /// </summary>
    public static class ShapeOverlayCachePolicy
    {
        public const double DefaultHeightRefreshIntervalSeconds = 0.5;

        public static OverlayCacheRefreshDecision EvaluateRefresh(
            bool geometryKeyChanged,
            bool hasHeightDataChanged,
            bool terrainDirtyHint,
            double nowSeconds,
            double lastHeightSampleTimeSeconds,
            double refreshIntervalSeconds = DefaultHeightRefreshIntervalSeconds)
        {
            if (geometryKeyChanged)
            {
                return new OverlayCacheRefreshDecision
                {
                    Rebuild2DGeometry = true,
                    Resample3DHeights = true,
                };
            }

            bool timeExpired = double.IsNaN(lastHeightSampleTimeSeconds) ||
                               double.IsInfinity(lastHeightSampleTimeSeconds) ||
                               nowSeconds < lastHeightSampleTimeSeconds ||
                               (nowSeconds - lastHeightSampleTimeSeconds) >= Math.Max(0.05, refreshIntervalSeconds);

            bool resampleHeights = hasHeightDataChanged || terrainDirtyHint || timeExpired;
            return new OverlayCacheRefreshDecision
            {
                Rebuild2DGeometry = false,
                Resample3DHeights = resampleHeights,
            };
        }
    }

    /// <summary>
    /// Shared tool interaction coordinator combining SelectionMode, Rectangle press/drag,
    /// Polygon draft state, and Freehand stroke drawing so runtime and tests execute one path.
    /// </summary>
    public sealed class MarqueeToolSession
    {
        public MarqueeSelectionMode Mode { get; private set; } = MarqueeSelectionMode.Rectangle;
        public bool PointerPressed { get; set; }
        public bool Dragging { get; set; }
        public bool FreehandDrawing { get; private set; }
        public bool PickSimilarArmed { get; set; }
        public float PressScreenX { get; set; }
        public float PressScreenY { get; set; }
        public CoordXZ PressWorldXZ { get; set; }
        public float PressWorldY { get; set; }
        public CoordXZ DragStartXZ { get; set; }
        public float DragStartY { get; set; }
        public CoordXZ DragEndXZ { get; set; }
        public float DragEndY { get; set; }
        public PolygonDraftSession PolygonDraft { get; } = new PolygonDraftSession();
        public FreehandDraftSession FreehandDraft { get; } = new FreehandDraftSession();

        public bool HasPolygonDraft => PolygonDraft.HasDraft;
        public bool HasFreehandDraft => FreehandDraft.HasDraft;
        public bool IsMouseButtonActive => PointerPressed || Dragging || FreehandDrawing;
        public bool IsBusy => PointerPressed || Dragging || FreehandDrawing || PolygonDraft.HasDraft || FreehandDraft.HasDraft;

        public bool SetMode(MarqueeSelectionMode targetMode)
        {
            MarqueeSelectionMode clean = MarqueePointerLogic.SanitizeSelectionMode((int)targetMode);
            bool changed = Mode != clean;
            CancelActiveInteraction();
            PickSimilarArmed = false;
            Mode = clean;
            return changed;
        }

        public MarqueeSelectionMode CycleMode()
        {
            MarqueeSelectionMode next = MarqueePointerLogic.CycleSelectionMode(Mode);
            SetMode(next);
            return next;
        }

        public void CancelActiveInteraction()
        {
            PointerPressed = false;
            Dragging = false;
            FreehandDrawing = false;
            PolygonDraft.Reset();
            FreehandDraft.Reset();
        }

        public void CancelMouseButtonOnly()
        {
            PointerPressed = false;
            Dragging = false;
            FreehandDrawing = false;
        }

        public void OnPermissionOrModalInterruption(bool abortPolygonDraft)
        {
            PointerPressed = false;
            Dragging = false;
            FreehandDrawing = false;
            FreehandDraft.Reset();
            if (abortPolygonDraft)
            {
                PolygonDraft.Reset();
            }
            else
            {
                PolygonDraft.InvalidateDoubleClick();
            }
        }

        public ToolStepResult StepFrame(in ToolStepInput input)
        {
            var res = default(ToolStepResult);

            ApplySafetyAndInterruptionGuards(in input, ref res);

            if (TryHandleHotkeysEscapeAndRightCancel(in input, ref res))
            {
                return res;
            }

            if (PointerPressed)
            {
                StepRectanglePress(in input, ref res);
                return res;
            }

            if (Dragging)
            {
                StepRectangleDrag(in input, ref res);
                return res;
            }

            if (FreehandDrawing)
            {
                StepFreehandDraw(in input, ref res);
                return res;
            }

            if (!input.HasActiveUndo && input.AllowInput && !input.ColorModalOpen && !input.MouseOverUi &&
                !input.ImportantModalOpen && !input.HasQueuedDeletion &&
                input.LeftPressedThisFrame && (PickSimilarArmed || input.HasCursorWorld))
            {
                StepPrimaryPressByMode(in input, ref res);
            }

            return res;
        }

        private void ApplySafetyAndInterruptionGuards(in ToolStepInput input, ref ToolStepResult res)
        {
            if (input.HasActiveUndo && IsBusy)
            {
                CancelActiveInteraction();
                res.ShouldResetHighlights = true;
            }

            if (input.HasActiveUndo || !input.AllowInput || input.ColorModalOpen || input.ImportantModalOpen ||
                !input.HasPending || PolygonDraft.HasDraft || FreehandDraft.HasDraft || FreehandDrawing || Dragging)
            {
                PickSimilarArmed = false;
            }

            if (!input.AllowInput || input.ColorModalOpen || input.ImportantModalOpen)
            {
                if (PointerPressed || Dragging || FreehandDrawing || FreehandDraft.HasDraft)
                {
                    CancelMouseButtonOnly();
                    FreehandDraft.Reset();
                    res.ShouldResetHighlights = true;
                }
                if (PolygonDraft.HasDraft)
                {
                    PolygonDraft.InvalidateDoubleClick();
                }
            }
        }

        private bool TryHandleHotkeysEscapeAndRightCancel(in ToolStepInput input, ref ToolStepResult res)
        {
            bool hasPolygonDraft = PolygonDraft.HasDraft;
            bool pointerIdle = !IsBusy;
            bool toolHotkeysAllowed = input.AllowInput && !input.ColorModalOpen;

            res.ToolHotkeysAllowed = toolHotkeysAllowed;
            res.CanConfirmPending = !input.HasActiveUndo && input.HasPending && pointerIdle &&
                                    toolHotkeysAllowed && !input.ImportantModalOpen && !input.PendingAffectsProtected;
            res.CanClosePolygonByEnter = toolHotkeysAllowed && !input.HasActiveUndo && !input.ImportantModalOpen &&
                                         !input.HasQueuedDeletion &&
                                         Mode == MarqueeSelectionMode.Polygon &&
                                         PolygonDraft.Count >= 3;
            res.CanForceConfirm = toolHotkeysAllowed && !input.HasActiveUndo && pointerIdle && !input.HasQueuedDeletion &&
                                  input.HasPending && input.ImportantModalOpen && !input.PendingAffectsProtected;
            res.CanExcludeImportant = toolHotkeysAllowed && !input.HasActiveUndo && pointerIdle && !input.HasQueuedDeletion &&
                                      input.HasPending && input.ImportantModalOpen;
            res.CanClear = MarqueePointerLogic.CanClearSelection(
                toolHotkeysAllowed,
                pointerIdle,
                input.ImportantModalOpen,
                input.HasPending,
                input.HasRetainedEdits);

            if (res.CanExcludeImportant && input.ExcludeImportantPressed)
            {
                res.Outcome = ToolStepOutcome.ExcludeImportant;
                return true;
            }

            if (res.CanForceConfirm && input.ForceConfirmImportantPressed)
            {
                res.Outcome = ToolStepOutcome.ForceConfirmImportant;
                return true;
            }

            if (res.CanClear && input.ClearSelectionPressed)
            {
                res.Outcome = input.ImportantModalOpen
                    ? ToolStepOutcome.CloseImportantModalWithSound
                    : ToolStepOutcome.ClearSelectionWithSound;
                return true;
            }

            if (res.CanClosePolygonByEnter && (input.ConfirmActionPressed || input.EnterKeyPressed))
            {
                if (PolygonDraft.TryClose(out MarqueeShapeXZ closedShape))
                {
                    res.ClosedShape = closedShape;
                    res.Outcome = ToolStepOutcome.CompletePolygonShape;
                    return true;
                }
            }

            if (res.CanConfirmPending && input.ConfirmActionPressed)
            {
                res.Outcome = ToolStepOutcome.ConfirmPending;
                return true;
            }

            if (PickSimilarArmed && input.AllowInput && !input.ColorModalOpen && !input.ImportantModalOpen)
            {
                if (input.EscapePressedThisFrame &&
                    (input.EscapeGate == null || input.EscapeGate.TryConsume(input.FrameCount)))
                {
                    PickSimilarArmed = false;
                    res.Outcome = ToolStepOutcome.CancelPickSimilarWithSound;
                    return true;
                }

                if (input.RightPressedThisFrame && !input.MouseOverUi)
                {
                    PickSimilarArmed = false;
                    res.Outcome = ToolStepOutcome.CancelPickSimilarWithSound;
                    return true;
                }
            }

            bool hasActiveStrokeOrDrag = Dragging || FreehandDrawing || FreehandDraft.HasDraft;
            EscapeAction escapeAction = MarqueePointerLogic.TryConsumeToolEscape(
                input.EscapeGate,
                input.FrameCount,
                input.AllowInput,
                input.EscapePressedThisFrame,
                input.ImportantModalOpen,
                input.ColorModalOpen,
                hasActiveStrokeOrDrag,
                PointerPressed,
                hasPolygonDraft,
                input.HasPending,
                input.HasRetainedEdits);

            if (escapeAction != EscapeAction.None)
            {
                switch (escapeAction)
                {
                    case EscapeAction.CloseColorModal:
                        res.Outcome = ToolStepOutcome.DeferredToUiColorModalEscape;
                        break;
                    case EscapeAction.CloseImportantModal:
                        res.Outcome = ToolStepOutcome.CloseImportantModalWithSound;
                        break;
                    case EscapeAction.CancelPointerOrDrag:
                        CancelActiveInteraction();
                        res.ShouldResetHighlights = true;
                        res.Outcome = ToolStepOutcome.CancelledActiveInteraction;
                        break;
                    case EscapeAction.ClearSelection:
                        res.Outcome = ToolStepOutcome.ClearSelectionWithSound;
                        break;
                }
                return true;
            }

            RightCancelAction rightCancel = input.ColorModalOpen
                ? RightCancelAction.None
                : MarqueePointerLogic.ResolveRightCancelAction(
                    input.AllowInput,
                    input.RightPressedThisFrame,
                    input.MouseOverUi,
                    input.ImportantModalOpen,
                    hasActiveStrokeOrDrag,
                    PointerPressed,
                    hasPolygonDraft,
                    input.HasPending);

            if (rightCancel != RightCancelAction.None)
            {
                switch (rightCancel)
                {
                    case RightCancelAction.CloseImportantModal:
                        res.Outcome = ToolStepOutcome.CloseImportantModalWithSound;
                        break;
                    case RightCancelAction.CancelPointerOrDrag:
                        CancelActiveInteraction();
                        res.ShouldResetHighlights = true;
                        res.Outcome = ToolStepOutcome.CancelledActiveInteraction;
                        break;
                    case RightCancelAction.RemoveLastPolygonVertex:
                        PolygonDraft.RemoveLastVertex();
                        res.Outcome = ToolStepOutcome.RemovedPolygonVertex;
                        break;
                    case RightCancelAction.RemoveEntityAtCursor:
                        res.Outcome = ToolStepOutcome.RemoveEntityAtCursor;
                        break;
                    case RightCancelAction.ExitTool:
                        res.Outcome = ToolStepOutcome.ExitTool;
                        break;
                }
                return true;
            }

            return false;
        }

        private void StepPrimaryPressByMode(in ToolStepInput input, ref ToolStepResult res)
        {
            if (PickSimilarArmed)
            {
                PickSimilarArmed = false;
                res.Outcome = ToolStepOutcome.PickSimilarAtCursor;
                return;
            }

            if (Mode == MarqueeSelectionMode.Polygon)
            {
                PolygonClickOutcome clickOutcome = PolygonDraft.HandleLeftClick(
                    input.PressGroundWorldXZ,
                    input.PressGroundWorldY,
                    input.CursorScreenX,
                    input.CursorScreenY,
                    input.HasStartScreen,
                    input.StartScreenX,
                    input.StartScreenY,
                    input.TimeSeconds,
                    out MarqueeShapeXZ closedShape);

                switch (clickOutcome)
                {
                    case PolygonClickOutcome.AddedVertex:
                        res.Outcome = ToolStepOutcome.PolygonAddedVertex;
                        break;
                    case PolygonClickOutcome.ClosedBySnap:
                    case PolygonClickOutcome.ClosedByDoubleClick:
                        res.ClosedShape = closedShape;
                        res.Outcome = ToolStepOutcome.CompletePolygonShape;
                        break;
                    case PolygonClickOutcome.VertexLimitReached:
                        res.Outcome = ToolStepOutcome.PolygonVertexLimitReached;
                        break;
                }
                return;
            }

            if (Mode == MarqueeSelectionMode.Freehand)
            {
                if (FreehandDraft.Start(input.PressGroundWorldXZ, input.PressGroundWorldY))
                {
                    FreehandDrawing = true;
                    res.ShouldClearEmptyNotice = !input.HasPending;
                    res.Outcome = ToolStepOutcome.FreehandStrokeStarted;
                }
                return;
            }

            if (Mode == MarqueeSelectionMode.Rectangle)
            {
                PointerPressed = true;
                PressScreenX = input.CursorScreenX;
                PressScreenY = input.CursorScreenY;
                PressWorldXZ = input.PressGroundWorldXZ;
                PressWorldY = input.PressGroundWorldY;
                res.Outcome = ToolStepOutcome.RectanglePressStarted;
            }
        }

        private void StepRectanglePress(in ToolStepInput input, ref ToolStepResult res)
        {
            if (input.HasActiveUndo || !input.AllowInput || input.ColorModalOpen ||
                input.ImportantModalOpen || input.HasQueuedDeletion)
            {
                PointerPressed = false;
                res.Outcome = ToolStepOutcome.RectanglePressCleared;
                return;
            }

            float dx = input.CursorScreenX - PressScreenX;
            float dy = input.CursorScreenY - PressScreenY;
            PointerPressOutcome pressOutcome = MarqueePointerLogic.ResolvePressUpdate(dx, dy, input.LeftStillPressed);

            if (pressOutcome == PointerPressOutcome.PromoteToDrag)
            {
                PointerPressed = false;
                Dragging = true;
                DragStartXZ = PressWorldXZ;
                DragStartY = PressWorldY;
                DragEndXZ = input.HasCursorWorld ? input.CursorWorldXZ : PressWorldXZ;
                DragEndY = input.HasCursorWorld ? input.CursorWorldY : PressWorldY;
                res.ShouldClearEmptyNotice = !input.HasPending;
                res.Outcome = ToolStepOutcome.RectanglePromotedToDrag;
            }
            else if (pressOutcome == PointerPressOutcome.CompleteDragOnRelease)
            {
                PointerPressed = false;
                if (MarqueePointerLogic.ResolveDragUpdate(
                        input.AllowInput && !input.ColorModalOpen,
                        input.ImportantModalOpen,
                        input.HasActiveUndo,
                        input.HasQueuedDeletion,
                        stillPressed: false) == DragUpdateOutcome.Cancel)
                {
                    CancelActiveInteraction();
                    res.ShouldResetHighlights = true;
                    res.Outcome = ToolStepOutcome.CancelledActiveInteraction;
                    return;
                }

                DragStartXZ = PressWorldXZ;
                DragStartY = PressWorldY;
                DragEndXZ = input.HasCursorWorld ? input.CursorWorldXZ : PressWorldXZ;
                DragEndY = input.HasCursorWorld ? input.CursorWorldY : PressWorldY;
                res.ShouldClearEmptyNotice = !input.HasPending;
                res.Outcome = ToolStepOutcome.RectangleCompleteDrag;
            }
            else if (pressOutcome == PointerPressOutcome.SingleClick)
            {
                PointerPressed = false;
                res.Outcome = ToolStepOutcome.RectangleSingleClickAdd;
            }
        }

        private void StepRectangleDrag(in ToolStepInput input, ref ToolStepResult res)
        {
            DragUpdateOutcome dragOutcome = MarqueePointerLogic.ResolveDragUpdate(
                input.AllowInput && !input.ColorModalOpen,
                input.ImportantModalOpen,
                input.HasActiveUndo,
                input.HasQueuedDeletion,
                input.LeftStillPressed);

            if (dragOutcome == DragUpdateOutcome.Cancel)
            {
                CancelActiveInteraction();
                res.ShouldResetHighlights = true;
                res.Outcome = ToolStepOutcome.CancelledActiveInteraction;
                return;
            }

            if (input.HasCursorWorld)
            {
                DragEndXZ = input.CursorWorldXZ;
                DragEndY = input.CursorWorldY;
            }

            res.Outcome = dragOutcome == DragUpdateOutcome.Continue
                ? ToolStepOutcome.RectangleContinueDrag
                : ToolStepOutcome.RectangleCompleteDrag;
            if (dragOutcome != DragUpdateOutcome.Continue)
            {
                Dragging = false;
            }
        }

        private void StepFreehandDraw(in ToolStepInput input, ref ToolStepResult res)
        {
            DragUpdateOutcome drawOutcome = MarqueePointerLogic.ResolveDragUpdate(
                input.AllowInput && !input.ColorModalOpen,
                input.ImportantModalOpen,
                input.HasActiveUndo,
                input.HasQueuedDeletion,
                input.LeftStillPressed);

            if (drawOutcome == DragUpdateOutcome.Cancel)
            {
                CancelActiveInteraction();
                res.ShouldResetHighlights = true;
                res.Outcome = ToolStepOutcome.CancelledActiveInteraction;
                return;
            }

            if (input.HasCursorWorld)
            {
                FreehandDraft.AppendPointIfFarEnough(input.CursorWorldXZ, input.CursorWorldY);
            }

            if (drawOutcome == DragUpdateOutcome.Continue)
            {
                res.Outcome = ToolStepOutcome.FreehandStrokeContinue;
                return;
            }

            FreehandDrawing = false;
            FreehandDraft.FinishAndSimplify(out MarqueeShapeXZ closedShape);
            res.ClosedShape = closedShape;
            res.Outcome = ToolStepOutcome.CompleteFreehandShape;
        }
    }

    public enum ToolStepOutcome : byte
    {
        None = 0,
        ExcludeImportant = 1,
        ForceConfirmImportant = 2,
        CloseImportantModalWithSound = 3,
        ClearSelectionWithSound = 4,
        ConfirmPending = 5,
        DeferredToUiColorModalEscape = 6,
        CancelledActiveInteraction = 7,
        RemovedPolygonVertex = 8,
        RemoveEntityAtCursor = 9,
        ExitTool = 10,
        PolygonAddedVertex = 11,
        PolygonVertexLimitReached = 12,
        CompletePolygonShape = 13,
        RectanglePressStarted = 14,
        RectanglePressCleared = 15,
        RectanglePromotedToDrag = 16,
        RectangleContinueDrag = 17,
        RectangleCompleteDrag = 18,
        RectangleSingleClickAdd = 19,
        FreehandStrokeStarted = 20,
        FreehandStrokeContinue = 21,
        CompleteFreehandShape = 22,
        PickSimilarAtCursor = 23,
        CancelPickSimilarWithSound = 24,
    }

    public struct ToolStepInput
    {
        public EscapeFrameGate EscapeGate;
        public int FrameCount;
        public double TimeSeconds;
        public bool AllowInput;
        public bool MouseOverUi;
        public bool ImportantModalOpen;
        public bool ColorModalOpen;
        public bool HasActiveUndo;
        public bool HasQueuedDeletion;
        public bool HasPending;
        public bool PendingAffectsProtected;
        public bool HasRetainedEdits;
        public bool ExcludeImportantPressed;
        public bool ForceConfirmImportantPressed;
        public bool ClearSelectionPressed;
        public bool ConfirmActionPressed;
        public bool EnterKeyPressed;
        public bool EscapePressedThisFrame;
        public bool RightPressedThisFrame;
        public bool LeftPressedThisFrame;
        public bool LeftStillPressed;
        public bool HasCursorWorld;
        public CoordXZ CursorWorldXZ;
        public float CursorWorldY;
        public CoordXZ PressGroundWorldXZ;
        public float PressGroundWorldY;
        public float CursorScreenX;
        public float CursorScreenY;
        public bool HasStartScreen;
        public float StartScreenX;
        public float StartScreenY;
    }

    public struct ToolStepResult
    {
        public ToolStepOutcome Outcome;
        public bool ToolHotkeysAllowed;
        public bool CanConfirmPending;
        public bool CanClosePolygonByEnter;
        public bool CanForceConfirm;
        public bool CanExcludeImportant;
        public bool CanClear;
        public bool ShouldResetHighlights;
        public bool ShouldClearEmptyNotice;
        public MarqueeShapeXZ ClosedShape;
    }

    public static class MarqueePointerLogic
    {
        public const float DefaultDragThresholdPx = 7f;

        public static bool ShouldPromoteToDrag(float dx, float dy, float thresholdPx = DefaultDragThresholdPx)
        {
            if (float.IsNaN(dx) || float.IsNaN(dy) || float.IsInfinity(dx) || float.IsInfinity(dy) ||
                float.IsNaN(thresholdPx) || thresholdPx <= 0f)
            {
                return false;
            }

            return (dx * dx + dy * dy) > (thresholdPx * thresholdPx);
        }

        public static PointerPressOutcome ResolvePressUpdate(
            float dx,
            float dy,
            bool stillPressed,
            float thresholdPx = DefaultDragThresholdPx)
        {
            bool exceededThreshold = ShouldPromoteToDrag(dx, dy, thresholdPx);
            if (stillPressed)
            {
                return exceededThreshold ? PointerPressOutcome.PromoteToDrag : PointerPressOutcome.StayPressed;
            }

            return exceededThreshold ? PointerPressOutcome.CompleteDragOnRelease : PointerPressOutcome.SingleClick;
        }

        public static bool IsInputAllowed(bool appFocused, bool hasInputFieldFocus)
        {
            return appFocused && !hasInputFieldFocus;
        }

        public static DragUpdateOutcome ResolveDragUpdate(
            bool allowInput,
            bool importantModalOpen,
            bool hasActiveUndo,
            bool queuedDeletion,
            bool stillPressed)
        {
            if (!allowInput || importantModalOpen || hasActiveUndo || queuedDeletion)
            {
                return DragUpdateOutcome.Cancel;
            }

            return stillPressed ? DragUpdateOutcome.Continue : DragUpdateOutcome.Complete;
        }

        public static MarqueeSelectionMode SanitizeSelectionMode(int rawMode)
        {
            return (rawMode >= (int)MarqueeSelectionMode.Rectangle && rawMode <= (int)MarqueeSelectionMode.Freehand)
                ? (MarqueeSelectionMode)rawMode
                : MarqueeSelectionMode.Rectangle;
        }

        public static MarqueeSelectionMode CycleSelectionMode(MarqueeSelectionMode current)
        {
            switch (current)
            {
                case MarqueeSelectionMode.Rectangle:
                    return MarqueeSelectionMode.Polygon;
                case MarqueeSelectionMode.Polygon:
                    return MarqueeSelectionMode.Freehand;
                default:
                    return MarqueeSelectionMode.Rectangle;
            }
        }

        public static bool CanCycleSelectionMode(
            bool allowInput,
            bool toolOrBulldozeActive,
            bool mouseButtonBusy,
            bool colorModalOpen,
            bool importantModalOpen,
            bool hasActiveUndo)
        {
            return allowInput &&
                   toolOrBulldozeActive &&
                   !mouseButtonBusy &&
                   !colorModalOpen &&
                   !importantModalOpen &&
                   !hasActiveUndo;
        }

        public static RightCancelAction ResolveRightCancelAction(
            bool allowInput,
            bool cancelPressedThisFrame,
            bool mouseOverUi,
            bool importantModalOpen,
            bool dragging,
            bool pointerPressed,
            bool hasPending)
        {
            return ResolveRightCancelAction(
                allowInput,
                cancelPressedThisFrame,
                mouseOverUi,
                importantModalOpen,
                dragging,
                pointerPressed,
                hasPolygonDraft: false,
                hasPending: hasPending);
        }

        public static RightCancelAction ResolveRightCancelAction(
            bool allowInput,
            bool cancelPressedThisFrame,
            bool mouseOverUi,
            bool importantModalOpen,
            bool dragging,
            bool pointerPressed,
            bool hasPolygonDraft,
            bool hasPending)
        {
            if (!allowInput || !cancelPressedThisFrame)
            {
                return RightCancelAction.None;
            }

            if (importantModalOpen)
            {
                return RightCancelAction.CloseImportantModal;
            }

            if (dragging || pointerPressed)
            {
                return RightCancelAction.CancelPointerOrDrag;
            }

            if (hasPolygonDraft)
            {
                return RightCancelAction.RemoveLastPolygonVertex;
            }

            if (mouseOverUi)
            {
                return RightCancelAction.None;
            }

            return hasPending ? RightCancelAction.RemoveEntityAtCursor : RightCancelAction.ExitTool;
        }

        public static bool CanTriggerUndo(
            bool canUseMod,
            bool allowInput,
            bool pointerIdle,
            bool importantModalOpen,
            bool colorModalOpen,
            bool queuedDeletion,
            bool undoInProgress,
            bool canUndoForMask)
        {
            return canUseMod &&
                   allowInput &&
                   pointerIdle &&
                   !importantModalOpen &&
                   !colorModalOpen &&
                   !queuedDeletion &&
                   !undoInProgress &&
                   canUndoForMask;
        }

        public static bool CanUsePanelHotkeys(
            bool allowInput,
            bool toolOrBulldozeActive,
            bool pointerIdle,
            bool colorModalOpen)
        {
            return allowInput && toolOrBulldozeActive && pointerIdle && !colorModalOpen;
        }

        /// <summary>
        /// Decides what a single ESC press should dismiss/cancel in priority order.
        /// Evaluated in one place (backend) to avoid double-consumption with UI DOM listeners.
        /// </summary>
        public static EscapeAction ResolveEscapeAction(
            bool importantModalOpen,
            bool dragging,
            bool pointerPressed,
            bool hasPending,
            bool hasRetainedEdits)
        {
            return ResolveEscapeAction(
                importantModalOpen,
                colorModalOpen: false,
                dragging: dragging,
                pointerPressed: pointerPressed,
                hasPending: hasPending,
                hasRetainedEdits: hasRetainedEdits);
        }

        public static EscapeAction ResolveEscapeAction(
            bool importantModalOpen,
            bool colorModalOpen,
            bool dragging,
            bool pointerPressed,
            bool hasPending,
            bool hasRetainedEdits)
        {
            return ResolveEscapeAction(
                importantModalOpen,
                colorModalOpen,
                dragging,
                pointerPressed,
                hasPolygonDraft: false,
                hasPending: hasPending,
                hasRetainedEdits: hasRetainedEdits);
        }

        public static EscapeAction ResolveEscapeAction(
            bool importantModalOpen,
            bool colorModalOpen,
            bool dragging,
            bool pointerPressed,
            bool hasPolygonDraft,
            bool hasPending,
            bool hasRetainedEdits)
        {
            if (importantModalOpen)
            {
                return EscapeAction.CloseImportantModal;
            }

            if (colorModalOpen)
            {
                return EscapeAction.CloseColorModal;
            }

            if (dragging || pointerPressed || hasPolygonDraft)
            {
                return EscapeAction.CancelPointerOrDrag;
            }

            if (hasPending || hasRetainedEdits)
            {
                return EscapeAction.ClearSelection;
            }

            return EscapeAction.None;
        }

        /// <summary>
        /// Tool-side unified ESC dispatch. Skips if already consumed on this frame (e.g., by UIUpdate).
        /// When CloseColorModal wins, intentionally defers consumption to the UI handler that
        /// actually closes the modal while still blocking drag/selection cancellation for this frame.
        /// </summary>
        public static EscapeAction TryConsumeToolEscape(
            EscapeFrameGate gate,
            int frameCount,
            bool allowInput,
            bool escapePressedThisFrame,
            bool importantModalOpen,
            bool colorModalOpen,
            bool dragging,
            bool pointerPressed,
            bool hasPending,
            bool hasRetainedEdits)
        {
            return TryConsumeToolEscape(
                gate,
                frameCount,
                allowInput,
                escapePressedThisFrame,
                importantModalOpen,
                colorModalOpen,
                dragging,
                pointerPressed,
                hasPolygonDraft: false,
                hasPending: hasPending,
                hasRetainedEdits: hasRetainedEdits);
        }

        public static EscapeAction TryConsumeToolEscape(
            EscapeFrameGate gate,
            int frameCount,
            bool allowInput,
            bool escapePressedThisFrame,
            bool importantModalOpen,
            bool colorModalOpen,
            bool dragging,
            bool pointerPressed,
            bool hasPolygonDraft,
            bool hasPending,
            bool hasRetainedEdits)
        {
            if (!allowInput || !escapePressedThisFrame || (gate != null && gate.IsConsumedThisFrame(frameCount)))
            {
                return EscapeAction.None;
            }

            EscapeAction action = ResolveEscapeAction(
                importantModalOpen,
                colorModalOpen,
                dragging,
                pointerPressed,
                hasPolygonDraft,
                hasPending,
                hasRetainedEdits);

            if (action == EscapeAction.None || action == EscapeAction.CloseColorModal)
            {
                return action;
            }

            if (gate != null && !gate.TryConsume(frameCount))
            {
                return EscapeAction.None;
            }

            return action;
        }

        /// <summary>
        /// UI-side unified ESC dispatch for the persistent Color Palette modal. Uses the same
        /// EscapeFrameGate so closing the modal registers the frame (blocking ToolUpdate from
        /// double-consuming if UIUpdate runs first) and skips if ToolUpdate already consumed ESC.
        /// </summary>
        public static bool TryConsumeUiColorModalEscape(
            EscapeFrameGate gate,
            int frameCount,
            bool canUse,
            bool colorModalOpen,
            bool importantModalOpen,
            bool allowInput,
            bool escapePressedThisFrame)
        {
            if (!canUse ||
                !colorModalOpen ||
                importantModalOpen ||
                !allowInput ||
                !escapePressedThisFrame)
            {
                return false;
            }

            return gate == null || gate.TryConsume(frameCount);
        }

        public static bool CanClearSelection(
            bool allowInput,
            bool pointerIdle,
            bool importantModalOpen,
            bool hasPending,
            bool hasRetainedEdits)
        {
            return allowInput && pointerIdle && (importantModalOpen || hasPending || hasRetainedEdits);
        }

        /// <summary>
        /// True when a re-evaluated selection still holds active items or user edit state
        /// (a locked box or manually click-added entities) that must survive filter toggles.
        /// </summary>
        public static bool ShouldRetainFilteredSelection(
            int visibleCount,
            bool hasUsableQuad,
            int manuallyAddedCount)
        {
            return visibleCount > 0 || hasUsableQuad || manuallyAddedCount > 0;
        }

        public static string ComputeHiddenRetainedNotice(
            int visibleCount,
            bool hasUsableQuad,
            int manuallyAddedCount)
        {
            if (visibleCount > 0)
            {
                return string.Empty;
            }

            if (manuallyAddedCount > 0)
            {
                return $"hidden_retained|{manuallyAddedCount}";
            }

            if (hasUsableQuad)
            {
                return "hidden_box";
            }

            return string.Empty;
        }
    }

    public static class SelectionHistoryPolicy
    {
        public const int MaxHistoryCount = 5;
        public const float VertexToleranceMeters = 0.15f;
        public const float AreaToleranceSqMeters = 0.5f;

        public static bool IsEquivalent(in MarqueeShapeXZ a, in MarqueeShapeXZ b, float vertexTolerance = VertexToleranceMeters)
        {
            if (a.Mode != b.Mode)
            {
                return false;
            }

            if (System.Math.Abs(a.AreaSqMeters - b.AreaSqMeters) > AreaToleranceSqMeters)
            {
                return false;
            }

            int countA = a.VertexCount;
            int countB = b.VertexCount;
            if (countA != countB || countA < 3)
            {
                return false;
            }

            float tolSq = vertexTolerance * vertexTolerance;

            // Direct comparison
            bool match = true;
            for (int i = 0; i < countA; i++)
            {
                if ((a.GetVertex(i) - b.GetVertex(i)).LengthSquared() > tolSq)
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;

            // Cyclic shifts
            for (int shift = 1; shift < countA; shift++)
            {
                bool shiftMatch = true;
                for (int i = 0; i < countA; i++)
                {
                    if ((a.GetVertex((i + shift) % countA) - b.GetVertex(i)).LengthSquared() > tolSq)
                    {
                        shiftMatch = false;
                        break;
                    }
                }
                if (shiftMatch) return true;
            }

            // Reverse cyclic shifts
            for (int shift = 0; shift < countA; shift++)
            {
                bool revMatch = true;
                for (int i = 0; i < countA; i++)
                {
                    int revIdx = (countA - 1 - i + shift) % countA;
                    if ((a.GetVertex(revIdx) - b.GetVertex(i)).LengthSquared() > tolSq)
                    {
                        revMatch = false;
                        break;
                    }
                }
                if (revMatch) return true;
            }

            return false;
        }

        public static bool PushHistory(List<MarqueeShapeXZ> history, in MarqueeShapeXZ shape, int maxCount = MaxHistoryCount)
        {
            if (history == null || !shape.IsUsable())
            {
                return false;
            }

            for (int i = 0; i < history.Count; i++)
            {
                if (IsEquivalent(history[i], shape))
                {
                    history.RemoveAt(i);
                    break;
                }
            }

            while (history.Count >= maxCount)
            {
                history.RemoveAt(0);
            }

            history.Add(shape);
            return true;
        }
    }

    public static class RestoreSelectionGate
    {
        public static bool CanRestore(
            bool isToolActive,
            bool hasActiveUndo,
            bool isPointerBusy,
            bool isMouseButtonBusy,
            bool importantModalOpen,
            bool colorModalOpen,
            bool hasActiveDraft,
            bool hasQueuedDeletion,
            int historyCount)
        {
            if (!isToolActive) return false;
            if (historyCount <= 0) return false;
            if (hasActiveUndo) return false;
            if (isPointerBusy || isMouseButtonBusy) return false;
            if (importantModalOpen || colorModalOpen) return false;
            if (hasActiveDraft) return false;
            if (hasQueuedDeletion) return false;
            return true;
        }
    }

    public static class SelectionRestoreExecutionPolicy
    {
        public static bool ShouldExecuteAutoDelete(bool allowAutoDelete, int count, int importantCount, bool confirmBeforeDelete, int threshold)
        {
            if (!allowAutoDelete) return false;
            return ConfirmationPolicy.ShouldAutoDelete(count, importantCount, confirmBeforeDelete, threshold);
        }
    }

    public static class ImpactReportLogic
    {
        public const int DefaultTopCount = 12;

        public static string BuildReport(IEnumerable<KeyValuePair<string, int>> prefabCounts, int topCount = DefaultTopCount)
        {
            if (prefabCounts == null)
            {
                return string.Empty;
            }

            var list = new List<KeyValuePair<string, int>>(prefabCounts);
            if (list.Count == 0)
            {
                return string.Empty;
            }

            list.Sort((a, b) =>
            {
                int cmp = b.Value.CompareTo(a.Value);
                if (cmp != 0) return cmp;
                return string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });

            var sb = new System.Text.StringBuilder();
            int top = System.Math.Min(topCount, list.Count);
            for (int i = 0; i < top; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(list[i].Key).Append('|').Append(list[i].Value);
            }

            int remaining = list.Count - top;
            if (remaining > 0)
            {
                int remainingSum = 0;
                for (int i = top; i < list.Count; i++)
                {
                    remainingSum += list[i].Value;
                }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("MORE|").Append(remainingSum);
            }

            return sb.ToString();
        }
    }

    public static class SoloFilterPolicy
    {
        public static FilterPresetValues CalculateSolo(string soloKey)
        {
            var p = new FilterPresetValues();
            if (string.IsNullOrEmpty(soloKey))
            {
                return p;
            }

            switch (soloKey.ToLowerInvariant())
            {
                case "residential": p.Residential = true; break;
                case "commercial": p.Commercial = true; break;
                case "industrial": p.Industrial = true; break;
                case "office": p.Office = true; break;
                case "buildings": p.Buildings = true; break;
                case "trees": p.Trees = true; break;
                case "props": p.Props = true; break;
                case "decals": p.Decals = true; break;
                case "moving": p.Moving = true; break;
                case "networks": p.Networks = true; break;
                case "areas": p.Areas = true; break;
                case "netroads": p.Networks = true; p.NetRoads = true; break;
                case "nettracks": p.Networks = true; p.NetTracks = true; break;
                case "netpipelines": p.Networks = true; p.NetPipelines = true; break;
                case "netpowerlines": p.Networks = true; p.NetPowerLines = true; break;
                case "netother": p.Networks = true; p.NetOther = true; break;
            }
            return p;
        }
    }
}
