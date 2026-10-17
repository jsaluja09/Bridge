# Agent brief: model-and-input track

Give this whole file to your coding agent as its instructions. It stands on its own.

You own the bridge model and the player-input geometry in the Unity-free Core of Bridge Builder: the bridge model with budget, undo, and delete; equal-segment snapping; fill planarity; surface setup; the ground-plane grid; the 3D stroke recognizer; and road-chain validation. Two other agents work the other tracks at the same time.

## Your issues

Every issue below except the gated one depends only on W0-01, so all of them are ready from the start. They are independent of each other and can run in parallel, each in its own worktree. If you work them one at a time, use this order.

| Order | Issue | ID | Title | Blocked by |
| --- | --- | --- | --- | --- |
| 1 | [#7](https://github.com/jsaluja09/Bridge/issues/7) | W1-06 | Bridge model, budget, node cap, undo, and delete | W0-01 |
| 2 | [#5](https://github.com/jsaluja09/Bridge/issues/5) | W1-04 | Equal-segment snapping | W0-01 |
| 3 | [#14](https://github.com/jsaluja09/Bridge/issues/14) | W2-03 | Fill planarity check | W0-01 |
| 4 | [#9](https://github.com/jsaluja09/Bridge/issues/9) | W1-08 | Surface setup math | W0-01 |
| 5 | [#10](https://github.com/jsaluja09/Bridge/issues/10) | W1-09 | Ground-plane snap grid | W0-01 |
| 6 | [#8](https://github.com/jsaluja09/Bridge/issues/8) | W1-07 | Stroke recognizer in free 3D | W0-01 |
| 7 | [#16](https://github.com/jsaluja09/Bridge/issues/16) | W2-05 | Road-chain validation | W0-01 |

Gated, do not start yet: [#15](https://github.com/jsaluja09/Bridge/issues/15) W2-04 "Hidden bracing generation". It is blocked by W1-06, W2-03, and the human gate HG-bracing. Start it only after a person writes the bracing pattern (the node pairs for a quad and for an n-gon) into the issue's "Appended pattern" note.

Other tracks wait on you. W3-01 (solver track) and W2-07 both wait on W1-06, so finish W1-06 first.

## Who owns what

- **solver:** W1-01, W1-02, W2-01, W3-01. Later W2-07.
- **model-and-input (you):** W1-06, W1-04, W2-03, W1-08, W1-09, W1-07, W2-05. Later W2-04.
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
- **Deterministic tests.** No `DateTime.Now`, `Thread.Sleep`, `System.Threading.Thread`, `Task.Run`, or unseeded `Random`. Check every computed number you return for NaN and Infinity.
- **GitHub.** Use `gh` read-only, except `gh pr create`. Don't comment on, edit, label, or close issues, and never merge a PR. People do those.

## One-time setup

- **Tools.** You need git, the GitHub CLI, and the .NET SDK 8 or newer with the .NET 8 runtime (the test project targets `net8.0`). `gh auth status` must show access to `jsaluja09/Bridge`.
- **Clone** if needed with `gh repo clone jsaluja09/Bridge`, and work from the clone root.
- **Read** `AGENTS.md` and `docs/spec.md` from `origin/bridge-core`, for example in your first worktree.
- **Worktree location.** Worktrees go in a sibling folder, `../Bridge-worktrees/<id>`, where `<id>` is the lowercase issue ID such as `w1-06`.

## Per-issue loop

Go through "Your issues" in order and take the first one that is ready. If none is ready, stop and report which PRs you are waiting for.

1. **Check readiness.** List the merged PRs:

   ```
   gh pr list --repo jsaluja09/Bridge --base bridge-core --state merged --limit 200 --json title --jq '.[].title'
   ```

   An issue is ready when every ID in its "Blocked by" line appears as a title prefix such as `[W1-06]`, and every human gate it lists (`HG-...`) has its note written into the issue body.

2. **Create the worktree.**

   ```
   git fetch origin
   git worktree add ../Bridge-worktrees/<id> -b issue/<n>-<id> origin/bridge-core
   ```

   For issue #7 that is `git worktree add ../Bridge-worktrees/w1-06 -b issue/7-w1-06 origin/bridge-core`. Run every later command from inside that worktree.

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

   The title is the uppercase ID in brackets followed by the exact issue title, for example `[W1-06] Bridge model, budget, node cap, undo, and delete`. Other agents' readiness checks depend on that prefix. The body has these sections:
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
- **Contract types** are classes with public get/set properties and parameterless constructors. Array properties default to empty arrays.
  - `Node` has `Id`, `Position` (room units), `Fixed`, `int? SurfaceId`, and `GroundAnchor`.
  - `Member` has `Id`, `NodeA`, `NodeB`, `SectionId`, `LineId`, and `HiddenBrace`.
  - `Fill` has `Id`, `int[] NodeIds` (a closed loop with the first id not repeated), `Road`, and `Valid`. `Valid` defaults to false.
  - `Line` has `Id` and `int[] MemberIds`.
- **`Vec3`** is a readonly struct of `double` with `Length`, `Dot`, `Cross` (right-handed), `Distance`, `Normalized()`, and the usual operators. It throws `ArgumentException` when any component is NaN, including operator results, and `Normalized()` throws on a zero-length vector. `Mat3` is a 3 by 3 `double` matrix.
- **Recognizer types** live in `SolverInterfaces.cs`:
  - `RecognitionResult.Accepted(kind, vertices)` and `RecognitionResult.Reject(reason)` build results. `RejectionReason.None` means the stroke was accepted.
  - `Vertices` is the fitted path in stroke order. A line has its start and end. A polyline has its start, each corner, and its end. An arc has its start, the point at mid-sweep, and its end.
  - `RecognizerTolerances` is a struct with a seven-argument constructor in the issue's field order.
- **New files are picked up automatically.** Core compiles `Assets/BridgeBuilder.Core/**/*.cs` and the test project compiles every `.cs` file in its folder. Don't edit either csproj.
- **Constants pattern.** Each area adds a nested static class to the partial `Constants` class, in its own file. Your W1-04 file looks like this:

  ```csharp
  namespace BridgeBuilder.Core
  {
      public static partial class Constants
      {
          public static class Snapping
          {
              /// <summary>Spec 2.3 and section 8. GUESS. Target snap spacing, m.</summary>
              public const double TargetSpacingMeters = 0.10;
          }
      }
  }
  ```

## Notes for your issues

### W1-06 bridge model

- Two later issues call this model, so keep these names exactly as the issue gives them:
  - W3-01 (solver track) steps `BridgeModel.Copy()`. Its tests check that the original model's member list and node positions don't change.
  - W2-04 calls `TryAddHiddenBrace(nodeA, nodeB, sectionId)`.
- Expose read-only views of the nodes, members, fills, and lines, plus spent budget and undo depth, so other issues' tests can inspect the model. W3-01 also has to turn a model into a `SolverInput`. A helper that builds one from the model is welcome, but it isn't required.
- You create `Constants.Budget.cs`. The issue tags its three values `HUMAN_OWNED`, so write them with that tag. Read the node cap from `Constants.Human.NodeCap`.
- No public method name may contain `Split`, `InsertNode`, or `Subdivide`.

### W1-04 equal-segment snapping

- Count segments with `Math.Round((decimal)length / (decimal)spacing, MidpointRounding.AwayFromZero)`. The double ratio `0.15 / 0.10` is `1.4999999999999998` and rounds the wrong way.
- No `0.10` literal may appear in the algorithm. The default overload reads `Constants.Snapping.TargetSpacingMeters`.
- `EqualSegments_DoesNotSplitExistingMember` needs existing nodes and members. Use the W0-01 `Node` and `Member` types and don't depend on W1-06, which may not be merged yet.

### W2-03 fill planarity

- The tolerance check is inclusive: a vertex exactly at the tolerance passes. The default overload reads `Constants.Fill.PlanarityToleranceMeters`.
- W2-04 later reads that constant and calls this check, so keep the public API simple.

### W1-08 surface setup

- The issue wants the W1-04 rounding rule implemented inside `SurfaceSetup.cs`. Don't call W1-04's code, and don't edit `Constants.Snapping.cs`; spacing is a parameter.
- Every snap dot has `Fixed` true and `GroundAnchor` false. The non-parallel case only sets a flag; dots stay on both edges.

### W1-09 ground-plane grid

- Compute each coordinate as `min + i * spacing` instead of adding up a running total, so floating-point error doesn't drop the last row.
- Every returned node has `Fixed` true and `GroundAnchor` true. Order points by X, then Z. The height override is absolute and replaces the floor height.

### W1-07 stroke recognizer

- Follow the issue's decision order exactly. Use the W0-01 `RecognizerTolerances` and `RecognitionResult`, and don't declare a second tolerances type.
- The plane fit is private inside `StrokeRecognizer.cs`. The default wrapper reads `Constants.Recognition`, while tests pass explicit tolerances.
- The noise test uses one `new System.Random(12345)` and walks the samples in order, adding noise to X, then Y, then Z.

### W2-05 road-chain validation

- Use `Fill.NodeIds`, `Fill.Road`, and `Node.SurfaceId`. Two road fills are adjacent when they share two node ids.
- Y is up. Slope is the angle between the fill plane and the horizontal. The default overload reads `Constants.Road.MaxSlopeDegrees`.

### W2-04 hidden bracing (gated)

- Wait for W1-06, W2-03, and the HG-bracing note. Copy the pattern id into `Constants.Bracing.PatternId` as `HUMAN_OWNED`, and never change the pattern afterwards.
- Add braces through `BridgeModel.TryAddHiddenBrace`. Don't edit `BridgeModel.cs` or `Constants.Fill.cs`.

## For the person running this agent

- Review and merge PRs into `bridge-core`. Squash or merge commit both work.
- After merging, close the issue with `gh issue close <n> --repo jsaluja09/Bridge --comment "Merged into bridge-core in #<pr>."`. GitHub won't close it, because `bridge-core` isn't the default branch.
- A gated issue unblocks when you write its human-gate note into the issue body.
- When the agent stops because it is waiting on another track, start it again with this brief once that PR merges.
