using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Creatures;
using Game.Economy;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Game.Zones;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace MarqueeDelete
{
    public struct SelectedEntityItem
    {
        public Entity Entity;
        public Entity Prefab;
        public SelectionCategory Category;
        public BuildingZoneType ZoneType;
        public ImportantBuildingKind ImportantKind;
        public NetworkSubKind NetworkKind;
        public float2 FootprintXZ;
    }

    internal struct PrefabBuildingMeta
    {
        public BuildingZoneType ZoneType;
        public ImportantBuildingKind ImportantKind;
        public float2 FootprintXZ;
    }

    public sealed class SelectionResult
    {
        public readonly List<Entity> Entities = new List<Entity>();
        public readonly List<SelectedEntityItem> Items = new List<SelectedEntityItem>();
        internal readonly Dictionary<Entity, ImportantBuildingKind> ImportantEntities = new Dictionary<Entity, ImportantBuildingKind>();
        internal readonly HashSet<Entity> ManuallyExcluded = new HashSet<Entity>();
        internal readonly HashSet<Entity> ManuallyAdded = new HashSet<Entity>();
        internal HashSet<Entity> DeferredNodes;
        internal int RawHits;
        public SelectionBreakdown Breakdown;
        internal DeletionPlan Plan;
        internal RoadFacilityMode FacilityMode;
        internal List<RoadFacilityTarget> FacilityTargets;
        internal bool AffectsProtectedObject;
        internal SafetyOptions Safety;
        public int CascadeTotalCount => Plan?.Affected.Count ?? 0;
        public MarqueeShapeXZ Shape;

        public int Count => FacilityTargets == null ? Entities.Count : Breakdown.Networks;
        public int EffectiveCount => System.Math.Max(Count, CascadeTotalCount);

        internal bool HasRetainedEdits =>
            MarqueePointerLogic.ShouldRetainFilteredSelection(Count, Shape.IsUsable(), ManuallyAdded.Count);
    }

    public static class SelectionCollector
    {
        private static readonly Dictionary<Entity, PrefabBuildingMeta> s_PrefabCache = new Dictionary<Entity, PrefabBuildingMeta>(512);
        private static readonly Dictionary<Entity, NetworkSubKind> s_NetworkPrefabCache = new Dictionary<Entity, NetworkSubKind>(256);

        public static void ClearPrefabCache()
        {
            s_PrefabCache.Clear();
            s_NetworkPrefabCache.Clear();
        }

        public static NetworkSubKind ResolveNetworkSubKind(EntityManager em, Entity entity)
        {
            if (entity == Entity.Null || !em.Exists(entity) || !em.HasComponent<PrefabRef>(entity))
            {
                return NetworkSubKind.Other;
            }

            Entity prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null)
            {
                return NetworkSubKind.Other;
            }

            if (!em.Exists(prefab))
            {
                s_NetworkPrefabCache.Remove(prefab);
                return NetworkSubKind.Other;
            }

            if (s_NetworkPrefabCache.Count > 16384)
            {
                s_NetworkPrefabCache.Clear();
            }

            if (s_NetworkPrefabCache.TryGetValue(prefab, out NetworkSubKind cached))
            {
                return cached;
            }

            // Classify strictly via prefab data components (never runtime WaterPipeConnection / ElectricityConnection,
            // because regular roads also carry utility connections).
            bool hasRoadOrPathway = em.HasComponent<RoadData>(prefab) || em.HasComponent<PathwayData>(prefab);
            bool hasTrack = em.HasComponent<TrackData>(prefab);
            bool hasPipeline = em.HasComponent<PipelineData>(prefab);
            bool hasPowerLine = em.HasComponent<PowerLineData>(prefab);

            NetworkSubKind kind = SelectionClassifier.ClassifyNetworkByPrefabFlags(
                hasRoadOrPathway,
                hasTrack,
                hasPipeline,
                hasPowerLine);
            s_NetworkPrefabCache[prefab] = kind;
            return kind;
        }

        public static void ResolveBuildingMeta(
            EntityManager em,
            Entity entity,
            out BuildingZoneType zoneType,
            out ImportantBuildingKind importantKind,
            out float2 footprintXZ)
        {
            zoneType = BuildingZoneType.None;
            importantKind = ImportantBuildingKind.None;
            footprintXZ = new float2(8f, 8f);

            if (entity == Entity.Null || !em.Exists(entity))
            {
                return;
            }

            Entity prefab = Entity.Null;
            if (em.HasComponent<PrefabRef>(entity))
            {
                prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
            }

            if (prefab != Entity.Null)
            {
                if (!em.Exists(prefab))
                {
                    s_PrefabCache.Remove(prefab);
                }
                else
                {
                    if (s_PrefabCache.Count > 16384)
                    {
                        s_PrefabCache.Clear();
                    }

                    if (!s_PrefabCache.TryGetValue(prefab, out PrefabBuildingMeta meta))
                    {
                        float2 footprint;
                        if (em.HasComponent<BuildingData>(prefab))
                        {
                            int2 lot = em.GetComponentData<BuildingData>(prefab).m_LotSize;
                            footprint = new float2(math.max(1, lot.x) * 8f, math.max(1, lot.y) * 8f);
                        }
                        else if (em.HasComponent<ObjectGeometryData>(prefab))
                        {
                            float3 sz = em.GetComponentData<ObjectGeometryData>(prefab).m_Size;
                            footprint = new float2(math.max(2f, sz.x), math.max(2f, sz.z));
                        }
                        else
                        {
                            footprint = new float2(8f, 8f);
                        }

                        ImportantBuildingKind important = ImportantBuildingKind.None;
                        if (em.HasComponent<SignatureBuildingData>(prefab))
                        {
                            important |= ImportantBuildingKind.Signature;
                        }
                        if (em.HasComponent<UniqueObjectData>(prefab))
                        {
                            important |= ImportantBuildingKind.Unique;
                        }
                        if (em.HasComponent<ServiceObjectData>(prefab))
                        {
                            important |= ImportantBuildingKind.Service;
                        }

                        BuildingZoneType zone = BuildingZoneType.None;
                        if (em.HasComponent<SpawnableBuildingData>(prefab))
                        {
                            Entity zonePrefab = em.GetComponentData<SpawnableBuildingData>(prefab).m_ZonePrefab;
                            if (zonePrefab != Entity.Null && em.Exists(zonePrefab) && em.HasComponent<ZoneData>(zonePrefab))
                            {
                                ZoneData zd = em.GetComponentData<ZoneData>(zonePrefab);
                                if ((zd.m_ZoneFlags & ZoneFlags.Office) != 0)
                                {
                                    zone = BuildingZoneType.Office;
                                }
                                else
                                {
                                    switch (zd.m_AreaType)
                                    {
                                        case Game.Zones.AreaType.Residential:
                                            zone = BuildingZoneType.Residential;
                                            break;
                                        case Game.Zones.AreaType.Commercial:
                                            zone = BuildingZoneType.Commercial;
                                            break;
                                        case Game.Zones.AreaType.Industrial:
                                            zone = BuildingZoneType.Industrial;
                                            break;
                                    }
                                }
                            }
                        }

                        if (zone == BuildingZoneType.None && em.HasComponent<BuildingPropertyData>(prefab))
                        {
                            BuildingPropertyData bpd = em.GetComponentData<BuildingPropertyData>(prefab);
                            if (bpd.m_ResidentialProperties > 0)
                            {
                                zone = BuildingZoneType.Residential;
                            }
                            else if (bpd.m_AllowedSold != Resource.NoResource)
                            {
                                zone = BuildingZoneType.Commercial;
                            }
                            else if (bpd.m_AllowedManufactured != Resource.NoResource || bpd.m_AllowedStored != Resource.NoResource)
                            {
                                zone = BuildingZoneType.Industrial;
                            }
                        }

                        if (zone == BuildingZoneType.None && important != ImportantBuildingKind.None)
                        {
                            zone = BuildingZoneType.Other;
                        }

                        meta = new PrefabBuildingMeta
                        {
                            ZoneType = zone,
                            ImportantKind = important,
                            FootprintXZ = footprint,
                        };
                        s_PrefabCache[prefab] = meta;
                    }

                    zoneType = meta.ZoneType;
                    importantKind = meta.ImportantKind;
                    footprintXZ = meta.FootprintXZ;
                }
            }

            if (zoneType == BuildingZoneType.None)
            {
                if (em.HasComponent<OfficeProperty>(entity))
                {
                    zoneType = BuildingZoneType.Office;
                }
                else if (em.HasComponent<IndustrialProperty>(entity) ||
                         em.HasComponent<ExtractorProperty>(entity) ||
                         em.HasComponent<StorageProperty>(entity))
                {
                    zoneType = BuildingZoneType.Industrial;
                }
                else if (em.HasComponent<ResidentialProperty>(entity))
                {
                    zoneType = BuildingZoneType.Residential;
                }
                else if (em.HasComponent<CommercialProperty>(entity))
                {
                    zoneType = BuildingZoneType.Commercial;
                }
                else
                {
                    zoneType = BuildingZoneType.Other;
                }
            }

            if (em.HasComponent<Signature>(entity))
            {
                importantKind |= ImportantBuildingKind.Signature;
            }
            if (em.HasComponent<Game.Objects.UniqueObject>(entity))
            {
                importantKind |= ImportantBuildingKind.Unique;
            }
            if (em.HasComponent<CityServiceUpkeep>(entity) || em.HasComponent<Game.Buildings.ServiceUpgrade>(entity))
            {
                importantKind |= ImportantBuildingKind.Service;
            }
        }

        public static SelectionResult Collect(
            World world,
            EntityManager em,
            MarqueeShapeXZ shape,
            SelectionFilterMask mask,
            SafetyOptions safety,
            bool undergroundView = false,
            bool includeCascade = true,
            IReadOnlyCollection<Entity> excluded = null)
        {
            var result = new SelectionResult { Shape = shape, Safety = safety };
            if (excluded != null)
            {
                foreach (Entity e in excluded)
                {
                    if (e != Entity.Null)
                    {
                        result.ManuallyExcluded.Add(e);
                    }
                }
            }

            if (world == null || !shape.IsUsable() || mask == SelectionFilterMask.None)
            {
                return result;
            }

            HashSet<Entity> seen = new HashSet<Entity>(result.ManuallyExcluded);

            bool needStaticTree = (mask & (SelectionFilterMask.Buildings |
                                           SelectionFilterMask.AllZones |
                                           SelectionFilterMask.Trees |
                                           SelectionFilterMask.Props |
                                           SelectionFilterMask.Decals)) != 0;

            bool needMovingTree = (mask & SelectionFilterMask.Moving) != 0;
            bool needNetTree = (mask & SelectionFilterMask.Networks) != 0;
            bool needAreaTree = !undergroundView && (mask & SelectionFilterMask.Areas) != 0;

            TerrainSystem terrainSystem = world.GetExistingSystemManaged<TerrainSystem>();
            TerrainHeightData terrainData = default(TerrainHeightData);
            bool hasTerrainData = false;
            if (terrainSystem != null && needMovingTree)
            {
                terrainData = terrainSystem.GetHeightData(false);
                hasTerrainData = terrainData.isCreated;
            }

            Game.Objects.SearchSystem objectSearch = (needStaticTree || needMovingTree)
                ? world.GetExistingSystemManaged<Game.Objects.SearchSystem>()
                : null;

            if (objectSearch != null && needStaticTree)
            {
                NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = objectSearch.GetStaticSearchTree(true, out JobHandle deps);
                deps.Complete();
                EntityTreeIterator iterator = new EntityTreeIterator(
                    em, shape, mask, safety, seen, result, isNetTree: false,
                    undergroundView: undergroundView, hasTerrainData: hasTerrainData, terrainData: terrainData);
                tree.Iterate(ref iterator);
                objectSearch.AddStaticSearchTreeReader(default(JobHandle));
            }

            if (objectSearch != null && needMovingTree)
            {
                NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = objectSearch.GetMovingSearchTree(true, out JobHandle deps);
                deps.Complete();
                EntityTreeIterator iterator = new EntityTreeIterator(
                    em, shape, mask, safety, seen, result, isNetTree: false,
                    undergroundView: undergroundView, hasTerrainData: hasTerrainData, terrainData: terrainData);
                tree.Iterate(ref iterator);
                objectSearch.AddMovingSearchTreeReader(default(JobHandle));
            }

            if (terrainSystem != null && needMovingTree)
            {
                terrainSystem.AddCPUHeightReader(default(JobHandle));
            }

            if (needNetTree)
            {
                Game.Net.SearchSystem netSearch = world.GetExistingSystemManaged<Game.Net.SearchSystem>();
                if (netSearch != null)
                {
                    NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = netSearch.GetNetSearchTree(true, out JobHandle deps);
                    deps.Complete();
                    EntityTreeIterator iterator = new EntityTreeIterator(
                        em, shape, mask, safety, seen, result, isNetTree: true,
                        undergroundView: undergroundView, hasTerrainData: false, terrainData: default(TerrainHeightData));
                    tree.Iterate(ref iterator);
                    netSearch.AddNetSearchTreeReader(default(JobHandle));
                }
            }

            if (needAreaTree)
            {
                Game.Areas.SearchSystem areaSearch = world.GetExistingSystemManaged<Game.Areas.SearchSystem>();
                if (areaSearch != null)
                {
                    NativeQuadTree<AreaSearchItem, QuadTreeBoundsXZ> tree = areaSearch.GetSearchTree(true, out JobHandle deps);
                    deps.Complete();
                    AreaTreeIterator iterator = new AreaTreeIterator(
                        em, shape, mask, safety, seen, result, undergroundView);
                    tree.Iterate(ref iterator);
                    areaSearch.AddSearchTreeReader(default(JobHandle));
                }
            }

            if (includeCascade) BuildDeletionPlan(em, result);
            return result;
        }

        internal static SelectionResult Revalidate(World world, EntityManager em, SelectionResult locked,
            IReadOnlyList<SelectedEntityItem> items, SelectionFilterMask mask, SafetyOptions safety, bool underground,
            IReadOnlyCollection<Entity> deferredNodes = null)
        {
            var result = new SelectionResult { Shape = locked.Shape, Safety = safety };
            foreach (Entity ex in locked.ManuallyExcluded)
            {
                result.ManuallyExcluded.Add(ex);
            }
            foreach (Entity add in locked.ManuallyAdded)
            {
                if (!result.ManuallyExcluded.Contains(add))
                {
                    result.ManuallyAdded.Add(add);
                }
            }

            var terrain = default(TerrainHeightData);
            bool hasTerrain = items.Count > 0 && (mask & SelectionFilterMask.Moving) != 0 && TryGetTerrain(world, out terrain);
            foreach (var item in items)
            {
                if (result.ManuallyExcluded.Contains(item.Entity) || locked.DeferredNodes?.Contains(item.Entity) == true)
                    continue;

                var snapshot = BuildSnapshot(em, item.Entity, hasTerrain, ref terrain);
                if (SelectionClassifier.Evaluate(in snapshot, mask, in safety, underground, out var category) != FilterSkipReason.Accepted)
                    continue;

                if (!result.ManuallyAdded.Contains(item.Entity))
                {
                    if ((em.HasComponent<Transform>(item.Entity) || em.HasComponent<Curve>(item.Entity) ||
                         em.HasComponent<Game.Net.Node>(item.Entity)) &&
                        !MatchesEntityGeometry(em, item.Entity, in locked.Shape, default(QuadTreeBoundsXZ), in snapshot))
                        continue;

                    if (em.HasComponent<Area>(item.Entity) && !MatchesAreaGeometry(em, item.Entity, in locked.Shape))
                        continue;
                }
                AddItem(em, result, item.Entity, category, in snapshot);
            }
            PlanWithDeferredNodes(em, result, deferredNodes);
            return result;
        }

        internal static SelectionResult ExcludeImportantBuildings(
            EntityManager em,
            SelectionResult current,
            out int excludedCount)
        {
            excludedCount = 0;
            if (current == null)
            {
                return null;
            }

            var importantSet = new HashSet<Entity>(current.ImportantEntities.Keys);
            for (int i = 0; i < current.Items.Count; i++)
            {
                SelectedEntityItem item = current.Items[i];
                if (item.ImportantKind != ImportantBuildingKind.None)
                {
                    importantSet.Add(item.Entity);
                }
            }

            if (importantSet.Count == 0)
            {
                return current;
            }

            var excludedSet = new HashSet<Entity>(current.ManuallyExcluded);
            foreach (Entity important in importantSet)
            {
                if (important != Entity.Null)
                {
                    excludedSet.Add(important);
                }
            }

            SelectionResult next = BuildFilteredCopy(em, current, excludedSet);
            excludedCount = importantSet.Count;
            return next;
        }

        private static SelectionResult BuildFilteredCopy(
            EntityManager em,
            SelectionResult source,
            HashSet<Entity> excludedSet)
        {
            var next = new SelectionResult
            {
                Shape = source.Shape,
                Safety = source.Safety,
            };
            foreach (Entity ex in excludedSet)
            {
                next.ManuallyExcluded.Add(ex);
            }
            foreach (Entity add in source.ManuallyAdded)
            {
                if (!excludedSet.Contains(add))
                {
                    next.ManuallyAdded.Add(add);
                }
            }

            for (int i = 0; i < source.Items.Count; i++)
            {
                SelectedEntityItem item = source.Items[i];
                if (excludedSet.Contains(item.Entity) ||
                    source.DeferredNodes?.Contains(item.Entity) == true ||
                    !em.Exists(item.Entity) ||
                    em.HasComponent<Deleted>(item.Entity))
                {
                    continue;
                }

                next.Entities.Add(item.Entity);
                next.Items.Add(item);
                next.Breakdown.Add(item.Category, item.ZoneType, item.ImportantKind);
            }

            PlanWithDeferredNodes(em, next, source.DeferredNodes);
            return next;
        }

        private static void PlanWithDeferredNodes(EntityManager em, SelectionResult result, IReadOnlyCollection<Entity> deferredNodes)
        {
            if (deferredNodes == null || deferredNodes.Count == 0)
            {
                BuildDeletionPlan(em, result);
                return;
            }

            var nodes = new HashSet<Entity>();
            foreach (Entity node in deferredNodes)
            {
                if (!result.ManuallyExcluded.Contains(node) && em.Exists(node) &&
                    em.HasComponent<Game.Net.Node>(node) && !em.HasComponent<Deleted>(node) && nodes.Add(node))
                    result.Entities.Add(node);
            }

            BuildDeletionPlan(em, result);
            result.Entities.RemoveAll(nodes.Contains);
            result.DeferredNodes = nodes;
            nodes.IntersectWith(result.Plan.Submitted);

            foreach (Entity node in nodes)
            {
                var snapshot = BuildSnapshot(em, node);
                AddItem(em, result, node, SelectionCategory.Network, in snapshot);
            }
        }

        internal static Entity ResolvePickRoot(EntityManager em, Entity picked)
        {
            if (picked == Entity.Null || !em.Exists(picked))
            {
                return Entity.Null;
            }

            Entity curr = picked;
            for (int depth = 0; depth < 16; depth++)
            {
                if (curr == Entity.Null || !em.Exists(curr))
                {
                    return Entity.Null;
                }

                // Vehicles & creatures often carry Owner pointing to depot/household; keep them
                // selectable on their own, walking only multi-carriage Controller links.
                if (IsMovingEntity(em, curr))
                {
                    if (em.HasComponent<Game.Vehicles.Controller>(curr))
                    {
                        Entity controller = em.GetComponentData<Game.Vehicles.Controller>(curr).m_Controller;
                        if (controller != Entity.Null && controller != curr && em.Exists(controller))
                        {
                            curr = controller;
                            continue;
                        }
                    }
                    break;
                }

                if (em.HasComponent<Owner>(curr))
                {
                    Entity owner = em.GetComponentData<Owner>(curr).m_Owner;
                    if (owner != Entity.Null && owner != curr && em.Exists(owner))
                    {
                        curr = owner;
                        continue;
                    }
                }
                else if (!IsBuilding(em, curr) && em.HasComponent<Attached>(curr))
                {
                    Entity parent = em.GetComponentData<Attached>(curr).m_Parent;
                    if (parent != Entity.Null && parent != curr && em.Exists(parent))
                    {
                        curr = parent;
                        continue;
                    }
                }

                break;
            }

            return curr;
        }

        internal static SelectionResult ReevaluateWithCurrentFilters(
            World world,
            EntityManager em,
            SelectionResult previous,
            SelectionFilterMask mask,
            SafetyOptions safety,
            bool underground)
        {
            if (previous == null)
            {
                return new SelectionResult { Safety = safety };
            }

            SelectionResult next = previous.Shape.IsUsable()
                ? Collect(world, em, previous.Shape, mask, safety, underground, includeCascade: false, previous.ManuallyExcluded)
                : new SelectionResult { Shape = previous.Shape, Safety = safety };

            if (!previous.Shape.IsUsable())
            {
                foreach (Entity ex in previous.ManuallyExcluded)
                {
                    next.ManuallyExcluded.Add(ex);
                }
            }

            if (previous.ManuallyAdded.Count > 0)
            {
                var existing = new HashSet<Entity>(next.Entities);
                var terrain = default(TerrainHeightData);
                bool hasTerrain = (mask & SelectionFilterMask.Moving) != 0 && TryGetTerrain(world, out terrain);

                foreach (Entity entity in previous.ManuallyAdded)
                {
                    if (entity == Entity.Null ||
                        next.ManuallyExcluded.Contains(entity) ||
                        !em.Exists(entity) ||
                        em.HasComponent<Deleted>(entity))
                    {
                        continue;
                    }

                    // Always preserve manual click additions across filter/view toggles even when
                    // currently filtered out so turning the filter back on restores them cleanly.
                    next.ManuallyAdded.Add(entity);

                    EntityFlagsSnapshot snapshot = BuildSnapshot(em, entity, hasTerrain, ref terrain);
                    if (SelectionClassifier.Evaluate(in snapshot, mask, in safety, underground, out SelectionCategory category) != FilterSkipReason.Accepted)
                    {
                        continue;
                    }

                    if (existing.Add(entity))
                    {
                        AddItem(em, next, entity, category, in snapshot);
                    }
                }
            }

            BuildDeletionPlan(em, next);
            return next;
        }

        internal static SelectionResult AddSingleEntity(
            World world,
            EntityManager em,
            SelectionResult current,
            Entity pickedEntity,
            SelectionFilterMask mask,
            SafetyOptions safety,
            bool underground,
            out bool added,
            out FilterSkipReason skipReason)
        {
            added = false;
            skipReason = FilterSkipReason.InvalidOrInactive;

            Entity root = ResolvePickRoot(em, pickedEntity);
            if (root == Entity.Null || !em.Exists(root))
            {
                return current;
            }

            if (current != null && current.Entities.Contains(root))
            {
                skipReason = FilterSkipReason.Accepted;
                return current;
            }

            var terrain = default(TerrainHeightData);
            bool hasTerrain = (mask & SelectionFilterMask.Moving) != 0 && TryGetTerrain(world, out terrain);
            EntityFlagsSnapshot snapshot = BuildSnapshot(em, root, hasTerrain, ref terrain);
            skipReason = SelectionClassifier.Evaluate(in snapshot, mask, in safety, underground, out SelectionCategory category);
            if (skipReason != FilterSkipReason.Accepted)
            {
                return current;
            }

            var next = new SelectionResult
            {
                Shape = current != null ? current.Shape : default(MarqueeShapeXZ),
                Safety = safety,
            };

            if (current != null)
            {
                foreach (Entity ex in current.ManuallyExcluded)
                {
                    if (ex != root)
                    {
                        next.ManuallyExcluded.Add(ex);
                    }
                }
                foreach (Entity add in current.ManuallyAdded)
                {
                    if (!next.ManuallyExcluded.Contains(add))
                    {
                        next.ManuallyAdded.Add(add);
                    }
                }
                for (int i = 0; i < current.Items.Count; i++)
                {
                    SelectedEntityItem item = current.Items[i];
                    if (next.ManuallyExcluded.Contains(item.Entity) ||
                        !em.Exists(item.Entity) ||
                        em.HasComponent<Deleted>(item.Entity))
                    {
                        continue;
                    }

                    next.Entities.Add(item.Entity);
                    next.Items.Add(item);
                    next.Breakdown.Add(item.Category, item.ZoneType, item.ImportantKind);
                }
            }

            next.ManuallyAdded.Add(root);
            AddItem(em, next, root, category, in snapshot);
            BuildDeletionPlan(em, next);

            if (!next.Entities.Contains(root))
            {
                return current;
            }

            added = true;
            return next;
        }

        internal static SelectionResult RemoveSingleEntity(
            EntityManager em,
            SelectionResult current,
            Entity pickedEntity,
            out bool removed)
        {
            removed = false;
            if (current == null || current.Count == 0 || pickedEntity == Entity.Null)
            {
                return current;
            }

            Entity root = ResolvePickRoot(em, pickedEntity);
            Entity target = Entity.Null;
            if (current.Entities.Contains(pickedEntity))
            {
                target = pickedEntity;
            }
            else if (root != Entity.Null && current.Entities.Contains(root))
            {
                target = root;
            }

            if (target == Entity.Null)
            {
                return current;
            }

            var excludedSet = new HashSet<Entity>(current.ManuallyExcluded) { target };
            SelectionResult next = BuildFilteredCopy(em, current, excludedSet);
            removed = true;
            return next;
        }

        internal static SelectionResult FilterPendingBySimilarEntity(
            EntityManager em,
            SelectionResult current,
            Entity pickedEntity,
            out MatchSimilarOutcome outcome,
            out int matchedCount)
        {
            matchedCount = 0;
            if (current == null || current.Count == 0)
            {
                outcome = MatchSimilarOutcome.InvalidSelection;
                return current;
            }

            if (pickedEntity == Entity.Null || !em.Exists(pickedEntity) || em.HasComponent<Deleted>(pickedEntity))
            {
                outcome = MatchSimilarOutcome.NoTarget;
                return current;
            }

            Entity target = ResolvePickRoot(em, pickedEntity);
            if (target == Entity.Null || !em.Exists(target) || em.HasComponent<Deleted>(target))
            {
                target = pickedEntity;
            }

            Entity targetPrefab = em.HasComponent<PrefabRef>(target)
                ? em.GetComponentData<PrefabRef>(target).m_Prefab
                : Entity.Null;
            if (targetPrefab == Entity.Null || !em.Exists(targetPrefab))
            {
                outcome = MatchSimilarOutcome.NoPrefab;
                return current;
            }

            EntityFlagsSnapshot targetSnap = BuildSnapshot(em, target);
            SelectionClassifier.Evaluate(
                in targetSnap,
                SelectionFilterMask.All,
                new SafetyOptions { ProtectOutsideConnections = false, ProtectUniqueBuildings = false },
                targetSnap.IsUnderground,
                out SelectionCategory targetCategory);

            if (targetCategory == SelectionCategory.None)
            {
                outcome = MatchSimilarOutcome.NoPrefab;
                return current;
            }

            var candidates = new List<SimilarKeyCandidate>(current.Items.Count);
            for (int i = 0; i < current.Items.Count; i++)
            {
                SelectedEntityItem item = current.Items[i];
                Entity itemPrefab = item.Prefab;
                if (itemPrefab == Entity.Null && item.Entity != Entity.Null && em.Exists(item.Entity) && em.HasComponent<PrefabRef>(item.Entity))
                {
                    itemPrefab = em.GetComponentData<PrefabRef>(item.Entity).m_Prefab;
                }
                candidates.Add(new SimilarKeyCandidate
                {
                    PrefabId = itemPrefab == targetPrefab ? targetPrefab.Index : -1,
                    Category = item.Category,
                });
            }

            List<int> matchedIndices = SelectionMatchLogic.FilterIndicesBySimilarKey(
                candidates,
                targetPrefab.Index,
                targetCategory,
                out outcome);

            if (outcome != MatchSimilarOutcome.Matched || matchedIndices.Count == 0)
            {
                return current;
            }

            var keepEntities = new HashSet<Entity>();
            for (int i = 0; i < matchedIndices.Count; i++)
            {
                SelectedEntityItem item = current.Items[matchedIndices[i]];
                if (item.Entity != Entity.Null && em.Exists(item.Entity) && !em.HasComponent<Deleted>(item.Entity))
                {
                    keepEntities.Add(item.Entity);
                }
            }

            if (keepEntities.Count == 0)
            {
                outcome = MatchSimilarOutcome.NoMatchesInSelection;
                return current;
            }

            var excludedSet = new HashSet<Entity>(current.ManuallyExcluded);
            for (int i = 0; i < current.Items.Count; i++)
            {
                Entity e = current.Items[i].Entity;
                if (e != Entity.Null && !keepEntities.Contains(e))
                {
                    excludedSet.Add(e);
                }
            }

            SelectionResult next = BuildFilteredCopy(em, current, excludedSet);
            matchedCount = next.Count;
            outcome = matchedCount > 0 ? MatchSimilarOutcome.Matched : MatchSimilarOutcome.NoMatchesInSelection;
            return matchedCount > 0 ? next : current;
        }

        private static void BuildDeletionPlan(EntityManager em, SelectionResult result)
        {
            HashSet<Entity> culledRoots = result.ManuallyExcluded.Count > 0 ? new HashSet<Entity>() : null;
            result.Plan = Cascade.PlanExcluding(em, result.Entities, result.ManuallyExcluded, culledRoots);
            if (culledRoots != null && culledRoots.Count > 0)
            {
                for (int i = result.Entities.Count - 1; i >= 0; i--)
                {
                    if (culledRoots.Contains(result.Entities[i]))
                    {
                        result.Entities.RemoveAt(i);
                    }
                }

                for (int i = result.Items.Count - 1; i >= 0; i--)
                {
                    if (culledRoots.Contains(result.Items[i].Entity))
                    {
                        result.Items.RemoveAt(i);
                    }
                }

                var rebuilt = default(SelectionBreakdown);
                for (int i = 0; i < result.Items.Count; i++)
                {
                    SelectedEntityItem item = result.Items[i];
                    rebuilt.Add(item.Category, item.ZoneType, item.ImportantKind);
                }
                result.Breakdown = rebuilt;
            }

            result.AffectsProtectedObject = false;
            var breakdown = result.Breakdown;
            breakdown.ImportantBuildings = breakdown.ServiceBuildings =
                breakdown.UniqueBuildings = breakdown.SignatureBuildings = 0;
            result.ImportantEntities.Clear();

            Dictionary<Entity, ImportantBuildingKind> knownBuildings = null;
            if (breakdown.Buildings > 0 && result.Items.Count > 0)
            {
                knownBuildings = new Dictionary<Entity, ImportantBuildingKind>(breakdown.Buildings);
                for (int i = 0; i < result.Items.Count; i++)
                {
                    SelectedEntityItem item = result.Items[i];
                    if (item.Category == SelectionCategory.Building)
                    {
                        knownBuildings[item.Entity] = item.ImportantKind;
                    }
                }
            }

            foreach (var entity in result.Plan.Affected)
            {
                if (em.HasComponent<MapTile>(entity) || em.HasComponent<District>(entity)
                    || (result.Safety.ProtectOutsideConnections && IsOutsideConnection(em, entity))
                    || (result.Safety.ProtectUniqueBuildings && em.HasComponent<Game.Objects.UniqueObject>(entity)))
                    result.AffectsProtectedObject = true;

                ImportantBuildingKind kind;
                if (knownBuildings != null && knownBuildings.TryGetValue(entity, out kind))
                {
                    // Reuse already-resolved building metadata from primary selection.
                }
                else
                {
                    if (!IsBuilding(em, entity)) continue;
                    ResolveBuildingMeta(em, entity, out _, out kind, out _);
                }

                if (kind == ImportantBuildingKind.None) continue;
                if (result.Safety.ProtectUniqueBuildings && (kind & (ImportantBuildingKind.Unique | ImportantBuildingKind.Signature)) != 0)
                    result.AffectsProtectedObject = true;

                result.ImportantEntities[entity] = kind;
                breakdown.AddImportant(kind);
            }
            result.Breakdown = breakdown;
        }

        private static bool TryGetTerrain(World world, out TerrainHeightData data)
        {
            var terrain = world.GetExistingSystemManaged<TerrainSystem>();
            data = terrain != null ? terrain.GetHeightData(false) : default(TerrainHeightData);
            return data.isCreated;
        }

        private static bool IsBuilding(EntityManager em, Entity entity) =>
            em.HasComponent<Building>(entity) || em.HasComponent<Game.Buildings.ServiceUpgrade>(entity) ||
            em.HasComponent<Extension>(entity);

        private static bool IsMovingEntity(EntityManager em, Entity entity) =>
            em.HasComponent<Moving>(entity) || em.HasComponent<Vehicle>(entity) ||
            em.HasComponent<Creature>(entity);

        private static bool IsOutsideConnection(EntityManager em, Entity entity) =>
            em.HasComponent<Game.Objects.OutsideConnection>(entity) || em.HasComponent<Game.Net.OutsideConnection>(entity) ||
            em.HasComponent<Game.Objects.ElectricityOutsideConnection>(entity) || em.HasComponent<Game.Objects.WaterPipeOutsideConnection>(entity);

        private static void AddItem(EntityManager em, SelectionResult result, Entity entity,
            SelectionCategory category, in EntityFlagsSnapshot snapshot)
        {
            float2 footprint = category == SelectionCategory.Building
                ? new float2(math.max(1f, snapshot.FootprintWidth), math.max(1f, snapshot.FootprintDepth))
                : new float2(8f, 8f);
            Entity prefab = em.HasComponent<PrefabRef>(entity)
                ? em.GetComponentData<PrefabRef>(entity).m_Prefab
                : Entity.Null;

            result.Entities.Add(entity);
            result.Items.Add(new SelectedEntityItem
            {
                Entity = entity,
                Prefab = prefab,
                Category = category,
                ZoneType = snapshot.ZoneType,
                ImportantKind = snapshot.ImportantKind,
                NetworkKind = snapshot.NetworkKind,
                FootprintXZ = footprint,
            });
            result.Breakdown.Add(category, snapshot.ZoneType, snapshot.ImportantKind);
        }

        public static EntityFlagsSnapshot BuildSnapshot(EntityManager em, Entity entity)
        {
            TerrainHeightData unused = default(TerrainHeightData);
            return BuildSnapshot(em, entity, false, ref unused);
        }

        private static EntityFlagsSnapshot BuildSnapshot(
            EntityManager em,
            Entity entity,
            bool hasTerrainData,
            ref TerrainHeightData terrainData)
        {
            if (entity == Entity.Null || !em.Exists(entity))
            {
                return new EntityFlagsSnapshot { IsNull = true };
            }

            bool hasAttachedParent = false;
            if (em.HasComponent<Attached>(entity))
            {
                hasAttachedParent = em.GetComponentData<Attached>(entity).m_Parent != Entity.Null;
            }

            bool isBuilding = IsBuilding(em, entity);

            BuildingZoneType zoneType = BuildingZoneType.None;
            ImportantBuildingKind importantKind = ImportantBuildingKind.None;
            float2 footprintXZ = new float2(8f, 8f);
            if (isBuilding)
            {
                ResolveBuildingMeta(em, entity, out zoneType, out importantKind, out footprintXZ);
            }

            bool isNetwork = em.HasComponent<Edge>(entity) || em.HasComponent<Game.Net.Node>(entity);
            NetworkSubKind networkKind = isNetwork ? ResolveNetworkSubKind(em, entity) : NetworkSubKind.None;
            bool isMoving =
                em.HasComponent<Moving>(entity) ||
                em.HasComponent<Vehicle>(entity) ||
                em.HasComponent<Creature>(entity);

            bool isUnderground = IsEntityUnderground(em, entity, isNetwork, isMoving, hasTerrainData, ref terrainData);

            return new EntityFlagsSnapshot
            {
                IsNull = false,
                IsDeleted = em.HasComponent<Deleted>(entity),
                IsHidden = em.HasComponent<Hidden>(entity),
                IsTemp = em.HasComponent<Temp>(entity),
                IsOverridden = em.HasComponent<Overridden>(entity),
                IsTerrain = em.HasComponent<Game.Common.Terrain>(entity) || em.HasComponent<Game.Areas.Terrain>(entity),
                IsPlaceholder = em.HasComponent<Placeholder>(entity),
                IsMarker = em.HasComponent<Game.Objects.Marker>(entity) || em.HasComponent<Game.Net.Marker>(entity),

                HasOwner = em.HasComponent<Owner>(entity),
                HasAttachedParent = hasAttachedParent,

                IsMapTile = em.HasComponent<MapTile>(entity),
                IsDistrict = em.HasComponent<District>(entity),

                IsOutsideConnection = IsOutsideConnection(em, entity),

                IsUniqueObject = em.HasComponent<Game.Objects.UniqueObject>(entity),

                IsBuilding = isBuilding,
                ZoneType = zoneType,
                ImportantKind = importantKind,
                NetworkKind = networkKind,
                FootprintWidth = footprintXZ.x,
                FootprintDepth = footprintXZ.y,

                IsMoving = isMoving,

                IsPlant = em.HasComponent<Plant>(entity) || em.HasComponent<Game.Objects.Tree>(entity),
                IsPillar = em.HasComponent<Pillar>(entity),
                IsObjectGeometry = em.HasComponent<ObjectGeometry>(entity) || em.HasComponent<Static>(entity),
                HasObjectSurface = em.HasComponent<Game.Objects.Surface>(entity),
                HasNetObject =
                    em.HasComponent<Game.Objects.NetObject>(entity) ||
                    em.HasComponent<Game.Objects.UtilityObject>(entity) ||
                    em.HasComponent<AssetStamp>(entity),

                IsNetwork = isNetwork,
                IsSafeArea =
                    em.HasComponent<Game.Areas.Lot>(entity) ||
                    em.HasComponent<Game.Areas.Surface>(entity) ||
                    em.HasComponent<Space>(entity),
                IsUnderground = isUnderground,
            };
        }

        private static bool IsEntityUnderground(
            EntityManager em,
            Entity entity,
            bool isNetwork,
            bool isMoving,
            bool hasTerrainData,
            ref TerrainHeightData terrainData)
        {
            if (isNetwork)
            {
                if (HasTunnelComposition(em, entity) || HasConnectedTunnelEdge(em, entity))
                {
                    return true;
                }

                bool isRoad = em.HasComponent<Road>(entity);
                if (!isRoad && em.HasComponent<Game.Net.WaterPipeConnection>(entity))
                {
                    return true;
                }

                if (em.HasComponent<Game.Net.Elevation>(entity))
                {
                    float2 e = em.GetComponentData<Game.Net.Elevation>(entity).m_Elevation;
                    if (!isRoad)
                    {
                        if ((e.x < -0.5f && e.y < -0.5f) ||
                            (math.min(e.x, e.y) < -1.5f && (e.x + e.y) < -1.5f))
                        {
                            return true;
                        }
                    }
                    else if (!em.HasComponent<Composition>(entity) && e.x <= -9.5f && e.y <= -9.5f)
                    {
                        return true;
                    }
                }

                return false;
            }

            if (em.HasComponent<Game.Objects.Elevation>(entity))
            {
                Game.Objects.Elevation objElev = em.GetComponentData<Game.Objects.Elevation>(entity);
                if (objElev.m_Elevation < -1.25f && (objElev.m_Flags & ElevationFlags.Lowered) == 0)
                {
                    return true;
                }
            }

            if (isMoving && hasTerrainData && em.HasComponent<Transform>(entity))
            {
                float3 pos = em.GetComponentData<Transform>(entity).m_Position;
                float terrainY = TerrainUtils.SampleHeight(ref terrainData, pos);
                if (pos.y < terrainY - 2.25f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTunnelComposition(EntityManager em, Entity entity)
        {
            if (em.HasComponent<Composition>(entity))
            {
                Entity comp = em.GetComponentData<Composition>(entity).m_Edge;
                if (comp != Entity.Null && em.Exists(comp) && em.HasComponent<NetCompositionData>(comp))
                {
                    if ((em.GetComponentData<NetCompositionData>(comp).m_Flags.m_General & CompositionFlags.General.Tunnel) != 0)
                    {
                        return true;
                    }
                }
            }

            if (em.HasComponent<Orphan>(entity))
            {
                Entity comp = em.GetComponentData<Orphan>(entity).m_Composition;
                if (comp != Entity.Null && em.Exists(comp) && em.HasComponent<NetCompositionData>(comp))
                {
                    if ((em.GetComponentData<NetCompositionData>(comp).m_Flags.m_General & CompositionFlags.General.Tunnel) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasConnectedTunnelEdge(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<Game.Net.Node>(entity) || em.HasComponent<Composition>(entity) || !em.HasBuffer<ConnectedEdge>(entity))
            {
                return false;
            }

            DynamicBuffer<ConnectedEdge> edges = em.GetBuffer<ConnectedEdge>(entity, true);
            int edgeCount = 0;
            int tunnelEdgeCount = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity edge = edges[i].m_Edge;
                if (edge == Entity.Null || !em.Exists(edge))
                {
                    continue;
                }

                edgeCount++;
                if (HasTunnelComposition(em, edge))
                {
                    tunnelEdgeCount++;
                }
            }

            if (edgeCount == 0 || tunnelEdgeCount == 0)
            {
                return false;
            }

            if (tunnelEdgeCount == edgeCount)
            {
                return true;
            }

            if (em.HasComponent<Game.Net.Elevation>(entity))
            {
                float2 e = em.GetComponentData<Game.Net.Elevation>(entity).m_Elevation;
                if (e.x < -0.5f && e.y < -0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetEntityCenter(EntityManager em, Entity entity, out float3 center, out float approxRadius)
        {
            center = default(float3);
            approxRadius = 1.5f;

            if (entity == Entity.Null || !em.Exists(entity))
            {
                return false;
            }

            if (em.HasComponent<Transform>(entity))
            {
                center = em.GetComponentData<Transform>(entity).m_Position;
                if (em.HasComponent<PrefabRef>(entity))
                {
                    Entity prefab = em.GetComponentData<PrefabRef>(entity).m_Prefab;
                    if (prefab != Entity.Null && em.Exists(prefab) && em.HasComponent<ObjectGeometryData>(prefab))
                    {
                        float3 size = em.GetComponentData<ObjectGeometryData>(prefab).m_Size;
                        approxRadius = math.clamp(math.max(size.x, size.z) * 0.5f, 0.75f, 60f);
                    }
                }
                return true;
            }

            if (em.HasComponent<Curve>(entity))
            {
                Curve curve = em.GetComponentData<Curve>(entity);
                center = MathUtils.Position(curve.m_Bezier, 0.5f);
                approxRadius = 3f;
                return true;
            }

            if (em.HasComponent<Game.Net.Node>(entity))
            {
                center = em.GetComponentData<Game.Net.Node>(entity).m_Position;
                approxRadius = 2.5f;
                return true;
            }

            if (em.HasBuffer<Game.Areas.Node>(entity))
            {
                DynamicBuffer<Game.Areas.Node> nodes = em.GetBuffer<Game.Areas.Node>(entity, true);
                if (nodes.Length > 0)
                {
                    float3 sum = default(float3);
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        sum += nodes[i].m_Position;
                    }
                    center = sum / nodes.Length;
                    approxRadius = 4f;
                    return true;
                }
            }

            return false;
        }

        private static bool IntersectsBezierXZ(in MarqueeShapeXZ shape, Bezier4x3 bezier, int steps)
        {
            float3 prev = bezier.a;
            for (int i = 1; i <= steps; i++)
            {
                float3 curr = MathUtils.Position(bezier, i / (float)steps);
                if (shape.IntersectsSegment(new CoordXZ(prev.x, prev.z), new CoordXZ(curr.x, curr.z)))
                {
                    return true;
                }
                prev = curr;
            }
            return false;
        }

        private static bool IntersectsSegmentXZ(in MarqueeShapeXZ shape, Game.Net.Segment seg)
        {
            if (IntersectsBezierXZ(in shape, seg.m_Left, 6) ||
                IntersectsBezierXZ(in shape, seg.m_Right, 6))
            {
                return true;
            }

            // Also test cross-ribs between left and right road edges (catches marquees inside wide multi-lane pavement).
            for (int i = 0; i <= 4; i++)
            {
                float t = i * 0.25f;
                float3 l = MathUtils.Position(seg.m_Left, t);
                float3 r = MathUtils.Position(seg.m_Right, t);
                if (shape.IntersectsSegment(new CoordXZ(l.x, l.z), new CoordXZ(r.x, r.z)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryPreRejectEntity(
            EntityManager em,
            Entity item,
            in MarqueeShapeXZ shape,
            SelectionFilterMask mask,
            in SafetyOptions safety,
            bool isNetTree)
        {
            if (item == Entity.Null || !em.Exists(item) ||
                em.HasComponent<Deleted>(item) ||
                em.HasComponent<Hidden>(item) ||
                em.HasComponent<Temp>(item) ||
                em.HasComponent<Overridden>(item) ||
                em.HasComponent<Game.Common.Terrain>(item) ||
                em.HasComponent<Game.Areas.Terrain>(item) ||
                em.HasComponent<Placeholder>(item) ||
                em.HasComponent<Game.Objects.Marker>(item) ||
                em.HasComponent<Game.Net.Marker>(item))
            {
                return true;
            }

            if (em.HasComponent<MapTile>(item) || em.HasComponent<District>(item))
            {
                return true;
            }

            if (safety.ProtectOutsideConnections && IsOutsideConnection(em, item))
            {
                return true;
            }

            if (!isNetTree && (em.HasComponent<Moving>(item) || em.HasComponent<Vehicle>(item) || em.HasComponent<Creature>(item)))
            {
                if ((mask & SelectionFilterMask.Moving) == 0)
                {
                    return true;
                }

                if (em.HasComponent<Transform>(item))
                {
                    float3 pos = em.GetComponentData<Transform>(item).m_Position;
                    if (!shape.ContainsPoint(new CoordXZ(pos.x, pos.z)))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (em.HasComponent<Owner>(item))
            {
                return true;
            }

            bool isBuilding = !isNetTree && IsBuilding(em, item);
            if (!isBuilding && em.HasComponent<Attached>(item) && em.GetComponentData<Attached>(item).m_Parent != Entity.Null)
            {
                return true;
            }

            if (!isNetTree && !isBuilding)
            {
                if (em.HasComponent<Transform>(item))
                {
                    float3 pos = em.GetComponentData<Transform>(item).m_Position;
                    if (!shape.ContainsPoint(new CoordXZ(pos.x, pos.z)))
                    {
                        return true;
                    }
                }

                if ((mask & SelectionFilterMask.Trees) == 0 &&
                    (em.HasComponent<Plant>(item) || em.HasComponent<Game.Objects.Tree>(item)) &&
                    !em.HasComponent<Pillar>(item))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAreaGeometry(EntityManager em, Entity entity, in MarqueeShapeXZ shape)
        {
            if (!em.HasBuffer<Game.Areas.Node>(entity) || !em.HasBuffer<Triangle>(entity)) return false;
            var nodes = em.GetBuffer<Game.Areas.Node>(entity, true);
            var triangles = em.GetBuffer<Triangle>(entity, true);
            for (int i = 0; i < triangles.Length; i++)
            {
                int3 indices = triangles[i].m_Indices;
                if (indices.x < 0 || indices.y < 0 || indices.z < 0 ||
                    indices.x >= nodes.Length || indices.y >= nodes.Length || indices.z >= nodes.Length) continue;
                float3 a = nodes[indices.x].m_Position;
                float3 b = nodes[indices.y].m_Position;
                float3 c = nodes[indices.z].m_Position;
                if (shape.IntersectsTriangle(new CoordXZ(a.x, a.z), new CoordXZ(b.x, b.z), new CoordXZ(c.x, c.z))) return true;
            }
            return false;
        }

        private static bool MatchesEntityGeometry(
            EntityManager em,
            Entity entity,
            in MarqueeShapeXZ shape,
            QuadTreeBoundsXZ bounds,
            in EntityFlagsSnapshot snap)
        {
            if (em.HasComponent<Transform>(entity))
            {
                Transform t = em.GetComponentData<Transform>(entity);
                float3 pos = t.m_Position;
                if (snap.IsBuilding)
                {
                    float halfW = math.max(1f, snap.FootprintWidth) * 0.5f;
                    float halfD = math.max(1f, snap.FootprintDepth) * 0.5f;
                    float3 rx = math.mul(t.m_Rotation, new float3(halfW, 0f, 0f));
                    float3 rz = math.mul(t.m_Rotation, new float3(0f, 0f, halfD));
                    return shape.IntersectsOrientedRect(
                        new CoordXZ(pos.x, pos.z),
                        new CoordXZ(rx.x, rx.z),
                        new CoordXZ(rz.x, rz.z));
                }

                return shape.ContainsPoint(new CoordXZ(pos.x, pos.z));
            }

            if (em.HasComponent<Curve>(entity))
            {
                Bezier4x3 bezier = em.GetComponentData<Curve>(entity).m_Bezier;
                if (IntersectsBezierXZ(in shape, bezier, 8))
                {
                    return true;
                }

                if (em.HasComponent<EdgeGeometry>(entity))
                {
                    EdgeGeometry eg = em.GetComponentData<EdgeGeometry>(entity);
                    if (IntersectsSegmentXZ(in shape, eg.m_Start) ||
                        IntersectsSegmentXZ(in shape, eg.m_End))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (em.HasComponent<Game.Net.Node>(entity))
            {
                float3 pos = em.GetComponentData<Game.Net.Node>(entity).m_Position;
                if (shape.ContainsPoint(new CoordXZ(pos.x, pos.z)))
                {
                    return true;
                }

                if (em.HasComponent<NodeGeometry>(entity))
                {
                    NodeGeometry ng = em.GetComponentData<NodeGeometry>(entity);
                    return shape.IntersectsAabb(
                        ng.m_Bounds.min.x, ng.m_Bounds.max.x,
                        ng.m_Bounds.min.z, ng.m_Bounds.max.z);
                }

                // Fallback small XZ radius when a network node lacks NodeGeometry bounds.
                return shape.IntersectsAabb(
                    pos.x - 1.5f, pos.x + 1.5f,
                    pos.z - 1.5f, pos.z + 1.5f);
            }

            return shape.IntersectsAabb(bounds.m_Bounds.min.x, bounds.m_Bounds.max.x, bounds.m_Bounds.min.z, bounds.m_Bounds.max.z);
        }

        private struct EntityTreeIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            private readonly EntityManager m_EntityManager;
            private readonly MarqueeShapeXZ m_Shape;
            private readonly SelectionFilterMask m_Mask;
            private readonly SafetyOptions m_Safety;
            private readonly HashSet<Entity> m_Seen;
            private readonly SelectionResult m_Result;
            private readonly bool m_IsNetTree;
            private readonly bool m_UndergroundView;
            private readonly bool m_HasTerrainData;
            private TerrainHeightData m_TerrainData;

            public EntityTreeIterator(
                EntityManager em,
                MarqueeShapeXZ shape,
                SelectionFilterMask mask,
                SafetyOptions safety,
                HashSet<Entity> seen,
                SelectionResult result,
                bool isNetTree,
                bool undergroundView,
                bool hasTerrainData,
                TerrainHeightData terrainData)
            {
                m_EntityManager = em;
                m_Shape = shape;
                m_Mask = mask;
                m_Safety = safety;
                m_Seen = seen;
                m_Result = result;
                m_IsNetTree = isNetTree;
                m_UndergroundView = undergroundView;
                m_HasTerrainData = hasTerrainData;
                m_TerrainData = terrainData;
            }

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return m_Shape.IntersectsAabb(
                    bounds.m_Bounds.min.x, bounds.m_Bounds.max.x,
                    bounds.m_Bounds.min.z, bounds.m_Bounds.max.z);
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                m_Result.RawHits++;

                if (!m_Shape.IntersectsAabb(
                    bounds.m_Bounds.min.x, bounds.m_Bounds.max.x,
                    bounds.m_Bounds.min.z, bounds.m_Bounds.max.z))
                {
                    return;
                }

                if (!m_Seen.Add(item))
                {
                    return;
                }

                if (TryPreRejectEntity(
                    m_EntityManager,
                    item,
                    in m_Shape,
                    m_Mask,
                    in m_Safety,
                    m_IsNetTree))
                {
                    return;
                }

                EntityFlagsSnapshot snap = BuildSnapshot(m_EntityManager, item, m_HasTerrainData, ref m_TerrainData);
                FilterSkipReason reason = SelectionClassifier.Evaluate(in snap, m_Mask, in m_Safety, m_UndergroundView, out SelectionCategory category);
                if (reason != FilterSkipReason.Accepted)
                {
                    return;
                }

                if (!MatchesEntityGeometry(m_EntityManager, item, in m_Shape, bounds, in snap))
                {
                    return;
                }

                AddItem(m_EntityManager, m_Result, item, category, in snap);
            }
        }

        private struct AreaTreeIterator : INativeQuadTreeIterator<AreaSearchItem, QuadTreeBoundsXZ>
        {
            private readonly EntityManager m_EntityManager;
            private readonly MarqueeShapeXZ m_Shape;
            private readonly SelectionFilterMask m_Mask;
            private readonly SafetyOptions m_Safety;
            private readonly HashSet<Entity> m_Seen;
            private readonly SelectionResult m_Result;
            private readonly bool m_UndergroundView;

            public AreaTreeIterator(
                EntityManager em,
                MarqueeShapeXZ shape,
                SelectionFilterMask mask,
                SafetyOptions safety,
                HashSet<Entity> seen,
                SelectionResult result,
                bool undergroundView)
            {
                m_EntityManager = em;
                m_Shape = shape;
                m_Mask = mask;
                m_Safety = safety;
                m_Seen = seen;
                m_Result = result;
                m_UndergroundView = undergroundView;
            }

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return m_Shape.IntersectsAabb(
                    bounds.m_Bounds.min.x, bounds.m_Bounds.max.x,
                    bounds.m_Bounds.min.z, bounds.m_Bounds.max.z);
            }

            public void Iterate(QuadTreeBoundsXZ bounds, AreaSearchItem item)
            {
                m_Result.RawHits++;
                Entity area = item.m_Area;
                if (area == Entity.Null || m_Seen.Contains(area))
                {
                    return;
                }

                if (!m_EntityManager.Exists(area) ||
                    m_EntityManager.HasComponent<Deleted>(area) ||
                    m_EntityManager.HasComponent<Hidden>(area) ||
                    m_EntityManager.HasComponent<Temp>(area) ||
                    m_EntityManager.HasComponent<Overridden>(area))
                {
                    m_Seen.Add(area);
                    return;
                }

                if (m_EntityManager.HasComponent<MapTile>(area) || m_EntityManager.HasComponent<District>(area))
                {
                    m_Seen.Add(area);
                    return;
                }

                if (m_EntityManager.HasComponent<Owner>(area))
                {
                    m_Seen.Add(area);
                    return;
                }

                if (!m_EntityManager.HasBuffer<Game.Areas.Node>(area) ||
                    !m_EntityManager.HasBuffer<Triangle>(area))
                {
                    return;
                }

                DynamicBuffer<Game.Areas.Node> nodes = m_EntityManager.GetBuffer<Game.Areas.Node>(area, true);
                DynamicBuffer<Triangle> triangles = m_EntityManager.GetBuffer<Triangle>(area, true);
                if (item.m_Triangle < 0 || item.m_Triangle >= triangles.Length)
                {
                    return;
                }

                int3 idx = triangles[item.m_Triangle].m_Indices;
                if (idx.x < 0 || idx.y < 0 || idx.z < 0 ||
                    idx.x >= nodes.Length || idx.y >= nodes.Length || idx.z >= nodes.Length)
                {
                    return;
                }

                float3 a = nodes[idx.x].m_Position;
                float3 b = nodes[idx.y].m_Position;
                float3 c = nodes[idx.z].m_Position;
                if (!m_Shape.IntersectsTriangle(
                    new CoordXZ(a.x, a.z),
                    new CoordXZ(b.x, b.z),
                    new CoordXZ(c.x, c.z)))
                {
                    return;
                }

                if (!m_Seen.Add(area))
                {
                    return;
                }

                EntityFlagsSnapshot snap = BuildSnapshot(m_EntityManager, area);
                FilterSkipReason reason = SelectionClassifier.Evaluate(in snap, m_Mask, in m_Safety, m_UndergroundView, out SelectionCategory category);
                if (reason != FilterSkipReason.Accepted)
                {
                    return;
                }

                AddItem(m_EntityManager, m_Result, area, category, in snap);
            }
        }
    }
}
