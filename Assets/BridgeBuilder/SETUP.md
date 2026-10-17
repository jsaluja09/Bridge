# BridgeBuilder MR: setup and controls

## Quick start: just build and run

`SampleScene` works as-is. On start, `BridgeBootstrap` hides the template's demo objects and adds the game,
and passthrough is forced on. Plug in the Quest, then **File → Build And Run** (Meta Quest or Android profile;
minimum API level 32 is set automatically for Quest 2).

## How a round works

1. **Brief.** Pick a challenge with the right stick (car weight, wind, grip, budget). A to start.
2. **Mark the surfaces: four taps.** Touch both ends of the start surface's edge that faces the gap, then both
   ends of the far surface's edge. Snap dots (fixed supports) appear along each edge, and ground dots appear
   on the floor under the gap for piers.
3. **Build.** Lines must start on a dot. Draw freehand: straight lines, arcs, and zigzags are recognized and
   cut into ~10 cm pieces. Every piece ends in a new dot you can build from.
4. **Deck.** Use the **Fill** tool to tap the corners of a flat shape (e.g. the two bottom chords and the
   surface edges), then the **Road** tool to flag it as road. The car only drives on road fills.
5. **Test.** A. The bridge settles under its own weight, then the car drives across. Members go green → yellow
   → red with load (stripes get denser too), pulse above 90%, and break past 100%. Pieces that come loose
   fall onto the floor and tables.

While you build, the bridge is colored by the load from its own weight (a dead-load preview); magenta
means a piece isn't connected to any support.

## Controls

| Button | Does |
|---|---|
| Right trigger | Use the current tool |
| Right stick left/right | Change tool: Draw, Fill, Road, Delete |
| Right stick click | Symmetry: mirror every line across the road's centerline |
| A | Test / back to building (brief: start) |
| B | Undo (Fill tool: remove the last corner) |
| X (left) | Clear the bridge (press twice); with nothing built, back to the brief |
| Y (left) | Swap which surface the car starts from |

Keyboard over Link: Space = A, Backspace = B, R = X, Y = Y, M = stick click, arrows = tool.

## Optional polish (Bridge Game component)

- **Car View → Car Prefab:** a free car model, e.g. Kenney's Car Kit (https://kenney.nl/assets/car-kit,
  CC0). Record each asset and its license in the team's asset list (spec §1). Set **Yaw Offset** if it drives
  sideways.
- **Sounds:** `MRTemplateAssets/Audio/ButtonClick.wav` (snap), `ButtonHover.wav` (error), `Goal.wav` (pass).
  A creak loop (e.g. a CC0 clip from freesound.org) on **Creak Clip** rises in pitch as members near failure.
- **Controller Pointer → Tip Offset** if the cursor dot isn't at the controller's tip.

## Tuning

Everything is on **Bridge Game → Settings**. Material and section were calibrated by the tests so a bare deck
fails under the standard car and a simple truss holds it; after changing them, run
**Window → General → Test Runner → EditMode → Run All**.

The grey line at the bottom of the panel is the live solver benchmark (spec §2.12): equations, matrix band,
milliseconds per step, and the time to start a test. Use it to set **Node Cap** on the headset.
