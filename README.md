# RegisterAI

RegisterAI registers a local MCP server with coding agents by running each agent's
own command. It is one self-contained executable for Windows x64, with no support
files, no prompts and no state of its own, built to be called by installers and by
agents.

It supports Claude Code and Codex, each at user scope (every project of this Windows
user) and project scope (one folder).

## What it does and does not do

- It writes only through the client's own `mcp add` and `mcp remove`. It never edits a
  client's configuration file.
- It reads before it writes, and reads again after: a change counts only when the
  entry reads back as asked.
- It changes only an entry that is the caller's own: one that names exactly the
  caller's command, or whose command resolves to a file under a folder the caller
  names with `--owned-root`. Every other entry is reported as `foreign` and left alone.
- A configuration it cannot read is reported as `unreadable`, never as empty.
- It never prompts, never reads stdin, and makes no network call.
- It changes the user PATH only when asked to, with `path add` and `path remove`, and
  never as a side effect of another verb.

## Install

Download `RegisterAI.exe` and `SHA256SUMS` from a release, and check the file before
running it:

```powershell
(Get-FileHash RegisterAI.exe -Algorithm SHA256).Hash.ToLowerInvariant() -eq (Get-Content SHA256SUMS).Split(' ')[0]
```

There is nothing else to install. Put the file wherever the program that calls it
expects it.

## Quick start

```powershell
registerai status --name demo
registerai register --name demo --client all --scope user --owned-root 'C:\Apps\Demo' -- 'C:\Apps\Demo\demo-mcp.exe'
registerai unregister --name demo --client all --scope user --owned-root 'C:\Apps\Demo'
```

Everything after `--` reaches the client unchanged, so your shell must not change it
first. In PowerShell and bash, use single quotes. cmd.exe expands `%NAME%` even inside
double quotes. Add `--dry-run` to see what arrived and what would happen, with nothing
written.

`registerai help` prints the whole contract; `registerai describe` prints it as JSON,
including the schema of every document.

## Clients and scopes

| Client | Scope | Written with | Read from |
|---|---|---|---|
| Claude Code | user | `claude mcp add <name> --scope user` | `%CLAUDE_CONFIG_DIR%\.claude.json`, else `%USERPROFILE%\.claude.json` |
| Claude Code | project | the same with `--scope project`, run in the folder | `<project>\.mcp.json` |
| Codex | user | `codex mcp add <name>` | `codex mcp list --json` |
| Codex | project | the same with `CODEX_HOME` set to `<project>\.codex` | `codex mcp list --json`, same variable |

Claude Code expands `${NAME}` and `${NAME:-default}` in a command; Codex expands
nothing. A bare file name is looked up on the PATH a newly started program gets, and
reported in `resolvesTo`. Name the folder it lives in with `--path-folder`, and the
`path-missing` advice says when that folder is not on the PATH and carries the
`registerai path add` line for it.

## The user PATH

```powershell
registerai path add 'C:\Apps\Demo'
registerai path remove 'C:\Apps\Demo'
```

Both change `HKEY_CURRENT_USER\Environment\Path` and nothing else, and both take
`--dry-run`.

- `path add` takes only a full path to a folder that exists, so it never adds an entry
  that names nothing. It appends the folder after the entries already there and writes
  nothing when the folder is there already.
- `path remove` takes off every entry naming exactly that folder, compared without case
  and without a trailing separator. An entry spelled another way, such as with a
  variable, is left alone.
- The value keeps its kind (`REG_EXPAND_SZ` or `REG_SZ`); a value that did not exist is
  created as `REG_EXPAND_SZ`. An add and the remove after it give back the value as it
  was, byte for byte.
- Each write is read back before it counts, and then announced to running programs with
  `WM_SETTINGCHANGE`. A program that does not act on that announcement keeps the PATH it
  started with, and so does everything it starts.

`status` lists the entries of the user PATH that name a folder that does not exist, each
with the `registerai path remove` line that takes it off. It removes none of them. An
entry that is not a full path once expanded, and a network path, are not judged.

## Output and exit codes

`status`, `register`, `unregister`, `path add`, `path remove`, `describe` and every
usage error write exactly one JSON document to stdout, UTF-8 with LF line ends, carrying
`"schema": 1`. Diagnostics go to stderr. `--env` values are never echoed.

| Code | Meaning |
|---|---|
| 0 | Done, or nothing needed doing. |
| 1 | A client command failed or timed out, or the result could not be confirmed. |
| 2 | The command line was not understood. Nothing was run. |
| 3 | Refused: the entry belongs to someone else. |
| 4 | Refused: a client's configuration could not be read. |
| 5 | The client was not found. With `--client all`, only when none was found. |

With several clients the code is the first that applies of 1, 4, 3, 5, 0.

## Use from an installer or an agent

Pass absolute paths, name your install folder with `--owned-root`, and read the
document, not the text: each result carries the state before and after, the commands
that ran with their exit codes, advice codes for what the person has to do next (start
a new session, approve a project, trust a folder), and the PowerShell line that makes
the same change by hand. `--timeout` bounds the whole run; the default is 30 seconds.

## Building and testing

Requirements: Windows x64, the .NET 10 SDK, the Visual Studio C++ build tools
(NativeAOT links with them), PowerShell 7 and Git.

```powershell
pwsh build/Publish.ps1   # writes artifacts\publish\RegisterAI.exe
dotnet test              # the contract tests run the published executable
```

The tests drive the executable against a fake client and never touch a real client
configuration. They never write your user PATH either: the path verbs are tested against
a PATH kept in memory or under a scratch registry key, the executable only with
`--dry-run`, and the run fails if your PATH reads differently after it than before. To
refuse commits that carry details of your machine, turn on the hook once per clone with
`git config core.hooksPath build/hooks`.

## Compatibility

The JSON documents carry a schema number. A change that renames or removes a key or a
published word comes with a new schema number. New keys and words can arrive within a
schema number, so read the documents tolerantly.

## License

RegisterAI is licensed under the RegisterAI License, a variant of the Functional
Source License 1.1 (MIT Future License) whose change date is the fifth anniversary of
each release instead of the second. See [LICENSE](LICENSE), or run `registerai license`.
