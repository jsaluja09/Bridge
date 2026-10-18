using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>A deck between two lines: a road fill with its triangles and hidden braces (node ids).</summary>
    public sealed class Deck
    {
        public int LineA;
        public int LineB;
        public List<int> Loop = new List<int>();
        public List<int> Triangles = new List<int>();
        public List<(int a, int b)> Braces = new List<(int, int)>();
        public double Area;
        public int SectionId;           // the deck's material (Materials.DeckMaterial), for its price and braces
    }

    /// <summary>
    /// Automatic decks (spec 11.3). Two lines that are each flat (every piece within the road slope limit),
    /// run roughly parallel, and sit side by side at a drivable spacing get a deck between them over the
    /// stretch where they overlap. The strip is zipped together with the shorter diagonal at each step, so
    /// the hidden bracing is a zigzag of short braces. The deck's material comes from its two lines
    /// (Materials.DeckMaterial). Decks are derived from the lines, never stored.
    /// </summary>
    public static class AutoDeck
    {
        sealed class Run
        {
            public int Line;
            public List<int> Nodes;
            public List<Vec3> Points;
            public Vec3 Direction;          // horizontal unit, first to last
            public Vec3 Mid;
            public double[] S;              // position of each node along Direction
        }

        public static List<Deck> Find(BridgeModel model) => Find(model, Constants.Road.MaxSlopeDegrees,
            Constants.Road.MinDeckSpacingMeters, Constants.Road.MaxDeckSpacingMeters, Constants.Road.MaxDeckAngleDegrees);

        public static List<Deck> Find(BridgeModel model, double maxSlopeDeg, double minSpacing, double maxSpacing, double maxAngleDeg)
        {
            var linesAt = new Dictionary<int, HashSet<int>>();
            foreach (var m in model.Members)
            {
                if (m.LineId < 0)
                    continue;
                foreach (int node in new[] { m.NodeA, m.NodeB })
                {
                    if (!linesAt.TryGetValue(node, out var set))
                        linesAt[node] = set = new HashSet<int>();
                    set.Add(m.LineId);
                }
            }
            bool OnOtherLine(int node, int line) => linesAt.TryGetValue(node, out var set) && set.Any(l => l != line);

            var runs = new List<Run>();
            foreach (var line in model.Lines)
            {
                // A rung (W4-03 cross beam): two dots, both on other lines. It ties a deck together; it never edges one.
                var ends = model.LineNodes(line.Id);
                if (ends.Count == 2 && OnOtherLine(ends[0], line.Id) && OnOtherLine(ends[1], line.Id))
                    continue;
                var run = FlatRun(model, line.Id, maxSlopeDeg);
                if (run != null)
                    runs.Add(run);
            }

            var decks = new List<Deck>();
            double cosLimit = Math.Cos(maxAngleDeg * Math.PI / 180);
            for (int i = 0; i < runs.Count; i++)
            {
                for (int j = i + 1; j < runs.Count; j++)
                {
                    var a = runs[i];
                    var b = runs[j];
                    double cos = Vec3.Dot(a.Direction, b.Direction);
                    if (Math.Abs(cos) < cosLimit)
                        continue;
                    if (cos < 0)
                        b = Reversed(b);

                    Vec3 side = Vec3.Cross(Vec3.UnitY, a.Direction);
                    double spacing = Math.Abs(Vec3.Dot(b.Mid - a.Mid, side));
                    if (spacing < minSpacing || spacing > maxSpacing)
                        continue;

                    var deck = Zip(a, b, a.Direction, maxSlopeDeg);
                    if (deck == null)
                        continue;
                    deck.SectionId = (int)Materials.DeckMaterial(model, deck);
                    decks.Add(deck);
                }
            }
            return decks;
        }

        static Run FlatRun(BridgeModel model, int lineId, double maxSlopeDeg)
        {
            var nodes = model.LineNodes(lineId);
            if (nodes.Count < 2)
                return null;
            var pts = nodes.Select(id => model.GetNode(id).Position).ToList();
            double tanMax = Math.Tan(maxSlopeDeg * Math.PI / 180);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vec3 d = pts[i + 1] - pts[i];
                double horizontal = Math.Sqrt(d.X * d.X + d.Z * d.Z);
                if (horizontal < 1e-9 || Math.Abs(d.Y) / horizontal > tanMax)
                    return null;
            }
            Vec3 span = pts[pts.Count - 1] - pts[0];
            var dir = new Vec3(span.X, 0, span.Z);
            if (dir.Length < 1e-6)
                return null;
            dir = dir.Normalized();

            var s = pts.Select(p => Vec3.Dot(p - pts[0], dir)).ToArray();
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] <= s[i - 1])
                    return null;     // doubles back: not a deck edge
            }
            return new Run { Line = lineId, Nodes = nodes, Points = pts, Direction = dir, Mid = Geometry.Centroid(pts), S = s };
        }

        static Run Reversed(Run r)
        {
            var nodes = Enumerable.Reverse(r.Nodes).ToList();
            var pts = Enumerable.Reverse(r.Points).ToList();
            var dir = -r.Direction;
            return new Run { Line = r.Line, Nodes = nodes, Points = pts, Direction = dir, Mid = r.Mid, S = pts.Select(p => Vec3.Dot(p - pts[0], dir)).ToArray() };
        }

        /// <summary>Zips the overlapping stretch of a and b into triangles, choosing the shorter diagonal each step.</summary>
        static Deck Zip(Run a, Run b, Vec3 dir, double maxSlopeDeg)
        {
            Vec3 origin = a.Points[0];
            double Along(Vec3 p) => Vec3.Dot(p - origin, dir);
            double lo = Math.Max(Along(a.Points[0]), Along(b.Points[0]));
            double hi = Math.Min(Along(a.Points[a.Points.Count - 1]), Along(b.Points[b.Points.Count - 1]));
            double tol = Constants.Snapping.TargetSpacingMeters * 0.5;
            if (hi - lo < Constants.Snapping.TargetSpacingMeters * 1.5)
                return null;

            var ia = Enumerable.Range(0, a.Nodes.Count).Where(i => Along(a.Points[i]) >= lo - tol && Along(a.Points[i]) <= hi + tol).ToList();
            var ib = Enumerable.Range(0, b.Nodes.Count).Where(i => Along(b.Points[i]) >= lo - tol && Along(b.Points[i]) <= hi + tol).ToList();
            if (ia.Count < 2 || ib.Count < 2)
                return null;

            var deck = new Deck { LineA = a.Line, LineB = b.Line };
            int x = 0, y = 0;
            void Tri(int p, int q, int r)
            {
                deck.Triangles.Add(p);
                deck.Triangles.Add(q);
                deck.Triangles.Add(r);
            }
            var brace = new HashSet<(int, int)>();
            void Brace(int p, int q) => brace.Add(p < q ? (p, q) : (q, p));

            Brace(a.Nodes[ia[0]], b.Nodes[ib[0]]);
            while (x + 1 < ia.Count || y + 1 < ib.Count)
            {
                int an = a.Nodes[ia[x]], bn = b.Nodes[ib[y]];
                bool advanceA;
                if (x + 1 >= ia.Count) advanceA = false;
                else if (y + 1 >= ib.Count) advanceA = true;
                else advanceA = Vec3.Distance(a.Points[ia[x + 1]], b.Points[ib[y]]) <= Vec3.Distance(a.Points[ia[x]], b.Points[ib[y + 1]]);

                if (advanceA)
                {
                    int next = a.Nodes[ia[x + 1]];
                    Tri(an, next, bn);
                    Brace(next, bn);
                    x++;
                }
                else
                {
                    int next = b.Nodes[ib[y + 1]];
                    Tri(an, next, bn);
                    Brace(an, next);
                    y++;
                }
            }

            // Too steep anywhere means not a road.
            double maxTilt = Math.Cos(maxSlopeDeg * Math.PI / 180);
            var pos = new Dictionary<int, Vec3>();
            for (int i = 0; i < a.Nodes.Count; i++) pos[a.Nodes[i]] = a.Points[i];
            for (int i = 0; i < b.Nodes.Count; i++) pos[b.Nodes[i]] = b.Points[i];
            for (int t = 0; t < deck.Triangles.Count; t += 3)
            {
                Vec3 p = pos[deck.Triangles[t]], q = pos[deck.Triangles[t + 1]], r = pos[deck.Triangles[t + 2]];
                Vec3 n = Vec3.Cross(q - p, r - p);
                double area = n.Length / 2;
                if (area < 1e-9)
                    continue;
                if (Math.Abs(n.Y) / n.Length < maxTilt)
                    return null;
                deck.Area += area;
            }

            deck.Loop.AddRange(ia.Select(i => a.Nodes[i]));
            deck.Loop.AddRange(Enumerable.Reverse(ib).Select(i => b.Nodes[i]));
            deck.Braces.AddRange(brace);
            return deck;
        }
    }
}
