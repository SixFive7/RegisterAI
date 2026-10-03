// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// The published executable against this machine's real Claude Code and Codex, each
/// copied into scratch and pointed at scratch configuration. What these tests hold is
/// what the fake cannot: that the real clients still store what they are handed, still
/// answer the way the fake answers, and still read and write where this tool looks.
/// </summary>
internal sealed class RealClientTests
{
    /// <summary>
    /// The real clients answer the commands the fake models with the same exit codes and
    /// the same key words.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheRealClientsSpeakTheDialectTheFakeModels()
    {
        var real = await RealClients.RequireAsync();

        using var scratch = Scratch.Create("dialect");
        var fake = FakeClients.Install(scratch);

        var spokenByReal = await HeardAsync("real", real);
        var spokenByFake = await HeardAsync("fake", fake);

        await Assert.That(spokenByReal).IsEqualTo("0 | 1 already-exists | 0 | 1 none-user | 1 none-project | 0 | 0 | 0 demo stdio C:\\Apps\\Demo\\demo-mcp.exe --stdio DEMO_HOME | 0 | 0");
        await Assert.That(spokenByFake).IsEqualTo(spokenByReal);
    }

    /// <summary>
    /// What one pair of clients answers to the commands the fake models, as one line: the
    /// exit code of each, and the words that mark the outcome where the outcome has words.
    /// </summary>
    private static async Task<string> HeardAsync(string who, FakeClients clients)
    {
        using var scratch = Scratch.Create("dialect-" + who);
        var sandbox = Tool.Sandbox(scratch);
        var project = Directory.CreateDirectory(scratch.In("project")).FullName;

        RealClients.SeedClaude(sandbox);

        async Task<string> say(string client, string directory, params string[] arguments)
        {
            var run = await Child.RunAsync(client, arguments, TimeSpan.FromSeconds(60), directory, sandbox);

            return run.ExitCode + " " + (run.Output + run.Error).Trim();
        }

        string[] spoken =
        [
            await say(clients.Claude, scratch.Folder, "mcp", "add", "demo", "--scope", "user", "--env", "DEMO_HOME=x", "--", @"C:\Apps\Demo\demo-mcp.exe", "--stdio"),
            await say(clients.Claude, scratch.Folder, "mcp", "add", "demo", "--scope", "user", "--", @"C:\Apps\Demo\demo-mcp.exe"),
            await say(clients.Claude, scratch.Folder, "mcp", "remove", "demo", "--scope", "user"),
            await say(clients.Claude, scratch.Folder, "mcp", "remove", "demo", "--scope", "user"),
            await say(clients.Claude, project, "mcp", "remove", "demo", "--scope", "project"),
            await say(clients.Codex, scratch.Folder, "mcp", "add", "demo", "--", @"C:\Apps\Demo\demo-mcp.exe"),
            await say(clients.Codex, scratch.Folder, "mcp", "add", "demo", "--env", "DEMO_HOME=x", "--", @"C:\Apps\Demo\demo-mcp.exe", "--stdio"),
            await say(clients.Codex, scratch.Folder, "mcp", "list", "--json"),
            await say(clients.Codex, scratch.Folder, "mcp", "remove", "demo"),
            await say(clients.Codex, scratch.Folder, "mcp", "remove", "demo"),
        ];

        return string.Join(
            " | ",
            spoken[0][..1],
            spoken[1][..1] + (spoken[1].Contains("already exists in user config", StringComparison.Ordinal) ? " already-exists" : " ?"),
            spoken[2][..1],
            spoken[3][..1] + (spoken[3].Contains("No MCP server named \"demo\" in user scope", StringComparison.Ordinal) ? " none-user" : " ?"),
            spoken[4][..1] + (spoken[4].Contains("No MCP server named \"demo\" in .mcp.json", StringComparison.Ordinal) ? " none-project" : " ?"),
            spoken[5][..1],
            spoken[6][..1],
            spoken[7][..1] + " " + ListShape(spoken[7][2..]),
            spoken[8][..1],
            spoken[9][..1]);
    }

    /// <summary>
    /// Register, status, a repeat and unregister through the real clients at both
    /// scopes, with an argument and an environment variable that must arrive and read back.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The test.</returns>
    [Test]
    [Arguments("user")]
    [Arguments("project")]
    public async Task TheRealClientsRegisterReadBackAndRemove(string scope)
    {
        var real = await RealClients.RequireAsync();

        using var scratch = Scratch.Create("real-" + scope);
        var sandbox = Tool.Sandbox(scratch);
        var root = Directory.CreateDirectory(scratch.In("apps", "Demo")).FullName;
        var server = Path.Combine(root, "demo-mcp.exe");
        var project = Directory.CreateDirectory(scratch.In("project")).FullName;
        string[] where = scope is "project" ? ["--scope", "project", "--project", project] : ["--scope", "user"];

        RealClients.SeedClaude(sandbox);
        await File.WriteAllTextAsync(server, "a server");

        string[] register = ["register", "--name", "demo", "--client", "all", .. where, "--owned-root", root, "--env", "DEMO_HOME=secret-value", .. real.Arguments, "--", server, "--stdio"];

        var added = await RunAsync(sandbox, register);

        await Assert.That(Words(added, "action")).IsEqualTo("added added");
        await Assert.That(Words(added, "after", "state")).IsEqualTo("ours ours");
        await Assert.That(added.Output).DoesNotContain("secret-value");

        var status = await RunAsync(sandbox, ["status", "--name", "demo", .. where, "--owned-root", root, .. real.Arguments, "--", server, "--stdio"]);

        await Assert.That(Words(status, "state")).IsEqualTo("ours ours");

        var again = await RunAsync(sandbox, register);

        await Assert.That(Words(again, "action")).IsEqualTo("none none");

        var removed = await RunAsync(sandbox, ["unregister", "--name", "demo", "--client", "all", .. where, "--owned-root", root, .. real.Arguments]);

        await Assert.That(Words(removed, "action")).IsEqualTo("removed removed");
        await Assert.That(Words(removed, "after", "state")).IsEqualTo("absent absent");

        // Codex leaves nothing in a project but its own configuration file.
        if (scope is "project")
        {
            await Assert.That(string.Join(' ', Directory.EnumerateFileSystemEntries(Path.Combine(project, ".codex")).Select(Path.GetFileName))).IsEqualTo("config.toml");
        }

        await Assert.That(await RealClients.RealConfigurationAsync()).DoesNotContain(scratch.Folder);
    }

