using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Symmetric positive-definite banded matrix with in-place Cholesky (spec 2.12). Stores only the lower
    /// band, row by row: no dense n x n array. Factor is O(n b^2); Solve is O(n b).
    /// Add accumulates; adding outside the half-bandwidth throws.
    /// </summary>
    public sealed class BandedMatrix : ILinearSolver
    {
        readonly int _n;
        readonly int _b;
        readonly int _w;
        readonly double[] _a;
        bool _factored;

        public int Order => _n;
        public int HalfBandwidth => _b;

        public BandedMatrix(int order, int halfBandwidth)
        {
            if (order < 0 || halfBandwidth < 0)
                throw new ArgumentOutOfRangeException(nameof(order));
            _n = order;
            _b = Math.Min(halfBandwidth, Math.Max(0, order - 1));
            _w = _b + 1;
            _a = new double[order * _w];
        }

        int Index(int i, int j) => i * _w + _b - (i - j);

        /// <summary>Adds to A(row, column). Symmetric: entries above the diagonal fold onto their mirror.</summary>
        public void Add(int row, int column, double value)
        {
            if (Math.Abs(row - column) > _b)
                throw new ArgumentOutOfRangeException(nameof(column), $"Entry ({row}, {column}) is outside half-bandwidth {_b}");
            if (column > row)
                return;     // the caller adds both halves of a symmetric matrix; keep the lower one
            _a[Index(row, column)] += value;
            _factored = false;
        }

        public double Get(int row, int column)
        {
            if (column > row)
                (row, column) = (column, row);
            return row - column > _b ? 0.0 : _a[Index(row, column)];
        }

        public void Factor()
        {
            for (int i = 0; i < _n; i++)
            {
                int kmin = Math.Max(0, i - _b);
                int rowI = i * _w + _b - i;
                for (int j = kmin; j <= i; j++)
                {
                    int rowJ = j * _w + _b - j;
                    double sum = _a[rowI + j];
                    for (int k = Math.Max(kmin, j - _b); k < j; k++)
                        sum -= _a[rowI + k] * _a[rowJ + k];
                    if (i == j)
                    {
                        if (!(sum > 0))
                            throw new InvalidOperationException("Matrix is not positive definite (a mechanism or a bad constraint)");
                        _a[rowI + i] = Math.Sqrt(sum);
                    }
                    else
                    {
                        _a[rowI + j] = sum / _a[rowJ + j];
                    }
                }
            }
            _factored = true;
        }

        public void Solve(double[] rhs, double[] solution)
        {
            if (!_factored)
                throw new InvalidOperationException("Factor before Solve");
            if (!ReferenceEquals(rhs, solution))
                Array.Copy(rhs, solution, _n);
            var x = solution;
            for (int i = 0; i < _n; i++)
            {
                int row = i * _w + _b - i;
                double sum = x[i];
                for (int k = Math.Max(0, i - _b); k < i; k++)
                    sum -= _a[row + k] * x[k];
                x[i] = sum / _a[row + i];
            }
            for (int i = _n - 1; i >= 0; i--)
            {
                double sum = x[i];
                int kmax = Math.Min(_n - 1, i + _b);
                for (int k = i + 1; k <= kmax; k++)
                    sum -= _a[k * _w + _b - k + i] * x[k];
                x[i] = sum / _a[i * _w + _b];
            }
        }
    }
}
