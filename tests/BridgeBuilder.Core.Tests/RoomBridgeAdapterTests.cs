using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class RoomBridgeAdapterTests
    {
        private const double Scale = 20.0;
        private const double Tolerance = 1e-15;

        [Fact]
        public void Scale_MultipliesCoordinates()
        {
            Assert.Equal(new Vec3(20, 40, 60), RoomBridgeAdapter.ToBridgePosition(new Vec3(1, 2, 3), Scale));
            Assert.Equal(new Vec3(-10, 0, 5), RoomBridgeAdapter.ToBridgePosition(new Vec3(-0.5, 0, 0.25), Scale));
            Assert.Equal(40.0, RoomBridgeAdapter.ToBridgeLength(2.0, Scale));
            Assert.Equal(-20.0, RoomBridgeAdapter.ToBridgeLength(-1.0, Scale));

            SolverInput room = TwoNodeInput(Scale);
            SolverInput bridge = RoomBridgeAdapter.ToBridge(room);

            Assert.Equal(new[] { 4, 9 }, bridge.Nodes.Select(n => n.Id));
            Assert.Equal(new Vec3(20, 40, 60), bridge.Nodes[0].Position);
            Assert.Equal(new Vec3(-10, 0, 5), bridge.Nodes[1].Position);
            Assert.True(bridge.Nodes[0].Fixed);
            Assert.Equal(1, bridge.Nodes[0].SurfaceId);
            Assert.False(bridge.Nodes[0].GroundAnchor);
            Assert.False(bridge.Nodes[1].Fixed);
            Assert.Null(bridge.Nodes[1].SurfaceId);
            Assert.True(bridge.Nodes[1].GroundAnchor);
            Assert.Equal(1.0, bridge.ScaleFactor);
            AssertFinite(bridge.Nodes.Select(n => n.Position));

            Assert.NotSame(room.Nodes, bridge.Nodes);
            Assert.NotSame(room.Nodes[0], bridge.Nodes[0]);
            Assert.Equal(new Vec3(1, 2, 3), room.Nodes[0].Position);
            Assert.Equal(new Vec3(-0.5, 0, 0.25), room.Nodes[1].Position);
            Assert.Equal(Scale, room.ScaleFactor);

            SolverInput again = RoomBridgeAdapter.ToBridge(bridge);
            Assert.Equal(bridge.Nodes.Select(n => n.Position), again.Nodes.Select(n => n.Position));
        }

        [Fact]
        public void Scale_DividesDisplacements()
        {
            var bridgeDisplacement = new Vec3(0.2, 0.2, 0.2);
            AssertComponents(0.01, RoomBridgeAdapter.ToRoomDisplacement(bridgeDisplacement, Scale));
            Assert.Equal(0.01, RoomBridgeAdapter.ToRoomLength(0.2, Scale), Tolerance);
            Assert.Equal(-0.01, RoomBridgeAdapter.ToRoomLength(-0.2, Scale), Tolerance);

            var rotation = new Vec3(0.001, -0.002, 0.003);
            var bridgeResult = new StaticSolveResult
            {
                Displacements = new[] { Vec3.Zero, bridgeDisplacement },
                Rotations = new[] { Vec3.Zero, rotation },
            };
            StaticSolveResult roomResult = RoomBridgeAdapter.ToRoom(bridgeResult, Scale);
            Assert.Equal(2, roomResult.Displacements.Length);
            Assert.Equal(Vec3.Zero, roomResult.Displacements[0]);
            AssertComponents(0.01, roomResult.Displacements[1]);
            Assert.Same(bridgeResult.Rotations, roomResult.Rotations);
            Assert.Equal(rotation, roomResult.Rotations[1]);
            Assert.NotSame(bridgeResult.Displacements, roomResult.Displacements);
            Assert.Equal(bridgeDisplacement, bridgeResult.Displacements[1]);
            AssertFinite(roomResult.Displacements);

            var bridgeStep = new StepOutput
            {
                Displacements = new[] { bridgeDisplacement, Vec3.Zero },
                Rotations = new[] { rotation, Vec3.Zero },
            };
            StepOutput roomStep = RoomBridgeAdapter.ToRoom(bridgeStep, Scale);
            AssertComponents(0.01, roomStep.Displacements[0]);
            Assert.Equal(Vec3.Zero, roomStep.Displacements[1]);
            Assert.Same(bridgeStep.Rotations, roomStep.Rotations);
            Assert.Equal(rotation, roomStep.Rotations[0]);
            Assert.Equal(bridgeDisplacement, bridgeStep.Displacements[0]);
            AssertFinite(roomStep.Displacements);
        }

        [Fact]
        public void Scale_SectionAreaUnchanged()
        {
            var section = new SectionProperties
            {
                Id = 1,
                E = 2.0e11,
                G = 7.7e10,
                A = 1.0e-3,
                Iy = 8.0e-6,
                Iz = 4.0e-6,
                J = 1.0e-6,
                YieldStrength = 2.5e8,
                Density = 7850.0,
                Cy = 0.05,
                Cz = 0.04,
            };
            SolverInput room = TwoNodeInput(Scale);
            room.Sections = new[] { section };

            SolverInput bridge = RoomBridgeAdapter.ToBridge(room);

            Assert.Same(room.Sections, bridge.Sections);
            SectionProperties passed = Assert.Single(bridge.Sections);
            Assert.Equal(1.0e-3, passed.A);
            Assert.Equal(1, passed.Id);
            Assert.Equal(2.0e11, passed.E);
            Assert.Equal(7.7e10, passed.G);
            Assert.Equal(8.0e-6, passed.Iy);
            Assert.Equal(4.0e-6, passed.Iz);
            Assert.Equal(1.0e-6, passed.J);
            Assert.Equal(2.5e8, passed.YieldStrength);
            Assert.Equal(7850.0, passed.Density);
            Assert.Equal(0.05, passed.Cy);
            Assert.Equal(0.04, passed.Cz);
            Assert.Same(room.Members, bridge.Members);
            Assert.Same(room.Fills, bridge.Fills);
        }

        [Fact]
        public void Scale_ForceUnchanged()
        {
            var force = new MemberForce
            {
                MemberId = 2,
                Axial = 1000.0,
                ShearLocalZAtA = 1000.0,
                MomentLocalYAtA = 1000.0,
                ShearLocalZAtB = -1000.0,
                MomentLocalYAtB = -1000.0,
                ShearLocalYAtA = 1000.0,
                MomentLocalZAtA = 1000.0,
                ShearLocalYAtB = -1000.0,
                MomentLocalZAtB = -1000.0,
            };
            var bridgeResult = new StaticSolveResult
            {
                Displacements = new[] { Vec3.Zero, new Vec3(0, -0.2, 0) },
                Rotations = new[] { Vec3.Zero, Vec3.Zero },
                MemberForces = new[] { force },
            };

            StaticSolveResult roomResult = RoomBridgeAdapter.ToRoom(bridgeResult, Scale);

            Assert.Same(bridgeResult.MemberForces, roomResult.MemberForces);
            MemberForce passed = Assert.Single(roomResult.MemberForces);
            Assert.Equal(1000.0, passed.Axial);
            Assert.Equal(1000.0, passed.ShearLocalZAtA);
            Assert.Equal(1000.0, passed.MomentLocalYAtA);
            Assert.Equal(-1000.0, passed.ShearLocalZAtB);
            Assert.Equal(-1000.0, passed.MomentLocalYAtB);
            Assert.Equal(1000.0, passed.ShearLocalYAtA);
            Assert.Equal(1000.0, passed.MomentLocalZAtA);
            Assert.Equal(-1000.0, passed.ShearLocalYAtB);
            Assert.Equal(-1000.0, passed.MomentLocalZAtB);

            var wheel = new WheelLoad { WheelIndex = 0, NormalLoad = 1000.0, Position = new Vec3(10, 2, 0) };
            var car = new CarState { Position = new Vec3(10, 2.5, 0), Speed = 5.0, WheelLoads = new[] { wheel } };
            var bridgeStep = new StepOutput
            {
                Displacements = new[] { Vec3.Zero, new Vec3(0, -0.2, 0) },
                MemberForces = new[] { force },
                FailedMemberIds = new[] { 5 },
                CarState = car,
            };

            StepOutput roomStep = RoomBridgeAdapter.ToRoom(bridgeStep, Scale);

            Assert.Same(bridgeStep.MemberForces, roomStep.MemberForces);
            Assert.Equal(1000.0, Assert.Single(roomStep.MemberForces).Axial);
            Assert.Same(bridgeStep.FailedMemberIds, roomStep.FailedMemberIds);
            Assert.Same(car, roomStep.CarState);
            Assert.Equal(1000.0, Assert.Single(roomStep.CarState.WheelLoads).NormalLoad);
            Assert.Equal(new Vec3(10, 2.5, 0), roomStep.CarState.Position);
        }

        [Fact]
        public void DefaultScale_ReadsConstant()
        {
            Assert.Equal(20.0, Constants.Scale.RoomToBridge);
            double constant = Constants.Scale.RoomToBridge;

            var point = new Vec3(1, 2, 3);
            Assert.Equal(RoomBridgeAdapter.ToBridgePosition(point, constant), RoomBridgeAdapter.ToBridgePosition(point));
            Assert.Equal(RoomBridgeAdapter.ToBridgeLength(1.5, constant), RoomBridgeAdapter.ToBridgeLength(1.5));

            var displacement = new Vec3(0.2, -0.4, 0.6);
            Assert.Equal(RoomBridgeAdapter.ToRoomDisplacement(displacement, constant), RoomBridgeAdapter.ToRoomDisplacement(displacement));
            Assert.Equal(RoomBridgeAdapter.ToRoomLength(0.2, constant), RoomBridgeAdapter.ToRoomLength(0.2));

            var bridgeResult = new StaticSolveResult { Displacements = new[] { displacement, Vec3.Zero } };
            Assert.Equal(RoomBridgeAdapter.ToRoom(bridgeResult, constant).Displacements, RoomBridgeAdapter.ToRoom(bridgeResult).Displacements);

            var bridgeStep = new StepOutput { Displacements = new[] { Vec3.Zero, displacement } };
            Assert.Equal(RoomBridgeAdapter.ToRoom(bridgeStep, constant).Displacements, RoomBridgeAdapter.ToRoom(bridgeStep).Displacements);

            SolverInput bridge = RoomBridgeAdapter.ToBridge(TwoNodeInput(constant));
            Assert.Equal(RoomBridgeAdapter.ToBridgePosition(point), bridge.Nodes[0].Position);

            string adapter = File.ReadAllText(RepoPath("Assets/BridgeBuilder.Core/Scale/RoomBridgeAdapter.cs"));
            Assert.DoesNotMatch(@"(?<![\w.])20(?!\w)", adapter);

            string constants = File.ReadAllText(RepoPath("Assets/BridgeBuilder.Core/Constants.Scale.cs"));
            Assert.Contains("GUESS", constants);
            Assert.Contains("Spec 2.6", constants);
            Assert.Contains("section 8", constants);
            Assert.DoesNotContain("DERIVED", constants);
            Assert.DoesNotContain("HUMAN_OWNED", constants);
        }

        [Fact]
        public void Scale_FiniteInput_NoNaNOrInfinity()
        {
            double[] scales = { 10.0, 20.0, 40.0, 0.5, 1e-3 };
            Vec3[] values =
            {
                Vec3.Zero,
                new Vec3(1, 2, 3),
                new Vec3(-1.25, 0.75, -3.5),
                new Vec3(1e-300, -1e-300, 4.9e-324),
                new Vec3(1e300, -1e300, 0.1),
            };

            foreach (double scale in scales)
            {
                AssertFinite(values.Select(v => RoomBridgeAdapter.ToBridgePosition(v, scale)));
                AssertFinite(values.Select(v => RoomBridgeAdapter.ToRoomDisplacement(v, scale)));
                Assert.All(values, v => Assert.True(double.IsFinite(RoomBridgeAdapter.ToBridgeLength(v.X, scale))));
                Assert.All(values, v => Assert.True(double.IsFinite(RoomBridgeAdapter.ToRoomLength(v.Y, scale))));

                var room = new SolverInput
                {
                    Nodes = values.Select((v, i) => new Node { Id = i, Position = v }).ToArray(),
                    ScaleFactor = scale,
                };
                AssertFinite(RoomBridgeAdapter.ToBridge(room).Nodes.Select(n => n.Position));
                AssertFinite(RoomBridgeAdapter.ToRoom(new StaticSolveResult { Displacements = values }, scale).Displacements);
                AssertFinite(RoomBridgeAdapter.ToRoom(new StepOutput { Displacements = values }, scale).Displacements);
            }

            var huge = new Vec3(double.MaxValue, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridgePosition(huge, Scale));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoomDisplacement(huge, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridgeLength(double.MaxValue, Scale));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoomLength(double.MaxValue, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridge(new SolverInput { Nodes = new[] { new Node { Position = huge } }, ScaleFactor = Scale }));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoom(new StaticSolveResult { Displacements = new[] { huge } }, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoom(new StepOutput { Displacements = new[] { huge } }, 0.5));

            var infinite = new Vec3(0, double.PositiveInfinity, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridgePosition(infinite, Scale));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoomDisplacement(infinite, Scale));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridgeLength(double.NegativeInfinity, Scale));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoomLength(double.NaN, Scale));
        }

        [Fact]
        public void Scale_NonPositive_Throws()
        {
            var point = new Vec3(1, 2, 3);
            var bridgeResult = new StaticSolveResult { Displacements = new[] { point } };
            var bridgeStep = new StepOutput { Displacements = new[] { point } };

            foreach (double scale in new[] { 0.0, -20.0, -0.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridgePosition(point, scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridgeLength(1.0, scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoomDisplacement(point, scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoomLength(1.0, scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoom(bridgeResult, scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToRoom(bridgeStep, scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridge(TwoNodeInput(scale)));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridge(new SolverInput()));
        }

        [Fact]
        public void Adapter_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => RoomBridgeAdapter.ToBridge(null));
            Assert.Throws<ArgumentNullException>(() => RoomBridgeAdapter.ToRoom((StaticSolveResult)null, Scale));
            Assert.Throws<ArgumentNullException>(() => RoomBridgeAdapter.ToRoom((StepOutput)null, Scale));
            Assert.Throws<ArgumentNullException>(() => RoomBridgeAdapter.ToRoom((StaticSolveResult)null));
            Assert.Throws<ArgumentNullException>(() => RoomBridgeAdapter.ToRoom((StepOutput)null));

            Assert.Throws<ArgumentException>(() => RoomBridgeAdapter.ToBridge(new SolverInput { Nodes = null, ScaleFactor = Scale }));
            Assert.Throws<ArgumentException>(() => RoomBridgeAdapter.ToBridge(new SolverInput { Nodes = new Node[] { null }, ScaleFactor = Scale }));
            Assert.Throws<ArgumentException>(() => RoomBridgeAdapter.ToRoom(new StaticSolveResult { Displacements = null }, Scale));
            Assert.Throws<ArgumentException>(() => RoomBridgeAdapter.ToRoom(new StepOutput { Displacements = null }, Scale));
        }

        private static SolverInput TwoNodeInput(double scaleFactor)
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 4, Position = new Vec3(1, 2, 3), Fixed = true, SurfaceId = 1 },
                    new Node { Id = 9, Position = new Vec3(-0.5, 0, 0.25), GroundAnchor = true },
                },
                Members = new[] { new Member { Id = 2, NodeA = 4, NodeB = 9, SectionId = 1, LineId = 3 } },
                Fills = Array.Empty<Fill>(),
                ScaleFactor = scaleFactor,
            };
        }

        private static void AssertComponents(double expected, Vec3 actual)
        {
            Assert.Equal(expected, actual.X, Tolerance);
            Assert.Equal(expected, actual.Y, Tolerance);
            Assert.Equal(expected, actual.Z, Tolerance);
        }

        private static void AssertFinite(IEnumerable<Vec3> values)
        {
            foreach (Vec3 value in values)
            {
                Assert.True(double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z), value + " is not finite.");
            }
        }

        private static string RepoPath(string relativePath)
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "dotnet", "BridgeBuilder.sln")))
                {
                    return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                }
            }

            throw new InvalidOperationException("No directory containing dotnet/BridgeBuilder.sln above " + AppContext.BaseDirectory);
        }
    }
}