    /// <summary>
    /// A variable in a command is stored as written by both clients at both scopes: the
    /// read-back compares the stored command with the one given, character for character.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AVariableIsStoredVerbatimAtBothScopes()
    {
        var real = await RealClients.RequireAsync();

        using var scratch = Scratch.Create("verbatim");
        var sandbox = Tool.Sandbox(scratch);
        var project = Directory.CreateDirectory(scratch.In("project")).FullName;
        const string Spelled = "${LOCALAPPDATA}/RegisterAI-Tests/demo-mcp.exe";

        RealClients.SeedClaude(sandbox);

        foreach (var where in new[] { new[] { "--scope", "user" }, ["--scope", "project", "--project", project] })
        {
            var added = await RunAsync(sandbox, ["register", "--name", "demo", "--client", "all", .. where, .. real.Arguments, "--", Spelled]);

            await Assert.That(Words(added, "action")).IsEqualTo("added added");
            await Assert.That(Words(added, "after", "command")).IsEqualTo(Spelled + " " + Spelled);

            var removed = await RunAsync(sandbox, ["unregister", "--name", "demo", "--client", "all", .. where, .. real.Arguments, "--", Spelled]);

            await Assert.That(Words(removed, "action")).IsEqualTo("removed removed");
        }

        await Assert.That(await RealClients.RealConfigurationAsync()).DoesNotContain(scratch.Folder);
    }

    /// <summary>A server path with a non-ASCII letter is stored and read back unchanged by both real clients.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ANonAsciiPathSurvivesTheRealClients()
    {
        var real = await RealClients.RequireAsync();

        using var scratch = Scratch.Create("real-utf8");
        var sandbox = Tool.Sandbox(scratch);
        var root = Directory.CreateDirectory(scratch.In("apps", "D" + "\u00EB" + "mo")).FullName;
        var server = Path.Combine(root, "d" + "\u00EB" + "mo-mcp.exe");

        RealClients.SeedClaude(sandbox);
        await File.WriteAllTextAsync(server, "a server");

        var added = await RunAsync(sandbox, ["register", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", root, .. real.Arguments, "--", server]);

        await Assert.That(Words(added, "action")).IsEqualTo("added added");
        await Assert.That(Words(added, "after", "command")).IsEqualTo(server + " " + server);
        await Assert.That(await RealClients.RealConfigurationAsync()).DoesNotContain(scratch.Folder);
    }

    /// <summary>
    /// Claude Code keeps its user configuration in .config.json when that file exists in
    /// its configuration folder, and the tool reads it there.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ClaudeCodeKeepsItsUserEntriesInConfigJsonWhenThatFileExists()
    {
        var real = await RealClients.RequireAsync();

        using var scratch = Scratch.Create("configjson");
        var sandbox = Tool.Sandbox(scratch);
        var preferred = Path.Combine(sandbox["CLAUDE_CONFIG_DIR"]!, ".config.json");

        RealClients.SeedClaude(sandbox);
        await File.WriteAllTextAsync(preferred, "{\"hasCompletedOnboarding\":true,\"autoUpdates\":false}");

        var added = await RunAsync(sandbox, ["register", "--name", "demo", "--client", "claude-code", "--scope", "user", .. real.Arguments, "--", @"C:\Apps\Demo\demo-mcp.exe"]);

        using var document = Tool.Document(added);

        await Assert.That(Words(added, "action")).IsEqualTo("added");
        await Assert.That(document.RootElement.GetProperty("results")[0].GetProperty("config").GetString()).IsEqualTo(preferred);
        await Assert.That(await File.ReadAllTextAsync(preferred)).Contains("demo-mcp.exe");
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(sandbox["CLAUDE_CONFIG_DIR"]!, ".claude.json"))).DoesNotContain("demo-mcp.exe");
    }

    private static Task<ChildResult> RunAsync(IReadOnlyDictionary<string, string?> sandbox, string[] arguments) =>
        Tool.RunAsync(sandbox, arguments);

    /// <summary>One property of every result, joined: "added added".</summary>
    private static string Words(ChildResult run, params string[] path)
    {
        using var document = Tool.Document(run);

        return string.Join(' ', document.RootElement.GetProperty("results").EnumerateArray().Select(result =>
        {
            var value = path.Aggregate(result, (current, step) => current.GetProperty(step));

            return value.GetString();
        }));
    }

    /// <summary>The parts of a Codex list answer the tool reads: name, transport type, command, args, env names.</summary>
    private static string ListShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        var server = document.RootElement.EnumerateArray().Single();
        var transport = server.GetProperty("transport");

        return string.Join(
            ' ',
            [
                server.GetProperty("name").GetString()!,
                transport.GetProperty("type").GetString()!,
                transport.GetProperty("command").GetString()!,
                .. transport.GetProperty("args").EnumerateArray().Select(argument => argument.GetString()!),
                .. transport.GetProperty("env").EnumerateObject().Select(variable => variable.Name),
            ]);
    }
}
