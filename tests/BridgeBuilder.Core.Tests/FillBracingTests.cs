using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class FillBracingTests
    {
        private const int Section = 1;
        private const double DeckHeight = 1.0;
        private const double SnapSpacingMeters = 0.1;

        private static readonly Vec3 LevelOrigin = new Vec3(0.0, DeckHeight, 0.0);
        private static readonly Vec3 TiltedOrigin = new Vec3(0.3, 1.2, -0.4);
        private static readonly Vec3 TiltedU = new Vec3(2.0, 1.0, 2.0) / 3.0;
        private static readonly Vec3 TiltedV = new Vec3(1.0, 2.0, -2.0) / 3.0;
        private static readonly Vec3 TiltedNormal = new Vec3(-2.0, 2.0, 1.0) / 3.0;

        [Fact]
        public void Bracing_UsesOnlyBoundaryNodes()
        {
            foreach (Vec3[] loop in new[] { DeckStrip(), LShape(), SnapNodeRectangle(TiltedOrigin, TiltedU, TiltedV, 4, 2) })
            {
                var model = new BridgeModel(Policy());
                int fillId = BuildFill(model, loop);
                int[] loopNodeIds = LoopNodeIds(model, fillId);
                int[] nodesBefore = model.Nodes.Select(node => node.Id).ToArray();

                int[] braceIds = FillBracing.AddBraces(model, fillId, Section);

                Assert.Equal(loop.Length - 3, braceIds.Length);
                Assert.Equal(nodesBefore, model.Nodes.Select(node => node.Id));
                Assert.Equal(nodesBefore.Length, model.NodeCount);
                foreach (Member brace in Members(model, braceIds))
                {
                    Assert.Contains(brace.NodeA, loopNodeIds);
                    Assert.Contains(brace.NodeB, loopNodeIds);
                    Assert.NotEqual(brace.NodeA, brace.NodeB);
                }

                AssertFinite(model);
            }
        }

        [Fact]
        public void Bracing_MembersLieInFillPlane()
        {
            double tolerance = Constants.Fill.PlanarityToleranceMeters;
            Vec3[] warped = LShape();
            warped[2] = warped[2] + Vec3.UnitY * (0.5 * tolerance);
            foreach (Vec3[] loop in new[] { Tilt(DeckStrip()), SnapNodeRectangle(TiltedOrigin, TiltedU, TiltedV, 4, 2), warped })
            {
                Assert.True(FillPlanarity.IsPlanar(loop));
                var model = new BridgeModel(Policy());
                int fillId = BuildFill(model, loop);

                int[] braceIds = FillBracing.AddBraces(model, fillId, Section);

                Assert.NotEmpty(braceIds);
                Vec3 normal = NewellNormal(loop);
                Vec3 centroid = Centroid(loop);
                Dictionary<int, Vec3> positions = model.Nodes.ToDictionary(node => node.Id, node => node.Position);
                foreach (Member brace in Members(model, braceIds))
                {
                    Vec3 a = positions[brace.NodeA];
                    Vec3 b = positions[brace.NodeB];
                    foreach (Vec3 point in new[] { a, (a + b) / 2.0, b })
                    {
                        double distance = Math.Abs(Vec3.Dot(point - centroid, normal));
                        Assert.False(double.IsNaN(distance) || double.IsInfinity(distance), "Brace " + brace.Id + " has a non-finite plane distance.");
                        Assert.True(distance <= tolerance, "Brace " + brace.Id + " is " + distance + " m off the fill plane.");
                    }
                }

                AssertFinite(model);
            }
        }

        [Fact]
        public void Bracing_MarkedHidden()
        {
            foreach (Vec3[] loop in new[] { QuadLoop(), DeckStrip(), LShape(), SnapNodeRectangle(LevelOrigin, Vec3.UnitX, Vec3.UnitZ, 5, 3) })
            {
                var model = new BridgeModel(Policy());
                int fillId = BuildFill(model, loop);
                int drawnMembers = model.MemberCount;

                int[] braceIds = FillBracing.AddBraces(model, fillId, Section);

                Assert.Equal(drawnMembers + braceIds.Length, model.MemberCount);
                foreach (Member brace in Members(model, braceIds))
                {
                    Assert.True(brace.HiddenBrace, "Brace " + brace.Id + " is not marked hidden.");
                    Assert.Equal(0, brace.LineId);
                    Assert.Equal(Section, brace.SectionId);
                }

                Assert.Equal(braceIds.OrderBy(id => id), model.Members.Where(member => member.HiddenBrace).Select(member => member.Id));
            }
        }

        [Fact]
        public void Bracing_DoesNotChangeBoundaryMembers()
        {
            var model = new BridgeModel(Policy());
            Vec3[] loop = DeckStrip();
            int start = model.AddFixedNode(loop[0]);
            EditResult first = model.TryAddLine(new[] { LineVertex.AtNode(start), LineVertex.AtPosition(loop[1]), LineVertex.AtPosition(loop[2]), LineVertex.AtPosition(loop[3]) }, Section);
            Assert.True(first.Succeeded);
            EditResult second = model.TryAddLine(new[] { LineVertex.AtNode(first.NodeIds[2]), LineVertex.AtPosition(loop[4]), LineVertex.AtPosition(loop[5]), LineVertex.AtNode(start) }, Section);
            Assert.True(second.Succeeded);
            Assert.True(model.TryAddLine(new[] { LineVertex.AtNode(start), LineVertex.AtPosition(new Vec3(0.0, 0.0, 0.0)) }, Section).Succeeded);
            int[] loopNodeIds = new[] { start }.Concat(first.NodeIds).Concat(second.NodeIds).ToArray();
            EditResult fill = model.TryAddFill(loopNodeIds, true);
            Assert.True(fill.Succeeded);

            int[] boundaryBefore = BoundaryMemberIds(model, loopNodeIds);
            string[] membersBefore = model.Members.Select(Describe).ToArray();
            string[] linesBefore = model.Lines.Select(Describe).ToArray();

            int[] braceIds = FillBracing.AddBraces(model, fill.Id, Section);

            Assert.Equal(loopNodeIds.Length, boundaryBefore.Length);
            Assert.Equal(boundaryBefore, BoundaryMemberIds(model, loopNodeIds));
            Assert.DoesNotContain(braceIds, id => boundaryBefore.Contains(id));
            Assert.Equal(membersBefore, model.Members.Where(member => !braceIds.Contains(member.Id)).Select(Describe));
            Assert.Equal(linesBefore, model.Lines.Select(Describe));
            Assert.Equal(loopNodeIds, LoopNodeIds(model, fill.Id));
            AssertFinite(model);
        }

        [Fact]
        public void NonPlanarFill_Throws()
        {
            double tolerance = Constants.Fill.PlanarityToleranceMeters;
            Vec3[] raisedCorner = QuadLoop();
            raisedCorner[2] = raisedCorner[2] + Vec3.UnitY * (2.0 * tolerance);
            Vec3[] bentStrip = Tilt(DeckStrip());
            bentStrip[4] = bentStrip[4] + TiltedNormal * (2.0 * tolerance);
            foreach (Vec3[] loop in new[] { raisedCorner, bentStrip })
            {
                Assert.False(FillPlanarity.IsPlanar(loop));
                var model = new BridgeModel(Policy());
                int fillId = BuildFill(model, loop);

                AssertBracingRefused(model, fillId);
            }
        }

        [Fact]
        public void SelfIntersectingFill_Throws()
        {
            Vec3[] crossed = AtDeckHeight(0.0, 0.0, 0.5, 0.0, 0.0, 0.25, 0.25, 0.25);
            Vec3[] touching = AtDeckHeight(0.0, 0.0, 0.5, 0.0, 0.5, 0.5, 0.25, 0.0, 0.0, 0.5);
            foreach (Vec3[] loop in new[] { crossed, touching })
            {
                Assert.True(FillPlanarity.IsPlanar(loop));
                var model = new BridgeModel(Policy());
                int fillId = BuildFill(model, loop);

                AssertBracingRefused(model, fillId);
            }
        }

        [Fact]
        public void Bracing_NotChargedToBudget()
        {
            // The strip's frame costs 1.5 and its fill 0.125, so this budget leaves nothing to spend.
            var spentOut = new BridgeModel(Policy(materialBudget: 1.625));
            int stripFill = BuildFill(spentOut, DeckStrip());
            Assert.Equal(1.625, spentOut.SpentBudget);
            Assert.Equal(0.0, spentOut.RemainingBudget);

            int[] stripBraces = FillBracing.AddBraces(spentOut, stripFill, Section);

            Assert.Equal(3, stripBraces.Length);
            Assert.Equal(1.625, spentOut.SpentBudget);
            Assert.Equal(0.0, spentOut.RemainingBudget);
            AssertFinite(spentOut);

            var model = new BridgeModel();
            int snapFill = BuildFill(model, SnapNodeRectangle(LevelOrigin, Vec3.UnitX, Vec3.UnitZ, 5, 3));
            double spent = model.SpentBudget;
            double remaining = model.RemainingBudget;

            int[] snapBraces = FillBracing.AddBraces(model, snapFill, Section);

            Assert.Equal(13, snapBraces.Length);
            Assert.Equal(spent, model.SpentBudget);
            Assert.Equal(remaining, model.RemainingBudget);
            AssertFinite(model);
        }

        [Fact]
        public void Bracing_MatchesAppendedPattern()
        {
            Assert.Equal("shortest-diagonal-triangulation-v1", Constants.Bracing.PatternId);
            AssertPattern(QuadLoop(), (0, 2));
            AssertPattern(DeckStrip(), (1, 4), (0, 4), (1, 3));
            AssertPattern(LShape(), (0, 3), (1, 3), (3, 5));
            AssertPattern(AtDeckHeight(0.0, 0.0, 0.5, 0.0, 0.0, 0.25));
        }

        [Fact]
        public void Bracing_SkipsPairsAnExistingMemberJoins()
        {
            var model = new BridgeModel(Policy());
            int fillId = BuildFill(model, DeckStrip());
            int[] loopNodeIds = LoopNodeIds(model, fillId);
            Assert.True(model.TryAddLine(new[] { LineVertex.AtNode(loopNodeIds[1]), LineVertex.AtNode(loopNodeIds[4]) }, Section).Succeeded);
            HashSet<(int, int)> drawnPairs = model.Members.Select(member => PairKey(member.NodeA, member.NodeB)).ToHashSet();

            int[] braceIds = FillBracing.AddBraces(model, fillId, Section);

            Assert.NotEmpty(braceIds);
            (int, int)[] bracePairs = Members(model, braceIds).Select(brace => PairKey(brace.NodeA, brace.NodeB)).ToArray();
            Assert.Equal(bracePairs.Length, bracePairs.Distinct().Count());
            Assert.All(bracePairs, pair => Assert.DoesNotContain(pair, drawnPairs));
        }

        [Fact]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => FillBracing.AddBraces(null, 1, Section));

            var model = new BridgeModel(Policy());
            int fillId = BuildFill(model, QuadLoop());
            AssertBracingRefused(model, fillId + 1);

            Assert.True(model.TryDeleteLine(Assert.Single(model.Lines).Id).Succeeded);
            Assert.False(Assert.Single(model.Fills).Valid);
            AssertBracingRefused(model, fillId);
        }

        private static void AssertPattern(Vec3[] loop, params (int, int)[] expected)
        {
            var model = new BridgeModel(Policy());
            model.AddFixedNode(new Vec3(-1.0, 0.0, -1.0));
            int fillId = BuildFill(model, loop);
            int[] loopNodeIds = LoopNodeIds(model, fillId);
            Assert.NotEqual(Enumerable.Range(0, loop.Length), loopNodeIds);

            int[] braceIds = FillBracing.AddBraces(model, fillId, Section);

            (int, int)[] positions = Members(model, braceIds)
                .Select(brace => (Array.IndexOf(loopNodeIds, brace.NodeA), Array.IndexOf(loopNodeIds, brace.NodeB)))
                .ToArray();
            Assert.Equal(Unordered(expected), Unordered(positions));
            AssertFinite(model);
        }

        private static void AssertBracingRefused(BridgeModel model, int fillId)
        {
            string[] membersBefore = model.Members.Select(Describe).ToArray();
            int undoDepth = model.UndoDepth;
            double spent = model.SpentBudget;

            Assert.Throws<ArgumentException>(() => FillBracing.AddBraces(model, fillId, Section));

            Assert.Equal(membersBefore, model.Members.Select(Describe));
            Assert.Equal(undoDepth, model.UndoDepth);
            Assert.Equal(spent, model.SpentBudget);
            AssertFinite(model);
        }

        private static BudgetPolicy Policy(double materialBudget = 1000.0)
        {
            return new BudgetPolicy(1.0, 1.0, materialBudget, 1000);
        }

        /// <summary>Draws the loop as one closed line from a fixed node at its first vertex and fills it as road.</summary>
        private static int BuildFill(BridgeModel model, Vec3[] loop)
        {
            int start = model.AddFixedNode(loop[0]);
            var vertices = new List<LineVertex> { LineVertex.AtNode(start) };
            vertices.AddRange(loop.Skip(1).Select(LineVertex.AtPosition));
            vertices.Add(LineVertex.AtNode(start));
            EditResult frame = model.TryAddLine(vertices, Section);
            Assert.True(frame.Succeeded, "The frame line was refused: " + frame.Reason + ".");

            EditResult fill = model.TryAddFill(new[] { start }.Concat(frame.NodeIds).ToArray(), true);
            Assert.True(fill.Succeeded, "The fill was refused: " + fill.Reason + ".");
            return fill.Id;
        }

        private static int[] LoopNodeIds(BridgeModel model, int fillId)
        {
            return Assert.Single(model.Fills, fill => fill.Id == fillId).NodeIds;
        }

        private static Member[] Members(BridgeModel model, int[] memberIds)
        {
            IReadOnlyList<Member> members = model.Members;
            return memberIds.Select(id => Assert.Single(members, member => member.Id == id)).ToArray();
        }

        private static int[] BoundaryMemberIds(BridgeModel model, int[] loopNodeIds)
        {
            var edges = new HashSet<(int, int)>();
            for (int k = 0; k < loopNodeIds.Length; k++)
            {
                edges.Add(PairKey(loopNodeIds[k], loopNodeIds[(k + 1) % loopNodeIds.Length]));
            }

            return model.Members.Where(member => edges.Contains(PairKey(member.NodeA, member.NodeB))).Select(member => member.Id).ToArray();
        }

        private static (int, int) PairKey(int a, int b)
        {
            return a < b ? (a, b) : (b, a);
        }

        private static (int, int)[] Unordered((int, int)[] pairs)
        {
            return pairs.Select(pair => PairKey(pair.Item1, pair.Item2)).OrderBy(pair => pair.Item1).ThenBy(pair => pair.Item2).ToArray();
        }

        private static string Describe(Member member)
        {
            return member.Id + ": " + member.NodeA + "-" + member.NodeB + " section " + member.SectionId + " line " + member.LineId + " hidden " + member.HiddenBrace;
        }

        private static string Describe(Line line)
        {
            return line.Id + ": " + string.Join(",", line.MemberIds);
        }

        /// <summary>Loop vertices at Y = <see cref="DeckHeight"/> from (x, z) pairs, room units.</summary>
        private static Vec3[] AtDeckHeight(params double[] xz)
        {
            var loop = new Vec3[xz.Length / 2];
            for (int k = 0; k < loop.Length; k++)
            {
                loop[k] = new Vec3(xz[2 * k], DeckHeight, xz[2 * k + 1]);
            }

            return loop;
        }

        /// <summary>HG-bracing quad: a 0.5 by 0.25 rectangle.</summary>
        private static Vec3[] QuadLoop()
        {
            return AtDeckHeight(0.0, 0.0, 0.5, 0.0, 0.5, 0.25, 0.0, 0.25);
        }

        /// <summary>HG-bracing n-gon: a two-panel deck strip.</summary>
        private static Vec3[] DeckStrip()
        {
            return AtDeckHeight(0.0, 0.0, 0.25, 0.0, 0.5, 0.0, 0.5, 0.25, 0.25, 0.25, 0.0, 0.25);
        }

        /// <summary>HG-bracing n-gon: a non-convex L.</summary>
        private static Vec3[] LShape()
        {
            return AtDeckHeight(0.0, 0.0, 0.5, 0.0, 0.5, 0.25, 0.25, 0.25, 0.25, 0.5, 0.0, 0.5);
        }

        /// <summary>Maps a level loop's (x, z) into the tilted plane through <see cref="TiltedOrigin"/>.</summary>
        private static Vec3[] Tilt(Vec3[] level)
        {
            return level.Select(p => TiltedOrigin + TiltedU * p.X + TiltedV * p.Z).ToArray();
        }

        /// <summary>A rectangle loop with a node at every snap spacing along each edge, as equal-segment snapping produces.</summary>
        private static Vec3[] SnapNodeRectangle(Vec3 origin, Vec3 u, Vec3 v, int segmentsU, int segmentsV)
        {
            var loop = new List<Vec3>();
            for (int i = 0; i < segmentsU; i++)
            {
                loop.Add(origin + u * (i * SnapSpacingMeters));
            }

            for (int j = 0; j < segmentsV; j++)
            {
                loop.Add(origin + u * (segmentsU * SnapSpacingMeters) + v * (j * SnapSpacingMeters));
            }

            for (int i = segmentsU; i > 0; i--)
            {
                loop.Add(origin + u * (i * SnapSpacingMeters) + v * (segmentsV * SnapSpacingMeters));
            }

            for (int j = segmentsV; j > 0; j--)
            {
                loop.Add(origin + v * (j * SnapSpacingMeters));
            }

            return loop.ToArray();
        }

        private static Vec3 NewellNormal(Vec3[] loop)
        {
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            for (int k = 0; k < loop.Length; k++)
            {
                Vec3 current = loop[k];
                Vec3 next = loop[(k + 1) % loop.Length];
                x += (current.Y - next.Y) * (current.Z + next.Z);
                y += (current.Z - next.Z) * (current.X + next.X);
                z += (current.X - next.X) * (current.Y + next.Y);
            }

            return new Vec3(x, y, z).Normalized();
        }

        private static Vec3 Centroid(Vec3[] loop)
        {
            Vec3 sum = Vec3.Zero;
            foreach (Vec3 vertex in loop)
            {
                sum += vertex;
            }

            return sum / loop.Length;
        }

        private static void AssertFinite(BridgeModel model)
        {
            Assert.True(double.IsFinite(model.SpentBudget), "Spent budget is not finite.");
            Assert.True(double.IsFinite(model.RemainingBudget), "Remaining budget is not finite.");
            foreach (Node node in model.Nodes)
            {
                Assert.True(double.IsFinite(node.Position.X) && double.IsFinite(node.Position.Y) && double.IsFinite(node.Position.Z), "Node " + node.Id + " position is not finite.");
            }
        }
    }
}
