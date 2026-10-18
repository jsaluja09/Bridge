using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Spec 11.5: lines join other lines between their dots by splitting a member, and share dots cleanly.</summary>
    public class JoiningTests
    {
        static LineVertex E(int id) => LineVertex.Existing(id);
        static LineVertex N(double x, double y, double z) => LineVertex.New(new Vec3(x, y, z));
        static Vec3 P(double x, double y, double z) => new Vec3(x, y, z);

        static BridgeModel Model(int cap = 1000) => new BridgeModel(new BudgetPolicy
        {
            CostPerMeter = 1, CostPerSquareMeter = 1, MaterialBudget = 1000, NodeCap = cap,
        });

        /// <summary>A 1 m beam along x from a support, as one line with one member.</summary>
        static BridgeModel Beam(out int line, out Member member, int cap = 1000)
        {
            var m = Model(cap);
            int a = m.AddFixedNode(Vec3.Zero);
            line = m.TryAddLine(new[] { E(a), N(1, 0, 0) }).Id;
            member = m.Members[0];
            return m;
        }

        static void AssertXs(BridgeModel m, int line, params double[] xs)
        {
            var nodes = m.LineNodes(line);
            Assert.Equal(xs.Length, nodes.Count);
            for (int i = 0; i < xs.Length; i++)
                Assert.Equal(xs[i], m.GetNode(nodes[i]).Position.X, 9);
        }

        static void AssertNoDuplicateMembers(BridgeModel m)
        {
            var pairs = m.Members.Select(k => (Math.Min(k.NodeA, k.NodeB), Math.Max(k.NodeA, k.NodeB))).ToList();
            Assert.Equal(pairs.Count, pairs.Distinct().Count());
        }

        // ---------- Model ----------

        [Fact]
        public void OnMember_SplitsTheMember_AndBothLinesShareTheNode()
        {
            var m = Beam(out int beam, out var member);
            int memberId = member.Id, a = member.NodeA, b = member.NodeB;
            var r = m.TryAddLine(new[] { LineVertex.OnMember(memberId, P(0.4, 0, 0)), N(0.4, 1, 0) });
            Assert.True(r.Ok);

            var nodes = m.LineNodes(beam);
            Assert.Equal(new[] { a, nodes[1], b }, nodes);
            Assert.True(Vec3.Distance(P(0.4, 0, 0), m.GetNode(nodes[1]).Position) < 1e-12);
            Assert.Equal(nodes[1], m.LineNodes(r.Id)[0]);
            Assert.Equal(memberId, m.GetLine(beam).Members[0]);
            Assert.Equal(1, m.LineLength(beam), 9);
            Assert.Equal(2, m.Spent, 9);
            Assert.Equal(2, m.UndoDepth);

            Assert.True(m.Undo());
            var only = Assert.Single(m.Members);
            Assert.Equal(b, only.NodeB);
            Assert.Equal(2, m.Nodes.Count);
        }

        [Fact]
        public void OnMember_OffTheMember_IsRejectedAndNothingChanges()
        {
            var m = Beam(out _, out var member);
            var r = m.TryAddLine(new[] { LineVertex.OnMember(member.Id, P(0.4, 0.1, 0)), N(0.4, 1, 0) });
            Assert.False(r.Ok);
            Assert.Equal(EditReason.InvalidLine, r.Reason);
            Assert.Single(m.Members);
            Assert.Equal(1, m.UndoDepth);
        }

        [Fact]
        public void OnMember_AtAnEndNode_JoinsThatNodeWithoutSplitting()
        {
            var m = Beam(out _, out var member);
            var r = m.TryAddLine(new[] { LineVertex.OnMember(member.Id, P(1, 0, 0)), N(1, 1, 0) });
            Assert.True(r.Ok);
            Assert.Equal(2, m.Members.Count);
            Assert.Equal(member.NodeB, m.LineNodes(r.Id)[0]);
        }

        [Fact]
        public void OnMember_SplitNodeCountsAgainstTheNodeCap()
        {
            var m = Beam(out _, out var member, cap: 3);
            var r = m.TryAddLine(new[] { LineVertex.OnMember(member.Id, P(0.5, 0, 0)), N(0.5, 1, 0) });
            Assert.False(r.Ok);
            Assert.Equal(BudgetCause.NodeCap, r.Cause);
            Assert.Single(m.Members);
        }

        [Fact]
        public void OnMember_TwoPointsOnOneMember_SplitInOrder()
        {
            var m = Beam(out int beam, out var member);
            var r = m.TryAddLine(new[]
            {
                LineVertex.OnMember(member.Id, P(0.7, 0, 0)), N(0.7, 0.5, 0), N(0.3, 0.5, 0), LineVertex.OnMember(member.Id, P(0.3, 0, 0)),
            });
            Assert.True(r.Ok);
            AssertXs(m, beam, 0, 0.3, 0.7, 1);
        }

        [Fact]
        public void OnMember_FindsThePieceAnEarlierSplitMovedItTo()
        {
            var m = Beam(out int beam, out var member);
            int memberId = member.Id;
            Assert.True(m.TryAddLine(new[] { LineVertex.OnMember(memberId, P(0.3, 0, 0)), N(0.3, 1, 0) }).Ok);
            Assert.True(m.TryAddLine(new[] { LineVertex.OnMember(memberId, P(0.7, 0, 0)), N(0.7, 1, 0) }).Ok);
            AssertXs(m, beam, 0, 0.3, 0.7, 1);
        }

        [Fact]
        public void DeletingTheSplitLine_IsBlockedWhileTheJoiningLineUsesTheDot()
        {
            var m = Beam(out int beam, out var member);
            var joining = m.TryAddLine(new[] { LineVertex.OnMember(member.Id, P(0.4, 0, 0)), N(0.4, 1, 0) });
            var r = m.TryDeleteLine(beam);
            Assert.False(r.Ok);
            Assert.Equal(EditReason.Blocked, r.Reason);
            Assert.Equal(m.GetLine(joining.Id).Members, r.BlockingMemberIds);
        }

        [Fact]
        public void DeletingTheJoiningLine_MergesTheMemberBack()
        {
            var m = Beam(out int beam, out var member);
            int memberId = member.Id, a = member.NodeA, b = member.NodeB;
            var joining = m.TryAddLine(new[] { LineVertex.OnMember(memberId, P(0.4, 0, 0)), N(0.4, 1, 0) });
            Assert.True(m.TryDeleteLine(joining.Id).Ok);

            var only = Assert.Single(m.Members);
            Assert.Equal(memberId, only.Id);
            Assert.Equal(a, only.NodeA);
            Assert.Equal(b, only.NodeB);
            Assert.Equal(new[] { memberId }, m.GetLine(beam).Members);
            Assert.Equal(2, m.Nodes.Count);
            Assert.Equal(1, m.Spent, 9);
            Assert.True(m.TryDeleteLine(beam).Ok);
        }

        [Fact]
        public void Merge_WaitsUntilNoOtherLineUsesTheDot()
        {
            var m = Beam(out int beam, out var member);
            var first = m.TryAddLine(new[] { LineVertex.OnMember(member.Id, P(0.4, 0, 0)), N(0.4, 1, 0) });
            int split = m.LineNodes(first.Id)[0];
            var second = m.TryAddLine(new[] { E(split), N(0.4, -1, 0) });

            Assert.True(m.TryDeleteLine(first.Id).Ok);
            AssertXs(m, beam, 0, 0.4, 1);
            Assert.True(m.TryDeleteLine(second.Id).Ok);
            AssertXs(m, beam, 0, 1);
            Assert.Single(m.Members);
        }

        [Fact]
        public void Split_PutsTheNodeInFillLoops_AndMergeTakesItOut()
        {
            var m = Model();
            int a = m.AddFixedNode(Vec3.Zero), b = m.AddFixedNode(P(3, 0, 0)), c = m.AddFixedNode(P(0, 4, 0));
            var ab = m.TryAddLine(new[] { E(a), E(b) });
            m.TryAddLine(new[] { E(b), E(c) });
            m.TryAddLine(new[] { E(c), E(a) });
            var fill = m.TryAddFill(new[] { a, b, c }, false);

            var joining = m.TryAddLine(new[] { LineVertex.OnMember(m.GetLine(ab.Id).Members[0], P(1, 0, 0)), N(1, -1, 0) });
            int split = m.LineNodes(joining.Id)[0];
            Assert.Equal(new[] { a, split, b, c }, m.GetFill(fill.Id).Loop);
            Assert.Equal(6, m.FillArea(m.GetFill(fill.Id)), 9);

            Assert.True(m.TryDeleteLine(joining.Id).Ok);
            Assert.Equal(new[] { a, b, c }, m.GetFill(fill.Id).Loop);
        }

        // ---------- Free drawing ----------

        /// <summary>A stroke through the corners with the same slight hand noise as Rooms.Draw.</summary>
        static List<Vec3> Raw(int seed, params Vec3[] corners)
        {
            var raw = new List<Vec3>();
            var rng = new Random(seed);
            for (int c = 0; c + 1 < corners.Length; c++)
                for (int i = 0; i <= 30; i++)
                    raw.Add(Vec3.Lerp(corners[c], corners[c + 1], i / 30.0) + new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.002);
            return raw;
        }

        /// <summary>A free 0.5 m line in mid-air along x: six dots about 10 cm apart.</summary>
        static BridgeModel FreeBeam(Pad[] pads, out int line)
        {
            var m = Rooms.Model();
            Rooms.Draw(m, pads, P(0.3, 1.2, 0), P(0.8, 1.2, 0));
            line = m.Lines[0].Id;
            return m;
        }

        /// <summary>The free beam, plus a second line going straight up from its end dot (the shared dot).</summary>
        static BridgeModel SharedDot(Pad[] pads, out int joint, out Vec3 along)
        {
            var m = FreeBeam(pads, out int beam);
            var nodes = m.LineNodes(beam);
            joint = nodes[nodes.Count - 1];
            Vec3 j = m.GetNode(joint).Position;
            Rooms.Draw(m, pads, j, j + P(0, 0.4, 0));
            along = (m.GetNode(nodes[0]).Position - j).Normalized();
            return m;
        }

        [Fact]
        public void FreeDraw_EndingOnALineBetweenDots_JoinsIt()
        {
            var pads = Rooms.Pads();
            var m = FreeBeam(pads, out int beam);
            var nodes = m.LineNodes(beam);
            Vec3 d1 = m.GetNode(nodes[1]).Position, d2 = m.GetNode(nodes[2]).Position;
            for (int i = 0; i <= 20; i++)
            {
                var copy = m.Copy();
                Vec3 target = Vec3.Lerp(d1, d2, i / 20.0);
                var lines = Rooms.DrawStroke(copy, pads, Raw(i, target + P(0, 0, 0.3), target));
                var drawn = copy.LineNodes(Assert.Single(lines));
                Assert.Contains(drawn[drawn.Count - 1], copy.LineNodes(beam));
            }
        }

        [Fact]
        public void FreeDraw_CrossingALineBetweenDots_JoinsItAtTheCrossing()
        {
            var pads = Rooms.Pads();
            var m = FreeBeam(pads, out int beam);
            var nodes = m.LineNodes(beam);
            Vec3 mid = Vec3.Lerp(m.GetNode(nodes[1]).Position, m.GetNode(nodes[2]).Position, 0.5);
            var line = Assert.Single(Rooms.DrawStroke(m, pads, Raw(1, mid + P(0, 0, -0.3), mid + P(0, 0, 0.3))));

            var after = m.LineNodes(beam);
            Assert.Equal(nodes.Count + 1, after.Count);
            Assert.Contains(after[2], m.LineNodes(line));
            Assert.True(Vec3.Distance(mid, m.GetNode(after[2]).Position) < 0.005);
        }

        [Fact]
        public void FreeDraw_RunningAlongALine_DoesNotSplitIt()
        {
            var pads = Rooms.Pads();
            var m = FreeBeam(pads, out int beam);
            var nodes = m.LineNodes(beam);
            Rooms.DrawStroke(m, pads, Raw(2, P(0.2, 1.2, 0.03), P(0.9, 1.2, 0.03)));
            Assert.Equal(nodes, m.LineNodes(beam));
            AssertNoDuplicateMembers(m);
        }

        [Theory]
        [InlineData(15)]
        [InlineData(20)]
        [InlineData(30)]
        public void FreeDraw_NarrowAngleAtASharedDot_StaysOnThatDot(double degrees)
        {
            var pads = Rooms.Pads();
            var m = SharedDot(pads, out int joint, out Vec3 along);
            Vec3 j = m.GetNode(joint).Position;
            double t = degrees * Math.PI / 180;
            Vec3 far = j + (along * Math.Cos(t) + Vec3.UnitY * Math.Sin(t)) * 0.4;

            var leaving = m.Copy();
            var lines = Rooms.DrawStroke(leaving, pads, Raw(3, j, far));
            Assert.Equal(joint, leaving.LineNodes(Assert.Single(lines))[0]);

            var reaching = m.Copy();
            lines = Rooms.DrawStroke(reaching, pads, Raw(4, far, j));
            var nodes = reaching.LineNodes(Assert.Single(lines));
            Assert.Equal(joint, nodes[nodes.Count - 1]);
        }

        [Fact]
        public void FreeDraw_TracingOverALine_DropsTheOverlap()
        {
            var pads = Rooms.Pads();
            var m = SharedDot(pads, out int joint, out Vec3 along);
            Vec3 j = m.GetNode(joint).Position;
            int before = m.Lines.Count;

            Rooms.DrawStroke(m, pads, Raw(5, j, j + along * 0.3));
            Assert.Equal(before, m.Lines.Count);

            double t = 5 * Math.PI / 180;
            var lines = Rooms.DrawStroke(m, pads, Raw(6, j, j + (along * Math.Cos(t) + Vec3.UnitY * Math.Sin(t)) * 0.4));
            Assert.All(lines, id => Assert.DoesNotContain(joint, m.LineNodes(id)));
            AssertNoDuplicateMembers(m);
        }

        [Fact]
        public void FreeDraw_DeletingTheJoiningLine_RestoresTheLineItSplit()
        {
            var pads = Rooms.Pads();
            var m = FreeBeam(pads, out int beam);
            var nodes = m.LineNodes(beam);
            var members = m.GetLine(beam).Members.ToList();
            Vec3 mid = Vec3.Lerp(m.GetNode(nodes[1]).Position, m.GetNode(nodes[2]).Position, 0.5);

            int line = Assert.Single(Rooms.DrawStroke(m, pads, Raw(7, mid + P(0, 0.3, 0), mid)));
            Assert.Equal(nodes.Count + 1, m.LineNodes(beam).Count);
            Assert.True(m.TryDeleteLine(line).Ok);
            Assert.Equal(nodes, m.LineNodes(beam));
            Assert.Equal(members, m.GetLine(beam).Members);
        }

        [Fact]
        public void Target_ShowsWhatAStrokeEndWouldJoin()
        {
            var pads = Rooms.Pads();
            var m = SharedDot(pads, out int joint, out _);
            var ctx = Rooms.Context(m, pads);
            var beam = m.LineNodes(m.Lines[0].Id);
            Vec3 d1 = m.GetNode(beam[1]).Position, d2 = m.GetNode(beam[2]).Position, mid = Vec3.Lerp(d1, d2, 0.5);

            Assert.Equal(beam[1], FreeDraw.Target(d1 + P(0, 0.02, 0), ctx).ExistingId);
            var onLine = FreeDraw.Target(mid + P(0, 0.03, 0), ctx);
            Assert.True(onLine.IsOnMember);
            Assert.True(Vec3.Distance(mid, onLine.Position) < 1e-3);
            Assert.False(FreeDraw.Target(mid + P(0, 0.06, 0), ctx).Joins);

            var start = FreeDraw.Target(m.GetNode(joint).Position, ctx);
            Assert.Equal(joint, start.ExistingId);
            Assert.False(FreeDraw.Target(m.GetNode(joint).Position + P(0, 0.01, 0), ctx, start).Joins);
        }

        [Fact]
        public void SplitBridge_DeadLoadSolve_HasNoNaN()
        {
            var pads = Rooms.Pads();
            var m = Rooms.BareDeck(pads);
            int left = m.Lines[0].Id, right = m.Lines[1].Id;
            int leftNodes = m.LineNodes(left).Count, rightNodes = m.LineNodes(right).Count;
            foreach (double x in new[] { 0.25, 0.65 })
                Rooms.Draw(m, pads, P(x, Rooms.Top, -0.1), P(x, Rooms.Top, 0.1));
            Rooms.Draw(m, pads, P(0.45, Rooms.Top + 0.25, -0.1), P(0.45, Rooms.Top, -0.1));
            Assert.Equal(leftNodes + 3, m.LineNodes(left).Count);
            Assert.Equal(rightNodes + 2, m.LineNodes(right).Count);

            var bridge = BridgeCompiler.Compile(m, pads, Constants.Scale.RoomToBridge);
            var r = DeadLoadPreview.Run(bridge.Input, new BandedStaticSolver(), null);
            Assert.Equal(PreviewStatus.Completed, r.Status);
            Assert.All(r.Solve.Displacements, d => Assert.True(d.IsFinite));
            Assert.All(r.Utilizations, u => Assert.True(double.IsFinite(u)));
        }
    }
}
