using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W1-01: frame elements and static solve, fixed supports only. Fixture 2 and 3 numbers: do not modify without human approval.</summary>
    public class FrameStaticSolverTests
    {
        static SectionProperties Section(double I) => new SectionProperties
        {
            Id = 0, E = 2.0e11, G = 1.0e11, A = 1.0e-3, Iy = I, Iz = I, J = 1.0e-6, YieldStrength = 1e12, Density = 0, OuterFiberY = 0.1, OuterFiberZ = 0.1,
        };

        static SolverInput Subset(FixtureLoader.FixtureFile f, SectionProperties s, params int[] nodeIds)
        {
            var input = f.ToInput(s);
            input.Nodes.RemoveAll(n => !nodeIds.Contains(n.Id));
            input.Members.RemoveAll(m => !nodeIds.Contains(m.NodeA) || !nodeIds.Contains(m.NodeB));
            return input;
        }

        static void AllFinite(StaticSolveResult r)
        {
            foreach (var d in r.Displacements)
                Assert.True(d.IsFinite);
            foreach (var f in r.MemberForces)
                Assert.True(double.IsFinite(f.Axial) && double.IsFinite(f.MomentLocalYAtA) && double.IsFinite(f.ShearLocalZAtA));
        }

        StaticSolveResult Cantilever(out SolverInput input)
        {
            input = Subset(FixtureLoader.Load("fixture-02-fixed-end-beams.json"), Section(8.0e-6), 0, 1);
            var r = new StaticSolver().Solve(input, new[] { new NodalLoad(1, new Vec3(0, 0, 16000)) });
            AllFinite(r);
            return r;
        }

        [Fact]
        public void FrameFixture2_CantileverTipDeflection_Is0_09m()
        {
            var r = Cantilever(out _);
            Assert.Equal(0.09, r.Displacements[1].Z, 9);
            Assert.Equal(0, r.Displacements[1].X, 9);
            Assert.Equal(0, r.Displacements[1].Y, 9);
        }

        [Fact]
        public void FrameFixture2_CantileverFixedEndMoment_Is48000()
        {
            var f = Cantilever(out _).MemberForces[0];
            Assert.Equal(48000, Math.Abs(f.MomentLocalYAtA), 6);
            Assert.Equal(16000, Math.Abs(f.ShearLocalZAtA), 6);
            Assert.Equal(0, f.Axial, 6);
            Assert.Equal(0, Math.Abs(f.MomentLocalYAtB), 6);
        }

        StaticSolveResult FixedFixed()
        {
            var input = Subset(FixtureLoader.Load("fixture-02-fixed-end-beams.json"), Section(1.0e-5), 10, 11, 12);
            var r = new StaticSolver().Solve(input, new[] { new NodalLoad(11, new Vec3(0, 0, 60000)) });
            AllFinite(r);
            return r;
        }

        [Fact]
        public void FrameFixture2_FixedFixedMidspanDeflection_Is0_01m() => Assert.Equal(0.01, FixedFixed().Displacements[1].Z, 9);

        [Fact]
        public void FrameFixture2_FixedFixedMoments_Are30000()
        {
            var r = FixedFixed();
            foreach (var f in r.MemberForces)
            {
                Assert.Equal(30000, Math.Abs(f.MomentLocalYAtA), 6);
                Assert.Equal(30000, Math.Abs(f.MomentLocalYAtB), 6);
                Assert.Equal(30000, Math.Abs(f.ShearLocalZAtA), 6);
            }
        }

        static double Rectangle(bool diagonal)
        {
            var input = FixtureLoader.Load("fixture-03-unbraced-rectangle.json").ToInput(Section(1e-5));
            if (diagonal)
                input.Members.Add(new Member { Id = 4, NodeA = 0, NodeB = 2 });
            var r = new StaticSolver().Solve(input, new[] { new NodalLoad(2, new Vec3(1000, 0, 0)) });
            AllFinite(r);
            return r.Displacements[2].Length;
        }

        [Fact]
        public void FrameFixture3_UnbracedRectangle_IsFiniteNotAMechanism()
        {
            double d = Rectangle(false);
            Assert.True(double.IsFinite(d) && d > 0);
        }

        [Fact]
        public void FrameFixture3_Diagonal_IsStrictlyStiffer()
        {
            double open = Rectangle(false), braced = Rectangle(true);
            Assert.True(braced < open);
            Assert.True((open - braced) / open >= 1e-3);
        }

        [Fact]
        public void StaticSolve_AllFixedNode_HasZeroDisplacement()
        {
            var input = new SolverInput();
            input.Sections.Add(Section(1e-5));
            input.Nodes.Add(new Node { Id = 0, Position = Vec3.Zero, Fixed = true });
            input.Nodes.Add(new Node { Id = 1, Position = new Vec3(1, 0, 0), Fixed = true });
            input.Members.Add(new Member { Id = 0, NodeA = 0, NodeB = 1 });
            var r = new StaticSolver().Solve(input, new[] { new NodalLoad(1, new Vec3(0, -1000, 0)) });
            foreach (var d in r.Displacements)
                Assert.True(d.Length <= 1e-9);
        }

        [Fact]
        public void StaticSolve_RejectsNaNLoad()
        {
            var input = Subset(FixtureLoader.Load("fixture-02-fixed-end-beams.json"), Section(8e-6), 0, 1);
            Assert.Throws<ArgumentException>(() => new StaticSolver().Solve(input, new[] { new NodalLoad(1, new Vec3(0, 0, double.PositiveInfinity)) }));
        }
    }
}
