// Game component structs, transcribed from Game.dll by reflection (field types and names, enum
// membership) so the offline restore path reads the same shapes the game does. Only members the
// restore code touches are present; unrelated fields are omitted.
using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Jobs
{
    public struct JobHandle { public void Complete() { } }
}

namespace Unity.Entities
{
    public struct EntityArchetype { public IntPtr m_Archetype; public bool Valid => m_Archetype != IntPtr.Zero; }
}

namespace Colossal.Collections
{
    public interface INativeQuadTreeIterator<TItem, TBounds>
    {
        bool Intersect(TBounds bounds);
        void Iterate(TBounds bounds, TItem item);
    }

    public struct NativeQuadTree<TItem, TBounds>
    {
        public void Iterate<TIterator>(ref TIterator iterator) where TIterator : struct, INativeQuadTreeIterator<TItem, TBounds> { }
    }
}

namespace Game.Common
{
    public struct Deleted : IComponentData { }
    public struct Created : IComponentData { }
    public struct Applied : IComponentData { }
    public struct Updated : IComponentData { }
    public struct BatchesUpdated : IComponentData { }
    public struct Owner : IComponentData { public Entity m_Owner; }
    public struct PseudoRandomSeed : IComponentData { public ushort m_Seed; }
    public struct QuadTreeBoundsXZ { public Colossal.Mathematics.Bounds3 m_Bounds; }
}

namespace Game.Net
{
    [Flags]
    public enum GeometryFlags : int
    {
        None = 0,
        StraightEdges = 1,
        StrictNodes = 2,
        SnapCellSize = 4,
        SupportRoundabout = 8,
        LoweredIsTunnel = 16,
        RaisedIsElevated = 32,
        NoEdgeConnection = 64,
        SnapToNetAreas = 128,
        StraightEnds = 256,
        RequireElevated = 512,
        SymmetricalEdges = 1024,
        BlockZone = 2048,
        Directional = 4096,
        SmoothSlopes = 8192,
        SmoothElevation = 16384,
        FlipTrafficHandedness = 32768,
        Asymmetric = 65536,
        FlattenTerrain = 131072,
        ClipTerrain = 262144,
        Marker = 524288,
        MiddlePillars = 1048576,
        StandingNodes = 2097152,
        ExclusiveGround = 4194304,
        NoCurveSplit = 8388608,
        SubOwner = 16777216,
        OnWater = 33554432,
        IsLefthanded = 67108864,
        InvertCompositionHandedness = 134217728,
        FlipCompositionHandedness = 268435456,
        ElevatedIsRaised = 536870912,
        NoAutoElevation = 1073741824,
    }

    public struct Node : IComponentData { public float3 m_Position; public quaternion m_Rotation; }
    public struct Edge : IComponentData { public Entity m_Start; public Entity m_End; }
    public struct Curve : IComponentData { public Colossal.Mathematics.Bezier4x3 m_Bezier; public float m_Length; }
    public struct Elevation : IComponentData { public float2 m_Elevation; }
    public struct SubLane : IBufferElementData { public Entity m_SubLane; public PathMethod m_PathMethods; }
    public struct SubNet : IBufferElementData { public Entity m_SubNet; }
    public struct ConnectedEdge : IBufferElementData { public Entity m_Edge; }
    public struct Upgraded : IComponentData { public Game.Prefabs.CompositionFlags m_Flags; }
    public struct Composition : IComponentData { public Entity m_Edge; public Entity m_StartNode; public Entity m_EndNode; }
    public struct Road : IComponentData { }
    public struct Lane : IComponentData { public Entity m_StartNode; public Entity m_MiddleNode; public Entity m_EndNode; }
    public struct OutsideConnection : IComponentData { }
    public struct SubReplacement : IBufferElementData, IEquatable<SubReplacement>
    {
        public Entity m_Prefab;
        public byte m_Type;
        public byte m_Side;
        public bool Equals(SubReplacement other) => m_Prefab == other.m_Prefab && m_Type == other.m_Type && m_Side == other.m_Side;
    }
    public sealed class SearchSystem
    {
        public Colossal.Collections.NativeQuadTree<Entity, Game.Common.QuadTreeBoundsXZ> GetNetSearchTree(bool readOnly, out Unity.Jobs.JobHandle deps)
        {
            deps = default;
            return default;
        }
        public void AddNetSearchTreeReader(Unity.Jobs.JobHandle handle) { }
    }
    public enum PathMethod : byte { None = 0 }

