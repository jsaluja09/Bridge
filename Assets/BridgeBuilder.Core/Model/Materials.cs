using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>What a line is built from. The value is the section id of its members and fills.</summary>
    public enum BridgeMaterial
    {
        Wood = 0,
        Cable = 1,
        Concrete = 2,
    }

    /// <summary>
    /// The three building materials: each is its own section (Constants.Wood, Cable, Concrete) and has its own
    /// price per room meter of line (Constants.Budget). A deck takes the material of the two lines it sits
    /// between; a cable can't carry a road, so a deck between cables is wood planks, like a rope bridge.
    /// </summary>
    public static class Materials
    {
        /// <summary>Every material, in section-id order.</summary>
        public static readonly BridgeMaterial[] All = { BridgeMaterial.Wood, BridgeMaterial.Cable, BridgeMaterial.Concrete };

        public static BridgeMaterial Of(int sectionId) =>
            sectionId >= 0 && sectionId < All.Length
                ? (BridgeMaterial)sectionId
                : throw new ArgumentOutOfRangeException(nameof(sectionId), sectionId, "No material has this section id");

        public static SectionProperties Section(BridgeMaterial m) => m switch
        {
            BridgeMaterial.Wood => Constants.Wood.Section(),
            BridgeMaterial.Cable => Constants.Cable.Section(),
            BridgeMaterial.Concrete => Constants.Concrete.Section(),
            _ => throw new ArgumentOutOfRangeException(nameof(m), m, "Unknown material"),
        };

        /// <summary>One section per material, in id order, for SolverInput.Sections.</summary>
        public static List<SectionProperties> Sections()
        {
            var sections = new List<SectionProperties>(All.Length);
            foreach (var m in All)
                sections.Add(Section(m));
            return sections;
        }

        /// <summary>Price of one room meter of line.</summary>
        public static double CostPerMeter(BridgeMaterial m) => m switch
        {
            BridgeMaterial.Wood => Constants.Budget.WoodCostPerMeter,
            BridgeMaterial.Cable => Constants.Budget.CableCostPerMeter,
            BridgeMaterial.Concrete => Constants.Budget.ConcreteCostPerMeter,
            _ => throw new ArgumentOutOfRangeException(nameof(m), m, "Unknown material"),
        };

        /// <summary>Price of one room square meter of deck or fill; a cable deck is wood planks and costs as wood.</summary>
        public static double DeckCostPerSquareMeter(BridgeMaterial m) => Decked(m) switch
        {
            BridgeMaterial.Wood => Constants.Wood.DeckCostPerSquareMeter,
            BridgeMaterial.Concrete => Constants.Concrete.DeckCostPerSquareMeter,
            _ => throw new ArgumentOutOfRangeException(nameof(m), m, "Unknown material"),
        };

        /// <summary>The deck between a line of a and a line of b: cables count as wood, then the pricier of the two.</summary>
        public static BridgeMaterial DeckMaterial(BridgeMaterial a, BridgeMaterial b)
        {
            a = Decked(a);
            b = Decked(b);
            return CostPerMeter(b) > CostPerMeter(a) ? b : a;
        }

        /// <summary>The deck's material, from the two lines it sits between.</summary>
        public static BridgeMaterial DeckMaterial(BridgeModel model, Deck deck) =>
            DeckMaterial(Of(model.LineSection(deck.LineA)), Of(model.LineSection(deck.LineB)));

        public static string Name(BridgeMaterial m) => m switch
        {
            BridgeMaterial.Wood => "Wood",
            BridgeMaterial.Cable => "Steel cable",
            BridgeMaterial.Concrete => "Concrete",
            _ => throw new ArgumentOutOfRangeException(nameof(m), m, "Unknown material"),
        };

        static BridgeMaterial Decked(BridgeMaterial m) => m == BridgeMaterial.Cable ? BridgeMaterial.Wood : m;
    }
}
