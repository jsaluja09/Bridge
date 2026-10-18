# BridgeBuilder MR: MVP Spec (working draft)

A mixed-reality physics game for Quest 2 (Unity, Meta XR SDK). The player marks two real surfaces, draws a bridge between them in mid-air, and the game tests it with a car and a condition such as wind. This is a local prototype that runs on one headset.

**Status: working draft.** This records the decisions reached so far in conversation, and it is meant to change. Any decision, rationale, or number here can be challenged. Anything marked "about", "roughly", or "~" is a starting guess, not a derived value. Edit it directly or comment on any line. Section 8 lists the numbers most in need of scrutiny. Section 11 records changes made after playtesting.

## 1. Goals

- Free-draw 3D input that the game converts into real structural members.
- Realistic component-level physics: members carry load, bend, buckle, and break.
- A middle schooler can draw almost any connected shape and see why it holds or fails.
- Bare spans fail. Supports, triangulation, and fills visibly matter.

**Non-goals for the MVP:** per-line thickness, plastic yielding, multiplayer, saving across sessions, anything beyond a single car per run. *(Changed: multiple materials are no longer a non-goal; see 11.6.)*

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
- Attaching a new line means starting it from an existing node. No member is ever split.
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
- **One fixed cross-section** for every member in the MVP. The data model carries a per-member section value from day one, set to the same constant, so per-line thickness is an easy first upgrade. *(Changed: each material now has its own section; see 11.6.)*
- **Failure modes:** tension (yield), compression, and **buckling** (Euler-style, so long slender members under compression fail early). The check is one closed-form capacity test per member, combined with bending.
- When a member fails it is removed, the structure redistributes load, and the solver continues. This gives progressive collapse.
- Plastic yielding and progressive damage are a later layer. The solver stays linear between failures, so this means adding state to members and not rewriting the core.

### 2.8 Dynamics and the car

- The bridge uses **real dynamics with an implicit integrator** (Newmark average-acceleration or similar) and light damping. This stays stable at any timestep, which avoids the wobble-and-explode behavior of rigid-body engines.
- The car is a small **suspension-based vehicle** with wheel contact points, not a rigid-body pile.
- **Lagged two-way coupling:** each step, the bridge solves with the car's last wheel loads, then the car reads the new deck height and slope at each wheel and updates its suspension and speed. The car dips a deck under a heavy truck, bounces on a flexing deck, and can ride a collapsing deck down.
- The player chooses which surface the car starts from. With surfaces at different heights, this decides whether the car climbs or descends. Wind direction also meets the car differently depending on travel direction.

### 2.9 Game rules

- **Material budget:** every line and fill costs by length or area. One material in the MVP. *(Changed: three materials, each with its own price; see 11.6.)* A hidden hard node cap protects performance and is enforced through the budget so the player never sees a separate rule.
- **Challenges are authored and shown before building**, like a design brief: for example, heavy truck, crosswind 15 m/s, budget 500. In the MVP each is a small config object (car weight, wind speed, grip, budget). Free play with player-chosen conditions comes later.
- **Scoring:** the run passes if the car reaches the far surface, even if some members broke. A star rating comes from members lost, peak utilization, and budget remaining. The first build can be pass or fail plus members lost.

### 2.10 Feedback

