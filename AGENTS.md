# AGENTS.md

Implement only the GitHub issue assigned to you. Edit only that issue's Owned files. Call public APIs of earlier issues and leave their files unchanged.

`Constants` is `public static partial class Constants` in namespace `BridgeBuilder.Core`. Add constants only in the Constants file you own. Leave every HUMAN_OWNED constant unchanged. Set a GUESS or DERIVED value only when your issue owns that file and names the value and the status tag. Spec wording such as "about", "roughly", "~", and "assumed" becomes a named constant, not a literal in logic.

Core (`Assets/BridgeBuilder.Core`) must not reference `UnityEngine` or `System.Text.Json`. Keep `noEngineReferences` true. The committed project is `dotnet/BridgeBuilder.Core.csproj`, target `netstandard2.1`, `LangVersion` 9.0.

Do not generate or download binary assets. Unity visuals use built-in primitives only. Build bridge member and fill meshes at runtime from solver geometry. Sourced assets wait on HG-licenses. Unity wiring is HG-unity, not an agent issue.

Tests are deterministic. No `DateTime.Now`, no `Thread.Sleep`, no `System.Threading.Thread`, no `Task.Run`, and no unseeded `Random`. Pass a fixed `dt` or a fake clock. Check solver displacements, forces, utilizations, wheel loads, and speeds for NaN and Infinity.

When you tune GUESS values, record the final numbers in the PR description. For W2-07, also record the achieved fixture 1 and fixture 5 utilizations. Do not lower `Fixture1MinimumUtilization` or raise `Fixture5MaximumUtilization`. Do not edit `Constants.Human.cs`. Do not change fixture 2 or fixture 4 expected values without human approval.

Spec: `docs/spec.md`.

`dotnet build` must be clean. Run `dotnet test tests/BridgeBuilder.Core.Tests/BridgeBuilder.Core.Tests.csproj`.
