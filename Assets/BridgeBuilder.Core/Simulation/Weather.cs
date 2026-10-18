using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Weather as constant loads (HG-car-and-wind: GUESS models). Wind: drag on every member and a steady side
    /// force on each car. Snow: a pressure on the road surface. Node loads are bridge-world newtons, in
    /// SolverInput node order.
    /// </summary>
    public static class Weather
    {
        /// <summary>Wind drag on members: only the part of the wind across each member pushes it.</summary>
        public static Vec3[] WindLoads(CompiledBridge bridge, Vec3 direction, double speed, double scale)
        {
            var input = bridge.Input;
            var loads = new Vec3[input.Nodes.Count];
            if (!(speed > 0) || direction.LengthSquared <= 0)
                return loads;
            Vec3 w = direction.Normalized();
            double q = DynamicPressure(speed);
            var index = bridge.NodeIndex;
            foreach (var m in input.Members)
            {
                int a = index[m.NodeA], b = index[m.NodeB];
                Vec3 pa = input.Nodes[a].Position * scale, pb = input.Nodes[b].Position * scale;
                double len = Vec3.Distance(pa, pb);
                if (len <= 0)
                    continue;
                Vec3 axis = (pb - pa) / len;
                Vec3 cross = w - axis * Vec3.Dot(w, axis);
                Vec3 f = cross * (q * Constants.Car.MemberDragCoefficient * Constants.Car.MemberDiameter * len);
                loads[a] += f * 0.5;
                loads[b] += f * 0.5;
            }
            return loads;
        }

        /// <summary>Wind force on a car heading along `heading`: side and front areas by how square the wind hits them.</summary>
        public static Vec3 WindOnCar(Vec3 direction, double speed, Vec3 heading)
        {
            if (!(speed > 0) || direction.LengthSquared <= 0)
                return Vec3.Zero;
            Vec3 w = direction.Normalized();
            Vec3 side = Vec3.Cross(Vec3.UnitY, heading);
            double area = Constants.Car.SideArea * Math.Abs(Vec3.Dot(w, side)) + Constants.Car.FrontArea * Math.Abs(Vec3.Dot(w, heading));
            return w * (DynamicPressure(speed) * Constants.Car.DragCoefficient * area);
        }

        /// <summary>Snow on the road: each deck triangle's area times the snow load, a third to each corner, downward.</summary>
        public static Vec3[] SnowLoads(CompiledBridge bridge, double pascals, double scale)
        {
            var nodes = bridge.Input.Nodes;
            var loads = new Vec3[nodes.Count];
            if (!(pascals > 0))
                return loads;
            foreach (var deck in bridge.Decks)
            {
                var t = deck.Triangles;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vec3 a = nodes[t[i]].Position * scale, b = nodes[t[i + 1]].Position * scale, c = nodes[t[i + 2]].Position * scale;
                    double area = 0.5 * Vec3.Cross(b - a, c - a).Length;
                    var share = new Vec3(0, -pascals * area / 3, 0);
                    loads[t[i]] += share;
                    loads[t[i + 1]] += share;
                    loads[t[i + 2]] += share;
                }
            }
            return loads;
        }

        static double DynamicPressure(double speed) => 0.5 * Constants.Car.AirDensity * speed * speed;
    }
}