- **Utilization coloring** is the default. Each member is colored by how close it is to failing, taking the worst of its tension, compression, and buckling checks (green, yellow, red). *(Changed: the color is now tinted over the material texture; see 11.6.)*
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
- Members: id, two node ids, section value (a constant in the MVP). *(Changed: the section value now names the member's material, and fills carry one for their deck material; see 11.6.)*
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

1. **Two bare cantilevers must fail.** Two lines from the surfaces with nothing else must fail under the standard car. Tune values until this holds. It also catches any later change that makes bridges too strong. *(Changed: it must fail in every material; see 11.6.)*
2. **Simply supported beam:** deflection and member force match hand-calculated values.
3. **Unbraced rectangle:** under load it behaves as a flexible frame, not a mechanism, because joints are rigid. The same rectangle with a diagonal is measurably stiffer.
4. **Long vs short compression member:** the long one buckles first, at the expected load.
5. **Triangulated truss:** holds the standard car with a visible margin. *(Changed: when built in wood; see 11.6.)*
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
| Section size and material strength | 2.7, 11.6 | Starting guesses for each material (wood, steel cable, concrete): its own section, stiffness, weight, and separate tension and compression strengths | Calibrate until the two-cantilever fixture fails in every material and the triangulated truss holds when built in wood, by the human-set margins (section 5 and 11.6). |
| Budget values and star thresholds | 2.9, 11.6 | Line prices are set by the human: wood 10, steel cable 25, concrete 50 per room meter. Deck prices and challenge budgets are starting guesses. Star thresholds are not chosen yet. | Set deck prices, budgets, and star thresholds from measured bridge costs, keeping the balance rule in 11.6. |

Decisions can be challenged as well as numbers. If a choice in section 2 (for example fixed supports, one cross-section, or lagged two-way coupling) turns out to be wrong in the headset, change it and note why.

## 9. Later upgrades

- Per-line thickness. (Multiple materials are done; see 11.6.)
- Cables that go slack when pushed and take load again when pulled, instead of snapping.
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

Holding the trigger starts a line anywhere in space; releasing it ends the line. The stroke is smoothed, straightened when the recognizer reads it as a line, and divided into equal ~10 cm segments. Its start, end, and any point where it passes within the snap radius of an existing dot fuse with that dot. Lines that touch nothing are allowed; the dead-load preview shows them as unsupported and they fall when tested. No stroke is rejected for its shape.

### 11.2 Two-point setup with pads

Setup is two taps: one per end of the bridge. Each tap places a flat rectangular pad facing the other tap. The pad's whole top is a fixed support covered in snap dots, and the car starts on one pad and must reach the other. The four-tap edge math (W1-08) remains in Core.

### 11.3 Automatic decks

There is no fill or road tool. Two lines that are each flat (under the road slope limit), run roughly parallel, and sit side by side at a drivable spacing get a deck between them automatically. The deck is a road fill with hidden bracing, costs material by area, and is validated as in 2.4. *(Changed: deck material and price; see 11.6.)*

### 11.4 Delete

Squeezing grip while touching a line deletes it (blocked lines flash, as in 2.11). B undoes, X clears.

### 11.5 Shape resolver

Approved with the Group A drawing issues (W4-01 to W4-04); it replaces the last sentence of 11.1. Each stroke resolves to one of a finite set of shapes: a line, a circular arc, or a polyline of straight pieces (a triangle, zigzag or W). The clean shape is built, not the hand's path. A stroke that fits none of them (a scribble, a spiral, a stroke out of one plane, or one that crosses itself) is rejected: it flashes red and fades, with no message. A new line drawn nearly parallel to an existing rail at a drivable spacing snaps exactly parallel to it, so the pair always gets a deck.

### 11.6 Materials

Decided with the project owner on 2026-10-18. This replaces the one fixed cross-section of 2.7 and the one material of 2.9. There are three materials: wood, steel cable, and concrete. The steel tube used until now is removed from the game and from Core.

Each material has its own cross-section, stiffness, and weight in the solver, and separate strengths in tension and in compression. The member check in 2.7 still covers tension, compression, buckling, and bending, but it compares pull against the tension strength and push against the compression strength.

- **Wood** is light, moderately strong both pulled and pushed, and bends easily.
- **Steel cable** is very strong when pulled and has almost no bending strength. A cable that is pushed fails: it snaps and is removed like any other broken member. Cables do not go slack and tighten again; that is a possible later upgrade (section 9).
- **Concrete** is very strong when squeezed, weak when pulled, and very heavy.

The section values are starting guesses. They live in the constants files and are tuned against the calibration fixtures below.

**Prices.** A line costs a fixed price per room meter of its material. The project owner chose the prices: wood 10, steel cable 25, concrete 50. Deleting a line refunds its own price.

**Decks.** An automatic deck (11.3) takes the material of the two lines it sits between. A deck between two cable lines is wood planks, like a rope bridge. A deck between two different materials uses the more expensive of the two. The deck's hidden bracing is built in the deck's material. Decks cost a price per room square meter of their material; the starting guesses are wood 40 and concrete 200, and the values live in the constants files. The game now charges automatic decks against the budget.

**Budgets and balance.** Challenge budgets are rescaled to the new prices. The starting point is ten times the old budgets, which were counted in room meters of line; the final values live with the challenge definitions in Core. Tests lock in a balance rule: a sensible mixed bridge (a wood frame with cable diagonals) carries the car and fits every default challenge budget, and the same bridge built entirely in concrete is over every one of them.

**Calibration.** Fixtures 1 and 5 (section 5) keep their human-set margins but get new targets. Fixture 1, the bare cantilevers, must fail in each material, with a worst-member utilization of at least the human minimum (`Fixture1MinimumUtilization`). Fixture 5, the truss, must hold when built in wood, staying below the human maximum (`Fixture5MaximumUtilization`).

**Picking a material.** While building, a flick of the right thumbstick cycles to the next material; over Link the arrow keys do the same. Each line is built in the material selected when it is drawn. The HUD shows the current material, its price, and the spending so far, including decks.

**Looks.** Each material has its own member shape, built at runtime from the solver geometry like every member (section 1): a square wood beam, a thin round cable, and a thick square concrete beam, each with a texture. The load color from 2.10 (green, yellow, red) is tinted over the texture, more strongly as the load rises, and the stripe cue stays. If the textures are missing, members fall back to flat colors per material.

**Textures and licensing.** The textures are three CC0 color maps from ambientCG (Wood 049, Rope 002, and Concrete 034, at 1K). They sit in `Assets/BridgeBuilder/Game/Resources/MaterialTextures/`, with a `LICENSES.md` there that records each source and license. The project owner approved downloading them as an exception to the agent rule against downloading binary assets. They meet the asset constraint in section 1: each comes from a free library, with its license checked and recorded.
