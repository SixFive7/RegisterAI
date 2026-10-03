// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace RegisterAI;

/// <summary>
/// How each client is found, written to and described. Every difference between the
/// two clients that the engine acts on lives here, so the engine has no branch on
/// which client it holds.
/// </summary>
/// <remarks>
/// <para>
/// Both clients are written only through their own commands, <c>mcp add</c> and
/// <c>mcp remove</c>, and never by editing their files: the file formats are theirs, and
/// a hand-rolled writer is a second implementation of somebody else's schema.
/// </para>
/// <para>
/// The scope lever differs. Claude Code takes <c>--scope</c> and writes a project's
/// <c>.mcp.json</c> in the folder it is run in. Codex takes no scope at all and writes
/// whichever configuration <c>CODEX_HOME</c> points at, so a project entry is the same
/// command with <c>CODEX_HOME</c> set to <c>&lt;project&gt;\.codex</c>. The arguments, the
/// working folder and the environment are applied together on every call, read or
/// write, because applying two of the three turns a project write into a user write.
/// </para>
/// </remarks>
internal static class ClientRules
{
    /// <summary>Codex's per-project configuration folder.</summary>
    public const string CodexProjectFolder = ".codex";

    /// <summary>The variable that moves Codex's configuration.</summary>
    public const string CodexHome = "CODEX_HOME";

    /// <summary>The variable that moves Claude Code's configuration folder.</summary>
    public const string ClaudeConfigDir = "CLAUDE_CONFIG_DIR";

    /// <summary>
    /// Finds a client's executable: the one <c>--client-exe</c> names, else the first on
    /// PATH, else the places each client's own installers use.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The request, for <c>--client-exe</c>.</param>
    /// <param name="machine">Where to look.</param>
    /// <returns>A full path, or null.</returns>
    public static string? Locate(ClientId client, Request request, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(machine);

        if (request.ClientExecutables.TryGetValue(client, out var named))
        {
            return File.Exists(named) ? named : null;
        }

        var executable = client.Info().Executable;
        var folders = (machine.Variable("PATH") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(folder => folder.Trim('"'))
            .Append(LocalBin(machine));

        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(folder, executable);

                if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (ArgumentException)
            {
                // A PATH entry with a character a path cannot hold.
            }
        }

        return client is ClientId.Codex ? FromCodexManifest(machine) ?? FromNpm(machine) : null;
    }

    /// <summary>The sentence for a client that was not found, naming every place looked.</summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The request, for <c>--client-exe</c>.</param>
    /// <param name="machine">The machine.</param>
    /// <param name="forStatus">Whether a status asked, which ran nothing either way.</param>
    /// <returns>The sentence.</returns>
    public static string NotFound(ClientId client, Request request, Machine machine, bool forStatus = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var info = client.Info();
        var consequence = forStatus ? $"so what {info.Name} has registered is not known" : $"so nothing was run for {info.Name}";

        if (request.ClientExecutables.TryGetValue(client, out var named))
        {
            return $"--client-exe names '{named}', which does not exist, {consequence}.";
        }

        var places = client is ClientId.Codex
            ? $"on PATH, in '{LocalBin(machine)}', in the Codex desktop manifest '{CodexManifest(machine)}' or under '{NpmPackage(machine)}'"
            : $"on PATH or in '{LocalBin(machine)}'";

        return $"{info.Executable} was not found {places}, {consequence}. Install it, or name it with --client-exe {info.Word}=<path>.";
    }

    /// <summary>The file an entry lives in, whether or not it exists.</summary>
    /// <param name="client">The client.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="project">The project folder, for project scope.</param>
    /// <param name="machine">The machine.</param>
    /// <returns>The file.</returns>
    /// <remarks>
    /// Claude Code keeps its user configuration in <c>.config.json</c> inside its
    /// configuration folder when that file exists, and otherwise in <c>.claude.json</c>:
    /// inside <c>CLAUDE_CONFIG_DIR</c> when that is set, beside the profile when it is not.
    /// Read from the client's own code on 2026-09-22 at Claude Code 2.1.278.
    /// </remarks>
    public static string ConfigFile(ClientId client, Scope scope, string? project, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (scope is Scope.Project)
        {
            return client is ClientId.ClaudeCode
                ? Path.Combine(project!, ".mcp.json")
                : Path.Combine(ProjectHome(project!), "config.toml");
        }

        if (client is ClientId.Codex)
        {
            return Path.Combine(machine.Variable(CodexHome) is { Length: > 0 } home ? home : Path.Combine(machine.Profile, CodexProjectFolder), "config.toml");
        }

        var overridden = machine.Variable(ClaudeConfigDir) is { Length: > 0 } directory ? directory : null;
        var preferred = Path.Combine(overridden ?? Path.Combine(machine.Profile, ".claude"), ".config.json");

        return File.Exists(preferred) ? preferred : Path.Combine(overridden ?? machine.Profile, ".claude.json");
    }

    /// <summary>Codex's home for a project.</summary>
    /// <param name="project">The project folder.</param>
    /// <returns>The folder Codex writes the project's configuration in.</returns>
    public static string ProjectHome(string project) => Path.Combine(project, CodexProjectFolder);

    /// <summary>Where a call runs: the project for Claude Code's project scope, else the profile.</summary>
    /// <param name="client">The client.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="project">The project.</param>
    /// <param name="machine">The machine.</param>
    /// <returns>The folder.</returns>
    public static string WorkingDirectory(ClientId client, Scope scope, string? project, Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return client is ClientId.ClaudeCode && scope is Scope.Project ? project! : machine.Profile;
    }

