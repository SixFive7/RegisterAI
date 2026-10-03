// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json.Nodes;
using RegisterAI.FakeClient;

namespace RegisterAI.Tests.Harness;

/// <summary>
/// The in-process fake: the client model answers every call the engine makes, against
/// files in the test's scratch folder, and every call is recorded.
/// </summary>
/// <param name="variables">The environment the clients see, before a call's own variables.</param>
internal sealed class ModelRunner(IReadOnlyDictionary<string, string?> variables) : IRunner
{
    /// <summary>Every call, in order.</summary>
    public List<(string Executable, List<string> Arguments, string Directory, Dictionary<string, string> Environment)> Calls { get; } = [];

    /// <summary>An answer that replaces the model's for the calls it returns one for.</summary>
    public Func<IReadOnlyList<string>, RunOutcome?>? Answer { get; set; }

    /// <summary>Whether every call throws, the way a runner fails when a client cannot be started.</summary>
    public bool Throws { get; set; }

    /// <summary>The verb of each call: add, remove or list.</summary>
    public List<string> Verbs => [.. Calls.Select(call => call.Arguments.Count > 1 ? call.Arguments[1] : "?")];

    /// <inheritdoc/>
    public Task<RunOutcome> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string> environment, TimeSpan budget)
    {
        Calls.Add((executable, [.. arguments], workingDirectory, new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase)));

        if (Throws)
        {
            throw new InvalidOperationException("The fake runner was asked to fail the way a client that cannot be started does.");
        }

        if (Answer?.Invoke(arguments) is { } scripted)
        {
            return Task.FromResult(scripted);
        }

        var outcome = ClientModel.Run(
            executable,
            arguments,
            workingDirectory,
            name => environment.TryGetValue(name, out var forced) ? forced : variables.GetValueOrDefault(name));

        return Task.FromResult(new RunOutcome(outcome.ExitCode, (outcome.Output + outcome.Error).Trim(), false, null));
    }
}

/// <summary>
/// A scratch machine for the decision tests: a profile, both fake clients on its PATH,
/// an owned root holding a server, and the model answering for the clients.
/// </summary>
internal sealed class Bench : IDisposable
{
    private Bench(string label)
    {
        Scratch = Scratch.Create(label);
        Profile = Directory.CreateDirectory(Scratch.In("profile")).FullName;
        Clients = Directory.CreateDirectory(Scratch.In("clients")).FullName;
        Root = Directory.CreateDirectory(Scratch.In("apps", "Demo")).FullName;
        Project = Directory.CreateDirectory(Scratch.In("repo")).FullName;
        Server = Path.Combine(Root, "demo-mcp.exe");
        File.WriteAllText(Server, "a server");
        File.WriteAllText(Path.Combine(Clients, "claude.exe"), "the model answers for it");
        File.WriteAllText(Path.Combine(Clients, "codex.exe"), "the model answers for it");

        Variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = Profile,
            ["CLAUDE_CONFIG_DIR"] = Directory.CreateDirectory(Path.Combine(Profile, ".claude")).FullName,
            ["CODEX_HOME"] = Directory.CreateDirectory(Path.Combine(Profile, ".codex")).FullName,
            ["PATH"] = Clients,
        };

        Runner = new ModelRunner(Variables);
        Machine = new Machine
        {
            Variable = name => Variables.GetValueOrDefault(name),
            Profile = Profile,
            LocalAppData = Path.Combine(Profile, "AppData", "Local"),
            AppData = Path.Combine(Profile, "AppData", "Roaming"),
            NewProgramPath = () => NewProgramPath,
            Runner = Runner,
        };
    }

    /// <summary>The scratch folder.</summary>
    public Scratch Scratch { get; }

    /// <summary>The profile folder.</summary>
    public string Profile { get; }

    /// <summary>The folder both fake clients sit in, which is on PATH.</summary>
    public string Clients { get; }

    /// <summary>The caller's owned root.</summary>
    public string Root { get; }

    /// <summary>A server file under the owned root.</summary>
    public string Server { get; }

    /// <summary>A project folder.</summary>
    public string Project { get; }

    /// <summary>The environment the engine and the clients see.</summary>
    public Dictionary<string, string?> Variables { get; }

    /// <summary>The PATH a new program would get.</summary>
    public List<string> NewProgramPath { get; } = [];

    /// <summary>The fake client runner.</summary>
    public ModelRunner Runner { get; }

    /// <summary>The machine handed to the engine.</summary>
    public Machine Machine { get; }

    /// <summary>Claude Code's user configuration file.</summary>
    public string ClaudeUserFile => Path.Combine(Variables["CLAUDE_CONFIG_DIR"]!, ".claude.json");

    /// <summary>Creates a bench.</summary>
    /// <param name="label">A word that says which test made it.</param>
    /// <returns>The bench.</returns>
    public static Bench Create(string label) => new(label);

    /// <summary>Parses a command line the way the executable does.</summary>
    /// <param name="arguments">The command line.</param>
    /// <returns>The request.</returns>
    public static Request Parse(params string[] arguments) =>
        CommandLine.Parse(arguments) is { Outcome: Outcome.Run, Request: { } request }
            ? request
            : throw new InvalidOperationException($"Not a runnable command line: {string.Join(' ', arguments)}");

    /// <summary>Runs a command line through the engine.</summary>
    /// <param name="arguments">The command line.</param>
    /// <returns>The results.</returns>
    public Task<EngineResult> RunAsync(params string[] arguments) => Engine.RunAsync(Parse(arguments), Machine);

    /// <summary>Writes an entry into a Claude Code configuration file, the way the client writes one.</summary>
    /// <param name="file">The file.</param>
    /// <param name="command">The command.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="extra">Further properties for the entry.</param>
    public static void WriteClaudeEntry(string file, string command, string[]? arguments = null, JsonObject? extra = null)
    {
        var entry = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = command,
            ["args"] = new JsonArray([.. (arguments ?? []).Select(argument => (JsonNode)argument)]),
            ["env"] = new JsonObject(),
        };

        foreach (var (key, value) in extra ?? [])
        {
            entry[key] = value?.DeepClone();
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, new JsonObject { ["mcpServers"] = new JsonObject { ["demo"] = entry } }.ToJsonString());
    }

    /// <summary>Registers an entry with the Codex model directly, as somebody else would have.</summary>
    /// <param name="home">The Codex home.</param>
    /// <param name="command">The command.</param>
    public void WriteCodexEntry(string home, string command)
    {
        var outcome = ClientModel.Run("codex.exe", ["mcp", "add", "demo", "--", command], Profile, name => name is "CODEX_HOME" ? home : Variables.GetValueOrDefault(name));

        if (outcome.ExitCode is not 0)
        {
            throw new InvalidOperationException(outcome.Error);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Scratch.Dispose();
}
