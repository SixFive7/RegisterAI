# Changelog

## 0.2.0 - 2026-10-03

- `path add <folder>` and `path remove <folder>` change the user PATH,
  `HKEY_CURRENT_USER\Environment\Path`, when asked to and at no other time. Adding a
  folder that is there already writes nothing; removing takes off every entry naming
  exactly that folder, case aside. The value keeps its kind, every write is read back
  before it counts and is then announced to running programs, and both take
  `--dry-run`. `path add` refuses a folder that does not exist.
- `status` lists the entries of the user PATH that name a folder that does not exist,
  each with the `registerai path remove` line for it, and removes none of them.
- `--path-folder <dir>` names the folder a bare command lives in. The `path-missing`
  advice then reports when that folder is not on the PATH and carries the
  `registerai path add` line for it, in place of the Windows editor for environment
  variables it offered before.
- Every JSON document gains a `path` key: the user PATH block for `status` and the path
  verbs, and null in the others. The schema number stays 1.

## 0.1.0 - 2026-10-03

- The repository: licence, build settings, banned calls, house-rule tests and the
  machine-details scan with its pre-commit hook. The executable prints its version.
- The command-line contract: one table feeds the parser, the help text and
  `describe`. Every usage error is one JSON document with exit code 2. `license`
  prints the terms compiled into the executable. A fake client plays `claude.exe` or
  `codex.exe` for the tests.
- `status`, `register` and `unregister` for Claude Code and Codex at user and project
  scope. An entry is the caller's own when it names the caller's command or resolves
  under an `--owned-root`; every other entry is refused. Every write is read back
  before it counts. Advice codes say what a person has to do next.
- Tests against the real Claude Code and Codex, copied into scratch and pointed at
  scratch configuration, at both scopes. `docs/clients.md` lists what they hold and
  the versions they were measured at.
