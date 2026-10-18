using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class CollapseEffectsTests
    {
        const double Scale = 20;
        const double Dt = 1.0 / 60;
        static double Linger => Constants.CollapseEffects.LingerSeconds;
        static double Shrink => Constants.CollapseEffects.ShrinkSeconds;

        static CompiledBridge BareDeck(out Pad[] pads)
        {
            pads = Rooms.Pads();
            return BridgeCompiler.Compile(Rooms.BareDeck(pads), pads, Scale);
        }

        static Vec3[] Rest(CompiledBridge b) => b.Input.Nodes.Select(n => n.Position).ToArray();

        static MemberState[] All(CompiledBridge b, MemberState s) => Enumerable.Repeat(s, b.Input.Members.Count).ToArray();

        static FailureReport Report(long step, params MemberFailure[] failures)
        {
            var r = new FailureReport { Step = step };
            r.Failures.AddRange(failures);
            return r;
        }

        /// <summary>The visible member nearest mid-span, away from the pads.</summary>
        static Member MidspanRail(CompiledBridge b)
        {
            var rest = Rest(b);
            return b.Input.Members.Where(m => !m.HiddenBrace)
                .OrderBy(m => Math.Abs((rest[b.NodeIndex[m.NodeA]].X + rest[b.NodeIndex[m.NodeB]].X) / 2 - 0.5)).First();
        }

        static void Run(CollapseEffects fx, CompiledBridge b, double from, double to, Action<CollapseEffects> check = null)
        {
            var rest = Rest(b);
            var state = All(b, MemberState.Standing);
            for (double t = from; t <= to; t += Dt)
            {
                fx.Update(t, rest, state, null);
                check?.Invoke(fx);
            }
        }

        [Fact]
        public void SnappedMember_SplitsAtTheFailurePoint_IntoTwoHalves()
        {
            var b = BareDeck(out _);
            var rail = MidspanRail(b);
            var fx = new CollapseEffects(b, 0, Scale);
            var rest = Rest(b);
            fx.Update(0, rest, All(b, MemberState.Standing), new[] { Report(0, new MemberFailure { MemberId = rail.Id, FailurePoint = 0.25 }) });

            Assert.Equal(2, fx.Fragments.Count);
            Vec3 a = rest[b.NodeIndex[rail.NodeA]], c = rest[b.NodeIndex[rail.NodeB]];
            Vec3 at = Vec3.Lerp(a, c, 0.25);
            var halves = fx.Fragments;
            Assert.All(halves, f => Assert.False(f.Plate));
            Assert.True(Vec3.Distance(halves[0].Point(0), a) < 1e-9 && Vec3.Distance(halves[0].Point(1), at) < 1e-9);
            Assert.True(Vec3.Distance(halves[1].Point(0), at) < 1e-9 && Vec3.Distance(halves[1].Point(1), c) < 1e-9);
        }

        [Fact]
        public void Pieces_FallLandAndLinger_ThenShrinkAway()
        {
            var b = BareDeck(out _);
            var fx = new CollapseEffects(b, 0, Scale);
            var rest = Rest(b);
            double top = rest.Max(p => p.Y);
            fx.Update(0, rest, All(b, MemberState.Standing), new[] { Report(0, new MemberFailure { MemberId = MidspanRail(b).Id, FailurePoint = 0.5 }) });

            double landed = double.NaN;
            Run(fx, b, Dt, 30, f =>
            {
                foreach (var p in f.Fragments)
                {
                    for (int i = 0; i < p.PointCount; i++)
                    {
                        var q = p.Point(i);
                        Assert.True(q.IsFinite);
                        Assert.True(q.Y >= -1e-9, "below the floor");
                        Assert.True(q.Y <= top + 0.05, "rose into the sky");
                        Assert.True(Math.Abs(q.X - 0.5) < 1.5 && Math.Abs(q.Z) < 1.5, "flew off sideways");
                    }
                    if (!double.IsNaN(p.LandedAt) && double.IsNaN(landed))
                        landed = p.LandedAt;
                }
            });
            Assert.False(double.IsNaN(landed));
            Assert.Empty(fx.Fragments);                       // gone before 30 s
        }

        [Fact]
        public void LandedPiece_StaysWholeForTheLinger_ThenShrinks()
        {
            var b = BareDeck(out _);
            var fx = new CollapseEffects(b, 0.6, Scale);      // a floor just under the deck: lands fast
            fx.Update(0, Rest(b), All(b, MemberState.Standing), new[] { Report(0, new MemberFailure { MemberId = MidspanRail(b).Id, FailurePoint = 0.5 }) });
            Fragment piece = fx.Fragments[0];
            double t = 0;
            while (double.IsNaN(piece.LandedAt) && t < 20)
                Run(fx, b, t += Dt, t);
            Assert.False(double.IsNaN(piece.LandedAt));

            Run(fx, b, t + Dt, piece.LandedAt + Linger - 0.05);
            Assert.Equal(1, piece.Scale);
            Run(fx, b, piece.LandedAt + Linger, piece.LandedAt + Linger + Shrink / 2);
            Assert.InRange(piece.Scale, 0.01, 0.99);
        }

        [Fact]
        public void TornDeck_DropsEachTriangleOnce_AsAPlate()
        {
            var b = BareDeck(out _);
            var brace = b.Input.Members.First(m => m.HiddenBrace);
            int k = b.Input.Members.IndexOf(brace);
            int triangles = b.Decks.Sum(d => Enumerable.Range(0, d.TriangleEdges.Length).Count(e => d.TriangleEdges[e] == k));
            Assert.True(triangles > 0);

            var fx = new CollapseEffects(b, 0, Scale);
            var failure = new MemberFailure { MemberId = brace.Id, FailurePoint = 0.5 };
            fx.Update(0, Rest(b), All(b, MemberState.Standing), new[] { Report(0, failure), Report(0, failure) });

            Assert.Equal(triangles, fx.Fragments.Count);
            Assert.All(fx.Fragments, f => Assert.True(f.Plate && f.PointCount == 3));
            int torn = b.Decks.Select((d, di) => Enumerable.Range(0, d.Triangles.Length / 3).Count(t => fx.Torn(di, t * 3))).Sum();
            Assert.Equal(triangles, torn);
        }

        [Fact]
        public void PieceOverAPad_LandsOnThePad()
        {
            var b = BareDeck(out var pads);
            var rail = b.Input.Members.Where(m => !m.HiddenBrace)
                .OrderBy(m => Rest(b)[b.NodeIndex[m.NodeA]].X + Rest(b)[b.NodeIndex[m.NodeB]].X).First();
            var fx = new CollapseEffects(b, 0, Scale);
            var rest = Rest(b);
            // Put the member's nodes over the start pad, a little above it.
            var lifted = rest.Select(p => p).ToArray();
            Vec3 over = pads[0].FrontCenter - pads[0].Forward * 0.1 + Vec3.UnitY * 0.05;
            lifted[b.NodeIndex[rail.NodeA]] = over;
            lifted[b.NodeIndex[rail.NodeB]] = over + pads[0].Across.Normalized() * 0.05;
            fx.Update(0, lifted, All(b, MemberState.Standing), new[] { Report(0, new MemberFailure { MemberId = rail.Id, FailurePoint = 0.5 }) });
            for (double t = Dt; t < 1.5; t += Dt)
                fx.Update(t, lifted, All(b, MemberState.Standing), null);
            Assert.Equal(2, fx.Fragments.Count);
            Assert.All(fx.Fragments, f => Assert.False(double.IsNaN(f.LandedAt)));
            Assert.All(fx.Fragments, f => Assert.True(f.Center.Y > pads[0].Height - 0.02));
        }

        [Fact]
        public void LooseJoint_FadesAfterItSettles()
        {
            var b = BareDeck(out _);
            var fx = new CollapseEffects(b, 0, Scale);
            var rest = Rest(b);
            var falling = All(b, MemberState.Falling);
            int free = Enumerable.Range(0, rest.Length).First(i => !b.Input.Nodes[i].Fixed);
            fx.Update(0, rest, falling, null);
            fx.Update(Dt, rest, falling, null);              // not moving: settled
            Assert.Equal(1, fx.NodeFade(free));
            fx.Update(Dt + Linger + Shrink / 2, rest, falling, null);
            Assert.InRange(fx.NodeFade(free), 0.01, 0.99);
            fx.Update(Dt + Linger + Shrink + 0.1, rest, falling, null);
            Assert.Equal(0, fx.NodeFade(free));

            var standing = new CollapseEffects(b, 0, Scale);
            standing.Update(0, rest, All(b, MemberState.Standing), null);
            standing.Update(10, rest, All(b, MemberState.Standing), null);
            Assert.Equal(1, standing.NodeFade(free));         // joints still in the bridge never fade
        }

        [Fact]
        public void SameInputs_SamePieces()
        {
            var b = BareDeck(out _);
            var report = Report(0, new MemberFailure { MemberId = MidspanRail(b).Id, FailurePoint = 0.3 });
            CollapseEffects Make()
            {
                var fx = new CollapseEffects(b, 0, Scale);
                fx.Update(0, Rest(b), All(b, MemberState.Standing), new[] { report });
                Run(fx, b, Dt, 1.5);
                return fx;
            }
            var x = Make();
            var y = Make();
            Assert.Equal(x.Fragments.Count, y.Fragments.Count);
            for (int i = 0; i < x.Fragments.Count; i++)
                for (int p = 0; p < x.Fragments[i].PointCount; p++)
                    Assert.Equal(x.Fragments[i].Point(p), y.Fragments[i].Point(p));
        }

        [Fact]
        public void BareDeckCollapse_EndToEnd_StaysInBounds_AndCleansUp()
        {
            var pads = Rooms.Pads();
            var run = Rooms.Run(Rooms.BareDeck(pads), pads);
            var tracker = new FailureTracker(run.Bridge.Input, Scale);
            var fx = new CollapseEffects(run.Bridge, 0, Scale);
            var snap = new RunSnapshot();
            run.Write(snap);
            double top = snap.Positions.Max(p => p.Y);
            int most = 0;
            long step = 0;
            var reports = new List<FailureReport>();
            for (int i = 0; i < 60 * 20; i++)
            {
                run.Step(Dt);
                run.Write(snap);
                reports.Clear();
                var r = tracker.Observe(run.Coupled.Frame, ++step);
                if (r != null)
                    reports.Add(r);
                fx.Update(snap.Time, snap.Positions, snap.State, reports);
                most = Math.Max(most, fx.Fragments.Count);
                foreach (var f in fx.Fragments)
                    for (int p = 0; p < f.PointCount; p++)
                    {
                        var q = f.Point(p);
                        Assert.True(q.IsFinite && q.Y >= -1e-9 && q.Y <= top + 0.05, $"piece out of bounds at {q}");
                    }
            }
            Assert.Equal(RunResult.Failed, run.Result);
            Assert.True(most > 0, "the collapse made no pieces");
            Assert.Empty(fx.Fragments);
            // Everything the solver dropped has settled and shrunk away by now.
            for (int k = 0; k < snap.State.Length; k++)
            {
                if (snap.State[k] != MemberState.Falling)
                    continue;
                var m = run.Bridge.Input.Members[k];
                Assert.Equal(0, Math.Min(fx.NodeFade(run.Bridge.NodeIndex[m.NodeA]), fx.NodeFade(run.Bridge.NodeIndex[m.NodeB])));
            }
        }

        [Fact]
        public void BadInputs_Throw()
        {
            var b = BareDeck(out _);
            var fx = new CollapseEffects(b, 0, Scale);
            Assert.Throws<ArgumentException>(() =>
                fx.Update(0, Rest(b), All(b, MemberState.Standing), new[] { Report(0, new MemberFailure { MemberId = 99999 }) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => fx.Update(double.NaN, Rest(b), All(b, MemberState.Standing), null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CollapseEffects(b, 0, 0));
        }

        [Fact]
        public void HugeCollapse_IsCapped()
        {
            var b = BareDeck(out _);
            var fx = new CollapseEffects(b, 0, Scale);
            var everything = b.Input.Members.Select(m => new MemberFailure { MemberId = m.Id, FailurePoint = 0.5 }).ToArray();
            for (int i = 0; i < 20; i++)
                fx.Update(0, Rest(b), All(b, MemberState.Standing), new[] { Report(0, everything) });
            Assert.True(fx.Fragments.Count <= Constants.CollapseEffects.MaxFragments);
        }
    }
}
