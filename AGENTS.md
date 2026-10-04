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
| Every `CHANGELOG.md` entry opens with one icon from the legend and a bold headline of one sentence and at most 100 characters; the legend is a table of the twelve icons; the first heading is `[Unreleased]` and every other one `[x.y.z] - <date>`; every released section opens with a paragraph; groups are Keep a Changelog's, in order, once each | `ChangelogTests` |
| A release page is built from the version's `CHANGELOG.md` section, one line per entry linked to its lines in the tagged file, and never from a changelog that differs from the tag | `build/New-ReleaseNotes.ps1`, run by `build/New-Release.ps1`; `ChangelogTests` runs it against a changelog in scratch |

## Rules a person keeps

- Write plainly: no announcing, no summary of what was just said, a number where a
  number exists.
- RegisterAI stands on its own. No file, test or example names a program that calls
  it, and no new commit message does either; examples use the fictional `demo` server
  in `C:\Apps\Demo`.
- Changelog entries go under `[Unreleased]` as the work lands. The release commit moves
  them under `## [x.y.z] - <date>`, dated the day of that commit, and leaves
  `[Unreleased]` empty; `build/New-Release.ps1` refuses a release with entries left
  there.
- Tests never read or write a real client configuration. Every test that starts
  `claude` or `codex` sets `CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `USERPROFILE` and `HOME`
  to scratch folders first.
- A test that runs the published `path add` or `path remove` passes `--dry-run`, and
  gives `path add` a folder no PATH holds. The guard above catches a slip after the
  fact; this rule is what keeps it from happening.
- Publish before testing: `pwsh build/Publish.ps1`, then `dotnet test`.
- Release with `pwsh build/New-Release.ps1 -Version x.y.z` from a clean tree whose HEAD
  is pushed. It tags, publishes, runs the suite with `REGISTERAI_RELEASE_RUN=1`, scans the
  executable, writes `SHA256SUMS` and creates the GitHub release.
- Scratch files go in `.work\` at the repository root, which is ignored.
