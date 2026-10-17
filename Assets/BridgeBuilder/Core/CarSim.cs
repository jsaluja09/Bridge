using System;
using System.Numerics;

namespace BridgeBuilder.Core
{
    public enum CarState
    {
        Driving,
        Arrived,
        Falling,
    }

    /// <summary>
    /// A small suspension car (spec 2.8): four spring-damper wheels on the road fills and the surfaces,
    /// cruise control limited by grip, crosswind, and lagged two-way coupling (wheel loads from the last
    /// step load the bridge; the car then reads the deck's new shape). Bridge-world units.
    /// </summary>
    public sealed class CarSim
    {
        readonly BridgeStructure _st;
        readonly BridgeSettings _s;
        readonly float _mass;
        readonly float _grip;
        readonly float _k;
        readonly float _c;
        readonly Vector3 _wind;

        public CarState State { get; private set; } = CarState.Driving;
        public Vector3 Position;         // body reference point at axle height
        public Vector3 Velocity;
        public float Pitch;              // nose up positive
        public float Roll;               // +Side up positive
        public readonly Vector3 Direction;
        public readonly Vector3 Side;
        public Vector3 Forward { get; private set; }
        public Vector3 Up { get; private set; }

        // Wheels: 0 front-left(+Side), 1 front-right, 2 rear-left, 3 rear-right.
        readonly float[] _lx, _ly;
        public readonly float[] WheelLoad = new float[4];
        readonly float[] _compression = new float[4];
        readonly Vector3[] _normal = new Vector3[4];
        readonly float[] _ground = new float[4];
        readonly int[] _fill = new int[4];          // fill index, -1 = a surface, -2 = nothing
        readonly int[] _tri = new int[4];
        readonly Vector3[] _bary = new Vector3[4];
        float _airTime;
        int _surfaceContacts;

        public CarSim(BridgeStructure st)
        {
            _st = st;
            _s = st.Settings;
            _mass = st.Challenge.CarMass;
            _grip = st.Challenge.Grip;
            _k = _mass * _s.Gravity / (4f * _s.SpringSag);
            _c = 2f * _s.SuspensionDamping * MathF.Sqrt(_k * _mass / 4f);

            float q = 0.5f * _s.AirDensity * st.Challenge.WindSpeed * st.Challenge.WindSpeed;
            Direction = st.GapDirection;
            Side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Direction));
            float area = _s.CarSideArea * MathF.Abs(Vector3.Dot(st.WindDirection, Side)) +
                         _s.CarFrontArea * MathF.Abs(Vector3.Dot(st.WindDirection, Direction));
            _wind = st.WindDirection * (q * _s.CarDragCoefficient * area);

            float hw = _s.Wheelbase * 0.5f, ht = _s.Track * 0.5f;
            _lx = new[] { hw, hw, -hw, -hw };
            _ly = new[] { ht, -ht, ht, -ht };

