using System;
using System.Collections.Generic;

namespace MarqueeDelete.Tests
{
    internal static class Program
    {
        private static int s_Passed;
        private static int s_Failed;

        private static void AssertTrue(bool condition, string name)
        {
            if (condition)
            {
                s_Passed++;
            }
            else
            {
                s_Failed++;
                Console.Error.WriteLine("[FAIL] " + name);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string name)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
            {
                s_Passed++;
            }
            else
            {
                s_Failed++;
                Console.Error.WriteLine($"[FAIL] {name}: expected={expected}, actual={actual}");
            }
        }

        private static int Main()
        {
            TestAxisAlignedQuad();
            TestCameraAlignedQuad45Deg();
            TestQuadPreviewMovement();
            TestDegenerateAndInvalidQuads();
            TestSegmentAndTriangleIntersections();
            TestClassificationAndBug1Regression();
            TestMovingOwnerAllowedButStaticOwnerRejected();
            TestSafeVsUnsafeAreaAndSafetyProtections();
            TestElevationLayerIsolation();
            TestConfirmationPolicy();
            TestMarqueeQuadCells();
            TestBuildingZoneIndependentFiltering();
            TestImportantBuildingBreakdownAndConfirmation();
            TestOrientedRectIntersection();
            TestUndoTreeAgeRoundTrip();
            TestLocalizationSymmetry();
            TestUndoBatchPolicy();
            TestSelectiveUndoClassifierAndArithmetic();
            TestSelectiveUndoZeroMatchDoesNotPopOrClearFinished();
            TestSelectiveUndoPauseResumePreservesMask();
            TestSelectiveUndoBuildingChildrenAndCategoryCaptureFailures();
            TestSelectiveUndoCompletionSwitchAndSameTotalCategoryDiff();
            TestSelectiveUndoStopFromInProgressAndPaused();
            TestPointerDragThresholdPromotion();
            TestEscapeResolutionAndEditRetention();
            TestSingleClickHiddenRetainedClearedByEscape();
            TestColorLibraryParsingAndTripleFallback();
            TestAssetOverlayBudgetDecimationAndPerf();
            TestInvalidGeometryFallbackHighlights();
            TestFastDragReleaseRightClickUiGatingAndUndoScope();
            TestDragAbortOnFocusLossAndGuards();
            TestMarqueeShapePolygonsAndFreehand();
            TestBatch5aModeSwitchAndPolygonStateMachine();
            TestBatch5bFreehandScanlineAndAllModeTransitions();
            TestHotkeyBindingsLocalizationAndUiIcons();
            TestBatch6aPresetsNetSubfiltersAndMatchSimilar();
            TestBatch7PureLogicHistoryImpactSoloAndGates();

            Console.WriteLine($"Unit tests finished: passed={s_Passed}, failed={s_Failed}");
            return s_Failed == 0 ? 0 : 1;
        }

        private static void TestUndoBatchPolicy()
        {
            var p = new UndoBatchPolicy();
            AssertEqual(4, p.SimpleLimit, "UndoBatch.InitialSimple");
            AssertEqual(1, p.BuildingLimit, "UndoBatch.InitialBuilding");
            for (int i = 0; i < 80; i++) { p.ObserveFrame(0.016); p.FinishBatch(0.1, true, false); }
            AssertEqual(16, p.SimpleLimit, "UndoBatch.SimpleCap");
            AssertEqual(4, p.NetworkLimit, "UndoBatch.NetworkCap");
            p.ObserveFrame(0.08);
            AssertEqual(2, p.Scale, "UndoBatch.SlowFrameBackoff");
            p.FinishBatch(0.1, true, false);
            AssertEqual(2, p.FrameGap, "UndoBatch.SlowFrameGap");
            p.FinishBatch(3, true, false);
            AssertEqual(1, p.Scale, "UndoBatch.CpuBudgetBackoff");
            for (int i = 0; i < 20; i++) p.FinishBatch(0.1, true, true);
            AssertEqual(1, p.Scale, "UndoBatch.WaitDoesNotAccelerate");
            p.ObserveFrame(double.NaN);
            p.ObserveFrame(double.PositiveInfinity);
            AssertEqual(1, p.Scale, "UndoBatch.InvalidSampleIgnored");
            for (int i = 0; i < 8; i++) { p.ObserveFrame(0.016); p.FinishBatch(0.1, true, false); }
            AssertEqual(2, p.Scale, "UndoBatch.RecoversAfterBackoff");
            AssertEqual(1, p.FrameGap, "UndoBatch.RecoversFrameGap");
            for (int i = 0; i < 16; i++) p.FinishBatch(0.1, true, false);
            p.ObserveFrame(0.08);
            p.ObserveFrame(0.08);
            AssertEqual(2, p.Scale, "UndoBatch.OncePerIntervalBackoff");
            p.FinishBatch(0.1, false, false);
            for (int i = 0; i < 8; i++) p.FinishBatch(0.1, false, false);
            AssertEqual(1, p.FrameGap, "UndoBatch.UnsaturatedGapRecovery");
            AssertEqual(2, p.Scale, "UndoBatch.UnsaturatedDoesNotIncreaseScale");
        }

        private static void TestAxisAlignedQuad()
        {
            MarqueeShapeXZ q = MarqueeShapeXZ.FromPoints(
                new CoordXZ(10f, 20f),
                new CoordXZ(30f, 60f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);

            AssertTrue(q.IsUsable(), "AxisQuad.IsUsable");
            AssertTrue(Math.Abs(q.Width - 20f) < 1e-3f, "AxisQuad.Width");
            AssertTrue(Math.Abs(q.Depth - 40f) < 1e-3f, "AxisQuad.Depth");
            AssertTrue(q.ContainsPoint(new CoordXZ(20f, 40f)), "AxisQuad.CenterInside");
            AssertTrue(q.ContainsPoint(new CoordXZ(10f, 20f)), "AxisQuad.CornerInside");
            AssertTrue(!q.ContainsPoint(new CoordXZ(9.9f, 40f)), "AxisQuad.LeftOutside");
            AssertTrue(!q.ContainsPoint(new CoordXZ(20f, 60.1f)), "AxisQuad.TopOutside");

            // Reverse drag direction
            MarqueeShapeXZ rev = MarqueeShapeXZ.FromPoints(
                new CoordXZ(30f, 60f),
                new CoordXZ(10f, 20f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);
            AssertTrue(rev.IsUsable(), "ReverseDrag.IsUsable");
            AssertTrue(rev.ContainsPoint(new CoordXZ(20f, 40f)), "ReverseDrag.CenterInside");
        }

        private static void TestCameraAlignedQuad45Deg()
        {
            // Camera looking NE (1, 1) -> normalized (0.707, 0.707)
            // Drag purely along camera right axis (u=20) and forward axis (v=10)
            float invSqrt2 = (float)(1.0 / Math.Sqrt(2.0));
            CoordXZ fwd = new CoordXZ(1f, 1f);
            CoordXZ a = new CoordXZ(0f, 0f);
            // Displacement along forward*(10) + right*(20), where right=(invSqrt2, -invSqrt2)
            CoordXZ b = new CoordXZ(
                invSqrt2 * 10f + invSqrt2 * 20f,
                invSqrt2 * 10f - invSqrt2 * 20f);

            MarqueeShapeXZ q = MarqueeShapeXZ.FromPoints(a, b, fwd, cameraAligned: true);
            AssertTrue(q.IsUsable(), "Cam45.IsUsable");
            AssertTrue(Math.Abs(q.Width - 20f) < 1e-2f, "Cam45.Width");
            AssertTrue(Math.Abs(q.Depth - 10f) < 1e-2f, "Cam45.Depth");

            // Center must be inside rotated box
            CoordXZ mid = new CoordXZ(b.X * 0.5f, b.Z * 0.5f);
            AssertTrue(q.ContainsPoint(mid), "Cam45.CenterInside");

            // Loose AABB corner that lies OUTSIDE the rotated rectangle must be rejected
            CoordXZ looseCorner = new CoordXZ(q.MinX + 0.05f, q.MaxZ - 0.05f);
            AssertTrue(!q.ContainsPoint(looseCorner), "Cam45.LooseAabbCornerRejected");
        }

        private static void TestQuadPreviewMovement()
        {
            var start = new CoordXZ(0f, 0f);
            var forward = new CoordXZ(0f, 1f);
            var end = new CoordXZ(20f, 20f);
            var quad = MarqueeShapeXZ.FromPoints(start, end, forward, cameraAligned: true);
            var slightMove = MarqueeShapeXZ.FromPoints(start, new CoordXZ(20.2f, 20f), forward, cameraAligned: true);
            var largerMove = MarqueeShapeXZ.FromPoints(start, new CoordXZ(20.7f, 20f), forward, cameraAligned: true);
            var rotated = MarqueeShapeXZ.FromPoints(start, end, new CoordXZ(0.3f, 1f), cameraAligned: true);

            AssertTrue(!quad.HasMovedFrom(in quad, 0.5f), "Preview does not scan a stationary box");
            AssertTrue(!slightMove.HasMovedFrom(in quad, 0.5f), "Preview tolerates minor pointer jitter");
            AssertTrue(largerMove.HasMovedFrom(in quad, 0.5f), "Preview refreshes after cumulative movement");
            AssertTrue(rotated.HasMovedFrom(in quad, 0.5f), "Preview refreshes when camera rotates the box");
        }

        private static void TestDegenerateAndInvalidQuads()
        {
            MarqueeShapeXZ zero = MarqueeShapeXZ.FromPoints(
                new CoordXZ(100f, 100f),
                new CoordXZ(100f, 100f),
                new CoordXZ(0f, 1f),
                cameraAligned: true);
            AssertTrue(!zero.IsUsable(), "ZeroQuad.NotUsable");
            AssertTrue(!zero.ContainsPoint(new CoordXZ(100f, 100f)), "ZeroQuad.NoContains");

            MarqueeShapeXZ tinyClick = MarqueeShapeXZ.FromPoints(
                new CoordXZ(0f, 0f),
                new CoordXZ(0.15f, 0.15f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);
            AssertTrue(!tinyClick.IsUsable(), "TinyClick.NotUsable");

            MarqueeShapeXZ nanQuad = MarqueeShapeXZ.FromPoints(
                new CoordXZ(float.NaN, 0f),
                new CoordXZ(10f, 10f),
                new CoordXZ(0f, 1f),
                cameraAligned: true);
            AssertTrue(!nanQuad.IsFinite(), "NanQuad.NotFinite");
            AssertTrue(!nanQuad.IsUsable(), "NanQuad.NotUsable");
            AssertTrue(!nanQuad.ContainsPoint(new CoordXZ(5f, 5f)), "NanQuad.NoContains");
        }

        private static void TestSegmentAndTriangleIntersections()
        {
            MarqueeShapeXZ q = MarqueeShapeXZ.FromPoints(
                new CoordXZ(0f, 0f),
                new CoordXZ(20f, 20f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);

            // Road crossing straight through center with both endpoints outside
            AssertTrue(
                q.IntersectsSegment(new CoordXZ(-50f, 10f), new CoordXZ(50f, 10f)),
                "Segment.CrossesMiddle");

            // Distant parallel road
            AssertTrue(
                !q.IntersectsSegment(new CoordXZ(-50f, 30f), new CoordXZ(50f, 30f)),
                "Segment.CompletelyOutside");

            // Large surface triangle completely covering marquee box
            AssertTrue(
                q.IntersectsTriangle(new CoordXZ(-100f, -100f), new CoordXZ(200f, -100f), new CoordXZ(10f, 200f)),
                "Triangle.EnclosesMarquee");

            // Small triangle inside
            AssertTrue(
                q.IntersectsTriangle(new CoordXZ(5f, 5f), new CoordXZ(8f, 5f), new CoordXZ(6f, 8f)),
                "Triangle.InsideMarquee");

            // Triangle outside
            AssertTrue(
                !q.IntersectsTriangle(new CoordXZ(30f, 30f), new CoordXZ(40f, 30f), new CoordXZ(35f, 40f)),
                "Triangle.OutsideMarquee");
        }

        private static void TestClassificationAndBug1Regression()
        {
            SafetyOptions safety = SafetyOptions.Default;

            // BUG 1 check: Parent building (HasOwner == false; regardless of SubObject buffers in ECS) MUST be Accepted!
            EntityFlagsSnapshot parentBuilding = new EntityFlagsSnapshot
            {
                HasOwner = false,
                IsBuilding = true,
                IsObjectGeometry = true,
                HasObjectSurface = true,
            };
            FilterSkipReason r1 = SelectionClassifier.Evaluate(
                in parentBuilding,
                SelectionFilterMask.DefaultStatic,
                in safety,
                out SelectionCategory cat1);
            AssertEqual(FilterSkipReason.Accepted, r1, "ParentBuilding.Accepted");
            AssertEqual(SelectionCategory.Building, cat1, "ParentBuilding.Category");

            // Owned sub-object prop inside building MUST be skipped (Parent handles cascade)
            EntityFlagsSnapshot childProp = new EntityFlagsSnapshot
            {
                HasOwner = true,
                IsObjectGeometry = true,
                HasObjectSurface = true,
            };
            FilterSkipReason r2 = SelectionClassifier.Evaluate(
                in childProp,
                SelectionFilterMask.DefaultStatic,
                in safety,
                out _);
            AssertEqual(FilterSkipReason.HasOwnerChild, r2, "ChildProp.SkippedAsOwnerChild");

            // Tree vs Pillar
            EntityFlagsSnapshot tree = new EntityFlagsSnapshot { IsPlant = true, IsObjectGeometry = true };
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in tree, SelectionFilterMask.DefaultStatic, in safety, out SelectionCategory cTree),
                "Tree.Accepted");
            AssertEqual(SelectionCategory.Tree, cTree, "Tree.Category");

            EntityFlagsSnapshot pillarPlant = new EntityFlagsSnapshot { IsPlant = true, IsPillar = true, IsObjectGeometry = true };
            SelectionClassifier.Evaluate(in pillarPlant, SelectionFilterMask.DefaultStatic, in safety, out SelectionCategory cPillar);
            AssertEqual(SelectionCategory.Prop, cPillar, "PillarNotClassifiedAsTree");

            // Decal vs Prop
            EntityFlagsSnapshot decal = new EntityFlagsSnapshot { IsObjectGeometry = true, HasObjectSurface = false };
            SelectionClassifier.Evaluate(in decal, SelectionFilterMask.DefaultStatic, in safety, out SelectionCategory cDecal);
            AssertEqual(SelectionCategory.Decal, cDecal, "Decal.Category");
        }

        private static void TestMovingOwnerAllowedButStaticOwnerRejected()
        {
            SafetyOptions safety = SafetyOptions.Default;

            EntityFlagsSnapshot depotVehicle = new EntityFlagsSnapshot
            {
                HasOwner = true,
                IsMoving = true,
            };

            // Off by default in DefaultStatic
            AssertEqual(
                FilterSkipReason.FilteredOutByCategory,
                SelectionClassifier.Evaluate(in depotVehicle, SelectionFilterMask.DefaultStatic, in safety, out SelectionCategory catOff),
                "Vehicle.FilteredWhenOff");
            AssertEqual(SelectionCategory.Moving, catOff, "Vehicle.Category");

            // Included when Moving enabled even though vehicle has Owner pointing to depot
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in depotVehicle, SelectionFilterMask.DefaultStatic | SelectionFilterMask.Moving, in safety, out SelectionCategory catOn),
                "OwnedVehicle.AcceptedWhenMovingOn");
            AssertEqual(SelectionCategory.Moving, catOn, "OwnedVehicle.CategoryOn");
        }

        private static void TestSafeVsUnsafeAreaAndSafetyProtections()
        {
            SafetyOptions safety = SafetyOptions.Default;

            // MapTile and District MUST NEVER be deleted even if All mask is set
            EntityFlagsSnapshot mapTile = new EntityFlagsSnapshot { IsMapTile = true, IsSafeArea = false };
            AssertEqual(
                FilterSkipReason.UnsafeAreaType,
                SelectionClassifier.Evaluate(in mapTile, SelectionFilterMask.All, in safety, out _),
                "MapTile.AlwaysProtected");

            EntityFlagsSnapshot district = new EntityFlagsSnapshot { IsDistrict = true };
            AssertEqual(
                FilterSkipReason.UnsafeAreaType,
                SelectionClassifier.Evaluate(in district, SelectionFilterMask.All, in safety, out _),
                "District.AlwaysProtected");

            // Custom painted Surface/Space/Lot CAN be deleted when Areas enabled
            EntityFlagsSnapshot paintedSurface = new EntityFlagsSnapshot { IsSafeArea = true };
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in paintedSurface, SelectionFilterMask.Areas, in safety, out SelectionCategory areaCat),
                "SafeArea.AcceptedWhenEnabled");
            AssertEqual(SelectionCategory.Area, areaCat, "SafeArea.Category");

            // Outside connection protection
            EntityFlagsSnapshot outsideHighway = new EntityFlagsSnapshot { IsNetwork = true, IsOutsideConnection = true };
            AssertEqual(
                FilterSkipReason.ProtectedOutsideConnection,
                SelectionClassifier.Evaluate(in outsideHighway, SelectionFilterMask.All, in safety, out _),
                "OutsideConnection.Protected");

