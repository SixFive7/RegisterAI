# RegisterAI

RegisterAI adds a local MCP server to Claude Code and Codex from the command line, and
takes it out again. It is one small executable for Windows x64.

It makes every change through the client's own `mcp add` and `mcp remove` commands, and
never edits a client's configuration file itself. After each change it reads the entry
back, and it answers with one JSON document: what it found, what it did, and what a
person still has to do. It never prompts and never reads from stdin, so an installer or
an agent can run it unattended.

- [Install](#install)
- [Quick start](#quick-start)
- [Commands](#commands)
- [Options](#options)
- [Clients and scopes](#clients-and-scopes)
- [Whose entry it is](#whose-entry-it-is)
- [Output](#output)
- [Exit codes](#exit-codes)
- [What it changes, and what it never touches](#what-it-changes-and-what-it-never-touches)
- [Use from an installer or an agent](#use-from-an-installer-or-an-agent)
- [Building from source](#building-from-source)
- [Compatibility](#compatibility), [Security](#security), [License](#license)

## Install

1. Download `RegisterAI.exe` and `SHA256SUMS` from the
   [latest release](https://github.com/SixFive7/RegisterAI/releases/latest).
2. Check the download. In PowerShell, in the folder that holds both files:

   ```powershell
   (Get-FileHash RegisterAI.exe -Algorithm SHA256).Hash.ToLowerInvariant() -eq (Get-Content SHA256SUMS).Split(' ')[0]
   ```

   It prints `True` when the file is the one the release lists.
3. Put `RegisterAI.exe` wherever suits you. There is nothing else to install: it needs
   no runtime, no support files and no administrator rights.

The examples below assume the folder that holds `RegisterAI.exe` is on your PATH. If it
is not, give the file's full path instead of `registerai`.

## Quick start

Say your MCP server is `C:\Apps\Demo\demo-mcp.exe` and you want the clients to call it
`demo`. Register it with both clients, for every project of your Windows user:

```powershell
registerai register --name demo --client all --scope user --owned-root 'C:\Apps\Demo' -- 'C:\Apps\Demo\demo-mcp.exe'
```

See what each client now holds under that name:

```powershell
registerai status --name demo --owned-root 'C:\Apps\Demo'
```

Take it out again:

```powershell
registerai unregister --name demo --client all --scope user --owned-root 'C:\Apps\Demo'
```

`--owned-root` tells RegisterAI which entries are yours: an entry whose command resolves
to a file under that folder. It changes no other entry. Add `--dry-run` to `register` or
`unregister` to see what would happen without changing anything. Running `register` a
second time changes nothing, because the entry is already right.

## Commands

| Command | What it does | What it writes |
|---|---|---|
| `status` | Reports, per client, whether the server is registered and whose entry it is. | Nothing. |
| `register` | Makes the entry exist and name your command. | The client's MCP entry. |
| `unregister` | Removes the entry, but only if it is yours. | The client's MCP entry. |
| `path add` | Puts a folder on the user PATH. | The user PATH. |
| `path remove` | Takes a folder off the user PATH. | The user PATH. |
| `describe` | Prints the whole contract as JSON. | Nothing. |
| `help` | Prints the contract as text, or one command's part of it. | Nothing. |
| `license` | Prints the licence terms. | Nothing. |
| `--version` | Prints the version and nothing else. | Nothing. |

Everything after `--` is the server's own command line. It reaches the client argument
by argument and unchanged, so your shell must not change it first: in PowerShell and
bash, put it in single quotes. cmd.exe expands `%NAME%` even inside double quotes; it
leaves `${NAME}` alone.

### status

```text
registerai status --name <server> [options] [-- <command> [args...]]
```

Reads each client's entry under that name and reports its state, the command it names
and the file that command resolves to. It only reads, and changes no entry. Without
`--owned-root` or a command after `--`, RegisterAI cannot tell which entries are yours,
so any entry it finds is reported as `foreign`.

```powershell
registerai status --name demo
registerai status --name demo --owned-root 'C:\Apps\Demo' -- 'C:\Apps\Demo\demo-mcp.exe'
```

`status` also lists the entries of your user PATH that name a folder that does not
exist, each with the `registerai path remove` line that takes it off. It removes none of
them.

### register

```text
registerai register --name <server> --client <id> --scope <scope> [options] -- <command> [args...]
```

Makes the entry exist and name `<command>` and its arguments. What it does depends on
what is there:

- nothing under that name: the entry is added;
- your own entry naming your command: it is left as it is, with any arguments a person
  added, unless you pass `--replace`;
- your own entry naming a different file, such as an older install's: it is replaced;
- someone else's entry: it is refused with exit code 3, unless you pass `--take-over`;
- a configuration RegisterAI cannot read: it is refused with exit code 4.

```powershell
registerai register --name demo --client all --scope user --owned-root 'C:\Apps\Demo' -- 'C:\Apps\Demo\demo-mcp.exe'
registerai register --name demo --client claude-code --scope project --project 'C:\src\repo' -- '${LOCALAPPDATA}/Demo/demo-mcp.exe'
registerai register --name demo --client codex --scope user --env 'DEMO_HOME=C:\Apps\Demo' --owned-root 'C:\Apps\Demo' -- 'C:\Apps\Demo\demo-mcp.exe' --stdio
```

The full table of states and what each verb does about them is in
[docs/contract.md](docs/contract.md#the-state-by-verb-table).

### unregister

```text
registerai unregister --name <server> --client <id> --scope <scope> [options] [-- <command> [args...]]
```

Removes the entry, but only when it is yours: name your folder with `--owned-root`, or
give your command after `--`. Someone else's entry is refused with exit code 3, and an
entry that is not there is not an error.

```powershell
registerai unregister --name demo --client all --scope user --owned-root 'C:\Apps\Demo'
```

### path add and path remove

```text
registerai path add <folder> [--dry-run]
registerai path remove <folder> [--dry-run]
```

These two change the user PATH, `HKEY_CURRENT_USER\Environment\Path`, and they are the
only commands that do. A client finds a server whose command is a bare file name, such
as `demo-mcp.exe`, through the PATH.

```powershell
registerai path add 'C:\Apps\Demo'
registerai path remove 'C:\Apps\Demo'
```

- `path add` takes only a full path to a folder that exists, so it never adds an entry
  that names nothing. It puts the folder after the entries already there, and writes
  nothing when the folder is there already.
- `path remove` takes off every entry naming exactly that folder, compared without case
  and without a trailing separator. An entry spelled another way, such as with a
  variable, is left alone.
- The value keeps its kind, `REG_EXPAND_SZ` or `REG_SZ`, and a value that did not exist
  is created as `REG_EXPAND_SZ`. An add and the remove after it give back the value as it
  was, byte for byte.
- Each write is read back before it counts, and then announced to running programs. A
  program that ignores the announcement keeps the PATH it started with, and so does
  everything it starts.

### describe, help, license and --version

```powershell
registerai describe            # the verbs, options, clients, words, exit codes and document schema, as JSON
registerai help                # the same contract as text
registerai help register       # one command's part of it; 'registerai help path' covers both path commands
registerai license             # the licence terms, compiled into the executable
registerai --version           # the version alone, for example 0.3.0
```

Run with no arguments at all, RegisterAI prints the help text and exits with code 2.

## Options

| Option | Taken by | Meaning |
|---|---|---|
| `--name <server>` | status, register, unregister | The server's name in the client: letters, digits, `-` and `_`. Required. |
| `--client <id>` | status, register, unregister | `claude-code`, `codex` or `all`. Repeatable. Required for register and unregister; status asks all by default. |
| `--scope <scope>` | status, register, unregister | `user` (every project of this Windows user) or `project` (one folder). Required for register and unregister; status reads the user scope by default. |
| `--project <dir>` | status, register, unregister | The folder for `--scope project`: a full path to a folder that exists. It is never guessed. |
| `--owned-root <dir>` | status, register, unregister | An entry whose command resolves to a file under this folder is yours. Repeatable. |
| `--env KEY=VALUE` | register | An environment variable for the server. Repeatable. Its value is kept out of the output; see [Output](#output). |
| `--replace` | register | Rewrite your own entry even when it already matches. |
| `--take-over` | register | Replace someone else's entry. Pass it only when a person has just asked for exactly that. |
| `--dry-run` | register, unregister, path add, path remove | Decide and report, and change nothing. |
| `--timeout <seconds>` | status, register, unregister | The budget for the whole run, from 1 to 3600 seconds. The default is 30. |
| `--client-exe <id>=<path>` | status, register, unregister | Use this executable for a client, in place of the search below. Repeatable. |
| `--path-folder <dir>` | status, register | The folder a bare command lives in. When it is not on the PATH a new program gets, the advice carries the `registerai path add` line for it. |

`help` lists the same options, and `registerai help <command>` shows the ones a command
takes.

## Clients and scopes

| Client | Scope | How RegisterAI writes | Where it reads the entry |
|---|---|---|---|
| Claude Code | user | `claude mcp add <name> --scope user` | `%CLAUDE_CONFIG_DIR%\.claude.json` when that variable is set, else `%USERPROFILE%\.claude.json` |
| Claude Code | project | the same with `--scope project`, run in the project folder | `<project>\.mcp.json` |
| Codex | user | `codex mcp add <name>` | `codex mcp list --json` |
| Codex | project | the same with `CODEX_HOME` set to `<project>\.codex` | `codex mcp list --json`, with the same variable |

Claude Code keeps its user entries in `.config.json` in its configuration folder
(`%CLAUDE_CONFIG_DIR%`, else `%USERPROFILE%\.claude`) when that file exists, and
RegisterAI reads them there.

RegisterAI finds `claude.exe` and `codex.exe` on PATH, then in
`%USERPROFILE%\.local\bin`. For Codex it also looks where the Codex desktop app records
its command-line tool, and in a global npm install. `--client-exe` overrides the search.
`describe` lists every place.

[docs/clients.md](docs/clients.md) lists what RegisterAI relies on in each client, with
the version it was last measured at.

## Whose entry it is

RegisterAI changes an entry only when it is yours, unless `register` is given
`--take-over`. An entry is yours when it names exactly your command, case aside, or when
its command resolves to a file under one of your `--owned-root` folders. Every other
entry is someone else's.

To resolve a command, RegisterAI follows each client's own rules. Claude Code expands
`${NAME}` and `${NAME:-default}`, so RegisterAI does the same for its entries; Codex
expands nothing. A bare file name is looked up on the PATH a newly started program would
get, and the file it finds is reported as `resolvesTo`.

| State | Meaning |
|---|---|
| `absent` | No entry of that name. |
| `ours` | Your own entry, naming a file that exists, and your command when you gave one. |
| `ours-stale` | Your own entry, naming a file that is gone or something other than your command. |
| `foreign` | An entry you did not write. RegisterAI leaves it alone. |
| `unreadable` | The configuration could not be read. RegisterAI assumes nothing and writes nothing. |
| `unknown` | The client was not found, so nothing is known. |

## Output

`status`, `register`, `unregister`, `path add`, `path remove`, `describe` and every
usage error write exactly one JSON document to stdout: UTF-8 without a byte order mark,
LF line ends, and `"schema": 1`. `help`, `license` and `--version` write plain text.
Diagnostics go to stderr. Values given with `--env` are kept out: the document names each
variable without its value, the commands it lists show `<redacted>` in the value's
place, and in what a client printed, every value of three characters or more is replaced
with `<redacted>`.

This is `status` for one client, with an entry that is yours:

```json
{
  "tool": "registerai",
  "version": "0.3.0",
  "schema": 1,
  "verb": "status",
  "dryRun": false,
  "exitCode": 0,
  "error": null,
  "server": {
    "name": "demo",
    "command": null,
    "args": [],
    "env": []
  },
  "results": [
    {
      "client": "claude-code",
      "scope": "user",
      "project": null,
      "clientPath": "C:\\Users\\<you>\\.local\\bin\\claude.exe",
      "config": "C:\\Users\\<you>\\.claude.json",
      "state": "ours",
      "command": "C:\\Apps\\Demo\\demo-mcp.exe",
      "args": [],
      "env": [],
      "resolvesTo": "C:\\Apps\\Demo\\demo-mcp.exe",
      "advice": [],
      "error": null
    }
  ],
  "path": {
    "where": "HKEY_CURRENT_USER\\Environment\\Path",
    "folder": null,
    "action": null,
    "announced": null,
    "dead": [],
    "error": null
  }
}
```

For `register` and `unregister`, each result carries the entry `before` and `after`, the
`action` taken, every client command that `ran` with its exit code, what the client
`said`, and the `manual` PowerShell line that makes the same change by hand. `describe`
prints the schema of every document.

Each result's `advice` lists what a person still has to do:

| Code | When |
|---|---|
| `claude-restart-session` | After any change to a Claude Code entry: open sessions do not see it until restarted. |
| `claude-approve-project` | After writing a Claude Code project entry: each person approves it the first time a session opens there. |
| `codex-new-thread` | After any change to a Codex entry: an open thread does not see it. |
| `codex-trust-project` | After writing a Codex project entry: Codex reads it only in a project it trusts. |
| `codex-restart-for-path` | A Codex entry names a bare command found on the PATH: a Codex started before that folder was on the PATH needs a restart. |
| `path-missing` | An entry names a bare command and the folder it needs is not on the PATH a new program gets. With `--path-folder`, the advice carries the `registerai path add` line for it. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Done, or nothing needed doing. |
| 1 | A client command failed or timed out, or a change could not be made or confirmed. |
| 2 | The command line was not understood. Nothing was run. |
| 3 | Refused: the entry belongs to someone else. |
| 4 | Refused: a client's configuration could not be read. |
| 5 | The client was not found. With `--client all`, only when neither client was found. |

With more than one client, the exit code is the first of 1, 4, 3, 5 and 0 that applies.
A dry run of `register` or `unregister` returns 0, 3, 4 or 5 and never 1. `path add` and
`path remove` return 0, 2, or 1 when the PATH could not be read, written or confirmed.

## What it changes, and what it never touches

RegisterAI changes these, and nothing else:

- **A client's MCP entries**, only through that client's own `mcp add` and `mcp remove`.
- **The user PATH**, `HKEY_CURRENT_USER\Environment\Path`, only through `path add` and
  `path remove`.
- **A project's `.codex` folder.** Codex refuses a configuration folder that does not
  exist, so before writing a Codex entry at project scope RegisterAI creates
  `<project>\.codex` when it is missing, and removes it again if the write fails and the
  folder is still empty. After each Codex run in a project it also removes the empty
  `tmp\arg0` and `tmp` folders Codex leaves there, only while they are empty.

It never:

- edits a client's configuration file itself;
- changes an entry that is not yours, unless you pass `register --take-over`;
- changes the PATH as a side effect of another command, or touches the machine PATH,
  which it only reads;
- prompts or reads stdin (a client it runs gets end of input at once);
- makes a network call of its own;
- needs administrator rights;
- keeps files, settings or registry values of its own.

The clients it runs may write files of their own while they run, as they would if you
ran them yourself.

## Use from an installer or an agent

- Pass absolute paths, and name your install folder with `--owned-root`.
- Read the JSON document, not the text and not the exit code alone. Each result says
  what the entry was before and after, which commands ran with their exit codes, and the
  advice codes for what the person has to do next.
- Show the `manual` line when a change fails: it is the PowerShell line that makes the
  same change by hand.
- `--timeout` bounds the whole run; the default is 30 seconds.
- Use `--take-over` only when a person has just asked to replace an entry that is not
  yours.

## Building from source

You need Windows x64, the .NET 10 SDK (10.0.401 or later), the Visual Studio C++ build
tools that NativeAOT links with, PowerShell 7 and Git.

```powershell
git clone https://github.com/SixFive7/RegisterAI.git
cd RegisterAI
pwsh build/Publish.ps1    # writes artifacts\publish\RegisterAI.exe
dotnet test               # the contract tests run the published executable
```

Publish before testing: the contract tests run the published executable and refuse one
older than the source.

The tests drive the executable against a fake client that plays `claude.exe` and
`codex.exe`. Where Claude Code and Codex are installed, `RealClientTests` also runs
copies of them, with their configuration folders and profile pointed into scratch, and
skips when either is missing. No test reads or writes a real client configuration, and
none writes your user PATH: the path commands are tested against a PATH kept in memory or
under a scratch registry key, the executable only with `--dry-run`, and the run fails if
your PATH reads differently afterwards.

To refuse commits that carry details of your machine, turn on the hook once per clone:

```powershell
git config core.hooksPath build/hooks
```

`pwsh build/New-Release.ps1 -Version <x.y.z>` cuts a release from a clean, pushed tree.
It tags the commit, publishes, runs the whole suite with the real clients required,
scans the executable for machine details, writes `RegisterAI.exe` and `SHA256SUMS`, and
creates the GitHub release with notes built from [CHANGELOG.md](CHANGELOG.md).

## Compatibility

Every JSON document carries a schema number. A change that renames or removes a key or
a published word comes with a new schema number. New keys and words can arrive within a
schema number, so read the documents tolerantly.

## Security

Report a vulnerability privately, as [SECURITY.md](SECURITY.md) describes, and not in a
public issue.

## License

RegisterAI is licensed under the RegisterAI License, a variant of the Functional Source
License 1.1 (MIT Future License) whose change date is the fifth anniversary of each
release instead of the second. See [LICENSE](LICENSE), or run `registerai license`.
