// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RegisterAI.FakeClient;

/// <summary>What one invocation of a client printed and exited with.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="Output">What it wrote to stdout.</param>
/// <param name="Error">What it wrote to stderr.</param>
/// <param name="SleepMilliseconds">How long the process waits before exiting, to provoke a timeout.</param>
internal sealed record ModelOutcome(int ExitCode, string Output, string Error, int SleepMilliseconds = 0);

/// <summary>
/// A model of the two clients' MCP commands, close enough to the real ones that the
/// tool cannot tell them apart by what it asks: Claude Code's <c>mcp add</c> and
/// <c>mcp remove</c> edit the same JSON files the real client edits, and Codex's
/// <c>mcp add</c>, <c>remove</c>, <c>list --json</c> and <c>get --json</c> keep their
/// state in a file of the model's own.
/// </summary>
/// <remarks>
/// <para>
/// The dialect is chosen by the executable's file name, <c>claude.exe</c> or
/// <c>codex.exe</c>, so one build serves both.
/// </para>
/// <para>
/// Two variables steer it. <c>FAKECLIENT_SCRIPT</c> names a JSON file of rules,
/// <c>{"rules":[{"args":["mcp","list"],"exitCode":1,"stdout":"","stderr":"","sleepMs":0}]}</c>;
/// the first rule whose <c>args</c> begin the argument vector answers in place of the
/// model. <c>FAKECLIENT_LOG</c> names a file that gets one JSON line per invocation.
/// </para>
/// </remarks>
internal static class ClientModel
{
    /// <summary>The variable naming the rules file.</summary>
    public const string ScriptVariable = "FAKECLIENT_SCRIPT";

    /// <summary>The variable naming the invocation log.</summary>
    public const string LogVariable = "FAKECLIENT_LOG";

    /// <summary>The file the Codex model keeps its servers in, inside the home.</summary>
    public const string CodexStateFile = "fake-codex-servers.json";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Answers one invocation.</summary>
    /// <param name="executable">The executable's file name, which picks the dialect.</param>
    /// <param name="arguments">The argument vector.</param>
    /// <param name="workingDirectory">Where it runs.</param>
    /// <param name="variable">Reads the environment it runs with.</param>
    /// <returns>What it printed and exited with.</returns>
    public static ModelOutcome Run(string executable, IReadOnlyList<string> arguments, string workingDirectory, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(variable);

        Log(executable, arguments, workingDirectory, variable);

        if (Scripted(arguments, variable) is { } scripted)
        {
            return scripted;
        }

        var name = Path.GetFileNameWithoutExtension(executable).ToUpperInvariant();

        return name switch
        {
            "CLAUDE" => Claude(arguments, workingDirectory, variable),
            "CODEX" => Codex(arguments, variable),
            _ => new ModelOutcome(9, string.Empty, $"The fake client does not know a client named '{executable}'.\n"),
        };
    }

