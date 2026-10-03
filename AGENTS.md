# RegisterAI: working instructions

RegisterAI is one NativeAOT executable for Windows x64 that registers a local MCP
server with coding agents by running each agent's own command. Keep it small: one
product project, one test project, one fake client.

## Rules a mechanism enforces

| Rule | Mechanism |
|---|---|
| Two-line SPDX header, `LicenseRef-RegisterAI-FSL-1.1-MIT-5yr`, on every source file | `HouseRuleTests.EverySourceFileCarriesTheTwoLineSpdxHeader` |
| Warnings are errors; no build file suppresses one | `Directory.Build.props`, `HouseRuleTests.WarningsAreErrorsAndNoBuildFileSuppressesOne` |
| Banned calls: process lookup by name, tree kill, recursive delete, the timed wait alone, a start that cannot hide its window, console reads | `build/BannedSymbols.txt` through BannedApiAnalyzers, every project |
| Only `Terminal` writes to stdout or stderr in the product | `src/RegisterAI/BannedSymbols.txt` bans `System.Console` there |
| Every process start sets `CreateNoWindow` and reads output as UTF-8 | `HouseRuleTests.EveryProcessLaunchHidesItsWindowAndReadsUtf8` |
| No skipped test | `HouseRuleTests.NoTestIsSkipped` |
| No en or em dash, ellipsis character, curly quote, no-break space, zero-width space or byte order mark in any text file | `HouseRuleTests.NoTextFileCarriesACharacterAPersonDoesNotType` |
| No user name, machine name, profile path, account SID, foreign e-mail address or drive path outside `C:\Apps\`, `C:\Tools\`, `C:\src\`, `D:\Other\`, `C:\Users\<you>\` and `X:\` in any tracked file, commit or published executable | `build/Find-MachineDetails.ps1`, run by `MachineDetailsTests` and by `build/hooks/pre-commit` |
| No test writes the person's own user PATH: the path verbs run against `ScratchUserPath` or a scratch key under `HKEY_CURRENT_USER\Software`, and the published executable only with `--dry-run` | `UserPathGuard`, a session hook that reads `HKEY_CURRENT_USER\Environment\Path` before the first test and after the last and fails the run when it moved |

## Rules a person keeps

- Write plainly: no announcing, no summary of what was just said, a number where a
  number exists.
- Tests never read or write a real client configuration. Every test that starts
  `claude` or `codex` sets `CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `USERPROFILE` and `HOME`
  to scratch folders first.
- A test that runs the published `path add` or `path remove` passes `--dry-run`, and
  gives `path add` a folder no PATH holds. The guard above catches a slip after the
  fact; this rule is what keeps it from happening.
- Publish before testing: `pwsh build/Publish.ps1`, then `dotnet test`.
- Scratch files go in `.work\` at the repository root, which is ignored.
