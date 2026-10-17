using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// W4-08: Primitive "Hot Wheels" car built from Unity built-in shapes.
    /// Dimensions come from Constants.Car (wheelbase, track, wheel radius).
    /// If <see cref="sourcedModelPrefab"/> is assigned the primitives are hidden and the prefab is scaled
    /// to fit the wheelbase and track.  Wheels always spin from car speed and the body pitches/rolls from
    /// the suspension state, regardless of which visual is active.
    ///
    /// All sizes are in room-space metres.
    ///
    /// Quest 2 notes (Snapdragon XR2, tile-based GPU, URP):
    ///   - Shadow casting/receiving is disabled on every renderer: Quest 2's recommended URP pipeline
    ///     omits the shadow resolve pass, and casting adds an extra tile-flush pass on the XR2.
    ///   - One shared Material per colour (4 total). Sharing lets Unity merge draw calls and avoids
    ///     per-primitive material allocation that would defeat GPU instancing.
    ///   - Colliders are destroyed immediately: PhysX is not used in this project and keeping them
    ///     wastes broadphase budget on Android.
    /// </summary>
    public sealed class CarVisual : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────
        [Tooltip("Optional sourced car prefab (license must be recorded per spec 1). " +
                 "Empty = primitive Hot Wheels body. Assign and it replaces the primitives.")]
        [SerializeField] public GameObject sourcedModelPrefab;

        [Tooltip("Rotate the sourced prefab so its nose faces +Z (Unity forward).")]
        [SerializeField] float sourcedYawOffset;

        // ── Runtime ──────────────────────────────────────────────────────────
        Transform _root;          // overall root: position = ground contact; rotation = heading
        Transform _body;          // low slab body (or sourced model holder)
        Transform[] _wheels;      // four wheel pivots (null when using sourced model)
        bool _primitiveActive;
        float _cachedScale = -1f;

        // Wheel spin accumulator (radians).
        float _spinAngle;

        // ── Shared materials ─────────────────────────────────────────────────
        // Quest 2: one shared Material instance per colour lets Unity batch draw calls and
        // avoids per-frame material allocation.  Destroyed with the application.
        static Material _matBody;
        static Material _matCabin;
        static Material _matWheel;
        static Material _matHub;

        // ── Dimensions from Core constants (room-space) ───────────────────────
        static float RoomOf(double bridgeMetres) => (float)(bridgeMetres / Constants.Scale.RoomToBridge);

        static float Wheelbase => RoomOf(Constants.Car.Wheelbase);
        static float Track     => RoomOf(Constants.Car.Track);
        static float CarLength => RoomOf(Constants.Car.Length);
        static float WheelR    => RoomOf(Constants.Car.StaticSag); // spring sag ≈ wheel visual radius, GUESS

        // Body shape ratios (relative to car length / track).
        const float BodyHeightRatio  = 0.18f;
        const float BodyWidthRatio   = 0.70f;
        const float CabinLengthRatio = 0.38f;
        const float CabinHeightRatio = 0.26f;

        // Hot-Wheels colour palette.
        static readonly Color BodyColor  = new Color(1.00f, 0.22f, 0.05f); // vivid red-orange
        static readonly Color CabinColor = new Color(0.55f, 0.85f, 1.00f); // light-blue glass
        static readonly Color WheelColor = new Color(0.10f, 0.10f, 0.12f); // dark rubber
        static readonly Color HubColor   = new Color(0.90f, 0.75f, 0.20f); // gold hub

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Show the car at the given room-space pose.  <paramref name="roomForward"/> and
        /// <paramref name="roomUp"/> come directly from <see cref="RunSnapshot"/> and already
        /// encode pitch and roll.
        /// </summary>
        public void Show(Vector3 roomPosition, Vector3 roomForward, Vector3 roomUp,
                         float roomLength, float speed, float pitch, float roll, float dt)
        {
            EnsureBuilt();
            if (!Mathf.Approximately(roomLength, _cachedScale))
                Fit(roomLength);

            _root.gameObject.SetActive(true);
            _root.SetPositionAndRotation(
                roomPosition,
                Quaternion.LookRotation(roomForward.normalized, roomUp.normalized));

            // Wheel spin: ω = v_room / r_room.
            float vRoom  = speed > 0 ? (float)(speed / Constants.Scale.RoomToBridge) : 0f;
            float angVel = WheelR > 1e-5f ? vRoom / WheelR : 0f;
            _spinAngle += angVel * dt;

            if (_primitiveActive && _wheels != null)
            {
                for (int i = 0; i < _wheels.Length; i++)
                    if (_wheels[i] != null)
                        _wheels[i].localRotation = Quaternion.Euler(_spinAngle * Mathf.Rad2Deg, 0f, 0f);

                if (_body != null)
                    _body.localRotation = Quaternion.Euler(pitch * Mathf.Rad2Deg, 0f, roll * Mathf.Rad2Deg);
            }
            else if (!_primitiveActive && _body != null)
            {
                _body.localRotation = Quaternion.Euler(pitch * Mathf.Rad2Deg, sourcedYawOffset, roll * Mathf.Rad2Deg);
            }
        }

        /// <summary>Hides the car.</summary>
        public void Hide()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        void EnsureBuilt()
        {
            if (_root != null) return;
            EnsureMaterials();
            _root = new GameObject("CarVisual").transform;
            _root.SetParent(transform, false);
            if (sourcedModelPrefab != null) BuildSourced();
            else                            BuildPrimitive();
        }

        // ── Shared material helpers ──────────────────────────────────────────

        static void EnsureMaterials()
        {
            if (_matBody != null) return;

            // URP Lit is the correct shader for Quest 2 + URP.  Fall back to Standard if
            // this assembly is loaded in a non-URP project (e.g. the CI build).
            Shader s = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");

            _matBody  = MakeMat(s, BodyColor);
            _matCabin = MakeMat(s, CabinColor);
            _matWheel = MakeMat(s, WheelColor);
            _matHub   = MakeMat(s, HubColor);
        }

        static Material MakeMat(Shader shader, Color color)
        {
            var mat = new Material(shader) { color = color };
            mat.enableInstancing = true; // allow GPU instancing within this colour group
            return mat;
        }

        /// <summary>
        /// Assigns the shared material and disables shadow casting/receiving.
        /// Quest 2: every shadow pass (cast map + receive resolve) costs an extra tile-flush on the XR2.
        /// <see cref="BridgeView"/> already does this for bridge members; we match the convention here.
        /// </summary>
        static void ApplyMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.sharedMaterial    = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
        }

        // ── Primitive car construction ────────────────────────────────────────

        void BuildPrimitive()
        {
            _primitiveActive = true;
            float len = CarLength;
            float trk = Track;
            float wb  = Wheelbase;
            float wr  = WheelR;
            float bh  = len * BodyHeightRatio;
            float bw  = trk * BodyWidthRatio;

            // Low wide slab body.
            _body = MakeBox("Body", new Vector3(len, bh, bw), _matBody);
            _body.SetParent(_root, false);
            _body.localPosition = new Vector3(0f, wr + bh * 0.5f, 0f);

            // Smaller cabin on top of body.
            float cLen = len * CabinLengthRatio;
            float cHgt = len * CabinHeightRatio;
            var cabin = MakeBox("Cabin", new Vector3(cLen, cHgt, bw * 0.9f), _matCabin);
            cabin.SetParent(_root, false);
            cabin.localPosition = new Vector3(len * 0.05f, wr + bh + cHgt * 0.5f, 0f);

            // Four oversized wheels (0=FL, 1=FR, 2=RL, 3=RR).
            _wheels = new Transform[4];
            float halfWb  = wb * 0.5f;
            float halfTrk = trk * 0.5f + wr * 0.4f; // slightly outside the body
            for (int i = 0; i < 4; i++)
            {
                float wx = i < 2 ?  halfWb : -halfWb;
                float wz = i % 2 == 0 ?  halfTrk : -halfTrk;
                _wheels[i] = MakeWheel("Wheel" + i, wr, wx, wz);
            }
        }

        Transform MakeBox(string name, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>()); // PhysX not used; remove to save broadphase cost
            go.transform.localScale = size;
            ApplyMat(go, mat);
            return go.transform;
        }

        Transform MakeWheel(string name, float radius, float x, float z)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(_root, false);
            pivot.transform.localPosition = new Vector3(x, radius, z);

            // Unity Cylinder: 1 m tall along Y, 1 m diameter in X/Z.
            // Rotate 90° so the flat disc faces outward (car's Z axis).
            float thick = radius * 0.55f;

            var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.name = "Tyre";
            Destroy(tyre.GetComponent<Collider>());
            tyre.transform.SetParent(pivot.transform, false);
            tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tyre.transform.localScale    = new Vector3(radius * 2f, thick, radius * 2f);
            ApplyMat(tyre, _matWheel);

            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = "Hub";
            Destroy(hub.GetComponent<Collider>());
            hub.transform.SetParent(pivot.transform, false);
            hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            hub.transform.localScale    = new Vector3(radius, thick * 0.25f, radius);
            ApplyMat(hub, _matHub);

            return pivot.transform;
        }

        // ── Sourced model ────────────────────────────────────────────────────

        void BuildSourced()
        {
            _primitiveActive = false;
            _body = new GameObject("SourcedHolder").transform;
            _body.SetParent(_root, false);

            var model = Instantiate(sourcedModelPrefab);
            foreach (var c in model.GetComponentsInChildren<Collider>())
                Destroy(c);
            // Disable shadows on every renderer in the sourced prefab (Quest 2 compatibility).
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows    = false;
            }
            model.transform.SetParent(_body, false);
            _wheels = null;
        }

        void Fit(float roomLength)
        {
            _cachedScale = roomLength;
            if (!_primitiveActive)
            {
                // Scale sourced model so its longest bounding-box axis matches roomLength.
                _body.localScale    = Vector3.one;
                _body.localRotation = Quaternion.Euler(0f, sourcedYawOffset, 0f);
                _body.localPosition = Vector3.zero;

                var bounds  = WorldBounds(_body.gameObject);
                float span  = Mathf.Max(bounds.size.x, bounds.size.z);
                _body.localScale = Vector3.one * (span > 1e-5f ? roomLength / span : 1f);
                bounds           = WorldBounds(_body.gameObject);
                // Seat the bottom of the model at the root (ground contact).
                _body.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }
            // Primitives are sized from Core constants and need no re-fit.
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0)
                return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}