    public static class NetUtils
    {
        public static bool ShouldInvert(Game.Prefabs.NetInvertMode invertMode, bool lefthandTraffic)
        {
            return (invertMode == Game.Prefabs.NetInvertMode.LefthandTraffic && lefthandTraffic) ||
                   (invertMode == Game.Prefabs.NetInvertMode.RighthandTraffic && !lefthandTraffic) ||
                   invertMode == Game.Prefabs.NetInvertMode.Always;
        }

        public static void FixInvertedUpgradeTrafficHandedness(ref Game.Prefabs.CompositionFlags flags)
        {
            uint left = (uint)flags.m_Left;
            uint right = (uint)flags.m_Right;
            const uint gateBit = (uint)Game.Prefabs.CompositionFlags.Side.Gate;
            uint newLeft = (left & ~gateBit) | (right & gateBit);
            uint newRight = (right & ~gateBit) | (left & gateBit);
            flags.m_Left = (Game.Prefabs.CompositionFlags.Side)newLeft;
            flags.m_Right = (Game.Prefabs.CompositionFlags.Side)newRight;
        }

        public static void FlipUpgradeTrafficHandedness(ref Game.Prefabs.CompositionFlags flags)
        {
            uint left = (uint)flags.m_Left;
            uint right = (uint)flags.m_Right;
            left = SwapTwoBits(left, (uint)Game.Prefabs.CompositionFlags.Side.ForbidLeftTurn, (uint)Game.Prefabs.CompositionFlags.Side.ForbidRightTurn);
            right = SwapTwoBits(right, (uint)Game.Prefabs.CompositionFlags.Side.ForbidLeftTurn, (uint)Game.Prefabs.CompositionFlags.Side.ForbidRightTurn);
            flags.m_Left = (Game.Prefabs.CompositionFlags.Side)right;
            flags.m_Right = (Game.Prefabs.CompositionFlags.Side)left;
        }

        private static uint SwapTwoBits(uint mask, uint a, uint b)
        {
            bool hasA = (mask & a) != 0;
            bool hasB = (mask & b) != 0;
            mask &= ~(a | b);
            if (hasA) mask |= b;
            if (hasB) mask |= a;
            return mask;
        }

        public static quaternion GetNodeRotation(float3 tangent) => GetNodeRotation(tangent, quaternion.identity);

        public static quaternion GetNodeRotation(float3 tangent, quaternion defaultRotation)
        {
            tangent.y = 0f;
            if (Colossal.Mathematics.MathUtils.TryNormalize(ref tangent))
            {
                return quaternion.LookRotation(tangent, math.up());
            }
            return defaultRotation;
        }

        public static Curve AdjustPosition(Curve curve, bool fixedStart, bool linearMiddle, bool fixedEnd, ref Game.Simulation.TerrainHeightData terrainHeightData)
        {
            if (!fixedStart) curve.m_Bezier.a.y = terrainHeightData.Sample(curve.m_Bezier.a);
            if (!fixedEnd) curve.m_Bezier.d.y = terrainHeightData.Sample(curve.m_Bezier.d);
            if (linearMiddle)
            {
                curve.m_Bezier.b.y = math.lerp(curve.m_Bezier.a.y, curve.m_Bezier.d.y, 1f / 3f);
                curve.m_Bezier.c.y = math.lerp(curve.m_Bezier.a.y, curve.m_Bezier.d.y, 2f / 3f);
            }
            else
            {
                curve.m_Bezier.b.y = terrainHeightData.Sample(curve.m_Bezier.b);
                curve.m_Bezier.c.y = terrainHeightData.Sample(curve.m_Bezier.c);
            }
            return curve;
        }

