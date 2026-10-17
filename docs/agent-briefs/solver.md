# Agent brief: solver track

Give this whole file to your coding agent as its instructions. It stands on its own.

You own the structural solver in the Unity-free Core of Bridge Builder: the frame element and static solve, banded storage with reverse Cuthill-McKee ordering, the Newmark integrator with progressive failure, and the lagged car-bridge coupling. Two other agents work the other tracks at the same time, and some of your issues wait for theirs.

## Your issues

Work in this order. W1-01 and W1-02 are independent, so they can run in parallel in separate worktrees.

| Order | Issue | ID | Title | Blocked by |
| --- | --- | --- | --- | --- |
| 1 | [#2](https://github.com/jsaluja09/Bridge/issues/2) | W1-01 | Frame element assembly and static solve, fixed supports only | W0-01 |
| 2 | [#3](https://github.com/jsaluja09/Bridge/issues/3) | W1-02 | Banded storage and reverse Cuthill-McKee | W0-01 |
| 3 | [#12](https://github.com/jsaluja09/Bridge/issues/12) | W2-01 | Newmark integrator and progressive member failure | W1-01, W1-02, W1-03 (capacity track) |
| 4 | [#19](https://github.com/jsaluja09/Bridge/issues/19) | W3-01 | Lagged car and solver coupling | W2-01, W2-02 (capacity track), W1-06 (model track, see notes) |

Gated, do not start yet: [#18](https://github.com/jsaluja09/Bridge/issues/18) W2-07 "Calibrate section size and yield on fixtures 1 and 5". It is blocked by W1-01, W1-03, W1-05, W1-06, W2-06, and the human gates HG-fixture-margins, HG-car-and-wind, and HG-material. Start it only after all three gate notes are written into the issue.

Other tracks wait on you. W2-06 (capacity track) waits on W1-01, and W2-07 waits on W1-01. Finish W1-01 first.

## Who owns what

- **solver (you):** W1-01, W1-02, W2-01, W3-01. Later W2-07.
- **model-and-input:** W1-06, W1-04, W2-03, W1-08, W1-09, W1-07, W2-05. Later W2-04.
- **capacity-sim-game:** W1-03, W1-05, W2-02, W1-10, W3-03, W2-06, W3-02. Later W3-04.

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
- **Worktree location.** Worktrees go in a sibling folder, `../Bridge-worktrees/<id>`, where `<id>` is the lowercase issue ID such as `w1-01`.

## Per-issue loop

Go through "Your issues" in order and take the first one that is ready. If none is ready, stop and report which PRs you are waiting for.

1. **Check readiness.** List the merged PRs:

   ```
   gh pr list --repo jsaluja09/Bridge --base bridge-core --state merged --limit 200 --json title --jq '.[].title'
   ```

   An issue is ready when every ID in its "Blocked by" line appears as a title prefix such as `[W1-03]`, and every human gate it lists (`HG-...`) has its note written into the issue body.

2. **Create the worktree.**

   ```
   git fetch origin
   git worktree add ../Bridge-worktrees/<id> -b issue/<n>-<id> origin/bridge-core
   ```

   For issue #2 that is `git worktree add ../Bridge-worktrees/w1-01 -b issue/2-w1-01 origin/bridge-core`. Run every later command from inside that worktree.

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

   The title is the uppercase ID in brackets followed by the exact issue title, for example `[W1-01] Frame element assembly and static solve, fixed supports only`. Other agents' readiness checks depend on that prefix. The body has these sections:
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
  - `Assets/BridgeBuilder.Core/Contracts/SolverInterfaces.cs` holds the interfaces and the recognizer types.
  - `Assets/BridgeBuilder.Core/Math/Vec3.cs` and `Mat3.cs` are the math types.
  - `Assets/BridgeBuilder.Core/Constants.Human.cs` holds the human-owned values.
  - `tests/BridgeBuilder.Core.Tests/FixtureLoader.cs` (namespace `BridgeBuilder.Core.Tests`) loads fixtures.
- **Contract types** are classes with public get/set properties and parameterless constructors. Array properties default to empty arrays. `SolverInput.ScaleFactor` defaults to 0, so always set it.
- **Result arrays.** `StaticSolveResult`, `StepOutput`, and `SimulationSnapshot` hold `Vec3[] Displacements` (translations) and `Vec3[] Rotations` (radians about global X, Y, and Z). Entry `i` belongs to `SolverInput.Nodes[i]`. Arrays follow node order, not node id, and ids need not start at 0 or be contiguous.
- **`MemberForce`** has `Axial` (tension positive), plus `ShearLocalZAtA/B` and `MomentLocalYAtA/B` from the issue text. It also has `ShearLocalYAtA/B` and `MomentLocalZAtA/B` for the other bending plane. `SectionProperties` has outer-fiber distances `Cy` and `Cz`.
- **`ILinearSolver.Add` accumulates**, and callers add every nonzero entry, so a symmetric matrix gets both (i, j) and (j, i). The interface has no clear or reset method.
- **`Vec3`** throws `ArgumentException` when any component is NaN, including operator results. `Normalized()` throws on a zero-length vector.
- **Car types** (`CarState`, `CarParameters`, `WheelLoad`, `DeckSample`) are in bridge-world units. `DeckSample.Slope` is positive when the deck descends along the direction of travel. `CarState` has `Heading` and `VerticalVelocity`, and `WheelLoad` has `Position`.
- **Fixtures** are JSON in `tests/BridgeBuilder.Core.Tests/Fixtures/`, in room units with Y up and camelCase fields:
  - `nodes`: `{ id, position: [x, y, z], fixed, surfaceId, groundAnchor }`
  - `members`: `{ id, nodeA, nodeB, sectionId, lineId, hiddenBrace }`
  - also `lines`, `fills`, and `sections`

  The loader ignores fields it doesn't know. Read your extra fields in the test through `FixtureLoader.PathFor(name)`, `ReadText(name)`, or `ReadDocument(name)`. Each starter stub has `"stub": true`. The issue that owns a fixture deletes that flag when it replaces the stub, which switches off the stub-only checks in `ScaffoldTests`.
- **New files are picked up automatically.** Core compiles `Assets/BridgeBuilder.Core/**/*.cs`, the test project compiles every `.cs` file in its folder, and `Fixtures/**/*.json` are copied to the output. Don't edit either csproj.
- **Constants pattern.** Each area adds a nested static class to the partial `Constants` class, in its own file. Your W2-01 file looks like this:

  ```csharp
  namespace BridgeBuilder.Core
  {
      public static partial class Constants
      {
          public static class Dynamics
          {
              /// <summary>Spec 2.8. DERIVED. Newmark average acceleration.</summary>
              public const double NewmarkBeta = 0.25;
          }
      }
  }
  ```

## Notes for your issues

### W1-01 frame element and static solve

- You own fixtures 02 and 03. Replace each stub with the issue's geometry, sections, loads, and expected values, and delete `"stub": true`. The fixture 02 `note` must be the issue's phrase "do not modify without human approval", and those expected numbers never change without human approval.
- With the issue's axis rule, a member along global X loaded in global Z bends about local y. The fixture 2 tests read `MomentLocalYAtA` and `ShearLocalZAtA`. Fill the other plane as well (`MomentLocalZAtA/B` and `ShearLocalYAtA/B`), because W1-03's bending check reads both planes.
- Map node ids to array indices. Don't assume ids are 0-based or contiguous.
- Dense factorization stays private inside `StaticSolver.cs`. W1-02 builds the banded solver separately, and you don't depend on it.

### W1-02 banded storage and RCM

- `BandedMatrix` implements `ILinearSolver`. It is constructed as `(order, halfBandwidth)`, throws when `|row - column|` exceeds the half-bandwidth, and never allocates an n by n array.
- Callers add both (i, j) and (j, i). Either store the full band, or, if you store one triangle, have `Add` ignore the other one. Never add both into the same slot.
- Follow the issue's RCM rule exactly: the start vertex, the tie-breaks, the reversal, and `permutation[newIndex] = oldIndex`. W2-01 uses this ordering.
- The dense reference solver lives only in the test file.

### W2-01 Newmark integrator and progressive failure

- Start only after W1-01, W1-02, and W1-03 are merged. Use `FrameElement.WorldStiffness`, the RCM permutation, `BandedMatrix` through `ILinearSolver`, and `MemberCapacity` without editing them.
- `ILinearSolver` has no clear method, so reassembling after a member fails needs a fresh solver. One clean option is to take a factory such as `Func<int, int, ILinearSolver>` (order, half-bandwidth), and have the test count `Factor` calls across instances with a counting wrapper.
- You own fixture 06. The stub has three members; the issue wants a redundant two-member frame in which one member, with a different section, reaches utilization `>= 1` first. Replace the stub and delete `"stub": true`.
- Keep the single-degree-of-freedom worked example exactly as the issue writes it.

### W3-01 lagged car and solver coupling

- The issue's "Blocked by" line lists W2-01 and W2-02, but `Coupling_CopyDoesNotMutateOriginal` steps `BridgeModel.Copy()`, which W1-06 (model track) provides. Wait for all three.
- Implement `IDynamicSolver` as `CoupledStep`. Take the integrator and `ICarModel` as arguments so tests can pass spies. Each step, the bridge solves with the previous step's wheel loads, and then the car reads the new deck.
- No constants file, no wind model, and no threads.

### W2-07 calibration (gated)

- You may change only the four `Constants.Section` values. You own fixtures 01 and 05 and delete their stub flags.
- Record the four tuned values and both achieved worst-member utilizations in the PR body.

## For the person running this agent

- Review and merge PRs into `bridge-core`. Squash or merge commit both work.
- After merging, close the issue with `gh issue close <n> --repo jsaluja09/Bridge --comment "Merged into bridge-core in #<pr>."`. GitHub won't close it, because `bridge-core` isn't the default branch.
- A gated issue unblocks when you write its human-gate note into the issue body.
- When the agent stops because it is waiting on another track, start it again with this brief once that PR merges.
