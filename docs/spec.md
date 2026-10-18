# BridgeBuilder MR: MVP Spec (working draft)

A mixed-reality physics game for Quest 2 (Unity, Meta XR SDK). The player marks two real surfaces, draws a bridge between them in mid-air, and the game tests it with a car and a condition such as wind. This is a local prototype that runs on one headset.

**Status: working draft.** This records the decisions reached so far in conversation, and it is meant to change. Any decision, rationale, or number here can be challenged. Anything marked "about", "roughly", or "~" is a starting guess, not a derived value. Edit it directly or comment on any line. Section 8 lists the numbers most in need of scrutiny. Section 11 records changes made after playtesting.

## 1. Goals

- Free-draw 3D input that the game converts into real structural members.
- Realistic component-level physics: members carry load, bend, buckle, and break.
- A middle schooler can draw almost any connected shape and see why it holds or fails.
- Bare spans fail. Supports, triangulation, and fills visibly matter.

**Non-goals for the MVP:** multiple materials, per-line thickness, plastic yielding, multiplayer, saving across sessions, anything beyond a single car per run.

**Constraint on assets.** All 3D models and other assets (the car, props, markers) come from free online libraries, with each asset's license checked and recorded. No agent or AI tool generates models for this project. If a needed asset has no free source, it becomes an open question instead of being generated. Bridge members and fills are the exception in kind: the game builds their meshes at runtime from the solver's geometry, so they are not sourced assets.

## 2. Current decisions

### 2.1 Input and recognition

- The player draws freehand in 3D with a controller.
- Every line must start from an existing snap point. *(Changed: see 11.1.)*
- The recognizer accepts **lines, arcs, and polylines** (triangles, zigzags, W shapes). Anything else is rejected with clear visual feedback. *(Changed: see 11.1.)*
- The recognizer is the riskiest component. It works in free 3D space, so depth noise is a known problem. Snap-point starts and endpoints reduce it, because connections are explicit instead of guessed.

### 2.2 Surface setup

- The player marks each surface with **two touches**: the controller tip on each end of the edge that faces the gap, trigger to drop a marker. Four taps in total. *(Changed: see 11.2.)*
- The game places snap dots evenly along each marked edge. Those dots become **fixed supports** in the solver.
- The marked edge length sets the maximum road width on that side.
- Up comes from headset gravity. The gap direction comes from the line between the two edges.
- Surfaces can be at different heights.
- *Deferred:* what to do when the two edges are not parallel (use the shorter edge, or ask the player to realign).

### 2.3 Snapping

- Each line is divided into the nearest whole number of **equal segments**, targeting about **10 cm** spacing. This avoids tiny runt segments.
- Every snap point is a solver node. Every segment between two nodes is a frame element.
- Attaching a new line means starting it from an existing node, or from a point on an existing line between its nodes. That member is split in two there, so both lines share the new node. *(Changed: see 11.5.)*
- Node count is roughly total line length divided by spacing, which gives a direct formula for the performance cap.
- The spacing value is a tuning constant, settled in the headset.

### 2.4 Fills and roads

- The player can select a **closed, planar polygon** and fill it. Non-planar loops are rejected. *(Changed: see 11.3.)*
- A fill is a visible surface plus **hidden automatic bracing** (diagonal members using the same frame elements). It looks solid, adds stiffness, and keeps the solver to one element type.
- **Only fills can be roads, and the player flags which fills are roads.** The game validates the flagged chain: it must connect the two marked surfaces, and each fill must be gentle enough to drive on (target maximum slope about 20 degrees, tunable). If validation fails, the game highlights the gap or the too-steep fill.
- The road's width comes from the fill itself.

### 2.5 Ground plane and supports

- A **virtual ground plane sits at the real floor height** tracked by the headset.
- Snap dots on the plane (on a grid, limited to the footprint under the span) let the player build piers down to it. This is optional. Arches and trusses can still span without it.
- A challenge may override the ground depth if a room makes things too easy or too hard.
- All supports (surface anchors and ground anchors) use one **fixed** condition. A pinned option stays available later as a one-flag change.
- A bare span of two lines from the surfaces **must fail**. See the first test fixture in section 5.

### 2.6 Scale

- The solver runs in a scaled-up world. Default is **20x**: a 1 m room gap is a 20 m span. Assumed road-overpass feel, one constant to change.
- The recognizer, snapping, and rendering stay in room units. A small adapter on the solver side applies the scale, so VR code never sees scaled units.
- Real materials and a realistic car are used in bridge-world units.

### 2.7 Structural model

