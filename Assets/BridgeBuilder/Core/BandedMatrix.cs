using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Symmetric banded matrix stored as its lower band, row by row, with in-place Cholesky (spec 2.12).
    /// Factor is O(n b^2); Solve and Multiply are O(n b).
    /// </summary>
    public sealed class BandedMatrix
    {
        public int Size { get; private set; }
        public int Band { get; private set; }
        double[] _data = Array.Empty<double>();

        public void Resize(int size, int band)
        {
            Size = size;
            Band = band;
            int needed = size * (band + 1);
            if (_data.Length < needed)
                _data = new double[needed];
            else
                Array.Clear(_data, 0, needed);
        }

        int Index(int i, int j) => i * (Band + 1) + Band - (i - j);

        /// <summary>Adds to A(i,j) and A(j,i). Entries outside the band are a bug in the caller's bandwidth.</summary>
        public void Add(int i, int j, double v)
        {
            if (j > i)
                (i, j) = (j, i);
            _data[Index(i, j)] += v;
        }

        public void Set(int i, int j, double v)
        {
            if (j > i)
                (i, j) = (j, i);
            _data[Index(i, j)] = v;
        }

        public double Get(int i, int j)
        {
            if (j > i)
                (i, j) = (j, i);
            return i - j > Band ? 0.0 : _data[Index(i, j)];
        }

        /// <summary>this = a * x + diag (diag added on the diagonal), keeping a's band.</summary>
        public void SetScaled(BandedMatrix x, double a, double[] diag)
        {
            Resize(x.Size, x.Band);
            int count = Size * (Band + 1);
            for (int k = 0; k < count; k++)
                _data[k] = a * x._data[k];
            for (int i = 0; i < Size; i++)
                _data[Index(i, i)] += diag[i];
        }

        public void CopyFrom(BandedMatrix x) => SetScaled(x, 1.0, new double[x.Size]);

        /// <summary>In-place Cholesky, A = L L^T. False if the matrix isn't positive definite.</summary>
        public bool Factor()
        {
            int n = Size, b = Band, w = b + 1;
            var a = _data;
            for (int i = 0; i < n; i++)
            {
                int kmin = Math.Max(0, i - b);
                for (int j = kmin; j <= i; j++)
                {
                    double sum = a[i * w + b - (i - j)];
                    int rowI = i * w + b - i;
                    int rowJ = j * w + b - j;
                    int k0 = Math.Max(kmin, j - b);
                    for (int k = k0; k < j; k++)
                        sum -= a[rowI + k] * a[rowJ + k];
                    if (i == j)
                    {
                        if (sum <= 1e-30)
                            return false;
                        a[rowI + i] = Math.Sqrt(sum);
                    }
                    else
                    {
                        a[rowI + j] = sum / a[rowJ + j];
                    }
                }
            }
            return true;
        }

        /// <summary>Solves L L^T x = rhs in place, after Factor.</summary>
        public void Solve(double[] x)
        {
            int n = Size, b = Band, w = b + 1;
            var a = _data;
            for (int i = 0; i < n; i++)
            {
                int row = i * w + b - i;
                double sum = x[i];
                for (int k = Math.Max(0, i - b); k < i; k++)
                    sum -= a[row + k] * x[k];
                x[i] = sum / a[row + i];
            }
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = x[i];
                int kmax = Math.Min(n - 1, i + b);
                for (int k = i + 1; k <= kmax; k++)
                    sum -= a[k * w + b - k + i] * x[k];
                x[i] = sum / a[i * w + b];
            }
        }

        /// <summary>y = A x for the unfactored symmetric matrix.</summary>
        public void Multiply(double[] x, double[] y)
        {
            int n = Size, b = Band, w = b + 1;
            var a = _data;
            Array.Clear(y, 0, n);
            for (int i = 0; i < n; i++)
            {
                int row = i * w + b - i;
                for (int j = Math.Max(0, i - b); j < i; j++)
                {
                    double v = a[row + j];
                    y[i] += v * x[j];
                    y[j] += v * x[i];
                }
                y[i] += a[row + i] * x[i];
            }
        }

        /// <summary>
        /// Reverse Cuthill-McKee ordering of graph nodes, for a narrow band. order[node] = position, -1 if
        /// the node isn't included. Returns the largest |order[a] - order[b]| over the edges.
        /// </summary>
        public static int ReverseCuthillMcKee(int nodeCount, bool[] include, List<int>[] adjacency, int[] order)
        {
            var visited = new bool[nodeCount];
            var sequence = new List<int>(nodeCount);
            var neighbors = new List<int>();

            while (true)
            {
                // Start each component at its lowest-degree node, a cheap stand-in for a peripheral node.
                int startNode = -1;
                for (int i = 0; i < nodeCount; i++)
                {
                    if (include[i] && !visited[i] && (startNode < 0 || adjacency[i].Count < adjacency[startNode].Count))
                        startNode = i;
                }
                if (startNode < 0)
                    break;

                var queue = new Queue<int>();
                queue.Enqueue(startNode);
                visited[startNode] = true;
                while (queue.Count > 0)
                {
                    int v = queue.Dequeue();
                    sequence.Add(v);
                    neighbors.Clear();
                    foreach (int nb in adjacency[v])
                    {
                        if (include[nb] && !visited[nb])
                        {
                            visited[nb] = true;
                            neighbors.Add(nb);
                        }
                    }
                    neighbors.Sort((x, y) => adjacency[x].Count.CompareTo(adjacency[y].Count));
                    foreach (int nb in neighbors)
                        queue.Enqueue(nb);
                }
            }

            for (int i = 0; i < nodeCount; i++)
                order[i] = -1;
            for (int k = 0; k < sequence.Count; k++)
                order[sequence[k]] = sequence.Count - 1 - k;

            int width = 0;
            for (int i = 0; i < nodeCount; i++)
            {
                if (order[i] < 0)
                    continue;
                foreach (int nb in adjacency[i])
                {
                    if (order[nb] >= 0)
                        width = Math.Max(width, Math.Abs(order[i] - order[nb]));
                }
            }
            return width;
        }
    }
}
