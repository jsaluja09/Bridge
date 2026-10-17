using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Square banded matrix with a direct solver (spec 2.12). Only entries with
    /// <c>|row - column| &lt;= HalfBandwidth</c> exist, and storage is
    /// <c>Order * (2 * HalfBandwidth + 1)</c> doubles. Both triangles are stored, so (i, j) and
    /// (j, i) are separate entries and no symmetry is assumed.
    /// <see cref="Factor"/> is banded Gaussian elimination without row exchanges, which keeps the
    /// factors inside the band and suits the symmetric positive definite and diagonally dominant
    /// systems the solver assembles. Use one instance per assembly: add, factor once, solve many times.
    /// </summary>
    public sealed class BandedMatrix : ILinearSolver
    {
        private readonly int storedHalfBandwidth;
        private readonly int rowWidth;
        private readonly double[] band;
        private bool factorCalled;
        private bool factored;

        public BandedMatrix(int order, int halfBandwidth)
        {
            if (order < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(order), order, "Order must not be negative.");
            }

            if (halfBandwidth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfBandwidth), halfBandwidth, "Half-bandwidth must not be negative.");
            }

            Order = order;
            HalfBandwidth = halfBandwidth;
            storedHalfBandwidth = Math.Min(halfBandwidth, Math.Max(order - 1, 0));
            rowWidth = 2 * storedHalfBandwidth + 1;
            band = new double[order * rowWidth];
        }

        /// <summary>Number of rows and columns.</summary>
        public int Order { get; }

        /// <summary>Largest <c>|row - column|</c> that <see cref="Add"/> accepts.</summary>
        public int HalfBandwidth { get; }

        /// <exception cref="ArgumentOutOfRangeException">An index is outside the matrix, or <c>|row - column|</c> exceeds <see cref="HalfBandwidth"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="value"/> is NaN or infinite.</exception>
        /// <exception cref="InvalidOperationException"><see cref="Factor"/> has already been called.</exception>
        public void Add(int row, int column, double value)
        {
            if (factorCalled)
            {
                throw new InvalidOperationException("Add is not allowed after Factor. Assemble a new BandedMatrix instead.");
            }

            RequireIndex(row, nameof(row));
            RequireIndex(column, nameof(column));
            if (Math.Abs(row - column) > HalfBandwidth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(column),
                    column,
                    "Entry (" + row + ", " + column + ") is outside the half-bandwidth " + HalfBandwidth + ".");
            }

            if (!double.IsFinite(value))
            {
                throw new ArgumentException("Matrix entries must be finite.", nameof(value));
            }

            band[Slot(row, column)] += value;
        }

        /// <summary>Factors the assembled matrix in place. Call it once, after every <see cref="Add"/>.</summary>
        /// <exception cref="InvalidOperationException">
        /// Factor was already called, or a pivot is zero or not finite (the matrix is singular or needs row exchanges).
        /// </exception>
        public void Factor()
        {
            if (factorCalled)
            {
                throw new InvalidOperationException("Factor has already been called on this BandedMatrix.");
            }

            factorCalled = true;
            for (int k = 0; k < Order; k++)
            {
                double pivot = band[Slot(k, k)];
                if (pivot == 0.0 || !double.IsFinite(pivot))
                {
                    throw new InvalidOperationException(
                        "Pivot " + k + " is " + pivot + ". The matrix is singular or needs row exchanges.");
                }

                int last = Math.Min(Order - 1, k + storedHalfBandwidth);
                for (int i = k + 1; i <= last; i++)
                {
                    int below = Slot(i, k);
                    double multiplier = band[below] / pivot;
                    band[below] = multiplier;
                    if (multiplier == 0.0)
                    {
                        continue;
                    }

                    int target = below + 1;
                    int source = Slot(k, k + 1);
                    for (int j = k + 1; j <= last; j++)
                    {
                        band[target++] -= multiplier * band[source++];
                    }
                }
            }

            factored = true;
        }

        /// <summary>
        /// Solves the factored system for <paramref name="rhs"/>, writing the result into
        /// <paramref name="solution"/>. <paramref name="rhs"/> is not modified.
        /// </summary>
        /// <exception cref="InvalidOperationException"><see cref="Factor"/> has not completed successfully.</exception>
        public void Solve(double[] rhs, double[] solution)
        {
            if (!factored)
            {
                throw new InvalidOperationException("Solve needs a successful Factor first.");
            }

            RequireVector(rhs, nameof(rhs));
            RequireVector(solution, nameof(solution));
            Array.Copy(rhs, solution, Order);

            for (int i = 1; i < Order; i++)
            {
                double sum = solution[i];
                for (int j = Math.Max(0, i - storedHalfBandwidth); j < i; j++)
                {
                    sum -= band[Slot(i, j)] * solution[j];
                }

                solution[i] = sum;
            }

            for (int i = Order - 1; i >= 0; i--)
            {
                double sum = solution[i];
                int last = Math.Min(Order - 1, i + storedHalfBandwidth);
                for (int j = i + 1; j <= last; j++)
                {
                    sum -= band[Slot(i, j)] * solution[j];
                }

                solution[i] = sum / band[Slot(i, i)];
            }
        }

        private int Slot(int row, int column)
        {
            return row * rowWidth + (column - row + storedHalfBandwidth);
        }

        private void RequireIndex(int index, string paramName)
        {
            if (index < 0 || index >= Order)
            {
                throw new ArgumentOutOfRangeException(paramName, index, "Index must be in [0, " + Order + ").");
            }
        }

        private void RequireVector(double[] vector, string paramName)
        {
            if (vector == null)
            {
                throw new ArgumentNullException(paramName);
            }

            if (vector.Length != Order)
            {
                throw new ArgumentException("Length is " + vector.Length + " but the matrix order is " + Order + ".", paramName);
            }
        }
    }
}
