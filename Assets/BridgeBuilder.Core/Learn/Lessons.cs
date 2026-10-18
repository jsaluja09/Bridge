using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>Every lesson in Learn mode, in the order the picker shows them.</summary>
    public static class Lessons
    {
        public static IReadOnlyList<Lesson> All() => new[]
        {
            GoldenGateLesson.Build(),
            TowerBridgeLesson.Build(),
            MillauLesson.Build(),
        };
    }
}
