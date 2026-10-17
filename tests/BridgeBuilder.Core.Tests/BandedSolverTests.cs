using System;
using System.Collections.Generic;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W1-02: banded storage and reverse Cuthill-McKee.</summary>
    public class BandedSolverTests
    {
        static List<(int, int)> Path(int n)
        {
            var edges = new List<(int, int)>();
            for (int i = 0; i + 1 < n; i++)
                edges.Add((i, i + 1));
            return edges;
        }

        [Fact]
        public void Rcm_ReducesBandwidth_OnAPathGraph()
        {
            var edges = Path(8);
            var perm = ReverseCuthillMcKee.Order(8, edges);
            Assert.True(ReverseCuthillMcKee.Bandwidth(perm, edges) <= 2);
        }

        [Fact]
        public void Rcm_IsDeterministic() => Assert.Equal(ReverseCuthillMcKee.Order(8, Path(8)), ReverseCuthillMcKee.Order(8, Path(8)));

        static double A(int i, int j) => i == j ? 4 : Math.Abs(i - j) == 1 ? -1 : Math.Abs(i - j) == 2 ? -0.25 : 0;

        static double[] BandedSolve()
        {
            const int n = 12;
            var m = new BandedMatrix(n, 2);
            for (int i = 0; i < n; i++)
                for (int j = Math.Max(0, i - 2); j <= Math.Min(n - 1, i + 2); j++)
                    m.Add(i, j, A(i, j));
            m.Factor();
            var rhs = new double[n];
            for (int i = 0; i < n; i++)
                rhs[i] = 1;
            var x = new double[n];
            m.Solve(rhs, x);
            return x;
        }

        static double[] DenseSolve()
        {
            const int n = 12;
            var a = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    a[i, j] = A(i, j);
                a[i, n] = 1;
            }
            for (int col = 0; col < n; col++)
            {
                int p = col;
                for (int r = col + 1; r < n; r++)
                    if (Math.Abs(a[r, col]) > Math.Abs(a[p, col])) p = r;
                for (int c = 0; c <= n; c++)
                    (a[col, c], a[p, c]) = (a[p, c], a[col, c]);
                for (int r = col + 1; r < n; r++)
                {
                    double f = a[r, col] / a[col, col];
                    for (int c = col; c <= n; c++)
                        a[r, c] -= f * a[col, c];
                }
            }
            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double s = a[r, n];
                for (int c = r + 1; c < n; c++)
                    s -= a[r, c] * x[c];
                x[r] = s / a[r, r];
            }
            return x;
        }

        [Fact]
        public void BandedSolve_MatchesDenseReference()
        {
            var banded = BandedSolve();
            var dense = DenseSolve();
            for (int i = 0; i < banded.Length; i++)
                Assert.True(Math.Abs(banded[i] - dense[i]) <= 1e-8);
        }

        [Fact]
        public void BandedSolve_NoNaNOrInfinity()
        {
            foreach (double v in BandedSolve())
                Assert.True(double.IsFinite(v));
        }

        [Fact]
        public void Banded_AddOutsideBand_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => new BandedMatrix(10, 2).Add(5, 1, 1));
    }
}