- **Rigid-jointed frame elements** (members resist bending, not just axial force). Almost any connected drawing stands up, and triangles still make a bridge measurably stronger.
- **One fixed cross-section** for every member in the MVP. The data model carries a per-member section value from day one, set to the same constant, so per-line thickness is an easy first upgrade.
- **Failure modes:** tension (yield), compression, and **buckling** (Euler-style, so long slender members under compression fail early). The check is one closed-form capacity test per member, combined with bending.
- When a member fails it is removed, the structure redistributes load, and the solver continues. This gives progressive collapse.
- Plastic yielding and progressive damage are a later layer. The solver stays linear between failures, so this means adding state to members and not rewriting the core.

### 2.8 Dynamics and the car

- The bridge uses **real dynamics with an implicit integrator** (Newmark average-acceleration or similar) and light damping. This stays stable at any timestep, which avoids the wobble-and-explode behavior of rigid-body engines.
- The car is a small **suspension-based vehicle** with wheel contact points, not a rigid-body pile.
- **Lagged two-way coupling:** each step, the bridge solves with the car's last wheel loads, then the car reads the new deck height and slope at each wheel and updates its suspension and speed. The car dips a deck under a heavy truck, bounces on a flexing deck, and can ride a collapsing deck down.
- The player chooses which surface the car starts from. With surfaces at different heights, this decides whether the car climbs or descends. Wind direction also meets the car differently depending on travel direction.

### 2.9 Game rules

- **Material budget:** every line and fill costs by length or area. One material in the MVP. A hidden hard node cap protects performance and is enforced through the budget so the player never sees a separate rule.
- **Challenges are authored and shown before building**, like a design brief: for example, heavy truck, crosswind 15 m/s, budget 500. In the MVP each is a small config object (car weight, wind speed, grip, budget). Free play with player-chosen conditions comes later.
- **Scoring:** the run passes if the car reaches the far surface, even if some members broke. A star rating comes from members lost, peak utilization, and budget remaining. The first build can be pass or fail plus members lost.

### 2.10 Feedback

- **Utilization coloring** is the default. Each member is colored by how close it is to failing, taking the worst of its tension, compression, and buckling checks (green, yellow, red).
- A joint takes the color of the worst attached member. A fill takes its color from its bracing and surrounding frame.
- The solver also outputs signed member force, so a tension and compression view can be added later with no new physics.
- **Second cue for accessibility** (details deferred): wide, low-frequency stripes that get denser as utilization rises, a pulse on members past about 90% utilization, and a creak that rises in pitch as the worst member nears failure. A member also visibly bows as it approaches its buckling load.
- **Dead-load preview while building:** after each edit, once the player pauses about 300 ms, the worker runs one static solve under the bridge's own weight and colors members with the same scale. It is cancelled if the player starts drawing again. It reveals mechanisms and weak spots without revealing whether the car will make it.

### 2.11 Editing and retry

- One **undo stack** covers every action.
- The player can point at any line or fill to delete it, with a budget refund, and can reset the whole bridge.
- Deleting a line that others attach to is **blocked**, and the dependents are highlighted so the player sees why. Deleting a line that borders a fill invalidates the fill, with a warning.
- Tests run on a **copy** of the bridge, so after a collapse the pre-test state returns automatically and the player edits and runs again.

### 2.12 Performance

- The solver and car run together on a **background worker thread** at a fixed rate (about 60 Hz). The render thread only reads the latest snapshot of displacements and utilizations, so the visual lag is about one solver step (roughly 15 to 30 ms).
- The stiffness matrix uses **banded storage** with node ordering (reverse Cuthill-McKee), from day one.
- Refactorization is needed only at run start and when a member fails. Every other step is a cheap back-substitution.
- **Node cap about 150** (roughly 900 equations), to be confirmed by a headset benchmark before the budget numbers are locked. My dense-factorization estimate was 100 to 250 ms for a failure event at that size, and banding should reduce it to a few milliseconds. These are estimates, not measurements.

## 3. Architecture and data contract

The solver and game layer are separated by a **plain C# assembly with no Unity dependency**, passed in memory so there is no serialization cost in the simulation loop.

**Inputs to the solver:**

- Nodes: id, position (room units), whether fixed (support).
- Members: id, two node ids, section value (a constant in the MVP).
- Fills: a loop of node ids, plus a road flag.
- Run configuration: car parameters, start surface, condition (wind, grip, car weight), scale factor.

**Outputs per step:**

- Node displacements.
- Per-member signed force and utilization.
- A list of members that failed this step.
- Car state (position, wheel loads, suspension).

**Notes:**

- JSON serialization of the same types is used only for saved test bridges and fixtures, not in the hot path.
- Room-to-bridge scaling lives in an adapter on the solver side.
- A "line" is just a chain of members created together.
- The solver can be unit-tested on a PC and run on the headset unchanged.