        public static Curve AdjustPosition(
            Curve curve,
            bool fixedStart,
            bool linearMiddle,
            bool fixedEnd,
            ref Game.Simulation.TerrainHeightData terrainHeightData,
            ref Game.Simulation.WaterSurfaceData<Game.Simulation.SurfaceWater> waterSurfaceData)
        {
            if (!fixedStart) curve.m_Bezier.a.y = Game.Simulation.WaterUtils.SampleHeight(ref waterSurfaceData, ref terrainHeightData, curve.m_Bezier.a);
            if (!fixedEnd) curve.m_Bezier.d.y = Game.Simulation.WaterUtils.SampleHeight(ref waterSurfaceData, ref terrainHeightData, curve.m_Bezier.d);
            if (linearMiddle)
            {
                curve.m_Bezier.b.y = math.lerp(curve.m_Bezier.a.y, curve.m_Bezier.d.y, 1f / 3f);
                curve.m_Bezier.c.y = math.lerp(curve.m_Bezier.a.y, curve.m_Bezier.d.y, 2f / 3f);
            }
            else
            {
                curve.m_Bezier.b.y = Game.Simulation.WaterUtils.SampleHeight(ref waterSurfaceData, ref terrainHeightData, curve.m_Bezier.b);
                curve.m_Bezier.c.y = Game.Simulation.WaterUtils.SampleHeight(ref waterSurfaceData, ref terrainHeightData, curve.m_Bezier.c);
            }
            return curve;
        }
    }
}

namespace Game.Prefabs
{
    public enum NetInvertMode : int
    {
        Never = 0,
        LefthandTraffic = 1,
        RighthandTraffic = 2,
        Always = 3,
    }

    public struct PrefabRef : IComponentData { public Entity m_Prefab; }
    public struct NetData : IComponentData { }
    public struct NetGeometryData : IComponentData { public Game.Net.GeometryFlags m_Flags; }
    public struct ObjectData : IComponentData { public EntityArchetype m_Archetype; }
    public struct AreaData : IComponentData { public EntityArchetype m_Archetype; }

    // Real CompositionFlags is a struct of flag groups, including nested General and Side flags.
    public struct CompositionFlags : IEquatable<CompositionFlags>
    {
        [Flags]
        public enum General : uint
        {
            Node = 1,
            Edge = 2,
            Invert = 4,
            Flip = 8,
            DeadEnd = 16,
            Intersection = 32,
            Roundabout = 64,
            LevelCrossing = 128,
            Crosswalk = 256,
            MedianBreak = 512,
            TrafficLights = 1024,
            Elevated = 65536,
            Tunnel = 131072,
        }

        [Flags]
        public enum Side : uint
        {
            None = 0,
            Raised = 1,
            Lowered = 2,
            LowTransition = 4,
            HighTransition = 8,
            Gate = 32768,
            Sidewalk = 65536,
            WideSidewalk = 131072,
            ParkingSpaces = 262144,
            ForbidLeftTurn = 16777216,
            ForbidRightTurn = 33554432,
        }

        public General m_General;
        public Side m_Left;
        public Side m_Right;

        public CompositionFlags(General general, Side left, Side right)
        {
            m_General = general;
            m_Left = left;
            m_Right = right;
        }

        public bool Equals(CompositionFlags other) =>
            m_General == other.m_General && m_Left == other.m_Left && m_Right == other.m_Right;

        public override bool Equals(object obj) => obj is CompositionFlags other && Equals(other);
        public override int GetHashCode() => unchecked(((int)m_General * 397) ^ ((int)m_Left * 17) ^ (int)m_Right);
        public static bool operator ==(CompositionFlags lhs, CompositionFlags rhs) => lhs.Equals(rhs);
        public static bool operator !=(CompositionFlags lhs, CompositionFlags rhs) => !lhs.Equals(rhs);
    }

    public struct NetCompositionData : IComponentData
    {
        public CompositionFlags m_Flags;
    }

    public struct SubNet : IBufferElementData
    {
        public Entity m_Prefab;
        public Colossal.Mathematics.Bezier4x3 m_Curve;
        public int2 m_NodeIndex;
        public int2 m_ParentMesh;
        public NetInvertMode m_InvertMode;
        public CompositionFlags m_Upgrades;
    }

