using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Wind and snow as constant loads.</summary>
    public class WeatherTests
    {
        const double Scale = 20;

        static CompiledBridge Bare()
        {
            var pads = Rooms.Pads();
            return BridgeCompiler.Compile(Rooms.BareDeck(pads), pads, Scale);
        }

        /// <summary>The road's area in bridge m^2, summed straight from the deck triangles.</summary>
        static double RoadArea(CompiledBridge bridge)
        {
            double area = 0;
            foreach (var deck in bridge.Decks)
            {
                for (int i = 0; i < deck.Triangles.Length; i += 3)
                {
                    Vec3 a = bridge.Input.Nodes[deck.Triangles[i]].Position, b = bridge.Input.Nodes[deck.Triangles[i + 1]].Position, c = bridge.Input.Nodes[deck.Triangles[i + 2]].Position;
                    area += 0.5 * Vec3.Cross(b - a, c - a).Length * Scale * Scale;
                }
            }
            return area;
        }

        [Fact]
        public void Snow_WeighsItsLoadTimesTheRoadArea()
        {
            var bridge = Bare();
            var loads = Weather.SnowLoads(bridge, 1000, Scale);

            double area = RoadArea(bridge);
            Assert.True(area > 70 && area < 90, $"road {area} m^2");       // a 20 m x 4 m road
            Assert.Equal(-1000 * area, loads.Sum(f => f.Y), 6);
            Assert.All(loads, f => Assert.True(f.X == 0 && f.Z == 0 && f.Y <= 0));
        }

        [Fact]
        public void Snow_FallsOnlyOnTheRoad()
        {
            var pads = Rooms.Pads();
            var bridge = BridgeCompiler.Compile(Rooms.Truss(pads), pads, Scale);
            var loads = Weather.SnowLoads(bridge, 1000, Scale);
            var road = bridge.Decks.SelectMany(d => d.Triangles).ToHashSet();

            for (int i = 0; i < loads.Length; i++)
            {
                if (road.Contains(i))
                    Assert.True(loads[i].Y < 0);
                else
                    Assert.Equal(Vec3.Zero, loads[i]);                  // truss members above the road get none
            }
        }

        [Fact]
        public void NoSnow_IsNoLoad()
        {
            Assert.All(Weather.SnowLoads(Bare(), 0, Scale), f => Assert.Equal(Vec3.Zero, f));
            Assert.All(Weather.SnowLoads(Bare(), -5, Scale), f => Assert.Equal(Vec3.Zero, f));
        }

        /// <summary>One 1 m (room) member along x: 20 m in the bridge world.</summary>
        static CompiledBridge OneMember()
        {
            var bridge = new CompiledBridge { Input = new SolverInput { ScaleFactor = Scale } };
            bridge.Input.Nodes.Add(new Node { Id = 5, Position = new Vec3(0, 1, 0), Fixed = true });
            bridge.Input.Nodes.Add(new Node { Id = 9, Position = new Vec3(1, 1, 0) });
            bridge.Input.Members.Add(new Member { Id = 1, NodeA = 5, NodeB = 9 });
            bridge.NodeIndex[5] = 0;
            bridge.NodeIndex[9] = 1;
            return bridge;
        }

        [Fact]
        public void Wind_PushesAcrossMembersOnly()
        {
            var bridge = OneMember();
            Assert.All(Weather.WindLoads(bridge, Vec3.UnitX, 20, Scale), f => Assert.Equal(Vec3.Zero, f));

            var across = Weather.WindLoads(bridge, Vec3.UnitZ, 20, Scale);
            double expected = 0.5 * Constants.Car.AirDensity * 20 * 20 * Constants.Car.MemberDragCoefficient * Constants.Car.MemberDiameter * 20;
            Assert.Equal(expected / 2, across[0].Z, 9);
            Assert.Equal(expected / 2, across[1].Z, 9);
            Assert.All(across, f => Assert.True(f.X == 0 && f.Y == 0));

            // At 45 degrees only the cross-flow half of the wind direction pushes, still square to the member.
            var diagonal = Weather.WindLoads(bridge, new Vec3(1, 0, 1), 20, Scale);
            Assert.Equal(0, diagonal[0].X, 9);
            Assert.Equal(expected / 2 / Math.Sqrt(2), diagonal[0].Z, 9);
        }

        [Fact]
        public void Wind_GrowsWithTheSquareOfSpeed()
        {
            var bridge = Bare();
            double slow = Weather.WindLoads(bridge, Vec3.UnitZ, 10, Scale).Sum(f => f.Z);
            double fast = Weather.WindLoads(bridge, Vec3.UnitZ, 20, Scale).Sum(f => f.Z);
            Assert.Equal(4 * slow, fast, 6);
            Assert.All(Weather.WindLoads(bridge, Vec3.UnitZ, 0, Scale), f => Assert.Equal(Vec3.Zero, f));
        }

        [Fact]
        public void WindOnCar_UsesTheSideOrFrontArea()
        {
            double q = 0.5 * Constants.Car.AirDensity * 20 * 20 * Constants.Car.DragCoefficient;
            Assert.Equal(q * Constants.Car.SideArea, Weather.WindOnCar(Vec3.UnitZ, 20, Vec3.UnitX).Length, 9);
            Assert.Equal(q * Constants.Car.FrontArea, Weather.WindOnCar(-Vec3.UnitX, 20, Vec3.UnitX).Length, 9);
            Assert.Equal(Vec3.Zero, Weather.WindOnCar(Vec3.UnitZ, 0, Vec3.UnitX));
        }

        [Fact]
        public void SnowyRun_LoadsTheBridgeMoreThanAClearOne()
        {
            var pads = Rooms.Pads();
            double Peak(double snow)
            {
                var c = new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), pads[0].Across, Scale);
                c.SnowLoadPa = snow;
                var run = new BridgeRun(BridgeCompiler.Compile(Rooms.Truss(pads), pads, Scale), c, 0);
                for (int i = 0; i < 60; i++)
                    run.Step(1.0 / 60);
                return run.PeakUtilization;
            }
            Assert.True(Peak(Constants.Weather.SnowLoadPa) > Peak(0) * 1.05);
        }
    }
}
