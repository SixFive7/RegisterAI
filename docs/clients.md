# The clients, as measured

What RegisterAI relies on in each client, with the version it was measured at. Every
fact here is held by a test in `RealClientTests`, which runs this machine's own clients,
copied into scratch, with `CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `USERPROFILE` and `HOME`
pointed into scratch. To re-establish all of it, publish and run the suite; to see one
fact, run its test.

Measured 2026-10-04 at Claude Code 2.1.288 and codex-cli 0.159.0-alpha.12.1, on
Windows 11 x64.

## Claude Code

| Fact | Test |
|---|---|
| `mcp add <name> --scope user --env K=V -- <command> <args>` exits 0 and stores the command, the arguments and the variable exactly as given | `TheRealClientsRegisterReadBackAndRemove(user)` |
| The same with `--scope project`, run in the project folder, writes `<project>\.mcp.json` | `TheRealClientsRegisterReadBackAndRemove(project)` |
| A duplicate add exits 1 with `already exists in user config` | `TheRealClientsSpeakTheDialectTheFakeModels` |
| Removing nothing exits 1 with `No MCP server named "<name>" in user scope`, and at project scope with `... in .mcp.json` | `TheRealClientsSpeakTheDialectTheFakeModels` |
| `${LOCALAPPDATA}` in a command is stored as written at both scopes | `AVariableIsStoredVerbatimAtBothScopes` |
| A path with a non-ASCII letter is stored and read back unchanged | `ANonAsciiPathSurvivesTheRealClients` |
| With `.config.json` in its configuration folder, user entries go there and not into `.claude.json` | `ClaudeCodeKeepsItsUserEntriesInConfigJsonWhenThatFileExists` |

RegisterAI does not read the wording. It confirms every write by reading the entry
back, so a client that changes its words changes only what `said` shows.

## Codex

| Fact | Test |
|---|---|
| `mcp add <name> --env K=V -- <command> <args>` exits 0, and an add over an existing name replaces the entry | `TheRealClientsSpeakTheDialectTheFakeModels` |
| Removing nothing exits 0 | `TheRealClientsSpeakTheDialectTheFakeModels` |
| `mcp list --json` prints an array of objects with `name` and a `transport` holding `type`, `command`, `args` and `env`; `env` is an object of names and values, or null | `TheRealClientsSpeakTheDialectTheFakeModels` |
| With `CODEX_HOME` set to `<project>\.codex`, add, list and remove act on that folder's `config.toml` | `TheRealClientsRegisterReadBackAndRemove(project)` |
| A run leaves empty `tmp\arg0` folders in its home; RegisterAI removes them from a project, and only while they are empty | `TheRealClientsRegisterReadBackAndRemove(project)` |
| `${LOCALAPPDATA}` in a command is stored as written | `AVariableIsStoredVerbatimAtBothScopes` |

`mcp list --json` prints environment values. RegisterAI keeps only the names.

That Codex does not expand a variable when it starts a server is not held by a test
here. It was measured on 2026-09-24 at codex-cli 0.155.0-alpha.9.2, in the project this
tool was extracted from: `${LOCALAPPDATA}`, `$LOCALAPPDATA`, `%LOCALAPPDATA%` and `~` in
a command started nothing in 48 attempts. RegisterAI's ownership rule follows it:
Codex commands are compared as written.
