using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Game.Common;
using Game.Net;
using MarqueeDelete;

// Offline ECS stand-ins exercise planning and command submission, not Unity playback.
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
    public class DynamicBuffer<T> : List<T> { public int Length => Count; }
    public class EntityManager
    {
        private readonly Dictionary<Entity, Dictionary<Type, object>> data = new();
        public Entity Make(int id) { var e = new Entity { Index = id }; data[e] = new(); return e; }
        public bool Exists(Entity e) => data.ContainsKey(e);
        public bool HasComponent<T>(Entity e) => Exists(e) && data[e].ContainsKey(typeof(T));
        public T GetComponentData<T>(Entity e) => (T)data[e][typeof(T)];
        public void Set<T>(Entity e, T value) => data[e][typeof(T)] = value;
        public bool HasBuffer<T>(Entity e) => HasComponent<DynamicBuffer<T>>(e);
        public DynamicBuffer<T> GetBuffer<T>(Entity e, bool isReadOnly = false) => GetComponentData<DynamicBuffer<T>>(e);
        public void Buffer<T>(Entity e, params T[] values) { var b = new DynamicBuffer<T>(); b.AddRange(values); Set(e, b); }
    }
    public class EntityCommandBuffer
    {
        public readonly HashSet<Entity> Deleted = new();
        public readonly HashSet<Entity> Updated = new();
        public readonly HashSet<Entity> Detached = new();
        // Ordered recordings, so tests can assert that a shuffled selection emits the same commands.
        public readonly List<Entity> DeleteOrder = new();
        public readonly List<Entity> UpdateOrder = new();
        public void AddComponent<T>(Entity e)
        {
            if (typeof(T) == typeof(Game.Common.Deleted)) { Deleted.Add(e); DeleteOrder.Add(e); }
            if (typeof(T) == typeof(Game.Common.Updated)) { Updated.Add(e); UpdateOrder.Add(e); }
        }
        public void RemoveComponent<T>(Entity e) { if (typeof(T) == typeof(Owner)) Detached.Add(e); }
    }
}
namespace Game.Common { public struct Deleted {} public struct Updated {} public struct Applied {} public struct Created {} public struct Owner { public Entity m_Owner; } }
namespace Game.Net { public struct Node {} public struct Edge { public Entity m_Start, m_End; } public struct ConnectedEdge { public Entity m_Edge; } public struct SubNet { public Entity m_SubNet; } public struct SubLane { public Entity m_SubLane; } }
namespace Game.Objects { public struct Moving {} public struct Attached { public Entity m_Parent; } public struct SubObject { public Entity m_SubObject; } }
namespace Game.Areas { public struct SubArea { public Entity m_Area; } }
namespace Game.Buildings { public struct InstalledUpgrade { public Entity m_Upgrade; } }
namespace Game.Routes { public struct SubRoute { public Entity m_Route; } public struct Route {} }
namespace Game.Creatures { public struct Creature {} }
namespace Game.Vehicles { public struct Vehicle {} public struct OwnedVehicle { public Entity m_Vehicle; } public struct Controller { public Entity m_Controller; } public struct LayoutElement { public Entity m_Vehicle; } }

// Symbols that Compatibility.cs looks up by name. They exist here so one offline run can reach the
// "names resolve, build fingerprint does not match" branch, which is the preview-only tier. The
// search and UI groups are deliberately left incomplete: that is the missing-symbol branch.
namespace Game.Buildings { public struct Signature {} }
namespace Game.Objects { public struct UniqueObject {} public struct UnderConstruction {} }
namespace Game.Prefabs { public struct PrefabRef { public Entity m_Prefab; } }
namespace Game.Net { public struct EdgeGeometry {} }

