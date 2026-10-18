using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// W4-08: the primitive "Hot Wheels" car, built once from CarBlueprint (a low wide body, cabin with windows,
    /// bumpers, lights and four big wheels) and posed every frame from CarPose: body from heading, pitch and
    /// roll, each wheel at its spring's ground contact, wheels turning with the distance driven. Sizes come from
    /// the Core car constants. If sourcedModelPrefab is set, that model is fitted to the car's length and the
    /// primitives are hidden. Colors use SolidMaterials, so nothing draws magenta on the headset.
    /// </summary>
    public sealed class CarVisual : MonoBehaviour
    {
        [Tooltip("Optional car model (its license recorded first, HG-licenses). Empty = the primitive toy car.")]
        public GameObject sourcedModelPrefab;
        [Tooltip("Turn the model if its nose isn't along +Z.")]
        [SerializeField] float sourcedYawOffset;

        [Header("Colors")]
        [SerializeField] Color bodyColor = new Color(1f, 0.25f, 0.08f);
        [SerializeField] Color glassColor = new Color(0.45f, 0.75f, 0.95f);
        [SerializeField] Color tireColor = new Color(0.08f, 0.08f, 0.09f);
        [SerializeField] Color hubColor = new Color(0.85f, 0.85f, 0.88f);
        [SerializeField] Color headlightColor = new Color(1f, 0.95f, 0.6f);
        [SerializeField] Color taillightColor = new Color(0.9f, 0.05f, 0.05f);
        [SerializeField] Color bumperColor = new Color(0.2f, 0.2f, 0.22f);

        static float Scale => (float)Constants.Scale.RoomToBridge;

        Transform _root;
        Transform _body;                      // everything that doesn't turn with a wheel
        readonly Transform[] _wheels = new Transform[4];
        readonly Vector3[] _wheelAnchor = new Vector3[4];    // car-local wheel centers at rest (room units)
        Transform _model;
        readonly List<Transform> _modelWheels = new List<Transform>();
        readonly List<Quaternion> _modelWheelRest = new List<Quaternion>();

        /// <summary>Places the car for this frame.</summary>
        public void Show(CarPose pose)
        {
            if (pose == null)
                return;
            EnsureBuilt();
            _root.gameObject.SetActive(true);
            var rotation = Quaternion.LookRotation(pose.Forward.ToUnity(), pose.Up.ToUnity());
            _root.SetPositionAndRotation(pose.Origin.ToUnity(), rotation);
            var spin = Quaternion.AngleAxis((float)pose.SpinAngle * Mathf.Rad2Deg, Vector3.right);
            if (_model != null)
            {
                // The model's own wheels (children named "...Wheel...") turn about their axles.
                for (int i = 0; i < _modelWheels.Count; i++)
                    _modelWheels[i].localRotation = _modelWheelRest[i] * spin;
                return;
            }

            for (int w = 0; w < 4; w++)
            {
                // Each wheel sits at its own contact point, so the springs show; it turns about the axle.
                _wheels[w].SetPositionAndRotation(pose.WheelCenters[w].ToUnity(), rotation * spin);
            }
        }

        /// <summary>Sets the body color; call before the car is first shown.</summary>
        public void Tint(Color body) => bodyColor = body;

        public void Hide()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// A licensed model each developer imports locally (gitignored, HG-licenses) and names BridgeCar in any
        /// Resources folder. The game is built at runtime, so this is how it reaches the slot.
        /// </summary>
        public const string LocalModelName = "BridgeCar";

        void EnsureBuilt()
        {
            if (_root != null)
                return;
            _root = new GameObject("Car").transform;
            _root.SetParent(transform, false);
            if (sourcedModelPrefab == null)
                sourcedModelPrefab = Resources.Load<GameObject>(LocalModelName);
            if (sourcedModelPrefab != null)
                BuildSourced();
            else
                BuildPrimitive();
        }

        void BuildPrimitive()
        {
            var car = Constants.Car.Standard(Constants.Human.CarMassKg);
            _body = new GameObject("Body").transform;
            _body.SetParent(_root, false);
            for (int w = 0; w < 4; w++)
            {
                _wheels[w] = new GameObject("Wheel" + w).transform;
                _wheels[w].SetParent(_root, false);
                _wheelAnchor[w] = Local(SuspensionCar.WheelX(car, w), CarBlueprint.WheelRadius(car), SuspensionCar.WheelY(car, w));
            }

            foreach (var part in CarBlueprint.Build(car, Constants.Car.Length))
            {
                var go = GameObject.CreatePrimitive(part.Shape == CarPartShape.Wheel ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                go.name = part.Name;
                Destroy(go.GetComponent<Collider>());
                var renderer = go.GetComponent<Renderer>();
                renderer.sharedMaterial = SolidMaterials.Get(ColorOf(part.Color));
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var t = go.transform;
                Vector3 center = Local(part.Along, part.Up, part.Across);
                Vector3 size = new Vector3((float)part.SizeAcross, (float)part.SizeUp, (float)part.SizeAlong) / Scale;
                if (part.Wheel >= 0)
                {
                    t.SetParent(_wheels[part.Wheel], false);
                    t.localPosition = center - _wheelAnchor[part.Wheel];
                }
                else
                {
                    t.SetParent(_body, false);
                    t.localPosition = center;
                }
                if (part.Shape == CarPartShape.Wheel)
                {
                    // A Unity cylinder is 1 across and 2 tall along its Y; lay it on its side along the axle.
                    t.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    t.localScale = new Vector3(size.y, size.x / 2f, size.z);
                }
                else
                {
                    t.localScale = size;
                }
            }
        }

        /// <summary>A car-frame point in Unity local axes: x across, y up, z along (room units).</summary>
        static Vector3 Local(double along, double up, double across) => new Vector3((float)across, (float)up, (float)along) / Scale;

        Color ColorOf(CarPartColor c) => c switch
        {
            CarPartColor.Body => bodyColor,
            CarPartColor.Cabin => bodyColor * 0.85f,
            CarPartColor.Glass => glassColor,
            CarPartColor.Tire => tireColor,
            CarPartColor.Hub => hubColor,
            CarPartColor.Headlight => headlightColor,
            CarPartColor.Taillight => taillightColor,
            _ => bumperColor,
        };

        /// <summary>Fits the sourced model to the car's length and sits it on the ground; the primitives stay unbuilt.</summary>
        void BuildSourced()
        {
            var holder = new GameObject("Model").transform;
            holder.SetParent(_root, false);
            holder.localRotation = Quaternion.Euler(0f, sourcedYawOffset, 0f);
            var model = Instantiate(sourcedModelPrefab, holder, false);
            foreach (var c in model.GetComponentsInChildren<Collider>())
                Destroy(c);
            foreach (var t in model.GetComponentsInChildren<Transform>())
            {
                if (t != model.transform && t.name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _modelWheels.Add(t);
                    _modelWheelRest.Add(t.localRotation);
                }
            }

            var bounds = LocalBounds(holder.gameObject);
            float length = Mathf.Max(bounds.size.x, bounds.size.z);
            holder.localScale = Vector3.one * (length > 1e-5f ? (float)(Constants.Car.Length / Scale) / length : 1f);
            bounds = LocalBounds(holder.gameObject);
            holder.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            _model = holder;
        }

        /// <summary>Renderer bounds in the root's frame.</summary>
        Bounds LocalBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                var wb = r.bounds;
                foreach (var corner in Corners(wb))
                {
                    Vector3 p = _root.InverseTransformPoint(corner);
                    if (first)
                    {
                        b = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        b.Encapsulate(p);
                    }
                }
            }
            return b;
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        }
    }
}
