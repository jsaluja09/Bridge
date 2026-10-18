using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>A road built as one shape: two rails, the cross beams between them, and the deck fill that pays for the deck.</summary>
    public sealed class Road
    {
        public int RailA;
        public int RailB;
        public List<int> CrossBeams = new List<int>();     // line ids
        public int DeckFillId = -1;

        public Road Clone() => new Road { RailA = RailA, RailB = RailB, CrossBeams = new List<int>(CrossBeams), DeckFillId = DeckFillId };
    }

    /// <summary>
    /// Road shapes (W4-03): a paired left and right rail treated as one road. Pairing adds a cross beam between
    /// each matching pair of dots on the two rails and the automatic deck (AutoDeck), paying for beams and deck
    /// through the model's budget. Deleting either rail takes its cross beams and deck with it. Each of these is
    /// one action for Undo, though the model records it as several edits.
    /// </summary>
    public sealed class RoadShapes
    {
        readonly BridgeModel _model;
        List<Road> _roads = new List<Road>();
        readonly Stack<(int before, int after, List<Road> roads)> _groups = new Stack<(int, int, List<Road>)>();

        public RoadShapes(BridgeModel model) => _model = model ?? throw new ArgumentNullException(nameof(model));

        public IReadOnlyList<Road> Roads => _roads;

        public Road RoadOf(int railId) => _roads.FirstOrDefault(r => r.RailA == railId || r.RailB == railId);

        /// <summary>Makes a road of two rails. The result's Id is the deck fill. Fails, changing nothing, if they make no deck.</summary>
        public EditResult TryPair(int railA, int railB)
        {
            if (railA == railB || _model.GetLine(railA) == null || _model.GetLine(railB) == null)
                return EditResult.Fail(EditReason.UnknownId);
            if (RoadOf(railA) != null || RoadOf(railB) != null)
                return EditResult.Fail(EditReason.InvalidLine);

            var pairs = MatchingDots(railA, railB);
            int before = _model.UndoDepth;
            var road = new Road { RailA = railA, RailB = railB };
            foreach (var (a, b) in pairs)
            {
                var beam = _model.TryAddFreeLine(new[] { LineVertex.Existing(a), LineVertex.Existing(b) });
                if (!beam.Ok)
                    return Rollback(before, beam);
                road.CrossBeams.Add(beam.Id);
            }

            var deck = AutoDeck.Find(_model).FirstOrDefault(d =>
                (d.LineA == railA && d.LineB == railB) || (d.LineA == railB && d.LineB == railA));
            if (deck == null)
                return Rollback(before, EditResult.Fail(EditReason.InvalidLine));
            var fill = _model.TryAddFill(deck.Loop, road: true);
            if (!fill.Ok)
                return Rollback(before, fill);
            road.DeckFillId = fill.Id;

            Commit(before, () => _roads.Add(road));
            return EditResult.Success(fill.Id);
        }

        /// <summary>Deletes a rail; if it belongs to a road, its cross beams and deck go with it. Blocked like any line delete.</summary>
        public EditResult TryDeleteRail(int railId)
        {
            var road = RoadOf(railId);
            if (road == null)
            {
                int start = _model.UndoDepth;
                var plain = _model.TryDeleteLine(railId);
                if (plain.Ok)
                    Commit(start, () => { });
                return plain;
            }

            int before = _model.UndoDepth;
            foreach (int beam in road.CrossBeams)
            {
                if (_model.GetLine(beam) == null)
                    continue;
                var r = _model.TryDeleteLine(beam);
                if (!r.Ok)
                    return Rollback(before, r);
            }
            if (_model.GetFill(road.DeckFillId) != null)
            {
                var r = _model.TryDeleteFill(road.DeckFillId);
                if (!r.Ok)
                    return Rollback(before, r);
            }
            var rail = _model.TryDeleteLine(railId);
            if (!rail.Ok)
                return Rollback(before, rail);

            Commit(before, () => _roads.Remove(road));
            return rail;
        }

        /// <summary>Undoes the last action, a whole road edit at once. False if there is nothing to undo.</summary>
        public bool Undo()
        {
            if (_groups.Count > 0 && _groups.Peek().after == _model.UndoDepth)
            {
                var (before, _, roads) = _groups.Pop();
                while (_model.UndoDepth > before)
                    _model.Undo();
                _roads = roads;
                return true;
            }
            return _model.Undo();
        }

        /// <summary>Each dot on rail A with the dot on rail B at the same place along the road, skipping pairs that are both supports.</summary>
        List<(int a, int b)> MatchingDots(int railA, int railB)
        {
            var a = _model.LineNodes(railA);
            var b = _model.LineNodes(railB);
            Vec3 pa = _model.GetNode(a[0]).Position, qa = _model.GetNode(a[a.Count - 1]).Position;
            Vec3 dir = new Vec3(qa.X - pa.X, 0, qa.Z - pa.Z);
            dir = dir.LengthSquared > 1e-12 ? dir.Normalized() : Vec3.UnitX;
            double Along(int id) => Vec3.Dot(_model.GetNode(id).Position - pa, dir);
            double tolerance = Constants.Snapping.TargetSpacingMeters * 0.5;

            var pairs = new List<(int, int)>();
            var used = new HashSet<int>();
            foreach (int x in a)
            {
                int best = -1;
                double bestGap = tolerance;
                foreach (int y in b)
                {
                    double gap = Math.Abs(Along(x) - Along(y));
                    if (!used.Contains(y) && gap <= bestGap)
                    {
                        best = y;
                        bestGap = gap;
                    }
                }
                if (best < 0 || (_model.GetNode(x).Fixed && _model.GetNode(best).Fixed))
                    continue;
                used.Add(best);
                pairs.Add((x, best));
            }
            return pairs;
        }

        void Commit(int before, Action change)
        {
            _groups.Push((before, _model.UndoDepth, _roads.Select(r => r.Clone()).ToList()));
            change();
        }

        EditResult Rollback(int before, EditResult failure)
        {
            while (_model.UndoDepth > before)
                _model.Undo();
            return failure;
        }
    }
}
