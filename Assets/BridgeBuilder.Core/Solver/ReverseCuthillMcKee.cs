using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Reverse Cuthill-McKee ordering (spec 2.12): renumbers graph vertices so that connected
    /// vertices get nearby indices, which keeps the stiffness matrix inside a narrow band.
    /// A permutation maps new index to old index: <c>permutation[newIndex] = oldIndex</c>.
    /// </summary>
    public static class ReverseCuthillMcKee
    {
        /// <summary>
        /// Orders the vertices <c>0 .. vertexCount - 1</c> of an undirected graph. Start at the lowest
        /// index among the minimum-degree unnumbered vertices. Visit numbered vertices in order and
        /// number each one's unnumbered neighbors by increasing degree, breaking ties by increasing
        /// index. When a component is exhausted, start the next one by the same rule. Finally reverse
        /// the whole order. Degree counts distinct neighbors; repeated edges and self-loops add nothing.
        /// </summary>
        /// <returns><c>permutation[newIndex] = oldIndex</c>.</returns>
        public static int[] Compute(int vertexCount, IEnumerable<(int A, int B)> edges)
        {
            if (vertexCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount), vertexCount, "Vertex count must not be negative.");
            }

            List<int>[] neighbors = DistinctNeighbors(vertexCount, edges);
            var degree = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                degree[v] = neighbors[v].Count;
            }

            Comparison<int> byDegreeThenIndex = (x, y) => degree[x] != degree[y] ? degree[x].CompareTo(degree[y]) : x.CompareTo(y);
            foreach (List<int> list in neighbors)
            {
                list.Sort(byDegreeThenIndex);
            }

            var startCandidates = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                startCandidates[v] = v;
            }

            Array.Sort(startCandidates, byDegreeThenIndex);

            var numbered = new bool[vertexCount];
            var order = new int[vertexCount];
            int count = 0;
            int nextCandidate = 0;
            while (count < vertexCount)
            {
                while (numbered[startCandidates[nextCandidate]])
                {
                    nextCandidate++;
                }

                int start = startCandidates[nextCandidate];
                numbered[start] = true;
                order[count++] = start;
                for (int visit = count - 1; visit < count; visit++)
                {
                    foreach (int neighbor in neighbors[order[visit]])
                    {
                        if (!numbered[neighbor])
                        {
                            numbered[neighbor] = true;
                            order[count++] = neighbor;
                        }
                    }
                }
            }

            Array.Reverse(order);
            return order;
        }

        /// <summary>Returns the inverse of a permutation from <see cref="Compute"/>: <c>inverse[oldIndex] = newIndex</c>.</summary>
        /// <exception cref="ArgumentException"><paramref name="permutation"/> is not a permutation of <c>0 .. Length - 1</c>.</exception>
        public static int[] Inverse(int[] permutation)
        {
            if (permutation == null)
            {
                throw new ArgumentNullException(nameof(permutation));
            }

            var inverse = new int[permutation.Length];
            var seen = new bool[permutation.Length];
            for (int newIndex = 0; newIndex < permutation.Length; newIndex++)
            {
                int oldIndex = permutation[newIndex];
                if (oldIndex < 0 || oldIndex >= permutation.Length || seen[oldIndex])
                {
                    throw new ArgumentException("Not a permutation: entry " + newIndex + " is " + oldIndex + ".", nameof(permutation));
                }

                seen[oldIndex] = true;
                inverse[oldIndex] = newIndex;
            }

            return inverse;
        }

        /// <summary>
        /// The maximum of <c>|newIndex(u) - newIndex(v)|</c> over the edges under
        /// <paramref name="permutation"/> (<c>permutation[newIndex] = oldIndex</c>), or 0 with no edges.
        /// </summary>
        public static int Bandwidth(int[] permutation, IEnumerable<(int A, int B)> edges)
        {
            int[] newIndex = Inverse(permutation);
            if (edges == null)
            {
                throw new ArgumentNullException(nameof(edges));
            }

            int bandwidth = 0;
            foreach ((int a, int b) in edges)
            {
                RequireVertex(a, newIndex.Length);
                RequireVertex(b, newIndex.Length);
                bandwidth = Math.Max(bandwidth, Math.Abs(newIndex[a] - newIndex[b]));
            }

            return bandwidth;
        }

        private static List<int>[] DistinctNeighbors(int vertexCount, IEnumerable<(int A, int B)> edges)
        {
            if (edges == null)
            {
                throw new ArgumentNullException(nameof(edges));
            }

            var sets = new HashSet<int>[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                sets[v] = new HashSet<int>();
            }

            foreach ((int a, int b) in edges)
            {
                RequireVertex(a, vertexCount);
                RequireVertex(b, vertexCount);
                if (a != b)
                {
                    sets[a].Add(b);
                    sets[b].Add(a);
                }
            }

            var neighbors = new List<int>[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                neighbors[v] = new List<int>(sets[v]);
            }

            return neighbors;
        }

        private static void RequireVertex(int vertex, int vertexCount)
        {
            if (vertex < 0 || vertex >= vertexCount)
            {
                throw new ArgumentOutOfRangeException("edges", vertex, "Edge endpoint must be in [0, " + vertexCount + ").");
            }
        }
    }
}
