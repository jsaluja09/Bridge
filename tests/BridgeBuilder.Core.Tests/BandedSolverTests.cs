using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class BandedSolverTests
    {
        private const int PathVertexCount = 8;
        private const int AcceptanceOrder = 12;
        private const int AcceptanceHalfBandwidth = 2;
        private const double DenseTolerance = 1e-8;

        [Fact]
        public void Rcm_ReducesBandwidth_OnAPathGraph()
        {
            (int A, int B)[] edges = PathEdges(PathVertexCount);

            int[] permutation = ReverseCuthillMcKee.Compute(PathVertexCount, edges);

            AssertIsPermutation(permutation, PathVertexCount);
            Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 1, 0 }, permutation);
            int bandwidth = BandwidthOf(permutation, edges);
            Assert.True(bandwidth <= 2, "RCM bandwidth is " + bandwidth + ".");
            Assert.Equal(bandwidth, ReverseCuthillMcKee.Bandwidth(permutation, edges));
        }

        [Fact]
        public void Rcm_IsDeterministic()
        {
            (int A, int B)[] edges = PathEdges(PathVertexCount);

            int[] first = ReverseCuthillMcKee.Compute(PathVertexCount, edges);
            int[] second = ReverseCuthillMcKee.Compute(PathVertexCount, edges);

            Assert.Equal(first, second);
            (int A, int B)[] reordered = Enumerable.Reverse(edges).Select(e => (A: e.B, B: e.A)).ToArray();
            Assert.Equal(first, ReverseCuthillMcKee.Compute(PathVertexCount, reordered));
        }

        [Fact]
        public void BandedSolve_MatchesDenseReference()
        {
            double[] banded = SolveAcceptanceSystem();
            double[] dense = DenseSolve(AcceptanceMatrix(), Ones(AcceptanceOrder));

            Assert.Equal(AcceptanceOrder, banded.Length);
            double difference = MaxAbsDifference(banded, dense);
            Assert.True(difference <= DenseTolerance, "Maximum difference from the dense reference is " + difference + ".");
        }

        [Fact]
        public void BandedSolve_NoNaNOrInfinity()
        {
            double[] solution = SolveAcceptanceSystem();

            Assert.Equal(AcceptanceOrder, solution.Length);
            AssertFinite(solution);
        }

        [Fact]
        public void Rcm_FollowsStartVertexTieBreakAndReversalRule()
        {
            // Degrees: 0:4, 1:2, 2:3, 3:1, 4:1, 5:1, 6:2. Start at 3, the lowest of the degree-1 vertices.
            // Visiting 0 numbers 1 and 6 (degree 2, by index) before 2 (degree 3), giving Cuthill-McKee
            // order 3, 0, 1, 6, 2, 5, 4, which is then reversed.
            var edges = new (int A, int B)[] { (3, 0), (0, 6), (0, 1), (0, 2), (1, 2), (2, 4), (6, 5) };

            int[] permutation = ReverseCuthillMcKee.Compute(7, edges);

            Assert.Equal(new[] { 4, 5, 2, 6, 1, 0, 3 }, permutation);
            Assert.Equal(new[] { 5, 4, 2, 6, 0, 1, 3 }, ReverseCuthillMcKee.Inverse(permutation));
        }

        [Fact]
        public void Rcm_NumbersEveryComponentAndIgnoresRepeatedEdgesAndSelfLoops()
        {
            // Distinct-neighbor degrees: 0:1, 1:2, 2:1, 3:0, 4:1, 5:1. Components start at 3, then 0,
            // then 4, giving Cuthill-McKee order 3, 0, 2, 4, 1, 5 before reversal. Counting the repeated
            // edge would start the last component at 5; counting the self-loop would start at 0.
            var edges = new (int A, int B)[] { (4, 1), (1, 4), (1, 5), (3, 3), (0, 2) };

            int[] permutation = ReverseCuthillMcKee.Compute(6, edges);

            Assert.Equal(new[] { 5, 1, 4, 2, 0, 3 }, permutation);
            Assert.Equal(1, ReverseCuthillMcKee.Bandwidth(permutation, edges));
            Assert.Empty(ReverseCuthillMcKee.Compute(0, Array.Empty<(int A, int B)>()));
        }

        [Fact]
        public void Rcm_ReducesBandwidth_OnAScrambledTruss()
        {
            const int panels = 10;
            const int vertexCount = 2 * panels;
            const int naturalBandwidth = 3;
            static int Scrambled(int natural) => 7 * natural % vertexCount;

            var edges = new List<(int A, int B)>();
            for (int i = 0; i < panels; i++)
            {
                int top = 2 * i;
                int bottom = 2 * i + 1;
                edges.Add((Scrambled(top), Scrambled(bottom)));
                if (i + 1 < panels)
                {
                    edges.Add((Scrambled(top), Scrambled(top + 2)));
                    edges.Add((Scrambled(bottom), Scrambled(bottom + 2)));
                    edges.Add((Scrambled(top), Scrambled(bottom + 2)));
                }
            }

            int scrambledBandwidth = BandwidthOf(Enumerable.Range(0, vertexCount).ToArray(), edges);
            int[] permutation = ReverseCuthillMcKee.Compute(vertexCount, edges);

            AssertIsPermutation(permutation, vertexCount);
            int rcmBandwidth = BandwidthOf(permutation, edges);
            Assert.True(scrambledBandwidth > naturalBandwidth, "Scrambled bandwidth is only " + scrambledBandwidth + ".");
            Assert.True(rcmBandwidth <= naturalBandwidth, "RCM bandwidth is " + rcmBandwidth + ".");
            Assert.Equal(rcmBandwidth, ReverseCuthillMcKee.Bandwidth(permutation, edges));
        }

        [Fact]
        public void Rcm_RejectsInvalidInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ReverseCuthillMcKee.Compute(-1, Array.Empty<(int A, int B)>()));
            Assert.Throws<ArgumentNullException>(() => ReverseCuthillMcKee.Compute(3, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReverseCuthillMcKee.Compute(3, new[] { (0, 3) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReverseCuthillMcKee.Compute(3, new[] { (-1, 0) }));
            Assert.Throws<ArgumentException>(() => ReverseCuthillMcKee.Inverse(new[] { 0, 0, 1 }));
            Assert.Throws<ArgumentException>(() => ReverseCuthillMcKee.Inverse(new[] { 0, 3, 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReverseCuthillMcKee.Bandwidth(new[] { 1, 0 }, new[] { (0, 2) }));
        }

        [Fact]
        public void BandedMatrix_RejectsEntriesOutsideTheBand()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BandedMatrix(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BandedMatrix(3, -1));

            var matrix = new BandedMatrix(5, 1);
            Assert.Equal(5, matrix.Order);
            Assert.Equal(1, matrix.HalfBandwidth);
            matrix.Add(2, 1, 1.0);
            matrix.Add(2, 3, 1.0);

            Assert.Throws<ArgumentOutOfRangeException>(() => matrix.Add(0, 2, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => matrix.Add(4, 2, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => matrix.Add(0, 4, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => matrix.Add(5, 5, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => matrix.Add(-1, 0, 1.0));
            Assert.Throws<ArgumentException>(() => matrix.Add(1, 1, double.NaN));
            Assert.Throws<ArgumentException>(() => matrix.Add(1, 1, double.PositiveInfinity));
        }

        [Fact]
        public void BandedMatrix_KeepsBothTrianglesAndAccumulatesAdds()
        {
            const int order = 9;
            var dense = new double[order, order];
            for (int i = 0; i < order; i++)
            {
                dense[i, i] = 6.0;
                if (i + 1 < order)
                {
                    dense[i, i + 1] = -1.5;
                    dense[i + 1, i] = 0.5;
                }

                if (i + 2 < order)
                {
                    dense[i, i + 2] = 0.75;
                    dense[i + 2, i] = -1.25;
                }
            }

            var matrix = new BandedMatrix(order, 2);
            AddNonzeroEntries(matrix, dense, 0.25);
            AddNonzeroEntries(matrix, dense, 0.75);
            matrix.Factor();

            double[][] rightHandSides =
            {
                Enumerable.Range(1, order).Select(i => (double)i).ToArray(),
                Enumerable.Range(0, order).Select(i => i % 2 == 0 ? 1.0 : -2.0).ToArray(),
            };
            foreach (double[] rhs in rightHandSides)
            {
                double[] original = (double[])rhs.Clone();
                var solution = new double[order];
                matrix.Solve(rhs, solution);

                Assert.Equal(original, rhs);
                AssertFinite(solution);
                double difference = MaxAbsDifference(solution, DenseSolve(dense, rhs));
                Assert.True(difference <= DenseTolerance, "Maximum difference from the dense reference is " + difference + ".");
            }
        }

        [Fact]
        public void BandedMatrix_FullWidthBandMatchesDenseReference()
        {
            const int order = 4;
            var dense = new double[order, order]
            {
                { 7.0, 1.0, -2.0, 0.5 },
                { -1.0, 8.0, 1.5, -2.0 },
                { 2.0, -0.5, 9.0, 1.0 },
                { 1.0, 2.5, -1.0, 6.0 },
            };
            double[] rhs = { 1.0, -2.0, 3.0, 0.5 };
            double[] expected = DenseSolve(dense, rhs);

            foreach (int halfBandwidth in new[] { order - 1, order + 5 })
            {
                var matrix = new BandedMatrix(order, halfBandwidth);
                AddNonzeroEntries(matrix, dense, 1.0);
                matrix.Factor();
                var solution = new double[order];
                matrix.Solve(rhs, solution);

                AssertFinite(solution);
                Assert.True(MaxAbsDifference(solution, expected) <= DenseTolerance, "Half-bandwidth " + halfBandwidth + " differs from the dense reference.");
            }
        }

        [Fact]
        public void BandedMatrix_EnforcesAddFactorSolveOrder()
        {
            var matrix = new BandedMatrix(2, 1);
            matrix.Add(0, 0, 2.0);
            matrix.Add(1, 1, 2.0);
            var rhs = new[] { 1.0, 3.0 };
            var solution = new double[2];

            Assert.Throws<InvalidOperationException>(() => matrix.Solve(rhs, solution));
            matrix.Factor();
            Assert.Throws<InvalidOperationException>(() => matrix.Add(0, 0, 1.0));
            Assert.Throws<InvalidOperationException>(() => matrix.Factor());
            Assert.Throws<ArgumentNullException>(() => matrix.Solve(null, solution));
            Assert.Throws<ArgumentNullException>(() => matrix.Solve(rhs, null));
            Assert.Throws<ArgumentException>(() => matrix.Solve(new double[3], solution));
            Assert.Throws<ArgumentException>(() => matrix.Solve(rhs, new double[1]));

            matrix.Solve(rhs, solution);
            Assert.Equal(new[] { 0.5, 1.5 }, solution);
        }

        [Fact]
        public void BandedMatrix_ThrowsOnZeroPivot()
        {
            var singular = new BandedMatrix(2, 1);
            singular.Add(0, 0, 1.0);
            singular.Add(0, 1, 1.0);
            singular.Add(1, 0, 1.0);
            singular.Add(1, 1, 1.0);

            Assert.Throws<InvalidOperationException>(() => singular.Factor());
            Assert.Throws<InvalidOperationException>(() => singular.Solve(new double[2], new double[2]));
            Assert.Throws<InvalidOperationException>(() => new BandedMatrix(3, 1).Factor());
        }

        [Fact]
        public void BandedMatrix_StorageScalesWithBandNotOrderSquared()
        {
            const int order = 20000;
            double[] rhs = Ones(order);
            var solution = new double[order];

            long before = GC.GetAllocatedBytesForCurrentThread();
            var matrix = new BandedMatrix(order, 1);
            for (int i = 0; i < order; i++)
            {
                matrix.Add(i, i, 4.0);
                if (i > 0)
                {
                    matrix.Add(i, i - 1, -1.0);
                    matrix.Add(i - 1, i, -1.0);
                }
            }

            matrix.Factor();
            matrix.Solve(rhs, solution);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            long denseBytes = (long)order * order * sizeof(double);
            Assert.True(allocated < denseBytes / 100, "Allocated " + allocated + " bytes; a dense matrix is " + denseBytes + ".");
            AssertFinite(solution);
        }

        private static (int A, int B)[] PathEdges(int vertexCount)
        {
            return Enumerable.Range(0, vertexCount - 1).Select(i => (A: i, B: i + 1)).ToArray();
        }

        private static int BandwidthOf(int[] permutation, IEnumerable<(int A, int B)> edges)
        {
            var newIndex = new int[permutation.Length];
            for (int i = 0; i < permutation.Length; i++)
            {
                newIndex[permutation[i]] = i;
            }

            int bandwidth = 0;
            foreach ((int a, int b) in edges)
            {
                bandwidth = Math.Max(bandwidth, Math.Abs(newIndex[a] - newIndex[b]));
            }

            return bandwidth;
        }

        private static void AssertIsPermutation(int[] permutation, int count)
        {
            Assert.Equal(Enumerable.Range(0, count), permutation.OrderBy(i => i));
        }

        private static double[,] AcceptanceMatrix()
        {
            var matrix = new double[AcceptanceOrder, AcceptanceOrder];
            for (int i = 0; i < AcceptanceOrder; i++)
            {
                for (int j = 0; j < AcceptanceOrder; j++)
                {
                    int distance = Math.Abs(i - j);
                    matrix[i, j] = distance == 0 ? 4.0 : distance == 1 ? -1.0 : distance == 2 ? -0.25 : 0.0;
                }
            }

            return matrix;
        }

        private static double[] SolveAcceptanceSystem()
        {
            var matrix = new BandedMatrix(AcceptanceOrder, AcceptanceHalfBandwidth);
            AddNonzeroEntries(matrix, AcceptanceMatrix(), 1.0);
            matrix.Factor();
            var solution = new double[AcceptanceOrder];
            matrix.Solve(Ones(AcceptanceOrder), solution);
            return solution;
        }

        private static void AddNonzeroEntries(ILinearSolver solver, double[,] dense, double scale)
        {
            for (int i = 0; i < dense.GetLength(0); i++)
            {
                for (int j = 0; j < dense.GetLength(1); j++)
                {
                    if (dense[i, j] != 0.0)
                    {
                        solver.Add(i, j, scale * dense[i, j]);
                    }
                }
            }
        }

        /// <summary>Dense Gaussian elimination with partial pivoting, the reference for the banded solver.</summary>
        private static double[] DenseSolve(double[,] matrix, double[] rhs)
        {
            int n = rhs.Length;
            var a = (double[,])matrix.Clone();
            var b = (double[])rhs.Clone();
            for (int k = 0; k < n; k++)
            {
                int pivot = k;
                for (int i = k + 1; i < n; i++)
                {
                    if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k]))
                    {
                        pivot = i;
                    }
                }

                Assert.True(a[pivot, k] != 0.0, "Dense reference matrix is singular.");
                if (pivot != k)
                {
                    for (int j = 0; j < n; j++)
                    {
                        (a[k, j], a[pivot, j]) = (a[pivot, j], a[k, j]);
                    }

                    (b[k], b[pivot]) = (b[pivot], b[k]);
                }

                for (int i = k + 1; i < n; i++)
                {
                    double factor = a[i, k] / a[k, k];
                    for (int j = k; j < n; j++)
                    {
                        a[i, j] -= factor * a[k, j];
                    }

                    b[i] -= factor * b[k];
                }
            }

            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = b[i];
                for (int j = i + 1; j < n; j++)
                {
                    sum -= a[i, j] * x[j];
                }

                x[i] = sum / a[i, i];
            }

            return x;
        }

        private static double[] Ones(int count)
        {
            return Enumerable.Repeat(1.0, count).ToArray();
        }

        private static double MaxAbsDifference(double[] actual, double[] expected)
        {
            Assert.Equal(expected.Length, actual.Length);
            double max = 0.0;
            for (int i = 0; i < actual.Length; i++)
            {
                max = Math.Max(max, Math.Abs(actual[i] - expected[i]));
            }

            return max;
        }

        private static void AssertFinite(double[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                Assert.True(double.IsFinite(values[i]), "Component " + i + " is " + values[i] + ".");
            }
        }
    }
}
