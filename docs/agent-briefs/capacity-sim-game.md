# Agent brief: capacity-sim-game track

Give this whole file to your coding agent as its instructions. It stands on its own.

You own member capacity, scale, the car, feedback, previews, and game rules in the Unity-free Core of Bridge Builder: the member capacity and utilization checks, the room-to-bridge scale adapter, the suspension car model, the feedback mapping functions, the challenge config, the dead-load preview, and the snapshot mailbox with its preview scheduler. Two other agents work the other tracks at the same time, and some of your issues wait for theirs.

## Your issues

Work in this order. Issues 1 to 5 depend only on W0-01, so they are ready from the start and can run in parallel, each in its own worktree.

| Order | Issue | ID | Title | Blocked by |
| --- | --- | --- | --- | --- |
| 1 | [#4](https://github.com/jsaluja09/Bridge/issues/4) | W1-03 | Tension, compression, buckling, bending, utilization, and signed force | W0-01 |
| 2 | [#6](https://github.com/jsaluja09/Bridge/issues/6) | W1-05 | Room-to-bridge scale adapter | W0-01 |
| 3 | [#13](https://github.com/jsaluja09/Bridge/issues/13) | W2-02 | Suspension car model | W0-01 |
| 4 | [#11](https://github.com/jsaluja09/Bridge/issues/11) | W1-10 | Feedback mapping functions | W0-01 |
| 5 | [#21](https://github.com/jsaluja09/Bridge/issues/21) | W3-03 | Challenge config and start-surface choice | W0-01 |
| 6 | [#17](https://github.com/jsaluja09/Bridge/issues/17) | W2-06 | Dead-load preview | W1-01 (solver track), W1-03, W1-05 |
| 7 | [#20](https://github.com/jsaluja09/Bridge/issues/20) | W3-02 | Snapshot mailbox and preview scheduler | W0-01, W2-06 |

Gated, do not start yet: [#22](https://github.com/jsaluja09/Bridge/issues/22) W3-04 "Scoring metrics". It is blocked by W3-03 and the human gates HG-reach and HG-stars. Start it only after both gate notes are written into the issue.

Other tracks wait on you:
- W2-01 (solver track) waits on W1-03.
- W3-01 (solver track) waits on W2-02.
- W2-07 waits on W1-03, W1-05, and W2-06.

Finish W1-03 first, then W1-05 and W2-02.

## Who owns what

- **solver:** W1-01, W1-02, W2-01, W3-01. Later W2-07.
- **model-and-input:** W1-06, W1-04, W2-03, W1-08, W1-09, W1-07, W2-05. Later W2-04.
- **capacity-sim-game (you):** W1-03, W1-05, W2-02, W1-10, W3-03, W2-06, W3-02. Later W3-04.

W0-01 ([#1](https://github.com/jsaluja09/Bridge/issues/1)) is the shared scaffold and is already done.

## Ground rules

- **Branch.** `bridge-core` is the team's integration branch. Branch from `origin/bridge-core` and open every PR against it. Never commit to, branch from, or open a PR against `main`, which carries unrelated work.
- **One issue per worktree.** Every issue gets its own git worktree and branch. Never work on two issues in one worktree, and never edit the main checkout.
- **Source of truth.** The GitHub issue body comes first (`gh issue view <n> --repo jsaluja09/Bridge`), then `AGENTS.md` and `docs/spec.md` on `bridge-core`. This brief adds only sequencing and context.
- **Owned files only.** Edit only the files listed under "Owned files" in the issue. Call earlier issues' public APIs and never edit their files. If a file you don't own has to change, don't edit it; describe the change in the PR body.
- **Constants.** Add constants only to the Constants file your issue owns, each with a spec citation and one status tag: `GUESS`, `DERIVED`, or `HUMAN_OWNED`. Never change a `HUMAN_OWNED` value, and never edit `Constants.Human.cs`.
- **Core stays Unity-free.** No file under `Assets/BridgeBuilder.Core` may contain the text `UnityEngine` or `System.Text.Json`. A W0-01 test enforces this.
- **No Unity or binary files.** Don't create `.meta` files, scenes, prefabs, models, or any other binary asset. Unity generates `.meta` files when a person opens the project.
- **Deterministic tests.** No `DateTime.Now`, `Thread.Sleep`, `System.Threading.Thread`, `Task.Run`, or unseeded `Random`. Pass a fixed `dt` or a fake clock. Check displacements, forces, utilizations, wheel loads, and speeds for NaN and Infinity.
- **GitHub.** Use `gh` read-only, except `gh pr create`. Don't comment on, edit, label, or close issues, and never merge a PR. People do those.

## One-time setup

- **Tools.** You need git, the GitHub CLI, and the .NET SDK 8 or newer with the .NET 8 runtime (the test project targets `net8.0`). `gh auth status` must show access to `jsaluja09/Bridge`.
- **Clone** if needed with `gh repo clone jsaluja09/Bridge`, and work from the clone root.
- **Read** `AGENTS.md` and `docs/spec.md` from `origin/bridge-core`, for example in your first worktree.
- **Worktree location.** Worktrees go in a sibling folder, `../Bridge-worktrees/<id>`, where `<id>` is the lowercase issue ID such as `w1-03`.

## Per-issue loop

Go through "Your issues" in order and take the first one that is ready. If none is ready, stop and report which PRs you are waiting for.

1. **Check readiness.** List the merged PRs:

   ```
   gh pr list --repo jsaluja09/Bridge --base bridge-core --state merged --limit 200 --json title --jq '.[].title'
   ```

   An issue is ready when every ID in its "Blocked by" line appears as a title prefix such as `[W1-01]`, and every human gate it lists (`HG-...`) has its note written into the issue body.

2. **Create the worktree.**

   ```
   git fetch origin
   git worktree add ../Bridge-worktrees/<id> -b issue/<n>-<id> origin/bridge-core
   ```

   For issue #4 that is `git worktree add ../Bridge-worktrees/w1-03 -b issue/4-w1-03 origin/bridge-core`. Run every later command from inside that worktree.

3. **Read the issue in full** with `gh issue view <n> --repo jsaluja09/Bridge`. Implement every acceptance test under its exact name.

4. **Verify.**

   ```
   dotnet build dotnet/BridgeBuilder.sln
   dotnet test tests/BridgeBuilder.Core.Tests/BridgeBuilder.Core.Tests.csproj
   ```

   The build must report 0 warnings and 0 errors, and every test must pass, both yours and the existing ones. `git status` must show only your owned files.

5. **Commit.** Stage files by explicit path, never with `git add -A` or `git add .`. The message is one plain sentence, a blank line, then `Closes #<n>`.

6. **Catch up with `bridge-core`.** Run `git fetch origin` and `git rebase origin/bridge-core`, then repeat step 4.

7. **Push and open the PR.**

   ```
   git push -u origin issue/<n>-<id>
   gh pr create --repo jsaluja09/Bridge --base bridge-core --head issue/<n>-<id> --title "[<ID>] <issue title>" --body-file <file>
   ```

   The title is the uppercase ID in brackets followed by the exact issue title, for example `[W1-03] Tension, compression, buckling, bending, utilization, and signed force`. Other agents' readiness checks depend on that prefix. The body has these sections:
   - **Summary:** what you built.
   - **Issue:** `Closes #<n>`. GitHub won't auto-close it, because `bridge-core` isn't the default branch.
   - **Tests:** the `dotnet test` pass count and the acceptance test names.
   - **Constants:** every `GUESS` or `DERIVED` value you set, with its final number.
   - **Deviations:** anything that differs from the issue text and why, or "None".
   - **Needs a change outside this issue:** files you don't own that should change and why, or "None".

8. **Stop and report** the PR link to your person. Don't merge it. Then return to step 1 for the next ready issue.

9. **After the PR merges,** run `git worktree remove ../Bridge-worktrees/<id>` and `git branch -D issue/<n>-<id>`.

## What W0-01 provides

Everything is in namespace `BridgeBuilder.Core`. Read the files for detail; these are the conventions that are easy to get wrong.

- **Files you call but never edit:**
  - `Assets/BridgeBuilder.Core/Contracts/SolverContracts.cs` holds the data types.
  - `Assets/BridgeBuilder.Core/Contracts/SolverInterfaces.cs` holds the interfaces.
  - `Assets/BridgeBuilder.Core/Math/Vec3.cs` and `Mat3.cs` are the math types.
  - `Assets/BridgeBuilder.Core/Constants.Human.cs` holds the human-owned values.
  - `tests/BridgeBuilder.Core.Tests/FixtureLoader.cs` (namespace `BridgeBuilder.Core.Tests`) loads fixtures.
- **Contract types** are classes with public get/set properties and parameterless constructors. Array properties default to empty arrays. `SolverInput.ScaleFactor` defaults to 0, so always set it.
- **Result arrays.** `StaticSolveResult`, `StepOutput`, and `SimulationSnapshot` hold `Vec3[] Displacements` (translations) and `Vec3[] Rotations` (radians about global X, Y, and Z). Entry `i` belongs to `SolverInput.Nodes[i]`. Arrays follow node order, not node id. `SimulationSnapshot` also has `StepId`, plus `MemberIds` and `Utilizations` aligned with each other.
- **`MemberForce`** has `Axial` (tension positive), plus `ShearLocalZAtA/B` and `MomentLocalYAtA/B` from the issue text. It also has `ShearLocalYAtA/B` and `MomentLocalZAtA/B` for the other bending plane, and `Utilization`, which the static solver leaves at 0. `SectionProperties` has `E`, `G`, `A`, `Iy`, `Iz`, `J`, `YieldStrength`, `Density`, and outer-fiber distances `Cy` and `Cz`.
- **Car types** are in bridge-world units:
  - `CarParameters` has `MassKg`, `WheelCount`, `Wheelbase`, `Track`, `SpringStiffness`, `DamperCoefficient`, and `RestLength`.
  - `CarState` has `Position`, `Heading`, `Speed`, `VerticalVelocity`, `WheelLoads`, and `SuspensionCompression`.
  - `WheelLoad` has `WheelIndex`, `NormalLoad`, and `Position`.
  - `DeckSample` has `WheelIndex`, `Height`, `Slope`, and `Position`. `Slope` is positive when the deck descends along the direction of travel.
- **`RunConfiguration`** has `StartSurfaceId`, `Car`, `WindSpeed`, `WindDirection`, `Grip`, `MaterialBudget`, `double? GroundHeightOverride` (an absolute height), and `ScaleFactor`.
- **`Vec3`** throws `ArgumentException` when any component is NaN, including operator results. `Normalized()` throws on a zero-length vector.
- **Fixtures** are JSON in `tests/BridgeBuilder.Core.Tests/Fixtures/`, in room units with Y up and camelCase fields:
  - `nodes`: `{ id, position: [x, y, z], fixed, surfaceId, groundAnchor }`
  - `members`: `{ id, nodeA, nodeB, sectionId, lineId, hiddenBrace }`
  - also `lines`, `fills`, and `sections`

  The loader ignores fields it doesn't know. Read your extra fields in the test through `FixtureLoader.PathFor(name)`, `ReadText(name)`, or `ReadDocument(name)`. Each starter stub has `"stub": true`. The issue that owns a fixture deletes that flag when it replaces the stub, which switches off the stub-only checks in `ScaffoldTests`.
- **New files are picked up automatically.** Core compiles `Assets/BridgeBuilder.Core/**/*.cs`, the test project compiles every `.cs` file in its folder, and `Fixtures/**/*.json` are copied to the output. Don't edit either csproj.
- **Constants pattern.** Each area adds a nested static class to the partial `Constants` class, in its own file. Your W1-05 file looks like this:

  ```csharp
  namespace BridgeBuilder.Core
  {
      public static partial class Constants
      {
          public static class Scale
          {
              /// <summary>Spec 2.6 and section 8. GUESS. Room-to-bridge scale factor.</summary>
              public const double RoomToBridge = 20.0;
          }
      }
  }
  ```

## Notes for your issues

### W1-03 member capacity

- You own fixtures 04 and 07. Replace each stub with the issue's columns and pier, and delete `"stub": true`. Fixture 04's numbers never change without human approval.
- `EulerCriticalLoad(E, I, K, L)` takes its arguments in that order, which is how the tests call it. The overload without `K` reads `Constants.Capacity.EffectiveLengthFactor = 0.5`, tagged `HUMAN_OWNED`.
- Utilization is the maximum of the separate checks, never their sum. The bending term `max(|My| / (Fy * Iy / cy), |Mz| / (Fy * Iz / cz))` pairs `MomentLocalY*` with `Iy` and `Cy`, and `MomentLocalZ*` with `Iz` and `Cz`.
- W2-01 and W2-06 call your function on solver output. Add an overload that takes a `MemberForce`, its `SectionProperties`, and the member length, and uses the worse of end A and end B for each moment.

### W1-05 scale adapter

- Multiply coordinates on the way in, and divide translations on the way back. Rotations are angles and pass through unchanged, as do section properties, forces, and car parameters.
- No `20` literal may appear in the adapter. The parameterless method reads `Constants.Scale.RoomToBridge`.

### W2-02 suspension car

- Implement `ICarModel` without editing the interface. `Step` has no gravity or grip argument, but the tests pass gravity `9.80665` and grip `0.1`, so take both through the constructor.
- This issue has no constants file. Gravity belongs to W2-06's `Constants.Loads`, and the standard car is still waiting on HG-car-and-wind.
- Keep all state in the returned `CarState` so `Step_IsDeterministic` holds. A positive `Slope` means downhill, which increases speed.

### W1-10 feedback mapping

- These are pure functions with explicit thresholds. The defaults read `Constants.Feedback`.
- NaN throws `ArgumentOutOfRangeException` from every function, including joint color and fill color.

### W3-03 challenge config

- The config holds car weight, wind speed, grip, and budget, and builds a `RunConfiguration`. Copy the ground override onto `RunConfiguration.GroundHeightOverride` and leave it null when there is none.
- Wind speed is stored, not converted to a force, and the public surface has no wind-force vector. No public method may be named `FreePlay` or `SetPlayerConditions`.

### W2-06 dead-load preview

- Start only after W1-01 (solver track), W1-03, and W1-05 are merged.
- Take an `IStaticSolver` as an argument; the tests pass a spy. Poll `Func<bool> shouldCancel` before calling the solver, and return `Cancelled` without calling it when the flag is true.
- Self-weight is `length_bridge * area * density * gravity`, split equally between the two nodes and pointing in `-Y`. Density comes from the `SectionProperties` argument, never from `Constants.Human`.
- You create `Constants.Loads.Gravity = 9.80665`, tagged `DERIVED`. Map utilizations with W1-03's function.

### W3-02 snapshot mailbox and preview scheduler

- Start only after W2-06 is merged.
- `SnapshotMailbox` implements `ISnapshotStore`. Define the `IClock` interface (a `double Now` that the test sets) in your owned files.
- The scheduler takes the debounce and solver period as arguments. The default debounce reads `Constants.Preview.DebounceSeconds`. W3-02 isn't blocked by W2-01, so don't reference `Constants.Dynamics`.

### W3-04 scoring (gated)

- Wait for W3-03 and the HG-reach and HG-stars notes. Stars take a caller-supplied `StarPolicy`; add no star constants.
- The default reach tolerance comes from `Constants.Human.ReachTolerance`. Read it, never write it.

## For the person running this agent

- Review and merge PRs into `bridge-core`. Squash or merge commit both work.
- After merging, close the issue with `gh issue close <n> --repo jsaluja09/Bridge --comment "Merged into bridge-core in #<pr>."`. GitHub won't close it, because `bridge-core` isn't the default branch.
- A gated issue unblocks when you write its human-gate note into the issue body.
- When the agent stops because it is waiting on another track, start it again with this brief once that PR merges.
