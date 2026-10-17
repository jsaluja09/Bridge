using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Reverse Cuthill-McKee node ordering for a narrow band (spec 2.12). Deterministic: each component starts
    /// at the lowest index among its minimum-degree vertices; unnumbered neighbors are visited by increasing
    /// degree, ties by increasing index; the whole order is then reversed.
    /// </summary>
    public static class ReverseCuthillMcKee
    {
        /// <summary>Returns permutation[newIndex] = oldIndex.</summary>
        public static int[] Order(int vertexCount, IReadOnlyList<(int a, int b)> edges)
        {
            var adjacency = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                adjacency[i] = new List<int>();
            foreach (var (a, b) in edges)
            {
                if (a == b)
                    continue;
                if (!adjacency[a].Contains(b))
                    adjacency[a].Add(b);
                if (!adjacency[b].Contains(a))
                    adjacency[b].Add(a);
            }

            var visited = new bool[vertexCount];
            var order = new List<int>(vertexCount);
            var neighbors = new List<int>();
            while (order.Count < vertexCount)
            {
                int start = -1;
                for (int i = 0; i < vertexCount; i++)
                {
                    if (!visited[i] && (start < 0 || adjacency[i].Count < adjacency[start].Count))
                        start = i;
                }

                var queue = new Queue<int>();
                queue.Enqueue(start);
                visited[start] = true;
                while (queue.Count > 0)
                {
                    int v = queue.Dequeue();
                    order.Add(v);
                    neighbors.Clear();
                    foreach (int nb in adjacency[v])
                    {
                        if (!visited[nb])
                        {
                            visited[nb] = true;
                            neighbors.Add(nb);
                        }
                    }
                    neighbors.Sort((x, y) =>
                    {
                        int byDegree = adjacency[x].Count.CompareTo(adjacency[y].Count);
                        return byDegree != 0 ? byDegree : x.CompareTo(y);
                    });
                    foreach (int nb in neighbors)
                        queue.Enqueue(nb);
                }
            }
            order.Reverse();
            return order.ToArray();
        }

        /// <summary>The largest |newIndex(u) - newIndex(v)| over the edges.</summary>
        public static int Bandwidth(int[] permutation, IReadOnlyList<(int a, int b)> edges)
        {
            var newIndex = new int[permutation.Length];
            for (int k = 0; k < permutation.Length; k++)
                newIndex[permutation[k]] = k;
            int width = 0;
            foreach (var (a, b) in edges)
                width = Math.Max(width, Math.Abs(newIndex[a] - newIndex[b]));
            return width;
        }
    }
}
