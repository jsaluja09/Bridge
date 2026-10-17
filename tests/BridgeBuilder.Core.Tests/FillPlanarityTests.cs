using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class FillPlanarityTests
    {
        private const double Tolerance = 0.01;
        private const double OffPlaneMeters = 0.05;
        private const double BeyondToleranceMeters = 0.001;
        private const double SnapSpacingMeters = 0.1;
        private const string FillPlanarityPath = "Assets/BridgeBuilder.Core/Model/FillPlanarity.cs";

        private static readonly Vec3 TiltedOrigin = new Vec3(0.3, 1.2, -0.4);
        private static readonly Vec3 TiltedU = new Vec3(2.0, 1.0, 2.0) / 3.0;
        private static readonly Vec3 TiltedV = new Vec3(1.0, 2.0, -2.0) / 3.0;
        private static readonly Vec3 TiltedNormal = new Vec3(-2.0, 2.0, 1.0) / 3.0;

        [Fact]
        public void PlanarQuad_Accepted()
        {
            Assert.True(FillPlanarity.IsPlanar(TiltedQuad(), Tolerance));
            Assert.True(FillPlanarity.IsPlanar(QuadOnFloor(0, 0.0), Tolerance));
        }

        [Fact]
        public void NonPlanarQuad_Rejected()
        {
            Vec3[] quad = TiltedQuad();
            quad[2] = quad[2] + TiltedNormal * OffPlaneMeters;
            AssertFinite(quad);

            double offset = Vec3.Dot(quad[2] - TiltedOrigin, TiltedNormal);
            Assert.False(double.IsNaN(offset) || double.IsInfinity(offset));
            Assert.Equal(OffPlaneMeters, offset, 12);
            Assert.False(FillPlanarity.IsPlanar(quad, Tolerance));
        }

        [Fact]
        public void ToleranceIsInclusive_OnThePlane()
        {
            for (int index = 0; index < 4; index++)
            {
                Assert.True(FillPlanarity.IsPlanar(QuadOnFloor(index, 0.0), Tolerance));
                Assert.True(FillPlanarity.IsPlanar(QuadOnFloor(index, 0.0), 0.0));
                Assert.True(FillPlanarity.IsPlanar(QuadOnFloor(index, Tolerance), Tolerance));
                Assert.True(FillPlanarity.IsPlanar(QuadOnFloor(index, -Tolerance), Tolerance));
                Assert.False(FillPlanarity.IsPlanar(QuadOnFloor(index, Tolerance + BeyondToleranceMeters), Tolerance));
                Assert.False(FillPlanarity.IsPlanar(QuadOnFloor(index, -(Tolerance + BeyondToleranceMeters)), Tolerance));
            }

            Assert.True(FillPlanarity.IsPlanar(AlternatingHexagon(Tolerance), Tolerance));
            Assert.False(FillPlanarity.IsPlanar(AlternatingHexagon(Tolerance + BeyondToleranceMeters), Tolerance));
        }

        [Fact]
        public void DefaultTolerance_ReadsConstant()
        {
            double tolerance = Constants.Fill.PlanarityToleranceMeters;
            Assert.True(FillPlanarity.IsPlanar(QuadOnFloor(3, tolerance)));
            Assert.False(FillPlanarity.IsPlanar(QuadOnFloor(3, 2.0 * tolerance)));
            Assert.True(FillPlanarity.IsPlanar(QuadOnFloor(3, 0.0)));

            Assert.DoesNotContain("0.01", ReadRepoFile(FillPlanarityPath), StringComparison.Ordinal);
        }

        [Fact]
        public void SnapNodeLoop_WithCollinearRuns_ChecksEveryVertex()
        {
            Vec3[] floor = SnapNodeRectangle(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, 4, 2);
            Assert.Equal(12, floor.Length);
            Assert.True(FillPlanarity.IsPlanar(floor, Tolerance));
            for (int index = 0; index < floor.Length; index++)
            {
                Vec3[] atTolerance = (Vec3[])floor.Clone();
                atTolerance[index] = atTolerance[index] + Vec3.UnitY * Tolerance;
                Assert.True(FillPlanarity.IsPlanar(atTolerance, Tolerance), "Vertex " + index + " at the tolerance should pass.");

                Vec3[] offPlane = (Vec3[])floor.Clone();
                offPlane[index] = offPlane[index] + Vec3.UnitY * OffPlaneMeters;
                Assert.False(FillPlanarity.IsPlanar(offPlane, Tolerance), "Vertex " + index + " off the plane should fail.");
            }

            Vec3[] tilted = SnapNodeRectangle(TiltedOrigin, TiltedU, TiltedV, 4, 2);
            Assert.True(FillPlanarity.IsPlanar(tilted, Tolerance));
            tilted[5] = tilted[5] + TiltedNormal * OffPlaneMeters;
            AssertFinite(tilted);
            Assert.False(FillPlanarity.IsPlanar(tilted, Tolerance));
        }

        [Fact]
        public void DegenerateLoop_Rejected()
        {
            Assert.False(FillPlanarity.IsPlanar(Array.Empty<Vec3>(), Tolerance));
            Assert.False(FillPlanarity.IsPlanar(new[] { Vec3.Zero, Vec3.UnitX }, Tolerance));
            Assert.False(FillPlanarity.IsPlanar(new[] { Vec3.Zero, Vec3.UnitX, new Vec3(2.0, 0.0, 0.0), new Vec3(3.0, 0.0, 0.0) }, Tolerance));
            Assert.False(FillPlanarity.IsPlanar(new[] { Vec3.UnitY, Vec3.UnitY, Vec3.UnitY }, Tolerance));
            Assert.True(FillPlanarity.IsPlanar(new[] { Vec3.Zero, Vec3.UnitX, new Vec3(0.3, 0.7, 0.2) }, Tolerance));
        }

        [Fact]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => FillPlanarity.IsPlanar(null));
            Assert.Throws<ArgumentNullException>(() => FillPlanarity.IsPlanar(null, Tolerance));

            Vec3[] quad = QuadOnFloor(0, 0.0);
            Assert.Throws<ArgumentOutOfRangeException>(() => FillPlanarity.IsPlanar(quad, -Tolerance));
            Assert.Throws<ArgumentOutOfRangeException>(() => FillPlanarity.IsPlanar(quad, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FillPlanarity.IsPlanar(quad, double.PositiveInfinity));

            quad[1] = new Vec3(double.PositiveInfinity, 0.0, 0.0);
            Assert.Throws<ArgumentException>(() => FillPlanarity.IsPlanar(quad, Tolerance));
        }

        /// <summary>A 1 m square on the floor plane (Y = 0) with vertex <paramref name="index"/> raised by <paramref name="offsetMeters"/>.</summary>
        private static Vec3[] QuadOnFloor(int index, double offsetMeters)
        {
            var quad = new[]
            {
                new Vec3(0.0, 0.0, 0.0),
                new Vec3(1.0, 0.0, 0.0),
                new Vec3(1.0, 0.0, 1.0),
                new Vec3(0.0, 0.0, 1.0),
            };
            quad[index] = new Vec3(quad[index].X, offsetMeters, quad[index].Z);
            AssertFinite(quad);
            return quad;
        }

        /// <summary>
        /// A regular hexagon of radius 1 m with every other vertex raised by <paramref name="raiseMeters"/>.
        /// Tilting the plane only makes it worse, so the best plane leaves vertices exactly at the raise.
        /// </summary>
        private static Vec3[] AlternatingHexagon(double raiseMeters)
        {
            var hexagon = new Vec3[6];
            for (int i = 0; i < hexagon.Length; i++)
            {
                double angle = i * Math.PI / 3.0;
                hexagon[i] = new Vec3(Math.Cos(angle), i % 2 == 0 ? 0.0 : raiseMeters, Math.Sin(angle));
            }

            AssertFinite(hexagon);
            return hexagon;
        }

        /// <summary>An irregular convex quad in a plane tilted off every axis.</summary>
        private static Vec3[] TiltedQuad()
        {
            var quad = new[]
            {
                InPlane(TiltedOrigin, TiltedU, TiltedV, 0.0, 0.0),
                InPlane(TiltedOrigin, TiltedU, TiltedV, 1.2, 0.0),
                InPlane(TiltedOrigin, TiltedU, TiltedV, 1.0, 0.8),
                InPlane(TiltedOrigin, TiltedU, TiltedV, 0.1, 0.6),
            };
            AssertFinite(quad);
            return quad;
        }

        /// <summary>A rectangle loop with a node at every snap spacing along each edge, as equal-segment snapping produces.</summary>
        private static Vec3[] SnapNodeRectangle(Vec3 origin, Vec3 u, Vec3 v, int segmentsU, int segmentsV)
        {
            var loop = new List<Vec3>();
            for (int i = 0; i < segmentsU; i++)
            {
                loop.Add(InPlane(origin, u, v, i * SnapSpacingMeters, 0.0));
            }

            for (int j = 0; j < segmentsV; j++)
            {
                loop.Add(InPlane(origin, u, v, segmentsU * SnapSpacingMeters, j * SnapSpacingMeters));
            }

            for (int i = segmentsU; i > 0; i--)
            {
                loop.Add(InPlane(origin, u, v, i * SnapSpacingMeters, segmentsV * SnapSpacingMeters));
            }

            for (int j = segmentsV; j > 0; j--)
            {
                loop.Add(InPlane(origin, u, v, 0.0, j * SnapSpacingMeters));
            }

            Vec3[] vertices = loop.ToArray();
            AssertFinite(vertices);
            return vertices;
        }

        private static Vec3 InPlane(Vec3 origin, Vec3 u, Vec3 v, double alongU, double alongV)
        {
            return origin + u * alongU + v * alongV;
        }

        private static void AssertFinite(Vec3[] vertices)
        {
            foreach (Vec3 vertex in vertices)
            {
                Assert.False(double.IsInfinity(vertex.X) || double.IsInfinity(vertex.Y) || double.IsInfinity(vertex.Z), "Non-finite vertex " + vertex + ".");
            }
        }

        private static string ReadRepoFile(string relativePath)
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "dotnet", "BridgeBuilder.sln")))
                {
                    return File.ReadAllText(Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                }
            }

            throw new InvalidOperationException("No directory containing dotnet/BridgeBuilder.sln above " + AppContext.BaseDirectory);
        }
    }
}
