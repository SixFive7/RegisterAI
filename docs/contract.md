# The contract

`registerai help` and `registerai describe` are the contract; both are rendered from
the tables in `src/RegisterAI/CommandLine.cs` and `src/RegisterAI/Vocabulary.cs`, and
`ContractTests.HelpAndDescribeAgree` holds them against each other. This page says why
the rules are what they are.

## Ownership

An entry is the caller's own when it names exactly the caller's command, case aside,
or when its command resolves to a file under an `--owned-root`. Claude Code's commands
are expanded the way Claude Code expands them; Codex's are not expanded. With neither a
command nor an owned root, every entry is foreign.

An own entry is `ours` when the file it names exists and, if a command was given, it
names that command. Otherwise it is `ours-stale`. Arguments and environment variables do
not enter the state: `register` leaves an own matching entry as it is, with any
arguments a person added, and `--replace` is the explicit way to rewrite it.

## The state-by-verb table

| State | register | register `--replace` | register `--take-over` | unregister | Exit |
|---|---|---|---|---|---|
| absent | added | added | added | none | 0 |
| ours | none | replaced | none | removed | 0 |
| ours-stale, names the command | none | replaced | none | removed | 0 |
| ours-stale, names something else | replaced | replaced | replaced | removed | 0 |
| foreign | refused-foreign | refused-foreign | replaced | refused-foreign | 3 |
| unreadable | refused-unreadable | refused-unreadable | refused-unreadable | refused-unreadable | 4 |
| unknown | client-not-found | client-not-found | client-not-found | client-not-found | 5 |

A write that needs a client which was not found becomes `client-not-found`. A dry run
decides the same way and runs no write. `DecisionTableTests.TheStateByVerbTable` holds
this table.

## Confirming a write

Every write is followed by the same read that preceded it. A register counts only
when the entry reads back with the command, arguments and environment names asked
for; an unregister only when the entry reads back absent. Otherwise the action is
`failed` and the exit code is 1, whatever the client's own exit code was.

## Advice

| Code | When |
|---|---|
| `claude-restart-session` | After any change to a Claude Code entry. |
| `claude-approve-project` | After writing a Claude Code project entry. |
| `codex-new-thread` | After any change to a Codex entry. |
| `codex-trust-project` | After writing a Codex project entry. |
| `codex-restart-for-path` | A Codex entry names a bare command that the new-program PATH finds. |
| `path-missing` | An entry names a bare command, and the folder `--path-folder` names is not on the new-program PATH; its `command` is `registerai path add` for that folder. Without `--path-folder`, the bare command is found in no folder there, and `command` is null because no folder is known. |

## The user PATH

`path add` and `path remove` are the only verbs that write the PATH, and they write only
`HKEY_CURRENT_USER\Environment\Path`. No other verb changes it: `register` with a bare
command reports a missing folder and leaves the PATH to whoever runs the line it offers.

- `path add` takes a full path to a folder that exists, so it cannot add an entry that
  names nothing. It appends after a separator even when the value already ends in one,
  which is what makes the remove after it give back the value byte for byte.
- `path remove` takes off every entry naming exactly the folder given, compared without
  case, surrounding spaces or a trailing separator. A different spelling of the same
  folder, such as one with a variable, is a different entry and is left.
- The value's kind is kept. A value that held only the removed folder is deleted, the way
  an add from nothing created it; a value that becomes empty text stays as empty text.
- A write counts only when the value reads back exactly as written, and only then is it
  announced with `WM_SETTINGCHANGE`. Otherwise the action is `failed` and the exit code
  is 1.

`status` reports, in `path.dead`, every entry of the user PATH that is a full path once
`%NAME%` is expanded (in a `REG_EXPAND_SZ` value only) and names a folder that does not
exist, each with the `registerai path remove` line for it. It never removes one. A network
path is not judged, because asking a share whether a folder exists can take as long as the
network does.
