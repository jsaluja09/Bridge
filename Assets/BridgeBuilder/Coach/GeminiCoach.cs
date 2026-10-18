using System;
using System.Collections;
using System.Text;
using BridgeBuilder.Core;
using BridgeBuilder.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace BridgeBuilder.Coach
{
    public enum CoachState { Off, Idle, Thinking, Ready, Failed }

    /// <summary>
    /// The optional online coach (spec 11.7). When a test's results appear, it sends the facts to Gemini and puts
    /// the reply on the results panel. It installs itself and only listens to BridgeGame's events, so the game
    /// never depends on it. With no key in Resources/GeminiApiKey.txt it stays Off and sends nothing.
    /// </summary>
    public sealed class GeminiCoach : MonoBehaviour
    {
        const string KeyResource = "GeminiApiKey";
        const int FindGameFrames = 10;
        const string ThinkingNote = "<size=80%><color=#8FD3FF>Coach is thinking...</color></size>";
        const string OfflineNote = "<size=60%><color=#9A9A9A>Coach offline</color></size>";

        static readonly string Url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Constants.Coach.Model}:generateContent";

        string _key = "";
        BridgeGame _game;
        int _test;                            // bumped per test and on going back, so a late reply is dropped

        public CoachState State { get; private set; }
        public string Reply { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() => new GameObject(nameof(GeminiCoach)).AddComponent<GeminiCoach>();

        void Awake()
        {
            var asset = Resources.Load<TextAsset>(KeyResource);
            if (asset != null)
                _key = asset.text.Trim('\uFEFF', ' ', '\t', '\r', '\n');
            State = _key.Length > 0 ? CoachState.Idle : CoachState.Off;
        }

        IEnumerator Start()
        {
            if (State == CoachState.Off)
            {
                Debug.Log($"Gemini coach is off: no key in Resources/{KeyResource}.txt");
                yield break;
            }
            // Install and BridgeBootstrap run in no fixed order, so the game may not exist yet.
            for (int i = 0; i < FindGameFrames && _game == null; i++)
            {
                _game = FindAnyObjectByType<BridgeGame>();
                if (_game == null)
                    yield return null;
            }
            if (_game == null)
            {
                Debug.LogWarning("Gemini coach: no BridgeGame in the scene");
                yield break;
            }
            _game.TestFinished += OnTestFinished;
            _game.BackToBuilding += OnBackToBuilding;
        }

        void OnDestroy()
        {
            if (_game == null)
                return;
            _game.TestFinished -= OnTestFinished;
            _game.BackToBuilding -= OnBackToBuilding;
        }

        void OnTestFinished(TestOutcome test)
        {
            _test++;
            State = CoachState.Thinking;
            Reply = "";
            _game.ResultNote = ThinkingNote;
            StartCoroutine(Ask(_test, CoachPrompt.Build(Facts(test))));
        }

        void OnBackToBuilding()
        {
            _test++;
            State = CoachState.Idle;
            Reply = "";
        }

        static CoachFacts Facts(TestOutcome test) => new CoachFacts
        {
            ChallengeName = test.Level.Name,
            ChallengeBrief = test.Level.Brief,
            CarWeightKg = test.LastRun.Car.MassKg,
            WindSpeed = test.LastRun.WindSpeed,
            Grip = test.LastRun.Grip,
            Passed = test.Passed,
            Stars = test.Stars,
            MembersLost = test.MembersLost,
            PeakUtilization = test.PeakUtilization,
            MaterialSpent = test.MaterialSpent,
            Budget = test.Level.Budget,
            Message = test.Phases[test.Phases.Count - 1].Message,
            Failures = test.Failures,
            Phases = test.Phases,
        };

        IEnumerator Ask(int test, string prompt)
        {
            using var request = new UnityWebRequest(Url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Body(prompt))),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Constants.Coach.RequestTimeoutSeconds,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-goog-api-key", _key);
            yield return request.SendWebRequest();

            if (test != _test)
                yield break;
            string reply = Read(request, out string problem);
            if (reply.Length > 0)
            {
                State = CoachState.Ready;
                Reply = reply;
                _game.ResultNote = $"<size=80%><color=#8FD3FF><noparse>{reply}</noparse></color></size>";
            }
            else
            {
                State = CoachState.Failed;
                _game.ResultNote = OfflineNote;
                Debug.LogWarning("Gemini coach offline: " + problem.Replace(_key, "***"));
            }
        }

        /// <summary>The generateContent request: the coach's instructions, this test's facts, and the thinking level.</summary>
        static string Body(string prompt) => new JObject
        {
            ["system_instruction"] = new JObject { ["parts"] = new JArray(new JObject { ["text"] = CoachPrompt.SystemInstruction }) },
            ["contents"] = new JArray(new JObject
            {
                ["role"] = "user",
                ["parts"] = new JArray(new JObject { ["text"] = prompt }),
            }),
            // No maxOutputTokens: thinking tokens count against it, and a small cap can leave no reply text.
            ["generationConfig"] = new JObject { ["thinkingConfig"] = new JObject { ["thinkingLevel"] = Constants.Coach.ThinkingLevel } },
        }.ToString(Formatting.None);

        /// <summary>The cleaned reply, or "" with the HTTP status and error in problem.</summary>
        static string Read(UnityWebRequest request, out string problem)
        {
            try
            {
                string text = request.downloadHandler.text;
                var json = string.IsNullOrEmpty(text) ? null : JObject.Parse(text);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    problem = $"HTTP {request.responseCode} {request.error} {(string)json?.SelectToken("error.message")}";
                    return "";
                }
                string reply = CoachPrompt.CleanReply(ReplyText(json?.SelectToken("candidates[0].content.parts")));
                problem = reply.Length > 0 ? "" : $"empty reply ({(string)json?.SelectToken("candidates[0].finishReason") ?? "no candidates"})";
                return reply;
            }
            catch (Exception e)
            {
                problem = $"HTTP {request.responseCode} {request.error}: unreadable reply ({e.GetType().Name})";
                return "";
            }
        }

        /// <summary>The answer's text parts joined, skipping the model's thought summaries.</summary>
        static string ReplyText(JToken parts)
        {
            if (parts == null)
                return "";
            var text = new StringBuilder();
            foreach (var part in parts.Children<JObject>())
            {
                if ((bool?)part["thought"] != true)
                    text.Append((string)part["text"]).Append(' ');
            }
            return text.ToString();
        }
    }
}