    private static ModelOutcome Claude(IReadOnlyList<string> arguments, string workingDirectory, Func<string, string?> variable)
    {
        if (arguments is ["--version"])
        {
            return new ModelOutcome(0, "2.1.999 (Claude Code)\n", string.Empty);
        }

        if (arguments.Count < 3 || arguments[0] != "mcp" || arguments[1] is not ("add" or "remove"))
        {
            return new ModelOutcome(1, string.Empty, "error: unknown command\n");
        }

        // claude mcp add [options] <name> <command> [args...], where -e takes every
        // value up to the next option: a name written after -e is read as a value.
        var scope = "local";
        var environment = new JsonObject();
        var positional = new List<string>();

        for (var index = 2; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (argument is "--")
            {
                positional.AddRange(arguments.Skip(index + 1));
                break;
            }

            if (argument is "-s" or "--scope")
            {
                scope = index + 1 < arguments.Count ? arguments[++index] : scope;
                continue;
            }

            if (argument is "-e" or "--env")
            {
                while (index + 1 < arguments.Count && !arguments[index + 1].StartsWith('-'))
                {
                    var pair = arguments[++index];
                    var equals = pair.IndexOf('=', StringComparison.Ordinal);

                    if (equals <= 0)
                    {
                        return new ModelOutcome(1, string.Empty, $"Invalid environment variable format: {pair}, environment variables should be added as: -e KEY1=value1 -e KEY2=value2\n");
                    }

                    environment[pair[..equals]] = pair[(equals + 1)..];
                }

                continue;
            }

            positional.Add(argument);
        }

        if (scope is not ("user" or "project"))
        {
            return new ModelOutcome(1, string.Empty, $"The fake client models user and project scope only, not '{scope}'.\n");
        }

        var file = scope is "user"
            ? Path.Combine(variable("CLAUDE_CONFIG_DIR") ?? variable("USERPROFILE") ?? workingDirectory, ".claude.json")
            : Path.Combine(workingDirectory, ".mcp.json");

        if (positional.Count is 0)
        {
            return new ModelOutcome(1, string.Empty, "error: missing required argument 'name'\n");
        }

        var name = positional[0];
        var root = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : [];
        var servers = root["mcpServers"] as JsonObject;

        if (arguments[1] is "remove")
        {
            if (servers is null || !servers.Remove(name))
            {
                // Claude Code 2.1.288 words the two scopes differently.
                return new ModelOutcome(
                    1,
                    string.Empty,
                    scope is "project" ? $"No MCP server named \"{name}\" in .mcp.json\n" : $"No MCP server named \"{name}\" in {scope} scope\n");
            }

            File.WriteAllText(file, root.ToJsonString(Indented) + "\n");

            return new ModelOutcome(0, $"Removed MCP server {name} from {scope} config\nFile modified: {file}\n", string.Empty);
        }

        if (positional.Count < 2)
        {
            return new ModelOutcome(1, string.Empty, "error: missing required argument 'commandOrUrl'\n");
        }

        if (servers is not null && servers.ContainsKey(name))
        {
            return new ModelOutcome(1, string.Empty, $"MCP server {name} already exists in {scope} config\n");
        }

        if (servers is null)
        {
            servers = [];
            root["mcpServers"] = servers;
        }

        servers[name] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = positional[1],
            ["args"] = new JsonArray([.. positional.Skip(2).Select(argument => (JsonNode)argument)]),
            ["env"] = environment,
        };

