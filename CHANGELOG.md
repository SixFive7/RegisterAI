# Changelog

## Unreleased

- The repository: licence, build settings, banned calls, house-rule tests and the
  machine-details scan with its pre-commit hook. The executable prints its version.
- The command-line contract: one table feeds the parser, the help text and
  `describe`. Every usage error is one JSON document with exit code 2. `license`
  prints the terms compiled into the executable. A fake client plays `claude.exe` or
  `codex.exe` for the tests. `status`, `register` and `unregister` parse and
  validate their command lines and stop there.