## 4. Module map

- **Surface setup:** two-touch marking, anchor nodes, ground plane.
- **Drawing and recognition:** stroke capture, snap-start enforcement, line/arc/polyline fitting, validity feedback.
- **Bridge model:** nodes, members, fills, budget, undo stack.
- **Solver:** frame-element assembly, banded matrix, implicit integration, failure checks.
- **Car:** suspension model, coupling with the solver.
- **Challenge and scoring:** config objects, pass or fail, member count.
- **Rendering and feedback:** utilization coloring, bowing, later the second cue.

## 5. Starting test fixtures

These are shared by both tracks and live as JSON bridges.

1. **Two bare cantilevers must fail.** Two lines from the surfaces with nothing else must fail under the standard car. Tune values until this holds. It also catches any later change that makes bridges too strong.
2. **Simply supported beam:** deflection and member force match hand-calculated values.
3. **Unbraced rectangle:** under load it behaves as a flexible frame, not a mechanism, because joints are rigid. The same rectangle with a diagonal is measurably stiffer.
4. **Long vs short compression member:** the long one buckles first, at the expected load.
5. **Triangulated truss:** holds the standard car with a visible margin.
6. **Progressive failure:** removing a member redistributes load, and the solver does not blow up or produce NaN.
7. **Pier to ground plane:** a fixed pier carries load until it buckles.

## 6. Build plan

Build in thin vertical slices, run as **two parallel tracks that meet at slice 2**. One person builds the solver against the fixtures while the other builds slice 1 in the headset. They meet at the shared data model.

**Do first, before slice 2:**

- Define the data model.
- Write the solver fixtures.
- Run the headset benchmark for the node cap (a throwaway dense-matrix test).

**Slices:**

1. **Feel:** mark two surfaces, draw snapped lines in the headset, no physics. Answers whether free-drawing with snap points is pleasant.
2. **Physics:** a bridge of lines holds or fails under one car, with utilization coloring. Includes the worker thread, banded solver, and car coupling.
3. **Structure:** fills and hidden bracing, road flagging and validation, ground plane and supports.
4. **Game:** budget, challenges and conditions, start-surface choice, scoring, undo and delete, dead-load preview.

**After slice 2:** fills and bracing, the star rating, weather beyond one wind force, the second accessibility cue, and the dead-load preview.

## 7. Deferred to build and tuning

- Non-parallel marked edges.
- Recognizer tolerance thresholds.
- Fill bracing pattern.
- Tuning values: snap spacing, scale, material strength, damping ratio, maximum road slope.
- Star rating thresholds.
- Stripe pattern for the second accessibility cue (avoid shimmer on the Quest 2).
- Node cap, pending the benchmark.

## 8. Numbers and assumptions to challenge

None of these values comes from a measurement or a source. Treat each as a hypothesis with a named way to test it, and replace it when the test disagrees.

| Value | Where used | Basis today | How to check or replace it |
|---|---|---|---|
| 20x world scale | 2.6 | A guess at a road-overpass feel | Run the cantilever and truss fixtures at 10x, 20x, and 40x. Keep the scale where failures teach the right lesson. |
| ~10 cm snap spacing | 2.3 | Keeps node count manageable and gives 2 m joints at 20x. Not tested against controller jitter. | Try 5, 10, and 15 cm in slice 1. Watch snap reliability and node count. |
| ~150 node cap | 2.12 | Back-of-envelope from solver cost. No measurement. | Headset benchmark with dense and banded solves. Set the cap from measured step time. |
| 100 to 250 ms dense refactorization | 2.12 | Rough estimate from matrix size | Same benchmark. Discard the estimate if measurements differ. |
| ~60 Hz solver rate, 15 to 30 ms lag | 2.12 | Conventional rate, with the lag derived from it | Measure end-to-end latency on the headset. Lower the rate if frames drop. |
| ~20 degree maximum road slope | 2.4 | Rule of thumb for drivability | Playtest with the car model. Replace it with what the suspension actually handles. |
| ~300 ms preview debounce | 2.10 | Guess at "the player has paused" | Playtest. Shorten if the preview feels laggy, lengthen if it distracts. |
| ~90% pulse threshold | 2.10 | Guess at "about to fail" | Compare against the utilization at which members actually fail in test runs. |
| Section size and material strength | 2.7 | Not chosen yet | Calibrate until the two-cantilever fixture fails and a triangulated truss passes (section 5). |
| Budget values and star thresholds | 2.9 | Not chosen yet | Set after slice 2 from measured bridge costs. |

