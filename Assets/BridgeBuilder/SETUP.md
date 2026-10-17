# BridgeBuilder MR: setup and how to play

## Build and run

`SampleScene` works as-is: on start, `BridgeBootstrap` hides the template's demo objects, adds the game,
and turns on passthrough. Plug in the Quest, then **File → Build And Run**. (Minimum API level 32 for
Quest 2 is set automatically.)

## How to play (for students)

1. **Pick a challenge.** Push the right stick left/right, then press **A**.
2. **Place the two ends.** Touch where the bridge starts and pull the trigger, then where it ends.
   Two orange pads appear. The car parks on the start pad.
3. **Draw.** Hold the trigger and draw in the air; let go to finish the line. Lines can start anywhere.
   They snap together where they touch, and they stick to the pads (and to the floor dots, for piers).
4. **Make a road.** Two flat lines side by side, from one pad to the other, automatically get a road
   between them.
5. **Test.** Press **A**. The car drives across. Members turn green → yellow → red as they work harder,
   pulse when close to breaking, and snap past 100%. A red dot marks where something broke, and the panel
   says why, in plain words.

While building, colors show how hard the bridge works just to hold itself up. Purple means a piece
isn't attached to anything and will fall.

| Button | Does |
|---|---|
| Right trigger (hold) | Draw a line |
| Right grip | Delete the line you're touching |
| A | Test / back to building |
| B | Undo |
| X (left) | Clear the bridge (press twice); with nothing built, back to the challenge list |
| Y (left) | Swap which pad the car starts on |

Keyboard over Link: Space = A, Backspace = B, Delete = grip, R = X, Y = Y, arrows = stick.

## Optional polish (Bridge Game component)

- **Car View → Car Prefab:** a free, license-checked car model, e.g. Kenney's Car Kit
  (https://kenney.nl/assets/car-kit, CC0). Record it in the team's asset list (spec §1). Set **Yaw Offset**
  if it drives sideways.
- **Sounds:** `MRTemplateAssets/Audio/ButtonClick.wav` (snap), `ButtonHover.wav` (error), `Goal.wav` (pass),
  and a CC0 creak loop on **Creak Clip**.
- **Controller Pointer → Tip Offset** if the cursor dot isn't at the controller's tip.

## For developers

The physics is the `BridgeBuilder.Core` library in `Assets/BridgeBuilder.Core` (no Unity code). It is built
and tested outside Unity:

```bash
dotnet test tests/BridgeBuilder.Core.Tests/BridgeBuilder.Core.Tests.csproj
```

Work is split into the GitHub issues (waves W0–W3); see `AGENTS.md`. Tuning values live in
`Constants.*.cs`, each tagged GUESS, DERIVED or HUMAN_OWNED. The spec is `docs/spec.md`; section 11 records
the playtest changes (free drawing, pads, automatic decks).

The grey line at the bottom of the panel is the live solver benchmark (spec §2.12): members, milliseconds per
step, time to start a test, and preview time. Use it on the headset to confirm the node cap.
