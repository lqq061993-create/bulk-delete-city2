using System.Collections.Generic;
using System.Text;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Creatures;
using Game.Net;
using Game.Objects;
using Game.Routes;
using Game.Vehicles;
using Unity.Entities;

namespace MarqueeDelete
{
    /// <summary>
    /// Deterministic entity order. An index alone is not a stable identity: the game reuses the index
    /// of a destroyed entity, so index plus version is the pair that identifies one entity, and ordering
    /// by both turns every hash-set walk into a repeatable sequence: the same selection produces the
    /// same plan, the same command buffer and the same interrupted remainder every time.
    /// </summary>
    internal static class EntityOrder
    {
        internal sealed class IndexComparer : IComparer<Entity>
        {
            public int Compare(Entity a, Entity b)
            {
                int byIndex = a.Index.CompareTo(b.Index);
                return byIndex != 0 ? byIndex : a.Version.CompareTo(b.Version);
            }
        }

        internal static readonly IComparer<Entity> ByIndex = new IndexComparer();

        internal static List<Entity> Sorted(IEnumerable<Entity> source)
        {
            var list = new List<Entity>();
            if (source == null)
            {
                return list;
            }

            foreach (Entity entity in source)
            {
                list.Add(entity);
            }

            list.Sort(ByIndex);
            return list;
        }
    }

    /// <summary>
    /// One facility: the root the player targeted together with the objects that must be removed or
    /// restored with it. Batching and reporting work in units, not in loose entities, because a
    /// half-submitted facility is what leaves a torn network or an orphaned upgrade behind.
    /// </summary>
    internal sealed class DeletionUnit
    {
        internal readonly Entity Root;
        internal readonly List<Entity> Members = new List<Entity>();

        internal DeletionUnit(Entity root)
        {
            Root = root;
        }

        internal int Count => Members.Count;
    }

    internal sealed class DeletionPlan
    {
        internal readonly HashSet<Entity> Roots;
        internal readonly HashSet<Entity> Submitted;
        internal readonly HashSet<Entity> Neighbors;
        internal readonly HashSet<Entity> Affected;
        internal readonly HashSet<Entity> StructuralAffected;

        // NOTE: only the five-argument constructor is defined on purpose.
        // Convenience overloads that defaulted Roots to Submitted were removed: they made
        // Roots == Submitted, which re-introduced the undo double-capture regression
        // (structural sub-nets captured both as top-level networks and under their owner).

        internal DeletionPlan(
            HashSet<Entity> roots,
            HashSet<Entity> submitted,
            HashSet<Entity> neighbors,
            HashSet<Entity> affected,
            HashSet<Entity> structuralAffected)
        {
            Roots = roots ?? submitted;
            Submitted = submitted;
            Neighbors = neighbors;
            Affected = affected;
            StructuralAffected = structuralAffected;
        }

        internal bool Expands(DeletionPlan reviewed) =>
            !Submitted.IsSubsetOf(reviewed.Submitted) || !Affected.IsSubsetOf(reviewed.Affected);

        /// <summary>
        /// Checks expansion ignoring runtime-volatile SubRoute / OwnedVehicle additions that occur
        /// while the simulation is unpaused; structural sub-objects, lanes, nets, and upgrades still require review.
        /// </summary>
        internal bool ExpandsConfirmation(DeletionPlan reviewed) =>
            !Submitted.IsSubsetOf(reviewed.Submitted) || !StructuralAffected.IsSubsetOf(reviewed.StructuralAffected);

        internal bool ChangesConfirmation(DeletionPlan reviewed) =>
            !Submitted.IsSubsetOf(reviewed.Submitted) || !StructuralAffected.SetEquals(reviewed.StructuralAffected);

        /// <summary>
        /// Facilities this plan removes. Computed on demand by <see cref="Cascade.BuildUnits"/> so
        /// planning itself stays free of relation walks; null until then.
        /// </summary>
        internal List<DeletionUnit> Units { get; private set; }

        internal void AdoptUnits(List<DeletionUnit> units)
        {
            Units = units;
        }