Decisions can be challenged as well as numbers. If a choice in section 2 (for example fixed supports, one cross-section, or lagged two-way coupling) turns out to be wrong in the headset, change it and note why.

## 9. Later upgrades

- Per-line thickness and multiple materials.
- Plastic yielding and progressive damage.
- Tension and compression color view.
- Pinned supports as a challenge option.
- Free play with player-chosen conditions.
- Escalating gauntlets of cars and weather.
- Room scanning through the Scene API as a convenience.

## 10. Top risks

1. **Recognizer in free 3D:** depth noise and ambiguous planes. Mitigated by snap-point starts and the validity feedback.
2. **Solver performance on the Quest 2:** mitigated by the worker thread, banded storage, and the benchmark-confirmed node cap.
3. **Tuning:** bridges that are too strong or too fragile. Mitigated by the two-cantilever fixture and single-constant tuning knobs.
4. **Passthrough and surface marking accuracy:** the Quest 2 has no depth sensor, so marked edges depend on controller tracking accuracy.
5. **Scope:** the design is large. The slice plan keeps something playable at every stage.

## 11. Changes after the first headset playtest

The first full build was hard to use: tools to switch between, a recognizer that rejected strokes, and four taps of setup. These changes trade some of section 2 for a simpler loop. The Core library still implements the original behavior where an issue specifies it (for example the recognizer still reports `SnapStart`); the game layer uses it differently.

### 11.1 Free drawing

Holding the trigger starts a line anywhere in space; releasing it ends the line. The stroke is smoothed and drawn as the shape the player meant. A stroke that bows only a little for its length becomes straight. One that bows more and roughly follows a circle becomes a clean circular arc through its two ends, bulging the way it was drawn. A zigzag, triangle, or W keeps straight sides between its corners. Other smooth curves, such as an S, are kept as drawn. Shapes are read forgivingly: a squiggle (one that changes bend direction more than about twice, or bends more than about one full circle in all) is not thrown away but becomes a straight beam from where it started to where it ended. Only a stroke too short to be a beam is rejected: it flashes red and fades, and the HUD says to draw a little longer. With mirroring on (the default; Y toggles it), everything a stroke builds is copied across the road's centerline, and the stroke and its copy are one undo step. Each straight side, arc, or curve is divided into equal ~10 cm segments, so an arc's joints sit evenly on its circle. Its start, end, and any point where it passes within the snap radius of an existing dot fuse with that dot, and a stroke that starts on, ends on, or crosses a line joins it as in 11.5; the split point is where the clean shape meets the line. Lines that touch nothing are allowed; the dead-load preview shows them as unsupported and they fall when tested. The thresholds are tuning constants.

### 11.2 Two-point setup with pads

Setup is two taps: one per end of the bridge. Each tap places a flat rectangular pad facing the other tap. The pad's whole top is a fixed support covered in snap dots, and the car starts on one pad and must reach the other. The four-tap edge math (W1-08) remains in Core.

### 11.3 Automatic decks

There is no fill or road tool. Two lines that are each flat (under the road slope limit), run roughly parallel, and sit side by side at a drivable spacing get a deck between them automatically. The deck is a road fill with hidden bracing, costs material by area, and is validated as in 2.4.

### 11.4 Delete

Squeezing grip while touching a line deletes it (blocked lines flash, as in 2.11). B undoes, X clears.

### 11.5 Joining lines

Lines drawn to a dot that other lines already share sometimes failed to join it, or did not form at all. Three changes:

- A stroke that starts on, ends on, or crosses a line between its dots joins that line there: the member is split in two and both lines share the new dot. A stroke that runs along a line, within a small angle, does not split it. The split dot belongs to the line it split, so that line stays blocked from deletion while another line uses the dot (2.11). Deleting the line that caused a split merges the member back once no other line uses that dot.
- A line drawn from or to a shared dot stays on that dot, even when it leaves at a narrow angle to a line already there. Only a stroke that runs almost exactly along an existing line counts as tracing over it, and the overlap is dropped.
- While the player draws, the dot or point on a line that the stroke would join is highlighted.

### 11.6 Matching sides

From the Group A drawing issues (W4-01 to W4-04). A line, arc, or polyline (a triangle, zigzag or W) shaped as in 11.1 and drawn beside a matching line at a drivable spacing snaps to an exact copy of that line shifted sideways, so the two sides of a bridge match. A line within a small angle of an existing rail becomes that rail shifted sideways, so the pair always gets a deck. An arc becomes a side-by-side arch or a concentric curve. A polyline must have the same number of corners, each close to the other side's, and is built one straight side at a time so its corners are shared dots. Smooth curves such as an S are not copied. While the trigger is held, a ghost shows what the stroke will build and highlights the line it copies.
