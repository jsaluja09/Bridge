using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The start menu: Build (the bridge game) or Learn (famous bridges, step by step). Each mode lives on its own
    /// child object, active only while it runs; a mode hands control back by raising ExitRequested. The chosen mode
    /// starts the frame after the press, so the press that picked it doesn't also act inside it.
    /// </summary>
    [RequireComponent(typeof(ControllerPointer))]
    public sealed class ModeMenu : MonoBehaviour
    {
        [Tooltip("Show the real room behind everything (Quest passthrough).")]
        [SerializeField] bool passthrough = true;
        [SerializeField] float fontSize = 0.3f;

        static readonly string[] Options = { "Build a bridge", "Learn from famous bridges" };
        static readonly string Keys = HudCard.Key("Stick", "Choose") + "   " + HudCard.Key("A", "Start");

        ControllerPointer _pointer;
        HudCard _card;
        BridgeGame _build;
        LearnMode _learn;
        GameObject _running;          // the active mode's object, or null while the menu shows
        GameObject _starting;         // the mode to start next frame
        int _choice;

        void Awake()
        {
            _pointer = GetComponent<ControllerPointer>();
            _build = CreateMode<BridgeGame>("Build");
            _learn = CreateMode<LearnMode>("Learn");
            _build.ExitRequested += ShowMenu;
            _learn.ExitRequested += ShowMenu;

            var go = new GameObject("Menu Card");
            go.transform.SetParent(transform, false);
            _card = go.AddComponent<HudCard>();
        }

        void Start()
        {
            if (passthrough)
                Passthrough.Enable();
        }

        /// <summary>A mode on its own child object, inactive until chosen.</summary>
        T CreateMode<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        void Update()
        {
            if (_starting != null)
            {
                _running = _starting;
                _starting = null;
                _running.SetActive(true);
                return;
            }
            if (_running != null)
                return;

            if (_pointer.StickStep != 0)
                _choice = (_choice + Options.Length + _pointer.StickStep) % Options.Length;
            if (_pointer.PrimaryPressed || _pointer.TriggerPressed)
            {
                _starting = _choice == 0 ? _build.gameObject : _learn.gameObject;
                _card.gameObject.SetActive(false);
                return;
            }
            DrawMenu();
        }

        void ShowMenu()
        {
            if (_running != null)
                _running.SetActive(false);
            _running = null;
            _card.gameObject.SetActive(true);
        }

        void DrawMenu()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            _card.Show(new HudCardContent
            {
                Kind = HudCardKind.Menu,
                Title = "Bridge Builder",
                Choices = Options,
                Selected = _choice,
                Controls = Keys,
            }, fontSize);
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 position = cam.transform.position + forward * 0.9f - Vector3.up * 0.05f;
            _card.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - cam.transform.position, Vector3.up));
        }
    }
}