    public enum TerraformingTarget : int { None = 0, Height = 1 }
    public struct TerraformingData : IComponentData { public TerraformingTarget m_Target; }
}

namespace Game.Objects
{
    public struct Transform : IComponentData { public float3 m_Position; public quaternion m_Rotation; }
    public struct Elevation : IComponentData { public float m_Elevation; public ElevationFlags m_Flags; }
    public struct Object : IComponentData { }
    public struct Tree : IComponentData { public TreeState m_State; public byte m_Growth; }
    public struct Plant : IComponentData { public float m_Pollution; }
    public struct Pillar : IComponentData { }
    public struct Surface : IComponentData { }
    public struct UtilityObject : IComponentData { }
    public struct NetObject : IComponentData { }
    public struct AssetStamp : IComponentData { }
    public struct Decoration : IComponentData { }
    public struct Moving : IComponentData { public float3 m_Velocity; public float3 m_AngularVelocity; }
    public struct Attached : IComponentData { public Entity m_Parent; public Entity m_OldParent; public float m_CurvePosition; }
    public struct SubObject : IBufferElementData { public Entity m_SubObject; }
    public struct Signature : IComponentData { }
    public struct UniqueObject : IComponentData { }
    public struct OutsideConnection : IComponentData { }
    public struct ElectricityOutsideConnection : IComponentData { }
    public struct WaterPipeOutsideConnection : IComponentData { }
    public sealed class SearchSystem
    {
        public Colossal.Collections.NativeQuadTree<Entity, Game.Common.QuadTreeBoundsXZ> GetStaticSearchTree(bool readOnly, out Unity.Jobs.JobHandle deps)
        {
            deps = default;
            return default;
        }
        public void AddStaticSearchTreeReader(Unity.Jobs.JobHandle handle) { }
    }
    public struct UnderConstruction : IComponentData { public Entity m_NewPrefab; public byte m_Progress; public byte m_Speed; }
    [Flags] public enum ElevationFlags : byte { None = 0, Stacked = 1, OnGround = 2, Lowered = 4, OnAttachedParent = 8 }
    public enum TreeState : byte { None = 0, Teen = 1, Adult = 2, Elderly = 4, Dead = 8, Stump = 16, Collected = 32 }

    public static class ObjectUtils
    {
        public static float3 LocalToWorld(Transform transform, float3 position) =>
            transform.m_Position + math.mul(transform.m_Rotation, position);

        public static float3 LocalToWorld(float3 transformPosition, quaternion transformRotation, float3 position) =>
            transformPosition + math.mul(transformRotation, position);

        public static Colossal.Mathematics.Bezier4x3 LocalToWorld(float3 transformPosition, quaternion transformRotation, Colossal.Mathematics.Bezier4x3 curve)
        {
            return new Colossal.Mathematics.Bezier4x3
            {
                a = LocalToWorld(transformPosition, transformRotation, curve.a),
                b = LocalToWorld(transformPosition, transformRotation, curve.b),
                c = LocalToWorld(transformPosition, transformRotation, curve.c),
                d = LocalToWorld(transformPosition, transformRotation, curve.d),
            };
        }
    }
}

namespace Game.Simulation
{
    public struct TerrainHeightData
    {
        public bool isCreated;
        public int3 resolution;
        public float3 offset;
        public float3 scale;
        public Unity.Collections.NativeArray<ushort> heights;
        public Func<float3, float> HeightSampler;
        internal float Sample(float3 pos) => HeightSampler != null ? HeightSampler(pos) : pos.y;
    }

    public struct SurfaceWater
    {
        public float m_Depth;
        public float m_Polluted;
        public float2 m_Velocity;
    }

    public struct WaterSurfaceData<T> where T : struct
    {
        public bool isCreated;
        public Func<float3, float> WaterHeightSampler;
        internal float Sample(float3 pos, float fallback) => WaterHeightSampler != null ? WaterHeightSampler(pos) : fallback;
    }

    public sealed class TerrainSystem
    {
        public TerrainHeightData Data;
        public UnityEngine.Texture heightmap;
        public bool freezeCascadeUpdates;
        public int GetHeightDataCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public TerrainHeightData GetHeightData(bool waitForPending = false)
        {
            GetHeightDataCalls++;
            return Data;
        }

