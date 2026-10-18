using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Floating text labels that face the player, each on a small dark pill, created as needed and reused. Call
    /// Begin each frame, Show each label, then End to hide the ones not shown. Text is only reassigned when it
    /// changes, so TextMeshPro rebuilds a label's mesh, and its pill is resized, only then.
    /// </summary>
    public sealed class LabelPool
    {
        const float PillPaddingX = 0.5f;                  // GUESS: shares of the text's height
        const float PillPaddingY = 0.3f;                  // GUESS
        // Behind the text: with the labels' LookRotation, local +Z points away from the player.
        const float PillDepth = 0.002f;
        const int PillSteps = 6;
        static readonly Color PillColor = new Color32(0x16, 0x1A, 0x24, 0xC8);   // GUESS: the HUD card's body at alpha 0.78

        sealed class Label
        {
            public TextMeshPro Text;
            public Mesh Pill;
        }

        readonly Transform _parent;
        readonly float _fontSize;
        readonly List<Label> _labels = new List<Label>();
        Material _pillMaterial;
        Color32 _pillColor;
        int _used;

        public LabelPool(Transform parent, float fontSize)
        {
            _parent = parent;
            _fontSize = fontSize;
        }

        public void Begin() => _used = 0;

        public void Show(string text, Vector3 position, Camera cam, Color color)
        {
            if (_used == _labels.Count)
                _labels.Add(Create());
            var label = _labels[_used++];
            var tmp = label.Text;
            if (!tmp.gameObject.activeSelf)
                tmp.gameObject.SetActive(true);
            if (!ReferenceEquals(tmp.text, text))
            {
                tmp.text = text;
                SizePill(label);
            }
            tmp.color = color;
            tmp.transform.position = position;
            tmp.transform.rotation = Quaternion.LookRotation(position - cam.transform.position, Vector3.up);
        }

        public void End()
        {
            for (int i = _used; i < _labels.Count; i++)
            {
                if (_labels[i].Text.gameObject.activeSelf)
                    _labels[i].Text.gameObject.SetActive(false);
            }
        }

        Label Create()
        {
            if (_pillMaterial == null)
            {
                _pillMaterial = new Material(Shader.Find("BridgeBuilder/HudCard") ?? Shader.Find("Sprites/Default"));
                _pillColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? PillColor.linear : PillColor;
            }
            var go = new GameObject("Label");
            go.transform.SetParent(_parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = _fontSize;
            tmp.rectTransform.sizeDelta = new Vector2(0.6f, 0.08f);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            var pill = new GameObject("Pill");
            pill.transform.SetParent(go.transform, false);
            var mesh = new Mesh { name = "Label Pill" };
            pill.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = pill.AddComponent<MeshRenderer>();
            r.sharedMaterial = _pillMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return new Label { Text = tmp, Pill = mesh };
        }

        /// <summary>A rounded pill just bigger than the label's text, centered on it.</summary>
        void SizePill(Label label)
        {
            Vector2 size = label.Text.GetPreferredValues();
            float halfH = size.y * (0.5f + PillPaddingY), halfW = size.x * 0.5f + size.y * PillPaddingX;
            float radius = Mathf.Min(halfH, halfW);
            var vertices = new Vector3[4 * (PillSteps + 1) + 1];
            vertices[0] = new Vector3(0f, 0f, PillDepth);
            int v = 1;
            for (int corner = 0; corner < 4; corner++)
            {
                float cx = corner == 0 || corner == 3 ? halfW - radius : radius - halfW;
                float cy = corner < 2 ? halfH - radius : radius - halfH;
                for (int step = 0; step <= PillSteps; step++)
                {
                    float angle = (corner + step / (float)PillSteps) * Mathf.PI * 0.5f;
                    vertices[v++] = new Vector3(cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius, PillDepth);
                }
            }
            int rim = vertices.Length - 1;
            var indices = new int[rim * 3];
            for (int i = 0; i < rim; i++)
            {
                indices[3 * i] = 0;
                indices[3 * i + 1] = 1 + i;
                indices[3 * i + 2] = 1 + (i + 1) % rim;
            }
            var colors = new Color32[vertices.Length];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = _pillColor;
            label.Pill.Clear();
            label.Pill.vertices = vertices;
            label.Pill.colors32 = colors;
            label.Pill.triangles = indices;
        }
    }
}
