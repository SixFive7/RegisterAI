# Changelog

Everything notable that has happened to RegisterAI. The format is
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versions are
[semantic](https://semver.org/spec/v2.0.0.html).

A version is a git tag. The build reads it from the nearest `v*` tag through MinVer, so
no version number is typed into a project file. A section heading carries the bare
version and the date of the commit the tag is on; the tag carries the `v`.

Entries go under `[Unreleased]` as the work lands. A release moves them under its own
heading.

Every entry opens with one icon from the palette below, then a bold one-sentence
headline, then the whole of what happened. The icon says what the entry is.
[`build/New-ReleaseNotes.ps1`](build/New-ReleaseNotes.ps1) builds each release page
from that shape: the section's opening paragraphs, then one line per entry linked to its
lines in this file.

| Icon | Meaning | Icon | Meaning |
|---|---|---|---|
| ✨ | new capability | 🐛 | fix |
| 🔧 | behaviour or configuration change | 🔒 | security or permissions |
| 🗑️ | removal or deprecation | 💥 | breaking, or the reader must act |
| 📝 | documentation | ✅ | tests and the gate |
| 📦 | packaging, installer, release pipeline | ⚡ | performance |
| ♻️ | refactor with no behaviour change | ⬆️ | dependency move |

## [Unreleased]

### Added

- 📦 **Each release page lists one line per change, linked to the full entry in the changelog.**
  `build/New-ReleaseNotes.ps1` reads a version's section of `CHANGELOG.md` and writes the
  body of its GitHub release: the section's opening paragraphs, then each entry as its
  icon and headline with a `read more` link to the entry's own lines in the changelog the
  tag carries, then the icon legend and a link to the whole section. It refuses a
  changelog that differs from HEAD, a tag that is not at HEAD, and any entry or group not
  written in the shape it reads. A body over GitHub's 125,000 characters keeps its
  headlines and drops the per-entry links. `build/New-Release.ps1` runs it before it tags
  HEAD, so a changelog in the wrong shape stops a release before anything is tagged. The
  release script now finds the section by its `## [<version>] - <date>` heading, and
  refuses a release that leaves entries under `[Unreleased]`.

- ✅ **`ChangelogTests` keeps the changelog in the shape the release page is built from.**
  Each entry opens with one icon from the legend and a bold headline of one sentence and
  at most 100 characters, whose first five words are not repeated as the first five of
  its detail. The legend is a table of exactly the twelve icons, two to a row. The first
  heading is `[Unreleased]` and every other one is the bare version and a date, each
  released section opens with a paragraph, and its groups are the Keep a Changelog set in
  that format's order, each at most once. The generator is run against a small changelog in scratch: the links carry
  each entry's own line range and the footer the section's anchor, and each refusal fires
  on the defect it names. Every rule has a planted control that must fail.

### Changed

- 📝 **The README is written for a first reading, with an example for every command.**
  It opens with what RegisterAI does and how to install it and check the download, and a
  quick start of three lines. Each command has its own part with its usage line and an
  example, and the options, clients and scopes, ownership rules, output and exit codes
  each have a table or a short list. A section says what RegisterAI changes on a machine
  and what it never touches, and the last ones cover building, testing and releasing from
  source. Every fact was checked against the code.

- 📝 **Every changelog entry has an icon and a one-sentence headline, and nothing was taken out.**
  The 0.1.0 and 0.2.0 sections were reshaped into the form above: each gained an opening
  paragraph, its entries were sorted into Keep a Changelog groups, and each entry gained
  an icon and a headline, with the text it had before following word for word. The
  headings gained the brackets Keep a Changelog writes, and `[Unreleased]` is where the
  next entries go.

- 📝 **`SECURITY.md` names the `.codex` folder RegisterAI creates for a Codex project entry.**
  Before writing a Codex entry at project scope, RegisterAI creates `<project>\.codex`
  when it is missing, because Codex refuses a configuration folder that does not exist,
  and removes it again when the write fails and the folder is still empty. The list of
  what RegisterAI can change on a machine left this out.

- 📝 **The real-client facts were measured again at Claude Code 2.1.289.**
  `RealClientTests` passed on 2026-10-04 against Claude Code 2.1.289 and codex-cli
  0.159.0-alpha.12.1, run in scratch, and `docs/clients.md` names those versions. The
  sentence there about Codex not expanding variables now gives what it rests on: the
  date it was measured, 2026-09-24, the codex-cli version, 0.155.0-alpha.9.2, and the 48
  attempts.

## [0.2.0] - 2026-10-03

RegisterAI can put a folder on the user PATH and take it off again, and `status` reports
the entries of the user PATH that name a folder that is gone. Nothing changes the PATH
unless one of the two new verbs is run.

### Added

- ✨ **Two new verbs put a folder on the user PATH and take it off again.**
  `path add <folder>` and `path remove <folder>` change the user PATH,
  `HKEY_CURRENT_USER\Environment\Path`, when asked to and at no other time. Adding a
  folder that is there already writes nothing; removing takes off every entry naming
  exactly that folder, case aside. The value keeps its kind, every write is read back
  before it counts and is then announced to running programs, and both take
  `--dry-run`. `path add` refuses a folder that does not exist.

- ✨ **`status` reports the user PATH entries that name a folder that does not exist.**
  `status` lists the entries of the user PATH that name a folder that does not exist,
  each with the `registerai path remove` line for it, and removes none of them.

### Changed

- 🔧 **The `path-missing` advice carries the line that adds the folder, given `--path-folder`.**
  `--path-folder <dir>` names the folder a bare command lives in. The `path-missing`
  advice then reports when that folder is not on the PATH and carries the
  `registerai path add` line for it, in place of the Windows editor for environment
  variables it offered before.

- 🔧 **The JSON documents gain a `path` key, and the schema number stays 1.**
  Every JSON document gains a `path` key: the user PATH block for `status` and the path
  verbs, and null in the others. The schema number stays 1.

## [0.1.0] - 2026-10-03

The first release. RegisterAI registers a local MCP server with Claude Code and Codex, at
user or project scope, through each client's own `mcp add` and `mcp remove`. It changes
only an entry that is the caller's own and reads every change back before it reports it.

### Added

- ✨ **RegisterAI registers a server with Claude Code and Codex, at user or project scope.**
  `status`, `register` and `unregister` for Claude Code and Codex at user and project
  scope. An entry is the caller's own when it names the caller's command or resolves
  under an `--owned-root`; every other entry is refused. Every write is read back
  before it counts. Advice codes say what a person has to do next.

- ✨ **One table defines the command line, the help text and `describe`.**
  The command-line contract: one table feeds the parser, the help text and
  `describe`. Every usage error is one JSON document with exit code 2. `license`
  prints the terms compiled into the executable. A fake client plays `claude.exe` or
  `codex.exe` for the tests.

- ✅ **The real Claude Code and Codex are tested too, never against a real configuration.**
  Tests against the real Claude Code and Codex, copied into scratch and pointed at
  scratch configuration, at both scopes. `docs/clients.md` lists what they hold and
  the versions they were measured at.

- ✅ **The repository starts with its build rules, its house rules and a machine-details scan.**
  The repository: licence, build settings, banned calls, house-rule tests and the
  machine-details scan with its pre-commit hook. The executable prints its version.