class DeletionRegression
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static void Main()
    {
        var em = new EntityManager();
        var road = em.Make(1); var node = em.Make(2); var end = em.Make(3);
        var liveRoad = em.Make(4); var lane = em.Make(5); var subNet = em.Make(6);
        em.Set(node, new Node()); em.Set(end, new Node());
        em.Set(road, new Edge { m_Start = node, m_End = end });
        em.Set(liveRoad, new Edge { m_Start = node, m_End = end });
        em.Buffer(node, new ConnectedEdge { m_Edge = road }, new ConnectedEdge { m_Edge = liveRoad });
        em.Buffer(road, new SubLane { m_SubLane = lane });
        em.Buffer(road, new SubNet { m_SubNet = subNet });
        var commands = new EntityCommandBuffer();
        var roadPlan = Cascade.Plan(em, new[] { road, node, road, Entity.Null });
        Cascade.Submit(em, commands, roadPlan, out _);
        Check(roadPlan.Submitted.Count == 3 && commands.Deleted.SetEquals(new[] { road, lane, subNet }), "Eligible roots and affected sub-elements must be submitted");
        Check(!em.HasComponent<Deleted>(road), "Planning must not mutate EntityManager");
        Check(commands.Deleted.Contains(lane) && commands.Deleted.Contains(subNet), "Lane and subnetwork must be submitted to prevent orphaned descendants");
        Check(commands.Updated.Contains(node) && commands.Updated.Contains(end) && commands.Updated.Contains(liveRoad), "Surviving junctions and adjacent roads must rebuild");
        Check(commands.Detached.Count == 0, "Keep ownership for PostTool processing");
        Check(roadPlan.Affected.SetEquals(new[] { road, lane, subNet }), "Safety estimate still includes descendants");
        em.Set(liveRoad, new Deleted());
        var junctionNowEligible = Cascade.Plan(em, new[] { road, node });
        Check(junctionNowEligible.Submitted.Contains(node) && junctionNowEligible.Expands(roadPlan),
            "A junction that no longer has a surviving connecting edge requires review");
        var owner = em.Make(7); var trailer = em.Make(8);
        em.Set(trailer, new Game.Vehicles.Controller { m_Controller = owner });
        commands = new EntityCommandBuffer();
        var initialVehiclePlan = Cascade.Plan(em, new[] { trailer, owner });
        Cascade.Submit(em, commands, initialVehiclePlan, out _);
        Check(commands.Deleted.SetEquals(new[] { owner }), "Vehicle controller deduplicates");
        em.Buffer(owner, new Game.Vehicles.LayoutElement { m_Vehicle = trailer });
        var mounted = Cascade.Plan(em, new[] { owner });
        Check(mounted.Expands(initialVehiclePlan), "A new carriage requires review");
        Check(!initialVehiclePlan.Expands(mounted), "A detached carriage does not expand the selection");
        var nextTrailer = em.Make(9);
        em.Buffer(owner, new Game.Vehicles.LayoutElement { m_Vehicle = nextTrailer });
        var replaced = Cascade.Plan(em, new[] { owner });
        Check(replaced.Affected.Count == mounted.Affected.Count && replaced.Expands(mounted),
            "A different carriage at the same count requires review");
        var depot = em.Make(10);
        var route = em.Make(11);
        em.Set(route, new Game.Routes.Route());
        em.Buffer(depot, new Game.Routes.SubRoute { m_Route = route }, new Game.Routes.SubRoute { m_Route = route });
        em.Buffer(depot, new Game.Vehicles.OwnedVehicle { m_Vehicle = owner });
        var depotPlan = Cascade.Plan(em, new[] { depot });
        Check(depotPlan.Affected.SetEquals(new[] { depot, route, owner, nextTrailer }),
            "Native route and vehicle cleanup, including carriages, must be counted once");
        commands = new EntityCommandBuffer();
        Cascade.Submit(em, commands, depotPlan, out _);
        Check(commands.Deleted.SetEquals(new[] { depot }), "Native routes and owned vehicles are left to the game");
        var newRoute = em.Make(12);
        em.Buffer(depot, new Game.Routes.SubRoute { m_Route = newRoute });
        var depotWithNewRoute = Cascade.Plan(em, new[] { depot });
        Check(depotWithNewRoute.Expands(depotPlan), "Affected set expands on new route");
        Check(!depotWithNewRoute.ExpandsConfirmation(depotPlan), "Transient SubRoute spawn must not block pending confirmation");

        var newBus = em.Make(13);
        em.Buffer(depot, new Game.Vehicles.OwnedVehicle { m_Vehicle = newBus });
        var depotWithNewVehicle = Cascade.Plan(em, new[] { depot });
        Check(!depotWithNewVehicle.ExpandsConfirmation(depotPlan), "Transient OwnedVehicle dispatch must not block pending confirmation");

        var upgrade = em.Make(14);
        em.Buffer(depot, new Game.Buildings.InstalledUpgrade { m_Upgrade = upgrade });
        var depotWithUpgrade = Cascade.Plan(em, new[] { depot });
        Check(depotWithUpgrade.ExpandsConfirmation(depotPlan), "New structural InstalledUpgrade requires confirmation review");

        Check(!depotWithNewVehicle.ChangesConfirmation(depotWithNewRoute), "Volatile traffic changes must not require structural review");
        Check(depotPlan.ChangesConfirmation(depotWithUpgrade), "Removed structural upgrades require review");
        Check(replaced.ChangesConfirmation(mounted), "Equal-count structural replacement requires review");
        Check(!depotPlan.ChangesConfirmation(depotPlan), "Unchanged structural selection needs no review");
        var passingVehicle = em.Make(91);
        var passingCitizen = em.Make(92);
        em.Set(passingVehicle, new Game.Vehicles.Vehicle());
        em.Set(passingCitizen, new Game.Creatures.Creature());
        var mixedSelection = Cascade.Plan(em, new[] { depot, passingVehicle, passingCitizen });
        var vehiclesMovedAway = Cascade.Plan(em, new[] { depot });
        Check(!vehiclesMovedAway.ChangesConfirmation(mixedSelection),
            "Selected moving roots leaving the box do not invalidate important-building confirmation");
        Check(mixedSelection.ChangesConfirmation(vehiclesMovedAway),
            "New direct moving roots were never approved and require review");
        Check(Cascade.Plan(em, new[] { passingVehicle, passingCitizen }).ChangesConfirmation(mixedSelection),
            "Removal of a structural root still requires review");

        commands = new EntityCommandBuffer();
        Cascade.Submit(em, commands, Cascade.Plan(em, Array.Empty<Entity>()), out int updated);
        Check(commands.Deleted.Count == 0 && updated == 0, "Empty selection no-op");

        // Baseline + PlanExcluding parity when no entities are excluded.
        var normalBld = em.Make(15);
        var importantBld = em.Make(16);
        var importantSubArea = em.Make(17);
        var importantUpgrade = em.Make(18);
        em.Buffer(importantBld, new Game.Buildings.InstalledUpgrade { m_Upgrade = importantUpgrade });
        em.Buffer(importantBld, new Game.Areas.SubArea { m_Area = importantSubArea });
        var withImportant = Cascade.PlanExcluding(em, new List<Entity> { normalBld, importantBld }, new HashSet<Entity>());
        var directImportant = Cascade.Plan(em, new[] { normalBld, importantBld });
        Check(withImportant.Submitted.SetEquals(directImportant.Submitted) &&
              withImportant.Affected.SetEquals(directImportant.Affected) &&
              withImportant.Neighbors.SetEquals(directImportant.Neighbors) &&
              withImportant.Affected.Contains(importantUpgrade) &&
              withImportant.Affected.Contains(importantSubArea),
            "PlanExcluding with empty exclusion set matches normal Cascade.Plan exactly");

        // Flow 1: Exclude important building I -> filter change adds indirect roots R (InstalledUpgrade / SubObject / OwnedVehicle) -> recompute keeps I out of Affected.
        var manuallyExcluded = new HashSet<Entity> { importantBld };
        var culled = new HashSet<Entity>();
        var afterExclude = Cascade.PlanExcluding(
            em, new List<Entity> { normalBld, importantBld }, manuallyExcluded, culled);
        Check(culled.SetEquals(new[] { importantBld }) &&
              afterExclude.Submitted.SetEquals(new[] { normalBld }) &&
              !afterExclude.Affected.Contains(importantBld) &&
              !afterExclude.Affected.Contains(importantUpgrade) &&
              !afterExclude.Affected.Contains(importantSubArea),
            "Direct exclusion removes important building I and its cascade children");

        var indirectUpgradeRoot = em.Make(19);
        em.Buffer(indirectUpgradeRoot, new Game.Buildings.InstalledUpgrade { m_Upgrade = importantBld });
        var indirectSubObjRoot = em.Make(20);
        em.Buffer(indirectSubObjRoot, new Game.Objects.SubObject { m_SubObject = importantBld });
        var indirectVehicleOwner = em.Make(21);
        em.Buffer(indirectVehicleOwner, new Game.Vehicles.OwnedVehicle { m_Vehicle = importantBld });

        culled.Clear();
        var afterFilterChange = Cascade.PlanExcluding(
            em,
            new List<Entity> { normalBld, indirectUpgradeRoot, indirectSubObjRoot, indirectVehicleOwner },
            manuallyExcluded,
            culled);
        Check(culled.SetEquals(new[] { indirectUpgradeRoot, indirectSubObjRoot, indirectVehicleOwner }) &&
              afterFilterChange.Submitted.SetEquals(new[] { normalBld }) &&
              !afterFilterChange.Affected.Contains(importantBld) &&
              !afterFilterChange.Affected.Contains(importantUpgrade) &&
              !afterFilterChange.Affected.Contains(importantSubArea),
            "Filter-change recompute culls indirect roots referencing excluded I and keeps I out of Affected");

        // Flow 2: Standalone manual exclusion persists across re-collection/re-evaluation (direct + joint-root cascade).
        var manualEntity = em.Make(22);
        var manualSet = new HashSet<Entity> { manualEntity };
        culled.Clear();
        var afterManualRecompute = Cascade.PlanExcluding(
            em, new List<Entity> { normalBld, manualEntity }, manualSet, culled);
        Check(culled.SetEquals(new[] { manualEntity }) &&
              afterManualRecompute.Submitted.SetEquals(new[] { normalBld }) &&
              !afterManualRecompute.Affected.Contains(manualEntity),
            "Standalone manual exclusion remains excluded across re-computation");

        // Joint-root fallback: neither ownerBld (with SubNet) nor externalEdge alone deletes sharedNode, only both together do; sharedNode in ManuallyExcluded must still hold.
        var sharedNode = em.Make(23);
        var ownerBld = em.Make(24);
        var externalEdge = em.Make(25);
        em.Set(sharedNode, new Game.Net.Node());
        em.Set(externalEdge, new Edge { m_Start = sharedNode, m_End = Entity.Null });
        em.Buffer(sharedNode, new ConnectedEdge { m_Edge = externalEdge });
        em.Buffer(ownerBld, new Game.Net.SubNet { m_SubNet = sharedNode });
        Check(!Cascade.Plan(em, new[] { ownerBld }).Affected.Contains(sharedNode) &&
              !Cascade.Plan(em, new[] { externalEdge }).Affected.Contains(sharedNode) &&
              Cascade.Plan(em, new[] { ownerBld, externalEdge }).Affected.Contains(sharedNode),
            "Test setup verifies sharedNode is only deleted when ownerBld and externalEdge are deleted together");
        culled.Clear();
        var jointPlan = Cascade.PlanExcluding(
            em, new List<Entity> { normalBld, ownerBld, externalEdge }, new HashSet<Entity> { sharedNode }, culled);
        Check(!jointPlan.Affected.Contains(sharedNode) &&
              jointPlan.Submitted.Contains(normalBld) &&
              culled.Count == 1,
            "Joint-root cascade pulling an excluded node is culled without looping infinitely");

        // Multi-round joint cases suggested by review:
        // 1) Three roots required together to pull excluded 3-way node
        var triNode = em.Make(26);
        var triOwner = em.Make(27);
        var triEdge1 = em.Make(28);
        var triEdge2 = em.Make(29);
        em.Set(triNode, new Game.Net.Node());
        em.Set(triEdge1, new Edge { m_Start = triNode, m_End = Entity.Null });
        em.Set(triEdge2, new Edge { m_Start = triNode, m_End = Entity.Null });
        em.Buffer(triNode, new ConnectedEdge { m_Edge = triEdge1 }, new ConnectedEdge { m_Edge = triEdge2 });
        em.Buffer(triOwner, new Game.Net.SubNet { m_SubNet = triNode });
        Check(!Cascade.Plan(em, new[] { triOwner, triEdge1 }).Affected.Contains(triNode) &&
              !Cascade.Plan(em, new[] { triOwner, triEdge2 }).Affected.Contains(triNode) &&
              Cascade.Plan(em, new[] { triOwner, triEdge1, triEdge2 }).Affected.Contains(triNode),
            "Three-root setup requires all three roots together to pull triNode");
        culled.Clear();
        var triPlan = Cascade.PlanExcluding(
            em, new List<Entity> { normalBld, triOwner, triEdge1, triEdge2 }, new HashSet<Entity> { triNode }, culled);
        Check(!triPlan.Affected.Contains(triNode) &&
              triPlan.Submitted.Contains(normalBld) &&
              culled.Count == 1,
            "Three-root joint cascade culls culprit cleanly and spares triNode");

        // 2) Two independent joint conflict groups resolved iteratively in multiple rounds
        var groupNode2 = em.Make(30);
        var groupOwner2 = em.Make(31);
        var groupEdge2 = em.Make(32);
        em.Set(groupNode2, new Game.Net.Node());
        em.Set(groupEdge2, new Edge { m_Start = groupNode2, m_End = Entity.Null });
        em.Buffer(groupNode2, new ConnectedEdge { m_Edge = groupEdge2 });
        em.Buffer(groupOwner2, new Game.Net.SubNet { m_SubNet = groupNode2 });
        culled.Clear();
        var twoGroupPlan = Cascade.PlanExcluding(
            em,
            new List<Entity> { normalBld, triOwner, triEdge1, triEdge2, groupOwner2, groupEdge2 },
            new HashSet<Entity> { triNode, groupNode2 },
            culled);
        Check(!twoGroupPlan.Affected.Contains(triNode) &&
              !twoGroupPlan.Affected.Contains(groupNode2) &&
              twoGroupPlan.Submitted.Contains(normalBld) &&
              culled.Count == 2,
            "Two independent joint conflict groups converge across rounds");

        // 3) New sub-edges expanding toUpdate neighbor set and separating Roots from Submitted
        var bldWithSubNet = em.Make(33);
        var subEdge = em.Make(34);
        var internalNode = em.Make(35);
        var externalNode = em.Make(36);
        var externalRoad = em.Make(37);
        em.Set(internalNode, new Game.Net.Node());
        em.Set(externalNode, new Game.Net.Node());
        em.Set(subEdge, new Edge { m_Start = internalNode, m_End = externalNode });
        em.Set(externalRoad, new Edge { m_Start = externalNode, m_End = Entity.Null });
        em.Buffer(externalNode, new ConnectedEdge { m_Edge = subEdge }, new ConnectedEdge { m_Edge = externalRoad });
        em.Buffer(bldWithSubNet, new Game.Net.SubNet { m_SubNet = subEdge });

        var bldPlan = Cascade.Plan(em, new[] { bldWithSubNet });
        Check(bldPlan.Roots.SetEquals(new[] { bldWithSubNet }), "bldPlan.Roots contains only the root building");
        Check(bldPlan.Submitted.Contains(bldWithSubNet) && bldPlan.Submitted.Contains(subEdge), "bldPlan.Submitted contains root building and subEdge");
        Check(bldPlan.Neighbors.Contains(externalNode), "externalNode must be in Neighbors for update");
        Check(bldPlan.Neighbors.Contains(externalRoad), "externalRoad connected to externalNode must be in Neighbors for update");

        var bldCommands = new EntityCommandBuffer();
        Cascade.Submit(em, bldCommands, bldPlan, out int bldUpdatedCount);
        Check(bldCommands.Deleted.Contains(bldWithSubNet) && bldCommands.Deleted.Contains(subEdge), "Sub-edge and building must be marked Deleted");
        Check(bldCommands.Updated.Contains(externalNode) && bldCommands.Updated.Contains(externalRoad), "External node and road must receive Updated");
        Check(bldUpdatedCount >= 2, "Updated count includes surviving neighbors");

        // 4) Roots vs Submitted separation prevents double capture of sub-edges
        Check(bldPlan.Roots.Count == 1 && bldPlan.Roots.Contains(bldWithSubNet), "Only root building is in Roots");
        Check(bldPlan.Submitted.Count == 2 && bldPlan.Submitted.Contains(subEdge), "Submitted contains both building and sub-edge for clean engine demolition");

        var firstOwner = em.Make(38);
        var secondOwner = em.Make(39);
        var junction = em.Make(40);
        var ownedEdge = em.Make(41);
        em.Set(junction, new Node());
        em.Set(ownedEdge, new Edge { m_Start = junction });
        em.Set(ownedEdge, new Owner { m_Owner = secondOwner });
        em.Buffer(junction, new ConnectedEdge { m_Edge = ownedEdge });
        em.Buffer(firstOwner, new SubNet { m_SubNet = junction });
        em.Buffer(secondOwner, new SubNet { m_SubNet = ownedEdge });
        var wholePlan = Cascade.Plan(em, new[] { firstOwner, secondOwner });
        var deferred = Cascade.GetApprovedNodes(em, wholePlan);
        Check(deferred.Count == 1 && deferred.Contains(junction) && !wholePlan.Roots.Contains(junction),
            "Batch cleanup must include a junction reached only through the full cascade");
        var firstPlan = Cascade.Plan(em, new[] { firstOwner });
        var secondPlan = Cascade.Plan(em, new[] { secondOwner });
        Check(!firstPlan.Submitted.Contains(junction) && !secondPlan.Submitted.Contains(junction),
            "Splitting the owners alone leaves the junction out of both submissions");
        Check(!Cascade.Plan(em, deferred).Submitted.Contains(junction),
            "The approved junction must not be removed before its last live edge");
        em.Set(ownedEdge, new Deleted());
        var cleanup = Cascade.Plan(em, deferred);
        Check(cleanup.Submitted.Contains(junction) && !cleanup.ExpandsConfirmation(wholePlan),
            "The deferred approved junction becomes eligible once the connected edge is deleted");
        var unapprovedEdge = em.Make(42);
        em.Set(unapprovedEdge, new Edge { m_End = junction });
        em.Buffer(junction, new ConnectedEdge { m_Edge = ownedEdge }, new ConnectedEdge { m_Edge = unapprovedEdge });
        Check(!Cascade.Plan(em, deferred).Submitted.Contains(junction),
            "A new live connection keeps the approved junction protected");
        Check(Cascade.GetApprovedNodes(em, Cascade.PlanExcluding(em,
                new List<Entity> { firstOwner, secondOwner }, new HashSet<Entity> { junction })).Count == 0,
            "A manually excluded junction cannot reappear in the deferred cleanup list");

        var nodeHolder = em.Make(101);
        var buildingHolder = em.Make(102);
        var lateUpgrade = em.Make(103);
        var dependencyNode = em.Make(104);
        var dependencyEdge = em.Make(105);
        em.Set(dependencyNode, new Node());
        em.Set(dependencyEdge, new Edge { m_Start = dependencyNode });
        em.Set(dependencyEdge, new Owner { m_Owner = lateUpgrade });
        em.Buffer(dependencyNode, new ConnectedEdge { m_Edge = dependencyEdge });
        em.Buffer(nodeHolder, new SubNet { m_SubNet = dependencyNode });
        em.Buffer(buildingHolder, new Game.Buildings.InstalledUpgrade { m_Upgrade = lateUpgrade });
        em.Buffer(lateUpgrade, new SubNet { m_SubNet = dependencyEdge });

        var orderA = Cascade.Plan(em, new[] { nodeHolder, buildingHolder });
        var orderB = Cascade.Plan(em, new[] { buildingHolder, nodeHolder });
        Check(orderA.Submitted.SetEquals(orderB.Submitted) && orderA.Affected.SetEquals(orderB.Affected),
            "Changing root order cannot change the demolition plan");
        Check(orderA.Submitted.Contains(dependencyNode) &&
              Cascade.GetApprovedNodes(em, orderA).Contains(dependencyNode),
            "A shared junction admitted after finding all edges can be reconciled during batching");

        var selectedEndpoint = Cascade.Plan(em, new[] { dependencyNode, buildingHolder });
        Check(selectedEndpoint.Submitted.Contains(dependencyNode) && selectedEndpoint.Roots.Contains(dependencyNode),
            "A directly selected junction is evaluated after resolving nested edges");
        var survivingEdge = em.Make(106);
        em.Set(survivingEdge, new Edge { m_End = dependencyNode });
        em.Buffer(dependencyNode, new ConnectedEdge { m_Edge = dependencyEdge }, new ConnectedEdge { m_Edge = survivingEdge });
        var guarded = Cascade.Plan(em, new[] { nodeHolder, buildingHolder });
        Check(!guarded.Submitted.Contains(dependencyNode) && guarded.Neighbors.Contains(survivingEdge),
            "A live unselected connection keeps the junction and still receives a neighbor update");

        // --- Stage B1: a shuffled selection must produce one and the same demolition plan ---
        var fp = new EntityManager();
        var fpNodes = new Entity[4];
        for (int i = 0; i < fpNodes.Length; i++) { fpNodes[i] = fp.Make(201 + i); fp.Set(fpNodes[i], new Node()); }
        var fpEdges = new Entity[3];
        for (int i = 0; i < fpEdges.Length; i++)
        {
            fpEdges[i] = fp.Make(211 + i);
            fp.Set(fpEdges[i], new Edge { m_Start = fpNodes[i], m_End = fpNodes[i + 1] });
            var fpLane = fp.Make(221 + i);
            fp.Buffer(fpEdges[i], new SubLane { m_SubLane = fpLane });
            fp.Buffer(fpNodes[i], new ConnectedEdge { m_Edge = fpEdges[i] });
            fp.Buffer(fpNodes[i + 1], new ConnectedEdge { m_Edge = fpEdges[i] });
        }
        var fpBuilding = fp.Make(231);
        var fpUpgrade = fp.Make(232);
        var fpAttached = fp.Make(233);
        fp.Buffer(fpBuilding, new Game.Buildings.InstalledUpgrade { m_Upgrade = fpUpgrade });
        fp.Set(fpAttached, new Game.Objects.Attached { m_Parent = fpBuilding });
        fp.Buffer(fpBuilding, new Game.Objects.SubObject { m_SubObject = fpAttached });
        var fpLoose = fp.Make(234);
        fp.Set(fpLoose, new Game.Objects.Moving());

        var permutations = new[]
        {
            new[] { fpEdges[1], fpBuilding, fpLoose },
            new[] { fpLoose, fpBuilding, fpEdges[1] },
            new[] { fpBuilding, fpEdges[1], fpLoose },
            new[] { fpLoose, fpEdges[1], fpBuilding },
        };
        string fpBaselineFingerprint = null;
        List<Entity> fpBaselineDelete = null;
        List<Entity> fpBaselineUpdate = null;
        DeletionPlan fpPlan = null;
        for (int i = 0; i < permutations.Length; i++)
        {
            var fpCommands = new EntityCommandBuffer();
            DeletionPlan plan = Cascade.Plan(fp, permutations[i]);
            Cascade.Submit(fp, fpCommands, plan, out _);
            if (i == 0)
            {
                fpPlan = plan;
                fpBaselineFingerprint = plan.Fingerprint();
                fpBaselineDelete = fpCommands.DeleteOrder;
                fpBaselineUpdate = fpCommands.UpdateOrder;
                continue;
            }

            Check(plan.Fingerprint() == fpBaselineFingerprint, "Shuffled roots must produce the same demolition plan");
            Check(SequenceEqual(fpCommands.DeleteOrder, fpBaselineDelete),
                "Shuffled roots must emit the same deletion commands in the same order");
            Check(SequenceEqual(fpCommands.UpdateOrder, fpBaselineUpdate),
                "Shuffled roots must emit the same rebuild commands in the same order");
        }
        Check(fpPlan.Submitted.Contains(fpEdges[1]) && fpPlan.Submitted.Contains(fpUpgrade) && fpPlan.Submitted.Contains(fpAttached),
            "A facility unit travels together: edge, upgrade and attached object");

        var fpUnits = Cascade.BuildUnits(fp, fpPlan);
        var fpBuildingUnit = FindByRoot(fpUnits, fpBuilding);
        Check(fpBuildingUnit != null &&
              fpBuildingUnit.Members.Contains(fpUpgrade) && fpBuildingUnit.Members.Contains(fpAttached),
            "An upgrade and an attached object belong to the building unit");
        Check(fpUnits.Count >= 3, "Independent facilities stay separate units");
        var fpUnitsAgain = Cascade.BuildUnits(fp, Cascade.Plan(fp, permutations[3]));
        bool sameUnits = fpUnits.Count == fpUnitsAgain.Count;
        for (int i = 0; sameUnits && i < fpUnits.Count; i++) sameUnits = fpUnits[i].Root == fpUnitsAgain[i].Root;
        Check(sameUnits, "Unit grouping is as order independent as the plan itself");

        // A junction shared with a surviving road is never submitted; it and its roads are rebuilt.
        var pub = new EntityManager();
        var hub = pub.Make(301);
        pub.Set(hub, new Node());
        var branch = pub.Make(302);
        var keepA = pub.Make(303);
        var keepB = pub.Make(304);
        pub.Set(branch, new Edge { m_Start = hub });
        pub.Set(keepA, new Edge { m_End = hub });
        pub.Set(keepB, new Edge { m_End = hub });
        pub.Buffer(hub, new ConnectedEdge { m_Edge = branch }, new ConnectedEdge { m_Edge = keepA }, new ConnectedEdge { m_Edge = keepB });
        var pubPlan = Cascade.Plan(pub, new[] { branch });
        var pubCommands = new EntityCommandBuffer();
        Cascade.Submit(pub, pubCommands, pubPlan, out _);
        Check(!pubPlan.Submitted.Contains(hub), "A junction that keeps live roads must not be submitted");
        Check(pubCommands.Updated.Contains(hub) && pubCommands.Updated.Contains(keepA) && pubCommands.Updated.Contains(keepB),
            "A shared junction and every road still attached to it must be rebuilt");

        // --- Stage B3: every pause entry keeps the same remainder ---
        bool[] unitAlive = { true, false, true, true };
        bool[] nodeAlive = { true, true, false };
        var stopUnits = new List<int>();
        var stopNodes = new List<int>();
        InterruptedCapture.Capture(1, 4, unitAlive, nodeAlive, stopUnits, stopNodes, out int stopUnitCount, out int stopNodeCount);
        var escUnits = new List<int>();
        var escNodes = new List<int>();
        InterruptedCapture.Capture(1, 4, unitAlive, nodeAlive, escUnits, escNodes, out _, out _);
        var reevaluateUnits = new List<int>();
        var reevaluateNodes = new List<int>();
        InterruptedCapture.Capture(1, 4, unitAlive, nodeAlive, reevaluateUnits, reevaluateNodes, out _, out _);
        Check(SequenceEqual(stopUnits, escUnits) && SequenceEqual(stopUnits, reevaluateUnits) &&
              SequenceEqual(stopNodes, escNodes) && SequenceEqual(stopNodes, reevaluateNodes),
            "Stop, Escape and re-evaluate must keep exactly the same remainder");
        Check(stopUnitCount == 2 && stopUnitCount == stopUnits.Count && stopUnits[0] == 2 && stopUnits[1] == 3,
            "A pause keeps every unfinished unit that is still a live target");
        Check(stopNodeCount == 2 && stopNodeCount == stopNodes.Count, "A pause keeps the deferred nodes not cleaned yet");

        var nodeOnlyUnits = new List<int>();
        var nodeOnlyNodes = new List<int>();
        InterruptedCapture.Capture(4, 4, unitAlive, nodeAlive, nodeOnlyUnits, nodeOnlyNodes, out int nodeOnlyUnitCount, out int nodeOnlyNodeCount);
        Check(nodeOnlyUnitCount == 0 && nodeOnlyUnitCount == nodeOnlyUnits.Count, "Nothing is left to submit once every unit went out");
        Check(nodeOnlyNodeCount == 2 && nodeOnlyNodeCount == nodeOnlyNodes.Count, "A pause in the node pass keeps the node remainder");
        Check(InterruptedCapture.FirstNodeIndex(4, 4) == 0 && InterruptedCapture.FirstNodeIndex(9, 4) == 5,
            "The node pass starts where the unit pass stopped");

        var freshUnits = new List<int>();
        var freshNodes = new List<int>();
        InterruptedCapture.Capture(0, 4, new[] { true, true, true, true }, new[] { true, true },
            freshUnits, freshNodes, out int freshUnitCount, out int freshNodeCount);
        Check(freshUnitCount == 4 && freshNodeCount == 2, "A pause before the first batch keeps every recorded target");

        // --- Stage B2: the batch window follows measured time, not a fixed interval ---
        Check(BatchWindowPolicy.NextLength(128, 32, 16, 8) == 16, "A batch that took twice the budget is halved");
        Check(BatchWindowPolicy.NextLength(128, 32, 4, 8) == 64, "A batch that took half the budget doubles");
        Check(BatchWindowPolicy.NextLength(128, 32, 9, 8) == 32, "A measurement near the budget keeps the length steady");
        Check(BatchWindowPolicy.NextLength(128, 100, 400, 8) == 2, "A stalled batch shrinks to the smallest step");
        Check(BatchWindowPolicy.NextLength(8, 8, 1, 8) == 8, "The configured batch size still caps the length");
        Check(BatchWindowPolicy.NextLength(128, 8, 0, 8) == 8, "Without a measurement the length is unchanged");
        Check(BatchWindowPolicy.Evaluate(0, false, 0f, 0f) == BatchAdvance.Advance,
            "Nothing outstanding means the next batch may run");
        Check(BatchWindowPolicy.Evaluate(5, false, 0.01f, 1f) == BatchAdvance.Wait,
            "A submitted batch blocks the next one until the game has processed it");
        Check(BatchWindowPolicy.Evaluate(5, true, 0.01f, 1f) == BatchAdvance.Advance,
            "An observed result lets the loop continue without waiting out the deadline");
        Check(BatchWindowPolicy.Evaluate(5, false, 2f, 1f) == BatchAdvance.TimedOut,
            "A batch the game never finishes is reported instead of stalling the tool");

        // --- Stage C1: attachment and reconnect are refused unless they are provably ours ---
        var owners = new OwnerCandidate[3];
        owners[0] = new OwnerCandidate { Slot = 41, X = 10f, Z = 10f, QW = 1f };
        Check(OwnerMatchPolicy.Resolve(owners, 1, 10f, 0f, 10f, 0f, 0f, 0f, 1f, out int ownerSlot, out int ownerMatches)
              == OwnerMatchStatus.Unique && ownerSlot == 0 && ownerMatches == 1,
            "A unique host created by this undo resolves");
        Check(owners[ownerSlot].Slot == 41,
            "The resolved entry keeps the source index of the host it matched");
        owners[1] = new OwnerCandidate { Slot = 42, X = 10f, Z = 10f, QW = 1f };
        Check(OwnerMatchPolicy.Resolve(owners, 2, 10f, 0f, 10f, 0f, 0f, 0f, 1f, out _, out int ambiguousMatches)
              == OwnerMatchStatus.Ambiguous && ambiguousMatches == 2,
            "Two same-prefab hosts on one pose must not take the attached objects");
        owners[0] = new OwnerCandidate { Slot = 41, X = 10f, Z = 10f, QW = 1f, PreExisting = true };
        Check(OwnerMatchPolicy.Resolve(owners, 1, 10f, 0f, 10f, 0f, 0f, 0f, 1f, out _, out _)
              == OwnerMatchStatus.OccupiedBefore,
            "A pose that was already occupied before the undo must not adopt the attached objects");
        owners[0] = new OwnerCandidate { Slot = 41, X = 10f, Z = 10f, QW = 0.7f };
        Check(OwnerMatchPolicy.Resolve(owners, 1, 10f, 0f, 10f, 0f, 0f, 0f, 1f, out _, out _)
              == OwnerMatchStatus.NotFound,
            "A host with a different rotation is not the recorded one");
        owners[0] = new OwnerCandidate { Slot = 41, X = 12f, Z = 10f, QW = 1f };
        Check(OwnerMatchPolicy.Resolve(owners, 1, 10f, 0f, 10f, 0f, 0f, 0f, 1f, out _, out _)
              == OwnerMatchStatus.NotFound,
            "A host two metres away is not the recorded one");
        Check(OwnerMatchPolicy.Resolve(null, 0, 0f, 0f, 0f, 0f, 0f, 0f, 1f, out _, out _) == OwnerMatchStatus.NotFound,
            "An empty city resolves nothing");

        Check(NetConnectivityPolicy.Evaluate(0f, 0f, 20f, 0f, 0f, 0f, 20f, 0f, 2, 2, 0.5f) == NetLinkStatus.Reconnected,
            "A segment rebuilt on its recorded endpoints is a clean reconnect");
        Check(NetConnectivityPolicy.Evaluate(0f, 0f, 20f, 0f, 20f, 0f, 0f, 0f, 2, 2, 0.5f) == NetLinkStatus.ReconnectedReversed,
            "A reversed rebuild still reconnects the same junctions");
        Check(NetConnectivityPolicy.Evaluate(0f, 0f, 20f, 0f, 0f, 0f, 26f, 0f, 2, 2, 0.5f) == NetLinkStatus.Moved,
            "A segment that ends elsewhere must not be reported as a reconnect");
        Check(NetConnectivityPolicy.Evaluate(0f, 0f, 20f, 0f, 0f, 0f, 20f, 0f, 2, 3, 0.5f) == NetLinkStatus.ForeignSplice,
            "A segment spliced into a road that changed must be flagged for review");
        Check(NetConnectivityPolicy.Evaluate(0f, 0f, 20f, 0f, 0f, 0f, 20f, 0f, 3, 2, 0.5f) == NetLinkStatus.Reconnected,
            "A neighbour that disappeared during the demolition is not a splice");

        float[] occupiedX = { 50f, 200f };
        float[] occupiedZ = { 50f, 50f };
        Check(OccupancyPolicy.FirstConflict(50.5f, 50.5f, 2f, occupiedX, occupiedZ, 2, -1) == 0,
            "A lot taken by another object blocks the restore");
        Check(OccupancyPolicy.FirstConflict(120f, 120f, 2f, occupiedX, occupiedZ, 2, -1) == -1,
            "A free lot allows the restore");
        Check(OccupancyPolicy.FirstConflict(50.5f, 50.5f, 2f, occupiedX, occupiedZ, 2, 0) == -1,
            "The restored object never conflicts with itself");

        // C1: a junction the demolition emptied must still be short one link. If it is back at its
        // captured count, something else re-made that connection and the segment must not be rebuilt.
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, 2, 1, 0.5f) == NetLinkStatus.Reconnected,
            "A junction that lost the removed edge is a clean reconnect");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, 2, 2, 0.5f) == NetLinkStatus.ForeignSplice,
            "A junction back at its captured count was re-linked by something else");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, 2, 3, 0.5f) == NetLinkStatus.ForeignSplice,
            "A junction that gained neighbours must not be spliced again");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 4f, 0f, 2, 1, 0.5f) == NetLinkStatus.Moved,
            "A junction that moved must not be treated as the recorded endpoint");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, -1, 9, 0.5f) == NetLinkStatus.Reconnected,
            "An unknown captured count cannot be contradicted");

        // C1: a count cannot tell a junction nobody touched from one that lost its captured edges and
        // was re-linked by something else. Two edges meeting a node are two edges meeting a node,
        // whatever they are, so the recorded identities are what the junction is checked against.
        Entity linkA = new Entity { Index = 101, Version = 1 };
        Entity linkB = new Entity { Index = 102, Version = 1 };
        Entity linkC = new Entity { Index = 103, Version = 1 };
        Entity linkBRecycled = new Entity { Index = 102, Version = 2 };
        Entity[] capturedLinks = { linkA, linkB };

        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, capturedLinks, new[] { linkA, linkB }, 3, 2, 0.5f) == NetLinkStatus.Reconnected,
            "A junction still holding exactly the captured edges is a clean reconnect");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, capturedLinks, new[] { linkA, linkC }, 3, 2, 0.5f) == NetLinkStatus.IdentityChanged,
            "The same number of edges with a changed identity must be refused");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, capturedLinks, new[] { linkBRecycled }, 3, 1, 0.5f) == NetLinkStatus.IdentityChanged,
            "A recycled entity slot is a different edge, not the captured one");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, capturedLinks, new[] { linkA }, 3, 1, 0.5f) == NetLinkStatus.Reconnected,
            "A captured edge that is merely gone splices nothing");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, null, new[] { linkA }, 3, 1, 0.5f) == NetLinkStatus.IdentityUnknown,
            "An unrecorded junction is unproven evidence and is refused");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, capturedLinks, null, 3, 1, 0.5f) == NetLinkStatus.IdentityUnknown,
            "A junction unreadable now is unproven evidence and is refused");

        // A: the verdict is taken once, before the snapshot writes anything. Two adjacent segments of
        // one snapshot restore in different batches; the second batch sees the junction holding an edge
        // the first batch just rebuilt, which is not among the recorded identities. Reading the junction
        // again at that moment would call it a foreign newcomer and drop the second segment.
        Entity linkRebuilt = new Entity { Index = 104, Version = 1 };
        NetLinkStatus preJudged = NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, capturedLinks, new[] { linkA, linkB }, 3, 2, 0.5f);
        var verdicts = new NetVerdictCache(2);
        verdicts.Set(0, preJudged, preJudged);
        verdicts.Set(1, preJudged, preJudged);
        Check(verdicts.TryGet(1, out NetLinkStatus cachedStart, out NetLinkStatus cachedEnd) &&
              NetVerdictCache.AllowsRebuild(cachedStart) && NetVerdictCache.AllowsRebuild(cachedEnd),
            "A verdict taken before the snapshot wrote anything stays reusable for a later batch");
        Check(NetConnectivityPolicy.EvaluateEndpoint(0f, 0f, 0f, 0f, capturedLinks, new[] { linkA, linkB, linkRebuilt }, 3, 3, 0.5f) == NetLinkStatus.IdentityChanged,
            "Re-reading that junction after the first batch landed would refuse the second segment - which is what the verdict cache prevents");
        Check(!NetVerdictCache.AllowsRebuild(NetLinkStatus.IdentityChanged) &&
              !NetVerdictCache.AllowsRebuild(NetLinkStatus.IdentityUnknown) &&
              !NetVerdictCache.AllowsRebuild(NetLinkStatus.Moved) &&
              NetVerdictCache.AllowsRebuild(NetLinkStatus.Reconnected) &&
              NetVerdictCache.AllowsRebuild(NetLinkStatus.ReconnectedReversed),
            "Only a clean reconnect allows a rebuild");
        Check(NetVerdictCache.IsIdentityRefusal(NetLinkStatus.IdentityUnknown) &&
              NetVerdictCache.IsIdentityRefusal(NetLinkStatus.IdentityChanged) &&
              !NetVerdictCache.IsIdentityRefusal(NetLinkStatus.Moved),
            "Identity refusals are counted apart from a junction that merely moved");

        var refusedVerdicts = new NetVerdictCache(1);
        refusedVerdicts.Set(0, NetLinkStatus.IdentityChanged, NetLinkStatus.Reconnected);
        Check(refusedVerdicts.TryGet(0, out NetLinkStatus refusedStart, out _) && !NetVerdictCache.AllowsRebuild(refusedStart),
            "A segment whose junction already failed identity at pre-judgement stays refused");

        var unfilledVerdicts = new NetVerdictCache(1);
        Check(unfilledVerdicts.TryGet(0, out NetLinkStatus unfilledStart, out _) && !NetVerdictCache.AllowsRebuild(unfilledStart),
            "An unfilled verdict must not read as reusable");
        Check(!unfilledVerdicts.TryGet(3, out _, out _),
            "An index outside the cache is a miss, not a reusable verdict");

        string undoSourceForVerdicts = ReadSource("DeletionUndo.cs");
        string verdictStepBody = MethodBody(undoSourceForVerdicts, "public bool Step(EntityManager em, EntityCommandBuffer ecb, out bool completed)", 9000);
        int prejudgeAt = verdictStepBody.IndexOf("EnsureNetVerdicts(em, snap)", StringComparison.Ordinal);
        int emitAt = verdictStepBody.IndexOf("case UndoOperationKind.Net:", StringComparison.Ordinal);
        Check(prejudgeAt >= 0 && emitAt >= 0 && prejudgeAt < emitAt,
            "Resume verdicts are taken before the restore loop emits anything");
        Check(verdictStepBody.Contains("NetLinksIntact(em, op.Net, op.NetIndex, snap)"),
            "The net case must consult the snapshot verdicts");

        // Identity comparison lives on the resume path alone. A plain batch checks junction positions
        // and takes no verdict cache, so no approval can outlive the batch that took it.
        string gateBody = MethodBody(undoSourceForVerdicts, "private void EnsureNetVerdicts(EntityManager em, UndoSnapshot snap)", 3000);
        int gateAt = gateBody.IndexOf("if (!snap.IdentityGate) return;", StringComparison.Ordinal);
        int cachedAt = gateBody.IndexOf("if (snap.Verdicts != null) return;", StringComparison.Ordinal);
        int allocateAt = gateBody.IndexOf("new NetVerdictCache(", StringComparison.Ordinal);
        Check(gateAt >= 0 && cachedAt > gateAt && allocateAt > gateAt,
            "Verdicts are taken only for a snapshot that was resumed");
        Check(CountOccurrences(undoSourceForVerdicts, "JudgeProbe(em, probes[") == 2,
            "Junction identity is judged only by the resume pass, and always against the shared snapshot");

        // The resume pass reads the city's junctions once and looks each dead position up once: a
        // per-endpoint query would be O(dead endpoints x city), and adjacent segments share the same
        // dead junction.
        string prejudgeBody = MethodBody(undoSourceForVerdicts, "private void EnsureNetVerdicts(EntityManager em, UndoSnapshot snap)", 6000);
        Check(prejudgeBody.Contains("using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetNode>()))") &&
              prejudgeBody.Contains("using (var nodes = query.ToEntityArray(Unity.Collections.Allocator.Temp))"),
            "The junction snapshot owns both the query and the array it reads");
        Check(prejudgeBody.Contains("m_JunctionSnapshotBuilds++;") &&
              CountOccurrences(prejudgeBody, "JudgeProbe(em, probes[") == 2,
            "One snapshot per pass serves every endpoint judged in it");

        string lookupBody = MethodBody(undoSourceForVerdicts, "private bool TryFindJunctionNear(JunctionSnapshot junctions,", 2000);
        Check(!lookupBody.Contains("CreateEntityQuery"),
            "The junction lookup walks the pre-pass snapshot instead of building its own query");
        Check(lookupBody.Contains("junctions.Near.TryGetValue(key, out found)") &&
              lookupBody.Contains("junctions.Near[key] = found;"),
            "A recorded position is looked up once and its answer reused for the rest of the pass");
        Check(undoSourceForVerdicts.Contains("private struct JunctionKey") &&
              undoSourceForVerdicts.Contains("BitConverter.DoubleToInt64Bits("),
            "Position keys keep the exact value, so reusing an answer cannot move a verdict");

        string resumeBody = MethodBody(undoSourceForVerdicts, "public bool StartUndo(UndoAssetMask mask)", 6000);
        Check(resumeBody.Contains("snap.Verdicts = null;") && resumeBody.Contains("snap.IdentityGate = true;"),
            "A resumed undo drops its verdicts and raises the identity gate");
        Check(resumeBody.Contains("snap.IdentityGate = false;") &&
              resumeBody.Contains("snap.EmittedNets.Clear();") &&
              resumeBody.Contains("snap.EmittedNetIndices.Clear();"),
            "A fresh undo clears the gate and the emitted records a reused snapshot carries");

        string missBody = MethodBody(undoSourceForVerdicts, "private bool NetLinksIntact(EntityManager em, in UndoNetRecord rec, int netIndex, UndoSnapshot snap)", 3000);
        Check(missBody.Contains("if (snap.Verdicts == null)") && missBody.Contains("PositionAllowsRebuild(em, rec.StartNode, rec.StartPos)"),
            "A batch without verdicts checks junction positions only");
        Check(missBody.Contains("m_NetPositionRejected++;"), "A position refusal is counted separately");
        Check(missBody.Contains("m_NetVerdictMisses++"), "A verdict miss is counted, not guessed");
        Check(!missBody.Contains("JudgeProbe("),
            "A verdict miss must not fall back to reading the junction after earlier batches landed");

        string emitRecordBody = MethodBody(undoSourceForVerdicts, "private static bool EmitNetRecord(", 6000);
        Check(emitRecordBody.Contains("out Entity submittedStart,") &&
              emitRecordBody.Contains("submittedStart = startEntity;") &&
              emitRecordBody.Contains("submittedEnd = endEntity;"),
            "Emitting a segment reports the endpoints it actually submitted");
        Check(verdictStepBody.Contains("snap.EmittedNets.Add(new EmittedNet") &&
              verdictStepBody.Contains("snap.EmittedNetIndices.Add(op.NetIndex);"),
            "An emitted segment is remembered with its submitted endpoints and its index");
        Check(undoSourceForVerdicts.Contains("private struct EmittedNet") &&
              undoSourceForVerdicts.Contains("public Entity SubmittedStart;") &&
              undoSourceForVerdicts.Contains("public Entity SubmittedEnd;"),
            "The emitted record keeps the submitted endpoints, not the recorded junctions");
        Check(undoSourceForVerdicts.Contains("Entity known = startUnknown ? emitted.SubmittedEnd : emitted.SubmittedStart;") &&
              undoSourceForVerdicts.Contains("return (shape.Start == known || shape.End == known) ? 2 : 0;"),
            "A dead-end rebuild is recognised from the one endpoint that was submitted");
        Check(undoSourceForVerdicts.Contains("return (forward || reverse) ? 1 : 0;"),
            "An exact match of both submitted junctions is the strictest tier");
        Check(undoSourceForVerdicts.Contains("One end alone is not enough") &&
              undoSourceForVerdicts.Contains("does not remove the misidentification"),
            "The geometry case documents that it compares both ends and still not the curve");

        // The cache holds the verdict taken at resume; it does not hold where the junction stands now.
        int positionCheck = missBody.IndexOf("if (!PositionAllowsRebuild(em, rec.StartNode, rec.StartPos)", StringComparison.Ordinal);
        int cacheRead = missBody.IndexOf("snap.Verdicts.TryGet(netIndex", StringComparison.Ordinal);
        Check(positionCheck >= 0 && cacheRead > positionCheck,
            "A resumed batch re-checks both junction positions before it reads the cached verdicts");
        Check(undoSourceForVerdicts.Contains("public float3 RecordedStartPos;") &&
              undoSourceForVerdicts.Contains("public float3 RecordedEndPos;"),
            "The emitted record keeps the geometry of the segment it submitted for");
        Check(verdictStepBody.Contains("RecordedStartPos = op.Net.StartPos,") &&
              verdictStepBody.Contains("RecordedEndPos = op.Net.EndPos,"),
            "An emitted segment records where it stood when it was captured");
        string claimBody = MethodBody(undoSourceForVerdicts, "private static void ClaimEmittedEdges(", 5000);
        Check(claimBody.Contains("candidates.Sort(CompareCandidates);") &&
              claimBody.Contains("claims[candidate.Record] = candidate.Edge;"),
            "Every pairing is collected before any claim is awarded, so walking order cannot decide it");
        Check(claimBody.Contains("if (IsRecordedLink(probe.RecordedIds, edge)) continue;"),
            "A neighbour the recording already knew never competes for a claim");
        string compareBody = MethodBody(undoSourceForVerdicts, "private static int CompareCandidates(", 1400);
        Check(compareBody.Contains("if (a.Tier != b.Tier) return a.Tier < b.Tier ? -1 : 1;") &&
              compareBody.Contains("if (a.Record != b.Record)"),
            "Strict matches are awarded before loose ones, and never by the order segments were walked");
        Check(undoSourceForVerdicts.Contains("return EdgeMeetsRecordedGeometry(em, shape, emitted) ? 3 : 0;"),
            "The geometry case is the loosest tier, so it cannot take a claim an exact match needed");
        string geometryBody = MethodBody(undoSourceForVerdicts, "private static bool EdgeMeetsRecordedGeometry(", 2000);
        Check(geometryBody.Contains("Distance3(start, emitted.RecordedStartPos) <= tolerance &&") &&
              geometryBody.Contains("Distance3(end, emitted.RecordedEndPos) <= tolerance;") &&
              geometryBody.Contains("bool reverse = "),
            "Both ends must stand where the recorded segment stood, in either direction");
        Check(undoSourceForVerdicts.Contains("float dy = a.y - b.y;"),
            "The geometry distance includes height, so a stacked junction is somewhere else");
        string trimBody = MethodBody(undoSourceForVerdicts, "private static Entity[] WithoutClaimed(", 2400);
        Check(trimBody.Contains("Entity[] claims") &&
              trimBody.Contains("IsRecordedLink(recordedIds, edge)") &&
              trimBody.Contains("!IsClaimed(claims, edge)"),
            "The filter drops what the whole prejudge claimed and never a neighbour the recording already knew");
        string claimsBody = MethodBody(undoSourceForVerdicts, "private static Entity[] EnsureEmittedClaims(", 1400);
        Check(claimsBody.Contains("if (claims != null && claims.Length == snap.EmittedNets.Count) return claims;") &&
              claimsBody.Contains("snap.EmittedNetClaims = grown;"),
            "A resume reuses the claims the snapshot already carries");
        Check(resumeBody.Contains("snap.EmittedNetClaims = null;"),
            "Only a fresh undo clears the claims, so a refusal before a pause stays refused after it");
        Check(undoSourceForVerdicts.Contains("public Entity[] EmittedNetClaims;"),
            "The claims live on the snapshot, not in the verdict pass");
        Check(undoSourceForVerdicts.Contains("private static bool IsRecordedLink(Entity[] recordedIds, Entity edge)"),
            "The filter can tell a neighbour that was already there from one this undo rebuilt");
        string keyBody = MethodBody(undoSourceForVerdicts, "private struct JunctionKey : IEquatable<JunctionKey>", 1400);
        Check(keyBody.Contains("public long Y;") &&
              keyBody.Contains("float y = position.y == 0f ? 0f : position.y;") &&
              keyBody.Contains("X == other.X && Y == other.Y && Z == other.Z") &&
              keyBody.Contains("(Y * 397)"),
            "The position key carries height too, so stacked junctions get their own answers");
        Check(lookupBody.Contains("math.distancesq(junctions.Positions[i], recorded)"),
            "The junction lookup compares height as well, so an overhead junction cannot hide the ground one");

        // C1: the same model already standing on the lot is a different building, so neither the
        // building nor its upgrades may be attached to it.
        OwnerCandidate[] lotCandidates =
        {
            new OwnerCandidate { Slot = 0, X = 10f, Y = 0f, Z = 10f, QX = 0f, QY = 0f, QZ = 0f, QW = 1f, PreExisting = true },
            new OwnerCandidate { Slot = 1, X = 10f, Y = 0f, Z = 10f, QX = 0f, QY = 0f, QZ = 0f, QW = 1f, PreExisting = false },
        };
        Check(OwnerMatchPolicy.Resolve(lotCandidates, 2, 10f, 0f, 10f, 0f, 0f, 0f, 1f, out int lotSlot, out int lotMatches) == OwnerMatchStatus.Ambiguous,
            "Two same-model candidates on one lot must not be attached to");
        Check(lotSlot == -1 && lotMatches == 2,
            "An ambiguous host reports no slot and both matches");
        OwnerCandidate[] onlyPreexisting = { lotCandidates[0] };
        Check(OwnerMatchPolicy.Resolve(onlyPreexisting, 1, 10f, 0f, 10f, 0f, 0f, 0f, 1f, out _, out _) == OwnerMatchStatus.OccupiedBefore,
            "The only candidate standing there before the undo is not this undo's host");
        OwnerCandidate[] onlyFresh = { new OwnerCandidate { Slot = 0, X = 10f, Y = 0f, Z = 10f, QX = 0f, QY = 0f, QZ = 0f, QW = 1f, PreExisting = false } };
        Check(OwnerMatchPolicy.Resolve(onlyFresh, 1, 10f, 0f, 10f, 0f, 0f, 0f, 1f, out int freshSlot, out _) == OwnerMatchStatus.Unique && freshSlot == 0,
            "The object this undo created is accepted as the host");

        // The coarse radius drops most candidates before the policy sees them, so the compacted array
        // is not the source list. The reported slot must index the array it was given, otherwise the
        // caller dereferences a different candidate and silently binds to the wrong entity.
        OwnerCandidate[] filtered =
        {
            new OwnerCandidate { Slot = 4, X = 90f, Y = 0f, Z = 90f, QX = 0f, QY = 0f, QZ = 0f, QW = 1f, PreExisting = false },
            new OwnerCandidate { Slot = 6, X = 40f, Y = 0f, Z = 40f, QX = 0f, QY = 0f, QZ = 0f, QW = 1f, PreExisting = false },
            new OwnerCandidate { Slot = 9, X = 12f, Y = 0f, Z = 12f, QX = 0f, QY = 0f, QZ = 0f, QW = 1f, PreExisting = false },
        };
        Check(OwnerMatchPolicy.Resolve(filtered, 3, 12f, 0f, 12f, 0f, 0f, 0f, 1f, out int filteredSlot, out _) == OwnerMatchStatus.Unique,
            "A single match among filtered candidates resolves as unique");
        Check(filteredSlot == 2 && filteredSlot < 3,
            "The reported slot indexes the filtered array, not the source list");
        Check(filtered[filteredSlot].Slot == 9,
            "The filtered entry still carries the source index needed to reach the real entity");

        // Index alone is reused by the game, so the deterministic order has to break ties on version.
        List<Entity> reordered = EntityOrder.Sorted(new[]
        {
            new Entity { Index = 5, Version = 2 },
            new Entity { Index = 5, Version = 1 },
            new Entity { Index = 3, Version = 9 },
        });
        Check(reordered.Count == 3 && reordered[0].Index == 3 && reordered[1].Version == 1 && reordered[2].Version == 2,
            "Ordering breaks an index tie on the entity version");

        // --- Stage A: the compatibility tier, not the feature, decides whether a write may happen ---
        Check(CompatibilityGate.AllowsDeletionSubmit(CompatibilityGate.TierVerified),
            "A verified deletion tier may submit");
        Check(!CompatibilityGate.AllowsDeletionSubmit(CompatibilityGate.TierPreviewOnly),
            "A preview-only deletion tier submits nothing");
        Check(!CompatibilityGate.AllowsDeletionSubmit(CompatibilityGate.TierUnavailable),
            "An unavailable deletion tier submits nothing");
        Check(CompatibilityGate.AllowsUndoRebuild(CompatibilityGate.TierVerified),
            "A verified undo tier may rebuild");
        Check(!CompatibilityGate.AllowsUndoRebuild(CompatibilityGate.TierPreviewOnly),
            "A preview-only undo tier writes nothing back");
        Check(!CompatibilityGate.AllowsUndoRebuild(CompatibilityGate.TierUnavailable),
            "An unavailable undo tier rebuilds nothing");
        Check(CompatibilityGate.AllowsRun(CompatibilityGate.TierVerified),
            "A verified search tier may run");
        Check(CompatibilityGate.AllowsRun(CompatibilityGate.TierPreviewOnly),
            "A preview-only search tier still lets the player select");
        Check(!CompatibilityGate.AllowsRun(CompatibilityGate.TierUnavailable),
            "An unavailable search tier returns the player to the vanilla tool");

        // Identity is index plus version. A bare index is reused after a destroy, so treating two
        // versions as one entity would silently merge distinct objects in every set and dictionary.
        Check(!new Entity { Index = 40, Version = 1 }.Equals(new Entity { Index = 40, Version = 3 }),
            "The same index at a different version is a different entity");
        Check(new Entity { Index = 40, Version = 2 }.Equals(new Entity { Index = 40, Version = 2 }),
            "Identity is the index together with the version");
        var versionedSet = new HashSet<Entity> { new Entity { Index = 40, Version = 1 }, new Entity { Index = 40, Version = 3 } };
        Check(versionedSet.Count == 2, "A set keeps two entities that share an index but not a version");

        // --- Stage A: a refused tier leaves the command buffer empty, proven on the write path itself ---
        var gateEm = new EntityManager();
        Entity gateRoot = gateEm.Make(901);
        Entity gateNode = gateEm.Make(902);
        var gateSubmitted = new HashSet<Entity> { gateRoot };
        var gateNeighbors = new HashSet<Entity> { gateNode };
        var gateEmpty = new HashSet<Entity>();
        var gatePlan = new DeletionPlan(gateSubmitted, gateSubmitted, gateNeighbors, gateEmpty, gateEmpty);

        var previewEcb = new EntityCommandBuffer();
        Check(!DeletionGate.Submit(CompatibilityGate.TierPreviewOnly, gateEm, previewEcb, gatePlan, out int previewUpdated, out string previewReason),
            "A preview-only tier refuses the deletion write");
        Check(previewEcb.Deleted.Count == 0 && previewEcb.Updated.Count == 0 && previewUpdated == 0,
            "A refused deletion writes no command at all");
        Check(previewReason == DeletionGate.ReasonVerifiedOnly,
            "The refusal is reported instead of failing silently");

        var unavailableEcb = new EntityCommandBuffer();
        Check(!DeletionGate.Submit(CompatibilityGate.TierUnavailable, gateEm, unavailableEcb, gatePlan, out _, out string unavailableReason)
              && unavailableEcb.Deleted.Count == 0 && unavailableEcb.Updated.Count == 0,
            "An unavailable tier writes no command either");
        Check(unavailableReason == DeletionGate.ReasonUnavailable,
            "An unavailable tier is reported as unavailable");

        var verifiedEcb = new EntityCommandBuffer();
        Check(DeletionGate.Submit(CompatibilityGate.TierVerified, gateEm, verifiedEcb, gatePlan, out _, out _),
            "A verified tier writes the plan");
        Check(verifiedEcb.Deleted.Contains(gateRoot) && verifiedEcb.Updated.Contains(gateNode),
            "The verified write reaches the command buffer");

        Check(!DeletionGate.TryClaimRebuild(CompatibilityGate.TierPreviewOnly, out _),
            "A preview-only undo tier claims no rebuild");
        Check(!DeletionGate.TryClaimRebuild(CompatibilityGate.TierUnavailable, out _),
            "An unavailable undo tier claims no rebuild");
        Check(DeletionGate.TryClaimRebuild(CompatibilityGate.TierVerified, out _),
            "A verified undo tier may claim a rebuild");

        // An index alone is reused after a destroy, so a plan over the same index with a different
        // version has to be a different plan. This is the case a bare index fingerprint misses.
        var versionRootsA = new HashSet<Entity> { new Entity { Index = 71, Version = 1 } };
        var versionRootsB = new HashSet<Entity> { new Entity { Index = 71, Version = 2 } };
        var versionEmpty = new HashSet<Entity>();
        var versionPlanA = new DeletionPlan(versionRootsA, versionRootsA, versionEmpty, versionEmpty, versionEmpty);
        var versionPlanB = new DeletionPlan(versionRootsB, versionRootsB, versionEmpty, versionEmpty, versionEmpty);
        Check(versionPlanA.Fingerprint() != versionPlanB.Fingerprint(),
            "An entity that only changes version changes the fingerprint");

        // --- Call-site guards: a pure function cannot see which variable the batch loop feeds it ---
        string toolSource = ReadSource("MarqueeDeleteToolSystem.cs");
        string compatibilitySource = ReadSource("Compatibility.cs");
        string undoSource = ReadSource("DeletionUndo.cs");
        Check(toolSource.Length > 0 && undoSource.Length > 0 && compatibilitySource.Length > 0,
            "The production sources are reachable from the regression run");
        Check(toolSource.Contains("BatchWindowPolicy.NextLength(configuredCap,"),
            "The measured batch length is capped by the configured size, not by the hard maximum");
        Check(!toolSource.Contains("BatchWindowPolicy.NextLength(MaxBatchRoots,"),
            "The hard maximum is never handed in as the batch cap");
        Check(toolSource.Contains("if (batchCap > configuredCap)"),
            "A measured length cannot exceed the size the player configured");
        Check(toolSource.Contains("BatchWindowPolicy.Evaluate(") && toolSource.Contains("m_LastBatchOutstanding"),
            "The batch loop advances on observed results instead of a fixed interval");
        Check(CountOccurrences(toolSource, "PauseDeletion(\"stopped\")") >= 3,
            "Every stop entry pauses through the same capturing path");
        Check(MethodBody(toolSource, "public void ClearPending()").Contains("PauseDeletion("),
            "Clearing the pending selection captures its remainder");
        Check(MethodBody(toolSource, "public void ReevaluatePending()").Contains("PauseDeletion("),
            "Re-evaluating the pending selection captures its remainder");
        Check(toolSource.Contains("DeletionGate.TryClaimSubmit(TierFor(CompatibilityGroup.DeletionSubmit)"),
            "Deletion submission consults the compatibility gate");
        Check(toolSource.Contains("DeletionGate.TryClaimRebuild(TierFor(CompatibilityGroup.UndoRebuild)"),
            "Undo rebuild consults the compatibility gate");
        Check(undoSource.Contains("CanRebuildNow"),
            "The undo stepper refuses to emit when the rebuild tier is unavailable");
        Check(compatibilitySource.Contains("IsGroupAvailable(CompatibilityGroup.SearchAndPick)"),
            "The run gate reads the search tier rather than a fixed answer");
        Check(toolSource.Contains("length = i > 0 ? i : 1"),
            "Roots queued before an oversized facility still go first");
        Check(!MethodBody(toolSource, "private void FinishBatchObservation()").Contains("m_OversizedRoots.Clear()"),
            "Finishing one batch does not forget the oversized facilities of the whole task");
        Check(toolSource.Contains("m_OversizedRoots.Clear()"),
            "The oversized set is still rebuilt when a task starts");
        Check(toolSource.Contains("\"deletion_observe_timeout\""),
            "A stalled observation reports a timeout instead of claiming the selection changed");        Check(CountOccurrences(toolSource, "\"deletion_blocked_compat\"") >= 3,
            "A compatibility refusal is reported as its own reason");
        string localizationSource = ReadSource("Localization.cs");
        Check(CountOccurrences(localizationSource, "MarqueeDelete.DeletionObserveTimeout") == 2,
            "The observation-timeout text exists in both languages");
        Check(CountOccurrences(localizationSource, "MarqueeDelete.DeletionBlockedCompat") == 2,
            "The compatibility-refusal text exists in both languages");
        int confirmGate = MethodBody(toolSource, "private void SubmitConfirmedDeletion(", 3000).IndexOf("CanSubmitDeletionNow", StringComparison.Ordinal);
        int confirmClear = MethodBody(toolSource, "private void SubmitConfirmedDeletion(", 3000).IndexOf("SetPending(null)", StringComparison.Ordinal);
        Check(confirmGate >= 0 && confirmClear > confirmGate,
            "A refused confirmation keeps the preview because the gate is checked before the state is cleared");

        string confirmBody = MethodBody(toolSource, "private void SubmitConfirmedDeletion(", 3000);
        Check(confirmBody.IndexOf("m_InterruptedDeletion = null", StringComparison.Ordinal) >
              confirmBody.IndexOf("CanSubmitDeletionNow", StringComparison.Ordinal),
            "The interrupted record survives a refusal because it is cleared only after the gate passes");
        Check(confirmBody.Contains("m_InterruptedReview = m_PendingResult"),
            "A refused review keeps its identity by pointing the review marker at the pending result");

        string autoBody = MethodBody(toolSource, "private bool InitializePendingFromShape(", 3000);
        Check(autoBody.IndexOf("CanSubmitDeletionNow", StringComparison.Ordinal) >= 0 &&
              autoBody.IndexOf("CanSubmitDeletionNow", StringComparison.Ordinal) <
              autoBody.IndexOf("SetPending(null)", StringComparison.Ordinal),
            "The automatic-delete path checks the gate before it clears the freshly drawn selection");

        Check(toolSource.Contains("DeletionGate.TryClaimSubmit") && toolSource.Contains("DeletionGate.TryClaimRebuild"),
            "Both write gates run through the predicate the offline regression executes");

        // --- Stage A judgement layer: the four tiers as Compatibility.cs itself derives them ---
        // Nothing has initialized the layer yet, so every group must read as unavailable rather than as
        // a tier that happens to be the enum default for another reason.
        Check(!Compatibility.IsInitialized, "The compatibility layer starts out uninitialized");
        Check(Compatibility.GetTier(CompatibilityGroup.SearchAndPick) == CompatibilityTier.Unavailable &&
              Compatibility.GetTier(CompatibilityGroup.DeletionSubmit) == CompatibilityTier.Unavailable &&
              Compatibility.GetTier(CompatibilityGroup.UndoRebuild) == CompatibilityTier.Unavailable &&
              Compatibility.GetTier(CompatibilityGroup.UIAccess) == CompatibilityTier.Unavailable,
            "Before initialization no group claims any capability");
        Check(!Compatibility.CanRun && !Compatibility.CanSubmitDeletion && !Compatibility.CanUndoRebuild && !Compatibility.IsPreviewOnly,
            "Before initialization nothing may run, submit, rebuild, or read as preview-only");
        Check(Compatibility.GetTier((CompatibilityGroup)99) == CompatibilityTier.Unavailable,
            "A group outside the table reads as unavailable instead of throwing");

        Compatibility.Initialize();
        Check(Compatibility.IsInitialized, "Initialization is recorded");
        Check(Compatibility.DetectedMvid.Length == 0,
            "Offline there is no Game assembly, so no build fingerprint is detected");

        // Missing symbols close a group outright, and they are named so a bug report can quote them.
        GroupCompatibilityResult search = Compatibility.GetGroupResult(CompatibilityGroup.SearchAndPick);
        Check(search.Tier == CompatibilityTier.Unavailable && search.MissingMembers != null && search.MissingMembers.Count > 0,
            "A group with missing symbols is unavailable and lists what is missing");
        GroupCompatibilityResult ui = Compatibility.GetGroupResult(CompatibilityGroup.UIAccess);
        Check(ui.Tier == CompatibilityTier.Unavailable && ui.MissingMembers.Count > 0,
            "The UI group is closed when its binding types are missing");
        Check(!Compatibility.CanRun,
            "No search symbols means the tool may not run at all");

        // Resolvable symbols plus an unknown build is exactly the preview-only tier: reads are allowed,
        // writes are not.
        GroupCompatibilityResult submit = Compatibility.GetGroupResult(CompatibilityGroup.DeletionSubmit);
        Check(submit.Tier == CompatibilityTier.PreviewOnly && submit.MissingMembers.Count == 0,
            "Resolvable deletion symbols on an unknown build are preview-only, not verified");
        GroupCompatibilityResult rebuild = Compatibility.GetGroupResult(CompatibilityGroup.UndoRebuild);
        Check(rebuild.Tier == CompatibilityTier.PreviewOnly && rebuild.MissingMembers.Count == 0,
            "Resolvable rebuild symbols on an unknown build are preview-only");
        Check(!Compatibility.CanSubmitDeletion && !Compatibility.CanUndoRebuild,
            "Preview-only forbids both writes");
        Check(Compatibility.IsPreviewOnly, "Preview-only is reported so the UI can explain why");
        Check(submit.StatusMessage.Length > 0 && rebuild.StatusMessage.Length > 0 && search.StatusMessage.Length > 0,
            "Every group carries a message the settings panel can show verbatim");

        // The derived tiers must line up with the write gates the batch and undo paths consult.
        Check(!CompatibilityGate.AllowsDeletionSubmit((int)Compatibility.GetTier(CompatibilityGroup.DeletionSubmit)),
            "The batch gate refuses the tier Compatibility.cs derived");
        Check(!CompatibilityGate.AllowsUndoRebuild((int)Compatibility.GetTier(CompatibilityGroup.UndoRebuild)),
            "The undo gate refuses the tier Compatibility.cs derived");

        Compatibility.Initialize("00000000A0000000B0000000C00000000", "9.9.9");
        Check(Compatibility.TargetMvid == Compatibility.VerifiedGameMvid &&
              Compatibility.GetTier(CompatibilityGroup.DeletionSubmit) == CompatibilityTier.PreviewOnly,
            "A second Initialize is ignored instead of re-deriving the tiers mid-session");

        // --- Lanes: the game keeps no lane identity, so only the evidence it does expose is checked ---
        Check(LaneOwnershipPolicy.Decide(0, 0, 0) == LaneOwnershipStatus.RegeneratesWithRoad,
            "A path with no lanes needs no lane evidence");
        Check(LaneOwnershipPolicy.Decide(4, 4, 0) == LaneOwnershipStatus.RegeneratesWithRoad,
            "Lanes that all name this road as their owner let it rebuild");
        Check(LaneOwnershipPolicy.Decide(4, 0, 0) == LaneOwnershipStatus.NoOwnershipRecorded,
            "Lanes that name nobody are not evidence against the road");
        Check(LaneOwnershipPolicy.AllowsRebuild(4, 0, 0),
            "A network the game keeps no lane owner for still rebuilds on the junction evidence");
        Check(LaneOwnershipPolicy.Decide(4, 2, 2) == LaneOwnershipStatus.UnconfirmedOwnership,
            "A lane that names another road refuses the rebuild");
        Check(LaneOwnershipPolicy.Decide(-1, -1, -1) == LaneOwnershipStatus.MissingEvidence,
            "Unreadable lane counts are missing evidence rather than a clean answer");
        Check(!LaneOwnershipPolicy.AllowsRebuild(-1, -1, -1) && !LaneOwnershipPolicy.AllowsRebuild(4, 2, 2),
            "Both refusals keep the segment out of the rebuild");

        // The road is rebuilt only when the junctions are intact AND the lanes were its own. Either
        // refusal alone has to stop it, which is what the undo stepper's condition encodes.
        NetLinkStatus occupiedJunction = NetConnectivityPolicy.EvaluateEndpoint(10f, 10f, 10f, 10f, 3, 4, 0.5f);
        Check(occupiedJunction == NetLinkStatus.ForeignSplice && LaneOwnershipPolicy.AllowsRebuild(4, 4, 0),
            "A junction something else re-linked is refused even when the lane evidence is clean");
        NetLinkStatus movedJunction = NetConnectivityPolicy.EvaluateEndpoint(10f, 10f, 16f, 10f, 3, 3, 0.5f);
        Check(movedJunction == NetLinkStatus.Moved && !LaneOwnershipPolicy.AllowsRebuild(4, 3, 1),
            "A moved junction and a lane claimed by another road are two independent refusals");
        Check(occupiedJunction != NetLinkStatus.Reconnected && movedJunction != NetLinkStatus.Reconnected,
            "Neither refusal path reads as a reconnect");

        // --- Units held by an interruption: a count and the largest size, never the whole grouping ---
        int[] unitOfItem = { 0, 0, 1, -1, 2 };
        int[] unitSize = { 7, 3, 5 };
        UnitRemainder.Count(unitOfItem, unitOfItem.Length, unitSize, unitSize.Length, out int heldUnits, out int largestHeld);
        Check(heldUnits == 3, "Each unit with a pending member is counted once");
        Check(largestHeld == 7, "The largest pending unit reports its own entity count, not the item count");
        int[] onlyFirst = { 0, 0, 0 };
        UnitRemainder.Count(onlyFirst, onlyFirst.Length, unitSize, unitSize.Length, out int singleUnit, out int singleLargest);
        Check(singleUnit == 1 && singleLargest == 7, "Several leftovers of one facility collapse into one unit");
        int[] noneMapped = { -1, -1 };
        UnitRemainder.Count(noneMapped, noneMapped.Length, unitSize, unitSize.Length, out int noUnits, out int noLargest);
        Check(noUnits == 0 && noLargest == 0, "Leftovers outside every unit report nothing");
        UnitRemainder.Count(null, 0, null, 0, out int emptyUnits, out int emptyLargest);
        Check(emptyUnits == 0 && emptyLargest == 0, "An empty remainder is not an error");

        // --- Call-site guards for the two completions above ---
        Check(toolSource.Contains("public int InterruptedDeletionUnitCount") && toolSource.Contains("public int InterruptedDeletionLargestUnit"),
            "The interface can read how many facilities an interruption holds");
        Check(MethodBody(toolSource, "private void CountRemainingUnits(").Contains("UnitRemainder.Count("),
            "The interruption counts units through the predicate the regression executes");
        Check(MethodBody(toolSource, "private void PauseDeletion(", 3000).Contains("CountRemainingUnits("),
            "Capturing an interruption records the unit counts with the remainder");
        string interruptedBody = MethodBody(toolSource, "private sealed class InterruptedDeletion", 500);
        Check(interruptedBody.Contains("public int UnitCount;") && interruptedBody.Contains("public int LargestUnit;"),
            "The interruption record carries the unit counts, not just the flat item list");
        Check(undoSource.Contains("LaneEvidenceConfirmed(op.Net)"),
            "The undo stepper refuses a segment whose lane evidence is unconfirmed");
        Check(undoSource.Contains("ReadLaneEvidence(em, entity, out rec.LaneCount, out rec.LaneOwned, out rec.LaneForeign)"),
            "The lane evidence is captured with the segment");
        Check(MethodBody(undoSource, "private static void ReadLaneEvidence(", 1800).Contains("m_Owner"),
            "Lane ownership is read from the relation the game itself keeps");
        Check(undoSource.Contains("LaneUnconfirmed: {m_LaneUnconfirmed}"),
            "A refused lane rebuild is reported in the restore audit");
        Check(!MethodBody(undoSource, "private struct UndoNetRecord", 1400).Contains("Lane lane") &&
              !MethodBody(undoSource, "private struct UndoNetRecord", 1400).Contains("m_SubLane"),
            "The segment record stores lane counts, never a lane entity that cannot outlive its road");

        Console.WriteLine($"Deletion regression: {checks} passed (offline stand-ins; in-game playback still requires validation).");
    }

    // The production files are read from the checkout so a call-site mistake — passing the wrong
    // variable into a pure helper — still fails the offline run.
    static string ReadSource(string fileName)
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            string nested = Path.Combine(dir, "MarqueeDelete", fileName);
            if (File.Exists(nested)) return File.ReadAllText(nested);
            dir = Path.GetDirectoryName(dir);
        }
        return string.Empty;
    }

    static string MethodBody(string source, string signature, int span = 1200)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return string.Empty;
        int end = Math.Min(source.Length, at + span);
        return source.Substring(at, end - at);
    }

    static int CountOccurrences(string text, string needle)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return 0;
        int count = 0;
        int at = text.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    static DeletionUnit FindByRoot(List<DeletionUnit> units, Entity root)    {
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].Root == root) return units[i];
        }
        return null;
    }

    static bool SequenceEqual(List<Entity> a, List<Entity> b)
    {
        if (a == null || b == null) return a == b;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    static bool SequenceEqual(List<int> a, List<int> b)
    {
        if (a == null || b == null) return a == b;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }
}
