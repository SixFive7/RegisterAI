// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json;
using System.Text.RegularExpressions;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// The command-line contract, held against the published executable: what each verb
/// writes, the exit codes, the help text and <c>describe</c>, and that the two agree.
/// </summary>
internal sealed partial class ContractTests
{
    /// <summary>Command lines that are not understood, and the verb each error names.</summary>
    /// <returns>The rows.</returns>
    public static IEnumerable<Func<(string[] Arguments, string? Verb)>> UsageErrors()
    {
        yield return () => (["frobnicate"], null);
        yield return () => (["--version", "extra"], null);
        yield return () => (["help", "frobnicate"], "help");
        yield return () => (["status"], "status");
        yield return () => (["status", "--name", "bad name"], "status");
        yield return () => (["status", "--name", "demo", "--client", "cursor"], "status");
        yield return () => (["status", "--name", "demo", "--scope", "project"], "status");
        yield return () => (["status", "--name", "demo", "--project", @"C:\src\repo"], "status");
        yield return () => (["status", "--name", "demo", "--scope", "project", "--project", "relative"], "status");
        yield return () => (["status", "--name", "demo", "--replace"], "status");
        yield return () => (["status", "--name", "demo", "--timeout", "0"], "status");
        yield return () => (["status", "--name", "demo", "--owned-root", "relative"], "status");
        yield return () => (["status", "--name", "demo", "stray"], "status");
        yield return () => (["status", "--name", "demo", "--name", "again"], "status");
        yield return () => (["register", "--name", "demo", "--client", "all", "--scope", "user"], "register");
        yield return () => (["register", "--name", "demo", "--client", "all", "--scope", "user", "--"], "register");
        yield return () => (["register", "--name", "demo", "--scope", "user", "--", "x.exe"], "register");
        yield return () => (["register", "--name", "demo", "--client", "all", "--scope", "user", "--env", "1BAD=x", "--", "x.exe"], "register");
        yield return () => (["register", "--name", "demo", "--client", "all", "--scope", "user", "--client-exe", "cursor=" + @"C:\Tools\cursor.exe", "--", "x.exe"], "register");
        yield return () => (["unregister", "--name", "demo", "--client", "all", "--scope", "user", "--take-over"], "unregister");
        yield return () => (["unregister", "--name", "demo", "--client", "all", "--scope", "user", "--path-folder", @"X:\Demo"], "unregister");
        yield return () => (["status", "--name", "demo", "--path-folder", "relative"], "status");
        yield return () => (["describe", "--name", "demo"], "describe");
        yield return () => (["license", "--", "x"], "license");

        // The path verbs. Every folder below is one no machine has, so not even a
        // parser that let a row through could reach the person's PATH with it.
        yield return () => (["path"], null);
        yield return () => (["path", "frobnicate", @"X:\does-not-exist"], null);
        yield return () => (["path", "add"], "path add");
        yield return () => (["path", "add", "--dry-run"], "path add");
        yield return () => (["path", "add", "relative"], "path add");
        yield return () => (["path", "add", @"X:\does-not-exist"], "path add");
        yield return () => (["path", "add", @"X:\does-not-exist;X:\also-not"], "path add");
        yield return () => (["path", "add", @"X:\does-not-exist", @"X:\also-not"], "path add");
        yield return () => (["path", "add", @"X:\does-not-exist", "--force"], "path add");
        yield return () => (["path", "add", @"X:\does-not-exist", "--dry-run", "--dry-run"], "path add");
        yield return () => (["path", "remove"], "path remove");
        yield return () => (["path", "remove", @"X:\a;X:\b"], "path remove");
        yield return () => (["path", "remove", @"X:\does-not-exist", "--name", "demo"], "path remove");
    }

    /// <summary>--version prints the version and a line feed, and nothing else.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task VersionPrintsTheVersionAndNothingElse()
    {
        using var scratch = Scratch.Create("version");
        var run = await Tool.RunAsync(Tool.Sandbox(scratch), "--version");

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(VersionLine().IsMatch(run.Output)).IsTrue();
        await Assert.That(run.Error).IsEmpty();
    }

