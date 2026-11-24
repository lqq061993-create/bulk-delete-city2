// Game component structs, transcribed from Game.dll by reflection (field types and names, enum
// membership) so the offline restore path reads the same shapes the game does. Only members the
// restore code touches are present; unrelated fields are omitted.
using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Entities
{
    public struct EntityArchetype { public IntPtr m_Archetype; public bool Valid => m_Archetype != IntPtr.Zero; }
    public struct JobHandle { public void Complete() { } }
}

namespace Game.Common
{
    public struct Deleted : IComponentData { }
    public struct Created : IComponentData { }
    public struct Applied : IComponentData { }
    public struct Updated : IComponentData { }
    public struct Owner : IComponentData { public Entity m_Owner; }
    public struct PseudoRandomSeed : IComponentData { public ushort m_Seed; }
}

namespace Game.Net
{
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
    public enum PathMethod : byte { None = 0 }
}

namespace Game.Prefabs
{
    public struct PrefabRef : IComponentData { public Entity m_Prefab; }
    public struct NetData : IComponentData { }
    public struct ObjectData : IComponentData { public EntityArchetype m_Archetype; }
    public struct AreaData : IComponentData { public EntityArchetype m_Archetype; }

    // Real CompositionFlags is a struct of flag groups, including nested General flags.
    public struct CompositionFlags
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

        public General m_General;
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
        public CompositionFlags m_Upgrades;
    }
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
    public struct UnderConstruction : IComponentData { public Entity m_NewPrefab; public byte m_Progress; public byte m_Speed; }
    [Flags] public enum ElevationFlags : byte { None = 0, Stacked = 1, OnGround = 2, Lowered = 4, OnAttachedParent = 8 }
    public enum TreeState : byte { None = 0, Teen = 1, Adult = 2, Elderly = 4, Dead = 8, Stump = 16, Collected = 32 }
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

    public struct Temp : IComponentData { public Entity m_Original; public float m_CurvePosition; public int m_Value; public int m_Cost; }

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
        public EntityCommandBuffer CreateCommandBuffer()
        {
            if (m_Buffer == null) m_Buffer = new EntityCommandBuffer();
            return m_Buffer;
        }
    }
}
