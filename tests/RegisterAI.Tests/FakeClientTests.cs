// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// The fake client speaks each client's dialect as it was measured: Claude Code exits 1
/// on a duplicate add and on removing nothing, Codex exits 0 on both and lists its
/// servers as a JSON array. The real-client tests hold the same facts against the real
/// clients, so a client that changes shows up as a disagreement and not as a fake that
/// quietly drifted.
/// </summary>
internal sealed class FakeClientTests
{
    /// <summary>Claude Code's add, duplicate add, remove and remove of nothing, at both scopes.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheClaudeDialectRefusesADuplicateAndAMissingNameWithExit1()
    {
        using var scratch = Scratch.Create("fake-claude");
        var fakes = FakeClients.Install(scratch);
        var sandbox = Tool.Sandbox(scratch);
        var project = Directory.CreateDirectory(scratch.In("project")).FullName;

        foreach (var (scope, directory, file) in new[]
        {
            ("user", scratch.Folder, Path.Combine(sandbox["CLAUDE_CONFIG_DIR"]!, ".claude.json")),
            ("project", project, Path.Combine(project, ".mcp.json")),
        })
        {
            var added = await RunAsync(fakes.Claude, sandbox, directory, "mcp", "add", "demo", "--scope", scope, "--", @"C:\Apps\Demo\demo-mcp.exe", "--stdio");
            var duplicate = await RunAsync(fakes.Claude, sandbox, directory, "mcp", "add", "demo", "--scope", scope, "--", @"C:\Apps\Demo\demo-mcp.exe");

            await Assert.That(added.ExitCode).IsEqualTo(0);
            await Assert.That(duplicate.ExitCode).IsEqualTo(1);
            await Assert.That(duplicate.Error).Contains("already exists");

            using (var written = JsonDocument.Parse(await File.ReadAllTextAsync(file)))
            {
                var entry = written.RootElement.GetProperty("mcpServers").GetProperty("demo");

                await Assert.That(entry.GetProperty("command").GetString()).IsEqualTo(@"C:\Apps\Demo\demo-mcp.exe");
                await Assert.That(entry.GetProperty("args")[0].GetString()).IsEqualTo("--stdio");
            }

            var removed = await RunAsync(fakes.Claude, sandbox, directory, "mcp", "remove", "demo", "--scope", scope);
            var nothing = await RunAsync(fakes.Claude, sandbox, directory, "mcp", "remove", "demo", "--scope", scope);

            await Assert.That(removed.ExitCode).IsEqualTo(0);
            await Assert.That(nothing.ExitCode).IsEqualTo(1);
            await Assert.That(nothing.Error).Contains("No MCP server named");
        }
    }

    /// <summary>Codex's add, list, remove and refusal of a home that does not exist.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheCodexDialectIsIdempotentAndListsJson()
    {
        using var scratch = Scratch.Create("fake-codex");
        var fakes = FakeClients.Install(scratch);
        var sandbox = Tool.Sandbox(scratch);

        await Assert.That((await RunAsync(fakes.Codex, sandbox, scratch.Folder, "mcp", "add", "demo", "--env", "A=1", "--", @"C:\Apps\Demo\demo-mcp.exe", "--stdio")).ExitCode).IsEqualTo(0);
        await Assert.That((await RunAsync(fakes.Codex, sandbox, scratch.Folder, "mcp", "add", "demo", "--", @"C:\Apps\Demo\demo-mcp.exe")).ExitCode).IsEqualTo(0);

        var listed = await RunAsync(fakes.Codex, sandbox, scratch.Folder, "mcp", "list", "--json");

        using (var document = JsonDocument.Parse(listed.Output))
        {
            var transport = document.RootElement[0].GetProperty("transport");

            await Assert.That(document.RootElement.GetArrayLength()).IsEqualTo(1);
            await Assert.That(document.RootElement[0].GetProperty("name").GetString()).IsEqualTo("demo");
            await Assert.That(transport.GetProperty("command").GetString()).IsEqualTo(@"C:\Apps\Demo\demo-mcp.exe");
        }

        await Assert.That((await RunAsync(fakes.Codex, sandbox, scratch.Folder, "mcp", "remove", "demo")).ExitCode).IsEqualTo(0);
        await Assert.That((await RunAsync(fakes.Codex, sandbox, scratch.Folder, "mcp", "remove", "demo")).ExitCode).IsEqualTo(0);

        // A home that does not exist is refused for every verb, and nothing is created.
        var missing = scratch.In("no-such-home");
        var refused = await RunAsync(fakes.Codex, new Dictionary<string, string?>(sandbox) { ["CODEX_HOME"] = missing }, scratch.Folder, "mcp", "list", "--json");

        await Assert.That(refused.ExitCode).IsEqualTo(1);
        await Assert.That(refused.Error).Contains("does not exist");
        await Assert.That(Directory.Exists(missing)).IsFalse();
    }

    private static Task<ChildResult> RunAsync(string client, IReadOnlyDictionary<string, string?> environment, string directory, params string[] arguments) =>
        Child.RunAsync(client, arguments, TimeSpan.FromSeconds(60), directory, environment);
}
