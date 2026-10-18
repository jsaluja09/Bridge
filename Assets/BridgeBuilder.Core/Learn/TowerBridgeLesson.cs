using System.Collections.Generic;
using static BridgeBuilder.Core.LessonShapes;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Tower Bridge (London, 1894): a bascule bridge between two towers, with suspended side spans. Rounded public
    /// figures, to be fact-checked before students see them:
    ///   total length 244 m; central opening 61 m between the towers; side spans about 82 m each, hung from
    ///   suspension chains; towers 65 m tall; high-level walkways about 42 m above high water; road about 8.6 m
    ///   above high water when closed; leaves open to 86°; each leaf, with its counterweight, weighs over 1,000 t;
    ///   steel frames clad in Cornish granite and Portland stone; first worked by steam-driven hydraulics, today by
    ///   electric hydraulics. Opened 1894; designed by Horace Jones and engineer John Wolfe Barry.
    /// </summary>
    public static class TowerBridgeLesson
    {
        const double SideSpan = 82, Opening = 61, TowerHalf = 4.75, Length = 2 * SideSpan + 4 * TowerHalf + Opening;
        const double DeckY = 8.6, TowerTop = 65, WalkwayY = 42, HalfWidth = 9, ChainHigh = 52, ChainLow = 20;

        const int River = 0, Towers = 1, Bascules = 2, Chains = 3, SideDecks = 4, Walkways = 5;

        // Tower centers, and the faces the leaves pivot on.
        const double T1 = SideSpan + TowerHalf, T2 = T1 + TowerHalf + Opening + TowerHalf;
        const double Pivot1 = T1 + TowerHalf, Pivot2 = T2 - TowerHalf;

        public static Lesson Build()
        {
            var bridge = Geometry();
            var lesson = new Lesson { Title = "Tower Bridge", Place = "London, UK", Year = 1894, RealLength = Length, Bridge = bridge };

            lesson.Steps.Add(new LessonStep
            {
                Title = "A road that has to open",
                Text = "In the 1880s the Pool of London was crowded with tall-masted ships. A new crossing had to carry " +
                       "horses and carts over the Thames and still let those ships through to the wharves.",
                Formula = "Clearance: about 8.6 m closed, about 42 m with the road open",
                Builds = new[] { River },
                Labels = new[] { new LessonLabel("river", "River Thames") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Towers push",
                Text = "Two towers, 65 m tall, stand on piers sunk into the riverbed. Inside is a steel frame that carries " +
                       "the load; the granite and stone outside is mostly a coat, chosen to match the Tower of London.",
                Formula = "Pressure on the riverbed  p = F / A   ·   wide piers spread the weight",
                Builds = new[] { Towers },
                Highlight = new[] { "towers" },
                Labels = new[] { new LessonLabel("towers", "Towers: 65 m, steel inside stone") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "A see-saw road",
                Text = "Each half of the road is a leaf that tips up about a pivot at its tower, like a see-saw. A heavy " +
                       "counterweight on the short end, inside the tower, balances the long end, so lifting takes little effort.",
                Formula = "Balanced when  m₁·d₁ = m₂·d₂   ·   then the motor only fights friction and wind",
                Builds = new[] { Bascules },
                Highlight = new[] { "leaf-north", "leaf-south" },
                Labels = new[] { new LessonLabel("leaf-north", "Leaf: over 1,000 t with its counterweight") },
                Demo = LoadDemo.Bascules,
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Side spans hang from chains",
                Text = "The road from each shore to its tower hangs from a pair of suspension chains, like a small Golden " +
                       "Gate. The chains run from the shore up to the top of the tower and pull on it, toward the shore.",
                Formula = "Chain pull  H = wL² / 8d   ·   the same suspension rule as the Golden Gate",
                Builds = new[] { Chains, SideDecks },
                Highlight = new[] { "chains", "side-decks" },
                Labels = new[] { new LessonLabel("chains", "Suspension chains: pulled") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Walkways hold the towers together",
                Text = "Both chains pull their towers toward the shore. The high walkways between the towers are ties: they " +
                       "hold the two towers together against that pull, so the towers don't have to lean against it.",
                Formula = "Balance at each tower:  chain pull toward the shore = walkway tie pull toward the river",
                Builds = new[] { Walkways },
                Highlight = new[] { "walkways", "chains" },
                Labels = new[] { new LessonLabel("walkways", "Walkways: ties, 42 m up") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Opening for a ship",
                Text = "The leaves lift to 86° in about a minute. Steam engines pumped water into hydraulic motors until " +
                       "1976; electric hydraulics do the job now. The bridge still opens for ships hundreds of times a year.",
                Formula = "Power = work / time   ·   balanced leaves mean little work to lift",
                Highlight = new[] { "leaf-north", "leaf-south" },
                Demo = LoadDemo.Bascules,
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "How it all works",
                Text = "The side roads hang from the chains, the chains pull on the towers, the walkways tie the towers " +
                       "together, and the balanced leaves open the middle. Designed by Horace Jones and John Wolfe Barry.",
                Formula = "Side roads → chains → towers ⇄ walkways   ·   leaves ↔ counterweights",
                Highlight = new[] { "chains", "walkways", "towers" },
            });
            return lesson;
        }

        static LessonBridge Geometry()
        {
            var b = new LessonBridge
            {
                Name = "Tower Bridge",
                MainSpanStart = Pivot1,
                MainSpanEnd = Pivot2,
                DeckY = DeckY,
            };

            var river = b.Add("river", "River Thames", PartRole.Scenery, River, 0);
            Quad(river, new Vec3(-30, 0, -70), new Vec3(Length + 30, 0, -70), new Vec3(Length + 30, 0, 70), new Vec3(-30, 0, 70));

            // Towers: four corner columns each, tied by floors, standing on piers in the river.
            var towers = b.Add("towers", "Towers", PartRole.Push, Towers, 1.5);
            foreach (double x in new[] { T1, T2 })
            {
                Box(towers, new Vec3(x - TowerHalf - 3, -4, -HalfWidth - 3), new Vec3(x + TowerHalf + 3, 2, HalfWidth + 3));   // pier
                foreach (double dx in new[] { -TowerHalf, TowerHalf })
                {
                    foreach (double z in new[] { -HalfWidth, HalfWidth })
                        Line(towers, new Vec3(x + dx, 2, z), new Vec3(x + dx, TowerTop, z));
                }
                foreach (double y in new[] { DeckY + 1, 25.0, WalkwayY, TowerTop })
                {
                    Line(towers, new Vec3(x - TowerHalf, y, -HalfWidth), new Vec3(x + TowerHalf, y, -HalfWidth));
                    Line(towers, new Vec3(x - TowerHalf, y, HalfWidth), new Vec3(x + TowerHalf, y, HalfWidth));
                    Line(towers, new Vec3(x - TowerHalf, y, -HalfWidth), new Vec3(x - TowerHalf, y, HalfWidth));
                    Line(towers, new Vec3(x + TowerHalf, y, -HalfWidth), new Vec3(x + TowerHalf, y, HalfWidth));
                }
            }

            // The two leaves meet in the middle; each pivots at its tower face, its counterweight inside the tower.
            double half = Opening / 2;
            Leaf(b, "leaf-north", "North leaf", Pivot1, +1, half);
            Leaf(b, "leaf-south", "South leaf", Pivot2, -1, half);

            // Side spans: chains from each shore up to its tower, the road hung below on hangers.
            var chains = b.Add("chains", "Suspension chains", PartRole.Pull, Chains, 0.9);
            var sideDecks = b.Add("side-decks", "Side spans", PartRole.Bend, SideDecks, 0.8);
            foreach (var (shore, tower) in new[] { (0.0, T1 - TowerHalf), (Length, T2 + TowerHalf) })
            {
                foreach (double z in new[] { -HalfWidth, HalfWidth })
                {
                    Vec3 a = new Vec3(shore, ChainLow, z), c = new Vec3(tower, ChainHigh, z);
                    double sag = 14;
                    Polyline(chains, Cable(a, c, sag, 16));
                    Hangers(chains, a, c, sag, DeckY, System.Math.Min(shore, tower), System.Math.Max(shore, tower), 8);
                    Line(sideDecks, new Vec3(shore, DeckY, z), new Vec3(tower, DeckY, z));
                }
                double x0 = System.Math.Min(shore, tower), x1 = System.Math.Max(shore, tower);
                Quad(sideDecks, new Vec3(x0, DeckY, -HalfWidth), new Vec3(x1, DeckY, -HalfWidth), new Vec3(x1, DeckY, HalfWidth), new Vec3(x0, DeckY, HalfWidth));
            }

            // High-level walkways between the towers: ties that hold the towers together against the chains' pull.
            var walkways = b.Add("walkways", "Walkways", PartRole.Pull, Walkways, 1.0);
            foreach (double z in new[] { -HalfWidth, HalfWidth })
            {
                Truss(walkways, T1 + TowerHalf, T2 - TowerHalf, WalkwayY + 3, 3, z, 8);
                Line(walkways, new Vec3(T1 + TowerHalf, WalkwayY, z), new Vec3(T2 - TowerHalf, WalkwayY, z));
            }
            return b;
        }

        /// <summary>
        /// One leaf: its road surface reaching `reach` from the pivot (toward the middle when turn is +1 over +x),
        /// its edges, and its counterweight behind and below the pivot inside the tower.
        /// </summary>
        static void Leaf(LessonBridge b, string id, string name, double pivotX, int turn, double reach)
        {
            var leaf = b.Add(id, name, PartRole.Bend, Bascules, 0.9);
            leaf.Pivot = new Vec3(pivotX, DeckY, 0);
            leaf.Turn = turn;
            double tip = pivotX + turn * reach;
            double x0 = System.Math.Min(pivotX, tip), x1 = System.Math.Max(pivotX, tip);
            Quad(leaf, new Vec3(x0, DeckY, -HalfWidth + 1), new Vec3(x1, DeckY, -HalfWidth + 1), new Vec3(x1, DeckY, HalfWidth - 1), new Vec3(x0, DeckY, HalfWidth - 1));
            foreach (double z in new[] { -HalfWidth + 1, HalfWidth - 1 })
                Line(leaf, new Vec3(pivotX, DeckY, z), new Vec3(tip, DeckY, z));
            double back = pivotX - turn * 8;
            Box(leaf, new Vec3(System.Math.Min(pivotX, back), DeckY - 7, -HalfWidth + 2), new Vec3(System.Math.Max(pivotX, back), DeckY - 1, HalfWidth - 2));
        }
    }
}
