using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Auto-mirror across the road's centerline. Rooms.Pads() run along +X, so the plane is z = 0.</summary>
    public class MirrorAssistTests
    {
        const double Top = Rooms.Top;
        static Vec3 P(double x, double y, double z) => new Vec3(x, y, z);
        static MirrorPlane Plane() { var pads = Rooms.Pads(); return MirrorPlane.Centerline(pads[0], pads[1]); }

        static void AssertClose(Vec3 expected, Vec3 actual) => Assert.True(Vec3.Distance(expected, actual) < 1e-9, $"{actual} != {expected}");

        [Fact]
        public void Centerline_RunsBetweenThePadsAndReflectsAcrossTheRoad()
        {
            var plane = Plane();
            Assert.Equal(0, plane.SignedDistance(P(0, Top, 0)), 9);
            Assert.Equal(0, plane.SignedDistance(P(1, Top, 0)), 9);
            Assert.Equal(0, plane.SignedDistance(P(0.5, Top + 0.4, 0)), 9);   // upright
            AssertClose(P(0.3, 1.2, -0.1), plane.Reflect(P(0.3, 1.2, 0.1)));
            AssertClose(P(0.3, 1.2, 0.1), plane.Reflect(plane.Reflect(P(0.3, 1.2, 0.1))));
        }

        [Fact]
        public void Rail_MirrorsToTheOtherSideInTheSameDirection()
        {
            var copy = MirrorAssist.Mirror(new[] { P(0, Top, 0.1), P(1, Top, 0.1) }, Plane());
            Assert.NotNull(copy);
            AssertClose(P(0, Top, -0.1), copy[0]);
            AssertClose(P(1, Top, -0.1), copy[1]);
        }

        [Fact]
        public void SideTrussDiagonal_Mirrors()
        {
            var copy = MirrorAssist.Mirror(new[] { P(0.2, Top, 0.1), P(0.4, Top + 0.2, 0.1) }, Plane());
            Assert.NotNull(copy);
            AssertClose(P(0.4, Top + 0.2, -0.1), copy[1]);
        }

        [Fact]
        public void PieceOnTheCenterline_IsNotCopied() =>
            Assert.Null(MirrorAssist.Mirror(new[] { P(0.2, Top, 0), P(0.8, Top + 0.1, 0) }, Plane()));

        [Fact]
        public void SymmetricCrossBeam_IsNotCopied() =>
            Assert.Null(MirrorAssist.Mirror(new[] { P(0.5, Top, -0.1), P(0.5, Top, 0.1) }, Plane()));

        [Fact]
        public void AsymmetricCrossBrace_IsCopiedAsTheOtherDiagonal()
        {
            var copy = MirrorAssist.Mirror(new[] { P(0.4, Top, -0.1), P(0.6, Top + 0.2, 0.1) }, Plane());
            Assert.NotNull(copy);
            AssertClose(P(0.4, Top, 0.1), copy[0]);
            AssertClose(P(0.6, Top + 0.2, -0.1), copy[1]);
        }

        [Fact]
        public void SelfMirrorTolerance_IsInclusive()
        {
            double t = Constants.Mirror.SelfMirrorToleranceMeters;
            // Drawn parallel to the centerline at z: the copy lands 2z away.
            Assert.Null(MirrorAssist.Mirror(new[] { P(0, Top, t / 2), P(1, Top, t / 2) }, Plane()));
            Assert.NotNull(MirrorAssist.Mirror(new[] { P(0, Top, t / 2 + 1e-4), P(1, Top, t / 2 + 1e-4) }, Plane()));
        }

        [Fact]
        public void TooShortPiece_IsNotCopied()
        {
            Assert.Null(MirrorAssist.Mirror(null, Plane()));
            Assert.Null(MirrorAssist.Mirror(new[] { P(0, Top, 0.1) }, Plane()));
        }
    }

    /// <summary>A finished stroke into the model: mirror copies, one undo step, all or nothing.</summary>
    public class StrokeBuilderTests
    {
        const double Top = Rooms.Top;
        static Vec3 P(double x, double y, double z) => new Vec3(x, y, z);

        static StrokeBuilder.Context Ctx(Pad[] pads, bool mirror) => new StrokeBuilder.Context
        {
            Pads = pads,
            Mirror = mirror ? MirrorPlane.Centerline(pads[0], pads[1]) : (MirrorPlane?)null,
        };

        static IReadOnlyList<IReadOnlyList<Vec3>> Piece(params Vec3[] points) => new[] { points };

        static List<Vec3> LineEnds(BridgeModel m, Line line)
        {
            var nodes = m.LineNodes(line.Id);
            return new List<Vec3> { m.GetNode(nodes[0]).Position, m.GetNode(nodes[nodes.Count - 1]).Position };
        }

        [Fact]
        public void MirrorOn_BuildsTheRailAndItsCopy()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();

            var result = StrokeBuilder.Build(m, Piece(P(0, Top, 0.1), P(1, Top, 0.1)), Ctx(pads, true));

            Assert.True(result.Ok);
            Assert.Equal(2, result.Lines);
            Assert.Equal(2, m.Lines.Count);
            var zs = m.Lines.Select(l => LineEnds(m, l)[0].Z).OrderBy(z => z).ToArray();
            Assert.Equal(-0.1, zs[0], 6);
            Assert.Equal(0.1, zs[1], 6);
        }

        [Fact]
        public void MirrorOff_BuildsOnlyTheStroke()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Assert.Equal(1, StrokeBuilder.Build(m, Piece(P(0, Top, 0.1), P(1, Top, 0.1)), Ctx(pads, false)).Lines);
            Assert.Single(m.Lines);
        }

        [Fact]
        public void OneRailWithMirror_MakesADrivableRoad()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            StrokeBuilder.Build(m, Piece(P(0, Top, 0.1), P(1, Top, 0.1)), Ctx(pads, true));

            var bridge = BridgeCompiler.Compile(m, pads, Constants.Scale.RoomToBridge);

            Assert.True(bridge.Road.Ok);
            Assert.NotEmpty(bridge.Decks);
        }

        [Fact]
        public void StrokeAndMirror_AreOneUndoStep()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            StrokeBuilder.Build(m, Piece(P(0, Top, 0.1), P(1, Top, 0.1)), Ctx(pads, true));
            int depth = m.UndoDepth;
            Assert.Equal(1, depth);

            Assert.True(m.Undo());

            Assert.Empty(m.Lines);
            Assert.Empty(m.Members);
            Assert.Equal(0, m.Spent);
        }

        [Fact]
        public void PolylinePieces_AreOneUndoStep()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            var pieces = new[]
            {
                new[] { P(0, Top, 0.1), P(0.5, Top + 0.2, 0.1) },
                new[] { P(0.5, Top + 0.2, 0.1), P(1, Top, 0.1) },
            };

            var result = StrokeBuilder.Build(m, pieces, Ctx(pads, false));

            Assert.Equal(2, result.Lines);
            Assert.Equal(1, m.UndoDepth);
            m.Undo();
            Assert.Empty(m.Lines);
        }

        [Fact]
        public void BudgetForOnlyTheStroke_RefusesTheWholePair()
        {
            var pads = Rooms.Pads();
            var policy = new BudgetPolicy { CostPerMeter = 1, CostPerSquareMeter = 1, MaterialBudget = 1.5, NodeCap = 1000 };
            var m = new BridgeModel(policy);

            var result = StrokeBuilder.Build(m, Piece(P(0, Top, 0.1), P(1, Top, 0.1)), Ctx(pads, true));

            Assert.False(result.Ok);
            Assert.Equal(EditReason.BudgetExceeded, result.Reason);
            Assert.Empty(m.Lines);
            Assert.Equal(0, m.Spent);
            Assert.Equal(0, m.UndoDepth);
        }

        [Fact]
        public void MirrorEnd_SnapsOntoAnExistingJoint()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            StrokeBuilder.Build(m, Piece(P(0.2, Top + 0.3, -0.1), P(0.8, Top + 0.3, -0.1)), Ctx(pads, false));
            int joint = m.LineNodes(m.Lines[0].Id)[0];          // at (0.2, top + 0.3, -0.1)

            // Its mirror image lands 1 cm from that joint, within the snap radius.
            StrokeBuilder.Build(m, Piece(P(0.21, Top + 0.3, 0.1), P(0.6, Top + 0.5, 0.1)), Ctx(pads, true));

            var copy = m.Lines.Last();
            Assert.Equal(joint, m.LineNodes(copy.Id)[0]);
        }

        [Fact]
        public void RetracingAnExistingBeam_BuildsNothing()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            StrokeBuilder.Build(m, Piece(P(0, Top, 0.1), P(1, Top, 0.1)), Ctx(pads, false));

            var again = StrokeBuilder.Build(m, Piece(P(0, Top, 0.1), P(1, Top, 0.1)), Ctx(pads, false));

            Assert.False(again.Ok);
            Assert.Equal(EditReason.None, again.Reason);
            Assert.Single(m.Lines);
        }
    }

    /// <summary>Grouped edits: one Undo takes back everything done inside a group.</summary>
    public class EditGroupTests
    {
        static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        static void AddLine(BridgeModel m, double y) =>
            Assert.True(m.TryAddFreeLine(new[] { LineVertex.New(P(0, y)), LineVertex.New(P(0.5, y)) }).Ok);

        [Fact]
        public void EditsOutsideAGroup_UndoOneByOne()
        {
            var m = Rooms.Model();
            AddLine(m, 1);
            AddLine(m, 1.2);
            Assert.Equal(2, m.UndoDepth);
        }

        [Fact]
        public void NestedGroups_SaveOneSnapshot()
        {
            var m = Rooms.Model();
            m.BeginGroup();
            AddLine(m, 1);
            m.BeginGroup();
            AddLine(m, 1.2);
            m.EndGroup();
            AddLine(m, 1.4);
            m.EndGroup();

            Assert.Equal(1, m.UndoDepth);
            m.Undo();
            Assert.Empty(m.Lines);
        }

        [Fact]
        public void UndoInsideAGroup_LetsLaterEditsSaveAgain()
        {
            var m = Rooms.Model();
            AddLine(m, 1);
            m.BeginGroup();
            AddLine(m, 1.2);
            m.Undo();                       // rolls the group back to one line
            AddLine(m, 1.4);
            m.EndGroup();

            Assert.Equal(2, m.Lines.Count);
            m.Undo();
            Assert.Single(m.Lines);         // the group's later edit is its own step again
        }

        [Fact]
        public void EndGroupWithoutBegin_IsHarmless()
        {
            var m = Rooms.Model();
            m.EndGroup();
            AddLine(m, 1);
            AddLine(m, 1.2);
            Assert.Equal(2, m.UndoDepth);
        }
    }
}
