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
| `path-missing` | An entry names a bare command that the new-program PATH does not find. Its `command` opens the Windows editor for environment variables; RegisterAI never edits PATH. |