        public void AddCPUHeightReader(Unity.Jobs.JobHandle handle)
        {
            ReleaseCalls++;
        }

        public void OnAreaChanged(in Colossal.Mathematics.Bounds2 area) { }
        public void TriggerAsyncChange() { }
    }

    public sealed class WaterSystem
    {
        public WaterSurfaceData<SurfaceWater> Data;
        public int GetSurfaceDataCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public WaterSurfaceData<SurfaceWater> GetSurfaceData(out Unity.Jobs.JobHandle deps)
        {
            GetSurfaceDataCalls++;
            deps = default;
            return Data;
        }

        public void AddSurfaceReader(Unity.Jobs.JobHandle handle)
        {
            ReleaseCalls++;
        }

        public void TerrainWillChangeFromBrush(Colossal.Mathematics.Bounds2 bounds) { }
    }

    public static class WaterUtils
    {
        public static float SampleHeight(ref WaterSurfaceData<SurfaceWater> data, ref TerrainHeightData terrainData, float3 worldPosition)
        {
            float t = terrainData.Sample(worldPosition);
            return data.isCreated ? math.max(t, data.Sample(worldPosition, t)) : t;
        }
    }
}

namespace Game.Areas
{
    public struct Node : IBufferElementData { public float3 m_Position; public float m_Elevation; }
    public struct SubArea : IBufferElementData { public Entity m_Area; }
    public struct Area : IComponentData { }
    public struct District : IComponentData { public uint m_OptionMask; }
    public struct MapTile : IComponentData { }
    public struct Surface : IComponentData { }
}

namespace Game.Buildings
{
    public struct Building : IComponentData { public Entity m_RoadEdge; public float m_CurvePosition; public uint m_OptionMask; }
    public struct InstalledUpgrade : IBufferElementData { public Entity m_Upgrade; public uint m_OptionMask; }
    public struct ServiceUpgrade : IComponentData { }
    public struct Signature : IComponentData { }
}

namespace Game.Creatures { public struct Creature : IComponentData { } }

namespace Game.Vehicles
{
    public struct Vehicle : IComponentData { }
    public struct Controller : IComponentData { public Entity m_Controller; }
    public struct LayoutElement : IBufferElementData { public Entity m_Vehicle; }
    public struct OwnedVehicle : IBufferElementData { public Entity m_Vehicle; }
}

namespace Game.Routes
{
    public struct Route : IComponentData { }
    public struct SubRoute : IBufferElementData { public Entity m_Route; }
}

namespace Game.Zones
{
    public struct ZoneType { public ushort m_Index; }
    public struct Block : IComponentData { public int2 m_Size; }
    public struct Cell : IBufferElementData { public ZoneType m_Zone; public ushort m_State; public short m_Height; }
}

namespace Game.Tools
{
    [Flags] public enum CreationFlags : uint
    {
        None = 0, Permanent = 1, Select = 2, Delete = 4, Attach = 8, Upgrade = 16, Relocate = 32,
        Invert = 64, Align = 128, Hidden = 256, Parent = 512, Dragging = 1024, Recreate = 2048,
        Optional = 4096, Lowered = 8192, Native = 16384, Construction = 32768, SubElevation = 65536,
        Duplicate = 131072, Repair = 262144, Stamping = 524288,
    }

    [Flags] public enum CoursePosFlags : uint
    {
        None = 0, IsFirst = 1, IsLast = 2, HalfAlign = 4, IsParallel = 8, IsRight = 16, IsLeft = 32,
        IsFixed = 64, FreeHeight = 128, LeftTransition = 256, RightTransition = 512,
        ForceElevatedNode = 1024, ForceElevatedEdge = 2048, DisableMerge = 4096, IsGrid = 8192, DontCreate = 16384,
    }

    public struct CreationDefinition : IComponentData
    {
        public Entity m_Prefab;
        public Entity m_SubPrefab;
        public Entity m_Original;
        public Entity m_Owner;
        public Entity m_Attached;
        public CreationFlags m_Flags;
        public int m_RandomSeed;
    }