        /// <summary>
        /// Stable text form of the plan. Two runs over the same selection must produce the same
        /// fingerprint regardless of the order the roots were handed in or hashed.
        /// Scope: this promises determinism for one entity set with one relation snapshot — the same
        /// game state reached twice from the same inputs. It does not promise that a save, a reload or
        /// a re-created entity keeps a fingerprint, because the game may hand out different indices
        /// and versions the second time.
        /// </summary>
        internal string Fingerprint()
        {
            var text = new StringBuilder(64);
            text.Append("R:");
            AppendSet(text, Roots);
            text.Append("|S:");
            AppendSet(text, Submitted);
            text.Append("|N:");
            AppendSet(text, Neighbors);
            text.Append("|A:");
            AppendSet(text, Affected);
            text.Append("|T:");
            AppendSet(text, StructuralAffected);
            return text.ToString();
        }

        private static void AppendSet(StringBuilder text, HashSet<Entity> entities)
        {
            if (entities == null)
            {
                text.Append("-");
                return;
            }

            List<Entity> ordered = EntityOrder.Sorted(entities);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0) text.Append(',');
                // Index alone can be reused by a different entity, so the fingerprint carries the pair.
                text.Append(ordered[i].Index);
                text.Append(':');
                text.Append(ordered[i].Version);
            }
        }
    }

    internal static class Cascade
    {
        /// <summary>
        /// Plans cascade deletion while ensuring no entity in <paramref name="excluded"/>
        /// appears in <see cref="DeletionPlan.Affected"/>, culling any root that would pull
        /// an excluded entity in directly or indirectly.
        /// </summary>
        internal static DeletionPlan PlanExcluding(
            EntityManager em,
            List<Entity> roots,
            HashSet<Entity> excluded,
            HashSet<Entity> culledRoots = null)
        {
            if (roots == null || roots.Count == 0)
            {
                return Plan(em, System.Array.Empty<Entity>());
            }

            if (excluded == null || excluded.Count == 0)
            {
                return Plan(em, roots);
            }

            var surviving = new List<Entity>(roots.Count);
            for (int i = 0; i < roots.Count; i++)
            {
                Entity root = roots[i];
                if (root == Entity.Null)
                {
                    continue;
                }

                if (excluded.Contains(root))
                {
                    culledRoots?.Add(root);
                    continue;
                }

                surviving.Add(root);
            }

            surviving.Sort(EntityOrder.ByIndex);
            DeletionPlan plan = Plan(em, surviving);
            if (surviving.Count == 0 || !Overlaps(plan.Affected, excluded))
            {
                return plan;
            }

            int maxIterations = surviving.Count;
            var singleRoot = new Entity[1];
            var roundCulled = new HashSet<Entity>();
            for (int iter = 0; iter < maxIterations && surviving.Count > 0 && Overlaps(plan.Affected, excluded); iter++)
            {
                roundCulled.Clear();
                for (int i = 0; i < surviving.Count; i++)
                {
                    Entity candidate = surviving[i];
                    singleRoot[0] = candidate;
                    DeletionPlan singlePlan = Plan(em, singleRoot);
                    if (Overlaps(singlePlan.Affected, excluded))
                    {
                        roundCulled.Add(candidate);
                    }
                }

                if (roundCulled.Count == 0)
                {
                    int culpritIndex = FindJointCulpritIndex(em, surviving, excluded);
                    roundCulled.Add(surviving[culpritIndex]);
                }

                for (int i = surviving.Count - 1; i >= 0; i--)
                {
                    Entity item = surviving[i];
                    if (roundCulled.Contains(item))
                    {
                        culledRoots?.Add(item);
                        surviving.RemoveAt(i);
                    }
                }

                plan = Plan(em, surviving);
            }

            return plan;
        }

        private static bool Overlaps(HashSet<Entity> affected, HashSet<Entity> excluded)
        {
            if (affected == null || affected.Count == 0 || excluded == null || excluded.Count == 0)
            {
                return false;
            }

            foreach (Entity e in excluded)
            {
                if (affected.Contains(e))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindJointCulpritIndex(
            EntityManager em,
            List<Entity> surviving,
            HashSet<Entity> excluded)
        {
            int low = 0;
            int high = surviving.Count - 1;
            while (low < high)
            {
                int mid = low + ((high - low) / 2);
                DeletionPlan prefixPlan = Plan(em, surviving.GetRange(0, mid + 1));
                if (Overlaps(prefixPlan.Affected, excluded))
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }

            return low;
        }

        internal static DeletionPlan Plan(EntityManager em, IReadOnlyList<Entity> roots)
        {
            // The game expands owned networks and lanes in PostTool and Modification.
            // The closure below is monotone, so the submitted set does not depend on the order the
            // roots arrive in; sorting keeps the command buffer order stable as well, which is what
            // makes two identically shaped selections yield the same demolition plan.
            List<Entity> orderedRoots = EntityOrder.Sorted(roots);
            var toDelete = new HashSet<Entity>();
            var queue = new Queue<Entity>();
            for (int i = 0; i < orderedRoots.Count; i++)
            {
                TryEnqueueInitialRoot(em, orderedRoots[i], toDelete, queue);
            }
            while (queue.Count > 0)
            {
                Entity vehicle = queue.Dequeue();
                if (!em.HasBuffer<LayoutElement>(vehicle)) continue;
                DynamicBuffer<LayoutElement> layout = em.GetBuffer<LayoutElement>(vehicle, true);
                for (int i = 0; i < layout.Length; i++)
                    EnqueueIfEligible(em, layout[i].m_Vehicle, toDelete, queue);
            }
            var selectedNodes = new List<Entity>();
            List<Entity> plannedOrder = EntityOrder.Sorted(toDelete);
            for (int i = 0; i < plannedOrder.Count; i++)
            {
                if (em.HasComponent<Game.Net.Node>(plannedOrder[i])) selectedNodes.Add(plannedOrder[i]);
            }
            foreach (Entity node in selectedNodes) toDelete.Remove(node);

            var affected = EstimateAffected(em, toDelete, selectedNodes, out HashSet<Entity> structuralAffected);
            var protectedNodes = new List<Entity>();
            for (int i = 0; i < selectedNodes.Count; i++)
            {
                Entity node = selectedNodes[i];
                if (affected.Contains(node)) toDelete.Add(node);
                else protectedNodes.Add(node);
            }
            var submitted = new HashSet<Entity>(toDelete);
            List<Entity> structuralOrder = EntityOrder.Sorted(structuralAffected);
            for (int i = 0; i < structuralOrder.Count; i++)
            {
                Entity entity = structuralOrder[i];
                if (entity == Entity.Null || !em.Exists(entity) || em.HasComponent<Deleted>(entity))
                {
                    continue;
                }

                if (em.HasComponent<Route>(entity))
                {
                    continue;
                }

                submitted.Add(entity);
            }

            var toUpdate = new HashSet<Entity>(protectedNodes);
            var nodesToScan = new HashSet<Entity>(protectedNodes);
            List<Entity> submittedOrder = EntityOrder.Sorted(submitted);
            for (int i = 0; i < submittedOrder.Count; i++)
            {
                Entity entity = submittedOrder[i];
                if (em.HasComponent<Edge>(entity))
                {
                    Edge edge = em.GetComponentData<Edge>(entity);
                    toUpdate.Add(edge.m_Start);
                    toUpdate.Add(edge.m_End);
                    nodesToScan.Add(edge.m_Start);
                    nodesToScan.Add(edge.m_End);
                }
                if (em.HasComponent<Game.Net.Node>(entity))
                {
                    // Node-only SubNets or LocalConnect nodes may be referenced by a surviving
                    // passing road's ConnectedNode buffer even when none of that road's edges
                    // are in submitted. Walk ConnectedEdge so the surviving road gets Updated.
                    nodesToScan.Add(entity);
                }
                if (em.HasComponent<Attached>(entity))
                    toUpdate.Add(em.GetComponentData<Attached>(entity).m_Parent);
            }
            var neighbors = EntityOrder.Sorted(nodesToScan);
            foreach (Entity node in neighbors)
            {
                if (node == Entity.Null || !em.Exists(node) || !em.HasBuffer<ConnectedEdge>(node)) continue;
                DynamicBuffer<ConnectedEdge> edges = em.GetBuffer<ConnectedEdge>(node, true);
                for (int i = 0; i < edges.Length; i++)
                {
                    Entity adjacent = edges[i].m_Edge;
                    if (adjacent != Entity.Null && em.Exists(adjacent) && !em.HasComponent<Deleted>(adjacent) && !submitted.Contains(adjacent))
                        toUpdate.Add(adjacent);
                }
            }

            return new DeletionPlan(toDelete, submitted, toUpdate, affected, structuralAffected);
        }

        internal static List<Entity> GetApprovedNodes(EntityManager em, DeletionPlan approved)
        {
            var nodes = new List<Entity>();
            List<Entity> ordered = EntityOrder.Sorted(approved.Submitted);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (em.HasComponent<Game.Net.Node>(ordered[i])) nodes.Add(ordered[i]);
            }
            return nodes;
        }

        /// <summary>
        /// Groups the plan into facilities. A member is owned by the root it hangs off through
        /// Attached/Owner; anything that cannot be traced back to a root becomes its own unit, so a
        /// loose object is still never reported as part of somebody else's building.
        /// </summary>
        internal static List<DeletionUnit> BuildUnits(EntityManager em, DeletionPlan plan)
        {
            var units = new Dictionary<Entity, DeletionUnit>();
            if (plan != null && plan.Submitted.Count > 0)
            {
                var roots = new HashSet<Entity>(plan.Roots);
                Dictionary<Entity, Entity> parentOf = BuildParentIndex(em, plan);
                List<Entity> ordered = EntityOrder.Sorted(plan.Submitted);
                for (int i = 0; i < ordered.Count; i++)
                {
                    Entity entity = ordered[i];
                    Entity root = FindUnitRoot(em, entity, roots, parentOf);
                    if (!units.TryGetValue(root, out DeletionUnit unit))
                    {
                        unit = new DeletionUnit(root);
                        units.Add(root, unit);
                    }
                    unit.Members.Add(entity);
                }
            }

            var result = new List<DeletionUnit>(units.Count);
            foreach (var pair in units) result.Add(pair.Value);
            result.Sort((a, b) => EntityOrder.ByIndex.Compare(a.Root, b.Root));
            if (plan != null) plan.AdoptUnits(result);
            return result;
        }

        /// <summary>
        /// Child-to-parent index. Most facility relations are stored on the parent (an installed
        /// upgrade, a sub-object, a sub-network), so grouping needs the reverse edge as well.
        /// First parent in sorted order wins, which keeps the index deterministic when a child is
        /// listed twice.
        /// </summary>
        private static Dictionary<Entity, Entity> BuildParentIndex(EntityManager em, DeletionPlan plan)
        {
            var parentOf = new Dictionary<Entity, Entity>();
            var parents = new HashSet<Entity>(plan.Roots);
            parents.UnionWith(plan.Submitted);
            List<Entity> ordered = EntityOrder.Sorted(parents);
            for (int i = 0; i < ordered.Count; i++)
            {
                Entity parent = ordered[i];
                if (!em.Exists(parent)) continue;

                if (em.HasBuffer<InstalledUpgrade>(parent))
                {
                    var upgrades = em.GetBuffer<InstalledUpgrade>(parent, true);
                    for (int j = 0; j < upgrades.Length; j++) Link(parentOf, upgrades[j].m_Upgrade, parent);
                }
                if (em.HasBuffer<SubObject>(parent))
                {
                    var subObjects = em.GetBuffer<SubObject>(parent, true);
                    for (int j = 0; j < subObjects.Length; j++) Link(parentOf, subObjects[j].m_SubObject, parent);
                }
                if (em.HasBuffer<SubArea>(parent))
                {
                    var subAreas = em.GetBuffer<SubArea>(parent, true);
                    for (int j = 0; j < subAreas.Length; j++) Link(parentOf, subAreas[j].m_Area, parent);
                }
                if (em.HasBuffer<SubLane>(parent))
                {
                    var subLanes = em.GetBuffer<SubLane>(parent, true);
                    for (int j = 0; j < subLanes.Length; j++) Link(parentOf, subLanes[j].m_SubLane, parent);
                }
                if (em.HasBuffer<SubNet>(parent))
                {
                    var subNets = em.GetBuffer<SubNet>(parent, true);
                    for (int j = 0; j < subNets.Length; j++) Link(parentOf, subNets[j].m_SubNet, parent);
                }
            }
            return parentOf;
        }

        private static void Link(Dictionary<Entity, Entity> parentOf, Entity child, Entity parent)
        {
            if (child == Entity.Null || child == parent || parentOf.ContainsKey(child)) return;
            parentOf.Add(child, parent);
        }

        private static Entity FindUnitRoot(
            EntityManager em,
            Entity entity,
            HashSet<Entity> roots,
            Dictionary<Entity, Entity> parentOf)
        {
            if (roots.Contains(entity)) return entity;

            Entity current = entity;
            for (int depth = 0; depth < 4; depth++)
            {
                Entity parent = Entity.Null;
                if (em.HasComponent<Attached>(current))
                {
                    parent = em.GetComponentData<Attached>(current).m_Parent;
                }
                if ((parent == Entity.Null || !em.Exists(parent)) && em.HasComponent<Owner>(current))
                {
                    parent = em.GetComponentData<Owner>(current).m_Owner;
                }
                if (parent == Entity.Null || !em.Exists(parent))
                {
                    parentOf.TryGetValue(current, out parent);
                }
                if (parent == Entity.Null || !em.Exists(parent) || parent == current)
                {
                    break;
                }
                if (roots.Contains(parent))
                {
                    return parent;
                }
                current = parent;
            }

            return entity;
        }

        internal static void Submit(EntityManager em, EntityCommandBuffer commands, DeletionPlan plan, out int updatedCount)
        {
            List<Entity> submittedOrder = EntityOrder.Sorted(plan.Submitted);
            for (int i = 0; i < submittedOrder.Count; i++)
            {
                Entity entity = submittedOrder[i];
                if (entity == Entity.Null || !em.Exists(entity) || em.HasComponent<Deleted>(entity))
                {
                    continue;
                }
                commands.RemoveComponent<Applied>(entity);
                commands.RemoveComponent<Created>(entity);
                commands.RemoveComponent<Updated>(entity);
                commands.AddComponent<Deleted>(entity);
            }

            updatedCount = 0;
            List<Entity> neighborOrder = EntityOrder.Sorted(plan.Neighbors);
            for (int i = 0; i < neighborOrder.Count; i++)
            {
                Entity entity = neighborOrder[i];
                if (entity == Entity.Null || !em.Exists(entity) || plan.Submitted.Contains(entity)
                    || em.HasComponent<Deleted>(entity) || em.HasComponent<Updated>(entity))
                    continue;

                commands.AddComponent<Updated>(entity);
                updatedCount++;
            }
        }

        private static HashSet<Entity> EstimateAffected(
            EntityManager em,
            HashSet<Entity> submitted,
            List<Entity> selectedNodes,
            out HashSet<Entity> structuralAffected)
        {
            var toDelete = new HashSet<Entity>(submitted);
            var nodeCandidates = new Dictionary<Entity, bool>(selectedNodes.Count);
            foreach (Entity node in selectedNodes) nodeCandidates.Add(node, true);
            structuralAffected = new HashSet<Entity>();
            var queue = new Queue<(Entity Entity, bool Structural)>(submitted.Count);
            List<Entity> seedOrder = EntityOrder.Sorted(submitted);
            for (int i = 0; i < seedOrder.Count; i++)
            {
                Entity root = seedOrder[i];
                bool structural = !em.HasComponent<Moving>(root) && !em.HasComponent<Vehicle>(root) &&
                                  !em.HasComponent<Creature>(root);
                if (structural) structuralAffected.Add(root);
                queue.Enqueue((root, structural));
            }

            while (queue.Count > 0 || AdmitDisconnectedNodes(em, nodeCandidates, toDelete, structuralAffected, queue))
            {
                var entry = queue.Dequeue();
                Entity current = entry.Entity;
                bool isStructural = entry.Structural;
                if (current == Entity.Null || !em.Exists(current))
                {
                    continue;
                }

                if (em.HasBuffer<LayoutElement>(current))
                {
                    DynamicBuffer<LayoutElement> layout = em.GetBuffer<LayoutElement>(current, true);
                    for (int i = 0; i < layout.Length; i++)
                    {
                        EnqueueAffected(em, layout[i].m_Vehicle, isStructural, toDelete, structuralAffected, queue);
                    }
                }

                // SubElementDeleteSystem also deletes the owner's routes and vehicles.
                // Track them for total impact count, but mark them volatile so unpaused traffic churn
                // does not repeatedly invalidate pending confirmation.
                if (em.HasBuffer<SubRoute>(current))
                {
                    var routes = em.GetBuffer<SubRoute>(current, true);
                    for (int i = 0; i < routes.Length; i++)
                        EnqueueAffected(em, routes[i].m_Route, false, toDelete, structuralAffected, queue);
                }
                if (em.HasBuffer<OwnedVehicle>(current))
                {
                    var vehicles = em.GetBuffer<OwnedVehicle>(current, true);
                    for (int i = 0; i < vehicles.Length; i++)
                        EnqueueAffected(em, vehicles[i].m_Vehicle, false, toDelete, structuralAffected, queue);
                }

                if (em.HasBuffer<InstalledUpgrade>(current))
                {
                    DynamicBuffer<InstalledUpgrade> upgrades = em.GetBuffer<InstalledUpgrade>(current, true);
                    for (int i = 0; i < upgrades.Length; i++)
                    {
                        EnqueueAffected(em, upgrades[i].m_Upgrade, isStructural, toDelete, structuralAffected, queue);
                    }
                }

                if (em.HasBuffer<SubObject>(current))
                {
                    DynamicBuffer<SubObject> subObjects = em.GetBuffer<SubObject>(current, true);
                    for (int i = 0; i < subObjects.Length; i++)
                    {
                        EnqueueAffected(em, subObjects[i].m_SubObject, isStructural, toDelete, structuralAffected, queue);
                    }
                }

                if (em.HasBuffer<SubArea>(current))
                {
                    DynamicBuffer<SubArea> subAreas = em.GetBuffer<SubArea>(current, true);
                    for (int i = 0; i < subAreas.Length; i++)
                    {
                        EnqueueAffected(em, subAreas[i].m_Area, isStructural, toDelete, structuralAffected, queue);
                    }
                }

                if (em.HasBuffer<SubLane>(current))
                {
                    DynamicBuffer<SubLane> subLanes = em.GetBuffer<SubLane>(current, true);
                    for (int i = 0; i < subLanes.Length; i++)
                    {
                        EnqueueAffected(em, subLanes[i].m_SubLane, isStructural, toDelete, structuralAffected, queue);
                    }
                }

                if (em.HasBuffer<SubNet>(current))
                {
                    DynamicBuffer<SubNet> subNets = em.GetBuffer<SubNet>(current, true);
                    for (int i = 0; i < subNets.Length; i++)
                    {
                        Entity subNet = subNets[i].m_SubNet;
                        if (subNet == Entity.Null || !em.Exists(subNet) || em.HasComponent<Deleted>(subNet))
                        {
                            continue;
                        }

                        if (em.HasComponent<Game.Net.Node>(subNet))
                        {
                            nodeCandidates.TryGetValue(subNet, out bool alreadyStructural);
                            nodeCandidates[subNet] = alreadyStructural || isStructural;
                        }
                        else
                        {
                            EnqueueAffected(em, subNet, isStructural, toDelete, structuralAffected, queue);
                        }
                    }
                }

            }

            return toDelete;
        }

        // Check nodes after the affected edges for this pass are known. An admitted node can add
        // another dependency, so repeat only if admitting a node added work.
        private static bool AdmitDisconnectedNodes(
            EntityManager em,
            Dictionary<Entity, bool> candidates,
            HashSet<Entity> affected,
            HashSet<Entity> structuralAffected,
            Queue<(Entity Entity, bool Structural)> queue)
        {
            // Sorted so an admission that changes what the next node may pull in cannot depend on
            // dictionary order: the same graph always admits the same nodes in the same order.
            var order = new List<Entity>(candidates.Count);
            foreach (var candidate in candidates) order.Add(candidate.Key);
            order.Sort(EntityOrder.ByIndex);
            for (int i = 0; i < order.Count; i++)
            {
                Entity node = order[i];
                bool structural = candidates[node];
                if ((!affected.Contains(node) || (structural && !structuralAffected.Contains(node))) &&
                    CanSafelyDeleteNode(em, node, affected))
                {
                    EnqueueAffected(em, node, structural, affected, structuralAffected, queue);
                }
            }
            return queue.Count > 0;
        }

        private static void EnqueueAffected(
            EntityManager em,
            Entity entity,
            bool structural,
            HashSet<Entity> toDelete,
            HashSet<Entity> structuralAffected,
            Queue<(Entity Entity, bool Structural)> queue)
        {
            if (entity == Entity.Null || !em.Exists(entity) || em.HasComponent<Deleted>(entity))
            {
                return;
            }

            bool addedToDelete = toDelete.Add(entity);
            bool promotedToStructural = structural && structuralAffected.Add(entity);
            if (addedToDelete || promotedToStructural)
            {
                queue.Enqueue((entity, structural));
            }
        }

        private static Entity ResolveController(EntityManager em, Entity root)
        {
            if (root != Entity.Null && em.Exists(root) && em.HasComponent<Controller>(root))
            {
                Entity controller = em.GetComponentData<Controller>(root).m_Controller;
                if (controller != Entity.Null && em.Exists(controller) && !em.HasComponent<Deleted>(controller)) return controller;
            }
            return root;
        }

        private static void TryEnqueueInitialRoot(
            EntityManager em,
            Entity root,
            HashSet<Entity> toDelete,
            Queue<Entity> queue)
        {
            if (root == Entity.Null || !em.Exists(root) || em.HasComponent<Deleted>(root))
            {
                return;
            }

            root = ResolveController(em, root);

            if (toDelete.Add(root))
            {
                queue.Enqueue(root);
            }
        }

        private static void EnqueueIfEligible(
            EntityManager em,
            Entity entity,
            HashSet<Entity> toDelete,
            Queue<Entity> queue)
        {
            if (entity == Entity.Null || !em.Exists(entity) || em.HasComponent<Deleted>(entity))
            {
                return;
            }

            if (toDelete.Add(entity))
            {
                queue.Enqueue(entity);
            }
        }

        private static bool CanSafelyDeleteNode(
            EntityManager em,
            Entity node,
            HashSet<Entity> toDelete)
        {
            if (!em.HasBuffer<ConnectedEdge>(node))
            {
                return true;
            }

            DynamicBuffer<ConnectedEdge> edges = em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < edges.Length; i++)
            {
                Entity edge = edges[i].m_Edge;
                if (edge == Entity.Null || !em.Exists(edge))
                {
                    continue;
                }

                if (em.HasComponent<Deleted>(edge) || toDelete.Contains(edge))
                {
                    continue;
                }

                if (em.HasComponent<Edge>(edge))
                {
                    Edge edgeComp = em.GetComponentData<Edge>(edge);
                    if (edgeComp.m_Start == node || edgeComp.m_End == node)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

    }
}