    /// <summary>The variables a call runs with: <c>CODEX_HOME</c> for Codex's project scope, else none.</summary>
    /// <param name="client">The client.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="project">The project.</param>
    /// <returns>The variables to set.</returns>
    public static IReadOnlyDictionary<string, string> Environment(ClientId client, Scope scope, string? project) =>
        client is ClientId.Codex && scope is Scope.Project
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [CodexHome] = ProjectHome(project!) }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The arguments that write the entry.</summary>
    /// <param name="client">The client.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="request">The name, environment and command line.</param>
    /// <returns>The arguments, one element each.</returns>
    /// <remarks>
    /// <c>--</c> ends the client's own options, so a command or an argument that starts
    /// with a dash is never read as one. The environment goes after the name: Claude
    /// Code's <c>--env</c> takes every value up to the next option, and a name written
    /// after it would be read as one more value.
    /// </remarks>
    public static List<string> AddArguments(ClientId client, Scope scope, Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var arguments = new List<string> { "mcp", "add", request.Name };

        if (client is ClientId.ClaudeCode)
        {
            arguments.AddRange(["--scope", scope.Word()]);
        }

        foreach (var (key, value) in request.Environment)
        {
            arguments.AddRange(["--env", key + "=" + value]);
        }

        arguments.Add("--");
        arguments.Add(request.Command!);
        arguments.AddRange(request.Arguments);

        return arguments;
    }

    /// <summary>The arguments that remove the entry. The scope is stated, so a remove never reaches another scope's entry.</summary>
    /// <param name="client">The client.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="name">The server's name.</param>
    /// <returns>The arguments.</returns>
    public static List<string> RemoveArguments(ClientId client, Scope scope, string name) =>
        client is ClientId.ClaudeCode ? ["mcp", "remove", name, "--scope", scope.Word()] : ["mcp", "remove", name];

    /// <summary>Codex's read: every server, as JSON.</summary>
    /// <returns>The arguments.</returns>
    public static List<string> ListArguments() => ["mcp", "list", "--json"];

    /// <summary>The PowerShell line that makes the same change by hand, with environment values as <c>&lt;value&gt;</c>.</summary>
    /// <param name="client">The client.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="request">The request.</param>
    /// <param name="add">Whether the change writes the entry; otherwise it removes it.</param>
    /// <returns>The line.</returns>
    public static string Manual(ClientId client, Scope scope, Request request, bool add)
    {
        ArgumentNullException.ThrowIfNull(request);

        var shown = request with { Environment = [.. request.Environment.Select(pair => new KeyValuePair<string, string>(pair.Key, "<value>"))] };
        var arguments = add && request.Command is not null ? AddArguments(client, scope, shown) : RemoveArguments(client, scope, request.Name);
        var line = Path.GetFileNameWithoutExtension(client.Info().Executable) + " " + string.Join(' ', arguments.Select(Shell.Quote));

        return (client, scope) switch
        {
            (ClientId.ClaudeCode, Scope.Project) => $"Set-Location -LiteralPath {Shell.Quote(request.Project!)}; {line}",
            (ClientId.Codex, Scope.Project) =>
                $"New-Item -ItemType Directory -Force -Path {Shell.Quote(ProjectHome(request.Project!))} | Out-Null; $env:CODEX_HOME = {Shell.Quote(ProjectHome(request.Project!))}; {line}; Remove-Item Env:CODEX_HOME",
            _ => line,
        };
    }

    private static string LocalBin(Machine machine) => Path.Combine(machine.Profile, ".local", "bin");

    private static string CodexManifest(Machine machine) =>
        Path.Combine(machine.LocalAppData, "OpenAI", "Codex", "chrome-native-hosts-v2.json");

    private static string NpmPackage(Machine machine) =>
        Path.Combine(machine.AppData, "npm", "node_modules", "@openai", "codex");

    /// <summary>
    /// The CLI the Codex desktop app names in its native-host manifest, which is the only
    /// place a desktop install records it: such an install puts the CLI nowhere on PATH.
    /// </summary>
    private static string? FromCodexManifest(Machine machine)
    {
        try
        {
            var manifest = CodexManifest(machine);

            if (!File.Exists(manifest))
            {
                return null;
            }

            using var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind is not JsonValueKind.Array)
            {
                return null;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind is JsonValueKind.Object
                    && entry.TryGetProperty("paths", out var paths)
                    && paths.ValueKind is JsonValueKind.Object
                    && paths.TryGetProperty("codexCliPath", out var cli)
                    && cli.ValueKind is JsonValueKind.String
                    && cli.GetString() is { Length: > 0 } candidate
                    && File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? FromNpm(Machine machine)
    {
        try
        {
            var package = NpmPackage(machine);

            return Directory.Exists(package)
                ? Directory.EnumerateFiles(package, "codex.exe", SearchOption.AllDirectories).Select(Path.GetFullPath).FirstOrDefault()
                : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>PowerShell quoting for a line a person pastes.</summary>
internal static class Shell
{
    /// <summary>
    /// An argument as PowerShell and bash read it unchanged: bare when it is plain,
    /// otherwise in single quotes, inside which neither shell expands anything.
    /// </summary>
    /// <param name="argument">The argument.</param>
    /// <returns>The quoted argument.</returns>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        return argument.Length > 0 && argument.All(character => char.IsAsciiLetterOrDigit(character) || "-_.:/=".Contains(character, StringComparison.Ordinal))
            ? argument
            : "'" + argument.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
