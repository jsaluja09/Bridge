# BridgeBuilder MR: setup (hackathon build)

## Scene (about 10 minutes)

1. Open `Assets/Scenes/SampleScene` and save a copy as `Assets/Scenes/Bridge` so the template stays intact.
2. Turn off the template's demo objects in the Hierarchy (untick each one):
   `Coaching UI`, `Tutorial Player`, `Hand Menu Setup MR Template Variant`, `Tooltip Worldspace`,
   `Spatial Panel Manipulator`, and the `UI` object (spawner dropdown and toggles).
   Inside `MR Interaction Setup`, find `Object Spawner` (search the Hierarchy for it) and turn it off,
   so the trigger doesn't spawn template objects.
   Keep `MR Interaction Setup`, `Permissions Manager`, `Directional Light` and `Lighting`.
3. Create an empty GameObject named `BridgeGame` at the origin and add the **Bridge Game** component.
   It adds `Controller Pointer`, `Bridge View` and `Car View` automatically.
4. Optional but nice:
   - **Bridge View → Member Material**: any URP Lit material (e.g. `MRTemplateAssets/Materials/Primitive/Interactables.mat`).
     Leave empty and one is made at runtime.
   - **Bridge Game → Snap Clip / Error Clip / Pass Clip**: `MRTemplateAssets/Audio/ButtonClick.wav`, `ButtonHover.wav`, `Goal.wav`.
5. Car model: download Kenney's **Car Kit** (https://kenney.nl/assets/car-kit, CC0 license, free for any use).
   Import one car `.fbx`, drag it into **Car View → Car Prefab**. It's scaled to `Car Length` automatically.
   If it drives sideways or backwards, set **Yaw Offset** to 90, -90 or 180.
   Record the asset and its license in the team's asset list (spec §1).
6. Add the scene to **File → Build Profiles → Scene List** and untick `SampleScene`.

## Controls (right controller unless noted)

| Button | Does |
|---|---|
| Trigger | Mark a surface edge / hold to draw a line from a dot, release to place it |
| A | Run the test / go back to building |
| B | Undo the last line |
| X (left) | Clear all lines; press again with no lines to re-mark the surfaces |

Keyboard over Link: Space = A, Backspace = B, R = X.

## How a round works

1. Touch the edge of the start surface where the gap begins, pull the trigger. Then the far surface.
   The car starts on the first surface.
2. Draw on the vertical plane between the two taps (a thin line shows where your controller projects onto it).
   Lines must start on a dot. Ends snap to a dot or to a 5 cm grid.
3. Your side is mirrored across the road width, with automatic cross-bracing. Flat lines (up to 20°) become road.
4. A: gravity ramps in for 1 s, then the car drives across. Members go green → yellow → red with load and break past 100%.

## Tuning

Everything is on **Bridge Game → Settings** and can be changed during Play mode (it applies on the next test).

- Bridges too strong: lower `Tension Limit`, `Compression Limit` or `Bending Limit`, or raise `Car Mass`.
- Long struts not buckling enough: lower `Buckling Coefficient` (capacity = coefficient / length²).
- Long road spans holding: lower `Bending Limit`.
- Bridge too wobbly: raise `Substeps` or `Iterations` (costs CPU).
- Cursor not at the controller tip: **Controller Pointer → Tip Offset**.

After changing defaults in `BridgeSettings.cs`, run the tests: **Window → General → Test Runner → EditMode → Run All**.
They check that bare spans and long planks fail and that a king-post truss and a Pratt truss hold.