        _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, root.ToJsonString(Indented) + "\n");

        return new ModelOutcome(
            0,
            $"Added stdio MCP server {name} with command: {positional[1]} {string.Join(' ', positional.Skip(2))} to {scope} config\nFile modified: {file}\n",
            string.Empty);
    }

    private static ModelOutcome Codex(IReadOnlyList<string> arguments, Func<string, string?> variable)
    {
        if (arguments is ["--version"])
        {
            return new ModelOutcome(0, "codex-cli 0.999.0\n", string.Empty);
        }

        var forced = variable("CODEX_HOME");
        var home = forced ?? Path.Combine(variable("USERPROFILE") ?? ".", ".codex");

        if (forced is not null && !Directory.Exists(forced))
        {
            return new ModelOutcome(1, string.Empty, $"CODEX_HOME points to \"{forced}\", but that path does not exist\nError: failed to resolve CODEX_HOME\n");
        }

        _ = Directory.CreateDirectory(home);

        // The residue a real run leaves in its home: two empty folders.
        _ = Directory.CreateDirectory(Path.Combine(home, "tmp", "arg0"));

        var state = Path.Combine(home, CodexStateFile);
        var servers = File.Exists(state) ? JsonNode.Parse(File.ReadAllText(state))!.AsObject() : [];

        switch (arguments)
        {
            case ["mcp", "list", "--json"]:
                return new ModelOutcome(0, new JsonArray([.. servers.Select(entry => Listed(entry.Key, entry.Value!))]).ToJsonString(Indented) + "\n", string.Empty);

            case ["mcp", "get", var name, "--json"]:
                return servers[name] is { } found
                    ? new ModelOutcome(0, Listed(name, found).ToJsonString(Indented) + "\n", string.Empty)
                    : new ModelOutcome(1, string.Empty, $"Error: No MCP server named '{name}' found.\n");

            case ["mcp", "remove", var name]:
                var removed = servers.Remove(name);
                File.WriteAllText(state, servers.ToJsonString(Indented));

                return new ModelOutcome(0, removed ? $"Removed global MCP server '{name}'.\n" : $"No MCP server named '{name}' found.\n", string.Empty);

            case ["mcp", "add", var name, ..]:
                return CodexAdd(servers, state, name, arguments);

            default:
                return new ModelOutcome(2, string.Empty, "error: unrecognized subcommand\n");
        }
    }

    private static ModelOutcome CodexAdd(JsonObject servers, string state, string name, IReadOnlyList<string> arguments)
    {
        var environment = new JsonObject();
        var index = 3;

        for (; index < arguments.Count && arguments[index] is not "--"; index++)
        {
            if (arguments[index] is not "--env" || index + 1 >= arguments.Count)
            {
                return new ModelOutcome(2, string.Empty, $"error: unexpected argument '{arguments[index]}' found\n");
            }

            var pair = arguments[++index];
            var equals = pair.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0)
            {
                return new ModelOutcome(2, string.Empty, $"error: invalid value '{pair}' for '--env <KEY=VALUE>'\n");
            }

            environment[pair[..equals]] = pair[(equals + 1)..];
        }

        if (index + 1 >= arguments.Count)
        {
            return new ModelOutcome(2, string.Empty, "error: the following required arguments were not provided:\n  <COMMAND>...\n");
        }

        servers[name] = new JsonObject
        {
            ["command"] = arguments[index + 1],
            ["args"] = new JsonArray([.. arguments.Skip(index + 2).Select(argument => (JsonNode)argument)]),
            ["env"] = environment.Count > 0 ? environment : null,
        };

        File.WriteAllText(state, servers.ToJsonString(Indented));

        return new ModelOutcome(0, $"Added global MCP server '{name}'.\n", string.Empty);
    }

    private static JsonObject Listed(string name, JsonNode server) => new()
    {
        ["name"] = name,
        ["enabled"] = true,
        ["transport"] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = server["command"]!.GetValue<string>(),
            ["args"] = server["args"]!.DeepClone(),
            ["env"] = server["env"]?.DeepClone(),
            ["env_vars"] = new JsonArray(),
            ["cwd"] = null,
        },
        ["startup_timeout_sec"] = null,
        ["tool_timeout_sec"] = null,
    };

    private static ModelOutcome? Scripted(IReadOnlyList<string> arguments, Func<string, string?> variable)
    {
        if (variable(ScriptVariable) is not { Length: > 0 } script || !File.Exists(script))
        {
            return null;
        }

        var rules = JsonNode.Parse(File.ReadAllText(script))!["rules"]!.AsArray();

        foreach (var rule in rules)
        {
            var prefix = rule!["args"]!.AsArray().Select(part => part!.GetValue<string>()).ToList();

            if (prefix.Count <= arguments.Count && prefix.SequenceEqual(arguments.Take(prefix.Count)))
            {
                return new ModelOutcome(
                    rule["exitCode"]?.GetValue<int>() ?? 0,
                    rule["stdout"]?.GetValue<string>() ?? string.Empty,
                    rule["stderr"]?.GetValue<string>() ?? string.Empty,
                    rule["sleepMs"]?.GetValue<int>() ?? 0);
            }
        }

        return null;
    }

    private static void Log(string executable, IReadOnlyList<string> arguments, string workingDirectory, Func<string, string?> variable)
    {
        if (variable(LogVariable) is not { Length: > 0 } log)
        {
            return;
        }

        var line = new JsonObject
        {
            ["exe"] = Path.GetFileName(executable),
            ["argv"] = new JsonArray([.. arguments.Select(argument => (JsonNode)argument)]),
            ["cwd"] = workingDirectory,
            ["codexHome"] = variable("CODEX_HOME"),
            ["claudeConfigDir"] = variable("CLAUDE_CONFIG_DIR"),
            ["at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };

        File.AppendAllText(log, line.ToJsonString() + "\n");
    }
}