    /// <summary>
    /// A command line that is not understood writes one JSON document with exit code 2
    /// and the reason, and runs nothing.
    /// </summary>
    /// <param name="row">The command line and the verb the error names.</param>
    /// <returns>The test.</returns>
    [Test]
    [MethodDataSource(nameof(UsageErrors))]
    public async Task EveryUsageErrorIsOneJsonDocumentWithExitCode2((string[] Arguments, string? Verb) row)
    {
        using var scratch = Scratch.Create("usage");
        var run = await Tool.RunAsync(Tool.Sandbox(scratch), row.Arguments);

        await Assert.That(run.ExitCode).IsEqualTo(2);

        using var document = Tool.Document(run);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("exitCode").GetInt32()).IsEqualTo(2);
        await Assert.That(root.GetProperty("error").GetString()).IsNotNull().And.IsNotEmpty();
        await Assert.That(root.GetProperty("verb").ValueKind is JsonValueKind.Null ? null : root.GetProperty("verb").GetString()).IsEqualTo(row.Verb);
        await Assert.That(root.GetProperty("results").GetArrayLength()).IsEqualTo(0);
    }

    /// <summary>
    /// The help text and describe list the same verbs, options, placeholders, states,
    /// actions, advice codes and exit codes, both ways round.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task HelpAndDescribeAgree()
    {
        using var scratch = Scratch.Create("agree");
        var sandbox = Tool.Sandbox(scratch);
        var help = await Tool.RunAsync(sandbox, "help");
        var described = await Tool.RunAsync(sandbox, "describe");

        await Assert.That(help.ExitCode).IsEqualTo(0);
        await Assert.That(described.ExitCode).IsEqualTo(0);

        using var document = Tool.Document(described);
        var root = document.RootElement;
        var text = help.Output;
        var sections = Sections(text);

        // Every verb, with its usage line and summary.
        foreach (var verb in root.GetProperty("verbs").EnumerateArray())
        {
            var name = verb.GetProperty("name").GetString()!;

            await Assert.That(sections["VERBS"]).Contains("  " + name + " ");
            await Assert.That(Collapse(sections["VERBS"])).Contains(name + " " + verb.GetProperty("summary").GetString());
            await Assert.That(Collapse(sections["USAGE"])).Contains(Collapse(verb.GetProperty("usage").GetString()!));

            // Every option the verb takes is in the OPTIONS section, with the same value.
            foreach (var option in verb.GetProperty("options").EnumerateArray())
            {
                var line = OptionLine(option);

                await Assert.That(sections["OPTIONS"]).Contains("  " + line + " ");
            }
        }

        // The other way: every option help lists is one describe knows.
        var describedOptions = root.GetProperty("verbs").EnumerateArray()
            .SelectMany(verb => verb.GetProperty("options").EnumerateArray())
            .Select(option => option.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (Match listed in OptionInHelp().Matches(sections["OPTIONS"]))
        {
            await Assert.That(describedOptions).Contains(listed.Groups[1].Value);
        }

        await Assert.That(OptionInHelp().Count(sections["OPTIONS"])).IsEqualTo(describedOptions.Count);

        // The published words, every one of them in its section.
        foreach (var (section, property) in new[] { ("STATES", "states"), ("ACTIONS", "actions"), ("ADVICE", "advice") })
        {
            var words = root.GetProperty(property).EnumerateArray().Select(word => word.GetProperty("name").GetString()!).ToList();

            await Assert.That(words.Count).IsGreaterThan(0);

            foreach (var word in words)
            {
                await Assert.That(sections[section]).Contains("  " + word + " ");
            }

            await Assert.That(WordInSection().Count(sections[section])).IsEqualTo(words.Count);
        }

        foreach (var code in root.GetProperty("exitCodes").EnumerateArray())
        {
            await Assert.That(sections["EXIT CODES"]).Contains("  " + code.GetProperty("code").GetInt32() + "  " + code.GetProperty("meaning").GetString());
        }

        // And describe names the schema every other document carries.
        await Assert.That(root.GetProperty("schema").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("output").GetProperty("$schema").GetString()).IsEqualTo("https://json-schema.org/draft/2020-12/schema");
    }

    /// <summary>
    /// help, a verb's --help, help verb, and no arguments at all: text, with the exit
    /// codes 0, 0, 0 and 2.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task HelpIsTextAndNoArgumentsIsAUsageExit()
    {
        using var scratch = Scratch.Create("help");
        var sandbox = Tool.Sandbox(scratch);

        var full = await Tool.RunAsync(sandbox, "help");
        var none = await Tool.RunAsync(sandbox);
        var verb = await Tool.RunAsync(sandbox, "register", "--help");
        var named = await Tool.RunAsync(sandbox, "help", "register");

        await Assert.That(full.ExitCode).IsEqualTo(0);
        await Assert.That(none.ExitCode).IsEqualTo(2);
        await Assert.That(none.Output).IsEqualTo(full.Output);
        await Assert.That(full.Output).StartsWith("RegisterAI ");
        await Assert.That(verb.ExitCode).IsEqualTo(0);
        await Assert.That(verb.Output).IsEqualTo(named.Output);
        await Assert.That(verb.Output).StartsWith("registerai register --name <server>");
        await Assert.That(verb.Output).Contains("--take-over");
        await Assert.That(verb.Output).DoesNotContain("STATES");
        await Assert.That(full.OutputBytes.Contains((byte)'\r')).IsFalse();
    }

    /// <summary>
    /// help path, path --help and help for each path verb: text naming both verbs, the
    /// folder and the dry run.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task HelpCoversBothPathVerbs()
    {
        using var scratch = Scratch.Create("help-path");
        var sandbox = Tool.Sandbox(scratch);

        var both = await Tool.RunAsync(sandbox, "help", "path");
        var asked = await Tool.RunAsync(sandbox, "path", "--help");
        var add = await Tool.RunAsync(sandbox, "help", "path", "add");
        var remove = await Tool.RunAsync(sandbox, "path", "remove", "--help");

        await Assert.That(both.ExitCode).IsEqualTo(0);
        await Assert.That(asked.Output).IsEqualTo(both.Output);
        await Assert.That(both.Output).StartsWith("registerai path add <folder> [--dry-run]");
        await Assert.That(both.Output).Contains("registerai path remove <folder> [--dry-run]");
        await Assert.That(add.ExitCode).IsEqualTo(0);
        await Assert.That(add.Output).StartsWith("registerai path add <folder> [--dry-run]");
        await Assert.That(add.Output).DoesNotContain("path remove <folder>");
        await Assert.That(remove.Output).StartsWith("registerai path remove <folder> [--dry-run]");
    }

    /// <summary>
    /// path add and path remove with --dry-run, against the person's own PATH, which
    /// they read and never write: one document each, the folder, the decision, nothing
    /// announced, and the PATH byte for byte what it was.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ThePathVerbsDecideAgainstTheUsersPathAndADryRunWritesNothing()
    {
        using var scratch = Scratch.Create("path-dry");
        var sandbox = Tool.Sandbox(scratch);
        var folder = Directory.CreateDirectory(scratch.In("never-on-a-path")).FullName;
        var before = UserPathGuard.Reading();

        var add = await Tool.RunAsync(sandbox, "path", "add", folder, "--dry-run");
        var remove = await Tool.RunAsync(sandbox, "path", "remove", "--dry-run", folder);

        await Assert.That(UserPathGuard.Reading()).IsEqualTo(before);

        foreach (var (run, verb, action) in new[] { (add, "path add", "added"), (remove, "path remove", "none") })
        {
            await Assert.That(run.ExitCode).IsEqualTo(0);

            using var document = Tool.Document(run);
            var root = document.RootElement;
            var path = root.GetProperty("path");

            await Assert.That(root.GetProperty("verb").GetString()).IsEqualTo(verb);
            await Assert.That(root.GetProperty("dryRun").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("server").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(root.GetProperty("results").GetArrayLength()).IsEqualTo(0);
            await Assert.That(path.GetProperty("where").GetString()).IsEqualTo(@"HKEY_CURRENT_USER\Environment\Path");
            await Assert.That(path.GetProperty("folder").GetString()).IsEqualTo(folder);
            await Assert.That(path.GetProperty("action").GetString()).IsEqualTo(action);
            await Assert.That(path.GetProperty("announced").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(path.GetProperty("error").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    /// <summary>license prints the repository's LICENSE file, byte for byte.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task LicensePrintsTheLicenseFile()
    {
        using var scratch = Scratch.Create("license");
        var run = await Tool.RunAsync(Tool.Sandbox(scratch), "license");

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Output).IsEqualTo(await File.ReadAllTextAsync(Path.Combine(RepositoryTree.Root, "LICENSE")));
    }

    /// <summary>
    /// The tool never reads stdin: with stdin held open it still finishes, where a
    /// read would wait until the budget ran out.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task StdinIsNeverRead()
    {
        using var scratch = Scratch.Create("stdin");
        var fakes = FakeClients.Install(scratch);

        foreach (var arguments in new[] { new[] { "describe" }, ["status", "--name", "demo", .. fakes.Arguments], ["frobnicate"] })
        {
            var run = await Child.RunAsync(Published.Require(), arguments, TimeSpan.FromSeconds(30), environment: Tool.Sandbox(scratch), holdInputOpen: true);

            await Assert.That(run.TimedOut).IsFalse();
        }
    }

    /// <summary>
    /// The server's command line arrives exactly as given, through two hops of
    /// argument quoting: spaces, quotes, an apostrophe, a non-ASCII letter, variables
    /// in both spellings, an ampersand, an empty argument and a trailing backslash.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task TheCommandLineArrivesUnchanged()
    {
        using var scratch = Scratch.Create("arrives");

        string[] tail =
        [
            @"C:\Apps\De mo\the server's\d" + "\u00EB" + "mo-mcp.exe",
            "${LOCALAPPDATA}/Demo/demo-mcp.exe",
            "%USERNAME%",
            "a&b",
            "say \"hi\"",
            string.Empty,
            @"C:\Apps\Demo\",
            "--stdio",
        ];

        var fakes = FakeClients.Install(scratch);
        var run = await Tool.RunAsync(
            Tool.Sandbox(scratch),
            ["register", "--name", "demo", "--client", "all", "--scope", "user", "--dry-run", "--env", "DEMO_HOME=secret-value", .. fakes.Arguments, "--", .. tail]);

        using var document = Tool.Document(run);
        var server = document.RootElement.GetProperty("server");

        await Assert.That(server.GetProperty("name").GetString()).IsEqualTo("demo");
        await Assert.That(server.GetProperty("command").GetString()).IsEqualTo(tail[0]);
        await Assert.That(server.GetProperty("args").EnumerateArray().Select(argument => argument.GetString()!).ToList()).IsEquivalentTo(tail[1..]);
        await Assert.That(server.GetProperty("env").EnumerateArray().Select(name => name.GetString()!).ToList()).IsEquivalentTo(["DEMO_HOME"]);
        await Assert.That(document.RootElement.GetProperty("dryRun").GetBoolean()).IsTrue();

        // The value went nowhere: not into the document, not into stderr.
        await Assert.That(run.Output).DoesNotContain("secret-value");
        await Assert.That(run.Error).DoesNotContain("secret-value");
    }

    private static Dictionary<string, string> Sections(string help)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? title = null;
        var body = new List<string>();

        foreach (var line in help.Split('\n'))
        {
            if (SectionTitle().IsMatch(line))
            {
                if (title is not null)
                {
                    sections[title] = string.Join('\n', body);
                }

                title = line;
                body.Clear();
            }
            else
            {
                body.Add(line);
            }
        }

        sections[title!] = string.Join('\n', body);

        return sections;
    }

    private static string OptionLine(JsonElement option)
    {
        var name = option.GetProperty("name").GetString()!;

        return option.GetProperty("value").GetString() is { } value
            ? name + " " + string.Join('=', value.Split('=').Select(part => part.Any(char.IsLower) ? "<" + part + ">" : part))
            : name;
    }

    private static string Collapse(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?\n$")]
    private static partial Regex VersionLine();

    [GeneratedRegex("^[A-Z][A-Z ]+$")]
    private static partial Regex SectionTitle();

    [GeneratedRegex(@"^  (--[a-z-]+)", RegexOptions.Multiline)]
    private static partial Regex OptionInHelp();

    [GeneratedRegex(@"^  [a-z][a-z-]+ ", RegexOptions.Multiline)]
    private static partial Regex WordInSection();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
