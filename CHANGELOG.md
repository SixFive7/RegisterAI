# Changelog

## Unreleased

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
