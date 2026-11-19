// Offline stand-ins for the Unity ECS surface and the game component structs that
// DeletionUndo.cs touches, plus stubs for the tool-system statics it calls.
//
// Real Unity.Entities cannot run here: Unity.Collections allocators go through engine
// ECalls, so World.Create() throws SecurityException outside the player. Unity.Mathematics
// and Colossal.Mathematics are pure managed, so those two are referenced for real.
// Field names below were taken from the code that reads them, not from Game.dll.
using System;
using System.Collections.Generic;

namespace Unity.Collections
{
    public enum Allocator { Invalid = 0, None = 1, Temp = 2, TempJob = 3, Persistent = 4 }
}

namespace Unity.Entities
{
    public struct Entity : IEquatable<Entity>
    {
        public int Index;
        public int Version;
        public static Entity Null => default;
        public bool Equals(Entity other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object obj) => obj is Entity e && Equals(e);
        public override int GetHashCode() => (Index * 397) ^ Version;
        public static bool operator ==(Entity a, Entity b) => a.Equals(b);
        public static bool operator !=(Entity a, Entity b) => !a.Equals(b);
    }

    public interface IComponentData { }
    public interface IBufferElementData { }
    public interface ISharedComponentData { }

    public struct ComponentType
    {
        public Type Type;
        public static ComponentType ReadOnly<T>() => new ComponentType { Type = typeof(T) };
        public static ComponentType ReadWrite<T>() => new ComponentType { Type = typeof(T) };
    }

    public sealed class DynamicBuffer<T> : List<T> where T : struct, IBufferElementData
    {
        public int Length => Count;
    }

    public sealed class EntityArray : IDisposable
    {
        private readonly List<Entity> m_Items;
        public EntityArray(List<Entity> items) { m_Items = items; }
        public int Length => m_Items.Count;
        public Entity this[int i] => m_Items[i];
        public List<Entity>.Enumerator GetEnumerator() => m_Items.GetEnumerator();
        public void Dispose() { }
    }

    public sealed class EntityQuery : IDisposable
    {
        private readonly EntityManager m_Manager;
        private readonly Type[] m_Required;
        public EntityQuery(EntityManager manager, Type[] required) { m_Manager = manager; m_Required = required; }
        public EntityArray ToEntityArray(Unity.Collections.Allocator allocator) => new EntityArray(m_Manager.Matching(m_Required));
        public void Dispose() { }
    }

    public sealed class EntityManager
    {
        private readonly Dictionary<Entity, Dictionary<Type, object>> m_Data = new Dictionary<Entity, Dictionary<Type, object>>();
        private readonly List<Entity> m_Order = new List<Entity>();
        private int m_Next = 100000;

        public Entity CreateEntity()
        {
            var e = new Entity { Index = m_Next++, Version = 1 };
            m_Data[e] = new Dictionary<Type, object>();
            m_Order.Add(e);
            return e;
        }

        /// <summary>Creates an entity with a fixed index so tests can talk about it by name.</summary>
        public Entity Make(int index)
        {
            var e = new Entity { Index = index, Version = 1 };
            m_Data[e] = new Dictionary<Type, object>();
            m_Order.Add(e);
            if (index >= m_Next) m_Next = index + 1;
            return e;
        }

        /// <summary>
        /// Creates an entity with a fixed index without advancing the allocator. Used for command-buffer
        /// entities: their indices come from the command buffer's own range (900000+), and letting them
        /// advance <see cref="m_Next"/> would make the world hand out indices that collide with the next
        /// command, so a later command would silently write onto an entity the replay already created.
        /// </summary>
        public Entity RawCreate(int index)
        {
            var e = new Entity { Index = index, Version = 1 };
            m_Data[e] = new Dictionary<Type, object>();
            m_Order.Add(e);
            return e;
        }

