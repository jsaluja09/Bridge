using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The one way to put a short message on the HUD (W4-07). Any part of the game calls Show; the HUD panel
    /// draws Current above its own text. A newer message replaces an older one.
    /// </summary>
    public static class HudMessages
    {
        /// <summary>How long a message stays up unless the caller says otherwise: "a few seconds". GUESS.</summary>
        public const float DefaultSeconds = 4f;

        static string _text;
        static float _until;

        public static void Show(string text, float seconds = DefaultSeconds)
        {
            _text = text;
            _until = Time.time + seconds;
        }

        public static void Clear() => _text = null;

        /// <summary>The message still showing, or null.</summary>
        public static string Current => _text != null && Time.time < _until ? _text : null;
    }
}