            SafetyOptions noProtect = new SafetyOptions { ProtectOutsideConnections = false, ProtectUniqueBuildings = true };
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in outsideHighway, SelectionFilterMask.All, in noProtect, out _),
                "OutsideConnection.AllowedWhenOptOff");

            // Unique building protection
            EntityFlagsSnapshot landmark = new EntityFlagsSnapshot { IsBuilding = true, IsUniqueObject = true };
            AssertEqual(
                FilterSkipReason.ProtectedUnique,
                SelectionClassifier.Evaluate(in landmark, SelectionFilterMask.All, in noProtect, out _),
                "UniqueBuilding.Protected");

            // Deleted / Hidden / Marker / Placeholder / Terrain
            EntityFlagsSnapshot deletedBuilding = new EntityFlagsSnapshot { IsBuilding = true, IsDeleted = true };
            AssertEqual(
                FilterSkipReason.InvalidOrInactive,
                SelectionClassifier.Evaluate(in deletedBuilding, SelectionFilterMask.All, in safety, out _),
                "Deleted.Skipped");

            EntityFlagsSnapshot hiddenProp = new EntityFlagsSnapshot { IsObjectGeometry = true, IsHidden = true };
            AssertEqual(
                FilterSkipReason.InvalidOrInactive,
                SelectionClassifier.Evaluate(in hiddenProp, SelectionFilterMask.All, in safety, out _),
                "Hidden.Skipped");
        }

        private static void TestElevationLayerIsolation()
        {
            SafetyOptions safety = SafetyOptions.Default;

            EntityFlagsSnapshot surfaceRoad = new EntityFlagsSnapshot
            {
                IsNetwork = true,
                IsUnderground = false,
            };
            EntityFlagsSnapshot undergroundTunnelOrPipe = new EntityFlagsSnapshot
            {
                IsNetwork = true,
                IsUnderground = true,
            };
            EntityFlagsSnapshot undergroundVehicle = new EntityFlagsSnapshot
            {
                HasOwner = true,
                IsMoving = true,
                IsUnderground = true,
            };

            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in surfaceRoad, SelectionFilterMask.All, in safety, undergroundView: false, out SelectionCategory cNet),
                "SurfaceRoad.AcceptedInGroundView");
            AssertEqual(SelectionCategory.Network, cNet, "SurfaceRoad.Category");

            AssertEqual(
                FilterSkipReason.WrongElevationLayer,
                SelectionClassifier.Evaluate(in surfaceRoad, SelectionFilterMask.All, in safety, undergroundView: true, out _),
                "SurfaceRoad.SkippedInUndergroundView");

            AssertEqual(
                FilterSkipReason.WrongElevationLayer,
                SelectionClassifier.Evaluate(in undergroundTunnelOrPipe, SelectionFilterMask.All, in safety, undergroundView: false, out _),
                "UndergroundNet.SkippedInGroundView");

            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in undergroundTunnelOrPipe, SelectionFilterMask.All, in safety, undergroundView: true, out _),
                "UndergroundNet.AcceptedInUndergroundView");

            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in undergroundVehicle, SelectionFilterMask.All, in safety, undergroundView: true, out SelectionCategory cMov),
                "UndergroundVehicle.AcceptedInUndergroundView");
            AssertEqual(SelectionCategory.Moving, cMov, "UndergroundVehicle.Category");
        }

        private static void TestConfirmationPolicy()
        {
            AssertTrue(!ConfirmationPolicy.ShouldAutoDelete(0, confirmBeforeDelete: false, 50), "AutoDelete.EmptyFalse");
            AssertTrue(!ConfirmationPolicy.ShouldAutoDelete(5, confirmBeforeDelete: true, 50), "AutoDelete.ConfirmModeFalse");
            AssertTrue(ConfirmationPolicy.ShouldAutoDelete(5, confirmBeforeDelete: false, 50), "AutoDelete.SmallImmediateTrue");
            AssertTrue(ConfirmationPolicy.ShouldAutoDelete(50, confirmBeforeDelete: false, 50), "AutoDelete.ExactThresholdTrue");
            // When confirmBeforeDelete is false, large selections auto-delete without being blocked by threshold
            AssertTrue(ConfirmationPolicy.ShouldAutoDelete(51, confirmBeforeDelete: false, 50), "AutoDelete.OverThresholdAllowedWhenConfirmOff");
            AssertTrue(ConfirmationPolicy.NeedsLargeSelectionWarning(51, 50), "Warning.OverThresholdTrue");
            AssertTrue(!ConfirmationPolicy.NeedsLargeSelectionWarning(50, 50), "Warning.AtThresholdFalse");

            // Even if cascade count is high, when confirmBeforeDelete is false and no important buildings, auto-delete succeeds
            AssertTrue(ConfirmationPolicy.ShouldAutoDelete(
                selectedCount: System.Math.Max(10, 60),
                importantBuildingCount: 0,
                confirmBeforeDelete: false,
                largeSelectionThreshold: 50), "AutoDelete.CascadeAllowedWhenConfirmOff");
        }

        private static void TestLocalizationSymmetry()
        {
            string baseDir = AppContext.BaseDirectory;
            string locPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "Localization.cs"));
            AssertTrue(System.IO.File.Exists(locPath), "Localization.FileExists");
            string src = System.IO.File.ReadAllText(locPath);

            int zhStart = src.IndexOf("public sealed class LocaleZH", StringComparison.Ordinal);
            AssertTrue(zhStart > 0, "Localization.LocaleZHFound");

            string enPart = src.Substring(0, zhStart);
            string zhPart = src.Substring(zhStart);

            var enMatches = System.Text.RegularExpressions.Regex.Matches(enPart, @"\{\s*([^,]+),\s*""[^""]*""\s*\}");
            var zhMatches = System.Text.RegularExpressions.Regex.Matches(zhPart, @"\{\s*([^,]+),\s*""[^""]*""\s*\}");

            var enKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in enMatches)
            {
                enKeys.Add(System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim());
            }

            var zhKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in zhMatches)
            {
                zhKeys.Add(System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim());
            }

            AssertTrue(enKeys.Count >= 40, "Localization.MinimumEntryCount");
            AssertEqual(enKeys.Count, zhKeys.Count, "Localization.EntryCountParity");

            foreach (string key in enKeys)
            {
                AssertTrue(zhKeys.Contains(key), "Localization.ZHContains(" + key + ")");
            }
        }

        private static void TestMarqueeQuadCells()
        {
            // 24m x 16m at 8m/cell => 3 x 2 cells = 6 cells
            MarqueeShapeXZ q24x16 = MarqueeShapeXZ.FromPoints(
                new CoordXZ(0f, 0f),
                new CoordXZ(24f, 16f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);

            AssertEqual(3, q24x16.WidthCells, "QuadCells.WidthCells_24m");
            AssertEqual(2, q24x16.DepthCells, "QuadCells.DepthCells_16m");
            AssertEqual(6, q24x16.AreaCells, "QuadCells.AreaCells_24x16");

            // 8m x 8m => 1 x 1 cell = 1 cell
            MarqueeShapeXZ q8x8 = MarqueeShapeXZ.FromPoints(
                new CoordXZ(10f, 10f),
                new CoordXZ(18f, 18f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);

            AssertEqual(1, q8x8.WidthCells, "QuadCells.WidthCells_8m");
            AssertEqual(1, q8x8.DepthCells, "QuadCells.DepthCells_8m");
            AssertEqual(1, q8x8.AreaCells, "QuadCells.AreaCells_8x8");

            // 40m x 32m => 5 x 4 cells = 20 cells
            MarqueeShapeXZ q40x32 = MarqueeShapeXZ.FromPoints(
                new CoordXZ(0f, 0f),
                new CoordXZ(40f, 32f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);

            AssertEqual(5, q40x32.WidthCells, "QuadCells.WidthCells_40m");
            AssertEqual(4, q40x32.DepthCells, "QuadCells.DepthCells_32m");
            AssertEqual(20, q40x32.AreaCells, "QuadCells.AreaCells_40x32");

            // Zero span quad => 0 cells
            MarqueeShapeXZ zero = MarqueeShapeXZ.FromPoints(
                new CoordXZ(5f, 5f),
                new CoordXZ(5f, 5f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);

            AssertEqual(0, zero.WidthCells, "QuadCells.ZeroWidth");
            AssertEqual(0, zero.DepthCells, "QuadCells.ZeroDepth");
            AssertEqual(0, zero.AreaCells, "QuadCells.ZeroArea");

            // Small span (> 0.01m but < 4m) clamps to minimum 1 cell
            MarqueeShapeXZ tiny = MarqueeShapeXZ.FromPoints(
                new CoordXZ(0f, 0f),
                new CoordXZ(2f, 3f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);

            AssertEqual(1, tiny.WidthCells, "QuadCells.TinyWidthClampedToOne");
            AssertEqual(1, tiny.DepthCells, "QuadCells.TinyDepthClampedToOne");
            AssertEqual(1, tiny.AreaCells, "QuadCells.TinyAreaClampedToOne");
        }

        private static void TestBuildingZoneIndependentFiltering()
        {
            SafetyOptions safety = SafetyOptions.Default;

            EntityFlagsSnapshot resBuilding = new EntityFlagsSnapshot { IsBuilding = true, ZoneType = BuildingZoneType.Residential };
            EntityFlagsSnapshot comBuilding = new EntityFlagsSnapshot { IsBuilding = true, ZoneType = BuildingZoneType.Commercial };
            EntityFlagsSnapshot indBuilding = new EntityFlagsSnapshot { IsBuilding = true, ZoneType = BuildingZoneType.Industrial };
            EntityFlagsSnapshot offBuilding = new EntityFlagsSnapshot { IsBuilding = true, ZoneType = BuildingZoneType.Office };
            EntityFlagsSnapshot othBuilding = new EntityFlagsSnapshot { IsBuilding = true, ZoneType = BuildingZoneType.Other };
            EntityFlagsSnapshot noneBuilding = new EntityFlagsSnapshot { IsBuilding = true, ZoneType = BuildingZoneType.None };

            // 1. DefaultStatic includes all zones and Buildings
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in resBuilding, SelectionFilterMask.DefaultStatic, in safety, out _),
                "ZoneFilter.DefaultStatic.ResAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in comBuilding, SelectionFilterMask.DefaultStatic, in safety, out _),
                "ZoneFilter.DefaultStatic.ComAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in indBuilding, SelectionFilterMask.DefaultStatic, in safety, out _),
                "ZoneFilter.DefaultStatic.IndAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in offBuilding, SelectionFilterMask.DefaultStatic, in safety, out _),
                "ZoneFilter.DefaultStatic.OffAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in othBuilding, SelectionFilterMask.DefaultStatic, in safety, out _),
                "ZoneFilter.DefaultStatic.OthAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in noneBuilding, SelectionFilterMask.DefaultStatic, in safety, out _),
                "ZoneFilter.DefaultStatic.NoneAccepted");

            // 2. Uncheck Residential only: Residential rejected, all other zones accepted
            SelectionFilterMask noRes = SelectionFilterMask.DefaultStatic & ~SelectionFilterMask.Residential;
            AssertEqual(
                FilterSkipReason.FilteredOutByCategory,
                SelectionClassifier.Evaluate(in resBuilding, noRes, in safety, out _),
                "ZoneFilter.NoRes.ResRejected");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in comBuilding, noRes, in safety, out _),
                "ZoneFilter.NoRes.ComAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in indBuilding, noRes, in safety, out _),
                "ZoneFilter.NoRes.IndAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in offBuilding, noRes, in safety, out _),
                "ZoneFilter.NoRes.OffAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in othBuilding, noRes, in safety, out _),
                "ZoneFilter.NoRes.OthAccepted");

            // 3. Uncheck Commercial only: Commercial rejected, all others accepted
            SelectionFilterMask noCom = SelectionFilterMask.DefaultStatic & ~SelectionFilterMask.Commercial;
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in resBuilding, noCom, in safety, out _),
                "ZoneFilter.NoCom.ResAccepted");
            AssertEqual(
                FilterSkipReason.FilteredOutByCategory,
                SelectionClassifier.Evaluate(in comBuilding, noCom, in safety, out _),
                "ZoneFilter.NoCom.ComRejected");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in indBuilding, noCom, in safety, out _),
                "ZoneFilter.NoCom.IndAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in offBuilding, noCom, in safety, out _),
                "ZoneFilter.NoCom.OffAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in othBuilding, noCom, in safety, out _),
                "ZoneFilter.NoCom.OthAccepted");

            // 4. Uncheck Industrial only: Industrial rejected, all others accepted
            SelectionFilterMask noInd = SelectionFilterMask.DefaultStatic & ~SelectionFilterMask.Industrial;
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in resBuilding, noInd, in safety, out _),
                "ZoneFilter.NoInd.ResAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in comBuilding, noInd, in safety, out _),
                "ZoneFilter.NoInd.ComAccepted");
            AssertEqual(
                FilterSkipReason.FilteredOutByCategory,
                SelectionClassifier.Evaluate(in indBuilding, noInd, in safety, out _),
                "ZoneFilter.NoInd.IndRejected");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in offBuilding, noInd, in safety, out _),
                "ZoneFilter.NoInd.OffAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in othBuilding, noInd, in safety, out _),
                "ZoneFilter.NoInd.OthAccepted");

            // 5. Uncheck Office only: Office rejected, all others accepted
            SelectionFilterMask noOff = SelectionFilterMask.DefaultStatic & ~SelectionFilterMask.Office;
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in resBuilding, noOff, in safety, out _),
                "ZoneFilter.NoOff.ResAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in comBuilding, noOff, in safety, out _),
                "ZoneFilter.NoOff.ComAccepted");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in indBuilding, noOff, in safety, out _),
                "ZoneFilter.NoOff.IndAccepted");
            AssertEqual(
                FilterSkipReason.FilteredOutByCategory,
                SelectionClassifier.Evaluate(in offBuilding, noOff, in safety, out _),
                "ZoneFilter.NoOff.OffRejected");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in othBuilding, noOff, in safety, out _),
                "ZoneFilter.NoOff.OthAccepted");

            // 6. Uncheck Buildings (master building filter): Other & None rejected
            SelectionFilterMask noBuildings = SelectionFilterMask.DefaultStatic & ~SelectionFilterMask.Buildings;
            AssertEqual(
                FilterSkipReason.FilteredOutByCategory,
                SelectionClassifier.Evaluate(in othBuilding, noBuildings, in safety, out _),
                "ZoneFilter.NoBuildings.OthRejected");
            AssertEqual(
                FilterSkipReason.FilteredOutByCategory,
                SelectionClassifier.Evaluate(in noneBuilding, noBuildings, in safety, out _),
                "ZoneFilter.NoBuildings.NoneRejected");
        }

        private static void TestImportantBuildingBreakdownAndConfirmation()
        {
            // 1. Breakdown counting
            SelectionBreakdown b = default;

            b.Add(SelectionCategory.Building, BuildingZoneType.Residential, ImportantBuildingKind.None);
            b.Add(SelectionCategory.Building, BuildingZoneType.Commercial, ImportantBuildingKind.Service);
            b.Add(SelectionCategory.Building, BuildingZoneType.Industrial, ImportantBuildingKind.Unique);
            b.Add(SelectionCategory.Building, BuildingZoneType.Office, ImportantBuildingKind.Signature);
            b.Add(SelectionCategory.Building, BuildingZoneType.Other, ImportantBuildingKind.Service | ImportantBuildingKind.Signature);
            b.Add(SelectionCategory.Tree, BuildingZoneType.None, ImportantBuildingKind.None);

            AssertEqual(5, b.Buildings, "Breakdown.Buildings");
            AssertEqual(1, b.Residential, "Breakdown.Residential");
            AssertEqual(1, b.Commercial, "Breakdown.Commercial");
            AssertEqual(1, b.Industrial, "Breakdown.Industrial");
            AssertEqual(1, b.Office, "Breakdown.Office");
            AssertEqual(1, b.OtherBuildings, "Breakdown.OtherBuildings");
            AssertEqual(4, b.ImportantBuildings, "Breakdown.ImportantBuildings");
            AssertEqual(2, b.ServiceBuildings, "Breakdown.ServiceBuildings");
            AssertEqual(1, b.UniqueBuildings, "Breakdown.UniqueBuildings");
            AssertEqual(2, b.SignatureBuildings, "Breakdown.SignatureBuildings");
            AssertEqual(1, b.Trees, "Breakdown.Trees");
            AssertEqual(6, b.Total, "Breakdown.Total");

            // 2. ConfirmationPolicy: ShouldAutoDelete returns false whenever importantBuildingCount > 0
            AssertTrue(
                ConfirmationPolicy.ShouldAutoDelete(selectedCount: 5, importantBuildingCount: 0, confirmBeforeDelete: false, largeSelectionThreshold: 50),
                "AutoDelete.NoImportant_AutoDeleteAllowed");

            AssertTrue(
                !ConfirmationPolicy.ShouldAutoDelete(selectedCount: 5, importantBuildingCount: 1, confirmBeforeDelete: false, largeSelectionThreshold: 50),
                "AutoDelete.WithOneImportant_Blocked");

            AssertTrue(
                !ConfirmationPolicy.ShouldAutoDelete(selectedCount: 1, importantBuildingCount: 1, confirmBeforeDelete: false, largeSelectionThreshold: 50),
                "AutoDelete.SingleImportant_Blocked");

            AssertTrue(
                !ConfirmationPolicy.ShouldAutoDelete(selectedCount: 10, importantBuildingCount: 3, confirmBeforeDelete: false, largeSelectionThreshold: 50),
                "AutoDelete.MultipleImportant_Blocked");

            // 3. NeedsImportantConfirmation
            AssertTrue(!ConfirmationPolicy.NeedsImportantConfirmation(0), "NeedsImportant.ZeroFalse");
            AssertTrue(ConfirmationPolicy.NeedsImportantConfirmation(1), "NeedsImportant.OneTrue");
            AssertTrue(ConfirmationPolicy.NeedsImportantConfirmation(5), "NeedsImportant.FiveTrue");

            // 4. Unique / Signature building safety protection
            SafetyOptions protectUnique = new SafetyOptions { ProtectUniqueBuildings = true, ProtectOutsideConnections = true };
            SafetyOptions noProtectUnique = new SafetyOptions { ProtectUniqueBuildings = false, ProtectOutsideConnections = true };

            EntityFlagsSnapshot serviceBld = new EntityFlagsSnapshot
            {
                IsBuilding = true,
                ZoneType = BuildingZoneType.Other,
                ImportantKind = ImportantBuildingKind.Service
            };
            EntityFlagsSnapshot uniqueBld = new EntityFlagsSnapshot
            {
                IsBuilding = true,
                ZoneType = BuildingZoneType.Other,
                ImportantKind = ImportantBuildingKind.Unique
            };
            EntityFlagsSnapshot signatureBld = new EntityFlagsSnapshot
            {
                IsBuilding = true,
                ZoneType = BuildingZoneType.Residential,
                ImportantKind = ImportantBuildingKind.Signature
            };

            // Service buildings are not blocked by ProtectUniqueBuildings
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in serviceBld, SelectionFilterMask.DefaultStatic, in protectUnique, out _),
                "Safety.ServiceNotBlockedByProtectUnique");

            // Unique and Signature are blocked when ProtectUniqueBuildings is on
            AssertEqual(
                FilterSkipReason.ProtectedUnique,
                SelectionClassifier.Evaluate(in uniqueBld, SelectionFilterMask.DefaultStatic, in protectUnique, out _),
                "Safety.UniqueBlockedWhenProtected");
            AssertEqual(
                FilterSkipReason.ProtectedUnique,
                SelectionClassifier.Evaluate(in signatureBld, SelectionFilterMask.DefaultStatic, in protectUnique, out _),
                "Safety.SignatureBlockedWhenProtected");

            // Unique and Signature are accepted when ProtectUniqueBuildings is off
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in uniqueBld, SelectionFilterMask.DefaultStatic, in noProtectUnique, out _),
                "Safety.UniqueAllowedWhenNotProtected");
            AssertEqual(
                FilterSkipReason.Accepted,
                SelectionClassifier.Evaluate(in signatureBld, SelectionFilterMask.DefaultStatic, in noProtectUnique, out _),
                "Safety.SignatureAllowedWhenNotProtected");
        }

        private static void TestOrientedRectIntersection()
        {
            MarqueeShapeXZ axisQuad = MarqueeShapeXZ.FromPoints(
                new CoordXZ(0f, 0f),
                new CoordXZ(20f, 20f),
                new CoordXZ(0f, 1f),
                cameraAligned: false);

            // Center outside marquee (25, 10), half-width=8 -> reaches x=17 (inside [0, 20])
            AssertTrue(
                axisQuad.IntersectsOrientedRect(new CoordXZ(25f, 10f), new CoordXZ(8f, 0f), new CoordXZ(0f, 4f)),
                "Obb.CornerOverlapFromOutsideCenter");

            // Center outside (25, 10), half-width=4 -> x in [21, 29], disjoint from [0, 20]
            AssertTrue(
                !axisQuad.IntersectsOrientedRect(new CoordXZ(25f, 10f), new CoordXZ(4f, 0f), new CoordXZ(0f, 4f)),
                "Obb.DisjointAxisAligned");

            // Long strip crossing marquee horizontally without any corner inside marquee
            AssertTrue(
                axisQuad.IntersectsOrientedRect(new CoordXZ(10f, 10f), new CoordXZ(40f, 0f), new CoordXZ(0f, 2f)),
                "Obb.WideStripCrossingWithoutInsideCorners");

            // Rotated 45deg building whose AABB overlaps (0,20) corner but SAT separates cleanly
            float invSqrt2 = (float)(1.0 / Math.Sqrt(2.0));
            CoordXZ u45 = new CoordXZ(invSqrt2 * 10f, invSqrt2 * 10f);
            CoordXZ v45 = new CoordXZ(-invSqrt2 * 2f, invSqrt2 * 2f);
            AssertTrue(
                !axisQuad.IntersectsOrientedRect(new CoordXZ(-5f, 25f), u45, v45),
                "Obb.RotatedDiagonalAabbOverlapButSatSeparated");

            // Camera-aligned 45deg marquee: diamond (0,0)-(10.6,-10.6)-(21.2,0)-(10.6,10.6)
            MarqueeShapeXZ camQuad = MarqueeShapeXZ.FromPoints(
                new CoordXZ(0f, 0f),
                new CoordXZ(invSqrt2 * 30f, 0f),
                new CoordXZ(invSqrt2, invSqrt2),
                cameraAligned: true);
            float bottomVertexX = invSqrt2 * 15f;
            float bottomVertexZ = -invSqrt2 * 15f;
            AssertTrue(
                camQuad.IntersectsOrientedRect(new CoordXZ(bottomVertexX, bottomVertexZ - 2f), new CoordXZ(2f, 0f), new CoordXZ(0f, 3f)),
                "Obb.CameraAlignedEdgeHit");
            AssertTrue(
                !camQuad.IntersectsOrientedRect(new CoordXZ(2f, -9f), new CoordXZ(1f, 0f), new CoordXZ(0f, 1f)),
                "Obb.CameraAlignedDiamondMissInsideAabb");
        }

        private static void TestUndoTreeAgeRoundTrip()
        {
            byte[] states = new byte[]
            {
                0,
                UndoMath.TreeStateTeen,
                UndoMath.TreeStateAdult,
                UndoMath.TreeStateElderly,
                UndoMath.TreeStateDead,
                UndoMath.TreeStateStump,
                UndoMath.TreeStateCollected,
            };
            byte[] growths = new byte[] { 0, 128, 255 };

            foreach (byte stateMask in states)
            {
                foreach (byte growth in growths)
                {
                    float age = UndoMath.TreeToDefinitionAge(stateMask, growth);
                    AssertTrue(age >= 0f && age <= 1f, $"UndoTree.Range.{stateMask}.{growth}");

                    // Replicate Game.Objects.ObjectUtils.InitializeTreeState(float age) thresholds & scaling
                    byte decodedState;
                    byte decodedGrowth;
                    if (age < 0.1f)
                    {
                        decodedState = 0;
                        decodedGrowth = ClampByte((int)Math.Floor(age * 2560f));
                    }
                    else if (age < 0.25f)
                    {
                        decodedState = UndoMath.TreeStateTeen;
                        decodedGrowth = ClampByte((int)Math.Floor((age - 0.1f) * 1706.6666f));
                    }
                    else if (age < 0.6f)
                    {
                        decodedState = UndoMath.TreeStateAdult;
                        decodedGrowth = ClampByte((int)Math.Floor((age - 0.25f) * 731.4286f));
                    }
                    else if (age < 0.95000005f)
                    {
                        decodedState = UndoMath.TreeStateElderly;
                        decodedGrowth = ClampByte((int)Math.Floor((age - 0.6f) * 731.4286f));
                    }
                    else
                    {
                        decodedState = UndoMath.TreeStateDead;
                        decodedGrowth = ClampByte((int)Math.Floor((age - 0.95f) * 5120f));
                    }

                    byte expectedState = (stateMask & (UndoMath.TreeStateDead | UndoMath.TreeStateStump | UndoMath.TreeStateCollected)) != 0
                        ? UndoMath.TreeStateDead
                        : stateMask;
                    AssertEqual(expectedState, decodedState, $"UndoTree.State.{stateMask}.{growth}");
                    AssertTrue(Math.Abs(growth - decodedGrowth) <= 1, $"UndoTree.Growth.{stateMask}.{growth}: g={growth}, d={decodedGrowth}");
                }
            }
        }

        private static byte ClampByte(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }

        private sealed class FakeUndoSnapshot : IUndoSnapshotMeta
        {
            public UndoCategoryCounts Counts { get; set; }
            public UndoCategoryCounts NotCaptured { get; set; }
            public int TotalCount => Counts.Total;
            public bool Truncated { get; set; }
            public bool HasOwnerTimeout { get; set; }
            public int RestoredCount { get; set; }
            public int SkippedCount { get; set; }
        }

        private static void TestSelectiveUndoClassifierAndArithmetic()
        {
            AssertEqual(
                SelectionCategory.Tree,
                SelectionClassifier.ClassifyNonBuildingObject(isPlant: true, isPillar: false, hasObjectSurface: false, hasNetObject: false),
                "SelUndo.Classify.Plant");
            AssertEqual(
                SelectionCategory.Prop,
                SelectionClassifier.ClassifyNonBuildingObject(isPlant: true, isPillar: true, hasObjectSurface: false, hasNetObject: false),
                "SelUndo.Classify.PillarPlantAsProp");
            AssertEqual(
                SelectionCategory.Prop,
                SelectionClassifier.ClassifyNonBuildingObject(isPlant: false, isPillar: false, hasObjectSurface: true, hasNetObject: false),
                "SelUndo.Classify.SurfaceProp");
            AssertEqual(
                SelectionCategory.Prop,
                SelectionClassifier.ClassifyNonBuildingObject(isPlant: false, isPillar: false, hasObjectSurface: false, hasNetObject: true),
                "SelUndo.Classify.NetObjProp");
            AssertEqual(
                SelectionCategory.Decal,
                SelectionClassifier.ClassifyNonBuildingObject(isPlant: false, isPillar: false, hasObjectSurface: false, hasNetObject: false),
                "SelUndo.Classify.Decal");

            AssertEqual(UndoAssetMask.None, SelectiveUndoClassifier.ToMaskBit(SelectionCategory.None), "SelUndo.UnknownNoneRejected");
            AssertEqual(UndoAssetMask.None, SelectiveUndoClassifier.ToMaskBit(SelectionCategory.Moving), "SelUndo.MovingRejected");
            AssertEqual(UndoAssetMask.None, SelectiveUndoClassifier.ToMaskBit((SelectionCategory)99), "SelUndo.OutOfRangeRejected");
            AssertTrue(!SelectiveUndoClassifier.IsIncluded(SelectionCategory.None, UndoAssetMask.All), "SelUndo.NoTrueFallbackNone");
            AssertTrue(!SelectiveUndoClassifier.IsIncluded(SelectionCategory.Moving, UndoAssetMask.All), "SelUndo.NoTrueFallbackMoving");
            AssertTrue(!SelectiveUndoClassifier.IsIncluded((SelectionCategory)99, UndoAssetMask.All), "SelUndo.NoTrueFallbackInvalid");
            AssertTrue(SelectiveUndoClassifier.IsIncluded(SelectionCategory.Tree, UndoAssetMask.Trees), "SelUndo.TreeIncluded");

            var counts = new UndoCategoryCounts { Buildings = 8, Networks = 5, Areas = 4, Trees = 3, Props = 2, Decals = 1 };
            counts.Add((SelectionCategory)99, 10);
            counts.Add(SelectionCategory.Moving, 10);
            AssertEqual(23, counts.Total, "SelUndo.CountsTotalIgnoresUnsupported");
            AssertEqual(13, counts.GetFilteredSum(UndoAssetMask.Buildings | UndoAssetMask.Networks), "SelUndo.FilteredSum");
        }

        private static void TestSelectiveUndoZeroMatchDoesNotPopOrClearFinished()
        {
            var tracker = new SelectiveUndoTracker<FakeUndoSnapshot>(5);
            var s1 = new FakeUndoSnapshot { Counts = new UndoCategoryCounts { Buildings = 4, Trees = 0 } };
            var s2 = new FakeUndoSnapshot { Counts = new UndoCategoryCounts { Buildings = 1, Trees = 2 } };

            AssertTrue(tracker.OnCaptureCompleted(s1), "SelUndo.ZeroMatch.PushS1");
            AssertTrue(tracker.OnCaptureCompleted(s2), "SelUndo.ZeroMatch.PushS2");

            AssertTrue(tracker.TryStartOrResume(UndoAssetMask.Trees, out FakeUndoSnapshot active, out UndoAssetMask mask, out bool isResume), "SelUndo.ZeroMatch.StartS2Trees");
            AssertTrue(!isResume, "SelUndo.ZeroMatch.NotResume");
            AssertEqual(UndoAssetMask.Trees, mask, "SelUndo.ZeroMatch.MaskS2");
            active.RestoredCount = 2;
            tracker.CompleteActive();

            // S1 has 0 Trees; completion banner from S2 must remain intact and S1 must not be popped.
            AssertTrue(!tracker.CanUndo(UndoAssetMask.Trees), "SelUndo.ZeroMatch.CanUndoFalse");
            SelectiveUndoUiSnapshot ui = tracker.GetUiSnapshot(UndoAssetMask.Trees);
            AssertTrue(ui.ZeroMatch, "SelUndo.ZeroMatch.Flag");
            AssertEqual(0, ui.NextCount, "SelUndo.ZeroMatch.NextCount0");
            AssertEqual(1, ui.HistoryDepth, "SelUndo.ZeroMatch.HistoryDepth1");
            AssertEqual("UndoSubmitted", ui.FinishedNotice, "SelUndo.ZeroMatch.FinishedNoticeKept");
            AssertEqual(2, ui.FinishedTotalCount, "SelUndo.ZeroMatch.FinishedTotalSeparate");

            AssertTrue(!tracker.TryStartOrResume(UndoAssetMask.Trees, out _, out _, out _), "SelUndo.ZeroMatch.StartBlocked");
            SelectiveUndoUiSnapshot uiAfter = tracker.GetUiSnapshot(UndoAssetMask.Trees);
            AssertEqual(1, uiAfter.HistoryDepth, "SelUndo.ZeroMatch.StackNotPopped");
            AssertEqual("UndoSubmitted", uiAfter.FinishedNotice, "SelUndo.ZeroMatch.FinishedStillNotCleared");
        }

        private static void TestSelectiveUndoPauseResumePreservesMask()
        {
            var tracker = new SelectiveUndoTracker<FakeUndoSnapshot>(5);
            var s1 = new FakeUndoSnapshot { Counts = new UndoCategoryCounts { Buildings = 2, Trees = 3 } };
            tracker.OnCaptureCompleted(s1);

            AssertTrue(tracker.TryStartOrResume(UndoAssetMask.Buildings, out FakeUndoSnapshot active, out UndoAssetMask startedMask, out _), "SelUndo.Pause.Start");
            AssertEqual(UndoAssetMask.Buildings, startedMask, "SelUndo.Pause.StartedMask");
            active.RestoredCount = 1;
            AssertTrue(tracker.Pause(), "SelUndo.Pause.Paused");

            // Even when current Settings mask has 0 matching items or changes to Trees, CanUndo is true and Resume keeps Buildings!
            AssertTrue(tracker.CanUndo(UndoAssetMask.None), "SelUndo.Pause.CanUndoWithNoneMask");
            AssertTrue(tracker.CanUndo(UndoAssetMask.Trees), "SelUndo.Pause.CanUndoWithOtherMask");

            AssertTrue(tracker.TryStartOrResume(UndoAssetMask.Trees, out FakeUndoSnapshot resumed, out UndoAssetMask resumedMask, out bool isResume), "SelUndo.Pause.Resume");
            AssertTrue(isResume, "SelUndo.Pause.IsResume");
            AssertEqual(UndoAssetMask.Buildings, resumedMask, "SelUndo.Pause.MaskPreserved");
            AssertEqual(2, tracker.ActiveTotalCount, "SelUndo.Pause.TotalPreserved");
            AssertEqual(1, tracker.GetNextUndoCount(UndoAssetMask.None), "SelUndo.Pause.RemainingUsesActiveTotal");
            AssertTrue(object.ReferenceEquals(active, resumed), "SelUndo.Pause.SameSnapshot");
        }

        private static void TestSelectiveUndoBuildingChildrenAndCategoryCaptureFailures()
        {
            // Simulate accumulating building record weights across two buildings (3 + 5 = 8, not overwritten by 5; no boundary counted)
            var counts = new UndoCategoryCounts();
            int weightBeforeB1 = 0;
            int recordedWeight = 3; // B1 root + sub-area + sub-net
            counts.Buildings += (recordedWeight - weightBeforeB1);
            int weightBeforeB2 = recordedWeight;
            recordedWeight += 5; // B2 root + upgrade + upgrade sub-area + 2 sub-nets
            counts.Buildings += (recordedWeight - weightBeforeB2);
            counts.Networks = 5;
            counts.Areas = 4;

            AssertEqual(8, counts.Buildings, "SelUndo.BuildingWeightAccumulated");
            // Building children stay under Buildings mask, unaffected by standalone Networks/Areas toggles.
            AssertEqual(8, counts.GetFilteredSum(UndoAssetMask.Buildings), "SelUndo.BuildingsIgnoreStandaloneNetAreaOff");
            AssertEqual(9, counts.GetFilteredSum(UndoAssetMask.Networks | UndoAssetMask.Areas), "SelUndo.StandaloneNetAreaIgnoreBuildings");

            // Regression test: Sub-edges belonging to a deleted building must NOT be captured as standalone Networks.
            AssertTrue(!SelectiveUndoLogic.ShouldCaptureEdgeAsStandalone(hasOwner: true, isOwnerInSubmitted: true),
                "SelUndo.SubEdgeWithOwnerInPlanNotCapturedAsStandalone");
            AssertTrue(SelectiveUndoLogic.ShouldCaptureEdgeAsStandalone(hasOwner: true, isOwnerInSubmitted: false),
                "SelUndo.EdgeWithOwnerOutsidePlanCapturedAsStandalone");
            AssertTrue(SelectiveUndoLogic.ShouldCaptureEdgeAsStandalone(hasOwner: false, isOwnerInSubmitted: false),
                "SelUndo.StandaloneEdgeWithoutOwnerCaptured");

            // Deleting only a building with sub-nets: Networks filter must yield 0, avoiding restoring orphan internal networks.
            var buildingOnlySnapshot = new UndoCategoryCounts { Buildings = 1 };
            AssertEqual(0, buildingOnlySnapshot.GetFilteredSum(UndoAssetMask.Networks),
                "SelUndo.NetworksOnlyDoesNotRestoreBuildingSubNets");

            // Per-category capture failures: Trees undo must NOT become Partial due to uncaptured Buildings.
            var tracker = new SelectiveUndoTracker<FakeUndoSnapshot>(5);
            var snap = new FakeUndoSnapshot
            {
                Counts = new UndoCategoryCounts { Buildings = 2, Trees = 3 },
                NotCaptured = new UndoCategoryCounts { Buildings = 4, Trees = 0 },
            };
            tracker.OnCaptureCompleted(snap);

            AssertEqual(0, tracker.GetUiSnapshot(UndoAssetMask.Trees).NotCapturedCount, "SelUndo.TreePreviewIgnoresBuildingNotCaptured");
            AssertEqual(4, tracker.GetUiSnapshot(UndoAssetMask.Buildings).NotCapturedCount, "SelUndo.BuildingPreviewSeesBuildingNotCaptured");

            AssertTrue(tracker.TryStartOrResume(UndoAssetMask.Trees, out FakeUndoSnapshot active, out _, out _), "SelUndo.StartTreesOnly");
            active.RestoredCount = 3;
            tracker.CompleteActive();
            SelectiveUndoUiSnapshot doneUi = tracker.GetUiSnapshot(UndoAssetMask.Trees);
            AssertEqual("UndoSubmitted", doneUi.FinishedNotice, "SelUndo.TreesNotPollutedByBuildingFailure");
            AssertEqual(0, doneUi.FinishedNotCapturedCount, "SelUndo.FinishedNotCapturedFiltered");

            // All supported objects failed capture -> do NOT save snapshot, report UndoNothingCaptured
            var allFailed = new FakeUndoSnapshot
            {
                Counts = default(UndoCategoryCounts),
                NotCaptured = new UndoCategoryCounts { Props = 5 },
            };
            AssertTrue(!tracker.OnCaptureCompleted(allFailed), "SelUndo.AllFailedNotSaved");
            SelectiveUndoUiSnapshot failUi = tracker.GetUiSnapshot(UndoAssetMask.All);
            AssertEqual(0, failUi.HistoryDepth, "SelUndo.AllFailedHistoryEmpty");
            AssertEqual(5, failUi.CaptureFailedCount, "SelUndo.AllFailedCountExposed");
            AssertEqual("UndoNothingCaptured", failUi.Notice, "SelUndo.AllFailedNotice");
        }

        private static void TestSelectiveUndoCompletionSwitchAndSameTotalCategoryDiff()
        {
            var tracker = new SelectiveUndoTracker<FakeUndoSnapshot>(5);
            var s1 = new FakeUndoSnapshot
            {
                Counts = new UndoCategoryCounts { Buildings = 2, Trees = 0 },
                NotCaptured = new UndoCategoryCounts { Buildings = 1 },
            };
            var s2 = new FakeUndoSnapshot
            {
                Counts = new UndoCategoryCounts { Buildings = 0, Trees = 2 },
                NotCaptured = new UndoCategoryCounts { Trees = 1 },
            };

            tracker.OnCaptureCompleted(s1);
            SelectiveUndoUiSnapshot uiBeforePushS2 = tracker.GetUiSnapshot(UndoAssetMask.All);

            tracker.OnCaptureCompleted(s2);
            SelectiveUndoUiSnapshot uiAfterPushS2 = tracker.GetUiSnapshot(UndoAssetMask.All);

            // Same NextCount (2) and CanUndo (true), but category mix changed [2,0] -> [0,2]: snapshot MUST differ for UI push!
            AssertEqual(uiBeforePushS2.NextCount, uiAfterPushS2.NextCount, "SelUndo.SameTotalDifferentCategories.NextEqual");
            AssertTrue(uiBeforePushS2 != uiAfterPushS2, "SelUndo.SameTotalDifferentCategories.TriggersPush");

            // Run S2 and complete (1 restored, 1 skipped) while S1 remains in history.
            AssertTrue(tracker.TryStartOrResume(UndoAssetMask.All, out FakeUndoSnapshot active, out _, out _), "SelUndo.Switch.StartS2");
            active.RestoredCount = 1;
            active.SkippedCount = 1;
            tracker.CompleteActive();

            // Simultaneous completion cache (S2) and next-snapshot preview (S1) must stay cleanly separated!
            SelectiveUndoUiSnapshot uiDone = tracker.GetUiSnapshot(UndoAssetMask.Buildings);
            AssertEqual("UndoSubmittedPartial", uiDone.FinishedNotice, "SelUndo.Switch.FinishedNotice");
            AssertEqual(1, uiDone.FinishedRestoredCount, "SelUndo.Switch.FinishedRestored");
            AssertEqual(1, uiDone.FinishedSkippedCount, "SelUndo.Switch.FinishedSkipped");
            AssertEqual(2, uiDone.FinishedTotalCount, "SelUndo.Switch.FinishedFilteredTotal");
            AssertEqual(1, uiDone.FinishedNotCapturedCount, "SelUndo.Switch.FinishedNotCaptured");

            AssertEqual(1, uiDone.HistoryDepth, "SelUndo.Switch.NextSnapshotIsS1");
            AssertEqual(2, uiDone.Counts.Buildings, "SelUndo.Switch.PreviewCountsFromS1");
            AssertEqual(0, uiDone.Counts.Trees, "SelUndo.Switch.PreviewTreesZeroFromS1");
            AssertEqual(2, uiDone.NextCount, "SelUndo.Switch.PreviewFilteredCountFromS1");
            AssertEqual(1, uiDone.NotCapturedCount, "SelUndo.Switch.PreviewNotCapturedFromS1");
            AssertTrue(uiDone.CanUndo, "SelUndo.Switch.CanUndoS1");
            AssertTrue(uiDone != uiAfterPushS2, "SelUndo.Switch.PostFinishDiffersFromPreRun");
        }

        private static void TestSelectiveUndoStopFromInProgressAndPaused()
        {
            // 1. Stop directly while InProgress (no earlier history): unlocks HasActiveUndo, CanUndo becomes false, mask falls back.
            var tracker = new SelectiveUndoTracker<FakeUndoSnapshot>(5);
            var sOnly = new FakeUndoSnapshot
            {
                Counts = new UndoCategoryCounts { Buildings = 5, Trees = 2 },
                NotCaptured = new UndoCategoryCounts { Buildings = 1, Trees = 3 },
            };
            tracker.OnCaptureCompleted(sOnly);
            AssertTrue(tracker.TryStartOrResume(UndoAssetMask.Buildings, out FakeUndoSnapshot active1, out _, out _), "SelUndo.StopActive.Start");
            active1.RestoredCount = 2;
            active1.SkippedCount = 1;

            tracker.FailActive("UndoStopped");
            AssertTrue(!tracker.HasActiveUndo, "SelUndo.StopActive.NotActive");
            AssertTrue(!tracker.IsInProgress, "SelUndo.StopActive.NotInProgress");
            AssertTrue(!tracker.IsPaused, "SelUndo.StopActive.NotPaused");
            AssertTrue(!tracker.CanUndo(UndoAssetMask.All), "SelUndo.StopActive.CanUndoFalseWhenHistoryEmpty");
            AssertTrue(!tracker.TryStartOrResume(UndoAssetMask.All, out _, out _, out _), "SelUndo.StopActive.CannotResumeStopped");

            SelectiveUndoUiSnapshot ui1 = tracker.GetUiSnapshot(UndoAssetMask.Trees);
            AssertEqual("UndoStopped", ui1.FinishedNotice, "SelUndo.StopActive.FinishedNotice");
            AssertEqual(2, ui1.FinishedRestoredCount, "SelUndo.StopActive.FinishedRestored");
            AssertEqual(1, ui1.FinishedSkippedCount, "SelUndo.StopActive.FinishedSkipped");
            AssertEqual(5, ui1.FinishedTotalCount, "SelUndo.StopActive.FinishedTotal");
            AssertEqual(1, ui1.FinishedNotCapturedCount, "SelUndo.StopActive.FinishedNotCapturedFiltered");
            AssertEqual(UndoAssetMask.Trees, ui1.EffectiveMask, "SelUndo.StopActive.EffectiveMaskFallsBackToCurrent");
            AssertEqual(0, ui1.NextCount, "SelUndo.StopActive.NextCountZero");

            // 2. Stop from Paused while an earlier snapshot S1 remains in history: S2 stays consumed, CanUndo reflects S1 only.
            var s1 = new FakeUndoSnapshot { Counts = new UndoCategoryCounts { Trees = 2 } };
            var s2 = new FakeUndoSnapshot { Counts = new UndoCategoryCounts { Buildings = 4 } };
            tracker.OnCaptureCompleted(s1);
            tracker.OnCaptureCompleted(s2);
            AssertTrue(tracker.TryStartOrResume(UndoAssetMask.Buildings, out FakeUndoSnapshot active2, out _, out _), "SelUndo.StopPaused.StartS2");
            active2.RestoredCount = 1;
            AssertTrue(tracker.Pause(), "SelUndo.StopPaused.Pause");
            AssertTrue(tracker.IsPaused, "SelUndo.StopPaused.IsPaused");

            tracker.FailActive("UndoStopped");
            AssertTrue(!tracker.HasActiveUndo, "SelUndo.StopPaused.HasActiveUndoFalse");
            AssertTrue(!tracker.IsPaused, "SelUndo.StopPaused.IsPausedFalse");
            AssertEqual(1, tracker.HistoryDepth, "SelUndo.StopPaused.HistoryHasOnlyS1");
            AssertTrue(!tracker.CanUndo(UndoAssetMask.Buildings), "SelUndo.StopPaused.CanUndoFalseForAbandonedS2Buildings");
            AssertTrue(tracker.CanUndo(UndoAssetMask.Trees), "SelUndo.StopPaused.CanUndoTrueForEarlierS1Trees");

            SelectiveUndoUiSnapshot ui2 = tracker.GetUiSnapshot(UndoAssetMask.Buildings);
            AssertEqual("UndoStopped", ui2.FinishedNotice, "SelUndo.StopPaused.FinishedNotice");
            AssertEqual(1, ui2.FinishedRestoredCount, "SelUndo.StopPaused.FinishedRestored");
            AssertEqual(0, ui2.FinishedSkippedCount, "SelUndo.StopPaused.FinishedSkipped");
            AssertEqual(4, ui2.FinishedTotalCount, "SelUndo.StopPaused.FinishedTotal");
            AssertEqual(UndoAssetMask.Buildings, ui2.EffectiveMask, "SelUndo.StopPaused.EffectiveMaskFallsBack");

            AssertTrue(tracker.TryStartOrResume(UndoAssetMask.Trees, out FakeUndoSnapshot nextSnap, out _, out bool isResume), "SelUndo.StopPaused.StartNextIsS1");
            AssertTrue(!isResume, "SelUndo.StopPaused.NotResumed");
            AssertTrue(object.ReferenceEquals(s1, nextSnap), "SelUndo.StopPaused.StartsS1NotS2");
        }

        private static void TestPointerDragThresholdPromotion()
        {
            AssertTrue(!MarqueePointerLogic.ShouldPromoteToDrag(0f, 0f), "Pointer.Zero stays click");
            AssertTrue(!MarqueePointerLogic.ShouldPromoteToDrag(7f, 0f), "Pointer.ExactThreshold stays click");
            AssertTrue(!MarqueePointerLogic.ShouldPromoteToDrag(-4f, 5f), "Pointer.Within7px stays click (4^2+5^2=41<=49)");
            AssertTrue(MarqueePointerLogic.ShouldPromoteToDrag(-5f, 5f), "Pointer.Above7px promotes to drag (5^2+5^2=50>49)");
            AssertTrue(MarqueePointerLogic.ShouldPromoteToDrag(0f, 7.1f), "Pointer.YAbove7px promotes to drag");
            AssertTrue(!MarqueePointerLogic.ShouldPromoteToDrag(float.NaN, 10f), "Pointer.NaN guarded");
            AssertTrue(!MarqueePointerLogic.ShouldPromoteToDrag(10f, float.PositiveInfinity), "Pointer.Infinity guarded");
        }

        private static void TestEscapeResolutionAndEditRetention()
        {
            // Single backend ESC priority: Modal -> Drag/PointerPressed -> Pending/RetainedBox -> None.
            AssertEqual(
                EscapeAction.CloseImportantModal,
                MarqueePointerLogic.ResolveEscapeAction(true, true, true, true, true),
                "Escape.ModalTakesHighestPriority");
            AssertEqual(
                EscapeAction.CloseImportantModal,
                MarqueePointerLogic.ResolveEscapeAction(true, true, true, true, true, true),
                "Escape.ImportantModalBeatsColorModal");
            AssertEqual(
                EscapeAction.CloseColorModal,
                MarqueePointerLogic.ResolveEscapeAction(false, true, true, true, true, true),
                "Escape.ColorModalClosesWithoutClearingSelectionOrDrag");

            // Cross-system ESC order tests using real EscapeFrameGate + TryConsumeToolEscape + TryConsumeUiColorModalEscape.
            // 1a. UIUpdate -> ToolUpdate with ColorModalOpen + HasPending (dragging=false):
            var gate = new EscapeFrameGate();
            bool colorModalOpen = true;
            bool importantModalOpen = false;
            bool hasPending = true;
            bool dragging = false;
            AssertTrue(
                MarqueePointerLogic.TryConsumeUiColorModalEscape(gate, 100, true, colorModalOpen, importantModalOpen, true, true),
                "Escape.UiToTool_Pending_UiClosesColorModal");
            colorModalOpen = false;
            AssertEqual(
                EscapeAction.None,
                MarqueePointerLogic.TryConsumeToolEscape(gate, 100, true, true, importantModalOpen, colorModalOpen, dragging, false, hasPending, false),
                "Escape.UiToTool_Pending_ToolSkipsAlreadyConsumedFrame");
            AssertTrue(hasPending, "Escape.UiToTool_PendingPreserved");
            AssertEqual(
                EscapeAction.ClearSelection,
                MarqueePointerLogic.TryConsumeToolEscape(gate, 101, true, true, importantModalOpen, colorModalOpen, dragging, false, hasPending, false),
                "Escape.UiToTool_NextFrameClearsPending");

            // 1b. UIUpdate -> ToolUpdate with ColorModalOpen + Dragging:
            gate.Reset();
            colorModalOpen = true;
            dragging = true;
            hasPending = true;
            AssertTrue(
                MarqueePointerLogic.TryConsumeUiColorModalEscape(gate, 200, true, colorModalOpen, false, true, true),
                "Escape.UiToTool_Dragging_UiClosesColorModal");
            colorModalOpen = false;
            AssertEqual(
                EscapeAction.None,
                MarqueePointerLogic.TryConsumeToolEscape(gate, 200, true, true, false, colorModalOpen, dragging, false, hasPending, false),
                "Escape.UiToTool_Dragging_ToolDoesNotCancelDrag");
            AssertEqual(
                EscapeAction.CancelPointerOrDrag,
                MarqueePointerLogic.TryConsumeToolEscape(gate, 201, true, true, false, colorModalOpen, dragging, false, hasPending, false),
                "Escape.UiToTool_NextFrameCancelsDrag");

            // 2a. ToolUpdate -> UIUpdate with ImportantModalOpen + ColorModalOpen (+ Pending):
            gate.Reset();
            importantModalOpen = true;
            colorModalOpen = true;
            dragging = false;
            hasPending = true;
            AssertEqual(
                EscapeAction.CloseImportantModal,
                MarqueePointerLogic.TryConsumeToolEscape(gate, 300, true, true, importantModalOpen, colorModalOpen, dragging, false, hasPending, false),
                "Escape.ToolToUi_ToolClosesImportantModalFirst");
            importantModalOpen = false;
            AssertTrue(
                !MarqueePointerLogic.TryConsumeUiColorModalEscape(gate, 300, true, colorModalOpen, importantModalOpen, true, true),
                "Escape.ToolToUi_UiDoesNotAlsoCloseColorModalSameFrame");
            AssertTrue(colorModalOpen, "Escape.ToolToUi_ColorModalStaysOpenSameFrame");
            AssertTrue(
                MarqueePointerLogic.TryConsumeUiColorModalEscape(gate, 301, true, colorModalOpen, importantModalOpen, true, true),
                "Escape.ToolToUi_NextFrameClosesColorModal");

            // 2b. ToolUpdate -> UIUpdate when Tool defers CloseColorModal (+ Pending & + Dragging):
            gate.Reset();
            colorModalOpen = true;
            AssertEqual(
                EscapeAction.CloseColorModal,
                MarqueePointerLogic.TryConsumeToolEscape(gate, 400, true, true, false, colorModalOpen, true, false, true, false),
                "Escape.ToolToUi_ToolDefersCloseColorModalWhileDragging");
            AssertTrue(!gate.IsConsumedThisFrame(400), "Escape.ToolToUi_DeferDoesNotPreConsumeGate");
            AssertTrue(
                MarqueePointerLogic.TryConsumeUiColorModalEscape(gate, 400, true, colorModalOpen, false, true, true),
                "Escape.ToolToUi_UiClosesColorModalAfterDefer");
            AssertTrue(gate.IsConsumedThisFrame(400), "Escape.ToolToUi_ActualCloserRegistersGate");

            // 3. Vanilla Bulldoze (only UI runs):
            gate.Reset();
            AssertTrue(
                MarqueePointerLogic.TryConsumeUiColorModalEscape(gate, 500, true, true, false, true, true),
                "Escape.VanillaBulldozeClosesColorModal");
            AssertTrue(
                !MarqueePointerLogic.TryConsumeUiColorModalEscape(gate, 501, false, true, false, true, true),
                "Escape.BlockedWhenCannotUseTool");
            AssertEqual(
                EscapeAction.CancelPointerOrDrag,
                MarqueePointerLogic.ResolveEscapeAction(false, false, true, true, true),
                "Escape.PointerPressedDoesNotClearExistingPending");
            AssertEqual(
                EscapeAction.CancelPointerOrDrag,
                MarqueePointerLogic.ResolveEscapeAction(false, true, false, true, true),
                "Escape.DraggingCancelsDragFirst");
            AssertEqual(
                EscapeAction.ClearSelection,
                MarqueePointerLogic.ResolveEscapeAction(false, false, false, true, false),
                "Escape.PendingClearsSelection");
            AssertEqual(
                EscapeAction.ClearSelection,
                MarqueePointerLogic.ResolveEscapeAction(false, false, false, false, true),
                "Escape.RetainedHiddenEditsClearsSelection");
            AssertEqual(
                EscapeAction.None,
                MarqueePointerLogic.ResolveEscapeAction(false, false, false, false, false),
                "Escape.IdlePassesThrough");
            AssertTrue(
                MarqueePointerLogic.CanClearSelection(true, true, false, false, true),
                "CanClear.TrueForHiddenRetainedEdits");
            AssertTrue(
                !MarqueePointerLogic.CanClearSelection(false, true, false, false, true),
                "CanClear.BlockedDuringInputFieldFocus");

            // Filtered-out edit state retention: 0 visible items must still retain locked box or manual click-adds.
            AssertTrue(
                MarqueePointerLogic.ShouldRetainFilteredSelection(0, hasUsableQuad: false, manuallyAddedCount: 1),
                "Retain.ZeroVisibleWithManualAddsPreserved");
            AssertTrue(
                MarqueePointerLogic.ShouldRetainFilteredSelection(0, hasUsableQuad: true, manuallyAddedCount: 0),
                "Retain.ZeroVisibleWithUsableQuadPreserved");
            AssertTrue(
                MarqueePointerLogic.ShouldRetainFilteredSelection(2, hasUsableQuad: false, manuallyAddedCount: 0),
                "Retain.VisibleItemsPreserved");
            AssertTrue(
                !MarqueePointerLogic.ShouldRetainFilteredSelection(0, hasUsableQuad: false, manuallyAddedCount: 0),
                "Retain.CompletelyEmptyDropped");
            AssertEqual(
                "hidden_retained|2",
                MarqueePointerLogic.ComputeHiddenRetainedNotice(0, hasUsableQuad: false, manuallyAddedCount: 2),
                "Retain.NoticeShowsManualCountWhenHidden");
            AssertEqual(
                "hidden_box",
                MarqueePointerLogic.ComputeHiddenRetainedNotice(0, hasUsableQuad: true, manuallyAddedCount: 0),
                "Retain.NoticeShowsBoxWhenHidden");
            AssertEqual(
                string.Empty,
                MarqueePointerLogic.ComputeHiddenRetainedNotice(1, hasUsableQuad: false, manuallyAddedCount: 1),
                "Retain.NoticeEmptyWhenVisible");
        }

        private static void TestSingleClickHiddenRetainedClearedByEscape()
        {
            // Flow: pure single-click add (no quad) -> all filtered out -> ESC clears -> re-enabling filter does not bring it back.
            bool hasUsableQuad = false;
            int manuallyAdded = 1;
            int visibleCount = 1;

            bool hasPending = visibleCount > 0;
            bool hasRetainedEdits = MarqueePointerLogic.ShouldRetainFilteredSelection(visibleCount, hasUsableQuad, manuallyAdded);
            AssertTrue(hasPending && hasRetainedEdits, "Flow.Step1.SingleClickAddedActive");

            // Step 2: filter hides the manually added item (visible becomes 0, no box).
            visibleCount = 0;
            hasPending = visibleCount > 0;
            hasRetainedEdits = MarqueePointerLogic.ShouldRetainFilteredSelection(visibleCount, hasUsableQuad, manuallyAdded);
            AssertTrue(!hasPending && hasRetainedEdits, "Flow.Step2.ZeroVisibleRetainsManualAdd");
            AssertEqual(
                "hidden_retained|1",
                MarqueePointerLogic.ComputeHiddenRetainedNotice(visibleCount, hasUsableQuad, manuallyAdded),
                "Flow.Step2.NoticeReportsHiddenManualAdd");
            AssertTrue(
                MarqueePointerLogic.CanClearSelection(true, true, false, hasPending, hasRetainedEdits),
                "Flow.Step2.CustomClearHotkeyEnabled");

            // Step 3: press ESC -> clears retained pending selection.
            EscapeAction esc = MarqueePointerLogic.ResolveEscapeAction(
                importantModalOpen: false,
                dragging: false,
                pointerPressed: false,
                hasPending: hasPending,
                hasRetainedEdits: hasRetainedEdits);
            AssertEqual(EscapeAction.ClearSelection, esc, "Flow.Step3.EscapeClearsRetainedManualAdd");
            if (esc == EscapeAction.ClearSelection)
            {
                manuallyAdded = 0;
                hasUsableQuad = false;
                visibleCount = 0;
            }

            // Step 4: re-enable filter -> no pending/retained record remains, so item does not reappear.
            hasRetainedEdits = MarqueePointerLogic.ShouldRetainFilteredSelection(visibleCount, hasUsableQuad, manuallyAdded);
            AssertTrue(!hasRetainedEdits, "Flow.Step4.DoesNotReappearAfterFilterRestored");
            AssertEqual(
                string.Empty,
                MarqueePointerLogic.ComputeHiddenRetainedNotice(visibleCount, hasUsableQuad, manuallyAdded),
                "Flow.Step4.NoticeCleared");
            AssertEqual(
                EscapeAction.None,
                MarqueePointerLogic.ResolveEscapeAction(false, false, false, false, hasRetainedEdits),
                "Flow.Step4.SubsequentEscapePassesThrough");
        }

        private static void TestColorLibraryParsingAndTripleFallback()
        {
            // Valid hex parsing + uppercase normalization + explicit pure black preserved.
            AssertTrue(ColorLibrary.TryParseHexRgb("#1a2b3c", out Rgb24 rgb1), "Color.LowercaseHexValid");
            AssertEqual("#1A2B3C", rgb1.ToHex(), "Color.NormalizedUppercase");
            AssertTrue(ColorLibrary.TryParseHexRgb("  #000000 ", out Rgb24 black), "Color.ExplicitBlackAllowed");
            AssertEqual(new Rgb24(0, 0, 0), black, "Color.ExplicitBlackRgb");

            // Invalid/missing/corrupt hex falls back cleanly to defaults.
            AssertEqual(ColorLibrary.DefaultBuildingResidential, ColorLibrary.NormalizeHex(null, ColorLibrary.DefaultBuildingResidential), "Color.NullFallsBack");
            AssertEqual(ColorLibrary.DefaultBuildingResidential, ColorLibrary.NormalizeHex("", ColorLibrary.DefaultBuildingResidential), "Color.EmptyFallsBack");
            AssertEqual(ColorLibrary.DefaultBuildingResidential, ColorLibrary.NormalizeHex("#123", ColorLibrary.DefaultBuildingResidential), "Color.ShortHexFallsBack");
            AssertEqual(ColorLibrary.DefaultBuildingResidential, ColorLibrary.NormalizeHex("#GGGGGG", ColorLibrary.DefaultBuildingResidential), "Color.NonHexFallsBack");
            AssertEqual(ColorLibrary.DefaultBuildingResidential, ColorLibrary.NormalizeHex("#FF00FFAA", ColorLibrary.DefaultBuildingResidential), "Color.EightHexRejected");
            AssertEqual(ColorLibrary.DefaultBuildingResidential, ColorLibrary.NormalizeHex("FF00FF", ColorLibrary.DefaultBuildingResidential), "Color.MissingHashRejected");

            // Alpha fallback rules: NaN / Infinity / negative / >1.0001 corrupt -> fallback; explicit 0f preserved for outline hide.
            AssertEqual(ColorLibrary.DefaultAlphaOutline, ColorLibrary.NormalizeAlpha(float.NaN, ColorLibrary.DefaultAlphaOutline), "Alpha.NaNFallsBack");
            AssertEqual(ColorLibrary.DefaultAlphaOutline, ColorLibrary.NormalizeAlpha(float.PositiveInfinity, ColorLibrary.DefaultAlphaOutline), "Alpha.InfFallsBack");
            AssertEqual(ColorLibrary.DefaultAlphaOutline, ColorLibrary.NormalizeAlpha(-0.5f, ColorLibrary.DefaultAlphaOutline), "Alpha.NegativeFallsBack");
            AssertEqual(ColorLibrary.DefaultAlphaOutline, ColorLibrary.NormalizeAlpha(50f, ColorLibrary.DefaultAlphaOutline), "Alpha.CorruptLargeFallsBack");
            AssertEqual(0f, ColorLibrary.NormalizeAlpha(0f, ColorLibrary.DefaultAlphaOutline), "Alpha.ZeroAllowedForOutlineHide");

            // Simulated legacy config upgrade (missing/null/corrupt fields on deserialized palette): Sanitize restores all defaults.
            var legacy = new ColorPaletteData
            {
                MarqueeGroundBorder = null,
                MarqueeGroundFill = "",
                MarqueeUndergroundBorder = "bad",
                MarqueeUndergroundFill = "#12",
                BuildingResidential = null,
                BuildingCommercial = "#GGGGGG",
                BuildingIndustrial = null,
                BuildingOffice = "   ",
                BuildingService = "#12345678",
                WarningPulseStart = null,
                WarningPulseEnd = "invalid",
                AssetNetwork = null,
                AssetTree = "",
                AssetProp = null,
                AssetDecal = null,
                AssetArea = null,
                AssetMoving = null,
                DarkOutline = null,
                AlphaMarqueeFill = float.NaN,
                AlphaBuildingFill = -1f,
                AlphaOutline = float.NegativeInfinity,
            };
            ColorLibrary.Sanitize(legacy);
            ColorPaletteData defaults = ColorLibrary.CreateDefaultPalette();
            AssertEqual(defaults.MarqueeGroundBorder, legacy.MarqueeGroundBorder, "Legacy.GroundBorderRestored");
            AssertEqual(defaults.MarqueeUndergroundFill, legacy.MarqueeUndergroundFill, "Legacy.UndergroundFillRestored");
            AssertEqual(defaults.BuildingResidential, legacy.BuildingResidential, "Legacy.BuildingResRestored");
            AssertEqual(defaults.BuildingService, legacy.BuildingService, "Legacy.BuildingServiceRestored");
            AssertEqual(defaults.WarningPulseStart, legacy.WarningPulseStart, "Legacy.WarningStartRestored");
            AssertEqual(defaults.AssetNetwork, legacy.AssetNetwork, "Legacy.AssetNetworkRestored");
            AssertEqual(defaults.DarkOutline, legacy.DarkOutline, "Legacy.DarkOutlineRestored");
            AssertEqual(defaults.AlphaMarqueeFill, legacy.AlphaMarqueeFill, "Legacy.AlphaMarqueeRestored");
            AssertEqual(defaults.AlphaBuildingFill, legacy.AlphaBuildingFill, "Legacy.AlphaBuildingRestored");
            AssertEqual(defaults.AlphaOutline, legacy.AlphaOutline, "Legacy.AlphaOutlineRestored");

            // Key mutation + JSON output preserves valid edits & rejects corrupt values.
            AssertTrue(ColorLibrary.SetColorByKey(legacy, "buildingResidential", "#00ff88"), "Color.SetValidKey");
            AssertTrue(!ColorLibrary.SetColorByKey(legacy, "buildingResidential", "badhex"), "Color.RejectInvalidKeyUpdate");
            AssertEqual("#00FF88", legacy.BuildingResidential, "Color.NormalizedKeyAfterUpdate");
            AssertTrue(ColorLibrary.SetAlphaByKey(legacy, "alphaOutline", 0f), "Alpha.SetZeroOutline");
            string json = ColorLibrary.ToJson(legacy);
            AssertTrue(json.Contains("\"buildingResidential\":\"#00FF88\""), "Color.JsonContainsUpdatedColor");
            AssertTrue(json.Contains("\"alphaOutline\":0"), "Color.JsonContainsOutlineZero");

            // Presets: all 4 preset IDs resolve to valid sanitized palettes; unknown rejected; colorblind avoids red/green.
            string[] presetIds = { "classic", "neon", "colorblind", "pastel" };
            for (int i = 0; i < presetIds.Length; i++)
            {
                AssertTrue(ColorLibrary.TryGetPreset(presetIds[i], out ColorPaletteData preset), "Preset.Exists_" + presetIds[i]);
                string beforeJson = ColorLibrary.ToJson(preset);
                ColorLibrary.Sanitize(preset);
                AssertEqual(beforeJson, ColorLibrary.ToJson(preset), "Preset.AlreadyCanonical_" + presetIds[i]);
            }
            AssertTrue(!ColorLibrary.TryGetPreset("nonexistent", out ColorPaletteData missingPreset) && missingPreset == null, "Preset.UnknownRejected");

            AssertTrue(ColorLibrary.TryGetPreset("colorblind", out ColorPaletteData cb), "Preset.ColorblindLoaded");
            AssertTrue(ColorLibrary.TryParseHexRgb(cb.BuildingResidential, out Rgb24 cbRes), "Preset.ColorblindResRgb");
            AssertTrue(ColorLibrary.TryParseHexRgb(cb.BuildingService, out Rgb24 cbSrv), "Preset.ColorblindServiceRgb");
            AssertTrue(ColorLibrary.TryParseHexRgb(cb.MarqueeGroundBorder, out Rgb24 cbBorder), "Preset.ColorblindBorderRgb");
            // Residential uses Sky Blue (B dominant), Ground Border uses Amber (no green-vs-red clash).
            AssertTrue(cbRes.B > cbRes.R && cbRes.B > cbRes.G, "Preset.ColorblindResNotGreen");
            AssertTrue(cbBorder.R > cbBorder.B && cbBorder.G > 100, "Preset.ColorblindBorderAmberNotPureRed");
            AssertTrue(!cbRes.Equals(cbSrv) && !cbRes.Equals(cbBorder), "Preset.ColorblindKeyColorsDistinct");

            // DeferredSaveCoordinator: verifies both shutdown orders prevent losing the last 0.25s color edit.
            var coordinator = new DeferredSaveCoordinator();
            var paletteTarget = ColorLibrary.CreateDefaultPalette();
            int savedCount = 0;
            string savedHex = null;
            Action<ColorPaletteData> saveTarget = p =>
            {
                savedCount++;
                savedHex = p.BuildingResidential;
            };

            // Order A: Mod.OnDispose() flushes BEFORE Settings reference is cleared, then UISystem.OnDestroy() runs.
            ColorPaletteData simulatedGlobalSettings = paletteTarget;
            ColorLibrary.SetColorByKey(simulatedGlobalSettings, "buildingResidential", "#AA1122");
            coordinator.Schedule(simulatedGlobalSettings, saveTarget, 0.25f);
            AssertTrue(!coordinator.Tick(0.10f), "DeferredSave.DoesNotFlushEarly");
            AssertEqual(0, savedCount, "DeferredSave.StillWaitingWithinDebounce");
            // Simulate Mod.OnDispose() flushing BEFORE clearing global Settings:
            AssertTrue(coordinator.Flush(), "DeferredSave.OnDisposeFlushesPending");
            simulatedGlobalSettings = null;
            // Subsequent UISystem.OnDestroy() flush is an idempotent no-op:
            AssertTrue(!coordinator.Flush(), "DeferredSave.SubsequentOnDestroyNoDuplicate");
            AssertEqual(1, savedCount, "DeferredSave.SavedOnceBeforeDisposeClear");
            AssertEqual("#AA1122", savedHex, "DeferredSave.OrderAPersistedHex");

            // Order B: Global reference already nulled BEFORE UISystem.OnDestroy() Flush() runs -> captured target still saves!
            simulatedGlobalSettings = paletteTarget;
            ColorLibrary.SetColorByKey(simulatedGlobalSettings, "buildingResidential", "#33CCFF");
            coordinator.Schedule(simulatedGlobalSettings, saveTarget, 0.25f);
            simulatedGlobalSettings = null; // cleared without calling OnDispose flush first
            AssertTrue(coordinator.Flush(), "DeferredSave.CapturedTargetStillFlushesWhenGlobalNulled");
            AssertEqual(2, savedCount, "DeferredSave.OrderBSavedOnce");
            AssertEqual("#33CCFF", savedHex, "DeferredSave.OrderBPersistedLastEdit");

            // Debounce timer expiry & SaveNow cancellation:
            ColorLibrary.SetColorByKey(paletteTarget, "buildingResidential", "#44EE55");
            coordinator.Schedule(paletteTarget, saveTarget, 0.25f);
            AssertTrue(coordinator.Tick(0.25f), "DeferredSave.TickExpiresAndFlushes");
            AssertEqual(3, savedCount, "DeferredSave.TickSavedOnce");
            ColorLibrary.SetColorByKey(paletteTarget, "buildingResidential", "#998877");
            coordinator.Schedule(paletteTarget, saveTarget, 0.25f);
            coordinator.SaveNow(paletteTarget, saveTarget);
            AssertTrue(!coordinator.HasPending && !coordinator.Flush(), "DeferredSave.SaveNowClearsPending");
            AssertEqual(4, savedCount, "DeferredSave.SaveNowCountExact");
            AssertEqual("#998877", savedHex, "DeferredSave.SaveNowPersisted");

            // Single-target contract: scheduling a DIFFERENT instance flushes prior target first instead of dropping it.
            var otherTarget = ColorLibrary.CreateDefaultPalette();
            ColorLibrary.SetColorByKey(paletteTarget, "buildingResidential", "#101010");
            ColorLibrary.SetColorByKey(otherTarget, "buildingResidential", "#202020");
            coordinator.Schedule(paletteTarget, saveTarget, 0.25f);
            coordinator.Schedule(otherTarget, saveTarget, 0.25f);
            AssertEqual(5, savedCount, "DeferredSave.SwitchingTargetFlushesPriorTarget");
            AssertEqual("#101010", savedHex, "DeferredSave.PriorTargetSavedBeforeOverwrite");
            AssertTrue(coordinator.Flush(), "DeferredSave.NewTargetStillPendingUntilFlushed");
            AssertEqual(6, savedCount, "DeferredSave.NewTargetFlushed");
            AssertEqual("#202020", savedHex, "DeferredSave.NewTargetPersisted");
        }

        private static void TestAssetOverlayBudgetDecimationAndPerf()
        {
            // 1. Area polygon decimation bounds & strict monotonicity
            var idx = new List<int>();
            AssetOverlayLogic.DecimatePolygonIndices(1, AssetOverlayLogic.MaxEdgesPerArea, idx);
            AssertEqual(0, idx.Count, "AreaDecimate.SingleNodeEmpty");
            AssetOverlayLogic.DecimatePolygonIndices(12, AssetOverlayLogic.MaxEdgesPerArea, idx);
            AssertEqual(12, idx.Count, "AreaDecimate.UnderCapKeepsAll");
            AssetOverlayLogic.DecimatePolygonIndices(400, AssetOverlayLogic.MaxEdgesPerArea, idx);
            AssertEqual(AssetOverlayLogic.MaxEdgesPerArea, idx.Count, "AreaDecimate.HugePastureCappedAt20Edges");
            for (int i = 1; i < idx.Count; i++)
            {
                AssertTrue(idx[i] > idx[i - 1], "AreaDecimate.StrictlyIncreasingIndices");
            }
            AssertEqual(0, idx[0], "AreaDecimate.StartsAtVertexZero");
            AssertTrue(idx[idx.Count - 1] < 400, "AreaDecimate.WithinBounds");

            // 2. Network width scaling & clamp fallbacks
            AssertEqual(3.2f, AssetOverlayLogic.ClampNetWidth(float.NaN), "NetWidth.NaNFallback");
            AssertEqual(3.2f, AssetOverlayLogic.ClampNetWidth(0f), "NetWidth.ZeroFallback");
            AssertEqual(1.2f, AssetOverlayLogic.ClampNetWidth(0.5f), "NetWidth.SmallPipeMinClamp");
            AssertEqual(13.6f, AssetOverlayLogic.ClampNetWidth(16f), "NetWidth.AvenueScaled");
            AssertEqual(24.0f, AssetOverlayLogic.ClampNetWidth(40f), "NetWidth.HighwayMaxClamp");

            // 3. Budget allocation: <=1600 fast path admits all (including 1000 trees)
            var candidates = new List<AssetBudgetCandidate>(2600);
            var scratchOrder = new List<int>(2600);
            var projectedFlags = new List<bool>(2600);
            var projectedIndices = new List<int>(2600);

            for (int i = 0; i < 1000; i++)
            {
                candidates.Add(new AssetBudgetCandidate
                {
                    WorldX = i * 2f,
                    WorldZ = 0f,
                    DistSqToCursor = i * 4f,
                    PrimitiveCost = 1,
                    InViewport = i < 500,
                    Valid = true,
                });
            }
            int used1k = AssetOverlayLogic.AllocateProjectedBudget(
                candidates, AssetOverlayLogic.MaxAssetOverlayPrimitives,
                scratchOrder, projectedFlags, projectedIndices);
            AssertEqual(1000, used1k, "AssetBudget.FastPathAll1000Projected");
            AssertEqual(1000, projectedIndices.Count, "AssetBudget.FastPathAll1000Indices");

            // 4. Budget allocation: >1600 viewport culling first
            candidates.Clear();
            for (int i = 0; i < 2500; i++)
            {
                bool inView = i >= 1000 && i < 2200; // 1200 in viewport, 1300 offscreen
                candidates.Add(new AssetBudgetCandidate
                {
                    WorldX = i,
                    WorldZ = 0f,
                    DistSqToCursor = i * (float)i,
                    PrimitiveCost = 1,
                    InViewport = inView,
                    Valid = true,
                });
            }
            int usedView = AssetOverlayLogic.AllocateProjectedBudget(
                candidates, AssetOverlayLogic.MaxAssetOverlayPrimitives,
                scratchOrder, projectedFlags, projectedIndices);
            AssertEqual(1200, usedView, "AssetBudget.ViewportOnly1200WhenOffscreenPushesOver1600");
            AssertTrue(!projectedFlags[0], "AssetBudget.OffscreenFallsBackToNativeHighlight");
            AssertTrue(projectedFlags[1500], "AssetBudget.InViewportProjected");

            // 5. Budget allocation: >1600 in-viewport -> nearest to cursor wins, rest fallback
            candidates.Clear();
            for (int i = 0; i < 2400; i++)
            {
                // Reverse distance order so furthest items appear earliest in input list
                float distSq = (2399 - i) * (2399 - i);
                candidates.Add(new AssetBudgetCandidate
                {
                    WorldX = i,
                    WorldZ = 0f,
                    DistSqToCursor = distSq,
                    PrimitiveCost = 1,
                    InViewport = true,
                    Valid = true,
                });
            }
            int usedProximity = AssetOverlayLogic.AllocateProjectedBudget(
                candidates, AssetOverlayLogic.MaxAssetOverlayPrimitives,
                scratchOrder, projectedFlags, projectedIndices);
            AssertEqual(1600, usedProximity, "AssetBudget.CappedAtExact1600");
            AssertTrue(!projectedFlags[0], "AssetBudget.FarthestFallsBackToNativeHighlight");
            AssertTrue(projectedFlags[2399], "AssetBudget.NearestToCursorPrioritized");

            // 6. Mixed area outlines (cost 20) + single primitives obey <=1600 hard ceiling without partial polygon
            candidates.Clear();
            for (int i = 0; i < 1590; i++)
            {
                candidates.Add(new AssetBudgetCandidate { DistSqToCursor = i, PrimitiveCost = 1, InViewport = true, Valid = true });
            }
            candidates.Add(new AssetBudgetCandidate { DistSqToCursor = 1595f, PrimitiveCost = 20, InViewport = true, Valid = true }); // doesn't fit cleanly (only 10 left)
            candidates.Add(new AssetBudgetCandidate { DistSqToCursor = 1600f, PrimitiveCost = 1, InViewport = true, Valid = true });  // fits in remaining 10
            int usedMixed = AssetOverlayLogic.AllocateProjectedBudget(
                candidates, AssetOverlayLogic.MaxAssetOverlayPrimitives,
                scratchOrder, projectedFlags, projectedIndices);
            AssertEqual(1591, usedMixed, "AssetBudget.AreaOverRemainingFallsBackWholeAndAdmitsFittingItem");
            AssertTrue(!projectedFlags[1590], "AssetBudget.OversizedAreaFallsBackInsteadOfHalfDrawn");
            AssertTrue(projectedFlags[1591], "AssetBudget.SubsequentSmallItemAdmitted");

            // 7. Offline CPU throughput benchmark for 1000-tree & 2500-mixed overflow scenarios (200 iterations each)
            var bench1k = new List<AssetBudgetCandidate>(1000);
            for (int i = 0; i < 1000; i++)
            {
                bench1k.Add(new AssetBudgetCandidate { WorldX = i * 3f, WorldZ = i * 1.5f, DistSqToCursor = i, PrimitiveCost = 1, InViewport = true, Valid = true });
            }
            // Warmup JIT
            AssetOverlayLogic.AllocateProjectedBudget(bench1k, AssetOverlayLogic.MaxAssetOverlayPrimitives, scratchOrder, projectedFlags, projectedIndices);
            var sw1k = System.Diagnostics.Stopwatch.StartNew();
            for (int iter = 0; iter < 200; iter++)
            {
                AssetOverlayLogic.AllocateProjectedBudget(bench1k, AssetOverlayLogic.MaxAssetOverlayPrimitives, scratchOrder, projectedFlags, projectedIndices);
            }
            sw1k.Stop();

            var benchOverflow = new List<AssetBudgetCandidate>(2500);
            var decIndices = new List<int>(20);
            for (int i = 0; i < 2500; i++)
            {
                benchOverflow.Add(new AssetBudgetCandidate
                {
                    WorldX = i * 2f,
                    WorldZ = (i % 50) * 4f,
                    DistSqToCursor = ((i * 7919) % 2500) + 1f,
                    PrimitiveCost = (i % 50 == 0) ? 20 : 1,
                    InViewport = true,
                    Valid = true,
                });
            }
            AssetOverlayLogic.AllocateProjectedBudget(benchOverflow, AssetOverlayLogic.MaxAssetOverlayPrimitives, scratchOrder, projectedFlags, projectedIndices);
            var swOverflow = System.Diagnostics.Stopwatch.StartNew();
            for (int iter = 0; iter < 200; iter++)
            {
                AssetOverlayLogic.AllocateProjectedBudget(benchOverflow, AssetOverlayLogic.MaxAssetOverlayPrimitives, scratchOrder, projectedFlags, projectedIndices);
                AssetOverlayLogic.DecimatePolygonIndices(400, AssetOverlayLogic.MaxEdgesPerArea, decIndices);
            }
            swOverflow.Stop();

            double us1k = (sw1k.Elapsed.TotalMilliseconds * 1000.0) / 200.0;
            double usOverflow = (swOverflow.Elapsed.TotalMilliseconds * 1000.0) / 200.0;
            AssertTrue(usOverflow < 10000.0, "AssetBudget.PerfUnder10msPerRefresh");
            Console.WriteLine($"AssetOverlay CPU bench (200 runs avg): 1k_fast={us1k:F1} us/refresh, 2.5k_overflow_sort+decimate={usOverflow:F1} us/refresh.");
        }

        private static void TestInvalidGeometryFallbackHighlights()
        {
            // 1. Direct invalid-geometry evaluation against real production AssetOverlayLogic helpers
            AssetRenderKind badCurveNan = AssetOverlayLogic.EvaluateCurveMarker(
                float.NaN, 0f, 0f,
                1f, 0f, 0f,
                2f, 0f, 0f,
                3f, 0f, 0f,
                1.5f, 0f, 0f,
                6f,
                out float widthNan);
            AssertTrue(badCurveNan == AssetRenderKind.None, "InvalidGeo.CurveNanIsRenderKindNone");
            AssertEqual(0f, widthNan, "InvalidGeo.CurveNanWidthZero");

            AssetRenderKind badCurveInf = AssetOverlayLogic.EvaluateCurveMarker(
                0f, 0f, 0f,
                1f, 0f, 0f,
                2f, float.PositiveInfinity, 0f,
                3f, 0f, 0f,
                1.5f, 0f, 0f,
                6f,
                out float _);
            AssertTrue(badCurveInf == AssetRenderKind.None, "InvalidGeo.CurveInfIsRenderKindNone");

            AssetRenderKind badCurveMid = AssetOverlayLogic.EvaluateCurveMarker(
                0f, 0f, 0f,
                1f, 0f, 0f,
                2f, 0f, 0f,
                3f, 0f, 0f,
                1.5f, float.NaN, 0f,
                6f,
                out float _);
            AssertTrue(badCurveMid == AssetRenderKind.None, "InvalidGeo.CurveBadMidIsRenderKindNone");

            AssetRenderKind badNetNode = AssetOverlayLogic.EvaluateNetworkNodeMarker(
                true,
                float.NegativeInfinity, 0f, 5f,
                out float netNodeDiameter);
            AssertTrue(badNetNode == AssetRenderKind.None, "InvalidGeo.NetNodeInfIsRenderKindNone");
            AssertEqual(0f, netNodeDiameter, "InvalidGeo.NetNodeInfDiameterZero");

            AssetRenderKind badObjMissing = AssetOverlayLogic.EvaluateObjectCircleMarker(false, 0f, 0f, 0f);
            AssetRenderKind badObjNan = AssetOverlayLogic.EvaluateObjectCircleMarker(true, 10f, float.NaN, 20f);
            AssetRenderKind badObjInf = AssetOverlayLogic.EvaluateObjectCircleMarker(true, 10f, 0f, float.PositiveInfinity);
            AssertTrue(badObjMissing == AssetRenderKind.None, "InvalidGeo.MissingTransformIsRenderKindNone");
            AssertTrue(badObjNan == AssetRenderKind.None, "InvalidGeo.TransformNanIsRenderKindNone");
            AssertTrue(badObjInf == AssetRenderKind.None, "InvalidGeo.TransformInfIsRenderKindNone");

            AssetRenderKind badAreaNoBuffer = AssetOverlayLogic.EvaluateAreaMarker(false, 0, 0, 0f, 0f, 0f, 0);
            AssetRenderKind badAreaEmpty = AssetOverlayLogic.EvaluateAreaMarker(true, 0, 0, 0f, 0f, 0f, 0);
            AssetRenderKind badAreaZeroFinite = AssetOverlayLogic.EvaluateAreaMarker(true, 4, 0, 0f, 0f, 0f, 0);
            AssetRenderKind badAreaBadCentroid = AssetOverlayLogic.EvaluateAreaMarker(true, 4, 4, float.PositiveInfinity, 0f, 0f, 4);
            AssetRenderKind smallFiniteArea = AssetOverlayLogic.EvaluateAreaMarker(true, 2, 2, 10f, 0f, 20f, 0);
            AssertTrue(badAreaNoBuffer == AssetRenderKind.None, "InvalidGeo.AreaNoBufferIsRenderKindNone");
            AssertTrue(badAreaEmpty == AssetRenderKind.None, "InvalidGeo.AreaEmptyNodesIsRenderKindNone");
            AssertTrue(badAreaZeroFinite == AssetRenderKind.None, "InvalidGeo.AreaZeroFiniteNodesIsRenderKindNone");
            AssertTrue(badAreaBadCentroid == AssetRenderKind.None, "InvalidGeo.AreaNonFiniteCentroidIsRenderKindNone");
            AssertTrue(smallFiniteArea == AssetRenderKind.Circle, "InvalidGeo.AreaTwoValidNodesFallsBackToCircle");

            // 2. End-to-end marker -> candidate -> budget allocation -> fallback set -> native Highlighted check
            AssetRenderKind validCurve = AssetOverlayLogic.EvaluateCurveMarker(
                0f, 0f, 0f,
                2f, 0f, 0f,
                4f, 0f, 0f,
                6f, 0f, 0f,
                3f, 0f, 0f,
                8f,
                out float validCurveWidth);
            AssertTrue(validCurve == AssetRenderKind.Curve, "InvalidGeo.ValidCurveKind");
            AssertTrue(validCurveWidth >= 1.2f && validCurveWidth <= 24.0f, "InvalidGeo.ValidCurveWidthClamped");

            AssetRenderKind validTree = AssetOverlayLogic.EvaluateObjectCircleMarker(true, 15f, 0f, 25f);
            AssertTrue(validTree == AssetRenderKind.Circle, "InvalidGeo.ValidTreeKind");

            AssetRenderKind validArea = AssetOverlayLogic.EvaluateAreaMarker(true, 4, 4, 30f, 0f, 40f, 4);
            AssertTrue(validArea == AssetRenderKind.AreaOutline, "InvalidGeo.ValidAreaKind");

            var markerIds = new List<int>
            {
                2001, // valid Curve
                1001, // invalid Curve (NaN Bezier)
                2002, // valid Tree Circle
                1002, // invalid Tree Transform (NaN position)
                2003, // valid AreaOutline (cost 4)
                1003, // invalid Area (0 valid nodes)
                1004, // invalid Net Node (-Infinity)
            };

            var candidates = new List<AssetBudgetCandidate>
            {
                AssetOverlayLogic.BuildCandidate(validCurve, 0, 3f, 0f, 9f, true),
                AssetOverlayLogic.BuildCandidate(badCurveNan, 0, float.NaN, 0f, 0f, false),
                AssetOverlayLogic.BuildCandidate(validTree, 0, 15f, 25f, 25f, true),
                AssetOverlayLogic.BuildCandidate(badObjNan, 0, 10f, float.NaN, 0f, false),
                AssetOverlayLogic.BuildCandidate(validArea, 4, 30f, 40f, 100f, true),
                AssetOverlayLogic.BuildCandidate(badAreaZeroFinite, 0, 0f, 0f, 0f, false),
                AssetOverlayLogic.BuildCandidate(badNetNode, 0, float.NegativeInfinity, 5f, 0f, false),
            };

            AssertEqual(0, candidates[1].PrimitiveCost, "InvalidGeo.BadCurveZeroCost");
            AssertTrue(!candidates[1].Valid, "InvalidGeo.BadCurveInvalidCandidate");
            AssertEqual(0, candidates[3].PrimitiveCost, "InvalidGeo.BadObjZeroCost");
            AssertTrue(!candidates[3].Valid, "InvalidGeo.BadObjInvalidCandidate");
            AssertEqual(0, candidates[5].PrimitiveCost, "InvalidGeo.BadAreaZeroCost");
            AssertTrue(!candidates[5].Valid, "InvalidGeo.BadAreaInvalidCandidate");
            AssertEqual(0, candidates[6].PrimitiveCost, "InvalidGeo.BadNetNodeZeroCost");
            AssertTrue(!candidates[6].Valid, "InvalidGeo.BadNetNodeInvalidCandidate");

            var scratchOrder = new List<int>();
            var projectedFlags = new List<bool>();
            var projectedIndices = new List<int>();
            int used = AssetOverlayLogic.AllocateProjectedBudget(
                candidates,
                AssetOverlayLogic.MaxAssetOverlayPrimitives,
                scratchOrder,
                projectedFlags,
                projectedIndices);

            // Only the 3 valid items consume primitives (1 + 1 + 4 = 6); invalid markers stay unprojected
            AssertEqual(6, used, "InvalidGeo.UsedPrimitivesOnlyValid");
            AssertEqual(3, projectedIndices.Count, "InvalidGeo.ProjectedCountExcludesInvalid");
            AssertTrue(!projectedFlags[1] && !projectedFlags[3] && !projectedFlags[5] && !projectedFlags[6], "InvalidGeo.InvalidMarkersNotProjected");

            var fallbackIds = new HashSet<int>();
            int fallbackCount = AssetOverlayLogic.BuildFallbackSet(markerIds, projectedFlags, fallbackIds);
            AssertEqual(4, fallbackCount, "InvalidGeo.FallbackSetCount");
            AssertTrue(!fallbackIds.Contains(2001) && !fallbackIds.Contains(2002) && !fallbackIds.Contains(2003), "InvalidGeo.ValidProjectedNotInFallback");
            AssertTrue(fallbackIds.Contains(1001) && fallbackIds.Contains(1002) && fallbackIds.Contains(1003) && fallbackIds.Contains(1004), "InvalidGeo.AllInvalidEntitiesInFallback");

            // Native Highlighted decision: all invalid-geometry non-building live entities get Highlighted, while buildings/deleted do not
            AssertTrue(AssetOverlayLogic.ShouldApplyNativeHighlight(SelectionCategory.Network, fallbackIds.Contains(1001), true), "InvalidGeo.NativeHighlightAppliedToBadCurve");
            AssertTrue(AssetOverlayLogic.ShouldApplyNativeHighlight(SelectionCategory.Tree, fallbackIds.Contains(1002), true), "InvalidGeo.NativeHighlightAppliedToBadObject");
            AssertTrue(AssetOverlayLogic.ShouldApplyNativeHighlight(SelectionCategory.Area, fallbackIds.Contains(1003), true), "InvalidGeo.NativeHighlightAppliedToBadArea");
            AssertTrue(AssetOverlayLogic.ShouldApplyNativeHighlight(SelectionCategory.Network, fallbackIds.Contains(1004), true), "InvalidGeo.NativeHighlightAppliedToBadNetNode");
            AssertTrue(!AssetOverlayLogic.ShouldApplyNativeHighlight(SelectionCategory.Network, fallbackIds.Contains(2001), true), "InvalidGeo.ValidProjectedSkipsNativeHighlight");
            AssertTrue(!AssetOverlayLogic.ShouldApplyNativeHighlight(SelectionCategory.Building, true, true), "InvalidGeo.BuildingsNeverGetAssetNativeHighlight");
            AssertTrue(!AssetOverlayLogic.ShouldApplyNativeHighlight(SelectionCategory.Tree, fallbackIds.Contains(1002), false), "InvalidGeo.DeletedEntitySkipsNativeHighlight");
        }

        private static void TestFastDragReleaseRightClickUiGatingAndUndoScope()
        {
            // 1. State machine for press -> release without intermediate stillPressed frame:
            // Helper simulating MarqueeDeleteToolSystem's press/drag/click transitions using real MarqueePointerLogic.
            static (bool didCompleteDrag, bool didSingleClick, MarqueeShapeXZ completedQuad) StepPressSequence(
                CoordXZ pressWorld,
                (float dx, float dy, bool stillPressed, CoordXZ cursorWorld)[] frames)
            {
                bool pointerPressed = true;
                bool dragging = false;
                bool completedDrag = false;
                bool singleClicked = false;
                MarqueeShapeXZ finalQuad = default(MarqueeShapeXZ);

                for (int i = 0; i < frames.Length; i++)
                {
                    var f = frames[i];
                    if (pointerPressed)
                    {
                        PointerPressOutcome outcome = MarqueePointerLogic.ResolvePressUpdate(f.dx, f.dy, f.stillPressed);
                        if (outcome == PointerPressOutcome.PromoteToDrag)
                        {
                            pointerPressed = false;
                            dragging = true;
                        }
                        else if (outcome == PointerPressOutcome.CompleteDragOnRelease)
                        {
                            pointerPressed = false;
                            dragging = false;
                            completedDrag = true;
                            finalQuad = MarqueeShapeXZ.FromPoints(pressWorld, f.cursorWorld, new CoordXZ(0f, 1f), false);
                            break;
                        }
                        else if (outcome == PointerPressOutcome.SingleClick)
                        {
                            pointerPressed = false;
                            singleClicked = true;
                            break;
                        }
                    }
                    else if (dragging && !f.stillPressed)
                    {
                        dragging = false;
                        completedDrag = true;
                        finalQuad = MarqueeShapeXZ.FromPoints(pressWorld, f.cursorWorld, new CoordXZ(0f, 1f), false);
                        break;
                    }
                }

                return (completedDrag, singleClicked, finalQuad);
            }

            // 1a. Fast swipe: Press -> next frame immediately released with >7px delta (no intermediate stillPressed frame)
            var fastSwipe = StepPressSequence(
                new CoordXZ(100f, 100f),
                new[] { (24f, -18f, false, new CoordXZ(135f, 128f)) });
            AssertTrue(fastSwipe.didCompleteDrag, "FastDrag.ImmediateReleaseOverThresholdCompletesDrag");
            AssertTrue(!fastSwipe.didSingleClick, "FastDrag.ImmediateReleaseDoesNotSingleClick");
            AssertTrue(fastSwipe.completedQuad.IsUsable(), "FastDrag.ProducesUsableSelectionQuad");

            // 1b. Small jitter click: Press -> immediate release within 7px -> single click (existing behavior preserved)
            var shortClick = StepPressSequence(
                new CoordXZ(100f, 100f),
                new[] { (4f, 5f, false, new CoordXZ(100.5f, 100.5f)) });
            AssertTrue(!shortClick.didCompleteDrag, "FastDrag.WithinThresholdDoesNotDrag");
            AssertTrue(shortClick.didSingleClick, "FastDrag.WithinThresholdTriggersSingleClick");

            // 1c. Multi-frame normal drag: Press -> StayPressed(<=7) -> PromoteToDrag(>7, held) -> Release -> CompleteDrag
            var normalDrag = StepPressSequence(
                new CoordXZ(50f, 50f),
                new[]
                {
                    (2f, 2f, true, new CoordXZ(51f, 51f)),
                    (15f, 12f, true, new CoordXZ(80f, 75f)),
                    (20f, 18f, false, new CoordXZ(90f, 85f)),
                });
            AssertTrue(normalDrag.didCompleteDrag && !normalDrag.didSingleClick, "FastDrag.NormalMultiFrameDragStillWorks");
            AssertTrue(normalDrag.completedQuad.IsUsable(), "FastDrag.NormalDragUsableQuad");

            // 2. Right-click UI gating: cancel modal/drag still wins over UI, while world remove / exit tool respect mouseOverUi.
            AssertEqual(
                RightCancelAction.CloseImportantModal,
                MarqueePointerLogic.ResolveRightCancelAction(true, true, mouseOverUi: true, importantModalOpen: true, dragging: false, pointerPressed: false, hasPending: true),
                "RightClick.ModalClosesEvenOverUi");
            AssertEqual(
                RightCancelAction.CancelPointerOrDrag,
                MarqueePointerLogic.ResolveRightCancelAction(true, true, mouseOverUi: true, importantModalOpen: false, dragging: true, pointerPressed: false, hasPending: true),
                "RightClick.ActiveDragCancelsEvenOverUi");
            AssertEqual(
                RightCancelAction.CancelPointerOrDrag,
                MarqueePointerLogic.ResolveRightCancelAction(true, true, mouseOverUi: true, importantModalOpen: false, dragging: false, pointerPressed: true, hasPending: false),
                "RightClick.PointerPressedCancelsEvenOverUi");
            AssertEqual(
                RightCancelAction.None,
                MarqueePointerLogic.ResolveRightCancelAction(true, true, mouseOverUi: true, importantModalOpen: false, dragging: false, pointerPressed: false, hasPending: true),
                "RightClick.BlockedOverUiWhenWouldRemoveWorldEntity");
            AssertEqual(
                RightCancelAction.None,
                MarqueePointerLogic.ResolveRightCancelAction(true, true, mouseOverUi: true, importantModalOpen: false, dragging: false, pointerPressed: false, hasPending: false),
                "RightClick.BlockedOverUiWhenWouldExitTool");
            AssertEqual(
                RightCancelAction.RemoveEntityAtCursor,
                MarqueePointerLogic.ResolveRightCancelAction(true, true, mouseOverUi: false, importantModalOpen: false, dragging: false, pointerPressed: false, hasPending: true),
                "RightClick.RemovesWorldEntityOffUi");
            AssertEqual(
                RightCancelAction.ExitTool,
                MarqueePointerLogic.ResolveRightCancelAction(true, true, mouseOverUi: false, importantModalOpen: false, dragging: false, pointerPressed: false, hasPending: false),
                "RightClick.ExitsToolOffUiWhenIdle");

            // 3. Unified Undo scope (button and Ctrl+Z share CanTriggerUndo across Marquee + Bulldoze/mod-usable state).
            AssertTrue(
                MarqueePointerLogic.CanTriggerUndo(canUseMod: true, allowInput: true, pointerIdle: true, importantModalOpen: false, colorModalOpen: false, queuedDeletion: false, undoInProgress: false, canUndoForMask: true),
                "UndoScope.AllowedWheneverModUsableEvenOutsideMarqueeTool");
            AssertTrue(
                !MarqueePointerLogic.CanTriggerUndo(canUseMod: false, allowInput: true, pointerIdle: true, importantModalOpen: false, colorModalOpen: false, queuedDeletion: false, undoInProgress: false, canUndoForMask: true),
                "UndoScope.BlockedWhenModUnusable");
            AssertTrue(
                !MarqueePointerLogic.CanTriggerUndo(canUseMod: true, allowInput: true, pointerIdle: false, importantModalOpen: false, colorModalOpen: false, queuedDeletion: false, undoInProgress: false, canUndoForMask: true),
                "UndoScope.BlockedDuringDragOrPress");
            AssertTrue(
                !MarqueePointerLogic.CanTriggerUndo(canUseMod: true, allowInput: true, pointerIdle: true, importantModalOpen: true, colorModalOpen: false, queuedDeletion: false, undoInProgress: false, canUndoForMask: true),
                "UndoScope.BlockedDuringImportantModal");
            AssertTrue(
                !MarqueePointerLogic.CanTriggerUndo(canUseMod: true, allowInput: true, pointerIdle: true, importantModalOpen: false, colorModalOpen: true, queuedDeletion: false, undoInProgress: false, canUndoForMask: true),
                "UndoScope.BlockedDuringColorModal");
            AssertTrue(
                !MarqueePointerLogic.CanTriggerUndo(canUseMod: true, allowInput: true, pointerIdle: true, importantModalOpen: false, colorModalOpen: false, queuedDeletion: false, undoInProgress: false, canUndoForMask: false),
                "UndoScope.BlockedWhenNothingUndoableForMask");

            // 4. Panel hotkeys blocked while Color Palette modal is open, while ESC keeps closing the modal.
            AssertTrue(
                MarqueePointerLogic.CanUsePanelHotkeys(allowInput: true, toolOrBulldozeActive: true, pointerIdle: true, colorModalOpen: false),
                "PanelHotkeys.AllowedWhenColorModalClosed");
            AssertTrue(
                !MarqueePointerLogic.CanUsePanelHotkeys(allowInput: true, toolOrBulldozeActive: true, pointerIdle: true, colorModalOpen: true),
                "PanelHotkeys.BlockedWhenColorModalOpen");
            AssertTrue(
                MarqueePointerLogic.TryConsumeUiColorModalEscape(null, 1, canUse: true, colorModalOpen: true, importantModalOpen: false, allowInput: true, escapePressedThisFrame: true),
                "ColorModal.EscapeStillWorksWhenPanelHotkeysBlocked");
        }

        private static void TestDragAbortOnFocusLossAndGuards()
        {
            AssertTrue(
                MarqueePointerLogic.IsInputAllowed(appFocused: true, hasInputFieldFocus: false),
                "InputAllowed.FocusedAndNoTextField");
            AssertTrue(
                !MarqueePointerLogic.IsInputAllowed(appFocused: false, hasInputFieldFocus: false),
                "InputAllowed.BlockedWhenAltTabbed");
            AssertTrue(
                !MarqueePointerLogic.IsInputAllowed(appFocused: true, hasInputFieldFocus: true),
                "InputAllowed.BlockedWhenTextFieldFocused");

            // Alt-Tab / focus loss while dragging must Cancel (never Complete even if stillPressed became false)
            bool altTabAllow = MarqueePointerLogic.IsInputAllowed(appFocused: false, hasInputFieldFocus: false);
            AssertEqual(
                DragUpdateOutcome.Cancel,
                MarqueePointerLogic.ResolveDragUpdate(altTabAllow, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: false, stillPressed: false),
                "DragAbort.AltTabReleaseCancelsInsteadOfCompleting");
            AssertEqual(
                DragUpdateOutcome.Cancel,
                MarqueePointerLogic.ResolveDragUpdate(altTabAllow, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: false, stillPressed: true),
                "DragAbort.AltTabWhileHeldCancels");

            // Input field focus / important modal / active undo / queued deletion during drag also Cancel
            bool textFocusAllow = MarqueePointerLogic.IsInputAllowed(appFocused: true, hasInputFieldFocus: true);
            AssertEqual(
                DragUpdateOutcome.Cancel,
                MarqueePointerLogic.ResolveDragUpdate(textFocusAllow, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: false, stillPressed: false),
                "DragAbort.TextFieldFocusCancels");
            AssertEqual(
                DragUpdateOutcome.Cancel,
                MarqueePointerLogic.ResolveDragUpdate(allowInput: true, importantModalOpen: true, hasActiveUndo: false, queuedDeletion: false, stillPressed: false),
                "DragAbort.ImportantModalCancels");
            AssertEqual(
                DragUpdateOutcome.Cancel,
                MarqueePointerLogic.ResolveDragUpdate(allowInput: true, importantModalOpen: false, hasActiveUndo: true, queuedDeletion: false, stillPressed: false),
                "DragAbort.ActiveUndoCancels");
            AssertEqual(
                DragUpdateOutcome.Cancel,
                MarqueePointerLogic.ResolveDragUpdate(allowInput: true, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: true, stillPressed: false),
                "DragAbort.QueuedDeletionCancels");

            // Normal focused drag continues while held and completes only when released with all guards clear
            bool focusedAllow = MarqueePointerLogic.IsInputAllowed(appFocused: true, hasInputFieldFocus: false);
            AssertEqual(
                DragUpdateOutcome.Continue,
                MarqueePointerLogic.ResolveDragUpdate(focusedAllow, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: false, stillPressed: true),
                "DragAbort.FocusedHeldContinues");
            AssertEqual(
                DragUpdateOutcome.Complete,
                MarqueePointerLogic.ResolveDragUpdate(focusedAllow, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: false, stillPressed: false),
                "DragAbort.FocusedReleaseCompletes");

            // Fast drag release (CompleteDragOnRelease) must also obey ResolveDragUpdate guards and abort instead of completing
            static (bool completed, bool cancelled) StepFastReleaseWithGuards(
                float dx,
                float dy,
                bool allowInputAtRelease,
                bool importantModalOpen,
                bool hasActiveUndo,
                bool queuedDeletion)
            {
                PointerPressOutcome pressOutcome = MarqueePointerLogic.ResolvePressUpdate(dx, dy, stillPressed: false);
                if (pressOutcome == PointerPressOutcome.CompleteDragOnRelease)
                {
                    if (MarqueePointerLogic.ResolveDragUpdate(
                            allowInputAtRelease,
                            importantModalOpen,
                            hasActiveUndo,
                            queuedDeletion,
                            stillPressed: false) == DragUpdateOutcome.Cancel)
                    {
                        return (false, true);
                    }
                    return (true, false);
                }
                return (false, false);
            }

            var fastReleaseLostFocus = StepFastReleaseWithGuards(24f, -18f, allowInputAtRelease: false, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: false);
            AssertTrue(!fastReleaseLostFocus.completed && fastReleaseLostFocus.cancelled, "CompleteDragOnRelease.FocusLossCancelsInsteadOfCompleting");
            var fastReleaseModal = StepFastReleaseWithGuards(24f, -18f, allowInputAtRelease: true, importantModalOpen: true, hasActiveUndo: false, queuedDeletion: false);
            AssertTrue(!fastReleaseModal.completed && fastReleaseModal.cancelled, "CompleteDragOnRelease.ImportantModalCancels");
            var fastReleaseUndo = StepFastReleaseWithGuards(24f, -18f, allowInputAtRelease: true, importantModalOpen: false, hasActiveUndo: true, queuedDeletion: false);
            AssertTrue(!fastReleaseUndo.completed && fastReleaseUndo.cancelled, "CompleteDragOnRelease.ActiveUndoCancels");
            var fastReleaseQueued = StepFastReleaseWithGuards(24f, -18f, allowInputAtRelease: true, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: true);
            AssertTrue(!fastReleaseQueued.completed && fastReleaseQueued.cancelled, "CompleteDragOnRelease.QueuedDeletionCancels");
            var fastReleaseAllowed = StepFastReleaseWithGuards(24f, -18f, allowInputAtRelease: true, importantModalOpen: false, hasActiveUndo: false, queuedDeletion: false);
            AssertTrue(fastReleaseAllowed.completed && !fastReleaseAllowed.cancelled, "CompleteDragOnRelease.AllowedCompletesNormally");
        }

        private static void TestMarqueeShapePolygonsAndFreehand()
        {
            // 1. Concave L-shape polygon: (0,0)-(40,0)-(40,16)-(16,16)-(16,40)-(0,40)
            // Area = 40*16 + 16*24 = 1024 m2, Perimeter = 40+16+24+24+16+40 = 160m
            var lVerts = new[]
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(40f, 0f),
                new CoordXZ(40f, 16f),
                new CoordXZ(16f, 16f),
                new CoordXZ(16f, 40f),
                new CoordXZ(0f, 40f),
            };
            MarqueeShapeXZ lShape = MarqueeShapeXZ.FromPolygon(lVerts, MarqueeSelectionMode.Polygon);
            AssertTrue(lShape.IsUsable(), "ShapeL.IsUsable");
            AssertTrue(!lShape.IsRectangleFastPath, "ShapeL.NotRectFastPath");
            AssertEqual(MarqueeSelectionMode.Polygon, lShape.Mode, "ShapeL.Mode");
            AssertEqual(6, lShape.VertexCount, "ShapeL.VertexCount");
            AssertTrue(Math.Abs(lShape.AreaSqMeters - 1024f) < 1f, "ShapeL.AreaSqMeters");
            AssertTrue(Math.Abs(lShape.PerimeterMeters - 160f) < 0.5f, "ShapeL.PerimeterMeters");
            AssertEqual(16, lShape.EquivalentCells, "ShapeL.EquivalentCells");
            AssertEqual(16, lShape.AreaCells, "ShapeL.AreaCellsMatchesEquivalent");

            // Point containment: inside both arms, OUTSIDE concave notch (28, 28)
            AssertTrue(lShape.ContainsPoint(new CoordXZ(8f, 8f)), "ShapeL.InsideCornerArm");
            AssertTrue(lShape.ContainsPoint(new CoordXZ(32f, 8f)), "ShapeL.InsideRightArm");
            AssertTrue(lShape.ContainsPoint(new CoordXZ(8f, 32f)), "ShapeL.InsideTopArm");
            AssertTrue(!lShape.ContainsPoint(new CoordXZ(28f, 28f)), "ShapeL.NotchExcluded");

            // AABB vs actual notch: AABB cleanly outside at (28, 28) with +/-4 fits in notch (24..32) without hitting walls
            AssertTrue(!lShape.IntersectsAabb(24f, 32f, 24f, 32f), "ShapeL.AabbInsideNotchExcluded");
            AssertTrue(lShape.IntersectsAabb(14f, 20f, 14f, 20f), "ShapeL.AabbOverlappingInnerCornerIncluded");

            // Segment completely inside notch misses; segment bridging across notch between arms hits
            AssertTrue(!lShape.IntersectsSegment(new CoordXZ(22f, 22f), new CoordXZ(35f, 35f)), "ShapeL.SegmentInNotchExcluded");
            AssertTrue(lShape.IntersectsSegment(new CoordXZ(10f, 30f), new CoordXZ(30f, 10f)), "ShapeL.SegmentBridgingArmsHits");

            // Rotated OBB inside notch misses; rotated OBB touching arm hits
            float invSqrt2 = (float)(1.0 / Math.Sqrt(2.0));
            AssertTrue(!lShape.IntersectsOrientedRect(new CoordXZ(28f, 28f), new CoordXZ(3f * invSqrt2, 3f * invSqrt2), new CoordXZ(-3f * invSqrt2, 3f * invSqrt2)), "ShapeL.RotatedObbInsideNotchExcluded");
            AssertTrue(lShape.IntersectsOrientedRect(new CoordXZ(18f, 24f), new CoordXZ(5f * invSqrt2, 5f * invSqrt2), new CoordXZ(-2f * invSqrt2, 2f * invSqrt2)), "ShapeL.RotatedObbTouchingArmHits");

            // Triangle enclosed wholly inside L arm hits; triangle inside notch misses; giant enclosing triangle hits
            AssertTrue(lShape.IntersectsTriangle(new CoordXZ(4f, 4f), new CoordXZ(12f, 4f), new CoordXZ(8f, 12f)), "ShapeL.TriInsideArmHits");
            AssertTrue(!lShape.IntersectsTriangle(new CoordXZ(24f, 24f), new CoordXZ(36f, 24f), new CoordXZ(30f, 36f)), "ShapeL.TriInsideNotchExcluded");
            AssertTrue(lShape.IntersectsTriangle(new CoordXZ(-50f, -50f), new CoordXZ(120f, -50f), new CoordXZ(20f, 120f)), "ShapeL.GiantEnclosingTriangleHits");

            // 2. Concave U-shape polygon: notch open at top
            var uVerts = new[]
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(30f, 0f),
                new CoordXZ(30f, 30f),
                new CoordXZ(20f, 30f),
                new CoordXZ(20f, 10f),
                new CoordXZ(10f, 10f),
                new CoordXZ(10f, 30f),
                new CoordXZ(0f, 30f),
            };
            MarqueeShapeXZ uShape = MarqueeShapeXZ.FromPolygon(uVerts);
            AssertTrue(uShape.IsUsable(), "ShapeU.IsUsable");
            AssertTrue(!uShape.ContainsPoint(new CoordXZ(15f, 20f)), "ShapeU.CenterPocketExcluded");
            AssertTrue(uShape.ContainsPoint(new CoordXZ(5f, 25f)), "ShapeU.LeftProngInside");
            AssertTrue(uShape.ContainsPoint(new CoordXZ(25f, 25f)), "ShapeU.RightProngInside");
            AssertTrue(uShape.ContainsPoint(new CoordXZ(15f, 5f)), "ShapeU.BaseInside");

            // 3. Self-intersecting bowtie (figure-8) must use Odd-Even rule robustly
            // (0,0)-(20,20)-(0,20)-(20,0): crossing at (10,10); left lobe x<10,z near 10? Wait:
            // Edge order: (0,0)->(20,20) diag1, (20,20)->(0,20) top, (0,20)->(20,0) diag2, (20,0)->(0,0) bottom.
            // Top & bottom triangles meet at (10,10): bottom triangle around (10,3), top around (10,17); left/right around (3,10),(17,10) are OUTSIDE.
            var bowtie = MarqueeShapeXZ.FromPolygon(new[]
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(20f, 20f),
                new CoordXZ(0f, 20f),
                new CoordXZ(20f, 0f),
            });
            AssertTrue(bowtie.IsUsable(), "Bowtie.IsUsable");
            AssertTrue(bowtie.ContainsPoint(new CoordXZ(10f, 3f)), "Bowtie.BottomLobeInside");
            AssertTrue(bowtie.ContainsPoint(new CoordXZ(10f, 17f)), "Bowtie.TopLobeInside");
            AssertTrue(!bowtie.ContainsPoint(new CoordXZ(3f, 10f)), "Bowtie.LeftGapOutside");
            AssertTrue(!bowtie.ContainsPoint(new CoordXZ(17f, 10f)), "Bowtie.RightGapOutside");
            // Two triangles each of base 20, height 10 => 2 * (0.5 * 20 * 10) = 200 m2
            AssertTrue(Math.Abs(bowtie.AreaSqMeters - 200f) < 5f, "Bowtie.OddEvenAreaPositiveAndApprox200");

            // 4. Douglas-Peucker simplification + Freehand 64-vertex cap + invalid input guards
            var denseCircle = new System.Collections.Generic.List<CoordXZ>();
            for (int i = 0; i < 200; i++)
            {
                double ang = (2.0 * Math.PI * i) / 200.0;
                denseCircle.Add(new CoordXZ((float)(50.0 + 30.0 * Math.Cos(ang)), (float)(50.0 + 30.0 * Math.Sin(ang))));
            }
            MarqueeShapeXZ freehand = MarqueeShapeXZ.FromFreehandStroke(denseCircle, simplifyToleranceMeters: 0.6f, maxVertices: 64);
            AssertTrue(freehand.IsUsable(), "Freehand.IsUsable");
            AssertEqual(MarqueeSelectionMode.Freehand, freehand.Mode, "Freehand.Mode");
            AssertTrue(freehand.VertexCount >= 12 && freehand.VertexCount <= 64, "Freehand.SimplifiedAndCappedTo64");
            AssertTrue(freehand.ContainsPoint(new CoordXZ(50f, 50f)), "Freehand.CenterInside");
            AssertTrue(!freehand.ContainsPoint(new CoordXZ(90f, 50f)), "Freehand.OutsideRejected");
            AssertTrue(freehand.AreaSqMeters > 2400f && freehand.AreaSqMeters < 3000f, "Freehand.CircleAreaApproxPiR2");

            // Collinear/tiny polygon (< 1 m2 area) or <3 vertices or NaN rejected
            AssertTrue(!MarqueeShapeXZ.FromPolygon(new[] { new CoordXZ(0f, 0f), new CoordXZ(10f, 0f) }).IsUsable(), "Polygon.TwoVerticesInvalid");
            AssertTrue(!MarqueeShapeXZ.FromPolygon(new[] { new CoordXZ(0f, 0f), new CoordXZ(10f, 0f), new CoordXZ(5f, 0.05f) }).IsUsable(), "Polygon.Sub1SqmRejected");
            AssertTrue(!MarqueeShapeXZ.FromPolygon(new[] { new CoordXZ(0f, 0f), new CoordXZ(float.NaN, 0f), new CoordXZ(10f, 10f) }).IsFinite(), "Polygon.NaNNotFinite");

            // 5. Rectangle fast-path vs 4-vertex polygon equivalence
            MarqueeShapeXZ rectFast = MarqueeShapeXZ.FromPoints(new CoordXZ(0f, 0f), new CoordXZ(40f, 24f), new CoordXZ(0f, 1f), cameraAligned: false);
            MarqueeShapeXZ rectPoly = MarqueeShapeXZ.FromPolygon(new[]
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(40f, 0f),
                new CoordXZ(40f, 24f),
                new CoordXZ(0f, 24f),
            });
            for (float px = -8f; px <= 48f; px += 7f)
            {
                for (float pz = -8f; pz <= 32f; pz += 6f)
                {
                    var p = new CoordXZ(px, pz);
                    AssertEqual(rectFast.ContainsPoint(p), rectPoly.ContainsPoint(p), $"RectVsPoly.Point({px},{pz})");
                    AssertEqual(
                        rectFast.IntersectsOrientedRect(p, new CoordXZ(5f, 2f), new CoordXZ(-2f, 5f)),
                        rectPoly.IntersectsOrientedRect(p, new CoordXZ(5f, 2f), new CoordXZ(-2f, 5f)),
                        $"RectVsPoly.OBB({px},{pz})");
                    AssertEqual(
                        rectFast.IntersectsAabb(px - 3f, px + 3f, pz - 3f, pz + 3f),
                        rectPoly.IntersectsAabb(px - 3f, px + 3f, pz - 3f, pz + 3f),
                        $"RectVsPoly.AABB({px},{pz})");
                    AssertEqual(
                        rectFast.IntersectsSegment(p, new CoordXZ(px + 11f, pz - 9f)),
                        rectPoly.IntersectsSegment(p, new CoordXZ(px + 11f, pz - 9f)),
                        $"RectVsPoly.Seg({px},{pz})");
                    AssertEqual(
                        rectFast.IntersectsTriangle(p, new CoordXZ(px + 7f, pz + 2f), new CoordXZ(px + 2f, pz + 9f)),
                        rectPoly.IntersectsTriangle(p, new CoordXZ(px + 7f, pz + 2f), new CoordXZ(px + 2f, pz + 9f)),
                        $"RectVsPoly.Tri({px},{pz})");
                }
            }

            // 6. ShapeMetricsSnapshot compatibility: Rectangle keeps W/D cells+meters; Polygon/Freehand zero W/D & report Area/Perim/Verts/EquivCells
            ShapeMetricsSnapshot rectSnap = rectFast.GetMetricsSnapshot();
            AssertTrue(rectSnap.IsUsable && rectSnap.IsRectangle, "Snapshot.RectFlags");
            AssertEqual(MarqueeSelectionMode.Rectangle, rectSnap.Mode, "Snapshot.RectMode");
            AssertEqual(5, rectSnap.WidthCells, "Snapshot.RectWidthCells");
            AssertEqual(3, rectSnap.DepthCells, "Snapshot.RectDepthCells");
            AssertEqual(40, rectSnap.WidthMeters, "Snapshot.RectWidthMeters");
            AssertEqual(24, rectSnap.DepthMeters, "Snapshot.RectDepthMeters");
            AssertEqual(15, rectSnap.EquivalentCells, "Snapshot.RectEquivCells");
            AssertEqual(960, rectSnap.AreaSqMeters, "Snapshot.RectAreaSqMeters");

            ShapeMetricsSnapshot polySnap = lShape.GetMetricsSnapshot();
            AssertTrue(polySnap.IsUsable && !polySnap.IsRectangle, "Snapshot.PolyFlags");
            AssertEqual(MarqueeSelectionMode.Polygon, polySnap.Mode, "Snapshot.PolyMode");
            AssertEqual(0, polySnap.WidthCells, "Snapshot.PolyZerosWidthCells");
            AssertEqual(0, polySnap.DepthCells, "Snapshot.PolyZerosDepthCells");
            AssertEqual(0, polySnap.WidthMeters, "Snapshot.PolyZerosWidthMeters");
            AssertEqual(0, polySnap.DepthMeters, "Snapshot.PolyZerosDepthMeters");
            AssertEqual(1024, polySnap.AreaSqMeters, "Snapshot.PolyAreaSqMeters");
            AssertEqual(160, polySnap.PerimeterMeters, "Snapshot.PolyPerimeterMeters");
            AssertEqual(6, polySnap.VertexCount, "Snapshot.PolyVertexCount");
            AssertEqual(16, polySnap.EquivalentCells, "Snapshot.PolyEquivCells");
            AssertTrue(!polySnap.WasVertexLimitApplied, "Snapshot.PolyNotSimplified");
            AssertEqual(6, polySnap.OriginalVertexCount, "Snapshot.PolyOriginalVertexCount");

            // 7. GPTb regression: 129-vertex self-intersecting bowtie (4 bowtie corners + 125 collinear points along closing bottom edge)
            // Construction enforces MaxPolygonVertices (64) via Douglas-Peucker instead of silently failing or dropping crossing Z-levels > 128.
            var bowtie129 = new System.Collections.Generic.List<CoordXZ>(129)
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(20f, 20f),
                new CoordXZ(0f, 20f),
                new CoordXZ(20f, 0f),
            };
            for (int k = 1; k <= 125; k++)
            {
                float t = k / 126f;
                bowtie129.Add(new CoordXZ(20f * (1f - t), 0f));
            }
            AssertEqual(129, bowtie129.Count, "Bowtie129.InputCount");
            MarqueeShapeXZ bowtie129Shape = MarqueeShapeXZ.FromPolygon(bowtie129, MarqueeSelectionMode.Polygon);
            AssertTrue(bowtie129Shape.IsUsable(), "Bowtie129.IsUsable");
            AssertTrue(bowtie129Shape.VertexCount <= MarqueeShapeXZ.MaxPolygonVertices, "Bowtie129.CappedAt64");
            AssertTrue(bowtie129Shape.WasVertexLimitApplied, "Bowtie129.WasVertexLimitAppliedMarked");
            AssertEqual(129, bowtie129Shape.OriginalVertexCount, "Bowtie129.OriginalVertexCountPreserved");
            AssertTrue(Math.Abs(bowtie129Shape.AreaSqMeters - 200f) < 5f, "Bowtie129.OddEvenAreaIs200NotZero");
            AssertTrue(bowtie129Shape.ContainsPoint(new CoordXZ(10f, 3f)), "Bowtie129.BottomLobeInside");
            AssertTrue(bowtie129Shape.ContainsPoint(new CoordXZ(10f, 17f)), "Bowtie129.TopLobeInside");
            AssertTrue(!bowtie129Shape.ContainsPoint(new CoordXZ(3f, 10f)), "Bowtie129.LeftOutside");

            ShapeMetricsSnapshot capSnap = bowtie129Shape.GetMetricsSnapshot();
            AssertTrue(capSnap.WasVertexLimitApplied, "Snapshot.Bowtie129SimplifiedFlag");
            AssertEqual(129, capSnap.OriginalVertexCount, "Snapshot.Bowtie129OriginalCount");

            // 8. Non-collinear 130-vertex self-intersecting bowtie (small zig-zag perturbation) still simplifies <= 64 and computes ~200 m2
            var bowtiePerturbed130 = new System.Collections.Generic.List<CoordXZ>(130)
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(20f, 20f),
                new CoordXZ(0f, 20f),
                new CoordXZ(20f, 0f),
            };
            for (int k = 1; k <= 126; k++)
            {
                float t = k / 127f;
                float zJitter = (k % 2 == 0) ? 0.08f : -0.08f;
                bowtiePerturbed130.Add(new CoordXZ(20f * (1f - t), zJitter));
            }
            MarqueeShapeXZ bowtiePerturbedShape = MarqueeShapeXZ.FromPolygon(bowtiePerturbed130, MarqueeSelectionMode.Polygon);
            AssertTrue(bowtiePerturbedShape.IsUsable(), "BowtiePerturbed130.IsUsable");
            AssertTrue(bowtiePerturbedShape.VertexCount <= MarqueeShapeXZ.MaxPolygonVertices, "BowtiePerturbed130.CappedAt64");
            AssertTrue(bowtiePerturbedShape.WasVertexLimitApplied, "BowtiePerturbed130.WasVertexLimitApplied");
            AssertEqual(130, bowtiePerturbedShape.OriginalVertexCount, "BowtiePerturbed130.OriginalCount");
            AssertTrue(Math.Abs(bowtiePerturbedShape.AreaSqMeters - 200f) < 10f, "BowtiePerturbed130.AreaApprox200");
            AssertTrue(bowtiePerturbedShape.ContainsPoint(new CoordXZ(10f, 3f)), "BowtiePerturbed130.BottomLobeInside");
            AssertTrue(bowtiePerturbedShape.ContainsPoint(new CoordXZ(10f, 17f)), "BowtiePerturbed130.TopLobeInside");

            // 9. Empty / null / small-array Mode metadata consistency
            MarqueeShapeXZ emptyPoly = MarqueeShapeXZ.FromPolygon(Array.Empty<CoordXZ>(), MarqueeSelectionMode.Polygon);
            AssertEqual(MarqueeSelectionMode.Polygon, emptyPoly.Mode, "EmptyPoly.ModePreserved");
            AssertTrue(!emptyPoly.IsUsable(), "EmptyPoly.NotUsable");
            MarqueeShapeXZ nullFreehandPoly = MarqueeShapeXZ.FromPolygon(null, MarqueeSelectionMode.Freehand);
            AssertEqual(MarqueeSelectionMode.Freehand, nullFreehandPoly.Mode, "NullPolyFreehand.ModePreserved");
            AssertTrue(!nullFreehandPoly.IsUsable(), "NullPolyFreehand.NotUsable");
            MarqueeShapeXZ emptyFreehand = MarqueeShapeXZ.FromFreehandStroke(Array.Empty<CoordXZ>());
            AssertEqual(MarqueeSelectionMode.Freehand, emptyFreehand.Mode, "EmptyFreehand.ModePreserved");
            AssertTrue(!emptyFreehand.IsUsable(), "EmptyFreehand.NotUsable");
        }

        private static ToolStepInput MakeStepInput(
            EscapeFrameGate gate,
            int frame,
            double timeSec,
            bool allowInput = true,
            bool hasCursorWorld = true)
        {
            return new ToolStepInput
            {
                EscapeGate = gate,
                FrameCount = frame,
                TimeSeconds = timeSec,
                AllowInput = allowInput,
                HasCursorWorld = hasCursorWorld,
                CursorWorldXZ = new CoordXZ(0f, 0f),
                PressGroundWorldXZ = new CoordXZ(0f, 0f),
            };
        }

        private static void TestBatch5aModeSwitchAndPolygonStateMachine()
        {
            // 1. SelectionMode sanitize, cycle, CanCycleSelectionMode
            AssertEqual(MarqueeSelectionMode.Rectangle, MarqueePointerLogic.SanitizeSelectionMode(-1), "Mode.SanitizeNegative");
            AssertEqual(MarqueeSelectionMode.Rectangle, MarqueePointerLogic.SanitizeSelectionMode(0), "Mode.Sanitize0");
            AssertEqual(MarqueeSelectionMode.Polygon, MarqueePointerLogic.SanitizeSelectionMode(1), "Mode.Sanitize1");
            AssertEqual(MarqueeSelectionMode.Freehand, MarqueePointerLogic.SanitizeSelectionMode(2), "Mode.Sanitize2");
            AssertEqual(MarqueeSelectionMode.Rectangle, MarqueePointerLogic.SanitizeSelectionMode(99), "Mode.SanitizeOverflow");

            AssertEqual(MarqueeSelectionMode.Polygon, MarqueePointerLogic.CycleSelectionMode(MarqueeSelectionMode.Rectangle), "Mode.CycleRectToPoly");
            AssertEqual(MarqueeSelectionMode.Freehand, MarqueePointerLogic.CycleSelectionMode(MarqueeSelectionMode.Polygon), "Mode.CyclePolyToFreehand");
            AssertEqual(MarqueeSelectionMode.Rectangle, MarqueePointerLogic.CycleSelectionMode(MarqueeSelectionMode.Freehand), "Mode.CycleFreehandToRect");

            AssertTrue(MarqueePointerLogic.CanCycleSelectionMode(true, true, false, false, false, false), "Mode.CanCycleIdle");
            AssertTrue(!MarqueePointerLogic.CanCycleSelectionMode(false, true, false, false, false, false), "Mode.BlockCycleNoInput");
            AssertTrue(!MarqueePointerLogic.CanCycleSelectionMode(true, false, false, false, false, false), "Mode.BlockCycleToolInactive");
            AssertTrue(!MarqueePointerLogic.CanCycleSelectionMode(true, true, true, false, false, false), "Mode.BlockCycleMouseBusy");
            AssertTrue(!MarqueePointerLogic.CanCycleSelectionMode(true, true, false, true, false, false), "Mode.BlockCycleColorModal");
            AssertTrue(!MarqueePointerLogic.CanCycleSelectionMode(true, true, false, false, true, false), "Mode.BlockCycleImportantModal");
            AssertTrue(!MarqueePointerLogic.CanCycleSelectionMode(true, true, false, false, false, true), "Mode.BlockCycleActiveUndo");

            // 2. Mode switching clears open draft / drag cleanly while session stays valid
            var gate = new EscapeFrameGate();
            var session = new MarqueeToolSession();
            int frame = 100;
            AssertEqual(MarqueeSelectionMode.Rectangle, session.Mode, "Session.InitialModeRect");
            session.SetMode(MarqueeSelectionMode.Polygon);
            AssertEqual(MarqueeSelectionMode.Polygon, session.Mode, "Session.SetPolygon");

            var pClick = MakeStepInput(gate, ++frame, 1.0);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            pClick.CursorScreenX = 500f;
            pClick.CursorScreenY = 500f;
            ToolStepResult r0 = session.StepFrame(in pClick);
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, r0.Outcome, "Poly.AddV0");
            AssertEqual(1, session.PolygonDraft.Count, "Poly.Count1");

            pClick = MakeStepInput(gate, ++frame, 1.8);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(10f, 0f);
            pClick.CursorScreenX = 550f;
            pClick.CursorScreenY = 500f;
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, session.StepFrame(in pClick).Outcome, "Poly.AddV1");
            AssertEqual(2, session.PolygonDraft.Count, "Poly.Count2");

            // Switch Polygon -> Freehand clears open draft
            AssertEqual(MarqueeSelectionMode.Freehand, session.CycleMode(), "Mode.CycleToFreehand");
            AssertEqual(0, session.PolygonDraft.Count, "Mode.CycleClearsPolyDraft");
            AssertTrue(!session.IsBusy, "Mode.CycleNotBusy");

            // Switch back to Rectangle -> press/promote -> switch cancels drag
            AssertEqual(MarqueeSelectionMode.Rectangle, session.CycleMode(), "Mode.CycleToRect");
            var rPress = MakeStepInput(gate, ++frame, 2.5);
            rPress.LeftPressedThisFrame = true;
            rPress.PressGroundWorldXZ = new CoordXZ(10f, 10f);
            rPress.CursorScreenX = 100f;
            rPress.CursorScreenY = 100f;
            AssertEqual(ToolStepOutcome.RectanglePressStarted, session.StepFrame(in rPress).Outcome, "Rect.PressStartBeforeSwitch");
            AssertTrue(session.PointerPressed, "Rect.PointerPressedBeforeSwitch");
            session.SetMode(MarqueeSelectionMode.Polygon);
            AssertTrue(!session.PointerPressed && !session.Dragging && !session.IsBusy, "Mode.SwitchClearsRectPress");

            // 3. Polygon placement, < 0.8m debounce, < 3 no-close, 14px snap, 15px no-snap, double-click, Enter
            // Click 0: (0,0)
            pClick = MakeStepInput(gate, ++frame, 3.0);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            pClick.CursorScreenX = 200f;
            pClick.CursorScreenY = 200f;
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, session.StepFrame(in pClick).Outcome, "Poly2.AddV0");

            // Too close (< 0.8m) even > 0.35s later -> debounced
            pClick = MakeStepInput(gate, ++frame, 3.6);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(0.5f, 0.5f); // ~0.707m < 0.8m
            pClick.CursorScreenX = 201f;
            pClick.CursorScreenY = 201f;
            AssertEqual(ToolStepOutcome.None, session.StepFrame(in pClick).Outcome, "Poly2.Debounce07m");
            AssertEqual(1, session.PolygonDraft.Count, "Poly2.Still1AfterDebounce");

            // Click 1: (20,0)
            pClick = MakeStepInput(gate, ++frame, 4.2);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(20f, 0f);
            pClick.CursorScreenX = 300f;
            pClick.CursorScreenY = 200f;
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, session.StepFrame(in pClick).Outcome, "Poly2.AddV1");
            AssertEqual(2, session.PolygonDraft.Count, "Poly2.Count2");

            // With Count == 2, fast double-click (<0.35s, <=12px) or snap near v0 must NOT close (< 3 vertices)
            pClick = MakeStepInput(gate, ++frame, 4.35);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(20.1f, 0.1f);
            pClick.CursorScreenX = 302f;
            pClick.CursorScreenY = 201f;
            pClick.HasStartScreen = true;
            pClick.StartScreenX = 200f;
            pClick.StartScreenY = 200f;
            AssertEqual(ToolStepOutcome.None, session.StepFrame(in pClick).Outcome, "Poly2.DoubleClickCount2DoesNotClose");
            AssertEqual(2, session.PolygonDraft.Count, "Poly2.CountStill2");

            // Enter with Count == 2 must NOT close
            var enterStep = MakeStepInput(gate, ++frame, 4.9);
            enterStep.EnterKeyPressed = true;
            enterStep.ConfirmActionPressed = true;
            enterStep.HasPending = true;
            ToolStepResult enterTooFew = session.StepFrame(in enterStep);
            AssertTrue(!enterTooFew.CanClosePolygonByEnter, "Poly2.EnterDisabledAt2");
            AssertTrue(!enterTooFew.CanConfirmPending, "Poly2.ConfirmBlockedWhileDraftActive");
            AssertEqual(ToolStepOutcome.None, enterTooFew.Outcome, "Poly2.EnterNoOpAt2");

            // Click 2: (20,20) -> Count == 3
            pClick = MakeStepInput(gate, ++frame, 5.5);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(20f, 20f);
            pClick.CursorScreenX = 300f;
            pClick.CursorScreenY = 300f;
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, session.StepFrame(in pClick).Outcome, "Poly2.AddV2");
            AssertEqual(3, session.PolygonDraft.Count, "Poly2.Count3");

            // At 15px (>14px) from start screen (200,200) -> adds vertex 3 instead of snapping
            pClick = MakeStepInput(gate, ++frame, 6.2);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(0f, 20f);
            pClick.CursorScreenX = 215f;
            pClick.CursorScreenY = 200f;
            pClick.HasStartScreen = true;
            pClick.StartScreenX = 200f;
            pClick.StartScreenY = 200f;
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, session.StepFrame(in pClick).Outcome, "Poly2.15pxAddsV3NotSnap");
            AssertEqual(4, session.PolygonDraft.Count, "Poly2.Count4");

            // At 14px (<=14px) from start screen -> snaps & closes!
            pClick = MakeStepInput(gate, ++frame, 7.0);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(1f, 1f);
            pClick.CursorScreenX = 214f;
            pClick.CursorScreenY = 200f;
            pClick.HasStartScreen = true;
            pClick.StartScreenX = 200f;
            pClick.StartScreenY = 200f;
            ToolStepResult snapClose = session.StepFrame(in pClick);
            AssertEqual(ToolStepOutcome.CompletePolygonShape, snapClose.Outcome, "Poly2.Snap14pxCloses");
            AssertEqual(MarqueeSelectionMode.Polygon, snapClose.ClosedShape.Mode, "Poly2.ClosedModePolygon");
            AssertTrue(snapClose.ClosedShape.IsUsable(), "Poly2.ClosedUsable");
            AssertTrue(Math.Abs(snapClose.ClosedShape.AreaSqMeters - 400f) < 1f, "Poly2.ClosedArea400");
            AssertEqual(0, session.PolygonDraft.Count, "Poly2.DraftResetAfterSnapClose");
            AssertTrue(!session.IsBusy, "Poly2.NotBusyAfterClose");

            // Immediate click after closure starts Polygon #3 cleanly with no stale double-click or snap
            pClick = MakeStepInput(gate, ++frame, 7.1); // 0.1s after close!
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            pClick.CursorScreenX = 214f;
            pClick.CursorScreenY = 200f;
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, session.StepFrame(in pClick).Outcome, "Poly3.ImmediateStartAfterClose");
            AssertEqual(1, session.PolygonDraft.Count, "Poly3.Count1");

            // Add V1, V2, then double-click to close
            pClick = MakeStepInput(gate, ++frame, 7.8);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(10f, 0f);
            pClick.CursorScreenX = 280f;
            pClick.CursorScreenY = 200f;
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, session.StepFrame(in pClick).Outcome, "Poly3.AddV1");

            pClick = MakeStepInput(gate, ++frame, 8.4);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(10f, 10f);
            pClick.CursorScreenX = 280f;
            pClick.CursorScreenY = 280f;
            AssertEqual(ToolStepOutcome.PolygonAddedVertex, session.StepFrame(in pClick).Outcome, "Poly3.AddV2");

            // Double-click within 0.35s & 12px closes without requiring snap-to-start
            pClick = MakeStepInput(gate, ++frame, 8.65);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(10.2f, 10.2f);
            pClick.CursorScreenX = 288f;
            pClick.CursorScreenY = 286f; // sqrt(64+36)=10 <= 12
            pClick.HasStartScreen = true;
            pClick.StartScreenX = 214f;
            pClick.StartScreenY = 200f;
            ToolStepResult dblClose = session.StepFrame(in pClick);
            AssertEqual(ToolStepOutcome.CompletePolygonShape, dblClose.Outcome, "Poly3.DoubleClickCloses");
            AssertTrue(Math.Abs(dblClose.ClosedShape.AreaSqMeters - 50f) < 1f, "Poly3.ClosedArea50");
            AssertEqual(0, session.PolygonDraft.Count, "Poly3.DraftResetAfterDoubleClick");

            // 4. Enter closure with HasPending == true: closes polygon ONLY, does not ConfirmPending same frame!
            for (int v = 0; v < 3; v++)
            {
                pClick = MakeStepInput(gate, ++frame, 10.0 + v);
                pClick.LeftPressedThisFrame = true;
                pClick.HasPending = true;
                pClick.PressGroundWorldXZ = v == 0 ? new CoordXZ(0f, 0f) : (v == 1 ? new CoordXZ(12f, 0f) : new CoordXZ(0f, 12f));
                pClick.CursorScreenX = 100f + v * 50f;
                pClick.CursorScreenY = 100f;
                session.StepFrame(in pClick);
            }
            enterStep = MakeStepInput(gate, ++frame, 13.5);
            enterStep.HasPending = true;
            enterStep.ConfirmActionPressed = true;
            enterStep.EnterKeyPressed = true;
            ToolStepResult enterClose = session.StepFrame(in enterStep);
            AssertEqual(ToolStepOutcome.CompletePolygonShape, enterClose.Outcome, "Poly4.EnterClosesWithoutConfirmingPending");
            AssertTrue(enterClose.CanClosePolygonByEnter, "Poly4.CanCloseTrue");
            AssertTrue(!enterClose.CanConfirmPending, "Poly4.CanConfirmFalseDuringDraft");
            AssertEqual(0, session.PolygonDraft.Count, "Poly4.ResetAfterEnterClose");

            // Next idle frame with HasPending: ConfirmAction confirms Pending normally
            enterStep = MakeStepInput(gate, ++frame, 14.0);
            enterStep.HasPending = true;
            enterStep.ConfirmActionPressed = true;
            enterStep.EnterKeyPressed = true;
            AssertEqual(ToolStepOutcome.ConfirmPending, session.StepFrame(in enterStep).Outcome, "Poly4.NextFrameEnterConfirmsPending");

            // 5. Invalid (< 1m^2 collinear) closure leaves draft reset cleanly
            for (int v = 0; v < 3; v++)
            {
                pClick = MakeStepInput(gate, ++frame, 15.0 + v);
                pClick.LeftPressedThisFrame = true;
                pClick.PressGroundWorldXZ = new CoordXZ(v * 10f, 0f);
                pClick.CursorScreenX = 100f + v * 40f;
                pClick.CursorScreenY = 100f;
                session.StepFrame(in pClick);
            }
            enterStep = MakeStepInput(gate, ++frame, 18.5);
            enterStep.EnterKeyPressed = true;
            ToolStepResult invalidClose = session.StepFrame(in enterStep);
            AssertEqual(ToolStepOutcome.CompletePolygonShape, invalidClose.Outcome, "PolyInvalid.ClosesForFinalizeValidation");
            AssertTrue(!invalidClose.ClosedShape.IsUsable(), "PolyInvalid.ShapeNotUsable");
            AssertEqual(0, session.PolygonDraft.Count, "PolyInvalid.DraftCleanlyReset");

            // 6. 64-vertex cap + VertexLimitReached + Enter still closes all 64
            for (int i = 0; i < MarqueeShapeXZ.MaxPolygonVertices; i++)
            {
                double ang = (2.0 * Math.PI * i) / MarqueeShapeXZ.MaxPolygonVertices;
                pClick = MakeStepInput(gate, ++frame, 20.0 + i * 0.6);
                pClick.LeftPressedThisFrame = true;
                pClick.PressGroundWorldXZ = new CoordXZ((float)(30.0 * Math.Cos(ang)), (float)(30.0 * Math.Sin(ang)));
                pClick.CursorScreenX = 400f + (float)(120.0 * Math.Cos(ang));
                pClick.CursorScreenY = 400f + (float)(120.0 * Math.Sin(ang));
                ToolStepResult add64 = session.StepFrame(in pClick);
                AssertEqual(ToolStepOutcome.PolygonAddedVertex, add64.Outcome, "PolyCap.Add" + i);
            }
            AssertEqual(MarqueeShapeXZ.MaxPolygonVertices, session.PolygonDraft.Count, "PolyCap.Count64");

            pClick = MakeStepInput(gate, ++frame, 99.0);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            pClick.CursorScreenX = 350f; // > 14px away from start screen (520,400)
            pClick.CursorScreenY = 350f;
            pClick.HasStartScreen = true;
            pClick.StartScreenX = 520f;
            pClick.StartScreenY = 400f;
            AssertEqual(ToolStepOutcome.PolygonVertexLimitReached, session.StepFrame(in pClick).Outcome, "PolyCap.65thBlocked");
            AssertEqual(MarqueeShapeXZ.MaxPolygonVertices, session.PolygonDraft.Count, "PolyCap.Still64");

            enterStep = MakeStepInput(gate, ++frame, 100.0);
            enterStep.EnterKeyPressed = true;
            ToolStepResult capClose = session.StepFrame(in enterStep);
            AssertEqual(ToolStepOutcome.CompletePolygonShape, capClose.Outcome, "PolyCap.EnterCloses64");
            AssertEqual(MarqueeShapeXZ.MaxPolygonVertices, capClose.ClosedShape.VertexCount, "PolyCap.Closed64Vertices");
            AssertTrue(capClose.ClosedShape.IsUsable(), "PolyCap.ClosedUsable");

            // 7. RMB unwind chain (3 -> 2 -> 1 -> 0 -> RemoveEntityAtCursor -> ExitTool) + MouseOverUI
            for (int v = 0; v < 3; v++)
            {
                pClick = MakeStepInput(gate, ++frame, 110.0 + v);
                pClick.LeftPressedThisFrame = true;
                pClick.PressGroundWorldXZ = new CoordXZ(v == 1 ? 10f : 0f, v == 2 ? 10f : 0f);
                pClick.CursorScreenX = 100f + v * 50f;
                pClick.CursorScreenY = 100f;
                session.StepFrame(in pClick);
            }
            AssertEqual(3, session.PolygonDraft.Count, "Rmb.StartWith3");

            // Modal blocks RMB vertex unwind first
            var rmb = MakeStepInput(gate, ++frame, 115.0);
            rmb.RightPressedThisFrame = true;
            rmb.ImportantModalOpen = true;
            rmb.HasPending = true;
            AssertEqual(ToolStepOutcome.CloseImportantModalWithSound, session.StepFrame(in rmb).Outcome, "Rmb.ImportantModalFirst");
            AssertEqual(3, session.PolygonDraft.Count, "Rmb.ModalPreservedVertices");

            // Unwind 3 -> 2 -> 1 -> 0 (even when MouseOverUi is true, active draft still unwinds)
            rmb = MakeStepInput(gate, ++frame, 115.5);
            rmb.RightPressedThisFrame = true;
            rmb.MouseOverUi = true;
            rmb.HasPending = true;
            AssertEqual(ToolStepOutcome.RemovedPolygonVertex, session.StepFrame(in rmb).Outcome, "Rmb.Unwind3To2");
            AssertEqual(2, session.PolygonDraft.Count, "Rmb.Count2");

            rmb = MakeStepInput(gate, ++frame, 116.0);
            rmb.RightPressedThisFrame = true;
            rmb.HasPending = true;
            AssertEqual(ToolStepOutcome.RemovedPolygonVertex, session.StepFrame(in rmb).Outcome, "Rmb.Unwind2To1");
            AssertEqual(1, session.PolygonDraft.Count, "Rmb.Count1");

            rmb = MakeStepInput(gate, ++frame, 116.5);
            rmb.RightPressedThisFrame = true;
            rmb.HasPending = true;
            AssertEqual(ToolStepOutcome.RemovedPolygonVertex, session.StepFrame(in rmb).Outcome, "Rmb.Unwind1To0");
            AssertEqual(0, session.PolygonDraft.Count, "Rmb.Count0");

            // Idle RMB over UI is ignored (neither removes entity nor exits tool)
            rmb = MakeStepInput(gate, ++frame, 117.0);
            rmb.RightPressedThisFrame = true;
            rmb.MouseOverUi = true;
            rmb.HasPending = true;
            AssertEqual(ToolStepOutcome.None, session.StepFrame(in rmb).Outcome, "Rmb.OverUiIgnoredWhenIdle");

            // Idle RMB with Pending -> RemoveEntityAtCursor
            rmb = MakeStepInput(gate, ++frame, 117.5);
            rmb.RightPressedThisFrame = true;
            rmb.HasPending = true;
            AssertEqual(ToolStepOutcome.RemoveEntityAtCursor, session.StepFrame(in rmb).Outcome, "Rmb.IdleWithPendingRemovesEntity");

            // Idle RMB without Pending -> ExitTool
            rmb = MakeStepInput(gate, ++frame, 118.0);
            rmb.RightPressedThisFrame = true;
            rmb.HasPending = false;
            AssertEqual(ToolStepOutcome.ExitTool, session.StepFrame(in rmb).Outcome, "Rmb.IdleWithoutPendingExits");

            // 8. ESC priority chain, ColorModal + EscapeFrameGate, Focus Loss, Active Undo
            for (int v = 0; v < 3; v++)
            {
                pClick = MakeStepInput(gate, ++frame, 120.0 + v);
                pClick.LeftPressedThisFrame = true;
                pClick.PressGroundWorldXZ = new CoordXZ(v == 1 ? 10f : 0f, v == 2 ? 10f : 0f);
                pClick.CursorScreenX = 100f + v * 50f;
                pClick.CursorScreenY = 100f;
                session.StepFrame(in pClick);
            }

            // Focus loss (!AllowInput): ESC ignored AND PolygonDraft preserved
            var esc = MakeStepInput(gate, ++frame, 125.0, allowInput: false);
            esc.EscapePressedThisFrame = true;
            esc.HasPending = true;
            AssertEqual(ToolStepOutcome.None, session.StepFrame(in esc).Outcome, "Esc.FocusLossBlocked");
            AssertEqual(3, session.PolygonDraft.Count, "Esc.FocusLossPreservesPolyDraft");

            // ColorModal open + ESC: defers to UI without clearing PolygonDraft; EscapeFrameGate blocks double consume
            esc = MakeStepInput(gate, ++frame, 126.0);
            esc.EscapePressedThisFrame = true;
            esc.ColorModalOpen = true;
            esc.HasPending = true;
            AssertEqual(ToolStepOutcome.DeferredToUiColorModalEscape, session.StepFrame(in esc).Outcome, "Esc.DefersToColorModal");
            AssertEqual(3, session.PolygonDraft.Count, "Esc.ColorModalPreservesPolyDraft");

            AssertTrue(MarqueePointerLogic.TryConsumeUiColorModalEscape(gate, frame, true, true, false, true, true), "Esc.UiConsumesColorModal");
            esc = MakeStepInput(gate, frame, 126.0); // same frame after UI consumed
            esc.EscapePressedThisFrame = true;
            esc.ColorModalOpen = false;
            esc.HasPending = true;
            AssertEqual(ToolStepOutcome.None, session.StepFrame(in esc).Outcome, "Esc.SameFrameBlockedByGate");
            AssertEqual(3, session.PolygonDraft.Count, "Esc.SameFrameGatePreservesPolyDraft");

            // Next frame ESC clears ENTIRE PolygonDraft, preserving Pending
            esc = MakeStepInput(gate, ++frame, 127.0);
            esc.EscapePressedThisFrame = true;
            esc.HasPending = true;
            ToolStepResult escCancelPoly = session.StepFrame(in esc);
            AssertEqual(ToolStepOutcome.CancelledActiveInteraction, escCancelPoly.Outcome, "Esc.CancelsWholePolyDraft");
            AssertTrue(escCancelPoly.ShouldResetHighlights, "Esc.ResetsHighlights");
            AssertEqual(0, session.PolygonDraft.Count, "Esc.ClearsAllPolyVertices");

            // Second ESC clears Pending
            esc = MakeStepInput(gate, ++frame, 128.0);
            esc.EscapePressedThisFrame = true;
            esc.HasPending = true;
            AssertEqual(ToolStepOutcome.ClearSelectionWithSound, session.StepFrame(in esc).Outcome, "Esc.SecondEscClearsPending");

            // ActiveUndo aborts existing PolygonDraft AND blocks new clicks
            pClick = MakeStepInput(gate, ++frame, 130.0);
            pClick.LeftPressedThisFrame = true;
            pClick.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            session.StepFrame(in pClick);
            AssertEqual(1, session.PolygonDraft.Count, "UndoAbort.PolyDraft1");

            var undoStep = MakeStepInput(gate, ++frame, 131.0);
            undoStep.HasActiveUndo = true;
            undoStep.LeftPressedThisFrame = true;
            undoStep.PressGroundWorldXZ = new CoordXZ(10f, 0f);
            ToolStepResult undoRes = session.StepFrame(in undoStep);
            AssertEqual(ToolStepOutcome.None, undoRes.Outcome, "UndoAbort.BlocksNewClick");
            AssertTrue(undoRes.ShouldResetHighlights, "UndoAbort.ResetsHighlights");
            AssertEqual(0, session.PolygonDraft.Count, "UndoAbort.ClearsPolyDraft");

            // 9. Rectangle mode parity through real StepFrame (press/single-click/drag/fast-release/focus-abort)
            session.SetMode(MarqueeSelectionMode.Rectangle);
            var rectStep = MakeStepInput(gate, ++frame, 140.0);
            rectStep.LeftPressedThisFrame = true;
            rectStep.PressGroundWorldXZ = new CoordXZ(5f, 5f);
            rectStep.CursorScreenX = 300f;
            rectStep.CursorScreenY = 300f;
            AssertEqual(ToolStepOutcome.RectanglePressStarted, session.StepFrame(in rectStep).Outcome, "RectStep.PressStart");

            rectStep = MakeStepInput(gate, ++frame, 140.05);
            rectStep.CursorScreenX = 303f;
            rectStep.CursorScreenY = 303f;
            rectStep.LeftStillPressed = false; // 4.2px release -> single click add!
            AssertEqual(ToolStepOutcome.RectangleSingleClickAdd, session.StepFrame(in rectStep).Outcome, "RectStep.SingleClickAdd");
            AssertTrue(!session.IsBusy, "RectStep.IdleAfterSingleClick");

            // Press -> Fast release (> 7px on release frame) -> RectangleCompleteDrag
            rectStep = MakeStepInput(gate, ++frame, 141.0);
            rectStep.LeftPressedThisFrame = true;
            rectStep.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            rectStep.CursorScreenX = 100f;
            rectStep.CursorScreenY = 100f;
            AssertEqual(ToolStepOutcome.RectanglePressStarted, session.StepFrame(in rectStep).Outcome, "RectStep.FastPressStart");

            rectStep = MakeStepInput(gate, ++frame, 141.02);
            rectStep.CursorScreenX = 115f;
            rectStep.CursorScreenY = 100f;
            rectStep.LeftStillPressed = false;
            rectStep.CursorWorldXZ = new CoordXZ(25f, 25f);
            ToolStepResult fastReleaseRes = session.StepFrame(in rectStep);
            AssertEqual(ToolStepOutcome.RectangleCompleteDrag, fastReleaseRes.Outcome, "RectStep.FastReleaseCompletesDrag");
            AssertTrue(fastReleaseRes.ShouldClearEmptyNotice, "RectStep.FastReleaseClearsEmptyNotice");
            AssertTrue(!session.Dragging && !session.PointerPressed, "RectStep.FastReleaseEndsIdle");
            AssertEqual(25f, session.DragEndXZ.X, "RectStep.FastReleaseDragEndSynced");

            // Press -> Promote (>7px while held) -> Continue -> Cancel via focus loss -> ResetHighlights
            rectStep = MakeStepInput(gate, ++frame, 142.0);
            rectStep.LeftPressedThisFrame = true;
            rectStep.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            rectStep.CursorScreenX = 100f;
            rectStep.CursorScreenY = 100f;
            session.StepFrame(in rectStep);

            rectStep = MakeStepInput(gate, ++frame, 142.1);
            rectStep.CursorScreenX = 120f;
            rectStep.CursorScreenY = 120f;
            rectStep.LeftStillPressed = true;
            rectStep.CursorWorldXZ = new CoordXZ(15f, 15f);
            AssertEqual(ToolStepOutcome.RectanglePromotedToDrag, session.StepFrame(in rectStep).Outcome, "RectStep.Promoted");
            AssertTrue(session.Dragging, "RectStep.IsDragging");

            rectStep = MakeStepInput(gate, ++frame, 142.2);
            rectStep.AllowInput = false;
            rectStep.LeftStillPressed = true;
            ToolStepResult rectAbort = session.StepFrame(in rectStep);
            AssertTrue(rectAbort.ShouldResetHighlights, "RectStep.FocusLossResetsHighlights");
            AssertTrue(!session.Dragging && !session.PointerPressed, "RectStep.FocusLossStopsDrag");
        }

        private static void TestBatch5bFreehandScanlineAndAllModeTransitions()
        {
            var gate = new EscapeFrameGate();
            var session = new MarqueeToolSession();
            int frame = 1000;

            // 1. Freehand stroke start, >= 2.5m sampling debounce, release auto-close & DP simplify
            session.SetMode(MarqueeSelectionMode.Freehand);
            AssertEqual(MarqueeSelectionMode.Freehand, session.Mode, "Freehand.ModeSet");

            var fStep = MakeStepInput(gate, ++frame, 1.0);
            fStep.LeftPressedThisFrame = true;
            fStep.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            ToolStepResult fStart = session.StepFrame(in fStep);
            AssertEqual(ToolStepOutcome.FreehandStrokeStarted, fStart.Outcome, "Freehand.StrokeStart");
            AssertTrue(session.FreehandDrawing, "Freehand.IsDrawing");
            AssertTrue(session.IsMouseButtonActive, "Freehand.MouseButtonActive");
            AssertEqual(1, session.FreehandDraft.Count, "Freehand.InitialPointCount1");
            int rev1 = session.FreehandDraft.Revision;

            // Move < 2.5m (2.4m) while holding -> ignored
            fStep = MakeStepInput(gate, ++frame, 1.05);
            fStep.LeftStillPressed = true;
            fStep.CursorWorldXZ = new CoordXZ(2.4f, 0f);
            AssertEqual(ToolStepOutcome.FreehandStrokeContinue, session.StepFrame(in fStep).Outcome, "Freehand.ContinueUnder25");
            AssertEqual(1, session.FreehandDraft.Count, "Freehand.DebounceUnder25");
            AssertEqual(rev1, session.FreehandDraft.Revision, "Freehand.RevisionUnchangedUnder25");

            // Move >= 2.5m -> appends sampled points around a 120-sample circle (radius 40m -> 251m circumference / >80 samples)
            const int ringSteps = 100;
            for (int i = 1; i < ringSteps; i++)
            {
                double ang = (2.0 * Math.PI * i) / ringSteps;
                fStep = MakeStepInput(gate, ++frame, 1.1 + i * 0.02);
                fStep.LeftStillPressed = true;
                fStep.CursorWorldXZ = new CoordXZ((float)(40.0 * Math.Cos(ang)), (float)(40.0 * Math.Sin(ang)));
                AssertEqual(ToolStepOutcome.FreehandStrokeContinue, session.StepFrame(in fStep).Outcome, "Freehand.Sample" + i);
            }
            AssertTrue(session.FreehandDraft.Count > 64, "Freehand.SampledOver64RawPoints");
            int rawCaptured = session.FreehandDraft.Count;

            // Release LMB -> auto-closes & simplifies <= 64
            fStep = MakeStepInput(gate, ++frame, 4.0);
            fStep.LeftStillPressed = false;
            fStep.CursorWorldXZ = new CoordXZ(40f, -1f);
            ToolStepResult fDone = session.StepFrame(in fStep);
            AssertEqual(ToolStepOutcome.CompleteFreehandShape, fDone.Outcome, "Freehand.CompleteOnRelease");
            AssertEqual(MarqueeSelectionMode.Freehand, fDone.ClosedShape.Mode, "Freehand.ClosedModeFreehand");
            AssertTrue(fDone.ClosedShape.IsUsable(), "Freehand.ClosedUsable");
            AssertTrue(fDone.ClosedShape.VertexCount <= MarqueeShapeXZ.MaxPolygonVertices, "Freehand.CappedAt64");
            AssertEqual(rawCaptured, fDone.ClosedShape.OriginalVertexCount, "Freehand.OriginalVertexCountPreserved");
            AssertTrue(fDone.ClosedShape.WasVertexLimitApplied, "Freehand.SimplifiedFlagSet");
            AssertTrue(Math.Abs(fDone.ClosedShape.AreaSqMeters - 5026f) < 350f, "Freehand.CircleAreaApprox");
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft && !session.IsBusy, "Freehand.IdleAfterRelease");

            // 2. Degenerate straight-line stroke (< 1 m^2) discards cleanly & resets draft
            fStep = MakeStepInput(gate, ++frame, 5.0);
            fStep.LeftPressedThisFrame = true;
            fStep.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            AssertEqual(ToolStepOutcome.FreehandStrokeStarted, session.StepFrame(in fStep).Outcome, "FreehandDegen.Start");

            for (int k = 1; k <= 5; k++)
            {
                fStep = MakeStepInput(gate, ++frame, 5.0 + k * 0.05);
                fStep.LeftStillPressed = true;
                fStep.CursorWorldXZ = new CoordXZ(k * 4f, 0f);
                session.StepFrame(in fStep);
            }
            fStep = MakeStepInput(gate, ++frame, 5.5);
            fStep.LeftStillPressed = false;
            ToolStepResult fDegen = session.StepFrame(in fStep);
            AssertEqual(ToolStepOutcome.CompleteFreehandShape, fDegen.Outcome, "FreehandDegen.ReleaseEmitsShapeForValidation");
            AssertTrue(!fDegen.ClosedShape.IsUsable(), "FreehandDegen.NotUsableUnder1m2");
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft, "FreehandDegen.CleanReset");

            // 3. Exhaustive state-machine interruptions during Freehand drawing & cross-mode combinations
            void StartActiveFreehandStroke()
            {
                session.SetMode(MarqueeSelectionMode.Freehand);
                var startIn = MakeStepInput(gate, ++frame, 10.0);
                startIn.LeftPressedThisFrame = true;
                startIn.HasPending = true;
                startIn.PressGroundWorldXZ = new CoordXZ(0f, 0f);
                AssertEqual(ToolStepOutcome.FreehandStrokeStarted, session.StepFrame(in startIn).Outcome, "Helper.StartFreehand");
                var moveIn = MakeStepInput(gate, ++frame, 10.1);
                moveIn.LeftStillPressed = true;
                moveIn.HasPending = true;
                moveIn.CursorWorldXZ = new CoordXZ(15f, 0f);
                session.StepFrame(in moveIn);
                var moveIn2 = MakeStepInput(gate, ++frame, 10.2);
                moveIn2.LeftStillPressed = true;
                moveIn2.HasPending = true;
                moveIn2.CursorWorldXZ = new CoordXZ(15f, 15f);
                session.StepFrame(in moveIn2);
                AssertEqual(3, session.FreehandDraft.Count, "Helper.StrokeHas3");
            }

            // 3a. Freehand drawing + ESC -> CancelledActiveInteraction, preserves Pending
            StartActiveFreehandStroke();
            var interrupt = MakeStepInput(gate, ++frame, 11.0);
            interrupt.EscapePressedThisFrame = true;
            interrupt.HasPending = true;
            ToolStepResult escRes = session.StepFrame(in interrupt);
            AssertEqual(ToolStepOutcome.CancelledActiveInteraction, escRes.Outcome, "Freehand.EscCancelsStroke");
            AssertTrue(escRes.ShouldResetHighlights, "Freehand.EscResetsHighlights");
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft, "Freehand.EscClearsDraft");

            // 3b. Freehand drawing + RMB -> CancelledActiveInteraction (does NOT remove entity or exit tool!)
            StartActiveFreehandStroke();
            interrupt = MakeStepInput(gate, ++frame, 12.0);
            interrupt.RightPressedThisFrame = true;
            interrupt.HasPending = true;
            ToolStepResult rmbRes = session.StepFrame(in interrupt);
            AssertEqual(ToolStepOutcome.CancelledActiveInteraction, rmbRes.Outcome, "Freehand.RmbCancelsStroke");
            AssertTrue(rmbRes.ShouldResetHighlights, "Freehand.RmbResetsHighlights");
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft, "Freehand.RmbClearsDraft");

            // 3c. Freehand drawing + Focus Loss -> aborts safely, does not complete on return
            StartActiveFreehandStroke();
            interrupt = MakeStepInput(gate, ++frame, 13.0, allowInput: false);
            interrupt.LeftStillPressed = true;
            ToolStepResult focusRes = session.StepFrame(in interrupt);
            AssertEqual(ToolStepOutcome.None, focusRes.Outcome, "Freehand.FocusLossNoComplete");
            AssertTrue(focusRes.ShouldResetHighlights, "Freehand.FocusLossResetsHighlights");
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft, "Freehand.FocusLossClearsStroke");

            // Restore focus -> stays cleanly idle
            interrupt = MakeStepInput(gate, ++frame, 13.1, allowInput: true);
            interrupt.LeftStillPressed = false;
            AssertEqual(ToolStepOutcome.None, session.StepFrame(in interrupt).Outcome, "Freehand.FocusRestoreStaysIdle");

            // 3d. Freehand drawing + ColorModal Open -> aborts stroke safely
            StartActiveFreehandStroke();
            interrupt = MakeStepInput(gate, ++frame, 14.0);
            interrupt.ColorModalOpen = true;
            interrupt.LeftStillPressed = true;
            ToolStepResult colorRes = session.StepFrame(in interrupt);
            AssertEqual(ToolStepOutcome.None, colorRes.Outcome, "Freehand.ColorModalNoComplete");
            AssertTrue(colorRes.ShouldResetHighlights, "Freehand.ColorModalResetsHighlights");
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft, "Freehand.ColorModalClearsStroke");
            AssertTrue(!MarqueePointerLogic.CanCycleSelectionMode(true, true, false, true, false, false), "Mode.BlockedDuringColorModal");

            // 3e. Freehand drawing + Cycle/Switch mode -> SetMode cancels active stroke while keeping new mode clean
            StartActiveFreehandStroke();
            AssertTrue(!MarqueePointerLogic.CanCycleSelectionMode(true, true, session.IsMouseButtonActive, false, false, false), "Mode.HotkeyBlockedWhileDrawing");
            session.SetMode(MarqueeSelectionMode.Rectangle);
            AssertEqual(MarqueeSelectionMode.Rectangle, session.Mode, "Freehand.SwitchToRect");
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft && !session.IsBusy, "Freehand.SwitchModeClearsStroke");

            // 3f. Freehand drawing + Tool Switch (CancelActiveInteraction)
            StartActiveFreehandStroke();
            session.CancelActiveInteraction();
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft && !session.IsBusy, "Freehand.ToolSwitchClearsStroke");

            // 3g. Unclosed Polygon draft -> switch to Freehand -> clears Polygon draft, ready for Freehand
            session.SetMode(MarqueeSelectionMode.Polygon);
            fStep = MakeStepInput(gate, ++frame, 15.0);
            fStep.LeftPressedThisFrame = true;
            fStep.PressGroundWorldXZ = new CoordXZ(0f, 0f);
            session.StepFrame(in fStep);
            fStep = MakeStepInput(gate, ++frame, 15.5);
            fStep.LeftPressedThisFrame = true;
            fStep.PressGroundWorldXZ = new CoordXZ(10f, 0f);
            session.StepFrame(in fStep);
            AssertEqual(2, session.PolygonDraft.Count, "CrossMode.PolyDraft2BeforeSwitch");

            session.SetMode(MarqueeSelectionMode.Freehand);
            AssertEqual(MarqueeSelectionMode.Freehand, session.Mode, "CrossMode.SwitchedToFreehand");
            AssertEqual(0, session.PolygonDraft.Count, "CrossMode.PolyDraftClearedOnSwitch");

            fStep = MakeStepInput(gate, ++frame, 16.0);
            fStep.LeftPressedThisFrame = true;
            fStep.PressGroundWorldXZ = new CoordXZ(5f, 5f);
            AssertEqual(ToolStepOutcome.FreehandStrokeStarted, session.StepFrame(in fStep).Outcome, "CrossMode.FreehandStartsImmediately");

            // 3h. ActiveUndo aborts Freehand AND blocks starting new draw across all 3 modes
            var undoIn = MakeStepInput(gate, ++frame, 17.0);
            undoIn.HasActiveUndo = true;
            undoIn.LeftPressedThisFrame = true;
            ToolStepResult undoAbort = session.StepFrame(in undoIn);
            AssertTrue(undoAbort.ShouldResetHighlights, "Undo.AbortsFreehand");
            AssertTrue(!session.FreehandDrawing && !session.HasFreehandDraft, "Undo.ClearsFreehandDraft");
            AssertEqual(ToolStepOutcome.None, undoAbort.Outcome, "Undo.BlocksNewFreehand");

            session.SetMode(MarqueeSelectionMode.Polygon);
            undoIn = MakeStepInput(gate, ++frame, 17.1);
            undoIn.HasActiveUndo = true;
            undoIn.LeftPressedThisFrame = true;
            AssertEqual(ToolStepOutcome.None, session.StepFrame(in undoIn).Outcome, "Undo.BlocksPolygon");

            session.SetMode(MarqueeSelectionMode.Rectangle);
            undoIn = MakeStepInput(gate, ++frame, 17.2);
            undoIn.HasActiveUndo = true;
            undoIn.LeftPressedThisFrame = true;
            AssertEqual(ToolStepOutcome.None, session.StepFrame(in undoIn).Outcome, "Undo.BlocksRectangle");

            // 4. Scanline fill + 8m clipped grid + long-edge terrain subdivision verification
            var geom = new ShapeFillOverlayGeometry();

            // 4a. Convex 40x40 square -> integral of scanline widths ~ 1600 m^2, grid lines at 8,16,24,32
            var square40 = new System.Collections.Generic.List<CoordXZ>
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(40f, 0f),
                new CoordXZ(40f, 40f),
                new CoordXZ(0f, 40f),
            };
            ShapeOverlayBuilder.BuildFillAndGridSegments(square40, geom);
            AssertTrue(geom.FillSegments.Count >= 8, "Scanline.SquareHasBands");
            double scanArea = 0.0;
            for (int i = 0; i < geom.FillSegments.Count; i++)
            {
                SegmentXZ s = geom.FillSegments[i];
                scanArea += Math.Abs(s.B.X - s.A.X) * geom.FillBandWidth;
            }
            AssertTrue(Math.Abs(scanArea - 1600.0) < 60.0, "Scanline.SquareAreaMatches1600");
            // 40m edge split at <= 12m => 4 edges * 4 segments = 16 outline segments
            AssertEqual(16, geom.OutlineSegments.Count, "Subdivide.Square40x4Has16EdgeSegments");
            // 4 X-grid (8,16,24,32) + 4 Z-grid (8,16,24,32) = 8 grid segments
            AssertEqual(8, geom.GridSegments.Count, "Grid8m.Square40Has8Lines");

            // 4b. Concave L-shape -> no scanline or grid midpoint falls in void quadrant (25..40, 25..40)
            var concaveL = new System.Collections.Generic.List<CoordXZ>
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(40f, 0f),
                new CoordXZ(40f, 20f),
                new CoordXZ(20f, 20f),
                new CoordXZ(20f, 40f),
                new CoordXZ(0f, 40f),
            };
            MarqueeShapeXZ concaveShape = MarqueeShapeXZ.FromPolygon(concaveL, MarqueeSelectionMode.Polygon);
            ShapeOverlayBuilder.BuildFillAndGridSegments(concaveL, geom);
            AssertTrue(geom.FillSegments.Count > 0 && geom.GridSegments.Count > 0, "Scanline.ConcaveLNonEmpty");
            for (int i = 0; i < geom.FillSegments.Count; i++)
            {
                SegmentXZ s = geom.FillSegments[i];
                CoordXZ mid = new CoordXZ(0.5f * (s.A.X + s.B.X), 0.5f * (s.A.Z + s.B.Z));
                AssertTrue(concaveShape.ContainsPoint(mid), "Scanline.ConcaveMidInside_" + i);
            }
            for (int i = 0; i < geom.GridSegments.Count; i++)
            {
                SegmentXZ g = geom.GridSegments[i];
                CoordXZ mid = new CoordXZ(0.5f * (g.A.X + g.B.X), 0.5f * (g.A.Z + g.B.Z));
                AssertTrue(concaveShape.ContainsPoint(mid), "Grid8m.ConcaveMidInside_" + i);
            }

            // 4c. Self-intersecting bowtie -> Odd-Even scanline & 8m grid respect crossover void
            var bowtie = new System.Collections.Generic.List<CoordXZ>
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(40f, 40f),
                new CoordXZ(0f, 40f),
                new CoordXZ(40f, 0f),
            };
            MarqueeShapeXZ bowtieShape = MarqueeShapeXZ.FromPolygon(bowtie, MarqueeSelectionMode.Polygon);
            ShapeOverlayBuilder.BuildFillAndGridSegments(bowtie, geom);
            AssertTrue(geom.FillSegments.Count > 0 && geom.GridSegments.Count > 0, "Scanline.BowtieNonEmpty");
            for (int i = 0; i < geom.FillSegments.Count; i++)
            {
                SegmentXZ s = geom.FillSegments[i];
                CoordXZ mid = new CoordXZ(0.5f * (s.A.X + s.B.X), 0.5f * (s.A.Z + s.B.Z));
                AssertTrue(bowtieShape.ContainsPoint(mid), "Scanline.BowtieMidInside_" + i);
            }
            for (int i = 0; i < geom.GridSegments.Count; i++)
            {
                SegmentXZ g = geom.GridSegments[i];
                CoordXZ mid = new CoordXZ(0.5f * (g.A.X + g.B.X), 0.5f * (g.A.Z + g.B.Z));
                AssertTrue(bowtieShape.ContainsPoint(mid), "Grid8m.BowtieMidInside_" + i);
            }

            // 4d. Large polygon (240x240m) scales grid step smoothly (bounded <= 32 lines) and subdivides long edges
            var huge = new System.Collections.Generic.List<CoordXZ>
            {
                new CoordXZ(0f, 0f),
                new CoordXZ(240f, 0f),
                new CoordXZ(240f, 240f),
                new CoordXZ(0f, 240f),
            };
            ShapeOverlayBuilder.BuildFillAndGridSegments(huge, geom);
            AssertTrue(geom.GridSegments.Count > 0 && geom.GridSegments.Count <= 32, "Grid8m.LargeSpanAutoScaledBounded");
            AssertTrue(geom.OutlineSegments.Count >= 40, "Subdivide.Large240mEdgesSubdivided");

            // 5. Dual-tier overlay cache invalidation policy (2D XZ cached per shape; 3D Y refreshed on terrain hints / 0.5s)
            OverlayCacheRefreshDecision steady = ShapeOverlayCachePolicy.EvaluateRefresh(false, false, false, 10.2, 10.0);
            AssertTrue(!steady.Rebuild2DGeometry && !steady.Resample3DHeights, "CachePolicy.SteadySkipsBoth");

            OverlayCacheRefreshDecision sliceDirty = ShapeOverlayCachePolicy.EvaluateRefresh(false, false, true, 10.1, 10.0);
            AssertTrue(!sliceDirty.Rebuild2DGeometry && sliceDirty.Resample3DHeights, "CachePolicy.TerrainHintResamplesHeightsOnly");

            OverlayCacheRefreshDecision intervalExpired = ShapeOverlayCachePolicy.EvaluateRefresh(false, false, false, 10.5, 10.0);
            AssertTrue(!intervalExpired.Rebuild2DGeometry && intervalExpired.Resample3DHeights, "CachePolicy.IntervalResamplesHeightsOnly");

            OverlayCacheRefreshDecision heightStateFlipped = ShapeOverlayCachePolicy.EvaluateRefresh(false, true, false, 10.05, 10.0);
            AssertTrue(!heightStateFlipped.Rebuild2DGeometry && heightStateFlipped.Resample3DHeights, "CachePolicy.HeightAvailabilityFlipResamplesOnly");

            OverlayCacheRefreshDecision shapeChanged = ShapeOverlayCachePolicy.EvaluateRefresh(true, false, false, 10.05, 10.0);
            AssertTrue(shapeChanged.Rebuild2DGeometry && shapeChanged.Resample3DHeights, "CachePolicy.GeometryChangeRebuildsBoth");
        }

        private static Dictionary<string, string> ParseLocaleEntries(string sourceSection)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            var matches = System.Text.RegularExpressions.Regex.Matches(
                sourceSection,
                @"\{\s*([^,{}]+?)\s*,\s*""([^""]*)""\s*\}");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                string keyExpr = System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
                dict[keyExpr] = m.Groups[2].Value;
            }
            return dict;
        }

        private static void AssertLocaleEntryValid(
            Dictionary<string, string> dict,
            string localeTag,
            string keyExpr,
            string propertyOrActionName)
        {
            AssertTrue(dict.TryGetValue(keyExpr, out string val), $"HotkeyLoc.{localeTag}.Has({keyExpr})");
            if (val != null)
            {
                AssertTrue(!string.IsNullOrWhiteSpace(val), $"HotkeyLoc.{localeTag}.NonEmpty({keyExpr})");
                AssertTrue(!string.Equals(val, propertyOrActionName, StringComparison.Ordinal),
                    $"HotkeyLoc.{localeTag}.NotRawSymbol({keyExpr})");
                AssertTrue(!val.StartsWith("OPTION[", StringComparison.OrdinalIgnoreCase)
                    && !val.StartsWith("Options.", StringComparison.OrdinalIgnoreCase),
                    $"HotkeyLoc.{localeTag}.NotRawLocaleKey({keyExpr})");
            }
        }

        private static void TestHotkeyBindingsLocalizationAndUiIcons()
        {
            string baseDir = AppContext.BaseDirectory;
            string rootDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", ".."));
            string settingsPath = System.IO.Path.Combine(rootDir, "Settings.cs");
            string locPath = System.IO.Path.Combine(rootDir, "Localization.cs");
            string tsxPath = System.IO.Path.Combine(rootDir, "ui", "src", "mods", "bulldoze-marquee-section.tsx");

            AssertTrue(System.IO.File.Exists(settingsPath), "HotkeyLoc.SettingsExists");
            AssertTrue(System.IO.File.Exists(locPath), "HotkeyLoc.LocalizationExists");
            AssertTrue(System.IO.File.Exists(tsxPath), "UiIcons.TsxExists");

            string settingsSrc = System.IO.File.ReadAllText(settingsPath);
            var bindingMatches = System.Text.RegularExpressions.Regex.Matches(
                settingsSrc,
                @"\[SettingsUIKeyboardBinding\(([^)]+)\)\]\s*public\s+ProxyBinding\s+(\w+)\s*\{");
            AssertTrue(bindingMatches.Count >= 28, "HotkeyLoc.BindingPropertyCountAtLeast28");

            string locSrc = System.IO.File.ReadAllText(locPath);
            int zhStart = locSrc.IndexOf("public sealed class LocaleZH", StringComparison.Ordinal);
            AssertTrue(zhStart > 0, "HotkeyLoc.LocaleZHFound");

            Dictionary<string, string> enDict = ParseLocaleEntries(locSrc.Substring(0, zhStart));
            Dictionary<string, string> zhDict = ParseLocaleEntries(locSrc.Substring(zhStart));

            foreach (System.Text.RegularExpressions.Match bm in bindingMatches)
            {
                string attrArgs = bm.Groups[1].Value;
                string propName = bm.Groups[2].Value;
                var actionMatch = System.Text.RegularExpressions.Regex.Match(attrArgs, @"([A-Za-z_]\w*ActionName)");
                AssertTrue(actionMatch.Success, "HotkeyLoc.ActionConstMatched(" + propName + ")");
                if (!actionMatch.Success)
                {
                    continue;
                }
                string actionConst = actionMatch.Groups[1].Value;

                string labelKey = $"m_Settings.GetOptionLabelLocaleID(nameof(Settings.{propName}))";
                string descKey = $"m_Settings.GetOptionDescLocaleID(nameof(Settings.{propName}))";
                string bindingKey = $"m_Settings.GetBindingKeyLocaleID(Settings.{actionConst})";

                AssertLocaleEntryValid(enDict, "EN", labelKey, propName);
                AssertLocaleEntryValid(enDict, "EN", descKey, propName);
                AssertLocaleEntryValid(enDict, "EN", bindingKey, actionConst);

                AssertLocaleEntryValid(zhDict, "ZH", labelKey, propName);
                AssertLocaleEntryValid(zhDict, "ZH", descKey, propName);
                AssertLocaleEntryValid(zhDict, "ZH", bindingKey, actionConst);
            }

            string tsxSrc = System.IO.File.ReadAllText(tsxPath);
            AssertTrue(!tsxSrc.Contains("coui://uil", StringComparison.OrdinalIgnoreCase), "UiIcons.NoCouiUilInTsx");
            AssertTrue(System.IO.File.Exists(System.IO.Path.Combine(rootDir, "ui", "src", "mods", "confirm-icon.svg")), "UiIcons.ConfirmSvgExists");
            AssertTrue(System.IO.File.Exists(System.IO.Path.Combine(rootDir, "ui", "src", "mods", "lock-icon.svg")), "UiIcons.LockSvgExists");
            AssertTrue(System.IO.File.Exists(System.IO.Path.Combine(rootDir, "ui", "src", "mods", "star-all-icon.svg")), "UiIcons.StarAllSvgExists");

            string[] netSubProps = { "DeleteNetRoads", "DeleteNetTracks", "DeleteNetPipelines", "DeleteNetPowerLines", "DeleteNetOther" };
            foreach (string prop in netSubProps)
            {
                string labelKey = $"m_Settings.GetOptionLabelLocaleID(nameof(Settings.{prop}))";
                string descKey = $"m_Settings.GetOptionDescLocaleID(nameof(Settings.{prop}))";
                AssertLocaleEntryValid(enDict, "EN", labelKey, prop);
                AssertLocaleEntryValid(enDict, "EN", descKey, prop);
                AssertLocaleEntryValid(zhDict, "ZH", labelKey, prop);
                AssertLocaleEntryValid(zhDict, "ZH", descKey, prop);
            }
        }

        private static void TestBatch6aPresetsNetSubfiltersAndMatchSimilar()
        {
            // 1. Prefab-based network sub-kind classification (multi-component priority + Other fallback)
            AssertEqual(NetworkSubKind.Road,
                SelectionClassifier.ClassifyNetworkByPrefabFlags(true, false, false, false),
                "NetClassify.RoadOnly");
            AssertEqual(NetworkSubKind.Track,
                SelectionClassifier.ClassifyNetworkByPrefabFlags(false, true, false, false),
                "NetClassify.TrackOnly");
            AssertEqual(NetworkSubKind.Pipeline,
                SelectionClassifier.ClassifyNetworkByPrefabFlags(false, false, true, false),
                "NetClassify.PipelineOnly");
            AssertEqual(NetworkSubKind.PowerLine,
                SelectionClassifier.ClassifyNetworkByPrefabFlags(false, false, false, true),
                "NetClassify.PowerLineOnly");
            AssertEqual(NetworkSubKind.Road,
                SelectionClassifier.ClassifyNetworkByPrefabFlags(true, true, true, true),
                "NetClassify.MultiCompPriorityRoadOverTrackPipePower");
            AssertEqual(NetworkSubKind.Track,
                SelectionClassifier.ClassifyNetworkByPrefabFlags(false, true, true, false),
                "NetClassify.MultiCompPriorityTrackOverPipe");
            AssertEqual(NetworkSubKind.Pipeline,
                SelectionClassifier.ClassifyNetworkByPrefabFlags(false, false, true, true),
                "NetClassify.MultiCompPriorityPipeOverPower");
            AssertEqual(NetworkSubKind.Other,
                SelectionClassifier.ClassifyNetworkByPrefabFlags(false, false, false, false),
                "NetClassify.UnknownPrefabFallbackOther");

            // 2. Network mask evaluation (legacy mask without subfiltersSpecified + explicit subfilters + independent NetworkOther)
            AssertTrue(SelectionClassifier.IsNetworkMaskEnabled(SelectionFilterMask.Networks, NetworkSubKind.Road),
                "NetMask.LegacyNetworksEnablesRoad");
            AssertTrue(SelectionClassifier.IsNetworkMaskEnabled(SelectionFilterMask.Networks, NetworkSubKind.Pipeline),
                "NetMask.LegacyNetworksEnablesPipeline");
            AssertTrue(SelectionClassifier.IsNetworkMaskEnabled(SelectionFilterMask.Networks, NetworkSubKind.Other),
                "NetMask.LegacyNetworksEnablesOther");
            AssertTrue(SelectionClassifier.IsNetworkMaskEnabled(SelectionFilterMask.All, NetworkSubKind.Other),
                "NetMask.LegacyAllEnablesOther");
            AssertTrue(!SelectionClassifier.IsNetworkMaskEnabled(SelectionFilterMask.None, NetworkSubKind.Road),
                "NetMask.MasterOffDisablesAll");
            AssertTrue(!SelectionClassifier.IsNetworkMaskEnabled(SelectionFilterMask.None, NetworkSubKind.Other),
                "NetMask.MasterOffDisablesOther");

            SelectionFilterMask pipeOnlyMask =
                SelectionFilterMask.Networks | SelectionFilterMask.NetworkSubfiltersSpecified | SelectionFilterMask.NetworkPipelines;
            AssertTrue(SelectionClassifier.IsNetworkMaskEnabled(pipeOnlyMask, NetworkSubKind.Pipeline),
                "NetMask.SubfilterEnablesPipeline");
            AssertTrue(!SelectionClassifier.IsNetworkMaskEnabled(pipeOnlyMask, NetworkSubKind.Road),
                "NetMask.SubfilterBlocksRoad");
            AssertTrue(!SelectionClassifier.IsNetworkMaskEnabled(pipeOnlyMask, NetworkSubKind.Other),
                "NetMask.PartialSubfilterBlocksOther");

            // Four original sub-types on, NetworkOther off -> Other is independently excluded
            SelectionFilterMask fourOnOtherOff =
                SelectionFilterMask.Networks | SelectionFilterMask.NetworkSubfiltersSpecified |
                SelectionFilterMask.NetworkRoads | SelectionFilterMask.NetworkTracks |
                SelectionFilterMask.NetworkPipelines | SelectionFilterMask.NetworkPowerLines;
            AssertTrue(!SelectionClassifier.IsNetworkMaskEnabled(fourOnOtherOff, NetworkSubKind.Other),
                "NetMask.OtherExcludedWhenNetworkOtherUnchecked");

            // NetworkOther only -> Other enabled while Road/Track/Pipeline/PowerLine remain excluded
            SelectionFilterMask otherOnlyMask =
                SelectionFilterMask.Networks | SelectionFilterMask.NetworkSubfiltersSpecified | SelectionFilterMask.NetworkOther;
            AssertTrue(SelectionClassifier.IsNetworkMaskEnabled(otherOnlyMask, NetworkSubKind.Other),
                "NetMask.NetworkOtherEnablesOtherOnly");
            AssertTrue(!SelectionClassifier.IsNetworkMaskEnabled(otherOnlyMask, NetworkSubKind.Road),
                "NetMask.NetworkOtherOnlyExcludesRoad");

            SelectionFilterMask allNetSubMask =
                SelectionFilterMask.Networks | SelectionFilterMask.NetworkSubfiltersSpecified | SelectionFilterMask.AllNetworkSubtypes;
            AssertTrue(SelectionClassifier.IsNetworkMaskEnabled(allNetSubMask, NetworkSubKind.Other),
                "NetMask.AllSubtypesIncludesNetworkOther");

            SelectionFilterMask masterOffWithSub =
                SelectionFilterMask.NetworkSubfiltersSpecified | SelectionFilterMask.AllNetworkSubtypes;
            AssertTrue(!SelectionClassifier.IsNetworkMaskEnabled(masterOffWithSub, NetworkSubKind.Road),
                "NetMask.MasterOffOverridesSubfilters");
            AssertTrue(!SelectionClassifier.IsNetworkMaskEnabled(masterOffWithSub, NetworkSubKind.Other),
                "NetMask.MasterOffOverridesNetworkOther");

            // 3. Built-in presets & FilterPresetValues v1/v2 serialize/deserialize round-trip
            AssertTrue(FilterPresetValues.TryGetBuiltIn("greenery", out FilterPresetValues greenery, out bool greeneryUg),
                "Preset.BuiltInGreenery");
            AssertTrue(greenery.Trees && !greenery.Buildings && !greenery.Networks && greenery.NetOther && !greeneryUg,
                "Preset.GreeneryFlags");

            AssertTrue(FilterPresetValues.TryGetBuiltIn("buildings", out FilterPresetValues bld, out bool bldUg),
                "Preset.BuiltInBuildings");
            AssertTrue(bld.Buildings && bld.Residential && bld.Commercial && bld.Industrial && bld.Office && bld.NetOther && !bld.Trees && !bldUg,
                "Preset.BuildingsFlags");

            AssertTrue(FilterPresetValues.TryGetBuiltIn("surfaces", out FilterPresetValues surf, out bool surfUg),
                "Preset.BuiltInSurfaces");
            AssertTrue(surf.Areas && !surf.Decals && !surf.Buildings && surf.NetOther && !surfUg,
                "Preset.SurfacesFlags");

            AssertTrue(FilterPresetValues.TryGetBuiltIn("underground_utilities", out FilterPresetValues ugUtil, out bool ugUtilPrefers),
                "Preset.BuiltInUndergroundUtilities");
            AssertTrue(ugUtil.Networks && ugUtil.NetPipelines && ugUtil.NetPowerLines && !ugUtil.NetRoads && !ugUtil.NetTracks && !ugUtil.NetOther && ugUtilPrefers,
                "Preset.UndergroundUtilitiesFlags");
            AssertTrue(!FilterPresetValues.TryGetBuiltIn("unknown_id", out _, out _),
                "Preset.UnknownBuiltInReturnsFalse");

            // v2 round-trip: NetOther = false (underground utilities) and NetOther = true (custom all-nets)
            string serializedUg = ugUtil.Serialize();
            AssertTrue(serializedUg.StartsWith("v2:", StringComparison.Ordinal), "Preset.SerializeUsesV2");
            AssertTrue(FilterPresetValues.TryDeserialize(serializedUg, out FilterPresetValues deserializedUg),
                "Preset.DeserializeV2NetOtherFalse");
            AssertTrue(ugUtil.Equals(deserializedUg) && ugUtil.MatchesActiveMask(in deserializedUg) && !deserializedUg.NetOther,
                "Preset.RoundTripV2NetOtherFalse");

            string serializedSurf = surf.Serialize();
            AssertTrue(FilterPresetValues.TryDeserialize(serializedSurf, out FilterPresetValues deserializedSurf),
                "Preset.DeserializeV2NetOtherTrue");
            AssertTrue(surf.Equals(deserializedSurf) && surf.MatchesActiveMask(in deserializedSurf) && deserializedSurf.NetOther,
                "Preset.RoundTripV2NetOtherTrue");

            // Backward compatibility: legacy v1:<bits> without bit 15 must deserialize cleanly and default NetOther = true
            int v1Bits = (1 << 5) | (1 << 9) | (1 << 11) | (1 << 12); // Trees + Networks + NetRoads + NetTracks
            AssertTrue(FilterPresetValues.TryDeserialize("v1:" + v1Bits, out FilterPresetValues fromV1),
                "Preset.DeserializeLegacyV1Valid");
            AssertTrue(fromV1.Trees && fromV1.Networks && fromV1.NetRoads && fromV1.NetTracks && !fromV1.NetPipelines && !fromV1.NetPowerLines && fromV1.NetOther,
                "Preset.LegacyV1DefaultsNetOtherTrue");

            // v1 with 0 bits still defaults NetOther = true while preserving all other fields false
            AssertTrue(FilterPresetValues.TryDeserialize("v1:0", out FilterPresetValues fromV1Zero),
                "Preset.DeserializeLegacyV1Zero");
            AssertTrue(!fromV1Zero.Buildings && !fromV1Zero.Networks && fromV1Zero.NetOther,
                "Preset.LegacyV1ZeroDefaultsNetOtherTrue");

            AssertTrue(!FilterPresetValues.TryDeserialize("", out _), "Preset.DeserializeEmptyRejected");
            AssertTrue(!FilterPresetValues.TryDeserialize("v3:123", out _), "Preset.DeserializeBadPrefixRejected");
            AssertTrue(!FilterPresetValues.TryDeserialize("v2:-1", out _), "Preset.DeserializeNegativeRejected");
            AssertTrue(!FilterPresetValues.TryDeserialize("v1:abc", out _), "Preset.DeserializeNonIntRejected");

            // 4. Underground view link tracker (auto switch on underground_utilities + user override priority)
            var tracker = new PresetViewLinkTracker();
            bool view = false;
            view = tracker.ResolveViewForPreset(true, view);
            AssertTrue(view && tracker.HasPresetViewChange && !tracker.PreviousUnderground,
                "ViewLink.SwitchesGroundToUnderground");

            // Leaving underground preset restores previous ground view
            view = tracker.ResolveViewForPreset(false, view);
            AssertTrue(!view && !tracker.HasPresetViewChange,
                "ViewLink.RestoresGroundWhenLeavingPreset");

            // User manual toggle while on preset clears link -> leaving preset must respect user's view choice
            view = tracker.ResolveViewForPreset(true, false);
            AssertTrue(view && tracker.HasPresetViewChange, "ViewLink.AutoSwitchedAgain");
            tracker.OnUserManualViewToggle();
            view = false; // user toggled back to surface manually
            AssertTrue(!tracker.HasPresetViewChange, "ViewLink.ManualToggleClearsLink");
            view = tracker.ResolveViewForPreset(false, view);
            AssertTrue(!view, "ViewLink.RespectsUserManualSurfaceAfterPresetExit");

            // User toggled manually from ground -> preset underground -> stays underground if user manually toggled while underground
            view = tracker.ResolveViewForPreset(true, false);
            tracker.OnUserManualViewToggle();
            view = tracker.ResolveViewForPreset(false, view);
            AssertTrue(view, "ViewLink.RespectsUserManualKeepUnderground");

            // Already underground when preset applied -> does NOT record preset change
            tracker.Reset();
            view = tracker.ResolveViewForPreset(true, true);
            AssertTrue(view && !tracker.HasPresetViewChange, "ViewLink.AlreadyUndergroundNotRecordedAsPresetChange");

            // 5. Match Similar double-key filtering (PrefabId + Category)
            var candidates = new[]
            {
                new SimilarKeyCandidate { PrefabId = 0, Category = SelectionCategory.Tree },
                new SimilarKeyCandidate { PrefabId = 100, Category = SelectionCategory.Area },
                new SimilarKeyCandidate { PrefabId = 100, Category = SelectionCategory.Decal },
                new SimilarKeyCandidate { PrefabId = 0, Category = SelectionCategory.Tree },
                new SimilarKeyCandidate { PrefabId = 200, Category = SelectionCategory.Building },
            };

            List<int> zeroPrefabMatches = SelectionMatchLogic.FilterIndicesBySimilarKey(
                candidates, 0, SelectionCategory.Tree, out MatchSimilarOutcome outcomeZero);
            AssertEqual(MatchSimilarOutcome.Matched, outcomeZero, "MatchSimilar.PrefabZeroValid");
            AssertEqual(2, zeroPrefabMatches.Count, "MatchSimilar.PrefabZeroMatchCount");
            AssertTrue(zeroPrefabMatches[0] == 0 && zeroPrefabMatches[1] == 3, "MatchSimilar.PrefabZeroIndices");

            List<int> doubleKeyArea = SelectionMatchLogic.FilterIndicesBySimilarKey(
                candidates, 100, SelectionCategory.Area, out MatchSimilarOutcome outcomeArea);
            AssertEqual(MatchSimilarOutcome.Matched, doubleKeyArea.Count == 1 ? outcomeArea : MatchSimilarOutcome.None,
                "MatchSimilar.DoubleKeySeparatesAreaFromDecal");
            AssertEqual(1, doubleKeyArea[0], "MatchSimilar.DoubleKeyExactIndex");

            List<int> noMatch = SelectionMatchLogic.FilterIndicesBySimilarKey(
                candidates, 999, SelectionCategory.Building, out MatchSimilarOutcome outcomeNoMatch);
            AssertEqual(MatchSimilarOutcome.NoMatchesInSelection, outcomeNoMatch, "MatchSimilar.NoMatchOutcome");
            AssertEqual(0, noMatch.Count, "MatchSimilar.NoMatchCountZero");

            List<int> noPrefab = SelectionMatchLogic.FilterIndicesBySimilarKey(
                candidates, -1, SelectionCategory.Tree, out MatchSimilarOutcome outcomeNoPrefab);
            AssertEqual(MatchSimilarOutcome.NoPrefab, outcomeNoPrefab, "MatchSimilar.NegativePrefabRejected");

            // 6. MarqueeToolSession one-shot PickSimilarArmed across Rectangle / Polygon / Freehand & ESC/RMB cancel
            var gate = new EscapeFrameGate();
            var session = new MarqueeToolSession();
            int f = 2000;

            session.PickSimilarArmed = true;
            var clickStep = MakeStepInput(gate, ++f, 10.0);
            clickStep.HasPending = true;
            clickStep.LeftPressedThisFrame = true;
            clickStep.HasCursorWorld = false; // UI-driven or raycast pick should still emit PickSimilarAtCursor
            ToolStepResult pickRes = session.StepFrame(in clickStep);
            AssertEqual(ToolStepOutcome.PickSimilarAtCursor, pickRes.Outcome, "PickSimilar.ClickEmitsOutcomeEvenInRect");
            AssertTrue(!session.PickSimilarArmed, "PickSimilar.AutoDisarmsAfterOneShot");

            // ESC cancels PickSimilarArmed without clearing Pending
            session.PickSimilarArmed = true;
            var escStep = MakeStepInput(gate, ++f, 11.0);
            escStep.HasPending = true;
            escStep.EscapePressedThisFrame = true;
            AssertEqual(ToolStepOutcome.CancelPickSimilarWithSound, session.StepFrame(in escStep).Outcome,
                "PickSimilar.EscCancelsArmedOnly");
            AssertTrue(!session.PickSimilarArmed, "PickSimilar.EscDisarms");

            // RMB cancels PickSimilarArmed without exiting tool or clearing Pending
            session.SetMode(MarqueeSelectionMode.Polygon);
            session.PickSimilarArmed = true;
            var rmbStep = MakeStepInput(gate, ++f, 12.0);
            rmbStep.HasPending = true;
            rmbStep.RightPressedThisFrame = true;
            AssertEqual(ToolStepOutcome.CancelPickSimilarWithSound, session.StepFrame(in rmbStep).Outcome,
                "PickSimilar.RmbCancelsArmedOnlyInPolygon");
            AssertTrue(!session.PickSimilarArmed, "PickSimilar.RmbDisarms");

            // SetMode & !HasPending clear PickSimilarArmed
            session.PickSimilarArmed = true;
            session.SetMode(MarqueeSelectionMode.Freehand);
            AssertTrue(!session.PickSimilarArmed, "PickSimilar.SetModeClearsArmed");

            session.PickSimilarArmed = true;
            var noPendingStep = MakeStepInput(gate, ++f, 13.0);
            noPendingStep.HasPending = false;
            session.StepFrame(in noPendingStep);
            AssertTrue(!session.PickSimilarArmed, "PickSimilar.NoPendingClearsArmed");

            // Active polygon draft disarms / blocks PickSimilarArmed so both intents cannot coexist
            session.SetMode(MarqueeSelectionMode.Polygon);
            var polyVertexStep = MakeStepInput(gate, ++f, 14.0);
            polyVertexStep.HasPending = true;
            polyVertexStep.LeftPressedThisFrame = true;
            polyVertexStep.PressGroundWorldXZ = new CoordXZ(10f, 10f);
            session.StepFrame(in polyVertexStep);
            AssertTrue(session.HasPolygonDraft, "PickSimilar.PolyDraftActive");
            session.PickSimilarArmed = true;
            var guardStep = MakeStepInput(gate, ++f, 15.0);
            guardStep.HasPending = true;
            session.StepFrame(in guardStep);
            AssertTrue(!session.PickSimilarArmed, "PickSimilar.PolygonDraftDisarmsPickSimilar");
        }

        private static void TestBatch7PureLogicHistoryImpactSoloAndGates()
        {
            // 1. SelectionHistoryPolicy.IsEquivalent: Rectangles at different locations with same dimensions
            var rect1 = MarqueeShapeXZ.FromPoints(new CoordXZ(0f, 0f), new CoordXZ(10f, 10f), new CoordXZ(0f, 1f), false);
            var rect2 = MarqueeShapeXZ.FromPoints(new CoordXZ(100f, 100f), new CoordXZ(110f, 110f), new CoordXZ(0f, 1f), false);
            AssertTrue(!SelectionHistoryPolicy.IsEquivalent(rect1, rect2), "History.DiffLocationSameSizeNotEquivalent");

            var rect3 = MarqueeShapeXZ.FromPoints(new CoordXZ(0f, 0f), new CoordXZ(10f, 10f), new CoordXZ(0f, 1f), false);
            AssertTrue(SelectionHistoryPolicy.IsEquivalent(rect1, rect3), "History.SameLocationIsEquivalent");

            // Opposite drag direction (p0=start, p2=end)
            var rectOpposite = MarqueeShapeXZ.FromPoints(new CoordXZ(10f, 10f), new CoordXZ(0f, 0f), new CoordXZ(0f, 1f), false);
            AssertTrue(SelectionHistoryPolicy.IsEquivalent(rect1, rectOpposite), "History.OppositeDragDirectionEquivalent");

            // 2. Polygon equivalence with cyclic shifts and reverse winding
            var poly1 = MarqueeShapeXZ.FromPolygon(new[]
            {
                new CoordXZ(0f, 0f), new CoordXZ(10f, 0f), new CoordXZ(10f, 10f), new CoordXZ(0f, 10f)
            }, MarqueeSelectionMode.Polygon);
            var poly2 = MarqueeShapeXZ.FromPolygon(new[]
            {
                new CoordXZ(50f, 50f), new CoordXZ(60f, 50f), new CoordXZ(60f, 60f), new CoordXZ(50f, 60f)
            }, MarqueeSelectionMode.Polygon);
            AssertTrue(!SelectionHistoryPolicy.IsEquivalent(poly1, poly2), "History.DiffLocationPolygonsNotEquivalent");

            var polyShift = MarqueeShapeXZ.FromPolygon(new[]
            {
                new CoordXZ(10f, 0f), new CoordXZ(10f, 10f), new CoordXZ(0f, 10f), new CoordXZ(0f, 0f)
            }, MarqueeSelectionMode.Polygon);
            AssertTrue(SelectionHistoryPolicy.IsEquivalent(poly1, polyShift), "History.CyclicShiftPolygonEquivalent");

            // 3. SelectionHistoryPolicy.PushHistory: 5 cap limit and deduplication
            var history = new List<MarqueeShapeXZ>();
            for (int i = 0; i < 6; i++)
            {
                var s = MarqueeShapeXZ.FromPoints(new CoordXZ(i * 20f, 0f), new CoordXZ(i * 20f + 10f, 10f), new CoordXZ(0f, 1f), false);
                SelectionHistoryPolicy.PushHistory(history, s, 5);
            }
            AssertEqual(5, history.Count, "History.MaxCap5");
            AssertTrue(history[0].MinX >= 19f && history[0].MinX <= 21f, "History.Oldest0Dropped");
            AssertTrue(history[4].MinX >= 99f && history[4].MinX <= 101f, "History.Newest5AtEnd");

            var duplicate = history[1]; // X around 40
            SelectionHistoryPolicy.PushHistory(history, duplicate, 5);
            AssertEqual(5, history.Count, "History.DedupMaintainsCap5");
            AssertTrue(SelectionHistoryPolicy.IsEquivalent(duplicate, history[4]), "History.DuplicatePromotedToTop");

            // 4. RestoreSelectionGate.CanRestore permission gate & execution policy
            AssertTrue(RestoreSelectionGate.CanRestore(true, false, false, false, false, false, false, false, 3), "Gate.Valid");
            AssertTrue(!RestoreSelectionGate.CanRestore(false, false, false, false, false, false, false, false, 3), "Gate.ToolInactiveRejected");
            AssertTrue(!RestoreSelectionGate.CanRestore(true, false, false, false, false, false, false, false, 0), "Gate.HistoryEmptyRejected");
            AssertTrue(!RestoreSelectionGate.CanRestore(true, true, false, false, false, false, false, false, 3), "Gate.ActiveUndoRejected");
            AssertTrue(!RestoreSelectionGate.CanRestore(true, false, true, false, false, false, false, false, 3), "Gate.PointerBusyRejected");
            AssertTrue(!RestoreSelectionGate.CanRestore(true, false, false, true, false, false, false, false, 3), "Gate.MouseButtonBusyRejected");
            AssertTrue(!RestoreSelectionGate.CanRestore(true, false, false, false, true, false, false, false, 3), "Gate.ImportantModalRejected");
            AssertTrue(!RestoreSelectionGate.CanRestore(true, false, false, false, false, true, false, false, 3), "Gate.ColorModalRejected");
            AssertTrue(!RestoreSelectionGate.CanRestore(true, false, false, false, false, false, true, false, 3), "Gate.ActiveDraftRejected");
            AssertTrue(!RestoreSelectionGate.CanRestore(true, false, false, false, false, false, false, true, 3), "Gate.QueuedDeletionRejected");

            // Regression test: restoring history must NEVER auto-delete objects even when confirmBeforeDelete is false and count <= threshold
            AssertTrue(!SelectionRestoreExecutionPolicy.ShouldExecuteAutoDelete(allowAutoDelete: false, count: 5, importantCount: 0, confirmBeforeDelete: false, threshold: 50), "HistoryRestore.AutoDeleteDisabled");
            AssertTrue(!SelectionRestoreExecutionPolicy.ShouldExecuteAutoDelete(allowAutoDelete: false, count: 1, importantCount: 0, confirmBeforeDelete: false, threshold: 50), "HistoryRestore.SingleItemNeverAutoDeleted");
            AssertTrue(SelectionRestoreExecutionPolicy.ShouldExecuteAutoDelete(allowAutoDelete: true, count: 5, importantCount: 0, confirmBeforeDelete: false, threshold: 50), "DrawingComplete.AutoDeleteAllowed");
            AssertTrue(!SelectionRestoreExecutionPolicy.ShouldExecuteAutoDelete(allowAutoDelete: true, count: 5, importantCount: 0, confirmBeforeDelete: true, threshold: 50), "DrawingComplete.ConfirmModeBlocksAutoDelete");

            // 5. ImpactReportLogic.BuildReport: sorting, ties, and >12 truncation
            AssertEqual(string.Empty, ImpactReportLogic.BuildReport(null), "Impact.NullInputEmpty");
            AssertEqual(string.Empty, ImpactReportLogic.BuildReport(new Dictionary<string, int>()), "Impact.EmptyDictEmpty");

            var smallGroup = new Dictionary<string, int>
            {
                { "OakTree", 5 },
                { "PineTree", 12 },
                { "AppleTree", 5 },
            };
            string rep = ImpactReportLogic.BuildReport(smallGroup);
            AssertEqual("PineTree|12\nAppleTree|5\nOakTree|5", rep, "Impact.SortedDescThenAlpha");

            var bigGroup = new Dictionary<string, int>();
            for (int i = 1; i <= 15; i++)
            {
                bigGroup[$"Prefab_{i:D2}"] = i;
            }
            string bigRep = ImpactReportLogic.BuildReport(bigGroup, 12);
            string[] lines = bigRep.Split('\n');
            AssertEqual(13, lines.Length, "Impact.Top12PlusMoreCount");
            AssertEqual("Prefab_15|15", lines[0], "Impact.HighestFirst");
            // Items 01 (1) + 02 (2) + 03 (3) = 6
            AssertEqual("MORE|6", lines[12], "Impact.MoreSumCorrect");

            // 6. SoloFilterPolicy.CalculateSolo
            var soloRes = SoloFilterPolicy.CalculateSolo("residential");
            AssertTrue(soloRes.Residential && !soloRes.Commercial && !soloRes.Trees && !soloRes.Networks, "Solo.ResOnly");

            var soloTrees = SoloFilterPolicy.CalculateSolo("trees");
            AssertTrue(soloTrees.Trees && !soloTrees.Residential && !soloTrees.Props, "Solo.TreesOnly");

            var soloRoads = SoloFilterPolicy.CalculateSolo("netroads");
            AssertTrue(soloRoads.Networks && soloRoads.NetRoads && !soloRoads.NetTracks && !soloRoads.NetPipelines, "Solo.NetRoadsEnablesNetworks");

            var soloTracks = SoloFilterPolicy.CalculateSolo("nettracks");
            AssertTrue(soloTracks.Networks && soloTracks.NetTracks && !soloTracks.NetRoads, "Solo.NetTracksEnablesNetworks");

            var soloPipes = SoloFilterPolicy.CalculateSolo("netpipelines");
            AssertTrue(soloPipes.Networks && soloPipes.NetPipelines && !soloPipes.NetRoads, "Solo.NetPipelinesEnablesNetworks");

            var soloPower = SoloFilterPolicy.CalculateSolo("netpowerlines");
            AssertTrue(soloPower.Networks && soloPower.NetPowerLines && !soloPower.NetRoads, "Solo.NetPowerLinesEnablesNetworks");

            var soloOther = SoloFilterPolicy.CalculateSolo("netother");
            AssertTrue(soloOther.Networks && soloOther.NetOther && !soloOther.NetRoads, "Solo.NetOtherEnablesNetworks");

            var soloBuildings = SoloFilterPolicy.CalculateSolo("buildings");
            AssertTrue(soloBuildings.Buildings && !soloBuildings.Residential, "Solo.BuildingsOnly");

            var soloNone = SoloFilterPolicy.CalculateSolo(null);
            AssertTrue(!soloNone.Residential && !soloNone.Networks && !soloNone.Trees, "Solo.NullAllOff");
        }
    }
}
