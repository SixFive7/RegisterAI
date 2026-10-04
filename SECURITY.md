# Security

## Reporting a vulnerability

Use GitHub's private vulnerability reporting: on this repository's **Security** tab,
choose **Report a vulnerability**. Do not open a public issue for it.

Say which release you ran (`registerai --version`), the command line, and what
happened. The JSON document a run writes helps, and it never carries `--env` values.

## Supported versions

Fixes ship in a new release. Only the newest release receives them.

## What RegisterAI can change

- A client's MCP entries, only through that client's own `mcp add` and `mcp remove`.
- The user PATH, `HKEY_CURRENT_USER\Environment\Path`, only through `path add` and
  `path remove`.
- The empty `tmp\arg0` and `tmp` folders a Codex run leaves in a project's `.codex`
  folder, and only while they are empty.

It makes no network call of its own and needs no administrator rights. The executable
is not signed, so check it against the release's `SHA256SUMS` before running it, as
the [README](README.md#install) shows.
