// Drives the production restore path (DeletionUndo.cs) offline: two adjacent segments sharing a
// junction, restored in separate batches, with the world replayed the way the game's barrier would.
// Unity.Entities itself cannot run outside the player (its World allocator is an internal call), so
// EntityManager and EntityCommandBuffer are shims and the component structs are transcribed from
// Game.dll. What this proves is that the real batching code and the real junction verdicts run and
// reach the intended decisions; it does not prove the game accepts the emitted courses.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.Mathematics;
using MarqueeDelete;

internal static class HarnessProgram
{
    private const int PrefabIndex = 5000;
    private const int NodeAIndex = 10;
    private const int NodeBIndex = 11;
    private const int NodeCIndex = 12;
    private const int EdgeABIndex = 20;
    private const int EdgeBCIndex = 21;
    private const int ExternalEdgeIndex = 700000;

    private static int s_Failures;
    private static int s_Checks;

    public static int Main()
    {
        Mod.Settings = new Settings { MaxUndoHistorySteps = 20 };

        // Bypass compatibility tier gate in offline harness so GlobalUndoManager runs end-to-end.
        var groupsField = typeof(Compatibility).GetField("s_Groups", BindingFlags.Static | BindingFlags.NonPublic);
        if (groupsField?.GetValue(null) is GroupCompatibilityResult[] groups)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = new GroupCompatibilityResult { Group = (CompatibilityGroup)i, Tier = CompatibilityTier.Verified };
            }
        }

        Run("scenario 1 (cross-batch, shared junction, no pause)", ScenarioCrossBatchRestoresBothSegments);
        Run("scenario 2 (foreign edge spliced during a pause)", ScenarioExternalSpliceBetweenBatches);
        Run("scenario 3 (resume tolerates its own rebuilt edge)", ScenarioResumeToleratesOwnRebuiltEdge);
        Run("scenario 4 (dead-end rebuild is still this undo's own edge)", ScenarioDeadEndRebuildIsRecognised);
        Run("scenario 5 (no pause: a foreign edge is not noticed - accepted)", ScenarioNoPauseIgnoresForeignEdge);
        Run("scenario 6 (replaced junction at the same spot refuses)", ScenarioReplacedJunctionRefuses);
        Run("scenario 7 (missing verdict on a resume refuses)", ScenarioCacheMissRefusesInsteadOfWriting);
        Run("scenario 8 (a shared dead junction is read once per resume)", ScenarioSharedDeadJunctionIsLookedUpOnce);
        Run("scenario 9 (a moved junction between batches refuses)", ScenarioMovedJunctionBetweenBatchesRefuses);
        Run("scenario 10 (a far same-shape edge is not this undo's)", ScenarioFarSameShapeEdgeIsNotOwn);
        Run("scenario 11 (an elevated junction does not hide the ground one)", ScenarioElevatedJunctionDoesNotHideGround);
        Run("scenario 12 (both junctions rebuilt are still this undo's own)", ScenarioBothEndsRebuiltStillCountAsOwn);
        Run("scenario 13 (stacked endpoints are looked up at their own height)", ScenarioStackedEndpointsAreLookedUpSeparately);
        Run("scenario 14 (a removed rebuild does not excuse two splices)", ScenarioRemovedRebuildDoesNotExcuseTwoSplices);
        Run("scenario 15 (an untouched neighbour keeps the claim for the rebuilt road)", ScenarioPreExistingNeighbourKeepsItsOwnClaim);
        Run("scenario 16 (strict match outranks a one-ended match)", ScenarioStrictMatchOutranksLooseOne);
        Run("scenario 17 (a spent claim survives another resume)", ScenarioSpentClaimSurvivesAnotherResume);
        Run("scenario 18 (both ends must stand where the recorded segment stood)", ScenarioGeometryNeedsBothEnds);
        Run("scenario 19 (atomic 1-frame BuildingUnit with SubNet, node-only portal, ParentMesh, CompositionFlags, upgrade)", ScenarioAtomicBuildingUnitSingleFrame);
        Run("scenario 20 (cross-building isolation keeps adjacent buildings in separate frames)", ScenarioCrossBuildingSubNetIsolation);
        Run("scenario 21 (external live junction reconnects without DisableMerge and rebuilt Attached parent relinks)", ScenarioExternalJunctionAndRebuiltAttachedParent);
        Run("scenario 22 (Attached edge outside parent SubNet buffer is captured standalone)", ScenarioAttachedEdgeOutsideSubNetNotDropped);
        Run("scenario 23 (pre-frozen batched capture preserves unmutated state and skips unsubmitted batches)", ScenarioPreFrozenBatchedCapture);
        Run("scenario 24 (non-physical ServiceUpgrade without Transform captures and restores)", ScenarioNonPhysicalServiceUpgrade);
        Run("scenario 25 (composite SubObject SubNet and SubArea defer across Boundary until SubObjectSystem spawns SubObject)", ScenarioCompositeSubObjectSubs);
        Run("scenario 26 (ground SubNet keeps ParentMesh -1, omits spurious FreeHeight, and queues connection refresh)", ScenarioGroundSubNetAndConnectionRefresh);
        Run("scenario 27 (oversized batched capture caps PrepareBatchedCapture and prioritizes roads/buildings over trees)", ScenarioMegaBatchPrioritizesNetsAndBuildings);
        Run("scenario 28 (uninterrupted boundary node drift snaps curve, cross-batch road binds rebuilt junction, driveway omits DisableMerge)", ScenarioBoundaryDriftAndCrossBatchJunctionBinding);
        Run("scenario 29 (prefab SubNet preserves ParentMesh on inverted LHT curve without DisableMerge)", ScenarioPrefabSubNetParentMeshRecovery);
        Run("scenario 30 (park prefab-driven atomic rebuild: shared m_NodeIndex averaging, ParentMesh, Upgrades, SubArea, LHT inversion)", ScenarioParkPrefabDrivenAtomicRebuildAndLht);
        Run("scenario 31 (atomic SubObjectUnit with prefab SubNets and OnWater lazy water reader)", ScenarioSubObjectUnitPrefabSubNetsAndWater);
        Run("scenario 32 (MarqueeDeleteUndoSystem cleans up orphaned permanent CourseSplitSystem carriers only during/after undo)", ScenarioMarqueeDeleteUndoSystemCarrierCleanup);
        Run("scenario 33 (ground terrain adjust, self-loop flags, inline/deferred upgrade prefab SubNets, non-inverting LHT upgrade flip, invalid SubNet skip accounting)", ScenarioPrefabSubNetEdgeCases);
        Run("scenario 34 (global undo Combine straight extension shrinks in-place without delete/rebuild)", ScenarioGlobalUndoCombineShrinkInPlace);
        Run("scenario 35 (global undo Replace adjacent road restores in-place and deletes only new branch)", ScenarioGlobalUndoReplaceAdjacentInPlace);
        Run("scenario 36 (global undo Split mid-segment T-junction preserves both halves and deletes only new branch)", ScenarioGlobalUndoSplitPreservesHalves);
        Run("scenario 37 (global undo Split with auto-demolished roadside building preserves halves and restores building)", ScenarioGlobalUndoSplitWithAutoDemolishedBuilding);
        Run("scenario 38 (global undo Combine skips shrinking when later road attaches to far node)", ScenarioGlobalUndoCombineTopologySafetyGuard);
        Run("scenario 39 (global undo Combine into pre-existing intersection falls back to Delete+Rebuild)", ScenarioGlobalUndoCombineExistingIntersectionFallback);
        Run("scenario 40 (global undo Node Replace remaps adjacent endpoints and replay refuses unrelated Edge rewiring)", ScenarioGlobalUndoNodeReplaceRemapAndRewireGuard);

        Console.WriteLine(s_Failures == 0
            ? "Undo harness: 40 scenarios, " + s_Checks + " checks passed"
            : "Undo harness: " + s_Failures + " of " + s_Checks + " checks failed");
        return s_Failures == 0 ? 0 : 1;
    }

    /// <summary>Runs one scenario and reports whether it added any failure.</summary>
    private static void Run(string name, Action scenario)
    {
        int before = s_Failures;
        scenario();
        Console.WriteLine((s_Failures == before ? "PASS: " : "FAIL: ") + name);
    }

    private sealed class Fixture
    {
        public readonly EntityManager Em = new EntityManager();
        public Entity Prefab;
        public Entity NodeA, NodeB, NodeC;
        public Entity EdgeAB, EdgeBC;
        public DeletionPlan Plan;
    }

    private static Fixture Build()
    {
        var fx = new Fixture();
        var em = fx.Em;

        fx.Prefab = em.Make(PrefabIndex);
        em.AddComponentData(fx.Prefab, new Game.Prefabs.NetData());

        fx.NodeA = MakeJunction(em, NodeAIndex, 0f);
        fx.NodeB = MakeJunction(em, NodeBIndex, 100f);
        fx.NodeC = MakeJunction(em, NodeCIndex, 200f);

        fx.EdgeAB = MakeEdge(em, EdgeABIndex, fx.Prefab, fx.NodeA, fx.NodeB, 0f, 100f);
        fx.EdgeBC = MakeEdge(em, EdgeBCIndex, fx.Prefab, fx.NodeB, fx.NodeC, 100f, 200f);

        Link(em, fx.NodeA, fx.EdgeAB);
        Link(em, fx.NodeB, fx.EdgeAB);
        Link(em, fx.NodeB, fx.EdgeBC);
        Link(em, fx.NodeC, fx.EdgeBC);

        var roots = new HashSet<Entity> { fx.EdgeAB, fx.EdgeBC };
        fx.Plan = new DeletionPlan(
            roots,
            new HashSet<Entity>(roots),
            new HashSet<Entity> { fx.NodeA, fx.NodeB, fx.NodeC },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        return fx;
    }

    private static Entity MakeJunction(EntityManager em, int index, float x)
    {
        Entity node = em.Make(index);
        em.AddComponentData(node, new Game.Net.Node
        {
            m_Position = new float3(x, 0f, 0f),
            m_Rotation = quaternion.identity,
        });
        em.AddBuffer<Game.Net.ConnectedEdge>(node);
        return node;
    }

    private static Entity MakeEdge(EntityManager em, int index, Entity prefab, Entity start, Entity end, float fromX, float toX)
    {
        Entity edge = em.Make(index);
        em.AddComponentData(edge, new Game.Prefabs.PrefabRef { m_Prefab = prefab });
        em.AddComponentData(edge, new Game.Net.Edge { m_Start = start, m_End = end });
        em.AddComponentData(edge, new Game.Net.Curve { m_Bezier = Straight(fromX, toX), m_Length = toX - fromX });

        // Two lanes, each naming this road as its owner: the shape most networks have, and the one
        // that must not be mistaken for a competing claim.
        var lanes = em.AddBuffer<Game.Net.SubLane>(edge);
        for (int i = 0; i < 2; i++)
        {
            Entity lane = em.Make(300 + index * 10 + i);
            em.AddComponentData(lane, new Game.Common.Owner { m_Owner = edge });
            lanes.Add(new Game.Net.SubLane { m_SubLane = lane, m_PathMethods = Game.Net.PathMethod.None });
        }
        return edge;
    }

    /// <summary>A junction at an arbitrary position, so a test can place one at a height or off-axis.</summary>
    private static Entity MakeJunctionAt(EntityManager em, int index, float3 position)
    {
        Entity node = em.Make(index);
        em.AddComponentData(node, new Game.Net.Node
        {
            m_Position = position,
            m_Rotation = quaternion.identity,
        });
        em.AddBuffer<Game.Net.ConnectedEdge>(node);
        return node;
    }

    /// <summary>A road whose recorded length is given directly, so two roads of different spans can share a length.</summary>
    private static Entity MakeEdgeWithLength(EntityManager em, int index, Entity prefab, Entity start, Entity end, float length)
    {
        Entity edge = em.Make(index);
        em.AddComponentData(edge, new Game.Prefabs.PrefabRef { m_Prefab = prefab });
        em.AddComponentData(edge, new Game.Net.Edge { m_Start = start, m_End = end });
        em.AddComponentData(edge, new Game.Net.Curve { m_Bezier = Straight(0f, length), m_Length = length });

        var lanes = em.AddBuffer<Game.Net.SubLane>(edge);
        for (int i = 0; i < 2; i++)
        {
            Entity lane = em.Make(300 + index * 10 + i);
            em.AddComponentData(lane, new Game.Common.Owner { m_Owner = edge });
            lanes.Add(new Game.Net.SubLane { m_SubLane = lane, m_PathMethods = Game.Net.PathMethod.None });
        }
        return edge;
    }

    private static Colossal.Mathematics.Bezier4x3 Straight(float fromX, float toX)
    {
        float third = (toX - fromX) / 3f;
        return new Colossal.Mathematics.Bezier4x3
        {
            a = new float3(fromX, 0f, 0f),
            b = new float3(fromX + third, 0f, 0f),
            c = new float3(toX - third, 0f, 0f),
            d = new float3(toX, 0f, 0f),
        };
    }

    private static void Link(EntityManager em, Entity node, Entity edge)
    {
        var links = em.AddBuffer<Game.Net.ConnectedEdge>(node);
        links.Add(new Game.Net.ConnectedEdge { m_Edge = edge });
    }

    private static void Unlink(EntityManager em, Entity node, Entity edge)
    {
        var links = em.GetBuffer<Game.Net.ConnectedEdge>(node);
        for (int i = links.Count - 1; i >= 0; i--)
        {
            if (links[i].m_Edge.Equals(edge)) links.RemoveAt(i);
        }
    }

    /// <summary>Removes the segment the way the game does: the junctions stop listing it first.</summary>
    private static void DeleteSegment(EntityManager em, Entity edge)
    {
        var e = em.GetComponentData<Game.Net.Edge>(edge);
        Unlink(em, e.m_Start, edge);
        Unlink(em, e.m_End, edge);
        em.DestroyEntity(edge);
    }

    private static DeletionUndoManager Capture(EntityManager em, DeletionPlan plan)
    {
        var manager = new DeletionUndoManager();
        manager.CaptureBeforeDelete(em, plan);
        return manager;
    }

    private static EntityCommandBuffer StepOnce(EntityManager em, DeletionUndoManager manager, out bool completed)
    {
        // The manager gates on Time.frameCount, so every batch (including road->building boundaries) needs enough frames.
        UnityEngine.Time.frameCount += 5;
        var ecb = new EntityCommandBuffer();
        manager.Step(em, ecb, out completed);
        return ecb;
    }

    private static int NetCarriers(EntityCommandBuffer ecb) =>
        ecb.Commands.Count(c => c.Type == typeof(Game.Tools.NetCourse));

    /// <summary>
    /// Applies one barrier the way the game would: command entities become real, then every permanent
    /// net course turns into a road that meets the junctions it names, creating one where it names
    /// none. The restored road gets a fresh entity, exactly as the game gives it one.
    /// </summary>
    private static List<Entity> ReplayBarrier(EntityManager em, EntityCommandBuffer ecb)
    {
        foreach (EntityCommandBuffer.Command command in ecb.Commands)
        {
            switch (command.Kind)
            {
                case EntityCommandBuffer.Kind.Create:
                    if (!em.Exists(command.Entity)) em.RawCreate(command.Entity.Index);
                    continue;
                case EntityCommandBuffer.Kind.RemoveComponent:
                    if (em.Exists(command.Entity)) em.Components(command.Entity).Remove(command.Type);
                    continue;
                default:
                    if (!em.Exists(command.Entity)) continue;
                    Dictionary<Type, object> components = em.Components(command.Entity);
                    if (command.Kind == EntityCommandBuffer.Kind.AddBuffer)
                        components[typeof(DynamicBuffer<>).MakeGenericType(command.Type)] = command.Value;
                    else
                        components[command.Type] = command.Value;
                    continue;
            }
        }

        var carriers = new List<Entity>();
        foreach (Entity entity in em.All.ToArray())
        {
            if (em.HasComponent<Game.Tools.NetCourse>(entity) && em.HasComponent<Game.Tools.CreationDefinition>(entity))
                carriers.Add(entity);
        }

        var created = new List<Entity>();
        foreach (Entity carrier in carriers)
        {
            var definition = em.GetComponentData<Game.Tools.CreationDefinition>(carrier);
            var course = em.GetComponentData<Game.Tools.NetCourse>(carrier);
            Entity start = ResolveJunction(em, course.m_StartPosition);
            Entity end = ResolveJunction(em, course.m_EndPosition);

            Entity edge = em.CreateEntity();
            em.AddComponentData(edge, new Game.Prefabs.PrefabRef { m_Prefab = definition.m_Prefab });
            em.AddComponentData(edge, new Game.Net.Edge { m_Start = start, m_End = end });
            em.AddComponentData(edge, new Game.Net.Curve { m_Bezier = course.m_Curve, m_Length = course.m_Length });
            Link(em, start, edge);
            Link(em, end, edge);

            em.DestroyEntity(carrier);
            created.Add(edge);
        }
        return created;
    }

    private static Entity ResolveJunction(EntityManager em, Game.Tools.CoursePos coursePos)
    {
        if (coursePos.m_Entity != Entity.Null && em.Exists(coursePos.m_Entity)) return coursePos.m_Entity;

        Entity node = em.CreateEntity();
        em.AddComponentData(node, new Game.Net.Node
        {
            m_Position = coursePos.m_Position,
            m_Rotation = coursePos.m_Rotation,
        });
        em.AddBuffer<Game.Net.ConnectedEdge>(node);
        return node;
    }

    private static int LiveEdgeCount(EntityManager em) =>
        em.All.Count(e => em.HasComponent<Game.Net.Edge>(e));

    private static string LastAuditLine()
    {
        if (MarqueeDeleteToolSystem.StatusLines.Count == 0) return string.Empty;
        return MarqueeDeleteToolSystem.StatusLines[MarqueeDeleteToolSystem.StatusLines.Count - 1];
    }

    private static bool LastAuditContains(string fragment) => LastAuditLine().Contains(fragment);

    /// <summary>Runs batches until the manager reports completion, replaying each barrier.</summary>
    private static void RunToCompletion(EntityManager em, DeletionUndoManager manager, List<Entity> rebuilt, int maxBatches = 12)
    {
        for (int i = 0; i < maxBatches; i++)
        {
            EntityCommandBuffer ecb = StepOnce(em, manager, out bool completed);
            List<Entity> batch = ReplayBarrier(em, ecb);
            rebuilt.AddRange(batch);
            if (completed) return;
        }
        Check(false, "the restore finished within " + maxBatches + " batches");
    }

    /// <summary>The manager's active snapshot, or null once the restore completed and it moved to history.</summary>
    private static object ActiveSnapshot(DeletionUndoManager manager)
    {
        FieldInfo trackerField = typeof(DeletionUndoManager).GetField("m_Tracker", BindingFlags.Instance | BindingFlags.NonPublic);
        object tracker = trackerField.GetValue(manager);
        return tracker.GetType().GetProperty("ActiveSnapshot").GetValue(tracker);
    }

    private static NetVerdictCache ForceVerdictCache(DeletionUndoManager manager, int entries)
    {
        object snapshot = ActiveSnapshot(manager);
        FieldInfo verdicts = snapshot.GetType().GetField("Verdicts");
        var forced = new NetVerdictCache(entries);
        verdicts.SetValue(snapshot, forced);
        return forced;
    }

    private static bool ReadIdentityGate(DeletionUndoManager manager)
    {
        object snapshot = ActiveSnapshot(manager);
        return snapshot != null && (bool)snapshot.GetType().GetField("IdentityGate").GetValue(snapshot);
    }

    private static void Check(bool condition, string what)
    {
        s_Checks++;
        if (condition) return;
        s_Failures++;
        Console.WriteLine("    check failed: " + what);
    }

    // 1. Two segments share a junction and are forced into separate batches. The second must still
    //    be submitted: its junction now holds the edge this undo rebuilt in batch one.
    private static void ScenarioCrossBatchRestoresBothSegments()
    {
        Fixture fx = Build();
        EntityManager em = fx.Em;
        DeletionUndoManager manager = Capture(em, fx.Plan);
        DeleteSegment(em, fx.EdgeAB);
        DeleteSegment(em, fx.EdgeBC);
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 1: the undo starts");

        EntityCommandBuffer first = StepOnce(em, manager, out bool completedAfterFirst);
        Check(NetCarriers(first) == 1, "scenario 1: the first batch submits exactly one segment");
        Check(!completedAfterFirst, "scenario 1: the first batch does not finish the restore");
        List<Entity> rebuilt = ReplayBarrier(em, first);
        Check(rebuilt.Count == 1, "scenario 1: the first batch rebuilt one segment");

        var rest = new List<Entity>();
        RunToCompletion(em, manager, rest);
        Check(rest.Count == 1, "scenario 1: the second segment is submitted after the first landed");
        Check(LiveEdgeCount(em) == 2, "scenario 1: both segments exist after the restore");
        Check(LastAuditContains("Restored: 2"), "scenario 1: both segments counted as restored (" + LastAuditLine() + ")");
        Check(LastAuditContains("Skipped: 0"), "scenario 1: nothing skipped (" + LastAuditLine() + ")");

        var rebuiltEdges = rebuilt.Concat(rest).ToList();
        var endpoints = rebuiltEdges
            .Select(e => em.GetComponentData<Game.Net.Edge>(e))
            .Select(e => (start: e.m_Start.Index, end: e.m_End.Index))
            .OrderBy(p => p.start).ToList();
        Check(endpoints.Count == 2 && endpoints[0] == (NodeAIndex, NodeBIndex) && endpoints[1] == (NodeBIndex, NodeCIndex),
            "scenario 1: each rebuilt segment meets its recorded junctions");
    }

    // 2. The player splices a new road onto the shared junction while the restore is paused. The
    //    segment that would join it must not be submitted.
    private static void ScenarioExternalSpliceBetweenBatches()
    {
        Fixture fx = Build();
        EntityManager em = fx.Em;
        DeletionUndoManager manager = Capture(em, fx.Plan);
        DeleteSegment(em, fx.EdgeAB);
        DeleteSegment(em, fx.EdgeBC);
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 2: the undo starts");

        EntityCommandBuffer first = StepOnce(em, manager, out _);
        ReplayBarrier(em, first);

        Entity foreignNode = MakeJunction(em, 800000, 100f);
        Entity foreign = MakeEdge(em, ExternalEdgeIndex, fx.Prefab, fx.NodeB, foreignNode, 100f, 200f);
        Link(em, fx.NodeB, foreign);
        Link(em, foreignNode, foreign);

        Check(manager.CancelActiveUndo(), "scenario 2: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 2: the restore resumes");

        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 0, "scenario 2: a segment whose junction gained a foreign edge is not submitted");
        Check(LiveEdgeCount(em) == 2, "scenario 2: only the rebuilt segment and the player's road exist");
        Check(LastAuditContains("NetIdentityRejected: 1"), "scenario 2: the refusal is counted (" + LastAuditLine() + ")");
    }

    // 3. A resume must tolerate the edge this undo rebuilt for its own earlier batch, or the second
    //    segment would be refused as a newcomer.
    private static void ScenarioResumeToleratesOwnRebuiltEdge()
    {
        Fixture fx = Build();
        EntityManager em = fx.Em;
        DeletionUndoManager manager = Capture(em, fx.Plan);
        DeleteSegment(em, fx.EdgeAB);
        DeleteSegment(em, fx.EdgeBC);
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 3: the undo starts");

        EntityCommandBuffer first = StepOnce(em, manager, out _);
        List<Entity> rebuiltFirst = ReplayBarrier(em, first);
        Check(rebuiltFirst.Count == 1, "scenario 3: the first batch rebuilt one segment");

        Check(manager.CancelActiveUndo(), "scenario 3: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 3: the restore resumes");
        Check(ReadIdentityGate(manager), "scenario 3: the resume raised the identity gate");

        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 1,
            "scenario 3: the second segment is submitted even though the shared junction now holds this undo's own edge");
        ReplayBarrier(em, second);
        Check(LiveEdgeCount(em) == 2, "scenario 3: both segments exist after the resume");
        Check(LastAuditContains("NetIdentityRejected: 0"), "scenario 3: no false identity refusal (" + LastAuditLine() + ")");
    }

    // 4. The first batch rebuilds a segment whose far junction died with the road, so the emission
    //    submits Entity.Null there and the game builds a fresh junction. On the resume the shared
    //    junction holds that edge; it must still be recognised as this undo's own work, otherwise the
    //    second segment is refused. This is the regression for demanding the recorded junctions on
    //    both ends, which never matched a dead-end rebuild.
    private static void ScenarioDeadEndRebuildIsRecognised()
    {
        Fixture fx = Build();
        EntityManager em = fx.Em;
        DeletionUndoManager manager = Capture(em, fx.Plan);
        DeleteSegment(em, fx.EdgeAB);
        DeleteSegment(em, fx.EdgeBC);
        em.DestroyEntity(fx.NodeA); // the cul-de-sac end went with the road

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 4: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool completedAfterFirst);
        Check(NetCarriers(first) == 1, "scenario 4: the first batch submits the dead-end segment");
        Check(!completedAfterFirst, "scenario 4: the first batch does not finish the restore");
        List<Entity> rebuilt = ReplayBarrier(em, first);
        Check(rebuilt.Count == 1, "scenario 4: one segment was rebuilt");
        if (rebuilt.Count == 1)
        {
            Game.Net.Edge replacement = em.GetComponentData<Game.Net.Edge>(rebuilt[0]);
            Check(em.Exists(replacement.m_Start) && em.HasComponent<Game.Net.Node>(replacement.m_Start),
                "scenario 4: a fresh junction stands where the dead one was");
        }

        Check(manager.CancelActiveUndo(), "scenario 4: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 4: the restore resumes");
        Check(ReadIdentityGate(manager), "scenario 4: the resume raised the identity gate");

        EntityCommandBuffer second = StepOnce(em, manager, out bool completedAfterSecond);
        Check(NetCarriers(second) == 1,
            "scenario 4: the second segment is submitted although the shared junction holds a dead-end rebuild");
        Check(completedAfterSecond, "scenario 4: the second batch finishes the restore");
        ReplayBarrier(em, second);
        Check(LiveEdgeCount(em) == 2, "scenario 4: both segments exist after the restore");
        Check(LastAuditContains("Restored: 2"), "scenario 4: both segments counted as restored (" + LastAuditLine() + ")");
        Check(LastAuditContains("NetIdentityRejected: 0"), "scenario 4: no false identity refusal (" + LastAuditLine() + ")");
        Check(LastAuditContains("NetVerdictMiss: 0"), "scenario 4: the resumed verdicts covered every segment (" + LastAuditLine() + ")");
    }

    // 5. No pause: the second batch checks junction positions only and never reads edge identities,
    //    so a road the player spliced between batches is not noticed and the segment is submitted
    //    anyway. That is the trade the user accepted for stability. This scenario records the current
    //    behaviour; passing it does not mean the splice was handled.
    private static void ScenarioNoPauseIgnoresForeignEdge()
    {
        Fixture fx = Build();
        EntityManager em = fx.Em;
        DeletionUndoManager manager = Capture(em, fx.Plan);
        DeleteSegment(em, fx.EdgeAB);
        DeleteSegment(em, fx.EdgeBC);
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 5: the undo starts");

        EntityCommandBuffer first = StepOnce(em, manager, out _);
        ReplayBarrier(em, first);

        Entity foreignNode = MakeJunction(em, 800000, 50f);
        Entity foreign = MakeEdge(em, ExternalEdgeIndex, fx.Prefab, fx.NodeB, foreignNode, 50f, 100f);
        Link(em, fx.NodeB, foreign);
        Link(em, foreignNode, foreign);

        var rest = new List<Entity>();
        RunToCompletion(em, manager, rest);
        Check(rest.Count == 1, "scenario 5: the second segment is submitted although a foreign edge was spliced in");
        Check(LastAuditContains("Restored: 2"), "scenario 5: both segments counted as restored (" + LastAuditLine() + ")");
        Check(LastAuditContains("NetIdentityRejected: 0"),
            "scenario 5: identity is not consulted without a pause (accepted trade-off, not a safety claim)");
    }

    // 6. The player demolishes the shared junction and builds a new one in its place with a road of
    //    their own. The recorded junction entity is gone but the spot is not empty, so the segment
    //    would be spliced into the player's junction. It must be refused.
    private static void ScenarioReplacedJunctionRefuses()
    {
        Fixture fx = Build();
        EntityManager em = fx.Em;
        DeletionUndoManager manager = Capture(em, fx.Plan);
        DeleteSegment(em, fx.EdgeAB);
        DeleteSegment(em, fx.EdgeBC);
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 6: the undo starts");

        EntityCommandBuffer first = StepOnce(em, manager, out _);
        ReplayBarrier(em, first);

        em.DestroyEntity(fx.NodeB);
        Entity replacement = MakeJunction(em, 800010, 100f);
        Entity foreignNode = MakeJunction(em, 800011, 50f);
        Entity foreign = MakeEdge(em, 800012, fx.Prefab, replacement, foreignNode, 50f, 100f);
        Link(em, replacement, foreign);
        Link(em, foreignNode, foreign);

        Check(manager.CancelActiveUndo(), "scenario 6: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 6: the restore resumes");

        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 0,
            "scenario 6: a segment aiming at a junction the player rebuilt in place is not submitted");
        Check(LastAuditContains("NetIdentityRejected: 1"), "scenario 6: the refusal is counted (" + LastAuditLine() + ")");
    }

    // 7. A resume whose verdict cache lost an entry must refuse rather than read the junction live:
    //    by then the earlier batch has landed and a live read would call its own work foreign.
    private static void ScenarioCacheMissRefusesInsteadOfWriting()
    {
        Fixture fx = Build();
        EntityManager em = fx.Em;
        DeletionUndoManager manager = Capture(em, fx.Plan);
        DeleteSegment(em, fx.EdgeAB);
        DeleteSegment(em, fx.EdgeBC);
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 7: the undo starts");

        EntityCommandBuffer first = StepOnce(em, manager, out _);
        ReplayBarrier(em, first);
        Check(manager.CancelActiveUndo(), "scenario 7: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 7: the restore resumes");
        Check(ReadIdentityGate(manager), "scenario 7: the resume raised the identity gate");

        // Force the invariant failure the code guards against: a cache with no entry for net 1.
        ForceVerdictCache(manager, 0);

        int linesBefore = MarqueeDeleteToolSystem.StatusLines.Count;
        int emitted = 0;
        bool completed = false;
        for (int i = 0; i < 12 && !completed; i++)
        {
            EntityCommandBuffer batch = StepOnce(em, manager, out completed);
            emitted += NetCarriers(batch);
        }

        Check(emitted == 0, "scenario 7: a missing verdict refuses instead of emitting");
        Check(completed, "scenario 7: the restore reaches completion without writing");
        Check(LiveEdgeCount(em) == 1, "scenario 7: only the first batch's segment exists");
        Check(MarqueeDeleteToolSystem.StatusLines.Count > linesBefore, "scenario 7: the restore reports an audit line");
        Check(LastAuditContains("NetVerdictMiss: 1"), "scenario 7: the miss is counted (" + LastAuditLine() + ")");
        Check(LastAuditContains("Skipped: 1"), "scenario 7: the skipped segment is reported (" + LastAuditLine() + ")");
    }

    // 8. Three segments meet at one junction that died with them, so every remaining segment looks
    //    for a junction at the same recorded position. The resume pre-pass must read the city's
    //    junctions once and answer the second ask from its cache; a per-endpoint query would scale
    //    with dead endpoints times city size. The restore itself must still complete.
    private static void ScenarioSharedDeadJunctionIsLookedUpOnce()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity a = MakeJunction(em, NodeAIndex, 0f);
        Entity b = MakeJunction(em, NodeBIndex, 100f);
        Entity d = MakeJunction(em, 13, -100f);
        Entity e = MakeJunction(em, 14, 300f);
        Entity ab = MakeEdge(em, EdgeABIndex, prefab, a, b, 0f, 100f);
        Entity ad = MakeEdge(em, 22, prefab, a, d, -100f, 0f);
        Entity ae = MakeEdge(em, 23, prefab, a, e, 0f, 300f);
        Link(em, a, ab);
        Link(em, b, ab);
        Link(em, a, ad);
        Link(em, d, ad);
        Link(em, a, ae);
        Link(em, e, ae);

        var roots = new HashSet<Entity> { ab, ad, ae };
        var plan = new DeletionPlan(
            roots,
            new HashSet<Entity>(roots),
            new HashSet<Entity> { a, b, d, e },
            new HashSet<Entity>(),
            new HashSet<Entity>());

        DeletionUndoManager manager = Capture(em, plan);
        DeleteSegment(em, ab);
        DeleteSegment(em, ad);
        DeleteSegment(em, ae);
        em.DestroyEntity(a); // all three lost the same junction

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 8: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool completedAfterFirst);
        Check(NetCarriers(first) == 1, "scenario 8: the first batch submits one segment");
        Check(!completedAfterFirst, "scenario 8: the first batch does not finish the restore");
        ReplayBarrier(em, first);

        Check(manager.CancelActiveUndo(), "scenario 8: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 8: the restore resumes");
        Check(manager.JunctionSnapshotBuilds == 0, "scenario 8: the resume has not read the city yet");

        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(manager.JunctionSnapshotBuilds == 1, "scenario 8: the resume reads the city's junctions exactly once");
        Check(manager.JunctionSnapshotNodes == 4, "scenario 8: the snapshot holds every live junction (" + manager.JunctionSnapshotNodes + ")");
        Check(manager.JunctionLookups == 2, "scenario 8: both remaining segments ask for the dead junction by position");
        Check(manager.JunctionScans == 1, "scenario 8: the second ask is answered from the position cache");
        Check(NetCarriers(second) >= 1, "scenario 8: the shared dead junction does not block the restore");
        List<Entity> secondRebuilt = ReplayBarrier(em, second);

        var rest = new List<Entity>();
        RunToCompletion(em, manager, rest);
        secondRebuilt.AddRange(rest);
        Check(secondRebuilt.Count == 2, "scenario 8: both remaining segments are submitted after the first landed");
        Check(LiveEdgeCount(em) == 3, "scenario 8: all three segments exist after the restore (" + LiveEdgeCount(em) + " of 3, " + rest.Count + " later; " + LastAuditLine() + ")");
        Check(LastAuditContains("Restored: 3"), "scenario 8: all three segments counted as restored (" + LastAuditLine() + ")");
        Check(LastAuditContains("NetIdentityRejected: 0"), "scenario 8: no false identity refusal (" + LastAuditLine() + ")");
    }

    // 9. The resume judges identity once, when the player resumes, and the batches after it only read
    //    those verdicts. A junction that is not part of the undo can still move while the remaining
    //    batches run, so the cached approval alone would submit a segment against a junction that is
    //    no longer where it was recorded. Three segments keep one batch left over after the batch that
    //    takes the cache.
    private static void ScenarioMovedJunctionBetweenBatchesRefuses()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeJunctionAt(em, 10, new float3(0f, 0f, 0f));
        Entity nodeB = MakeJunctionAt(em, 11, new float3(100f, 0f, 0f));
        Entity nodeC = MakeJunctionAt(em, 12, new float3(200f, 0f, 0f));
        Entity nodeD = MakeJunctionAt(em, 13, new float3(300f, 0f, 0f));
        Entity segAB = MakeEdgeWithLength(em, 20, prefab, nodeA, nodeB, 100f);
        Entity segBC = MakeEdgeWithLength(em, 21, prefab, nodeB, nodeC, 100f);
        Entity segCD = MakeEdgeWithLength(em, 22, prefab, nodeC, nodeD, 100f);
        Link(em, nodeA, segAB);
        Link(em, nodeB, segAB);
        Link(em, nodeB, segBC);
        Link(em, nodeC, segBC);
        Link(em, nodeC, segCD);
        Link(em, nodeD, segCD);

        var roots = new HashSet<Entity> { segAB, segBC, segCD };
        var plan = new DeletionPlan(
            roots,
            roots,
            new HashSet<Entity> { nodeA, nodeB, nodeC, nodeD },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager = Capture(em, plan);

        DeleteSegment(em, segAB);
        DeleteSegment(em, segBC);
        DeleteSegment(em, segCD);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 9: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool completedAfterFirst);
        Check(NetCarriers(first) == 1, "scenario 9: the first batch submits one segment");
        Check(!completedAfterFirst, "scenario 9: the first batch does not finish the restore");
        ReplayBarrier(em, first);

        Check(manager.CancelActiveUndo(), "scenario 9: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 9: the restore resumes");

        // This is the batch that takes the verdicts: both remaining segments are still approved here,
        // because every junction still stands where it was recorded.
        EntityCommandBuffer resumed = StepOnce(em, manager, out bool completedAfterResume);
        Check(NetCarriers(resumed) == 1, "scenario 9: the resumed batch submits the next segment (" + NetCarriers(resumed) + ")");
        Check(!completedAfterResume, "scenario 9: one segment is still left after the resumed batch");
        ReplayBarrier(em, resumed);

        // Only now does the player drag the junction the last segment would join, two metres along.
        Game.Net.Node moved = em.GetComponentData<Game.Net.Node>(nodeD);
        moved.m_Position = new float3(302f, 0f, 0f);
        em.Set(nodeD, moved);

        EntityCommandBuffer refused = StepOnce(em, manager, out _);
        Check(NetCarriers(refused) == 0, "scenario 9: nothing is submitted against the moved junction");
        Check(manager.NetPositionRejected >= 1, "scenario 9: the refusal is counted as a position refusal (" + manager.NetPositionRejected + ")");
        Check(LiveEdgeCount(em) == 2, "scenario 9: only the two already rebuilt segments exist (" + LiveEdgeCount(em) + ")");
        Check(LastAuditContains("NetPositionRejected: 1"), "scenario 9: the audit names the position refusal (" + LastAuditLine() + ")");
        Check(LastAuditContains("Restored: 2"), "scenario 9: the two earlier segments stay restored (" + LastAuditLine() + ")");
    }

    // 10. An emission whose recorded junctions are both gone names no junction, so its fingerprint is
    //     prefab, length and geometry. Without the geometry, a same-shape road a kilometre away excused
    //     whatever the player spliced here, and the resume emitted instead of refusing.
    private static void ScenarioFarSameShapeEdgeIsNotOwn()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity farStart = MakeJunctionAt(em, 30, new float3(0f, 0f, 0f));
        Entity farEnd = MakeJunctionAt(em, 31, new float3(100f, 0f, 0f));
        Entity far = MakeEdgeWithLength(em, 32, prefab, farStart, farEnd, 100f);
        Link(em, farStart, far);
        Link(em, farEnd, far);

        Entity nearStart = MakeJunctionAt(em, 40, new float3(1000f, 0f, 0f));
        Entity nearEnd = MakeJunctionAt(em, 41, new float3(1100f, 0f, 0f));
        Entity near = MakeEdgeWithLength(em, 42, prefab, nearStart, nearEnd, 100f);
        Link(em, nearStart, near);
        Link(em, nearEnd, near);

        var roots = new HashSet<Entity> { far, near };
        var plan = new DeletionPlan(
            roots,
            new HashSet<Entity>(roots),
            new HashSet<Entity> { farStart, farEnd, nearStart, nearEnd },
            new HashSet<Entity>(),
            new HashSet<Entity>());

        var manager = new DeletionUndoManager();
        manager.CaptureBeforeDelete(em, plan);

        DeleteSegment(em, far);
        DeleteSegment(em, near);
        em.DestroyEntity(farStart); // the far segment is submitted naming no junction at all
        em.DestroyEntity(farEnd);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 10: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool completedAfterFirst);
        Check(NetCarriers(first) >= 1, "scenario 10: the first batch submits the far segment");
        Check(!completedAfterFirst, "scenario 10: the first batch does not finish the restore");
        ReplayBarrier(em, first);

        Check(manager.CancelActiveUndo(), "scenario 10: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 10: the restore resumes");

        // A road of the same prefab and the same length is spliced onto the near junction, a kilometre
        // from where the undo's own segment sits.
        Entity spliceEnd = MakeJunctionAt(em, 50, new float3(1000f, 0f, 100f));
        Entity splice = MakeEdgeWithLength(em, 51, prefab, nearStart, spliceEnd, 100f);
        Link(em, nearStart, splice);
        Link(em, spliceEnd, splice);

        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 0, "scenario 10: the far record does not excuse a same-shape edge here");
        Check(manager.NetIdentityRejected >= 1, "scenario 10: the splice is refused as a changed junction (" + manager.NetIdentityRejected + ")");
    }

    // 11. The junction a resumed segment would rejoin is gone. At its ground position the player built
    //     a junction with a foreign road, and an empty junction stands eight metres up at the same XZ.
    //     Flat distance prefers the empty one because it is exactly overhead; distance must use height.
    private static void ScenarioElevatedJunctionDoesNotHideGround()
    {
        var fx = Build();
        var em = fx.Em;
        var manager = new DeletionUndoManager();
        manager.CaptureBeforeDelete(em, fx.Plan);

        DeleteSegment(em, fx.EdgeAB);
        DeleteSegment(em, fx.EdgeBC);
        em.DestroyEntity(fx.NodeA); // the first batch rebuilds the start junction afresh

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 11: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool completedAfterFirst);
        Check(NetCarriers(first) >= 1, "scenario 11: the first batch submits the segment whose start is gone");
        Check(!completedAfterFirst, "scenario 11: the first batch does not finish the restore");
        ReplayBarrier(em, first);

        Check(manager.CancelActiveUndo(), "scenario 11: the restore pauses");

        em.DestroyEntity(fx.NodeB);
        Entity ground = MakeJunctionAt(em, 60, new float3(100.3f, 0f, 0.3f));
        Entity spliceEnd = MakeJunctionAt(em, 61, new float3(100.3f, 0f, 60f));
        Entity splice = MakeEdgeWithLength(em, 62, fx.Prefab, ground, spliceEnd, 60f);
        Link(em, ground, splice);
        Link(em, spliceEnd, splice);
        MakeJunctionAt(em, 63, new float3(100f, 8f, 0f)); // empty, directly overhead

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 11: the restore resumes");
        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 0, "scenario 11: the overhead junction does not stand in for the ground one");
        Check(manager.NetIdentityRejected >= 1, "scenario 11: the ground splice is refused (" + manager.NetIdentityRejected + ")");
    }

    // 12. Both recorded junctions of a segment were destroyed with it, so its emission names neither
    //     endpoint. The game then builds replacement junctions at those positions and the rebuilt road
    //     hangs off them; the next segment must still be allowed to join. This is the positive twin of
    //     scenario 10: the geometry rule added there must not refuse what the undo itself built.
    private static void ScenarioBothEndsRebuiltStillCountAsOwn()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeJunctionAt(em, 10, new float3(0f, 0f, 0f));
        Entity nodeB = MakeJunctionAt(em, 11, new float3(100f, 0f, 0f));
        Entity nodeC = MakeJunctionAt(em, 12, new float3(200f, 0f, 0f));
        Entity segAB = MakeEdgeWithLength(em, 20, prefab, nodeA, nodeB, 100f);
        Entity segBC = MakeEdgeWithLength(em, 21, prefab, nodeB, nodeC, 100f);
        Link(em, nodeA, segAB);
        Link(em, nodeB, segAB);
        Link(em, nodeB, segBC);
        Link(em, nodeC, segBC);

        var roots = new HashSet<Entity> { segAB, segBC };
        var plan = new DeletionPlan(
            roots,
            roots,
            new HashSet<Entity> { nodeA, nodeB, nodeC },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager = Capture(em, plan);

        DeleteSegment(em, segAB);
        DeleteSegment(em, segBC);
        em.DestroyEntity(nodeA);
        em.DestroyEntity(nodeB);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 12: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool doneAfterFirst);
        Check(NetCarriers(first) == 1, "scenario 12: the segment whose junctions are both gone is placed by position");
        Check(!doneAfterFirst, "scenario 12: one segment is still left");
        List<Entity> rebuilt = ReplayBarrier(em, first);
        Check(rebuilt.Count == 1, "scenario 12: the game builds the replacement junctions for it");

        Check(manager.CancelActiveUndo(), "scenario 12: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 12: the restore resumes");
        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 1, "scenario 12: the remaining segment joins the rebuilt junction (" + NetCarriers(second) + ")");
        Check(LastAuditContains("NetIdentityRejected: 0"),
            "scenario 12: the undo's own rebuilt road is not mistaken for a splice (" + LastAuditLine() + ")");
    }

    // 13. Stacked junctions share an XZ and differ only in height. The lookup is three-dimensional, so
    //     the position key has to be too: a flat key would hand the answer found for the ground
    //     endpoint to the one eight metres above it without ever searching at that height.
    private static void ScenarioStackedEndpointsAreLookedUpSeparately()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        // A plain segment restored first, so the stacked one is still waiting when the player resumes:
        // identity is judged on the resume, and only then is a junction looked up.
        Entity decoyStart = MakeJunctionAt(em, 60, new float3(500f, 0f, 0f));
        Entity decoyEnd = MakeJunctionAt(em, 61, new float3(600f, 0f, 0f));
        Entity decoy = MakeEdgeWithLength(em, 20, prefab, decoyStart, decoyEnd, 100f);
        Link(em, decoyStart, decoy);
        Link(em, decoyEnd, decoy);

        Entity low = MakeJunctionAt(em, 70, new float3(100f, 0f, 0f));
        Entity high = MakeJunctionAt(em, 71, new float3(100f, 8f, 0f));
        Entity ramp = em.Make(72);
        em.AddComponentData(ramp, new Game.Prefabs.PrefabRef { m_Prefab = prefab });
        em.AddComponentData(ramp, new Game.Net.Edge { m_Start = low, m_End = high });
        em.AddComponentData(ramp, new Game.Net.Curve
        {
            m_Bezier = new Colossal.Mathematics.Bezier4x3
            {
                a = new float3(100f, 0f, 0f),
                b = new float3(100f, 2.6f, 0f),
                c = new float3(100f, 5.3f, 0f),
                d = new float3(100f, 8f, 0f),
            },
            m_Length = 8f,
        });
        var lanes = em.AddBuffer<Game.Net.SubLane>(ramp);
        for (int i = 0; i < 2; i++)
        {
            Entity lane = em.Make(1020 + i);
            em.AddComponentData(lane, new Game.Common.Owner { m_Owner = ramp });
            lanes.Add(new Game.Net.SubLane { m_SubLane = lane, m_PathMethods = Game.Net.PathMethod.None });
        }

        Link(em, low, ramp);
        Link(em, high, ramp);

        var roots = new HashSet<Entity> { decoy, ramp };
        var plan = new DeletionPlan(
            roots,
            roots,
            new HashSet<Entity> { decoyStart, decoyEnd, low, high },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager = Capture(em, plan);

        DeleteSegment(em, decoy);
        DeleteSegment(em, ramp);
        em.DestroyEntity(decoyStart);
        em.DestroyEntity(decoyEnd);
        em.DestroyEntity(low);
        em.DestroyEntity(high);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 13: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool doneAfterFirst);
        Check(NetCarriers(first) == 1, "scenario 13: the first batch restores one segment");
        Check(!doneAfterFirst, "scenario 13: the stacked segment is still waiting");
        List<Entity> rebuilt = ReplayBarrier(em, first);
        float firstLength = em.GetComponentData<Game.Net.Curve>(rebuilt[0]).m_Length;
        Check(math.abs(firstLength - 100f) <= 0.05f, "scenario 13: the plain segment is the one restored first (" + firstLength + ")");

        Check(manager.CancelActiveUndo(), "scenario 13: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 13: the restore resumes");

        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(manager.JunctionLookups == 2, "scenario 13: both stacked endpoints ask for a junction (" + manager.JunctionLookups + ")");
        Check(manager.JunctionScans == 2, "scenario 13: each height is searched for separately (" + manager.JunctionScans + ")");
        Check(NetCarriers(second) >= 1, "scenario 13: the segment is still placed by position");
    }

    // 14. The player removes the road the undo just rebuilt and splices one same-shape road onto each of
    //     the two junctions it left behind. One emitted record must not excuse both of them, or the two
    //     remaining segments would be submitted into foreign roads.
    private static void ScenarioRemovedRebuildDoesNotExcuseTwoSplices()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeJunctionAt(em, 10, new float3(0f, 0f, 0f));
        Entity nodeB = MakeJunctionAt(em, 11, new float3(100f, 0f, 0f));
        Entity nodeP = MakeJunctionAt(em, 12, new float3(0f, 0f, 100f));
        Entity nodeQ = MakeJunctionAt(em, 13, new float3(100f, 0f, 100f));
        Entity segAB = MakeEdgeWithLength(em, 20, prefab, nodeA, nodeB, 100f);
        Entity segPA = MakeEdgeWithLength(em, 21, prefab, nodeP, nodeA, 100f);
        Entity segQB = MakeEdgeWithLength(em, 22, prefab, nodeB, nodeQ, 100f);
        Link(em, nodeA, segAB);
        Link(em, nodeB, segAB);
        Link(em, nodeP, segPA);
        Link(em, nodeA, segPA);
        Link(em, nodeB, segQB);
        Link(em, nodeQ, segQB);

        var roots = new HashSet<Entity> { segAB, segPA, segQB };
        var plan = new DeletionPlan(
            roots,
            roots,
            new HashSet<Entity> { nodeA, nodeB, nodeP, nodeQ },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager = Capture(em, plan);

        DeleteSegment(em, segAB);
        DeleteSegment(em, segPA);
        DeleteSegment(em, segQB);
        em.DestroyEntity(nodeA);
        em.DestroyEntity(nodeB);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 14: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool doneAfterFirst);
        Check(NetCarriers(first) == 1, "scenario 14: the segment whose junctions are both gone is submitted");
        Check(!doneAfterFirst, "scenario 14: two segments are still left");
        List<Entity> rebuilt = ReplayBarrier(em, first);
        Check(rebuilt.Count == 1, "scenario 14: the game builds its replacement junctions");

        Check(manager.CancelActiveUndo(), "scenario 14: the restore pauses");

        Game.Net.Edge rebuiltEdge = em.GetComponentData<Game.Net.Edge>(rebuilt[0]);
        Entity rebuiltStart = rebuiltEdge.m_Start;
        Entity rebuiltEnd = rebuiltEdge.m_End;
        DeleteSegment(em, rebuilt[0]);

        Entity farA = MakeJunctionAt(em, 40, new float3(0f, 0f, 150f));
        Entity spliceA = MakeEdgeWithLength(em, 41, prefab, rebuiltStart, farA, 100f);
        Link(em, rebuiltStart, spliceA);
        Link(em, farA, spliceA);

        Entity farB = MakeJunctionAt(em, 42, new float3(100f, 0f, 150f));
        Entity spliceB = MakeEdgeWithLength(em, 43, prefab, rebuiltEnd, farB, 100f);
        Link(em, rebuiltEnd, spliceB);
        Link(em, farB, spliceB);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 14: the restore resumes");
        var later = new List<Entity>();
        RunToCompletion(em, manager, later);

        // Both of this record's junctions were gone, so it never submitted an endpoint and can only be
        // honoured through the geometry tier: that needs both ends of the road standing where the
        // recorded segment stood. A splice that leaves one end towards open ground is neither, so
        // nothing else is submitted and each splice stops its own segment once.
        Check(later.Count == 0, "scenario 14: neither splice is excused, so nothing else is submitted (" + LastAuditLine() + ")");
        Check(LastAuditContains("Restored: 1"),
            "scenario 14: the audit counts the segment restored before the pause, once (" + LastAuditLine() + ")");
        Check(LastAuditContains("Skipped: 2"), "scenario 14: both pending segments are refused (" + LastAuditLine() + ")");
        Check(manager.NetIdentityRejected >= 2, "scenario 14: each splice stops its own segment (" + manager.NetIdentityRejected + ")");
    }

    // 15. A neighbour the deletion never touched keeps its place at the junction. It is not this undo's
    //     rebuilt segment, so it must not consume that segment's single claim and leave the undo's own
    //     road looking like a foreign splice.
    private static void ScenarioPreExistingNeighbourKeepsItsOwnClaim()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeJunctionAt(em, 10, new float3(0f, 0f, 0f));
        Entity nodeB = MakeJunctionAt(em, 11, new float3(100f, 0f, 0f));
        Entity nodeC = MakeJunctionAt(em, 12, new float3(0f, 0f, 100f));
        Entity nodeD = MakeJunctionAt(em, 13, new float3(200f, 0f, 0f));
        Entity segAB = MakeEdgeWithLength(em, 20, prefab, nodeA, nodeB, 100f);
        Entity segAC = MakeEdgeWithLength(em, 25, prefab, nodeA, nodeC, 100f); // never deleted
        Entity segAD = MakeEdgeWithLength(em, 26, prefab, nodeA, nodeD, 200f);
        Link(em, nodeA, segAB);
        Link(em, nodeB, segAB);
        Link(em, nodeA, segAC);
        Link(em, nodeC, segAC);
        Link(em, nodeA, segAD);
        Link(em, nodeD, segAD);

        var roots = new HashSet<Entity> { segAB, segAD };
        var plan = new DeletionPlan(
            roots,
            roots,
            new HashSet<Entity> { nodeA, nodeB, nodeC, nodeD },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager = Capture(em, plan);

        DeleteSegment(em, segAB);
        DeleteSegment(em, segAD);
        em.DestroyEntity(nodeB); // the first segment keeps one live junction, so its rebuild names A

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 15: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool doneAfterFirst);
        Check(NetCarriers(first) == 1, "scenario 15: the first segment is submitted");
        Check(!doneAfterFirst, "scenario 15: one segment is still left");
        List<Entity> rebuilt = ReplayBarrier(em, first);
        Check(rebuilt.Count == 1, "scenario 15: the game gives the rebuilt segment a fresh edge");
        float rebuiltLength = em.GetComponentData<Game.Net.Curve>(rebuilt[0]).m_Length;
        Check(math.abs(rebuiltLength - 100f) <= 0.05f, "scenario 15: the segment restored first is the one sharing A (" + rebuiltLength + ")");

        Check(manager.CancelActiveUndo(), "scenario 15: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 15: the restore resumes");
        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 1, "scenario 15: the untouched neighbour does not take the rebuilt road's claim (" + NetCarriers(second) + ")");
        Check(LastAuditContains("NetIdentityRejected: 0"), "scenario 15: nothing is mistaken for a foreign splice (" + LastAuditLine() + ")");
    }

    /// <summary>The claim list the resume pass keeps for the emitted records.</summary>
    private static Entity[] ReadClaims(DeletionUndoManager manager)
    {
        object snapshot = ActiveSnapshot(manager);
        if (snapshot == null) return null;
        return (Entity[])snapshot.GetType().GetField("EmittedNetClaims").GetValue(snapshot);
    }

    /// <summary>The verdict the resume pass stored for one segment, as "start/end".</summary>
    private static string ReadVerdict(DeletionUndoManager manager, int netIndex)
    {
        object snapshot = ActiveSnapshot(manager);
        if (snapshot == null) return "no-snapshot";
        object cache = snapshot.GetType().GetField("Verdicts").GetValue(snapshot);
        if (cache == null) return "no-cache";

        MethodInfo tryGet = cache.GetType().GetMethod("TryGet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object[] arguments = { netIndex, null, null };
        bool found = (bool)tryGet.Invoke(cache, arguments);
        return found ? arguments[1] + "/" + arguments[2] : "miss";
    }

    // 16. Two records could both excuse the same foreign road: one names both of its junctions and
    //     matches it exactly, the other names only one junction. Only one record may take it. The
    //     exact pairing has to win, so the segment that is still pending passes and the foreign road
    //     is dropped from its junction. Awarding in record order instead would hand it to the
    //     one-ended record and stop the pending segment.
    private static void ScenarioStrictMatchOutranksLooseOne()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeJunctionAt(em, 10, new float3(0f, 0f, 0f));
        Entity nodeB = MakeJunctionAt(em, 11, new float3(100f, 0f, 0f));
        Entity nodeC = MakeJunctionAt(em, 12, new float3(0f, 0f, 100f));
        Entity nodeD = MakeJunctionAt(em, 13, new float3(0f, 0f, -100f));
        Entity nodeF = MakeJunctionAt(em, 15, new float3(0f, 0f, 200f));

        // The dead-end segment is captured first: after its far junction is gone it is the record with
        // one known endpoint, the shape that matches any equally long road touching that junction.
        Entity segAD = MakeEdgeWithLength(em, 20, prefab, nodeA, nodeD, 100f);
        Entity segAB = MakeEdgeWithLength(em, 21, prefab, nodeA, nodeB, 100f);
        Entity segAC = MakeEdgeWithLength(em, 22, prefab, nodeA, nodeC, 100f);
        Entity segAF = MakeEdgeWithLength(em, 23, prefab, nodeA, nodeF, 100f);
        Link(em, nodeA, segAD); Link(em, nodeD, segAD);
        Link(em, nodeA, segAB); Link(em, nodeB, segAB);
        Link(em, nodeA, segAC); Link(em, nodeC, segAC);
        Link(em, nodeA, segAF); Link(em, nodeF, segAF);

        // A fourth segment stays pending, so the batch that refuses the others ends saturated instead
        // of finishing the undo: the snapshot, and the claims awarded in it, stay readable.
        var roots = new HashSet<Entity> { segAD, segAB, segAC, segAF };
        var plan = new DeletionPlan(
            roots,
            roots,
            new HashSet<Entity> { nodeA, nodeB, nodeC, nodeD, nodeF },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager = Capture(em, plan);

        DeleteSegment(em, segAD);
        DeleteSegment(em, segAB);
        DeleteSegment(em, segAC);
        DeleteSegment(em, segAF);
        em.DestroyEntity(nodeD);

        // One segment per batch: the dead-end record is emitted first, then the exact-pair record. The
        // third segment is what is still pending when the pause comes.
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 16: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out _);
        Check(NetCarriers(first) == 1, "scenario 16: the dead-end segment is submitted first");
        List<Entity> rebuiltFirst = ReplayBarrier(em, first);
        Check(rebuiltFirst.Count == 1, "scenario 16: the game builds its replacement");
        EntityCommandBuffer later = StepOnce(em, manager, out _);
        Check(NetCarriers(later) == 1, "scenario 16: the second segment is submitted");
        List<Entity> rebuiltLater = ReplayBarrier(em, later);
        Check(rebuiltLater.Count == 1, "scenario 16: the game builds its replacement");

        Check(manager.CancelActiveUndo(), "scenario 16: the restore pauses");

        // Both rebuilt roads are removed, so only the pending segment stands at A. The road spliced in
        // from A to B is this undo's own shape, and either record could claim it.
        DeleteSegment(em, rebuiltFirst[0]);
        DeleteSegment(em, rebuiltLater[0]);
        Entity foreign = MakeEdgeWithLength(em, 30, prefab, nodeA, nodeB, 100f);
        Link(em, nodeA, foreign); Link(em, nodeB, foreign);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 16: the restore resumes");
        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 1,
            "scenario 16: the exact match takes the claim and the pending segment passes (" + LastAuditLine() + ")");
        Check(LastAuditContains("NetIdentityRejected: 0"),
            "scenario 16: no segment is stopped by the foreign road (" + LastAuditLine() + ")");

        // Which record was awarded the road is the point of the tier order, and the claim list is the
        // only place that records it: the one-ended record must come away empty.
        Entity[] claims = ReadClaims(manager);
        Check(claims != null && claims.Length == 2,
            "scenario 16: both emitted records have a claim slot (" + (claims == null ? "no snapshot" : claims.Length.ToString()) + ")");
        if (claims != null && claims.Length == 2)
        {
            Check(claims[0] == Entity.Null, "scenario 16: the one-ended record takes nothing (" + claims[0] + ")");
            Check(claims[1] == foreign, "scenario 16: the exact record takes the foreign road (" + claims[1] + ")");
        }
    }

    // 17. A claim that was already awarded must not come back after another pause. The undo's own
    //     rebuilt road is whatever the record claimed; while the restore is paused the player removes
    //     that road and puts an identical one there. The record already spent its single claim, so the
    //     identical replacement must stay foreign instead of being excused a second time.
    private static void ScenarioSpentClaimSurvivesAnotherResume()
    {
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeJunctionAt(em, 10, new float3(0f, 0f, 0f));
        Entity nodeB = MakeJunctionAt(em, 11, new float3(100f, 0f, 0f));
        Entity nodeC = MakeJunctionAt(em, 12, new float3(0f, 0f, 100f));
        Entity nodeE = MakeJunctionAt(em, 14, new float3(0f, 0f, -100f));
        Entity nodeG = MakeJunctionAt(em, 16, new float3(100f, 0f, 200f));

        Entity segAB = MakeEdgeWithLength(em, 20, prefab, nodeA, nodeB, 100f);
        Entity segAC = MakeEdgeWithLength(em, 21, prefab, nodeA, nodeC, 100f);
        Entity segAE = MakeEdgeWithLength(em, 22, prefab, nodeA, nodeE, 100f);
        Entity segAG = MakeEdgeWithLength(em, 23, prefab, nodeE, nodeG, 100f);
        Link(em, nodeA, segAB); Link(em, nodeB, segAB);
        Link(em, nodeA, segAC); Link(em, nodeC, segAC);
        Link(em, nodeA, segAE); Link(em, nodeE, segAE);
        Link(em, nodeE, segAG); Link(em, nodeG, segAG);

        // Two segments beyond the first stay pending, so the batch that refuses them ends saturated
        // and the snapshot, with the claims it awarded, stays readable.
        var roots = new HashSet<Entity> { segAB, segAC, segAE, segAG };
        var plan = new DeletionPlan(
            roots,
            roots,
            new HashSet<Entity> { nodeA, nodeB, nodeC, nodeE, nodeG },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager = Capture(em, plan);

        DeleteSegment(em, segAB);
        DeleteSegment(em, segAC);
        DeleteSegment(em, segAE);
        DeleteSegment(em, segAG);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 17: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out _);
        Check(NetCarriers(first) == 1, "scenario 17: the first segment is submitted");
        List<Entity> rebuilt = ReplayBarrier(em, first);
        Check(rebuilt.Count == 1, "scenario 17: the game builds its replacement");

        Check(manager.CancelActiveUndo(), "scenario 17: the restore pauses");

        // A road still standing at A blocks the next segment, so the restore cannot finish here.
        Entity blocker = MakeEdgeWithLength(em, 30, prefab, nodeA, nodeE, 100f);
        Link(em, nodeA, blocker); Link(em, nodeE, blocker);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 17: the restore resumes once");
        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 0, "scenario 17: the blocked segment is refused (" + LastAuditLine() + ")");

        Entity[] afterFirstResume = ReadClaims(manager);
        Check(afterFirstResume != null && afterFirstResume.Length == 1,
            "scenario 17: the emitted record has a claim slot (" + (afterFirstResume == null ? "no snapshot" : afterFirstResume.Length.ToString()) + ")");
        if (afterFirstResume != null && afterFirstResume.Length == 1)
            Check(afterFirstResume[0] == rebuilt[0],
                "scenario 17: the record claims the road this undo rebuilt (" + afterFirstResume[0] + ")");

        Check(manager.CancelActiveUndo(), "scenario 17: the restore pauses again");
        DeleteSegment(em, blocker);
        DeleteSegment(em, rebuilt[0]);

        // The player's replacement is the same shape as the road the claim was already spent on.
        Entity impostor = MakeEdgeWithLength(em, 31, prefab, nodeA, nodeB, 100f);
        Link(em, nodeA, impostor); Link(em, nodeB, impostor);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 17: the restore resumes twice");
        EntityCommandBuffer third = StepOnce(em, manager, out _);
        Check(NetCarriers(third) == 0,
            "scenario 17: a spent claim is not handed back after another pause (" + LastAuditLine() + ")");

        Entity[] afterSecondResume = ReadClaims(manager);
        Check(afterSecondResume != null && afterSecondResume.Length == 1,
            "scenario 17: the claim list survives another resume (" + (afterSecondResume == null ? "no snapshot" : afterSecondResume.Length.ToString()) + ")");
        if (afterSecondResume != null && afterSecondResume.Length == 1)
            Check(afterSecondResume[0] != impostor,
                "scenario 17: the replacement is not claimed in place of the spent one (" + afterSecondResume[0] + ")");

    }
    // 18. Two segments whose recorded junctions were both gone submitted no junction at all, so the
    //     next segment is recognised by where it stands. Both ends must stand where the recorded
    //     segment stood: a road that only starts there occupies the ground this segment needs.
    private static void ScenarioGeometryNeedsBothEnds()
    {
        // Positive: the rebuilt segment runs between the two junctions the game built at the recorded
        // positions, so both ends match and the second segment may be released.
        var em = new EntityManager();
        Entity prefab = em.Make(PrefabIndex);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeJunctionAt(em, 10, new float3(0f, 0f, 0f));
        Entity nodeB = MakeJunctionAt(em, 11, new float3(100f, 0f, 0f));
        Entity segAB = MakeEdgeWithLength(em, 20, prefab, nodeA, nodeB, 100f);
        Entity segAB2 = MakeEdgeWithLength(em, 21, prefab, nodeA, nodeB, 100f);
        Link(em, nodeA, segAB); Link(em, nodeB, segAB);
        Link(em, nodeA, segAB2); Link(em, nodeB, segAB2);

        var roots = new HashSet<Entity> { segAB, segAB2 };
        var plan = new DeletionPlan(
            roots,
            roots,
            new HashSet<Entity> { nodeA, nodeB },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager = Capture(em, plan);

        DeleteSegment(em, segAB);
        DeleteSegment(em, segAB2);
        em.DestroyEntity(nodeA);
        em.DestroyEntity(nodeB);

        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 18: the undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out _);
        Check(NetCarriers(first) == 1, "scenario 18: the first segment is submitted");
        Check(ReplayBarrier(em, first).Count == 1, "scenario 18: the game builds fresh junctions");
        Check(manager.CancelActiveUndo(), "scenario 18: the restore pauses");
        Check(manager.StartUndo(UndoAssetMask.Networks), "scenario 18: the restore resumes");
        EntityCommandBuffer second = StepOnce(em, manager, out _);
        Check(NetCarriers(second) == 1,
            "scenario 18: both ends stand where the recorded segment stood (" + LastAuditLine() + ")");
        Check(LastAuditContains("NetIdentityRejected: 0"), "scenario 18: the match is not counted as foreign (" + LastAuditLine() + ")");

        // Negative: same shape of record, but the only road between the rebuilt junctions leaves one of
        // them towards open ground. That is one end near, not both, so it stays foreign.
        var em2 = new EntityManager();
        Entity prefab2 = em2.Make(PrefabIndex);
        em2.AddComponentData(prefab2, new Game.Prefabs.NetData());
        Entity nodeP = MakeJunctionAt(em2, 10, new float3(0f, 0f, 0f));
        Entity nodeQ = MakeJunctionAt(em2, 11, new float3(100f, 0f, 0f));
        Entity segPQ = MakeEdgeWithLength(em2, 20, prefab2, nodeP, nodeQ, 100f);
        Entity segPQ2 = MakeEdgeWithLength(em2, 21, prefab2, nodeP, nodeQ, 100f);
        Link(em2, nodeP, segPQ); Link(em2, nodeQ, segPQ);
        Link(em2, nodeP, segPQ2); Link(em2, nodeQ, segPQ2);
        var roots2 = new HashSet<Entity> { segPQ, segPQ2 };
        var plan2 = new DeletionPlan(
            roots2,
            roots2,
            new HashSet<Entity> { nodeP, nodeQ },
            new HashSet<Entity>(),
            new HashSet<Entity>());
        var manager2 = Capture(em2, plan2);

        DeleteSegment(em2, segPQ);
        DeleteSegment(em2, segPQ2);
        em2.DestroyEntity(nodeP);
        em2.DestroyEntity(nodeQ);

        Check(manager2.StartUndo(UndoAssetMask.Networks), "scenario 18 negative: the undo starts");
        EntityCommandBuffer negFirst = StepOnce(em2, manager2, out _);
        Check(NetCarriers(negFirst) == 1, "scenario 18 negative: the first segment is submitted");
        List<Entity> negRebuilt = ReplayBarrier(em2, negFirst);
        Check(negRebuilt.Count == 1, "scenario 18 negative: the game rebuilds the segment");
        Game.Net.Edge rebuiltLinks = em2.GetComponentData<Game.Net.Edge>(negRebuilt[0]);

        Check(manager2.CancelActiveUndo(), "scenario 18 negative: the restore pauses");
        DeleteSegment(em2, negRebuilt[0]);
        Entity far = MakeJunctionAt(em2, 40, new float3(0f, 0f, 100f));
        Entity oneEnd = MakeEdgeWithLength(em2, 41, prefab2, rebuiltLinks.m_Start, far, 100f);
        Link(em2, rebuiltLinks.m_Start, oneEnd);
        Link(em2, far, oneEnd);

        Check(manager2.StartUndo(UndoAssetMask.Networks), "scenario 18 negative: the restore resumes");
        EntityCommandBuffer negSecond = StepOnce(em2, manager2, out _);
        Check(NetCarriers(negSecond) == 0,
            "scenario 18 negative: one end near is not both ends (" + LastAuditLine() + ")");
        Check(manager2.NetIdentityRejected >= 1, "scenario 18 negative: the refusal is counted (" + manager2.NetIdentityRejected + ")");
    }

    private static Entity MakeObjectPrefab(EntityManager em, int id)
    {
        Entity prefab = em.Make(id);
        em.AddComponentData(prefab, new Game.Prefabs.ObjectData
        {
            m_Archetype = new EntityArchetype { m_Archetype = new IntPtr(id) }
        });
        return prefab;
    }

    private static Entity MakeAreaPrefab(EntityManager em, int id)
    {
        Entity prefab = em.Make(id);
        em.AddComponentData(prefab, new Game.Prefabs.AreaData
        {
            m_Archetype = new EntityArchetype { m_Archetype = new IntPtr(id) }
        });
        return prefab;
    }

    private static Entity MakeNetPrefab(EntityManager em, int id)
    {
        Entity prefab = em.Make(id);
        em.AddComponentData(prefab, new Game.Prefabs.NetData());
        return prefab;
    }

    private static List<T> ReadEmittedComponents<T>(EntityCommandBuffer ecb) where T : struct
    {
        var list = new List<T>();
        foreach (var cmd in ecb.Commands)
        {
            if (cmd.Kind == EntityCommandBuffer.Kind.AddComponent && cmd.Type == typeof(T))
            {
                list.Add((T)cmd.Value);
            }
        }
        return list;
    }

    private static Dictionary<Entity, Dictionary<Type, object>> ReadCarrierComponents(EntityCommandBuffer ecb)
    {
        var map = new Dictionary<Entity, Dictionary<Type, object>>();
        foreach (var cmd in ecb.Commands)
        {
            if (cmd.Kind == EntityCommandBuffer.Kind.Create)
            {
                map[cmd.Entity] = new Dictionary<Type, object>();
            }
            else if (cmd.Kind == EntityCommandBuffer.Kind.AddComponent && map.TryGetValue(cmd.Entity, out var bag))
            {
                bag[cmd.Type] = cmd.Value;
            }
            else if (cmd.Kind == EntityCommandBuffer.Kind.AddBuffer && map.TryGetValue(cmd.Entity, out var bag2))
            {
                bag2[cmd.Type] = cmd.Value;
            }
        }
        return map;
    }

    private static Entity MakeOwnedBuilding(
        EntityManager em, int baseId, Entity bldPrefab, float3 pos,
        out Entity[] subNodes, out Entity[] subEdges)
    {
        Entity bld = em.Make(baseId);
        em.AddComponentData(bld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bld, new Game.Objects.Transform { m_Position = pos, m_Rotation = quaternion.identity });
        em.AddComponentData(bld, new Game.Buildings.Building());
        em.AddBuffer<Game.Net.SubNet>(bld);
        em.AddBuffer<Game.Areas.SubArea>(bld);
        em.AddBuffer<Game.Objects.SubObject>(bld);

        subNodes = new Entity[4];
        for (int i = 0; i < 4; i++)
        {
            Entity n = MakeJunctionAt(em, baseId + 10 + i, pos + new float3(i * 5f, 0f, (i % 2) * 5f));
            em.AddComponentData(n, new Game.Common.Owner { m_Owner = bld });
            em.AddComponentData(n, new Game.Tools.LocalTransformCache { m_ParentMesh = 2 });
            subNodes[i] = n;
        }
        subEdges = System.Array.Empty<Entity>();
        return bld;
    }

    // 19. A building with SubArea, 4 interconnected SubNet edges, 1 node-only portal, and a physical
    //     upgrade with its own SubArea + SubNet must restore atomically in a single frame via OwnerDefinition.
    private static void ScenarioAtomicBuildingUnitSingleFrame()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5100);
        Entity upgPrefab = MakeObjectPrefab(em, 5101);
        Entity areaPrefab = MakeAreaPrefab(em, 5102);
        Entity netPrefab = MakeNetPrefab(em, 5103);

        Entity bld = MakeOwnedBuilding(em, 6000, bldPrefab, new float3(20f, 0f, 30f), out Entity[] nodes, out _);

        Entity compEntity = em.Make(5999);
        em.AddComponentData(compEntity, new Game.Prefabs.NetCompositionData
        {
            m_Flags = new Game.Prefabs.CompositionFlags
            {
                m_General = Game.Prefabs.CompositionFlags.General.Elevated | Game.Prefabs.CompositionFlags.General.Invert
            }
        });

        // 4 interconnected SubNet edges + 1 node-only SubNet portal
        var subNetBuf = em.GetBuffer<Game.Net.SubNet>(bld);
        for (int i = 0; i < 4; i++)
        {
            Entity sNode = nodes[i];
            Entity eNode = nodes[(i + 1) % 4];
            float3 sp = em.GetComponentData<Game.Net.Node>(sNode).m_Position;
            float3 ep = em.GetComponentData<Game.Net.Node>(eNode).m_Position;
            Entity se = em.Make(6100 + i);
            em.AddComponentData(se, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
            em.AddComponentData(se, new Game.Net.Edge { m_Start = sNode, m_End = eNode });
            em.AddComponentData(se, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(sp, sp, ep, ep), m_Length = 7f });
            em.AddComponentData(se, new Game.Common.Owner { m_Owner = bld });
            em.AddComponentData(se, new Game.Net.Composition { m_Edge = compEntity });
            subNetBuf.Add(new Game.Net.SubNet { m_SubNet = se });
        }

        Entity portalNode = MakeJunctionAt(em, 6120, new float3(22f, 0f, 35f));
        em.AddComponentData(portalNode, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(portalNode, new Game.Common.Owner { m_Owner = bld });
        em.AddComponentData(portalNode, new Game.Tools.LocalTransformCache { m_ParentMesh = 1 });
        subNetBuf.Add(new Game.Net.SubNet { m_SubNet = portalNode });

        // Building SubArea
        Entity bldArea = em.Make(6200);
        em.AddComponentData(bldArea, new Game.Prefabs.PrefabRef { m_Prefab = areaPrefab });
        em.AddComponentData(bldArea, new Game.Areas.Area());
        em.Buffer(bldArea,
            new Game.Areas.Node { m_Position = new float3(10f, 0f, 20f) },
            new Game.Areas.Node { m_Position = new float3(30f, 0f, 20f) },
            new Game.Areas.Node { m_Position = new float3(20f, 0f, 40f) });
        em.Buffer(bld, new Game.Areas.SubArea { m_Area = bldArea });

        // Physical upgrade with its own SubArea and SubNet
        Entity upg = em.Make(6300);
        float3 upgPos = new float3(26f, 0f, 30f);
        em.AddComponentData(upg, new Game.Prefabs.PrefabRef { m_Prefab = upgPrefab });
        em.AddComponentData(upg, new Game.Objects.Transform { m_Position = upgPos, m_Rotation = quaternion.identity });
        em.AddComponentData(upg, new Game.Objects.Object());
        em.AddComponentData(upg, new Game.Buildings.ServiceUpgrade());
        em.AddComponentData(upg, new Game.Tools.LocalTransformCache { m_ParentMesh = 0 });
        em.Buffer(bld, new Game.Buildings.InstalledUpgrade { m_Upgrade = upg });

        Entity upgArea = em.Make(6310);
        em.AddComponentData(upgArea, new Game.Prefabs.PrefabRef { m_Prefab = areaPrefab });
        em.AddComponentData(upgArea, new Game.Areas.Area());
        em.Buffer(upgArea,
            new Game.Areas.Node { m_Position = new float3(24f, 0f, 28f) },
            new Game.Areas.Node { m_Position = new float3(28f, 0f, 28f) },
            new Game.Areas.Node { m_Position = new float3(26f, 0f, 32f) });
        em.Buffer(upg, new Game.Areas.SubArea { m_Area = upgArea });

        Entity upgNode1 = MakeJunctionAt(em, 6320, nodes[1] != Entity.Null ? em.GetComponentData<Game.Net.Node>(nodes[1]).m_Position : upgPos);
        Entity upgNode2 = MakeJunctionAt(em, 6321, new float3(28f, 0f, 30f));
        em.AddComponentData(upgNode1, new Game.Common.Owner { m_Owner = upg });
        em.AddComponentData(upgNode2, new Game.Common.Owner { m_Owner = upg });
        Entity upgEdge = em.Make(6322);
        em.AddComponentData(upgEdge, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(upgEdge, new Game.Net.Edge { m_Start = upgNode1, m_End = upgNode2 });
        em.AddComponentData(upgEdge, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(upgPos, upgPos, new float3(28f, 0f, 30f), new float3(28f, 0f, 30f)), m_Length = 2f });
        em.AddComponentData(upgEdge, new Game.Common.Owner { m_Owner = upg });
        em.Buffer(upg, new Game.Net.SubNet { m_SubNet = upgEdge });

        var roots = new HashSet<Entity> { bld };
        var plan = new DeletionPlan(roots, new HashSet<Entity> { bld, upg, bldArea, upgArea, upgEdge }, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);

        em.DestroyEntity(bld);
        em.DestroyEntity(upg);

        Check(manager.StartUndo(UndoAssetMask.Buildings), "scenario 19: undo starts");
        EntityCommandBuffer ecb = StepOnce(em, manager, out bool completed);
        Check(completed, "scenario 19: whole BuildingUnit restores in a single frame (" + LastAuditLine() + ")");

        var carriers = ReadCarrierComponents(ecb);
        Check(carriers.Count == 10, "scenario 19: all 10 entities emitted in single frame (got " + carriers.Count + ")");
        Check(ReadEmittedComponents<Game.Tools.NetCourse>(ecb).Count == 6, "scenario 19: 5 SubNet edges + 1 node-only portal emitted together");
        Check(ReadEmittedComponents<Game.Tools.OwnerDefinition>(ecb).Count == 9, "scenario 19: every child carrier carries OwnerDefinition");

        var courses = ReadEmittedComponents<Game.Tools.NetCourse>(ecb);
        bool foundPortal = false;
        int mesh2Count = 0;
        foreach (var c in courses)
        {
            if (c.m_Length == 0f &&
                (c.m_StartPosition.m_Flags & (Game.Tools.CoursePosFlags.IsFirst | Game.Tools.CoursePosFlags.IsLast)) == (Game.Tools.CoursePosFlags.IsFirst | Game.Tools.CoursePosFlags.IsLast))
            {
                foundPortal = true;
                Check(c.m_StartPosition.m_ParentMesh == 1, "scenario 19: node-only portal preserves m_ParentMesh == 1");
            }
            if (c.m_StartPosition.m_ParentMesh == 2) mesh2Count++;
        }
        Check(foundPortal, "scenario 19: zero-length node-only SubNet course emitted");
        Check(mesh2Count == 4, "scenario 19: all 4 SubNet edges preserve m_ParentMesh == 2 (" + mesh2Count + ")");

        var upgradedComps = ReadEmittedComponents<Game.Net.Upgraded>(ecb);
        Check(upgradedComps.Count >= 4 &&
              (upgradedComps[0].m_Flags.m_General & (Game.Prefabs.CompositionFlags.General.Elevated | Game.Prefabs.CompositionFlags.General.Invert)) ==
              (Game.Prefabs.CompositionFlags.General.Elevated | Game.Prefabs.CompositionFlags.General.Invert),
            "scenario 19: CompositionFlags.General.Elevated and Invert preserved on SubNets");
        Check(LastAuditContains("Restored: 10") && LastAuditContains("Skipped: 0"),
            "scenario 19: audit accounts cleanly (" + LastAuditLine() + ")");
    }

    // 20. Two adjacent buildings both having SubNets emit one building-with-SubNets per frame.
    private static void ScenarioCrossBuildingSubNetIsolation()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5200);
        Entity netPrefab = MakeNetPrefab(em, 5201);

        Entity b1 = MakeOwnedBuilding(em, 6400, bldPrefab, new float3(0f, 0f, 0f), out Entity[] n1, out _);
        Entity e1 = em.Make(6420);
        float3 b1p0 = em.GetComponentData<Game.Net.Node>(n1[0]).m_Position;
        float3 b1p1 = em.GetComponentData<Game.Net.Node>(n1[1]).m_Position;
        em.AddComponentData(e1, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(e1, new Game.Net.Edge { m_Start = n1[0], m_End = n1[1] });
        em.AddComponentData(e1, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(b1p0, b1p0, b1p1, b1p1), m_Length = 5f });
        em.AddComponentData(e1, new Game.Common.Owner { m_Owner = b1 });
        em.Buffer(b1, new Game.Net.SubNet { m_SubNet = e1 });

        Entity b2 = MakeOwnedBuilding(em, 6450, bldPrefab, new float3(10f, 0f, 0f), out Entity[] n2, out _);
        Entity e2 = em.Make(6470);
        float3 b2p0 = em.GetComponentData<Game.Net.Node>(n2[0]).m_Position;
        float3 b2p1 = em.GetComponentData<Game.Net.Node>(n2[1]).m_Position;
        em.AddComponentData(e2, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(e2, new Game.Net.Edge { m_Start = n2[0], m_End = n2[1] });
        em.AddComponentData(e2, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(b2p0, b2p0, b2p1, b2p1), m_Length = 5f });
        em.AddComponentData(e2, new Game.Common.Owner { m_Owner = b2 });
        em.Buffer(b2, new Game.Net.SubNet { m_SubNet = e2 });

        var roots = new HashSet<Entity> { b1, b2 };
        var plan = new DeletionPlan(roots, roots, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);
        em.DestroyEntity(b1);
        em.DestroyEntity(b2);

        Check(manager.StartUndo(UndoAssetMask.Buildings), "scenario 20: undo starts");
        EntityCommandBuffer first = StepOnce(em, manager, out bool done1);
        Check(!done1 && ReadEmittedComponents<Game.Tools.ObjectDefinition>(first).Count == 1 && NetCarriers(first) == 1,
            "scenario 20: frame 1 emits exactly building 1 + its SubNet");

        EntityCommandBuffer second = StepOnce(em, manager, out bool done2);
        Check(done2 && ReadEmittedComponents<Game.Tools.ObjectDefinition>(second).Count == 1 && NetCarriers(second) == 1,
            "scenario 20: frame 2 emits building 2 + its SubNet and completes");
    }

    // 21. SubNet connecting to an external street node omits DisableMerge on the external endpoint;
    //     and a building Attached to a deleted & rebuilt road resolves the new road parent.
    private static void ScenarioExternalJunctionAndRebuiltAttachedParent()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5300);
        Entity netPrefab = MakeNetPrefab(em, 5301);

        Entity roadStart = MakeJunctionAt(em, 6500, new float3(0f, 0f, 0f));
        Entity roadEnd = MakeJunctionAt(em, 6501, new float3(20f, 0f, 0f));
        Entity road = MakeEdge(em, 6502, netPrefab, roadStart, roadEnd, 0f, 20f);
        Link(em, roadStart, road); Link(em, roadEnd, road);

        Entity extStreetNode = MakeJunctionAt(em, 6505, new float3(5f, 0f, 10f));
        Entity bld = MakeOwnedBuilding(em, 6510, bldPrefab, new float3(10f, 0f, 0f), out Entity[] nodes, out _);
        em.AddComponentData(bld, new Game.Objects.Attached { m_Parent = road });

        Entity subEdge = em.Make(6530);
        float3 pStart = em.GetComponentData<Game.Net.Node>(nodes[0]).m_Position;
        float3 pEnd = em.GetComponentData<Game.Net.Node>(extStreetNode).m_Position;
        em.AddComponentData(subEdge, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(subEdge, new Game.Net.Edge { m_Start = nodes[0], m_End = extStreetNode });
        em.AddComponentData(subEdge, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(pStart, pStart, pEnd, pEnd), m_Length = 10f });
        em.AddComponentData(subEdge, new Game.Common.Owner { m_Owner = bld });
        em.Buffer(bld, new Game.Net.SubNet { m_SubNet = subEdge });

        var roots = new HashSet<Entity> { road, bld };
        var plan = new DeletionPlan(roots, new HashSet<Entity> { road, bld, subEdge }, new HashSet<Entity> { roadStart, roadEnd }, new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);

        DeleteSegment(em, road);
        em.DestroyEntity(bld);

        Check(manager.StartUndo(UndoAssetMask.All), "scenario 21: undo starts for road + attached building");
        EntityCommandBuffer netStep = StepOnce(em, manager, out bool doneNet);
        Check(!doneNet && NetCarriers(netStep) == 1, "scenario 21: road emits in phase 1");
        List<Entity> rebuiltRoads = ReplayBarrier(em, netStep);
        Check(rebuiltRoads.Count == 1, "scenario 21: rebuilt road created in EntityManager");

        EntityCommandBuffer bldStep = StepOnce(em, manager, out bool doneBld);
        Check(doneBld, "scenario 21: attached building + SubNet emit in phase 2");

        var defs = ReadEmittedComponents<Game.Tools.CreationDefinition>(bldStep);
        bool relinkedRoad = false;
        foreach (var d in defs)
        {
            if (d.m_Prefab == bldPrefab)
            {
                relinkedRoad = d.m_Attached == rebuiltRoads[0] && (d.m_Flags & Game.Tools.CreationFlags.Attach) != 0;
            }
        }
        Check(relinkedRoad, "scenario 21: building relinked m_Attached to rebuilt road entity");

        var courses = ReadEmittedComponents<Game.Tools.NetCourse>(bldStep);
        Check(courses.Count == 1 &&
              courses[0].m_StartPosition.m_Entity == Entity.Null &&
              (courses[0].m_StartPosition.m_Flags & Game.Tools.CoursePosFlags.DisableMerge) == 0 &&
              courses[0].m_EndPosition.m_Entity == extStreetNode &&
              (courses[0].m_EndPosition.m_Flags & Game.Tools.CoursePosFlags.DisableMerge) == 0,
            "scenario 21: owned SubNet omits DisableMerge while external live junction binds m_EndPosition.m_Entity");
    }

    // 22. An Edge with Attached pointing to a building in the plan that does NOT list it in SubNet is not dropped.
    private static void ScenarioAttachedEdgeOutsideSubNetNotDropped()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5400);
        Entity netPrefab = MakeNetPrefab(em, 5401);

        Entity bld = em.Make(6600);
        em.AddComponentData(bld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bld, new Game.Objects.Transform { m_Position = float3.zero, m_Rotation = quaternion.identity });
        em.AddComponentData(bld, new Game.Buildings.Building());

        Entity nA = MakeJunction(em, 6601, 0f);
        Entity nB = MakeJunction(em, 6602, 20f);
        Entity attachedEdge = MakeEdge(em, 6603, netPrefab, nA, nB, 0f, 20f);
        em.AddComponentData(attachedEdge, new Game.Objects.Attached { m_Parent = bld });

        var roots = new HashSet<Entity> { bld, attachedEdge };
        var plan = new DeletionPlan(roots, roots, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);

        em.DestroyEntity(bld);
        DeleteSegment(em, attachedEdge);

        Check(manager.StartUndo(UndoAssetMask.All), "scenario 22: attached edge outside SubNet was captured");
        var rebuilt = new List<Entity>();
        RunToCompletion(em, manager, rebuilt);
        Check(rebuilt.Count == 1, "scenario 22: standalone attached edge rebuilt (rebuilt.Count=" + rebuilt.Count + ")");
    }

    // 23. Pre-frozen batched capture preserves untouched state and excludes unsubmitted roots.
    private static void ScenarioPreFrozenBatchedCapture()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5500);
        Entity netPrefab = MakeNetPrefab(em, 5501);

        Entity b1 = MakeOwnedBuilding(em, 6700, bldPrefab, new float3(0f, 0f, 0f), out Entity[] n1, out _);
        Entity b2 = MakeOwnedBuilding(em, 6750, bldPrefab, new float3(40f, 0f, 0f), out Entity[] n2, out _);
        Entity b3Unsubmitted = MakeOwnedBuilding(em, 6800, bldPrefab, new float3(80f, 0f, 0f), out Entity[] _, out _);

        Entity fullSub = em.Make(6790);
        em.AddComponentData(fullSub, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(fullSub, new Game.Net.Edge { m_Start = n2[0], m_End = n2[1] });
        em.AddComponentData(fullSub, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(new float3(40f, 0f, 0f), new float3(40f, 0f, 0f), new float3(45f, 0f, 5f), new float3(45f, 0f, 5f)), m_Length = 7f });
        em.AddComponentData(fullSub, new Game.Common.Owner { m_Owner = b2 });
        em.Buffer(b2, new Game.Net.SubNet { m_SubNet = fullSub });

        var allRoots = new HashSet<Entity> { b1, b2, b3Unsubmitted };
        var fullPlan = new DeletionPlan(allRoots, allRoots, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());

        var manager = new DeletionUndoManager();
        manager.PrepareBatchedCapture(em, fullPlan);

        var batch1Plan = new DeletionPlan(new HashSet<Entity> { b1 }, new HashSet<Entity> { b1 }, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        manager.RecordSubmittedBatch(em, batch1Plan);
        em.DestroyEntity(b1);

        // Mutate b2's sub-node before batch 2 is submitted (simulates batch 1 mutation in city);
        // frozen snapshot must already hold b2's intact pre-batch data.
        em.DestroyEntity(n2[0]);
        var batch2Plan = new DeletionPlan(new HashSet<Entity> { b2 }, new HashSet<Entity> { b2 }, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        manager.RecordSubmittedBatch(em, batch2Plan);
        em.DestroyEntity(b2);

        // Note b3Unsubmitted is never passed to RecordSubmittedBatch (e.g., player canceled/stopped).
        manager.CommitDeletionCapture();

        Check(manager.StartUndo(UndoAssetMask.Buildings), "scenario 23: frozen batched capture commits submitted batches");
        EntityCommandBuffer step1 = StepOnce(em, manager, out bool done1);
        EntityCommandBuffer step2 = done1 ? step1 : StepOnce(em, manager, out bool done2);
        Check(manager.GetUiSnapshot(UndoAssetMask.Buildings).FinishedRestoredCount == 3,
            "scenario 23: restores exactly b1(1) + b2(1 + intact SubNet = 2) = 3, skipping unsubmitted b3");
    }

    // 24. Non-physical ServiceUpgrade without Game.Objects.Transform captures cleanly and restores.
    private static void ScenarioNonPhysicalServiceUpgrade()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5600);
        Entity upgPrefab = MakeObjectPrefab(em, 5601);

        Entity bld = em.Make(6900);
        em.AddComponentData(bld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bld, new Game.Objects.Transform { m_Position = new float3(5f, 0f, 5f), m_Rotation = quaternion.identity });
        em.AddComponentData(bld, new Game.Buildings.Building());

        Entity upg = em.Make(6901);
        em.AddComponentData(upg, new Game.Prefabs.PrefabRef { m_Prefab = upgPrefab });
        em.AddComponentData(upg, new Game.Buildings.ServiceUpgrade());
        em.Buffer(bld, new Game.Buildings.InstalledUpgrade { m_Upgrade = upg });

        var roots = new HashSet<Entity> { bld };
        var plan = new DeletionPlan(roots, new HashSet<Entity> { bld, upg }, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);

        var uiSnap = manager.GetUiSnapshot(UndoAssetMask.Buildings);
        Check(uiSnap.NotCapturedCount == 0 && uiSnap.NextCount == 2,
            "scenario 24: non-physical upgrade captured with NotCapturedCount==0 and NextCount==2");

        em.DestroyEntity(bld);
        em.DestroyEntity(upg);

        Check(manager.StartUndo(UndoAssetMask.Buildings), "scenario 24: undo starts");
        EntityCommandBuffer step1 = StepOnce(em, manager, out bool done1);
        Check(!done1 && ReadEmittedComponents<Game.Tools.ObjectDefinition>(step1).Count == 1,
            "scenario 24: building emits in step 1");

        // Replay building root creation into EntityManager so TryResolveOwner finds it
        Entity rebuiltBld = em.CreateEntity();
        em.AddComponentData(rebuiltBld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(rebuiltBld, new Game.Objects.Transform { m_Position = new float3(5f, 0f, 5f), m_Rotation = quaternion.identity });

        EntityCommandBuffer step2 = StepOnce(em, manager, out bool done2);
        Check(done2, "scenario 24: deferred upgrade emits after boundary and completes");
        var objDefs = ReadEmittedComponents<Game.Tools.ObjectDefinition>(step2);
        Check(objDefs.Count == 1 && objDefs[0].m_ParentMesh == 0 && math.all(objDefs[0].m_LocalPosition == float3.zero),
            "scenario 24: non-physical upgrade emits with ParentMesh==0 and zero LocalPosition");
    }

    // 25. Composite SubObject SubNets & SubAreas defer across a Boundary so FindOwnersSystem2 cannot
    //     strip their OwnerDefinition before SubObjectSystem spawns the SubObject; once the live
    //     SubObject exists, they emit directly with CreationDefinition.m_Owner = liveSubObj.
    private static void ScenarioCompositeSubObjectSubs()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5700);
        Entity subPrefab = MakeObjectPrefab(em, 5701);
        Entity areaPrefab = MakeAreaPrefab(em, 5702);
        Entity netPrefab = MakeNetPrefab(em, 5703);

        Entity bld = em.Make(7100);
        em.AddComponentData(bld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bld, new Game.Objects.Transform { m_Position = new float3(50f, 0f, 50f), m_Rotation = quaternion.identity });
        em.AddComponentData(bld, new Game.Buildings.Building());

        Entity subObj = em.Make(7101);
        float3 subPos = new float3(54f, 0f, 52f);
        em.AddComponentData(subObj, new Game.Prefabs.PrefabRef { m_Prefab = subPrefab });
        em.AddComponentData(subObj, new Game.Objects.Transform { m_Position = subPos, m_Rotation = quaternion.identity });
        em.AddComponentData(subObj, new Game.Common.Owner { m_Owner = bld });
        em.Buffer(bld, new Game.Objects.SubObject { m_SubObject = subObj });

        Entity subArea = em.Make(7110);
        em.AddComponentData(subArea, new Game.Prefabs.PrefabRef { m_Prefab = areaPrefab });
        em.AddComponentData(subArea, new Game.Areas.Area());
        em.Buffer(subArea,
            new Game.Areas.Node { m_Position = subPos },
            new Game.Areas.Node { m_Position = subPos + new float3(2f, 0f, 0f) },
            new Game.Areas.Node { m_Position = subPos + new float3(0f, 0f, 2f) });
        em.Buffer(subObj, new Game.Areas.SubArea { m_Area = subArea });

        Entity snA = MakeJunctionAt(em, 7120, subPos);
        Entity snB = MakeJunctionAt(em, 7121, subPos + new float3(3f, 0f, 0f));
        em.AddComponentData(snA, new Game.Common.Owner { m_Owner = subObj });
        em.AddComponentData(snB, new Game.Common.Owner { m_Owner = subObj });
        Entity subEdge = em.Make(7122);
        em.AddComponentData(subEdge, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(subEdge, new Game.Net.Edge { m_Start = snA, m_End = snB });
        em.AddComponentData(subEdge, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(subPos, subPos, subPos + new float3(3f, 0f, 0f), subPos + new float3(3f, 0f, 0f)), m_Length = 3f });
        em.AddComponentData(subEdge, new Game.Common.Owner { m_Owner = subObj });
        em.Buffer(subObj, new Game.Net.SubNet { m_SubNet = subEdge });

        var roots = new HashSet<Entity> { bld };
        var plan = new DeletionPlan(roots, new HashSet<Entity> { bld, subObj, subArea, subEdge }, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);

        em.DestroyEntity(bld);
        em.DestroyEntity(subObj);

        Check(manager.StartUndo(UndoAssetMask.Buildings), "scenario 25: undo starts");
        EntityCommandBuffer ecb1 = StepOnce(em, manager, out bool completed1);
        Check(!completed1, "scenario 25: pauses at Boundary after building root so SubObjectSystem can spawn SubObject");
        var objDefs = ReadEmittedComponents<Game.Tools.ObjectDefinition>(ecb1);
        Check(objDefs.Count == 1, "scenario 25: does not duplicate auto-spawned SubObject (only 1 ObjectDefinition for building root)");
        Check(ReadEmittedComponents<Game.Tools.OwnerDefinition>(ecb1).Count == 0,
            "scenario 25: does not emit SubObjectSubs before SubObject exists");

        // Simulate GenerateObjectsSystem + SubObjectSystem spawning the live building and SubObject.
        Entity liveBld = em.Make(7200);
        em.AddComponentData(liveBld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(liveBld, new Game.Objects.Transform { m_Position = new float3(50f, 0f, 50f), m_Rotation = quaternion.identity });
        em.AddComponentData(liveBld, new Game.Buildings.Building());
        Entity liveSubObj = em.Make(7201);
        em.AddComponentData(liveSubObj, new Game.Prefabs.PrefabRef { m_Prefab = subPrefab });
        em.AddComponentData(liveSubObj, new Game.Objects.Transform { m_Position = subPos, m_Rotation = quaternion.identity });
        em.AddComponentData(liveSubObj, new Game.Common.Owner { m_Owner = liveBld });

        var defs2 = new List<Game.Tools.CreationDefinition>();
        bool completed2 = false;
        for (int tries = 0; tries < 4 && !completed2; tries++)
        {
            EntityCommandBuffer step = StepOnce(em, manager, out completed2);
            defs2.AddRange(ReadEmittedComponents<Game.Tools.CreationDefinition>(step));
        }
        Check(completed2, "scenario 25: completes after live SubObject exists");
        Check(defs2.Count == 2 && defs2.All(d => d.m_Owner == liveSubObj),
            "scenario 25: SubObject SubArea and SubNet bind directly to liveSubObj via CreationDefinition.m_Owner");
        Check(LastAuditContains("Restored: 3") && LastAuditContains("Skipped: 0"),
            "scenario 25: audit accounts cleanly (" + LastAuditLine() + ")");
    }

    // 26. A ground-level building SubNet with small terrain NetElevation and no LocalTransformCache must
    //     keep m_ParentMesh == -1 and omit CoursePosFlags.FreeHeight so CS2 ground road matching succeeds,
    //     and completing the restore must queue post-restore connection refresh coordinates.
    private static void ScenarioGroundSubNetAndConnectionRefresh()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5600);
        Entity netPrefab = MakeNetPrefab(em, 5601);

        Entity bld = em.Make(7200);
        float3 bldPos = new float3(30f, 0f, 40f);
        em.AddComponentData(bld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bld, new Game.Objects.Transform { m_Position = bldPos, m_Rotation = quaternion.identity });
        em.AddComponentData(bld, new Game.Buildings.Building());

        Entity snA = MakeJunctionAt(em, 7210, bldPos);
        Entity snB = MakeJunctionAt(em, 7211, bldPos + new float3(4f, 0f, 0f));
        em.AddComponentData(snA, new Game.Common.Owner { m_Owner = bld });
        em.AddComponentData(snB, new Game.Common.Owner { m_Owner = bld });
        em.AddComponentData(snA, new Game.Net.Elevation { m_Elevation = new float2(0.01f, -0.02f) });
        em.AddComponentData(snB, new Game.Net.Elevation { m_Elevation = new float2(0.02f, 0f) });

        Entity subEdge = em.Make(7212);
        em.AddComponentData(subEdge, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(subEdge, new Game.Net.Edge { m_Start = snA, m_End = snB });
        em.AddComponentData(subEdge, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(bldPos, bldPos, bldPos + new float3(4f, 0f, 0f), bldPos + new float3(4f, 0f, 0f)), m_Length = 4f });
        em.AddComponentData(subEdge, new Game.Net.Elevation { m_Elevation = new float2(0.01f, 0.01f) });
        em.AddComponentData(subEdge, new Game.Common.Owner { m_Owner = bld });
        em.Buffer(bld, new Game.Net.SubNet { m_SubNet = subEdge });

        var roots = new HashSet<Entity> { bld };
        var plan = new DeletionPlan(roots, new HashSet<Entity> { bld, subEdge }, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);

        em.DestroyEntity(bld);
        Check(manager.StartUndo(UndoAssetMask.Buildings), "scenario 26: undo starts");
        EntityCommandBuffer ecb = StepOnce(em, manager, out bool completed);
        Check(completed, "scenario 26: completes cleanly");

        var courses = ReadEmittedComponents<Game.Tools.NetCourse>(ecb);
        Check(courses.Count == 1, "scenario 26: one SubNet course emitted");
        if (courses.Count == 1)
        {
            Check(courses[0].m_StartPosition.m_ParentMesh == -1 && courses[0].m_EndPosition.m_ParentMesh == -1,
                "scenario 26: ground SubNet keeps m_ParentMesh == -1 (" + courses[0].m_StartPosition.m_ParentMesh + "," + courses[0].m_EndPosition.m_ParentMesh + ")");
            Check((courses[0].m_StartPosition.m_Flags & Game.Tools.CoursePosFlags.FreeHeight) == 0 &&
                  (courses[0].m_EndPosition.m_Flags & Game.Tools.CoursePosFlags.FreeHeight) == 0,
                "scenario 26: sub-5cm terrain slope does not set CoursePosFlags.FreeHeight");
        }

        Check(manager.HasPendingConnectionRefresh, "scenario 26: connection refresh queued after building restore");
        var bldPositions = new List<float3>();
        var netPositions = new List<float3>();
        Check(manager.TryConsumeConnectionRefresh(bldPositions, netPositions) && bldPositions.Count == 1 && bldPositions[0].Equals(bldPos),
            "scenario 26: consumed refresh list contains restored building position");
        Check(!manager.HasPendingConnectionRefresh, "scenario 26: refresh queue clears after consumption");
    }

    // 27. Selections larger than MaxEntitiesPerSnapshot (8,000) must bound PrepareBatchedCapture work
    //     to MaxEntitiesPerSnapshot AND prioritize Networks & Buildings over Trees even when Trees are
    //     submitted in earlier batches.
    private static void ScenarioMegaBatchPrioritizesNetsAndBuildings()
    {
        var em = new EntityManager();
        Entity treePrefab = MakeObjectPrefab(em, 5800);
        Entity bldPrefab = MakeObjectPrefab(em, 5801);
        Entity netPrefab = MakeNetPrefab(em, 5802);

        const int treeCount = 8100;
        const int bldCount = 50;
        const int netCount = 100;

        var treeEntities = new List<Entity>(treeCount);
        var bldEntities = new List<Entity>(bldCount);
        var netEntities = new List<Entity>(netCount);
        var allRoots = new HashSet<Entity>(treeCount + bldCount + netCount);

        int id = 10000;
        for (int i = 0; i < treeCount; i++)
        {
            Entity t = em.Make(id++);
            em.AddComponentData(t, new Game.Prefabs.PrefabRef { m_Prefab = treePrefab });
            em.AddComponentData(t, new Game.Objects.Transform { m_Position = new float3(i * 2f, 0f, 0f), m_Rotation = quaternion.identity });
            em.AddComponentData(t, new Game.Objects.Object());
            em.AddComponentData(t, new Game.Objects.Tree());
            treeEntities.Add(t);
            allRoots.Add(t);
        }

        for (int i = 0; i < bldCount; i++)
        {
            Entity b = em.Make(id++);
            em.AddComponentData(b, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
            em.AddComponentData(b, new Game.Objects.Transform { m_Position = new float3(i * 10f, 0f, 50f), m_Rotation = quaternion.identity });
            em.AddComponentData(b, new Game.Buildings.Building());
            bldEntities.Add(b);
            allRoots.Add(b);
        }

        for (int i = 0; i < netCount; i++)
        {
            Entity nA = MakeJunctionAt(em, id++, new float3(i * 15f, 0f, 100f));
            Entity nB = MakeJunctionAt(em, id++, new float3(i * 15f + 10f, 0f, 100f));
            Entity e = MakeEdge(em, id++, netPrefab, nA, nB, i * 15f, i * 15f + 10f);
            netEntities.Add(e);
            allRoots.Add(e);
        }

        var fullPlan = new DeletionPlan(allRoots, new HashSet<Entity>(allRoots), new HashSet<Entity>(), new HashSet<Entity>(allRoots), new HashSet<Entity>(allRoots));
        var manager = new DeletionUndoManager();
        manager.PrepareBatchedCapture(em, fullPlan);

        Check(manager.FrozenCaptureWeightForTest == DeletionUndoManager.MaxEntitiesPerSnapshot &&
              manager.FrozenCaptureRootsForTest == DeletionUndoManager.MaxEntitiesPerSnapshot,
            "scenario 27: PrepareBatchedCapture caps frozen records at MaxEntitiesPerSnapshot (" + manager.FrozenCaptureRootsForTest + ")");

        // Simulate DeletionPhase submitting ALL trees and buildings BEFORE road edges!
        var treeSet = new HashSet<Entity>(treeEntities);
        manager.RecordSubmittedBatch(em, new DeletionPlan(treeSet, treeSet, new HashSet<Entity>(), treeSet, treeSet));
        for (int i = 0; i < treeEntities.Count; i++) em.DestroyEntity(treeEntities[i]);

        var bldSet = new HashSet<Entity>(bldEntities);
        manager.RecordSubmittedBatch(em, new DeletionPlan(bldSet, bldSet, new HashSet<Entity>(), bldSet, bldSet));
        for (int i = 0; i < bldEntities.Count; i++) em.DestroyEntity(bldEntities[i]);

        var netSet = new HashSet<Entity>(netEntities);
        manager.RecordSubmittedBatch(em, new DeletionPlan(netSet, netSet, new HashSet<Entity>(), netSet, netSet));
        for (int i = 0; i < netEntities.Count; i++) DeleteSegment(em, netEntities[i]);

        manager.CommitDeletionCapture();

        var uiSnap = manager.GetUiSnapshot(UndoAssetMask.All);
        Check(uiSnap.Notice == "UndoPartial", "scenario 27: snapshot marks truncated as UndoPartial (" + uiSnap.Notice + ")");
        Check(uiSnap.Counts.Networks == netCount && uiSnap.Counts.Buildings == bldCount,
            "scenario 27: all " + netCount + " networks and " + bldCount + " buildings survive snapshot cap (" + uiSnap.Counts.Networks + " nets, " + uiSnap.Counts.Buildings + " blds)");
        Check(uiSnap.NextCount == DeletionUndoManager.MaxEntitiesPerSnapshot,
            "scenario 27: total snapshot count respects MaxEntitiesPerSnapshot (" + uiSnap.NextCount + ")");
    }

    // 28. Uninterrupted undo accepts CS2 boundary node geometry drift (e.g. 1.2m) and snaps the rebuilt
    //     curve endpoint to the live node; cross-batch road restore binds dead shared junctions to the
    //     live junction created by the previous batch; and building driveway SubNets omit DisableMerge.
    private static void ScenarioBoundaryDriftAndCrossBatchJunctionBinding()
    {
        var em = new EntityManager();
        Entity netPrefab = MakeNetPrefab(em, 5900);
        Entity bldPrefab = MakeObjectPrefab(em, 5901);

        Entity nodeA = MakeJunctionAt(em, 9001, new float3(0f, 0f, 0f));
        Entity nodeB = MakeJunctionAt(em, 9002, new float3(50f, 0f, 0f));
        Entity nodeC = MakeJunctionAt(em, 9003, new float3(100f, 0f, 0f));
        Entity segAB = MakeEdge(em, 9010, netPrefab, nodeA, nodeB, 0f, 50f);
        Entity segBC = MakeEdge(em, 9011, netPrefab, nodeB, nodeC, 50f, 100f);
        Link(em, nodeA, segAB); Link(em, nodeB, segAB);
        Link(em, nodeB, segBC); Link(em, nodeC, segBC);

        // Building with an owned driveway SubNet (both ends owned by building -> Entity.Null).
        Entity bld = MakeOwnedBuilding(em, 9020, bldPrefab, new float3(50f, 0f, 12f), out Entity[] bldNodes, out _);
        Entity drv = em.Make(9040);
        float3 d0 = em.GetComponentData<Game.Net.Node>(bldNodes[0]).m_Position;
        float3 d1 = em.GetComponentData<Game.Net.Node>(bldNodes[1]).m_Position;
        em.AddComponentData(drv, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(drv, new Game.Net.Edge { m_Start = bldNodes[0], m_End = bldNodes[1] });
        em.AddComponentData(drv, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(d0, d0, d1, d1), m_Length = math.distance(d0, d1) });
        em.AddComponentData(drv, new Game.Common.Owner { m_Owner = bld });
        em.Buffer(bld, new Game.Net.SubNet { m_SubNet = drv });

        var roots = new HashSet<Entity> { segAB, segBC, bld };
        var plan = new DeletionPlan(roots, new HashSet<Entity> { segAB, segBC, bld, drv }, new HashSet<Entity> { nodeA, nodeB, nodeC }, new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);

        // Delete both segments + interior shared nodeB + building; survive boundary nodeA & nodeC.
        DeleteSegment(em, segAB);
        DeleteSegment(em, segBC);
        em.DestroyEntity(nodeB);
        em.DestroyEntity(bld);

        // Simulate CS2 geometry recalculation shifting surviving boundary nodeC by 1.2m on deletion.
        Game.Net.Node driftedC = em.GetComponentData<Game.Net.Node>(nodeC);
        driftedC.m_Position = new float3(101.2f, 0f, 0f);
        em.Set(nodeC, driftedC);

        Check(manager.StartUndo(UndoAssetMask.All), "scenario 28: undo starts");

        // Batch 1 restores segAB (creates a brand-new live junction at B = (50,0,0)).
        EntityCommandBuffer step1 = StepOnce(em, manager, out bool done1);
        Check(!done1 && NetCarriers(step1) == 1, "scenario 28: batch 1 restores segAB");
        List<Entity> rebuilt1 = ReplayBarrier(em, step1);
        Entity rebuiltNodeB = em.GetComponentData<Game.Net.Edge>(rebuilt1[0]).m_End;
        Check(rebuiltNodeB != Entity.Null && rebuiltNodeB != nodeB, "scenario 28: batch 1 created new live nodeB");

        // Batch 2 restores segBC: must NOT reject drifted nodeC (1.2m), must snap curve end to 101.2f,
        // and must bind m_StartPosition.m_Entity directly to rebuiltNodeB!
        EntityCommandBuffer step2 = StepOnce(em, manager, out bool done2);
        Check(!done2 && NetCarriers(step2) == 1, "scenario 28: batch 2 restores segBC despite 1.2m boundary drift");
        var bcCourses = ReadEmittedComponents<Game.Tools.NetCourse>(step2);
        Check(bcCourses.Count == 1 &&
              bcCourses[0].m_StartPosition.m_Entity == rebuiltNodeB &&
              bcCourses[0].m_EndPosition.m_Entity == nodeC &&
              math.abs(bcCourses[0].m_EndPosition.m_Position.x - 101.2f) < 0.01f &&
              math.abs(bcCourses[0].m_Curve.d.x - 101.2f) < 0.01f,
            "scenario 28: segBC binds rebuiltNodeB and snaps end curve to drifted nodeC");
        ReplayBarrier(em, step2);

        // Boundary + Building step (stepWait=5, so advance frameCount cleanly).
        EntityCommandBuffer step3 = default;
        bool done3 = false;
        for (int tries = 0; tries < 6 && !done3; tries++)
        {
            UnityEngine.Time.frameCount += 6;
            step3 = new EntityCommandBuffer();
            manager.Step(em, step3, out done3);
            if (ReadEmittedComponents<Game.Tools.ObjectDefinition>(step3).Count > 0) break;
        }
        var bldCourses = ReadEmittedComponents<Game.Tools.NetCourse>(step3);
        Check(done3 && bldCourses.Count == 1 &&
              (bldCourses[0].m_StartPosition.m_Flags & Game.Tools.CoursePosFlags.DisableMerge) == 0 &&
              (bldCourses[0].m_EndPosition.m_Flags & Game.Tools.CoursePosFlags.DisableMerge) == 0 &&
              (bldCourses[0].m_StartPosition.m_Flags & Game.Tools.CoursePosFlags.FreeHeight) == 0 &&
              (bldCourses[0].m_EndPosition.m_Flags & Game.Tools.CoursePosFlags.FreeHeight) == 0,
            "scenario 28: owned building driveway SubNet omits DisableMerge and omits FreeHeight matching ObjectToolBaseSystem");
        Check(manager.NetPositionRejected == 0, "scenario 28: zero NetPositionRejected (" + manager.NetPositionRejected + ")");
    }

    // 29. Prefab-driven building SubNets preserve exact m_ParentMesh (including inverted curves)
    //     without DisableMerge or FreeHeight.
    private static void ScenarioPrefabSubNetParentMeshRecovery()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 5950);
        Entity netPrefab = MakeNetPrefab(em, 5951);
        em.AddComponentData(netPrefab, new Game.Prefabs.NetGeometryData
        {
            m_Flags = Game.Net.GeometryFlags.FlipTrafficHandedness
        });

        // Prefab SubNet has m_ParentMesh = (0, -1) on local curve (0,2,0) -> (0,0,8) with m_InvertMode = Always.
        var localCurve = new Colossal.Mathematics.Bezier4x3(
            new float3(0f, 2f, 0f), new float3(0f, 2f, 2f), new float3(0f, 0f, 6f), new float3(0f, 0f, 8f));
        em.Buffer(bldPrefab, new Game.Prefabs.SubNet
        {
            m_Prefab = netPrefab,
            m_Curve = localCurve,
            m_NodeIndex = new int2(0, 1),
            m_ParentMesh = new int2(0, -1),
            m_InvertMode = Game.Prefabs.NetInvertMode.Always,
        });

        float3 bldPos = new float3(40f, 10f, 20f);
        Entity bld = em.Make(9100);
        em.AddComponentData(bld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bld, new Game.Objects.Transform { m_Position = bldPos, m_Rotation = quaternion.identity });
        em.AddComponentData(bld, new Game.Buildings.Building());

        // Live runtime SubNet is reversed and carries NO LocalTransformCache.
        float3 liveStart = bldPos + new float3(0f, 0f, 8f);
        float3 liveEnd = bldPos + new float3(0f, 2f, 0f);
        Entity nStart = MakeJunctionAt(em, 9101, liveStart);
        Entity nEnd = MakeJunctionAt(em, 9102, liveEnd);
        em.AddComponentData(nStart, new Game.Common.Owner { m_Owner = bld });
        em.AddComponentData(nEnd, new Game.Common.Owner { m_Owner = bld });
        em.AddComponentData(nEnd, new Game.Net.Elevation { m_Elevation = new float2(2f, 2f) });

        Entity subEdge = em.Make(9103);
        em.AddComponentData(subEdge, new Game.Prefabs.PrefabRef { m_Prefab = netPrefab });
        em.AddComponentData(subEdge, new Game.Net.Edge { m_Start = nStart, m_End = nEnd });
        em.AddComponentData(subEdge, new Game.Net.Curve { m_Bezier = new Colossal.Mathematics.Bezier4x3(liveStart, liveStart, liveEnd, liveEnd), m_Length = 8.25f });
        em.AddComponentData(subEdge, new Game.Common.Owner { m_Owner = bld });
        em.Buffer(bld, new Game.Net.SubNet { m_SubNet = subEdge });

        var roots = new HashSet<Entity> { bld };
        var plan = new DeletionPlan(roots, new HashSet<Entity> { bld, subEdge }, new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);

        em.DestroyEntity(bld);
        Check(manager.StartUndo(UndoAssetMask.Buildings), "scenario 29: undo starts");
        EntityCommandBuffer ecb = StepOnce(em, manager, out bool done);
        Check(done, "scenario 29: completes in 1 step");
        var courses = ReadEmittedComponents<Game.Tools.NetCourse>(ecb);
        Check(courses.Count == 1 &&
              courses[0].m_StartPosition.m_ParentMesh == -1 &&
              courses[0].m_EndPosition.m_ParentMesh == 0 &&
              (courses[0].m_EndPosition.m_Flags & Game.Tools.CoursePosFlags.FreeHeight) == 0 &&
              (courses[0].m_EndPosition.m_Flags & Game.Tools.CoursePosFlags.DisableMerge) == 0,
            "scenario 29: inverted prefab SubNet preserves swapped m_ParentMesh (-1, 0) without DisableMerge");
    }

    // 30. Park building prefab with multi-curve Game.Prefabs.SubNet sharing m_NodeIndex (with jitter),
    //     m_ParentMesh, m_Upgrades, m_InvertMode = LefthandTraffic, and SubArea restores atomically in
    //     one Step, with bitwise-identical shared endpoints, zero DisableMerge, exact CoursePos/NetCourse
    //     elevations, and LHT curve/NodeIndex/ParentMesh/Upgrade inversion.
    private static void ScenarioParkPrefabDrivenAtomicRebuildAndLht()
    {
        var em = new EntityManager();
        Entity parkPrefab = MakeObjectPrefab(em, 6000);
        Entity pathPrefab = MakeNetPrefab(em, 6001);
        Entity areaPrefab = MakeAreaPrefab(em, 6002);
        em.AddComponentData(pathPrefab, new Game.Prefabs.NetGeometryData
        {
            m_Flags = Game.Net.GeometryFlags.FlipTrafficHandedness | Game.Net.GeometryFlags.FlattenTerrain
        });

        var upgFlags = new Game.Prefabs.CompositionFlags(
            Game.Prefabs.CompositionFlags.General.Crosswalk,
            Game.Prefabs.CompositionFlags.Side.Gate,
            Game.Prefabs.CompositionFlags.Side.None);

        // Two curves sharing m_NodeIndex 1 with slight local jitter:
        // Curve 0: node 0 (0, 0, -10) -> node 1 (10.000, 1.5, 0.000)
        // Curve 1: node 1 (10.004, 1.5, -0.002) -> node 2 (20, 0, 10)  (inverted on LHT!)
        var c0Local = new Colossal.Mathematics.Bezier4x3(
            new float3(0f, 0f, -10f), new float3(3f, 0.5f, -7f), new float3(7f, 1.0f, -3f), new float3(10.000f, 1.5f, 0.000f));
        var c1Local = new Colossal.Mathematics.Bezier4x3(
            new float3(10.004f, 1.5f, -0.002f), new float3(13f, 1.0f, 3f), new float3(17f, 0.5f, 7f), new float3(20f, 0f, 10f));

        em.Buffer(parkPrefab,
            new Game.Prefabs.SubNet
            {
                m_Prefab = pathPrefab,
                m_Curve = c0Local,
                m_NodeIndex = new int2(0, 1),
                m_ParentMesh = new int2(-1, 0),
                m_InvertMode = Game.Prefabs.NetInvertMode.Never,
            },
            new Game.Prefabs.SubNet
            {
                m_Prefab = pathPrefab,
                m_Curve = c1Local,
                m_NodeIndex = new int2(1, 2),
                m_ParentMesh = new int2(0, 2),
                m_InvertMode = Game.Prefabs.NetInvertMode.LefthandTraffic,
                m_Upgrades = upgFlags,
            });

        float3 parkPos = new float3(100f, 12f, 200f);
        quaternion parkRot = quaternion.RotateY(0.5f);

        // Build RHT live park (including split live edges that must NOT be replayed when PrefabSubNetCount > 0!)
        Entity parkRht = em.Make(9200);
        em.AddComponentData(parkRht, new Game.Prefabs.PrefabRef { m_Prefab = parkPrefab });
        em.AddComponentData(parkRht, new Game.Objects.Transform { m_Position = parkPos, m_Rotation = parkRot });
        em.AddComponentData(parkRht, new Game.Buildings.Building());

        Entity parkArea = em.Make(9201);
        em.AddComponentData(parkArea, new Game.Prefabs.PrefabRef { m_Prefab = areaPrefab });
        em.AddComponentData(parkArea, new Game.Areas.Area());
        em.Buffer(parkArea,
            new Game.Areas.Node { m_Position = parkPos + new float3(-10f, 0f, -10f) },
            new Game.Areas.Node { m_Position = parkPos + new float3(10f, 0f, -10f) },
            new Game.Areas.Node { m_Position = parkPos + new float3(0f, 0f, 10f) });
        em.Buffer(parkRht, new Game.Areas.SubArea { m_Area = parkArea });

        // Deliberately add 5 live split edges on parkRht to prove PrefabSubNetCount=2 ignores live split edges!
        var liveSubNetBuf = em.AddBuffer<Game.Net.SubNet>(parkRht);
        for (int s = 0; s < 5; s++)
        {
            Entity sa = MakeJunctionAt(em, 9210 + s * 2, parkPos + new float3(s, 0f, 0f));
            Entity sb = MakeJunctionAt(em, 9211 + s * 2, parkPos + new float3(s + 1f, 0f, 0f));
            Entity se = em.Make(9230 + s);
            em.AddComponentData(se, new Game.Prefabs.PrefabRef { m_Prefab = pathPrefab });
            em.AddComponentData(se, new Game.Net.Edge { m_Start = sa, m_End = sb });
            em.AddComponentData(se, new Game.Net.Curve { m_Bezier = Straight(s, s + 1f), m_Length = 1f });
            em.AddComponentData(se, new Game.Common.Owner { m_Owner = parkRht });
            liveSubNetBuf.Add(new Game.Net.SubNet { m_SubNet = se });
        }

        // --- Part A: RHT restore ---
        var planRht = new DeletionPlan(
            new HashSet<Entity> { parkRht },
            new HashSet<Entity> { parkRht, parkArea },
            new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager mgrRht = Capture(em, planRht);
        var snapUiRht = mgrRht.GetUiSnapshot(UndoAssetMask.All);
        Check(snapUiRht.NextCount == 4, "scenario 30 (RHT): TotalCount = 1 building + 1 SubArea + 2 prefab SubNets = 4 (got " + snapUiRht.NextCount + ")");

        em.DestroyEntity(parkRht);
        Check(mgrRht.StartUndo(UndoAssetMask.All), "scenario 30 (RHT): undo starts");
        var terrainSys = new Game.Simulation.TerrainSystem
        {
            Data = new Game.Simulation.TerrainHeightData { isCreated = true, HeightSampler = p => 12f }
        };
        var waterSys = new Game.Simulation.WaterSystem();
        UnityEngine.Time.frameCount += 5;
        var ecbRht = new EntityCommandBuffer();
        mgrRht.Step(em, ecbRht, terrainSys, waterSys, isLeftHandTraffic: false, out bool completedRht);

        Check(completedRht, "scenario 30 (RHT): completed == true in 1 single Step");
        Check(terrainSys.GetHeightDataCalls == 1 && terrainSys.ReleaseCalls == 1,
            "scenario 30 (RHT): TerrainHeightData lazily acquired once and released in finally");
        Check(waterSys.GetSurfaceDataCalls == 0 && waterSys.ReleaseCalls == 0,
            "scenario 30 (RHT): WaterSurfaceData not acquired when no OnWater SubNet exists");
        var finishedRht = mgrRht.GetUiSnapshot(UndoAssetMask.All);
        Check(finishedRht.FinishedRestoredCount == 4 && finishedRht.FinishedSkippedCount == 0 &&
              finishedRht.FinishedRestoredCount + finishedRht.FinishedSkippedCount == finishedRht.FinishedTotalCount,
            "scenario 30 (RHT): RestoredCount == TotalCount == 4, SkippedCount == 0");

        var rhtCourses = ReadEmittedComponents<Game.Tools.NetCourse>(ecbRht);
        Check(rhtCourses.Count == 2, "scenario 30 (RHT): emits 2 prefab SubNets instead of 5 live split edges");
        if (rhtCourses.Count == 2)
        {
            // Shared m_NodeIndex 1: curve 0 end == curve 1 start bitwise-identically!
            Check(rhtCourses[0].m_EndPosition.m_Position.Equals(rhtCourses[1].m_StartPosition.m_Position),
                "scenario 30 (RHT): shared m_NodeIndex 1 endpoints are bitwise-identical despite 4mm local jitter");
            foreach (var c in rhtCourses)
            {
                Check((c.m_StartPosition.m_Flags & Game.Tools.CoursePosFlags.DisableMerge) == 0 &&
                      (c.m_EndPosition.m_Flags & Game.Tools.CoursePosFlags.DisableMerge) == 0,
                    "scenario 30 (RHT): DisableMerge == 0 on both endpoints");
                Check(c.m_Elevation.Equals(float2.zero), "scenario 30 (RHT): NetCourse.m_Elevation == float2.zero");
            }
            Check(rhtCourses[0].m_StartPosition.m_Elevation.Equals(new float2(c0Local.a.y)) &&
                  rhtCourses[0].m_EndPosition.m_Elevation.Equals(new float2(c0Local.d.y)) &&
                  rhtCourses[1].m_StartPosition.m_Elevation.Equals(new float2(c1Local.a.y)) &&
                  rhtCourses[1].m_EndPosition.m_Elevation.Equals(new float2(c1Local.d.y)),
                "scenario 30 (RHT): CoursePos m_Elevation matches localCurve.a.y / d.y");
            Check(rhtCourses[1].m_StartPosition.m_ParentMesh == 0 && rhtCourses[1].m_EndPosition.m_ParentMesh == 2,
                "scenario 30 (RHT): non-inverted curve 1 preserves m_ParentMesh (0, 2)");
        }

        // --- Part B: LHT restore (isLeftHandTraffic = true) ---
        Entity parkLht = em.Make(9300);
        em.AddComponentData(parkLht, new Game.Prefabs.PrefabRef { m_Prefab = parkPrefab });
        em.AddComponentData(parkLht, new Game.Objects.Transform { m_Position = parkPos, m_Rotation = parkRot });
        em.AddComponentData(parkLht, new Game.Buildings.Building());
        em.Buffer(parkLht, new Game.Areas.SubArea { m_Area = parkArea });

        var planLht = new DeletionPlan(
            new HashSet<Entity> { parkLht },
            new HashSet<Entity> { parkLht, parkArea },
            new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager mgrLht = Capture(em, planLht);
        em.DestroyEntity(parkLht);
        Check(mgrLht.StartUndo(UndoAssetMask.All), "scenario 30 (LHT): undo starts");
        UnityEngine.Time.frameCount += 5;
        var ecbLht = new EntityCommandBuffer();
        mgrLht.Step(em, ecbLht, terrainSys, waterSys, isLeftHandTraffic: true, out bool completedLht);
        Check(completedLht, "scenario 30 (LHT): completed == true in 1 single Step");

        var lhtCourses = ReadEmittedComponents<Game.Tools.NetCourse>(ecbLht);
        var lhtUpgraded = ReadEmittedComponents<Game.Net.Upgraded>(ecbLht);
        Check(lhtCourses.Count == 2, "scenario 30 (LHT): emits 2 SubNets");
        if (lhtCourses.Count == 2)
        {
            // Curve 1 had m_InvertMode = LefthandTraffic + FlipTrafficHandedness:
            // - curve inverted (d -> a), so startPos == node 2, endPos == averaged node 1!
            // - m_NodeIndex swapped (1,2) -> (2,1), so curve 0 end == curve 1 end bitwise-identical!
            // - m_ParentMesh swapped (0,2) -> (2,0)
            // - m_Elevation matches inverted localCurve.a.y (0f) and inverted localCurve.d.y (1.5f)
            // - Upgraded Side.Gate swapped Left <-> Right
            Check(lhtCourses[0].m_EndPosition.m_Position.Equals(lhtCourses[1].m_EndPosition.m_Position),
                "scenario 30 (LHT): inverted curve 1 swaps m_NodeIndex to (2,1), matching curve 0 end (node 1) bitwise");
            Check(lhtCourses[1].m_StartPosition.m_ParentMesh == 2 && lhtCourses[1].m_EndPosition.m_ParentMesh == 0,
                "scenario 30 (LHT): inverted curve 1 swaps m_ParentMesh to (2, 0)");
            Check(lhtCourses[1].m_StartPosition.m_Elevation.Equals(new float2(c1Local.d.y)) &&
                  lhtCourses[1].m_EndPosition.m_Elevation.Equals(new float2(c1Local.a.y)),
                "scenario 30 (LHT): inverted curve 1 swaps start/end CoursePos elevations");
            float3 expectedLhtStartWorld = Game.Objects.ObjectUtils.LocalToWorld(parkPos, parkRot, c1Local.d);
            Check(math.distancesq(lhtCourses[1].m_Curve.a, expectedLhtStartWorld) < 1e-4f,
                "scenario 30 (LHT): inverted curve 1 starts at former d endpoint");
            Check(lhtUpgraded.Count == 1 &&
                  lhtUpgraded[0].m_Flags.m_Left == Game.Prefabs.CompositionFlags.Side.None &&
                  lhtUpgraded[0].m_Flags.m_Right == Game.Prefabs.CompositionFlags.Side.Gate,
                "scenario 30 (LHT): FixInvertedUpgradeTrafficHandedness swapped Gate bit Left -> Right");
        }
    }

    // 31. Composite SubObject with prefab SubNets (including OnWater) restores atomically in one
    //     SubObjectUnit frame using CreationDefinition.m_Owner = liveSubObj and acquires WaterSystem lazily.
    private static void ScenarioSubObjectUnitPrefabSubNetsAndWater()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 6100);
        Entity subPrefab = MakeObjectPrefab(em, 6101);
        Entity waterPathPrefab = MakeNetPrefab(em, 6102);
        em.AddComponentData(waterPathPrefab, new Game.Prefabs.NetGeometryData
        {
            m_Flags = Game.Net.GeometryFlags.OnWater
        });

        em.Buffer(subPrefab,
            new Game.Prefabs.SubNet
            {
                m_Prefab = waterPathPrefab,
                m_Curve = new Colossal.Mathematics.Bezier4x3(
                    new float3(0f, 5f, 0f), new float3(1f, 5f, 0f), new float3(2f, 5f, 0f), new float3(3f, 5f, 0f)),
                m_NodeIndex = new int2(0, 1),
                m_ParentMesh = new int2(-1, -1),
            },
            new Game.Prefabs.SubNet
            {
                m_Prefab = waterPathPrefab,
                m_Curve = new Colossal.Mathematics.Bezier4x3(
                    new float3(3f, 5f, 0f), new float3(4f, 5f, 0f), new float3(5f, 5f, 0f), new float3(6f, 5f, 0f)),
                m_NodeIndex = new int2(1, 2),
                m_ParentMesh = new int2(-1, -1),
            });

        float3 bldPos = new float3(10f, 0f, 10f);
        float3 subPos = new float3(14f, 0f, 12f);
        Entity bld = em.Make(9400);
        em.AddComponentData(bld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bld, new Game.Objects.Transform { m_Position = bldPos, m_Rotation = quaternion.identity });
        em.AddComponentData(bld, new Game.Buildings.Building());

        Entity subObj = em.Make(9401);
        em.AddComponentData(subObj, new Game.Prefabs.PrefabRef { m_Prefab = subPrefab });
        em.AddComponentData(subObj, new Game.Objects.Transform { m_Position = subPos, m_Rotation = quaternion.identity });
        em.AddComponentData(subObj, new Game.Common.Owner { m_Owner = bld });
        em.Buffer(bld, new Game.Objects.SubObject { m_SubObject = subObj });
        em.AddBuffer<Game.Net.SubNet>(subObj);

        var plan = new DeletionPlan(
            new HashSet<Entity> { bld },
            new HashSet<Entity> { bld, subObj },
            new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager manager = Capture(em, plan);
        Check(manager.GetUiSnapshot(UndoAssetMask.All).NextCount == 3,
            "scenario 31: TotalCount = 1 building + 2 SubObject prefab SubNets = 3");

        em.DestroyEntity(bld);
        em.DestroyEntity(subObj);

        Check(manager.StartUndo(UndoAssetMask.Buildings), "scenario 31: undo starts");
        var terrainSys = new Game.Simulation.TerrainSystem
        {
            Data = new Game.Simulation.TerrainHeightData { isCreated = true, HeightSampler = p => 2f }
        };
        var waterSys = new Game.Simulation.WaterSystem
        {
            Data = new Game.Simulation.WaterSurfaceData<Game.Simulation.SurfaceWater> { isCreated = true, WaterHeightSampler = p => 7.5f }
        };

        UnityEngine.Time.frameCount += 5;
        var ecb1 = new EntityCommandBuffer();
        manager.Step(em, ecb1, terrainSys, waterSys, false, out bool done1);
        Check(!done1, "scenario 31: pauses at Boundary before SubObjectUnit");

        Entity liveBld = em.Make(9500);
        em.AddComponentData(liveBld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(liveBld, new Game.Objects.Transform { m_Position = bldPos, m_Rotation = quaternion.identity });
        Entity liveSubObj = em.Make(9501);
        em.AddComponentData(liveSubObj, new Game.Prefabs.PrefabRef { m_Prefab = subPrefab });
        em.AddComponentData(liveSubObj, new Game.Objects.Transform { m_Position = subPos, m_Rotation = quaternion.identity });
        em.AddComponentData(liveSubObj, new Game.Common.Owner { m_Owner = liveBld });

        UnityEngine.Time.frameCount += 5;
        var ecb2 = new EntityCommandBuffer();
        manager.Step(em, ecb2, terrainSys, waterSys, false, out bool done2);
        Check(done2, "scenario 31: atomic SubObjectUnit restores both OnWater SubNets in 1 Step");
        Check(terrainSys.GetHeightDataCalls == 1 && terrainSys.ReleaseCalls == 1 &&
              waterSys.GetSurfaceDataCalls == 1 && waterSys.ReleaseCalls == 1,
            "scenario 31: TerrainSystem and WaterSystem lazily acquired once and released");

        var defs = ReadEmittedComponents<Game.Tools.CreationDefinition>(ecb2);
        var ownerDefs = ReadEmittedComponents<Game.Tools.OwnerDefinition>(ecb2);
        var courses = ReadEmittedComponents<Game.Tools.NetCourse>(ecb2);
        Check(defs.Count == 2 && defs.All(d => d.m_Owner == liveSubObj) && ownerDefs.Count == 0,
            "scenario 31: live-owner mode binds m_Owner = liveSubObj without OwnerDefinition");
        Check(courses.Count == 2 &&
              math.abs(courses[0].m_StartPosition.m_Position.y - 7.5f) < 1e-4f &&
              courses[0].m_StartPosition.m_Elevation.Equals(float2.zero),
            "scenario 31: OnWater SubNet samples water height 7.5f and zeroes local y elevation");
        Check(LastAuditContains("Restored: 3") && LastAuditContains("Skipped: 0"),
            "scenario 31: audit accounts cleanly (" + LastAuditLine() + ")");
    }

    // 32. MarqueeDeleteUndoSystem cleans up orphaned permanent CourseSplitSystem carriers
    //     while undo is active or within the 4-frame post-undo window, ignores Updated/Temp/Deleted/non-Permanent,
    //     and incurs zero work once the post-undo window expires.
    private static void ScenarioMarqueeDeleteUndoSystemCarrierCleanup()
    {
        var undoSys = new MarqueeDeleteUndoSystem();
        var em = undoSys.EntityManager;
        undoSys.CreateForTest();

        var toolSys = undoSys.World.GetOrCreateSystemManaged<MarqueeDeleteToolSystem>();
        var barrier = undoSys.World.GetOrCreateSystemManaged<Game.Tools.ToolOutputBarrier>();

        Entity orphanPerm = em.Make(9600);
        em.AddComponentData(orphanPerm, new Game.Tools.CreationDefinition { m_Flags = Game.Tools.CreationFlags.Permanent });

        Entity activeUpdated = em.Make(9601);
        em.AddComponentData(activeUpdated, new Game.Tools.CreationDefinition { m_Flags = Game.Tools.CreationFlags.Permanent });
        em.AddComponentData(activeUpdated, new Game.Common.Updated());

        Entity tempCarrier = em.Make(9602);
        em.AddComponentData(tempCarrier, new Game.Tools.CreationDefinition { m_Flags = Game.Tools.CreationFlags.Permanent });
        em.AddComponentData(tempCarrier, new Game.Tools.Temp());

        Entity alreadyDeleted = em.Make(9603);
        em.AddComponentData(alreadyDeleted, new Game.Tools.CreationDefinition { m_Flags = Game.Tools.CreationFlags.Permanent });
        em.AddComponentData(alreadyDeleted, new Game.Common.Deleted());

        Entity nonPermanent = em.Make(9604);
        em.AddComponentData(nonPermanent, new Game.Tools.CreationDefinition { m_Flags = Game.Tools.CreationFlags.None });

        // Idle frame before any undo: must NOT touch orphanPerm.
        undoSys.UpdateForTest();
        Check(barrier.CurrentBuffer == null, "scenario 32: idle system emits nothing before undo");

        // Active undo finishes on this frame (hadActive=true -> hasActive=false => m_PendingCarrierCleanupFrames=4, then decrements to 3).
        toolSys.HasActiveUndo = true;
        toolSys.OnTickUndo = _ => { toolSys.HasActiveUndo = false; };
        undoSys.UpdateForTest();
        var buf1 = barrier.CurrentBuffer;
        var deleted1 = new List<Entity>();
        if (buf1 != null)
        {
            foreach (var cmd in buf1.Commands)
            {
                if (cmd.Kind == EntityCommandBuffer.Kind.AddComponent && cmd.Type == typeof(Game.Common.Deleted))
                {
                    deleted1.Add(cmd.Entity);
                }
            }
        }
        Check(deleted1.Count == 1 && deleted1[0] == orphanPerm,
            "scenario 32: marks only orphaned Permanent carrier Deleted (ignores Updated/Temp/Deleted/non-Permanent)");

        // Simulate ClearSystem removing orphanPerm, then exhaust the remaining 3 post-undo frames.
        em.DestroyEntity(orphanPerm);
        barrier.ClearBuffer();
        toolSys.OnTickUndo = null;
        for (int f = 0; f < 3; f++)
        {
            undoSys.UpdateForTest();
        }
        Check(barrier.CurrentBuffer == null, "scenario 32: post-undo frames emit nothing when no orphans exist");

        // Spawn another orphan after the 4-frame window has expired: idle system must not scan/delete it.
        Entity lateOrphan = em.Make(9605);
        em.AddComponentData(lateOrphan, new Game.Tools.CreationDefinition { m_Flags = Game.Tools.CreationFlags.Permanent });
        undoSys.UpdateForTest();
        Check(barrier.CurrentBuffer == null, "scenario 32: zero-cost idle state ignores late entity after cleanup window expires");
    }

    // 33. Covers remaining EmitPrefabSubNets & upgrade/accounting paths:
    //     - ground-following (!flattenTerrain && !bothFixed) adjusts curve Y to TerrainHeightData + localCurve.y
    //     - self-loop (startPos == endPos) combines IsFirst | IsLast on both CoursePos endpoints
    //     - non-inverting LHT entry flips Upgrade traffic handedness (ForbidLeftTurn <-> ForbidRightTurn, Left <-> Right) without inverting curve
    //     - physical upgrade with PrefabSubNetCount > 0 inline in BuildingUnit
    //     - deferred upgrade (!HasOwnerSubBuffer) with PrefabSubNetCount > 0 uses live owner Transform
    //     - invalid prefab SubNet entry (missing NetData) accounts cleanly in SkippedCount (Restored + Skipped == Total)
    private static void ScenarioPrefabSubNetEdgeCases()
    {
        var em = new EntityManager();
        Entity bldPrefab = MakeObjectPrefab(em, 6200);
        Entity upgPrefab = MakeObjectPrefab(em, 6201);
        Entity groundPathPrefab = MakeNetPrefab(em, 6202);
        Entity invalidNetPrefab = em.Make(6203); // deliberately no NetData

        em.AddComponentData(groundPathPrefab, new Game.Prefabs.NetGeometryData
        {
            m_Flags = Game.Net.GeometryFlags.None // neither FlattenTerrain nor FlipTrafficHandedness
        });

        var upgHandedness = new Game.Prefabs.CompositionFlags(
            Game.Prefabs.CompositionFlags.General.Crosswalk,
            Game.Prefabs.CompositionFlags.Side.ForbidLeftTurn,
            Game.Prefabs.CompositionFlags.Side.None);

        // Building prefab has 3 SubNets:
        // 0: ground-following curve (-1, -1) with local y = 0.5f and LHT FlipUpgradeTrafficHandedness (no FlipTrafficHandedness!)
        // 1: self-loop (start == end)
        // 2: invalid prefab (missing NetData)
        em.Buffer(bldPrefab,
            new Game.Prefabs.SubNet
            {
                m_Prefab = groundPathPrefab,
                m_Curve = new Colossal.Mathematics.Bezier4x3(
                    new float3(0f, 0.5f, 0f), new float3(2f, 0.5f, 0f), new float3(4f, 0.5f, 0f), new float3(6f, 0.5f, 0f)),
                m_NodeIndex = new int2(0, 1),
                m_ParentMesh = new int2(-1, -1),
                m_InvertMode = Game.Prefabs.NetInvertMode.LefthandTraffic,
                m_Upgrades = upgHandedness,
            },
            new Game.Prefabs.SubNet
            {
                m_Prefab = groundPathPrefab,
                m_Curve = new Colossal.Mathematics.Bezier4x3(
                    new float3(10f, 0f, 0f), new float3(12f, 0f, 2f), new float3(8f, 0f, 2f), new float3(10f, 0f, 0f)),
                m_NodeIndex = new int2(2, 2),
                m_ParentMesh = new int2(0, 0),
                m_InvertMode = Game.Prefabs.NetInvertMode.Never,
            },
            new Game.Prefabs.SubNet
            {
                m_Prefab = invalidNetPrefab,
                m_Curve = Straight(0f, 2f),
                m_NodeIndex = new int2(-1, -1),
                m_ParentMesh = new int2(-1, -1),
            });

        // Upgrade prefab has 1 SubNet
        em.Buffer(upgPrefab, new Game.Prefabs.SubNet
        {
            m_Prefab = groundPathPrefab,
            m_Curve = new Colossal.Mathematics.Bezier4x3(
                new float3(0f, 0f, 0f), new float3(1f, 0f, 0f), new float3(2f, 0f, 0f), new float3(3f, 0f, 0f)),
            m_NodeIndex = new int2(0, 1),
            m_ParentMesh = new int2(-1, -1),
        });

        // Part A: Inline physical upgrade (HasOwnerSubBuffer == true)
        float3 bldPos = new float3(20f, 10f, 30f);
        Entity bldInline = em.Make(9700);
        em.AddComponentData(bldInline, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bldInline, new Game.Objects.Transform { m_Position = bldPos, m_Rotation = quaternion.identity });
        em.AddComponentData(bldInline, new Game.Buildings.Building());
        em.AddBuffer<Game.Net.SubNet>(bldInline);

        float3 upgPos = new float3(25f, 10f, 30f);
        Entity upgInline = em.Make(9701);
        em.AddComponentData(upgInline, new Game.Prefabs.PrefabRef { m_Prefab = upgPrefab });
        em.AddComponentData(upgInline, new Game.Objects.Transform { m_Position = upgPos, m_Rotation = quaternion.identity });
        em.AddComponentData(upgInline, new Game.Objects.Object());
        em.AddComponentData(upgInline, new Game.Buildings.ServiceUpgrade());
        em.Buffer(bldInline, new Game.Buildings.InstalledUpgrade { m_Upgrade = upgInline });

        var planInline = new DeletionPlan(
            new HashSet<Entity> { bldInline },
            new HashSet<Entity> { bldInline, upgInline },
            new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager mgrInline = Capture(em, planInline);
        // 1 building + 3 bldPrefab SubNets + 1 inline upgrade + 1 upgPrefab SubNet = 6
        Check(mgrInline.GetUiSnapshot(UndoAssetMask.Buildings).NextCount == 6,
            "scenario 33 (inline): TotalCount == 6");

        em.DestroyEntity(bldInline);
        em.DestroyEntity(upgInline);
        Check(mgrInline.StartUndo(UndoAssetMask.Buildings), "scenario 33 (inline): undo starts");
        var terrainSys = new Game.Simulation.TerrainSystem
        {
            Data = new Game.Simulation.TerrainHeightData { isCreated = true, HeightSampler = p => 10f + p.x * 0.1f }
        };
        UnityEngine.Time.frameCount += 5;
        var ecbInline = new EntityCommandBuffer();
        mgrInline.Step(em, ecbInline, terrainSys, null, isLeftHandTraffic: true, out bool doneInline);
        Check(doneInline, "scenario 33 (inline): completes in 1 Step");

        var inlineFinished = mgrInline.GetUiSnapshot(UndoAssetMask.Buildings);
        Check(inlineFinished.FinishedRestoredCount == 5 && inlineFinished.FinishedSkippedCount == 1 &&
              inlineFinished.FinishedRestoredCount + inlineFinished.FinishedSkippedCount == inlineFinished.FinishedTotalCount,
            "scenario 33 (inline): Restored=5 + Skipped=1 == Total=6");

        var courses = ReadEmittedComponents<Game.Tools.NetCourse>(ecbInline);
        var upgraded = ReadEmittedComponents<Game.Net.Upgraded>(ecbInline);
        Check(courses.Count == 3, "scenario 33 (inline): emits 2 valid bld SubNets + 1 upgrade SubNet");
        if (courses.Count == 3)
        {
            // Curve 0: non-inverting LHT keeps curve direction (a.x = 20, d.x = 26), terrain at x=20 is 12.0 + 0.5 local = 12.5!
            Check(math.abs(courses[0].m_Curve.a.x - 20f) < 1e-4f && math.abs(courses[0].m_Curve.a.y - 12.5f) < 1e-4f,
                "scenario 33 (inline): ground-following AdjustPosition samples terrain (12.0) + localCurve.a.y (0.5) = 12.5 without inverting");
            // Curve 1: self-loop combines IsFirst | IsLast on both endpoints
            var bothFlags = Game.Tools.CoursePosFlags.IsFirst | Game.Tools.CoursePosFlags.IsLast;
            Check((courses[1].m_StartPosition.m_Flags & bothFlags) == bothFlags &&
                  (courses[1].m_EndPosition.m_Flags & bothFlags) == bothFlags,
                "scenario 33 (inline): self-loop SubNet sets IsFirst | IsLast on both endpoints");
        }
        // Curve 0 had Side.ForbidLeftTurn on m_Left, no FlipTrafficHandedness -> FlipUpgradeTrafficHandedness moves it to m_Right as ForbidRightTurn!
        Check(upgraded.Count == 1 &&
              upgraded[0].m_Flags.m_Left == Game.Prefabs.CompositionFlags.Side.None &&
              upgraded[0].m_Flags.m_Right == Game.Prefabs.CompositionFlags.Side.ForbidRightTurn,
            "scenario 33 (inline): FlipUpgradeTrafficHandedness swaps Left<->Right and ForbidLeftTurn->ForbidRightTurn");

        // Part B: Deferred upgrade (!HasOwnerSubBuffer) with PrefabSubNetCount > 0 uses live owner Transform
        float3 bld2Pos = new float3(80f, 10f, 30f);
        float3 upg2CapturedPos = new float3(85f, 10f, 30f);
        float3 upg2LiveSnappedPos = new float3(85.02f, 10.02f, 30.01f);
        Entity bldDef = em.Make(9800);
        em.AddComponentData(bldDef, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bldDef, new Game.Objects.Transform { m_Position = bld2Pos, m_Rotation = quaternion.identity });
        em.AddComponentData(bldDef, new Game.Buildings.Building());
        // Deliberately NO SubNet/SubArea/SubObject buffer -> HasOwnerSubBuffer == false

        Entity upgDef = em.Make(9801);
        em.AddComponentData(upgDef, new Game.Prefabs.PrefabRef { m_Prefab = upgPrefab });
        em.AddComponentData(upgDef, new Game.Objects.Transform { m_Position = upg2CapturedPos, m_Rotation = quaternion.identity });
        em.AddComponentData(upgDef, new Game.Objects.Object());
        em.AddComponentData(upgDef, new Game.Buildings.ServiceUpgrade());
        em.Buffer(bldDef, new Game.Buildings.InstalledUpgrade { m_Upgrade = upgDef });

        var planDef = new DeletionPlan(
            new HashSet<Entity> { bldDef },
            new HashSet<Entity> { bldDef, upgDef },
            new HashSet<Entity>(), new HashSet<Entity>(), new HashSet<Entity>());
        DeletionUndoManager mgrDef = Capture(em, planDef);
        em.DestroyEntity(bldDef);
        em.DestroyEntity(upgDef);

        Check(mgrDef.StartUndo(UndoAssetMask.Buildings), "scenario 33 (deferred): undo starts");
        StepOnce(em, mgrDef, out bool doneDef1); // BuildingUnit + Boundary
        Check(!doneDef1, "scenario 33 (deferred): pauses at Boundary before deferred Upgrade");

        Entity liveBldDef = em.Make(9900);
        em.AddComponentData(liveBldDef, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(liveBldDef, new Game.Objects.Transform { m_Position = bld2Pos, m_Rotation = quaternion.identity });
        StepOnce(em, mgrDef, out bool doneDef2); // Upgrade + Boundary
        Check(!doneDef2, "scenario 33 (deferred): pauses at Boundary before deferred Upgrade's SubObjectUnit");

        Entity liveUpgDef = em.Make(9901);
        em.AddComponentData(liveUpgDef, new Game.Prefabs.PrefabRef { m_Prefab = upgPrefab });
        em.AddComponentData(liveUpgDef, new Game.Objects.Transform { m_Position = upg2LiveSnappedPos, m_Rotation = quaternion.identity });

        UnityEngine.Time.frameCount += 5;
        var ecbDef3 = new EntityCommandBuffer();
        mgrDef.Step(em, ecbDef3, terrainSys, null, false, out bool doneDef3);
        Check(doneDef3, "scenario 33 (deferred): completes on SubObjectUnit");

        var defCourses = ReadEmittedComponents<Game.Tools.NetCourse>(ecbDef3);
        var defCreators = ReadEmittedComponents<Game.Tools.CreationDefinition>(ecbDef3);
        Check(defCreators.Count == 1 && defCreators[0].m_Owner == liveUpgDef &&
              defCourses.Count == 1 && math.distancesq(defCourses[0].m_StartPosition.m_Position, upg2LiveSnappedPos) < 1e-6f,
            "scenario 33 (deferred): live-owner SubObjectUnit binds liveUpgDef and uses its live Transform");
    }

    // -------------------------------------------------------------------------
    // GlobalUndo Road-Build Reconciliation Scenarios (34 - 38)
    // -------------------------------------------------------------------------

    private static void ApplyGlobalUndoEcb(EntityManager em, EntityCommandBuffer ecb, List<Game.Tools.CreationDefinition> capturedCreations = null)
    {
        if (ecb == null || ecb.Commands.Count == 0) return;
        foreach (EntityCommandBuffer.Command cmd in ecb.Commands)
        {
            if (cmd.Kind == EntityCommandBuffer.Kind.Create)
            {
                if (!em.Exists(cmd.Entity))
                {
                    em.RawCreate(cmd.Entity.Index);
                }
            }
            else if (cmd.Kind == EntityCommandBuffer.Kind.AddComponent)
            {
                if (capturedCreations != null && cmd.Type == typeof(Game.Tools.CreationDefinition))
                {
                    capturedCreations.Add((Game.Tools.CreationDefinition)cmd.Value);
                }
                if (em.Exists(cmd.Entity))
                {
                    em.Components(cmd.Entity)[cmd.Type] = cmd.Value;
                }
            }
            else if (cmd.Kind == EntityCommandBuffer.Kind.AddBuffer)
            {
                if (em.Exists(cmd.Entity))
                {
                    em.Components(cmd.Entity)[cmd.Value.GetType()] = cmd.Value;
                }
            }
            else if (cmd.Kind == EntityCommandBuffer.Kind.RemoveComponent)
            {
                if (em.Exists(cmd.Entity))
                {
                    em.Components(cmd.Entity).Remove(cmd.Type);
                }
            }
        }
    }

    private static void CreateGlobalUndoQueries(
        EntityManager em,
        out EntityQuery tempQuery,
        out EntityQuery zoneTempQuery,
        out EntityQuery brushTempQuery,
        out EntityQuery createdQuery)
    {
        tempQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<Game.Tools.Temp>(),
            ComponentType.Exclude<Game.Common.Deleted>());
        zoneTempQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<Game.Tools.Temp>(),
            ComponentType.ReadOnly<Game.Zones.Block>(),
            ComponentType.ReadOnly<Game.Zones.Cell>(),
            ComponentType.Exclude<Game.Common.Deleted>());
        brushTempQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<Game.Tools.Temp>(),
            ComponentType.ReadOnly<Game.Tools.Brush>(),
            ComponentType.Exclude<Game.Common.Deleted>());
        createdQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<Game.Common.Applied>(),
            ComponentType.ReadOnly<Game.Common.Created>(),
            ComponentType.ReadOnly<Game.Prefabs.PrefabRef>(),
            ComponentType.Exclude<Game.Common.Deleted>(),
            ComponentType.Exclude<Game.Tools.Temp>());
    }

    private static Entity MakeNetNodeWithPrefab(EntityManager em, int id, Entity prefab, float3 pos)
    {
        Entity n = em.Make(id);
        em.AddComponentData(n, new Game.Prefabs.PrefabRef { m_Prefab = prefab });
        em.AddComponentData(n, new Game.Net.Node { m_Position = pos, m_Rotation = quaternion.identity });
        em.AddBuffer<Game.Net.ConnectedEdge>(n);
        return n;
    }

    private static Colossal.Mathematics.Bezier4x3 StraightLine(float3 a, float3 d) => new Colossal.Mathematics.Bezier4x3
    {
        a = a,
        b = math.lerp(a, d, 1f / 3f),
        c = math.lerp(a, d, 2f / 3f),
        d = d,
    };

    private static Entity MakeRoadEdge3D(EntityManager em, int id, Entity prefab, Entity start, Entity end, float3 startPos, float3 endPos)
    {
        Entity e = em.Make(id);
        em.AddComponentData(e, new Game.Prefabs.PrefabRef { m_Prefab = prefab });
        em.AddComponentData(e, new Game.Net.Edge { m_Start = start, m_End = end });
        em.AddComponentData(e, new Game.Net.Curve { m_Bezier = StraightLine(startPos, endPos), m_Length = math.distance(startPos, endPos) });
        em.AddBuffer<Game.Net.SubLane>(e);
        return e;
    }

    private static void ReplayGlobalUndoToCompletion(
        EntityManager em,
        GlobalUndoManager globalUndo,
        DeletionUndoManager delUndo,
        Game.Simulation.TerrainSystem terrainSys,
        List<Game.Tools.CreationDefinition> capturedCreations = null)
    {
        var barrier = new Game.Tools.ToolOutputBarrier();
        for (int iter = 0; iter < 20; iter++)
        {
            barrier.ClearBuffer();
            bool ticked = globalUndo.TickReplay(em, barrier, terrainSys, null, null, null, delUndo, out bool completed, isLeftHandTraffic: false);
            ApplyGlobalUndoEcb(em, barrier.CurrentBuffer, capturedCreations);
            UnityEngine.Time.frameCount++;
            if (completed || !ticked) break;
        }
    }

    private static void ScenarioGlobalUndoCombineShrinkInPlace()
    {
        var em = new EntityManager();
        var globalUndo = new GlobalUndoManager();
        var delUndo = new DeletionUndoManager();
        var terrainSys = new Game.Simulation.TerrainSystem();
        var toolSys = new Game.Tools.ToolSystem { activeTool = new Game.Tools.ToolBaseSystem { toolID = "Net Tool" } };
        CreateGlobalUndoQueries(em, out var tempQ, out var zoneQ, out var brushQ, out var createdQ);

        Entity roadPrefab = em.Make(5000);
        em.AddComponentData(roadPrefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeNetNodeWithPrefab(em, 10, roadPrefab, new float3(0f, 0f, 0f));
        Entity nodeB = MakeNetNodeWithPrefab(em, 11, roadPrefab, new float3(100f, 0f, 0f));
        Entity edgeOld = MakeRoadEdge3D(em, 20, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        Link(em, nodeA, edgeOld);
        Link(em, nodeB, edgeOld);

        // PreApply: Combine edgeOld (A->B) + extension into tempCombined (A->C), deleting nodeB
        Entity tempNodeC = MakeNetNodeWithPrefab(em, 201, roadPrefab, new float3(200f, 0f, 0f));
        em.AddComponentData(tempNodeC, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempCombined = MakeRoadEdge3D(em, 200, roadPrefab, nodeA, tempNodeC, new float3(0f, 0f, 0f), new float3(200f, 0f, 0f));
        em.AddComponentData(tempCombined, new Game.Tools.Temp
        {
            m_Original = edgeOld,
            m_Flags = Game.Tools.TempFlags.Replace | Game.Tools.TempFlags.Combine,
        });

        Entity tempDelNodeB = MakeNetNodeWithPrefab(em, 202, roadPrefab, new float3(100f, 0f, 0f));
        em.AddComponentData(tempDelNodeB, new Game.Tools.Temp { m_Original = nodeB, m_Flags = Game.Tools.TempFlags.Delete });

        UnityEngine.Time.frameCount = 500;
        globalUndo.OnPreApply(em, toolSys, terrainSys, delUndo, tempQ, zoneQ, brushQ);

        // Simulate ApplyNetSystem: edgeOld & nodeB marked Deleted; tempCombined & tempNodeC promoted in-place
        em.AddComponentData(edgeOld, new Game.Common.Deleted());
        em.AddComponentData(nodeB, new Game.Common.Deleted());
        em.DestroyEntity(tempDelNodeB);
        em.RemoveComponent<Game.Tools.Temp>(tempCombined);
        em.AddComponentData(tempCombined, new Game.Common.Created());
        em.AddComponentData(tempCombined, new Game.Common.Applied());
        em.RemoveComponent<Game.Tools.Temp>(tempNodeC);
        em.AddComponentData(tempNodeC, new Game.Common.Created());
        em.AddComponentData(tempNodeC, new Game.Common.Applied());
        em.Buffer(nodeA, new Game.Net.ConnectedEdge { m_Edge = tempCombined });
        em.Buffer(tempNodeC, new Game.Net.ConnectedEdge { m_Edge = tempCombined });

        globalUndo.OnPostApply(em, createdQ);
        globalUndo.FlushOpenStroke();

        GlobalUndoTransaction tx = globalUndo.PeekTopTransaction();
        Check(tx != null, "scenario 34: transaction pushed");
        Check(tx.Kind == GlobalUndoActionKind.Build, "scenario 34: classified as Build, not UpgradeReplace");
        Check(tx.CreatedEntities.Count == 0, "scenario 34: combined edge excluded from CreatedEntities");
        Check(tx.DeletedCount == 0, "scenario 34: old edge and nodeB excluded from DeletedSnapshot");
        Check(tx.InPlaceNetPatches.Count == 1 && tx.InPlaceNetPatches.ContainsKey(tempCombined), "scenario 34: single InPlaceNetPatch on combined edge");
        Check(tx.InPlaceNetPatches[tempCombined].MoveNodeEntity == tempNodeC && tx.InPlaceNetPatches[tempCombined].RequireSingleConnectedEdge,
            "scenario 34: patch records MoveNodeEntity=C and RequireSingleConnectedEdge=true");

        // Replay undo
        Check(globalUndo.StartOrResumeUndo(UndoAssetMask.All, delUndo), "scenario 34: undo starts");
        ReplayGlobalUndoToCompletion(em, globalUndo, delUndo, terrainSys);

        Check(!em.HasComponent<Game.Common.Deleted>(tempCombined), "scenario 34: combined edge NOT deleted");
        Check(!em.HasComponent<Game.Common.Deleted>(tempNodeC), "scenario 34: far node C NOT deleted");
        Game.Net.Node cAfter = em.GetComponentData<Game.Net.Node>(tempNodeC);
        Check(math.distancesq(cAfter.m_Position, new float3(100f, 0f, 0f)) < 1e-4f, "scenario 34: node C moved back to node B position (100,0,0)");
        Game.Net.Curve curveAfter = em.GetComponentData<Game.Net.Curve>(tempCombined);
        Check(math.abs(curveAfter.m_Length - 100f) < 1e-3f && math.distancesq(curveAfter.m_Bezier.d, new float3(100f, 0f, 0f)) < 1e-4f,
            "scenario 34: combined edge curve shrunk back to original 0..100");
        Check(em.HasComponent<Game.Common.Updated>(tempCombined) && em.HasComponent<Game.Common.Updated>(tempNodeC) && em.HasComponent<Game.Common.Updated>(nodeA),
            "scenario 34: edge and endpoints marked Updated");
        var refreshPos = new List<float3>();
        Check(globalUndo.TryConsumeInPlaceNetRefresh(refreshPos) && refreshPos.Count == 1, "scenario 34: roadside refresh queued");
    }

    private static void ScenarioGlobalUndoReplaceAdjacentInPlace()
    {
        var em = new EntityManager();
        var globalUndo = new GlobalUndoManager();
        var delUndo = new DeletionUndoManager();
        var terrainSys = new Game.Simulation.TerrainSystem();
        var toolSys = new Game.Tools.ToolSystem { activeTool = new Game.Tools.ToolBaseSystem { toolID = "Net Tool" } };
        CreateGlobalUndoQueries(em, out var tempQ, out var zoneQ, out var brushQ, out var createdQ);

        Entity roadPrefab = em.Make(5000);
        em.AddComponentData(roadPrefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeNetNodeWithPrefab(em, 10, roadPrefab, new float3(0f, 0f, 0f));
        Entity nodeB = MakeNetNodeWithPrefab(em, 11, roadPrefab, new float3(100f, 0f, 0f));
        Entity edgeAdj = MakeRoadEdge3D(em, 20, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        Link(em, nodeA, edgeAdj);
        Link(em, nodeB, edgeAdj);

        // PreApply: new branch (B->D) causes CS2 to Replace edgeAdj -> tempAdjPrime (same prefab, slightly bent curve)
        Entity tempNodeD = MakeNetNodeWithPrefab(em, 201, roadPrefab, new float3(100f, 0f, 80f));
        em.AddComponentData(tempNodeD, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempBranch = MakeRoadEdge3D(em, 202, roadPrefab, nodeB, tempNodeD, new float3(100f, 0f, 0f), new float3(100f, 0f, 80f));
        em.AddComponentData(tempBranch, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempAdjPrime = MakeRoadEdge3D(em, 200, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        var bentBezier = StraightLine(new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        bentBezier.b.z = 2f;
        em.Set(tempAdjPrime, new Game.Net.Curve { m_Bezier = bentBezier, m_Length = 100.5f });
        em.AddComponentData(tempAdjPrime, new Game.Tools.Temp { m_Original = edgeAdj, m_Flags = Game.Tools.TempFlags.Replace });

        UnityEngine.Time.frameCount = 600;
        globalUndo.OnPreApply(em, toolSys, terrainSys, delUndo, tempQ, zoneQ, brushQ);

        // Simulate ApplyNetSystem
        em.AddComponentData(edgeAdj, new Game.Common.Deleted());
        foreach (Entity e in new[] { tempAdjPrime, tempBranch, tempNodeD })
        {
            em.RemoveComponent<Game.Tools.Temp>(e);
            em.AddComponentData(e, new Game.Common.Created());
            em.AddComponentData(e, new Game.Common.Applied());
        }
        em.Buffer(nodeA, new Game.Net.ConnectedEdge { m_Edge = tempAdjPrime });
        em.Buffer(nodeB, new Game.Net.ConnectedEdge { m_Edge = tempAdjPrime }, new Game.Net.ConnectedEdge { m_Edge = tempBranch });
        em.Buffer(tempNodeD, new Game.Net.ConnectedEdge { m_Edge = tempBranch });

        globalUndo.OnPostApply(em, createdQ);
        globalUndo.FlushOpenStroke();

        GlobalUndoTransaction tx = globalUndo.PeekTopTransaction();
        Check(tx != null && tx.Kind == GlobalUndoActionKind.Build, "scenario 35: classified as Build");
        Check(tx.CreatedEntities.Count == 1 && tx.CreatedEntities[0].Entity == tempBranch, "scenario 35: only new branch in CreatedEntities");
        Check(tx.DeletedCount == 0, "scenario 35: replaced adjacent road excluded from DeletedSnapshot");
        Check(tx.InPlaceNetPatches.Count == 1 && tx.InPlaceNetPatches.ContainsKey(tempAdjPrime), "scenario 35: InPlaceNetPatch recorded on tempAdjPrime");

        Check(globalUndo.StartOrResumeUndo(UndoAssetMask.All, delUndo), "scenario 35: undo starts");
        ReplayGlobalUndoToCompletion(em, globalUndo, delUndo, terrainSys);

        Check(em.HasComponent<Game.Common.Deleted>(tempBranch), "scenario 35: new branch deleted");
        Check(!em.HasComponent<Game.Common.Deleted>(tempAdjPrime), "scenario 35: replaced adjacent road NOT deleted");
        Game.Net.Curve restoredCurve = em.GetComponentData<Game.Net.Curve>(tempAdjPrime);
        Check(math.abs(restoredCurve.m_Bezier.b.z) < 1e-4f && math.abs(restoredCurve.m_Length - 100f) < 1e-3f,
            "scenario 35: adjacent road curve restored in-place");
        Check(em.HasComponent<Game.Common.Updated>(tempAdjPrime), "scenario 35: adjacent road marked Updated");
    }

    private static void ScenarioGlobalUndoSplitPreservesHalves()
    {
        var em = new EntityManager();
        var globalUndo = new GlobalUndoManager();
        var delUndo = new DeletionUndoManager();
        var terrainSys = new Game.Simulation.TerrainSystem();
        var toolSys = new Game.Tools.ToolSystem { activeTool = new Game.Tools.ToolBaseSystem { toolID = "Net Tool" } };
        CreateGlobalUndoQueries(em, out var tempQ, out var zoneQ, out var brushQ, out var createdQ);

        Entity roadPrefab = em.Make(5000);
        em.AddComponentData(roadPrefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeNetNodeWithPrefab(em, 10, roadPrefab, new float3(0f, 0f, 0f));
        Entity nodeB = MakeNetNodeWithPrefab(em, 11, roadPrefab, new float3(100f, 0f, 0f));
        Entity edgeOld = MakeRoadEdge3D(em, 20, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        Link(em, nodeA, edgeOld);
        Link(em, nodeB, edgeOld);

        // PreApply: SplitEdge deletes edgeOld, creates nodeM, nodeD, part1 (A->M), part2 (M->B), branch (M->D)
        Entity tempDelOld = MakeRoadEdge3D(em, 200, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        em.AddComponentData(tempDelOld, new Game.Tools.Temp { m_Original = edgeOld, m_Flags = Game.Tools.TempFlags.Delete });

        Entity tempNodeM = MakeNetNodeWithPrefab(em, 201, roadPrefab, new float3(50f, 0f, 0f));
        em.AddComponentData(tempNodeM, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempNodeD = MakeNetNodeWithPrefab(em, 202, roadPrefab, new float3(50f, 0f, 60f));
        em.AddComponentData(tempNodeD, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempPart1 = MakeRoadEdge3D(em, 203, roadPrefab, nodeA, tempNodeM, new float3(0f, 0f, 0f), new float3(50f, 0f, 0f));
        em.AddComponentData(tempPart1, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempPart2 = MakeRoadEdge3D(em, 204, roadPrefab, tempNodeM, nodeB, new float3(50f, 0f, 0f), new float3(100f, 0f, 0f));
        em.AddComponentData(tempPart2, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempBranch = MakeRoadEdge3D(em, 205, roadPrefab, tempNodeM, tempNodeD, new float3(50f, 0f, 0f), new float3(50f, 0f, 60f));
        em.AddComponentData(tempBranch, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        UnityEngine.Time.frameCount = 700;
        globalUndo.OnPreApply(em, toolSys, terrainSys, delUndo, tempQ, zoneQ, brushQ);

        // Simulate ApplyNetSystem
        em.AddComponentData(edgeOld, new Game.Common.Deleted());
        em.DestroyEntity(tempDelOld);
        foreach (Entity e in new[] { tempNodeM, tempNodeD, tempPart1, tempPart2, tempBranch })
        {
            em.RemoveComponent<Game.Tools.Temp>(e);
            em.AddComponentData(e, new Game.Common.Created());
            em.AddComponentData(e, new Game.Common.Applied());
        }
        em.Buffer(nodeA, new Game.Net.ConnectedEdge { m_Edge = tempPart1 });
        em.Buffer(nodeB, new Game.Net.ConnectedEdge { m_Edge = tempPart2 });
        em.Buffer(tempNodeM,
            new Game.Net.ConnectedEdge { m_Edge = tempPart1 },
            new Game.Net.ConnectedEdge { m_Edge = tempPart2 },
            new Game.Net.ConnectedEdge { m_Edge = tempBranch });
        em.Buffer(tempNodeD, new Game.Net.ConnectedEdge { m_Edge = tempBranch });

        globalUndo.OnPostApply(em, createdQ);
        globalUndo.FlushOpenStroke();

        GlobalUndoTransaction tx = globalUndo.PeekTopTransaction();
        Check(tx != null && tx.Kind == GlobalUndoActionKind.Build, "scenario 36: classified as Build");
        Check(tx.CreatedEntities.Count == 1 && tx.CreatedEntities[0].Entity == tempBranch, "scenario 36: only new branch in CreatedEntities");
        Check(tx.DeletedCount == 0, "scenario 36: old split road excluded from DeletedSnapshot");
        Check(tx.PreservedSplitRefreshTargets.Contains(tempPart1) &&
              tx.PreservedSplitRefreshTargets.Contains(tempPart2) &&
              tx.PreservedSplitRefreshTargets.Contains(tempNodeM),
            "scenario 36: split halves and mid node recorded in PreservedSplitRefreshTargets");

        Check(globalUndo.StartOrResumeUndo(UndoAssetMask.All, delUndo), "scenario 36: undo starts");
        ReplayGlobalUndoToCompletion(em, globalUndo, delUndo, terrainSys);

        Check(em.HasComponent<Game.Common.Deleted>(tempBranch), "scenario 36: new branch deleted");
        Check(!em.HasComponent<Game.Common.Deleted>(tempPart1) && !em.HasComponent<Game.Common.Deleted>(tempPart2) && !em.HasComponent<Game.Common.Deleted>(tempNodeM),
            "scenario 36: split halves and mid node survive intact");
        Check(em.HasComponent<Game.Common.Updated>(tempPart1) && em.HasComponent<Game.Common.Updated>(tempPart2) && em.HasComponent<Game.Common.Updated>(tempNodeM),
            "scenario 36: split halves and mid node marked Updated");
        var refreshPos = new List<float3>();
        Check(globalUndo.TryConsumeInPlaceNetRefresh(refreshPos) && refreshPos.Count == 2, "scenario 36: preserved split midpoints queued for roadside refresh");
    }

    private static void ScenarioGlobalUndoSplitWithAutoDemolishedBuilding()
    {
        var em = new EntityManager();
        var globalUndo = new GlobalUndoManager();
        var delUndo = new DeletionUndoManager();
        var terrainSys = new Game.Simulation.TerrainSystem();
        var toolSys = new Game.Tools.ToolSystem { activeTool = new Game.Tools.ToolBaseSystem { toolID = "Net Tool" } };
        CreateGlobalUndoQueries(em, out var tempQ, out var zoneQ, out var brushQ, out var createdQ);

        Entity roadPrefab = em.Make(5000);
        em.AddComponentData(roadPrefab, new Game.Prefabs.NetData());
        Entity bldPrefab = MakeObjectPrefab(em, 5100);

        Entity nodeA = MakeNetNodeWithPrefab(em, 10, roadPrefab, new float3(0f, 0f, 0f));
        Entity nodeB = MakeNetNodeWithPrefab(em, 11, roadPrefab, new float3(100f, 0f, 0f));
        Entity edgeOld = MakeRoadEdge3D(em, 20, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        Link(em, nodeA, edgeOld);
        Link(em, nodeB, edgeOld);

        // Existing roadside building auto-demolished by the new branch road
        Entity bld = em.Make(30);
        em.AddComponentData(bld, new Game.Prefabs.PrefabRef { m_Prefab = bldPrefab });
        em.AddComponentData(bld, new Game.Objects.Object());
        em.AddComponentData(bld, new Game.Buildings.Building { m_RoadEdge = edgeOld, m_CurvePosition = 0.5f });
        em.AddComponentData(bld, new Game.Objects.Transform { m_Position = new float3(50f, 0f, 20f), m_Rotation = quaternion.identity });

        Entity tempDelBld = em.Make(210);
        em.AddComponentData(tempDelBld, new Game.Tools.Temp { m_Original = bld, m_Flags = Game.Tools.TempFlags.Delete });

        Entity tempDelOld = MakeRoadEdge3D(em, 200, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        em.AddComponentData(tempDelOld, new Game.Tools.Temp { m_Original = edgeOld, m_Flags = Game.Tools.TempFlags.Delete });

        Entity tempNodeM = MakeNetNodeWithPrefab(em, 201, roadPrefab, new float3(50f, 0f, 0f));
        em.AddComponentData(tempNodeM, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });
        Entity tempNodeD = MakeNetNodeWithPrefab(em, 202, roadPrefab, new float3(50f, 0f, 60f));
        em.AddComponentData(tempNodeD, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });
        Entity tempPart1 = MakeRoadEdge3D(em, 203, roadPrefab, nodeA, tempNodeM, new float3(0f, 0f, 0f), new float3(50f, 0f, 0f));
        em.AddComponentData(tempPart1, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });
        Entity tempPart2 = MakeRoadEdge3D(em, 204, roadPrefab, tempNodeM, nodeB, new float3(50f, 0f, 0f), new float3(100f, 0f, 0f));
        em.AddComponentData(tempPart2, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });
        Entity tempBranch = MakeRoadEdge3D(em, 205, roadPrefab, tempNodeM, tempNodeD, new float3(50f, 0f, 0f), new float3(50f, 0f, 60f));
        em.AddComponentData(tempBranch, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        UnityEngine.Time.frameCount = 800;
        globalUndo.OnPreApply(em, toolSys, terrainSys, delUndo, tempQ, zoneQ, brushQ);

        // Simulate ApplyNetSystem + ApplyObjectsSystem
        em.AddComponentData(edgeOld, new Game.Common.Deleted());
        em.AddComponentData(bld, new Game.Common.Deleted());
        em.DestroyEntity(tempDelOld);
        em.DestroyEntity(tempDelBld);
        foreach (Entity e in new[] { tempNodeM, tempNodeD, tempPart1, tempPart2, tempBranch })
        {
            em.RemoveComponent<Game.Tools.Temp>(e);
            em.AddComponentData(e, new Game.Common.Created());
            em.AddComponentData(e, new Game.Common.Applied());
        }
        em.Buffer(nodeA, new Game.Net.ConnectedEdge { m_Edge = tempPart1 });
        em.Buffer(nodeB, new Game.Net.ConnectedEdge { m_Edge = tempPart2 });
        em.Buffer(tempNodeM,
            new Game.Net.ConnectedEdge { m_Edge = tempPart1 },
            new Game.Net.ConnectedEdge { m_Edge = tempPart2 },
            new Game.Net.ConnectedEdge { m_Edge = tempBranch });
        em.Buffer(tempNodeD, new Game.Net.ConnectedEdge { m_Edge = tempBranch });

        globalUndo.OnPostApply(em, createdQ);
        globalUndo.FlushOpenStroke();

        GlobalUndoTransaction tx = globalUndo.PeekTopTransaction();
        Check(tx != null && tx.Kind == GlobalUndoActionKind.Build, "scenario 37: classified as Build even with auto-demolished building");
        Check(tx.CreatedEntities.Count == 1 && tx.CreatedEntities[0].Entity == tempBranch, "scenario 37: only new branch in CreatedEntities");
        Check(tx.DeletedCount == 1 && tx.DeletedSnapshot.Counts.Buildings == 1 && tx.DeletedSnapshot.Counts.Networks == 0,
            "scenario 37: DeletedSnapshot contains ONLY the auto-demolished building, NOT the split road");

        var creations = new List<Game.Tools.CreationDefinition>();
        Check(globalUndo.StartOrResumeUndo(UndoAssetMask.All, delUndo), "scenario 37: undo starts");
        ReplayGlobalUndoToCompletion(em, globalUndo, delUndo, terrainSys, creations);

        Check(em.HasComponent<Game.Common.Deleted>(tempBranch), "scenario 37: branch deleted");
        Check(!em.HasComponent<Game.Common.Deleted>(tempPart1) && !em.HasComponent<Game.Common.Deleted>(tempPart2),
            "scenario 37: split halves preserved intact");
        Check(creations.Count == 1 && creations[0].m_Prefab == bldPrefab,
            "scenario 37: auto-demolished roadside building emitted for restore");
    }

    private static void ScenarioGlobalUndoCombineTopologySafetyGuard()
    {
        var em = new EntityManager();
        var globalUndo = new GlobalUndoManager();
        var delUndo = new DeletionUndoManager();
        var terrainSys = new Game.Simulation.TerrainSystem();
        var toolSys = new Game.Tools.ToolSystem { activeTool = new Game.Tools.ToolBaseSystem { toolID = "Net Tool" } };
        CreateGlobalUndoQueries(em, out var tempQ, out var zoneQ, out var brushQ, out var createdQ);

        Entity roadPrefab = em.Make(5000);
        em.AddComponentData(roadPrefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeNetNodeWithPrefab(em, 10, roadPrefab, new float3(0f, 0f, 0f));
        Entity nodeB = MakeNetNodeWithPrefab(em, 11, roadPrefab, new float3(100f, 0f, 0f));
        Entity edgeOld = MakeRoadEdge3D(em, 20, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        Link(em, nodeA, edgeOld);
        Link(em, nodeB, edgeOld);

        Entity tempNodeC = MakeNetNodeWithPrefab(em, 201, roadPrefab, new float3(200f, 0f, 0f));
        em.AddComponentData(tempNodeC, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });
        Entity tempCombined = MakeRoadEdge3D(em, 200, roadPrefab, nodeA, tempNodeC, new float3(0f, 0f, 0f), new float3(200f, 0f, 0f));
        em.AddComponentData(tempCombined, new Game.Tools.Temp { m_Original = edgeOld, m_Flags = Game.Tools.TempFlags.Replace | Game.Tools.TempFlags.Combine });
        Entity tempDelNodeB = MakeNetNodeWithPrefab(em, 202, roadPrefab, new float3(100f, 0f, 0f));
        em.AddComponentData(tempDelNodeB, new Game.Tools.Temp { m_Original = nodeB, m_Flags = Game.Tools.TempFlags.Delete });

        UnityEngine.Time.frameCount = 900;
        globalUndo.OnPreApply(em, toolSys, terrainSys, delUndo, tempQ, zoneQ, brushQ);

        em.AddComponentData(edgeOld, new Game.Common.Deleted());
        em.AddComponentData(nodeB, new Game.Common.Deleted());
        em.DestroyEntity(tempDelNodeB);
        em.RemoveComponent<Game.Tools.Temp>(tempCombined);
        em.AddComponentData(tempCombined, new Game.Common.Created());
        em.AddComponentData(tempCombined, new Game.Common.Applied());
        em.RemoveComponent<Game.Tools.Temp>(tempNodeC);
        em.AddComponentData(tempNodeC, new Game.Common.Created());
        em.AddComponentData(tempNodeC, new Game.Common.Applied());
        em.Buffer(nodeA, new Game.Net.ConnectedEdge { m_Edge = tempCombined });
        em.Buffer(tempNodeC, new Game.Net.ConnectedEdge { m_Edge = tempCombined });

        globalUndo.OnPostApply(em, createdQ);
        globalUndo.FlushOpenStroke();

        // Player builds a LATER road attached to node C before undoing the Combine step
        Entity nodeE = MakeNetNodeWithPrefab(em, 300, roadPrefab, new float3(200f, 0f, 80f));
        Entity edgeLater = MakeRoadEdge3D(em, 301, roadPrefab, tempNodeC, nodeE, new float3(200f, 0f, 0f), new float3(200f, 0f, 80f));
        em.Buffer(tempNodeC,
            new Game.Net.ConnectedEdge { m_Edge = tempCombined },
            new Game.Net.ConnectedEdge { m_Edge = edgeLater });
        em.Buffer(nodeE, new Game.Net.ConnectedEdge { m_Edge = edgeLater });

        // Undo must skip moving node C / shrinking tempCombined to protect edgeLater
        Check(globalUndo.StartOrResumeUndo(UndoAssetMask.All, delUndo), "scenario 38: undo starts");
        ReplayGlobalUndoToCompletion(em, globalUndo, delUndo, terrainSys);

        Game.Net.Node cAfter = em.GetComponentData<Game.Net.Node>(tempNodeC);
        Check(math.distancesq(cAfter.m_Position, new float3(200f, 0f, 0f)) < 1e-4f,
            "scenario 38: node C stayed at (200,0,0) because edgeLater is connected");
        Game.Net.Curve curveAfter = em.GetComponentData<Game.Net.Curve>(tempCombined);
        Check(math.abs(curveAfter.m_Length - 200f) < 1e-3f,
            "scenario 38: tempCombined was NOT shrunk");
        Check(!em.HasComponent<Game.Common.Deleted>(edgeLater), "scenario 38: later road untouched");
        SelectiveUndoUiSnapshot ui = globalUndo.GetSelectiveUiSnapshot(UndoAssetMask.All, delUndo);
        Check(ui.FinishedSkippedCount == 1 && ui.FinishedNotice == "UndoSubmittedPartial",
            "scenario 38: reports skipped=1 and UndoSubmittedPartial");
    }

    private static void ScenarioGlobalUndoCombineExistingIntersectionFallback()
    {
        var em = new EntityManager();
        var globalUndo = new GlobalUndoManager();
        var delUndo = new DeletionUndoManager();
        var terrainSys = new Game.Simulation.TerrainSystem();
        var toolSys = new Game.Tools.ToolSystem { activeTool = new Game.Tools.ToolBaseSystem { toolID = "Net Tool" } };
        CreateGlobalUndoQueries(em, out var tempQ, out var zoneQ, out var brushQ, out var createdQ);

        Entity roadPrefab = em.Make(5000);
        em.AddComponentData(roadPrefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeNetNodeWithPrefab(em, 10, roadPrefab, new float3(0f, 0f, 0f));
        Entity nodeB = MakeNetNodeWithPrefab(em, 11, roadPrefab, new float3(100f, 0f, 0f));
        Entity edgeOld = MakeRoadEdge3D(em, 20, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        Link(em, nodeA, edgeOld);
        Link(em, nodeB, edgeOld);

        // Pre-existing intersection nodeC (NOT newly created in this stroke!) with another existing road
        Entity nodeCExisting = MakeNetNodeWithPrefab(em, 12, roadPrefab, new float3(200f, 0f, 0f));
        Entity nodeF = MakeNetNodeWithPrefab(em, 13, roadPrefab, new float3(200f, 0f, 80f));
        Entity existingCross = MakeRoadEdge3D(em, 21, roadPrefab, nodeCExisting, nodeF, new float3(200f, 0f, 0f), new float3(200f, 0f, 80f));
        Link(em, nodeCExisting, existingCross);
        Link(em, nodeF, existingCross);

        // PreApply: Combine edgeOld (A->B) straight into pre-existing nodeCExisting
        Entity tempCombined = MakeRoadEdge3D(em, 200, roadPrefab, nodeA, nodeCExisting, new float3(0f, 0f, 0f), new float3(200f, 0f, 0f));
        em.AddComponentData(tempCombined, new Game.Tools.Temp
        {
            m_Original = edgeOld,
            m_Flags = Game.Tools.TempFlags.Replace | Game.Tools.TempFlags.Combine,
        });
        Entity tempDelNodeB = MakeNetNodeWithPrefab(em, 202, roadPrefab, new float3(100f, 0f, 0f));
        em.AddComponentData(tempDelNodeB, new Game.Tools.Temp { m_Original = nodeB, m_Flags = Game.Tools.TempFlags.Delete });

        UnityEngine.Time.frameCount = 1000;
        globalUndo.OnPreApply(em, toolSys, terrainSys, delUndo, tempQ, zoneQ, brushQ);

        em.AddComponentData(edgeOld, new Game.Common.Deleted());
        em.AddComponentData(nodeB, new Game.Common.Deleted());
        em.DestroyEntity(tempDelNodeB);
        em.RemoveComponent<Game.Tools.Temp>(tempCombined);
        em.AddComponentData(tempCombined, new Game.Common.Created());
        em.AddComponentData(tempCombined, new Game.Common.Applied());
        em.Buffer(nodeA, new Game.Net.ConnectedEdge { m_Edge = tempCombined });
        em.Buffer(nodeCExisting,
            new Game.Net.ConnectedEdge { m_Edge = existingCross },
            new Game.Net.ConnectedEdge { m_Edge = tempCombined });

        globalUndo.OnPostApply(em, createdQ);
        globalUndo.FlushOpenStroke();

        GlobalUndoTransaction tx = globalUndo.PeekTopTransaction();
        Check(tx != null, "scenario 39: transaction pushed");
        Check(tx.InPlaceNetPatches.Count == 0, "scenario 39: pre-existing far node NOT reconciled as Combine shrink");
        Check(tx.CreatedEntities.Count == 1 && tx.CreatedEntities[0].Entity == tempCombined,
            "scenario 39: combined edge stays in CreatedEntities for Delete+Rebuild fallback");
        Check(tx.DeletedCount == 1, "scenario 39: old edge stays in DeletedSnapshot for fallback rebuild");

        var creations = new List<Game.Tools.CreationDefinition>();
        Check(globalUndo.StartOrResumeUndo(UndoAssetMask.All, delUndo), "scenario 39: undo starts");
        ReplayGlobalUndoToCompletion(em, globalUndo, delUndo, terrainSys, creations);

        Check(em.HasComponent<Game.Common.Deleted>(tempCombined), "scenario 39: combined bridge edge deleted");
        Check(!em.HasComponent<Game.Common.Deleted>(nodeCExisting), "scenario 39: pre-existing intersection NOT deleted");
        Game.Net.Node cAfter = em.GetComponentData<Game.Net.Node>(nodeCExisting);
        Check(math.distancesq(cAfter.m_Position, new float3(200f, 0f, 0f)) < 1e-4f,
            "scenario 39: pre-existing intersection NOT moved");
        Check(creations.Count == 1 && creations[0].m_Prefab == roadPrefab,
            "scenario 39: original edgeOld emitted for fallback rebuild");
    }

    private static void ScenarioGlobalUndoNodeReplaceRemapAndRewireGuard()
    {
        var em = new EntityManager();
        var globalUndo = new GlobalUndoManager();
        var delUndo = new DeletionUndoManager();
        var terrainSys = new Game.Simulation.TerrainSystem();
        var toolSys = new Game.Tools.ToolSystem { activeTool = new Game.Tools.ToolBaseSystem { toolID = "Net Tool" } };
        CreateGlobalUndoQueries(em, out var tempQ, out var zoneQ, out var brushQ, out var createdQ);

        Entity roadPrefab = em.Make(5000);
        em.AddComponentData(roadPrefab, new Game.Prefabs.NetData());

        Entity nodeA = MakeNetNodeWithPrefab(em, 10, roadPrefab, new float3(0f, 0f, 0f));
        Entity nodeB = MakeNetNodeWithPrefab(em, 11, roadPrefab, new float3(100f, 0f, 0f));
        Entity edgeAdj = MakeRoadEdge3D(em, 20, roadPrefab, nodeA, nodeB, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        Link(em, nodeA, edgeAdj);
        Link(em, nodeB, edgeAdj);

        // PreApply: nodeB is Replaced by tempNodeBPrime (slightly shifted); edgeAdj stays same entity with m_End updated to tempNodeBPrime,
        // while a new branch (tempNodeBPrime -> tempNodeD) is created.
        Entity tempNodeBPrime = MakeNetNodeWithPrefab(em, 201, roadPrefab, new float3(100f, 0f, 1f));
        em.AddComponentData(tempNodeBPrime, new Game.Tools.Temp { m_Original = nodeB, m_Flags = Game.Tools.TempFlags.Replace });

        Entity tempNodeD = MakeNetNodeWithPrefab(em, 202, roadPrefab, new float3(100f, 0f, 80f));
        em.AddComponentData(tempNodeD, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempBranch = MakeRoadEdge3D(em, 203, roadPrefab, tempNodeBPrime, tempNodeD, new float3(100f, 0f, 1f), new float3(100f, 0f, 80f));
        em.AddComponentData(tempBranch, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });

        Entity tempModAdj = MakeRoadEdge3D(em, 204, roadPrefab, nodeA, tempNodeBPrime, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        em.AddComponentData(tempModAdj, new Game.Tools.Temp { m_Original = edgeAdj, m_Flags = Game.Tools.TempFlags.Modify });

        UnityEngine.Time.frameCount = 1100;
        globalUndo.OnPreApply(em, toolSys, terrainSys, delUndo, tempQ, zoneQ, brushQ);

        // Simulate ApplyNetSystem: nodeB deleted, tempNodeBPrime promoted, edgeAdj updated in-place to end at tempNodeBPrime (same curve)
        em.AddComponentData(nodeB, new Game.Common.Deleted());
        em.DestroyEntity(tempModAdj);
        em.Set(edgeAdj, new Game.Net.Edge { m_Start = nodeA, m_End = tempNodeBPrime });
        foreach (Entity e in new[] { tempNodeBPrime, tempNodeD, tempBranch })
        {
            em.RemoveComponent<Game.Tools.Temp>(e);
            em.AddComponentData(e, new Game.Common.Created());
            em.AddComponentData(e, new Game.Common.Applied());
        }
        em.Buffer(nodeA, new Game.Net.ConnectedEdge { m_Edge = edgeAdj });
        em.Buffer(tempNodeBPrime,
            new Game.Net.ConnectedEdge { m_Edge = edgeAdj },
            new Game.Net.ConnectedEdge { m_Edge = tempBranch });
        em.Buffer(tempNodeD, new Game.Net.ConnectedEdge { m_Edge = tempBranch });

        globalUndo.OnPostApply(em, createdQ);
        globalUndo.FlushOpenStroke();

        GlobalUndoTransaction tx = globalUndo.PeekTopTransaction();
        Check(tx != null && tx.Kind == GlobalUndoActionKind.Build, "scenario 40: classified as Build");
        Check(tx.InPlaceNetPatches.ContainsKey(tempNodeBPrime), "scenario 40: replaced nodeBPrime has InPlaceNetPatch");
        Check(!tx.InPlaceNetPatches.ContainsKey(edgeAdj),
            "scenario 40: unchanged edgeAdj does NOT emit spurious patch with dead nodeB after replacedNodeMap remap");

        // Also verify replay refuses unrelated Edge rewiring on a Combine patch when live edge was externally rewired
        var em2 = new EntityManager();
        var gu2 = new GlobalUndoManager();
        var du2 = new DeletionUndoManager();
        CreateGlobalUndoQueries(em2, out var tq2, out var zq2, out var bq2, out var cq2);
        Entity p2 = em2.Make(5000);
        em2.AddComponentData(p2, new Game.Prefabs.NetData());
        Entity a2 = MakeNetNodeWithPrefab(em2, 10, p2, new float3(0f, 0f, 0f));
        Entity b2 = MakeNetNodeWithPrefab(em2, 11, p2, new float3(100f, 0f, 0f));
        Entity old2 = MakeRoadEdge3D(em2, 20, p2, a2, b2, new float3(0f, 0f, 0f), new float3(100f, 0f, 0f));
        Link(em2, a2, old2);
        Link(em2, b2, old2);

        Entity c2 = MakeNetNodeWithPrefab(em2, 201, p2, new float3(200f, 0f, 0f));
        em2.AddComponentData(c2, new Game.Tools.Temp { m_Original = Entity.Null, m_Flags = Game.Tools.TempFlags.Create });
        Entity comb2 = MakeRoadEdge3D(em2, 200, p2, a2, c2, new float3(0f, 0f, 0f), new float3(200f, 0f, 0f));
        em2.AddComponentData(comb2, new Game.Tools.Temp { m_Original = old2, m_Flags = Game.Tools.TempFlags.Replace | Game.Tools.TempFlags.Combine });
        Entity delB2 = MakeNetNodeWithPrefab(em2, 202, p2, new float3(100f, 0f, 0f));
        em2.AddComponentData(delB2, new Game.Tools.Temp { m_Original = b2, m_Flags = Game.Tools.TempFlags.Delete });

        UnityEngine.Time.frameCount = 1200;
        gu2.OnPreApply(em2, toolSys, terrainSys, du2, tq2, zq2, bq2);

        em2.AddComponentData(old2, new Game.Common.Deleted());
        em2.AddComponentData(b2, new Game.Common.Deleted());
        em2.DestroyEntity(delB2);
        em2.RemoveComponent<Game.Tools.Temp>(comb2);
        em2.AddComponentData(comb2, new Game.Common.Created());
        em2.AddComponentData(comb2, new Game.Common.Applied());
        em2.RemoveComponent<Game.Tools.Temp>(c2);
        em2.AddComponentData(c2, new Game.Common.Created());
        em2.AddComponentData(c2, new Game.Common.Applied());
        em2.Buffer(a2, new Game.Net.ConnectedEdge { m_Edge = comb2 });
        em2.Buffer(c2, new Game.Net.ConnectedEdge { m_Edge = comb2 });

        gu2.OnPostApply(em2, cq2);
        gu2.FlushOpenStroke();

        // Externally rewire comb2's start from a2 to an unrelated node x2 before Undo
        Entity x2 = MakeNetNodeWithPrefab(em2, 300, p2, new float3(50f, 0f, 50f));
        em2.Set(comb2, new Game.Net.Edge { m_Start = x2, m_End = c2 });

        Check(gu2.StartOrResumeUndo(UndoAssetMask.All, du2), "scenario 40: rewire-guard undo starts");
        ReplayGlobalUndoToCompletion(em2, gu2, du2, terrainSys);

        Game.Net.Edge liveAfter = em2.GetComponentData<Game.Net.Edge>(comb2);
        Check(liveAfter.m_Start == x2 && liveAfter.m_End == c2,
            "scenario 40: replay refuses to rewire comb2 back to unrelated node a2");
        Check(math.distancesq(em2.GetComponentData<Game.Net.Node>(c2).m_Position, new float3(200f, 0f, 0f)) < 1e-4f,
            "scenario 40: node c2 NOT moved when comb2 endpoints changed externally");
    }
}