    public struct CoursePos
    {
        public Entity m_Entity;
        public float3 m_Position;
        public quaternion m_Rotation;
        public float2 m_Elevation;
        public float m_CourseDelta;
        public float m_SplitPosition;
        public CoursePosFlags m_Flags;
        public int m_ParentMesh;
    }

    public struct NetCourse : IComponentData
    {
        public CoursePos m_StartPosition;
        public CoursePos m_EndPosition;
        public Colossal.Mathematics.Bezier4x3 m_Curve;
        public float2 m_Elevation;
        public float m_Length;
        public int m_FixedIndex;
    }

    public struct ObjectDefinition : IComponentData
    {
        public float3 m_Position;
        public float3 m_LocalPosition;
        public float3 m_Scale;
        public quaternion m_Rotation;
        public quaternion m_LocalRotation;
        public float m_Elevation;
        public float m_Intensity;
        public float m_Age;
        public bool m_IsDecoration;
        public int m_ParentMesh;
        public int m_GroupIndex;
        public int m_Probability;
        public int m_PrefabSubIndex;
    }

    [Flags]
    public enum TempFlags : uint
    {
        None = 0,
        Create = 1,
        Delete = 2,
        IsLast = 4,
        Essential = 8,
        Dragging = 16,
        Select = 32,
        Modify = 64,
        Regenerate = 128,
        Replace = 256,
        Upgrade = 512,
        Hidden = 1024,
        Parent = 2048,
        Combine = 4096,
        RemoveCost = 8192,
        Cancel = 16384,
        SubDetail = 32768,
        Duplicate = 65536,
        ModifyEdge = 131072,
    }

    public enum ApplyMode : int { None = 0, Apply = 1, Clear = 2 }

    public struct Temp : IComponentData
    {
        public Entity m_Original;
        public float m_CurvePosition;
        public int m_Value;
        public int m_Cost;
        public TempFlags m_Flags;
    }

    public struct Brush : IComponentData
    {
        public Entity m_Tool;
        public float3 m_Position;
        public float m_Size;
    }

    public static class ToolUtils
    {
        public static Colossal.Mathematics.Bounds2 GetBounds(Brush brush)
        {
            float r = brush.m_Size * 0.5f;
            return new Colossal.Mathematics.Bounds2(
                new float2(brush.m_Position.x - r, brush.m_Position.z - r),
                new float2(brush.m_Position.x + r, brush.m_Position.z + r));
        }
    }

    public class ToolBaseSystem
    {
        public string toolID = "Net Tool";
        public bool brushing;
        public ApplyMode applyMode;
    }

    public sealed class ToolSystem
    {
        public ToolBaseSystem activeTool;
        public Game.GameMode actionMode = Game.GameMode.Game;
        public ApplyMode applyMode;
    }

    public sealed class ApplyObjectsSystem { }
    public sealed class ApplyNetSystem { }
    public sealed class ApplyAreasSystem { }
    public sealed class ApplyZonesSystem { }
    public sealed class ApplyBrushesSystem { }

    public struct OwnerDefinition : IComponentData, IEquatable<OwnerDefinition>
    {
        public Entity m_Prefab;
        public float3 m_Position;
        public quaternion m_Rotation;

        public bool Equals(OwnerDefinition other) =>
            m_Prefab.Equals(other.m_Prefab) && m_Position.Equals(other.m_Position) && m_Rotation.Equals(other.m_Rotation);
    }

    public struct LocalTransformCache : IComponentData
    {
        public float3 m_Position;
        public quaternion m_Rotation;
        public int m_ParentMesh;
        public int m_GroupIndex;
        public int m_Probability;
        public int m_PrefabSubIndex;
    }

    public sealed class ToolOutputBarrier
    {
        private EntityCommandBuffer m_Buffer;
        public EntityCommandBuffer CurrentBuffer => m_Buffer;
        public void ClearBuffer() => m_Buffer = null;
        public EntityCommandBuffer CreateCommandBuffer()
        {
            if (m_Buffer == null) m_Buffer = new EntityCommandBuffer();
            return m_Buffer;
        }
    }
}
