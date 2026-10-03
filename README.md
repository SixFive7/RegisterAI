# RegisterAI

RegisterAI is a small Windows command-line tool that registers a local MCP server
with coding agents by running each agent's own command. It ships as one
self-contained executable with no support files.

The first version supports Claude Code and Codex, each at user and project scope.

## Status

In development. This build implements `--version` only; the command-line contract
follows.

## Building

Requirements: Windows x64, the .NET 10 SDK, the Visual Studio C++ build tools
(NativeAOT links with them), PowerShell 7 and Git.

```powershell
pwsh build/Publish.ps1   # writes artifacts\publish\RegisterAI.exe
dotnet test              # the suite runs the published executable
```

To refuse commits that carry details of your machine, turn on the hook once per clone:

```powershell
git config core.hooksPath build/hooks
```

## License

RegisterAI is licensed under the RegisterAI License, a variant of the Functional
Source License 1.1 (MIT Future License) whose change date is the fifth anniversary
of each release instead of the second. See [LICENSE](LICENSE).
