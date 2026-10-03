// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// The published executable against the fake client, out of process: golden documents,
/// a repeat, a timeout, a non-ASCII path through every hop, and the documents' shape
/// against the schema describe publishes.
/// </summary>
internal sealed class EndToEndTests
{
    /// <summary>
    /// Register, status and unregister, each against a committed golden document with the
    /// scratch folder and the version replaced by placeholders.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task RegisterStatusAndUnregisterMatchTheirGoldenDocuments()
    {
        using var scratch = Scratch.Create("golden");
        var fakes = FakeClients.Install(scratch);
        var sandbox = Tool.Sandbox(scratch);
        var root = Directory.CreateDirectory(scratch.In("apps", "Demo")).FullName;
        var server = Path.Combine(root, "demo-mcp.exe");
        await File.WriteAllTextAsync(server, "a server");

        string[][] runs =
        [
            ["register", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", root, "--env", "DEMO_HOME=secret-value", .. fakes.Arguments, "--", server, "--stdio"],
            ["status", "--name", "demo", "--owned-root", root, .. fakes.Arguments, "--", server, "--stdio"],
            ["unregister", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", root, .. fakes.Arguments],
        ];

        foreach (var arguments in runs)
        {
            var run = await Tool.RunAsync(sandbox, arguments);

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(run.Output).DoesNotContain("secret-value");

            using var document = Tool.Document(run);
            var normal = Normalize(run.Output, scratch.Folder);
            var golden = Path.Combine(RepositoryTree.Root, "tests", "RegisterAI.Tests", "Golden", arguments[0] + ".json");

            if (!File.Exists(golden))
            {
                await File.WriteAllTextAsync(golden, normal);
                throw new InvalidOperationException($"'{golden}' did not exist and was written from this run. Read it, and commit it if it is right.");
            }

            await Assert.That(normal).IsEqualTo(await File.ReadAllTextAsync(golden));
        }
    }

    /// <summary>A second register changes nothing, runs only the reads, and exits 0.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ARepeatedRegisterChangesNothing()
    {
        using var scratch = Scratch.Create("repeat2");
        var fakes = FakeClients.Install(scratch);
        var sandbox = Tool.Sandbox(scratch);
        var server = Path.Combine(Directory.CreateDirectory(scratch.In("apps")).FullName, "demo-mcp.exe");
        await File.WriteAllTextAsync(server, "a server");

        string[] register = ["register", "--name", "demo", "--client", "all", "--scope", "user", .. fakes.Arguments, "--", server];

        _ = await Tool.RunAsync(sandbox, register);
        var again = await Tool.RunAsync(sandbox, register);

        using var document = Tool.Document(again);
        var results = document.RootElement.GetProperty("results");

        await Assert.That(again.ExitCode).IsEqualTo(0);
        await Assert.That(results[0].GetProperty("action").GetString()).IsEqualTo("none");
        await Assert.That(results[1].GetProperty("action").GetString()).IsEqualTo("none");
        await Assert.That(results[0].GetProperty("ran").GetArrayLength()).IsEqualTo(0);
        await Assert.That(results[1].GetProperty("ran").GetArrayLength()).IsEqualTo(1);
    }

    /// <summary>
    /// A client that hangs is stopped when the run's budget is spent: the run fails with
    /// exit code 1 well before the client would have finished.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task AHangingClientIsStoppedWhenTheBudgetIsSpent()
    {
        using var scratch = Scratch.Create("timeout");
        var fakes = FakeClients.Install(scratch);
        var sandbox = Tool.Sandbox(scratch);
        var script = scratch.In("script.json");

        await File.WriteAllTextAsync(script, "{\"rules\":[{\"args\":[\"mcp\",\"add\"],\"sleepMs\":60000}]}");
        sandbox["FAKECLIENT_SCRIPT"] = script;

        var clock = Stopwatch.StartNew();
        var run = await Tool.RunAsync(sandbox, ["register", "--name", "demo", "--client", "claude-code", "--scope", "user", "--timeout", "3", .. fakes.Arguments, "--", @"C:\Apps\Demo\demo-mcp.exe"]);
        clock.Stop();

        using var document = Tool.Document(run);
        var result = document.RootElement.GetProperty("results")[0];

        await Assert.That(run.ExitCode).IsEqualTo(1);
        await Assert.That(result.GetProperty("action").GetString()).IsEqualTo("failed");
        await Assert.That(result.GetProperty("ran")[0].GetProperty("timedOut").GetBoolean()).IsTrue();
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(50));
    }

    /// <summary>
    /// A server path with a non-ASCII letter is written and read back unchanged by both
    /// clients: through the argument vector, Claude Code's file, Codex's list output and
    /// the document.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ANonAsciiPathSurvivesEveryHop()
    {
        using var scratch = Scratch.Create("utf8");
        var fakes = FakeClients.Install(scratch);
        var sandbox = Tool.Sandbox(scratch);
        var root = Directory.CreateDirectory(scratch.In("apps", "D" + "\u00EB" + "mo")).FullName;
        var server = Path.Combine(root, "d" + "\u00EB" + "mo-mcp.exe");
        await File.WriteAllTextAsync(server, "a server");

        var run = await Tool.RunAsync(sandbox, ["register", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", root, .. fakes.Arguments, "--", server]);

        using var document = Tool.Document(run);

        await Assert.That(run.ExitCode).IsEqualTo(0);

        foreach (var result in document.RootElement.GetProperty("results").EnumerateArray())
        {
            await Assert.That(result.GetProperty("action").GetString()).IsEqualTo("added");
            await Assert.That(result.GetProperty("after").GetProperty("state").GetString()).IsEqualTo("ours");
            await Assert.That(result.GetProperty("after").GetProperty("command").GetString()).IsEqualTo(server);
        }
    }

    /// <summary>
    /// The documents hold the shape describe publishes: the keys each object must have
    /// and may have, and every published word from its list.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheDocumentsHoldTheShapeDescribePublishes()
    {
        using var scratch = Scratch.Create("shape");
        var fakes = FakeClients.Install(scratch);
        var sandbox = Tool.Sandbox(scratch);
        var described = await Tool.RunAsync(sandbox, "describe");

        using var describe = Tool.Document(described);
        var schema = describe.RootElement.GetProperty("output");
        var definitions = schema.GetProperty("$defs");

        string[][] runs =
        [
            ["register", "--name", "demo", "--client", "all", "--scope", "user", .. fakes.Arguments, "--", @"C:\Apps\Demo\demo-mcp.exe", "--stdio"],
            ["register", "--name", "demo", "--client", "all", "--scope", "user", "--dry-run", .. fakes.Arguments, "--", @"C:\Apps\Demo\demo-mcp.exe"],
            ["status", "--name", "demo", .. fakes.Arguments],
            ["unregister", "--name", "demo", "--client", "all", "--scope", "user", .. fakes.Arguments, "--", @"C:\Apps\Demo\demo-mcp.exe"],
            ["status", "--name", "bad name"],
        ];

        foreach (var arguments in runs)
        {
            using var document = Tool.Document(await Tool.RunAsync(sandbox, arguments));
            var root = document.RootElement;

            await Assert.That(Keys(root)).IsEqualTo(Required(schema));

            foreach (var result in root.GetProperty("results").EnumerateArray())
            {
                var kind = arguments[0] is "status" ? "statusResult" : "changeResult";
                var expected = Required(definitions.GetProperty("place")) + " " + Required(definitions.GetProperty(kind));

                await Assert.That(Keys(result)).IsEqualTo(string.Join(' ', expected.Split(' ').Order(StringComparer.Ordinal)));
                await Assert.That(Words(definitions.GetProperty("place").GetProperty("properties").GetProperty("client"))).Contains(result.GetProperty("client").GetString()!);

                foreach (var advice in result.GetProperty("advice").EnumerateArray())
                {
                    await Assert.That(Keys(advice)).IsEqualTo(Required(definitions.GetProperty("advice")));
                    await Assert.That(Words(definitions.GetProperty("advice").GetProperty("properties").GetProperty("code"))).Contains(advice.GetProperty("code").GetString()!);
                }

                if (kind is "changeResult")
                {
                    foreach (var entry in new[] { result.GetProperty("before"), result.GetProperty("after") })
                    {
                        await Assert.That(Keys(entry)).IsEqualTo(Required(definitions.GetProperty("entry")));
                        await Assert.That(Words(definitions.GetProperty("entry").GetProperty("properties").GetProperty("state"))).Contains(entry.GetProperty("state").GetString()!);
                    }

                    await Assert.That(Words(definitions.GetProperty("changeResult").GetProperty("properties").GetProperty("action"))).Contains(result.GetProperty("action").GetString()!);

                    foreach (var ran in result.GetProperty("ran").EnumerateArray())
                    {
                        await Assert.That(Keys(ran)).IsEqualTo(Required(definitions.GetProperty("ran")));
                    }
                }
                else
                {
                    await Assert.That(Words(definitions.GetProperty("entry").GetProperty("properties").GetProperty("state"))).Contains(result.GetProperty("state").GetString()!);
                }
            }
        }
    }

    /// <summary>The document with the scratch folder and the version replaced by placeholders.</summary>
    private static string Normalize(string output, string scratch)
    {
        var escaped = JsonSerializer.Serialize(scratch)[1..^1];
        var node = JsonNode.Parse(output)!;

        node["version"] = "<version>";

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .Replace(escaped, "<scratch>", StringComparison.OrdinalIgnoreCase)
            .ReplaceLineEndings("\n") + "\n";
    }

    private static string Keys(JsonElement element) =>
        string.Join(' ', element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

    private static string Required(JsonElement schema) =>
        string.Join(' ', schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()!).Order(StringComparer.Ordinal));

    private static List<string> Words(JsonElement property) =>
        [.. property.GetProperty("enum").EnumerateArray().Select(word => word.GetString()!)];
}
