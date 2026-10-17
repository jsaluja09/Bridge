using System;
using System.Numerics;

namespace BridgeBuilder.Core
{
    public enum CarState
    {
        Waiting,
        Driving,
        Arrived,
        Falling,
    }

    /// <summary>
    /// The car as two axle loads moving along the road at constant speed (lagged coupling:
    /// loads come from the deck's current shape, then the solver steps).
    /// Each axle rests on a start surface, a far surface, or a road member and its twin.
    /// </summary>
    public sealed class CarSim
    {
        readonly BridgeSettings _s;
        readonly BridgeStructure _st;

        public CarState State { get; private set; } = CarState.Waiting;
        public float S { get; private set; }               // center position along the gap
        public Vector3 Position { get; private set; }       // world, center of the road at axle height
        public Vector3 Forward { get; private set; }        // world, rear axle to front axle

        readonly float[] _axleHeight = new float[2];
        readonly int[] _axleMember = new int[2];            // -1 = on a surface
        readonly float[] _axleT = new float[2];

        public CarSim(BridgeSettings settings, BridgeStructure structure)
        {
            _s = settings;
            _st = structure;
            S = -settings.CarStartBack;
            for (int i = 0; i < 2; i++)
                _axleMember[i] = -1;
            UpdatePose();
        }

        public void StartDriving()
        {
            if (State == CarState.Waiting)
                State = CarState.Driving;
        }

        float AxleS(int axle) => S + (axle == 0 ? -0.5f : 0.5f) * _s.Wheelbase;

        /// <summary>Moves the car, finds what each axle rests on, and loads the solver.</summary>
        public void PreStep(float dt)
        {
            if (State != CarState.Driving)
                return;

            S += _s.CarSpeed * dt;
            for (int axle = 0; axle < 2; axle++)
            {
                if (!FindSupport(AxleS(axle), _axleHeight[axle], out int member, out float t, out float h))
                {
                    State = CarState.Falling;
                    return;
                }
                _axleMember[axle] = member;
                _axleT[axle] = t;
                _axleHeight[axle] = h;
            }

            if (AxleS(0) >= _st.FarU + 0.02f)
            {
                State = CarState.Arrived;
                return;
            }

            ApplyLoads();
        }

        /// <summary>Re-reads the deck height under each axle after the solver moved it.</summary>
        public void PostStep()
        {
            if (State == CarState.Driving)
            {
                for (int axle = 0; axle < 2; axle++)
                {
                    int m = _axleMember[axle];
                    if (m >= 0 && TryRoadHeight(m, AxleS(axle), out _, out float h))
                        _axleHeight[axle] = h;
                }
            }
            UpdatePose();
        }

        void ApplyLoads()
        {
            var solver = _st.Solver;
            float axleWeight = _s.CarMass * _s.Gravity * 0.5f;
            float axleMass = _s.CarMass * 0.5f;
            for (int axle = 0; axle < 2; axle++)
            {
                int m = _axleMember[axle];
                if (m < 0)
                    continue;
                float t = _axleT[axle];
                for (int side = 0; side < 2; side++)
                {
                    int member = side == 0 ? m : _st.TwinMember(m);
                    int a = solver.MemberA[member], b = solver.MemberB[member];
                    float wa = (1f - t) * 0.5f, wb = t * 0.5f;
                    solver.AddLoad(a, new Vector3(0f, -axleWeight * wa, 0f), axleMass * wa);
                    solver.AddLoad(b, new Vector3(0f, -axleWeight * wb, 0f), axleMass * wb);
                    // Simply supported point load: M = P * t(1-t) * L.
                    solver.AddBending(member, axleWeight * 0.5f * t * (1f - t) * solver.RestLength(member));
                }
            }
        }

        struct Support
        {
            public int Member;
            public float T;
            public float Height;
            public float Error;
        }

        bool FindSupport(float s, float prevHeight, out int member, out float t, out float height)
        {
            var best = new Support { Member = -1, Error = float.MaxValue };

            if (s <= 0f)
                Consider(ref best, prevHeight, -1, 0f, 0f);
            if (s >= _st.FarU)
                Consider(ref best, prevHeight, -1, 0f, _st.FarV);
            foreach (int m in _st.RoadMembers)
            {
                if (TryRoadHeight(m, s, out float tt, out float h))
                    Consider(ref best, prevHeight, m, tt, h);
            }

            member = best.Member;
            t = best.T;
            height = best.Height;
            return best.Error < float.MaxValue;
        }

        void Consider(ref Support best, float prevHeight, int member, float t, float height)
        {
            float rise = height - prevHeight;
            if (rise > _s.CarStepUp || -rise > _s.CarMaxDrop)
                return;
            float err = Math.Abs(rise);
            if (err < best.Error)
                best = new Support { Member = member, T = t, Height = height, Error = err };
        }

        bool TryRoadHeight(int m, float s, out float t, out float height)
        {
            t = 0f;
            height = 0f;
            var solver = _st.Solver;
            int twin = _st.TwinMember(m);
            if (!solver.Active[m] || !solver.Active[twin])
                return false;

            Vector2 a = Mid(solver.MemberA[m], solver.MemberA[twin]);
            Vector2 b = Mid(solver.MemberB[m], solver.MemberB[twin]);
            float du = b.X - a.X;
            if (Math.Abs(du) < 1e-5f)
                return false;
            t = (s - a.X) / du;
            if (t < 0f || t > 1f)
                return false;
            height = a.Y + (b.Y - a.Y) * t;
            return true;
        }

        Vector2 Mid(int node, int twin)
        {
            var p = _st.Solver.Positions;
            return _st.Frame.ToPlane((p[node] + p[twin]) * 0.5f);
        }

        void UpdatePose()
        {
            var f = _st.Frame;
            Vector3 side = f.W * (f.Width * 0.5f);
            Vector3 rear = f.ToWorld(new Vector2(AxleS(0), _axleHeight[0])) + side;
            Vector3 front = f.ToWorld(new Vector2(AxleS(1), _axleHeight[1])) + side;
            Position = (rear + front) * 0.5f;
            Vector3 d = front - rear;
            Forward = d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : f.U;
        }
    }
}