            var start = st.Surfaces[st.StartSurface];
            Position = start.Mid + start.Back * (_s.CarLength * 0.5f + 1.5f);
            Position.Y = start.Height + _s.RideHeight;
            for (int i = 0; i < 4; i++)
                _fill[i] = -2;
            UpdatePose();
        }

        Vector3 WheelAnchor(int i) =>
            Position + Direction * _lx[i] + Side * _ly[i] +
            Vector3.UnitY * (_lx[i] * MathF.Tan(Pitch) + _ly[i] * MathF.Tan(Roll));

        public void Step(float dt, Vector3[] positions, bool[] elementActive)
        {
            int substeps = Math.Max(1, _s.CarSubsteps);
            float h = dt / substeps;
            for (int sub = 0; sub < substeps; sub++)
                Substep(h, positions, elementActive);

            if (State == CarState.Driving)
            {
                _airTime = _surfaceContacts == 0 && TotalLoad() <= 0f ? _airTime + dt : 0f;
                float lowest = MathF.Min(_st.Surfaces[0].Height, _st.Surfaces[1].Height);
                if (_airTime > 0.4f || Position.Y < lowest - 3f)
                    State = CarState.Falling;
                else if (ReachedFarSide())
                    State = CarState.Arrived;
            }
            UpdatePose();
        }

        float TotalLoad()
        {
            float sum = 0f;
            for (int i = 0; i < 4; i++)
                sum += WheelLoad[i];
            return sum;
        }

        bool ReachedFarSide()
        {
            var far = _st.Surfaces[1 - _st.StartSurface];
            float past = Vector3.Dot(Position - far.Mid, Direction);
            return past >= _s.CarLength * 0.5f + 1f && _surfaceContacts >= 2 && Position.Y > far.Height;
        }

        void Substep(float h, Vector3[] positions, bool[] elementActive)
        {
            float normalSum = 0f;
            Vector3 force = new Vector3(0f, -_mass * _s.Gravity, 0f) + _wind;
            float front = 0f, rear = 0f, left = 0f, right = 0f;
            int frontN = 0, rearN = 0, leftN = 0, rightN = 0;
            _surfaceContacts = 0;
            bool contacts = State != CarState.Falling;

            for (int i = 0; i < 4; i++)
            {
                Vector3 anchor = WheelAnchor(i);
                float c = 0f;
                WheelLoad[i] = 0f;
                if (contacts && FindGround(anchor, positions, elementActive, i))
                {
                    c = _s.RideHeight + _s.SpringSag - (anchor.Y - _ground[i]);
                    if (c > 0f)
                    {
                        float f = MathF.Max(0f, _k * c + _c * (c - _compression[i]) / h);
                        WheelLoad[i] = f;
                        normalSum += f;
                        force += _normal[i] * f;
                        if (_fill[i] == -1)
                            _surfaceContacts++;
                    }
                    if (i < 2) { front += _ground[i]; frontN++; } else { rear += _ground[i]; rearN++; }
                    if ((i & 1) == 0) { left += _ground[i]; leftN++; } else { right += _ground[i]; rightN++; }
                }
                else
                {
                    _fill[i] = -2;
                }
                _compression[i] = MathF.Max(0f, c);
            }

            if (State == CarState.Driving && normalSum > 0f)
            {
                float maxFriction = _grip * normalSum;
                float vLong = Vector3.Dot(Velocity, Direction);
                float drive = Math.Clamp(_mass * (_s.CarSpeed - vLong) * 2f, -maxFriction, maxFriction);
                float vLat = Vector3.Dot(Velocity, Side);
                float needed = -_mass * vLat / h - Vector3.Dot(force, Side);
                float maxLat = MathF.Sqrt(MathF.Max(0f, maxFriction * maxFriction - drive * drive));
                force += Direction * drive + Side * Math.Clamp(needed, -maxLat, maxLat);
            }

            Velocity += force / _mass * h;
            Position += Velocity * h;

            if (Position.Y < _st.GroundY + _s.RideHeight)
            {
                Position.Y = _st.GroundY + _s.RideHeight;
                Velocity *= 0.5f;
                Velocity.Y = 0f;
            }

            // Body attitude follows the ground under the wheels; tumble a little when falling.
            float blend = MathF.Min(1f, h * 10f);
            if (State == CarState.Falling)
            {
                Pitch -= 0.8f * h;
                Roll += 0.5f * h;
            }
            else
            {
                if (frontN > 0 && rearN > 0)
                    Pitch += (MathF.Atan((front / frontN - rear / rearN) / _s.Wheelbase) - Pitch) * blend;
                if (leftN > 0 && rightN > 0)
                    Roll += (MathF.Atan((left / leftN - right / rightN) / _s.Track) - Roll) * blend;
            }
        }

        /// <summary>Highest drivable ground under the wheel that it could be resting on.</summary>
        bool FindGround(Vector3 anchor, Vector3[] positions, bool[] elementActive, int wheel)
        {
            float best = float.MinValue;
            float reach = anchor.Y + 0.3f;
            for (int s = 0; s < 2; s++)
            {
                var surface = _st.Surfaces[s];
                if (surface.Height <= reach && surface.Height > best && surface.Contains(anchor))
                {
                    best = surface.Height;
                    _fill[wheel] = -1;
                    _normal[wheel] = Vector3.UnitY;
                }
            }

            for (int f = 0; f < _st.Fills.Count; f++)
            {
                var g = _st.Fills[f];
                if (!g.IsRoad)
                    continue;
                for (int t = 0; t < g.Tris.Length; t += 3)
                {
                    if (!Usable(g, t, elementActive))
                        continue;
                    Vector3 a = positions[g.Tris[t]], b = positions[g.Tris[t + 1]], c = positions[g.Tris[t + 2]];
                    if (!Geometry.BarycentricXZ(anchor, a, b, c, out float wa, out float wb, out float wc))
                        continue;
                    float height = wa * a.Y + wb * b.Y + wc * c.Y;
                    if (height > reach || height <= best)
                        continue;
                    best = height;
                    _fill[wheel] = f;
                    _tri[wheel] = t;
                    _bary[wheel] = new Vector3(wa, wb, wc);
                    Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                    _normal[wheel] = n.Y < 0f ? -n : n;
                }
            }

            _ground[wheel] = best;
            return best > float.MinValue;
        }

        static bool Usable(FillGeometry g, int t, bool[] elementActive)
        {
            for (int e = 0; e < 3; e++)
            {
                int k = g.TriEdges[t + e];
                if (k >= 0 && !elementActive[k])
                    return false;
            }
            return true;
        }

        /// <summary>Lagged coupling: the wheels' last loads pushed into the deck nodes they rest on.</summary>
        public void ApplyLoads(Action<int, Vector3> addLoad)
        {
            for (int i = 0; i < 4; i++)
            {
                if (_fill[i] < 0 || WheelLoad[i] <= 0f)
                    continue;
                var g = _st.Fills[_fill[i]];
                Vector3 f = -_normal[i] * WheelLoad[i];
                int t = _tri[i];
                addLoad(g.Tris[t], f * _bary[i].X);
                addLoad(g.Tris[t + 1], f * _bary[i].Y);
                addLoad(g.Tris[t + 2], f * _bary[i].Z);
            }
        }

        void UpdatePose()
        {
            var pitch = Quaternion.CreateFromAxisAngle(Side, -Pitch);
            Vector3 forward = Vector3.Transform(Direction, pitch);
            Vector3 up = Vector3.Transform(Vector3.UnitY, pitch);
            var roll = Quaternion.CreateFromAxisAngle(forward, Roll);
            Forward = Vector3.Normalize(forward);
            Up = Vector3.Normalize(Vector3.Transform(up, roll));
        }
    }
}