        public bool Exists(Entity e) => e != Entity.Null && m_Data.ContainsKey(e);
        public bool HasComponent<T>(Entity e) where T : struct, IComponentData =>
            Exists(e) && m_Data[e].ContainsKey(typeof(T));
        public T GetComponentData<T>(Entity e) where T : struct, IComponentData => (T)m_Data[e][typeof(T)];
        public void Set<T>(Entity e, T value) where T : struct, IComponentData => m_Data[e][typeof(T)] = value;
        public void AddComponentData<T>(Entity e, T value) where T : struct, IComponentData => m_Data[e][typeof(T)] = value;
        public void RemoveComponent<T>(Entity e) where T : struct, IComponentData => m_Data[e].Remove(typeof(T));
        public bool HasBuffer<T>(Entity e) where T : struct, IBufferElementData =>
            Exists(e) && m_Data[e].ContainsKey(typeof(DynamicBuffer<T>));
        public DynamicBuffer<T> GetBuffer<T>(Entity e, bool isReadOnly = false) where T : struct, IBufferElementData =>
            (DynamicBuffer<T>)m_Data[e][typeof(DynamicBuffer<T>)];
        public DynamicBuffer<T> AddBuffer<T>(Entity e) where T : struct, IBufferElementData
        {
            if (!m_Data[e].TryGetValue(typeof(DynamicBuffer<T>), out object existing)) return SetBuffer(e, new DynamicBuffer<T>());
            return (DynamicBuffer<T>)existing;
        }
        public DynamicBuffer<T> SetBuffer<T>(Entity e, DynamicBuffer<T> buffer) where T : struct, IBufferElementData
        {
            m_Data[e][typeof(DynamicBuffer<T>)] = buffer;
            return buffer;
        }
        public void Buffer<T>(Entity e, params T[] values) where T : struct, IBufferElementData
        {
            var buffer = AddBuffer<T>(e);
            buffer.Clear();
            buffer.AddRange(values);
        }
        public EntityQuery CreateEntityQuery(params ComponentType[] types)
        {
            var required = new Type[types.Length];
            for (int i = 0; i < types.Length; i++) required[i] = types[i].Type;
            return new EntityQuery(this, required);
        }
        internal List<Entity> Matching(Type[] required)
        {
            var hits = new List<Entity>();
            foreach (Entity e in m_Order)
            {
                if (!m_Data.TryGetValue(e, out Dictionary<Type, object> components)) continue;
                bool ok = true;
                foreach (Type t in required)
                {
                    if (!components.ContainsKey(t)) { ok = false; break; }
                }
                if (ok) hits.Add(e);
            }
            return hits;
        }
        public Dictionary<Type, object> Components(Entity e) => m_Data[e];
        public List<Entity> All => m_Order;

        /// <summary>
        /// Removes the entity itself. References held in other entities' buffers are left alone on
        /// purpose: the game drops them, and the harness drops them explicitly where that matters, so
        /// a test can tell "the junction still lists a dead edge" from "the junction was cleaned up".
        /// </summary>
        public void DestroyEntity(Entity e)
        {
            if (!m_Data.TryGetValue(e, out Dictionary<Type, object> components)) return;
            m_Data.Remove(e);
            m_Order.Remove(e);
        }
    }

    public sealed class EntityCommandBuffer
    {
        public enum Kind { Create, AddComponent, AddBuffer, RemoveComponent }

        public sealed class Command
        {
            public Kind Kind;
            public Entity Entity;
            public Type Type;
            public object Value;
        }

        private int m_NextCreated = 900000;
        public readonly List<Command> Commands = new List<Command>();

        public Entity CreateEntity()
        {
            var e = new Entity { Index = m_NextCreated++, Version = 1 };
            Commands.Add(new Command { Kind = Kind.Create, Entity = e });
            return e;
        }

        public void AddComponent<T>(Entity e) where T : struct, IComponentData =>
            Commands.Add(new Command { Kind = Kind.AddComponent, Entity = e, Type = typeof(T), Value = default(T) });

        public void AddComponent<T>(Entity e, T value) where T : struct, IComponentData =>
            Commands.Add(new Command { Kind = Kind.AddComponent, Entity = e, Type = typeof(T), Value = value });

        // The game returns the deferred buffer so callers can fill it; replay uses the same
        // instance, which is why the command holds a reference rather than a copy.
        public DynamicBuffer<T> AddBuffer<T>(Entity e) where T : struct, IBufferElementData
        {
            var buffer = new DynamicBuffer<T>();
            Commands.Add(new Command { Kind = Kind.AddBuffer, Entity = e, Type = typeof(T), Value = buffer });
            return buffer;
        }

        public void RemoveComponent<T>(Entity e) where T : struct, IComponentData =>
            Commands.Add(new Command { Kind = Kind.RemoveComponent, Entity = e, Type = typeof(T) });
    }
}

namespace UnityEngine
{
    public static class Time
    {
        public static int frameCount = 1;
        public static float unscaledDeltaTime = 0.016f;
    }
}

namespace Game
{
    public abstract class GameSystemBase
    {
        public Unity.Entities.World World { get; } = new Unity.Entities.World();
        protected Unity.Entities.JobHandle Dependency { get; set; }
        protected virtual void OnCreate() { }
        protected virtual void OnUpdate() { }
        protected virtual void OnDestroy() { }
    }
}

namespace Unity.Entities
{
    public sealed class World
    {
        public T GetOrCreateSystemManaged<T>() where T : new() => new T();
        public T GetExistingSystemManaged<T>() where T : class => null;
    }
}

namespace MarqueeDelete
{
    internal sealed class Settings { public int MaxUndoHistorySteps = 20; }

    internal static class Mod { public static Settings Settings; }

    internal sealed class MarqueeDeleteToolSystem
    {
        // The harness bypasses the compatibility gate on purpose: it exercises batching and the
        // junction verdict cache, not the tier gate, which DeletionRegression covers separately.
        public static bool CanRebuildNow => true;
        public static CompatibilityTier TierFor(CompatibilityGroup group) => CompatibilityTier.Verified;
        public static void AppendStatusLine(string line) { StatusLines.Add(line); }
        public static readonly System.Collections.Generic.List<string> StatusLines = new System.Collections.Generic.List<string>();
        public void TickUndo(Game.Tools.ToolOutputBarrier barrier) { }
    }
}
