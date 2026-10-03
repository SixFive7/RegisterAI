// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI;

/// <summary>The clients this version supports, in the fixed order every report lists them.</summary>
internal enum ClientId
{
    /// <summary>Anthropic's Claude Code.</summary>
    ClaudeCode,

    /// <summary>OpenAI's Codex.</summary>
    Codex,
}

/// <summary>Which of a client's configurations an entry is in.</summary>
internal enum Scope
{
    /// <summary>Every project of this Windows user.</summary>
    User,

    /// <summary>One folder, in a file that folder holds.</summary>
    Project,
}

/// <summary>
/// What a caller, a person or <c>describe</c> needs to know about one client.
/// </summary>
/// <param name="Id">The client.</param>
/// <param name="Word">Its id on the command line and in every document.</param>
/// <param name="Name">Its name in a sentence.</param>
/// <param name="Executable">The executable this tool runs, by file name.</param>
/// <param name="Reads">How this tool reads an entry: <c>file</c> or <c>client-command</c>.</param>
/// <param name="ExpandsVariables">The variable spellings the client expands in a command, if any.</param>
/// <param name="UserConfig">Where a user-scope entry lives.</param>
/// <param name="ProjectConfig">Where a project-scope entry lives.</param>
/// <param name="Search">Where the executable is looked for, in order, when no --client-exe names it.</param>
internal sealed record ClientInfo(
    ClientId Id,
    string Word,
    string Name,
    string Executable,
    string Reads,
    IReadOnlyList<string> ExpandsVariables,
    string UserConfig,
    string ProjectConfig,
    IReadOnlyList<string> Search);

/// <summary>The client table. Every per-client difference visible to a caller is here.</summary>
internal static class Clients
{
    /// <summary>How every client in this version is written to.</summary>
    public const string Writes = "client-command";

    /// <summary>Both clients, Claude Code first.</summary>
    public static IReadOnlyList<ClientInfo> All { get; } =
    [
        new(
            ClientId.ClaudeCode,
            "claude-code",
            "Claude Code",
            "claude.exe",
            "file",
            ["${NAME}", "${NAME:-default}"],
            "%CLAUDE_CONFIG_DIR%\\.claude.json when that variable is set, else %USERPROFILE%\\.claude.json",
            "<project>\\.mcp.json",
            ["PATH", "%USERPROFILE%\\.local\\bin"]),
        new(
            ClientId.Codex,
            "codex",
            "Codex",
            "codex.exe",
            "client-command",
            [],
            "%CODEX_HOME%\\config.toml when that variable is set, else %USERPROFILE%\\.codex\\config.toml",
            "<project>\\.codex\\config.toml",
            [
                "PATH",
                "%USERPROFILE%\\.local\\bin",
                "the Codex desktop app's manifest, %LOCALAPPDATA%\\OpenAI\\Codex\\chrome-native-hosts-v2.json",
                "an npm global install, %APPDATA%\\npm\\node_modules\\@openai\\codex",
            ]),
    ];

    /// <summary>A client's row.</summary>
    /// <param name="id">The client.</param>
    /// <returns>Its row.</returns>
    public static ClientInfo Info(this ClientId id) => All.Single(client => client.Id == id);

    /// <summary>A scope's word on the command line and in documents.</summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The word.</returns>
    public static string Word(this Scope scope) => scope is Scope.User ? "user" : "project";
}
