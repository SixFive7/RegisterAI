// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Text.RegularExpressions;

namespace RegisterAI;

/// <summary>The verbs.</summary>
internal enum Verb
{
    /// <summary>Report what is registered.</summary>
    Status,

    /// <summary>Make the entry exist.</summary>
    Register,

    /// <summary>Remove the caller's own entry.</summary>
    Unregister,

    /// <summary>The contract as JSON.</summary>
    Describe,

    /// <summary>The licence terms.</summary>
    License,

    /// <summary>The contract as text.</summary>
    Help,
}

/// <summary>Whether a verb takes the server's command line after <c>--</c>.</summary>
internal enum Tail
{
    /// <summary>No command line.</summary>
    None,

    /// <summary>A command line may follow.</summary>
    Optional,

    /// <summary>A command line must follow.</summary>
    Required,
}

/// <summary>One option in the table that feeds the parser, the help text and <c>describe</c>.</summary>
/// <param name="Name">The option, with its dashes.</param>
/// <param name="Value">The value's placeholder, or null for a flag.</param>
/// <param name="Summary">What it does, in one or two sentences.</param>
/// <param name="Repeatable">Whether it may be given more than once.</param>
/// <param name="Values">The values it accepts, when that is a fixed list.</param>
internal sealed record OptionSpec(string Name, string? Value, string Summary, bool Repeatable = false, IReadOnlyList<string>? Values = null);

/// <summary>One verb in the table.</summary>
/// <param name="Verb">The verb.</param>
/// <param name="Name">Its word.</param>
/// <param name="Writes">Whether it can change a client's configuration.</param>
/// <param name="Summary">What it does, in one line.</param>
/// <param name="Usage">Its usage line, after the tool's name.</param>
/// <param name="Required">The options it requires.</param>
/// <param name="Optional">The options it accepts besides those.</param>
/// <param name="Tail">Whether a command line follows <c>--</c>.</param>
/// <param name="Examples">Example argument vectors, after the tool's name.</param>
internal sealed record VerbSpec(
    Verb Verb,
    string Name,
    bool Writes,
    string Summary,
    string Usage,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Optional,
    Tail Tail,
    IReadOnlyList<IReadOnlyList<string>> Examples);

/// <summary>A command line that was understood.</summary>
internal sealed record Request
{
    /// <summary>The verb.</summary>
    public required Verb Verb { get; init; }

    /// <summary>The server's name in the client.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The clients asked about, in the fixed order, each once.</summary>
    public IReadOnlyList<ClientId> Clients { get; init; } = [];

    /// <summary>Whether the clients were asked for as <c>all</c>, which changes when exit code 5 applies.</summary>
    public bool AllClients { get; init; }

    /// <summary>The scope.</summary>
    public Scope Scope { get; init; }

    /// <summary>The project folder, for project scope.</summary>
    public string? Project { get; init; }

    /// <summary>Folders under which a command is the caller's own.</summary>
    public IReadOnlyList<string> OwnedRoots { get; init; } = [];

    /// <summary>Environment variables for the server, in the order given.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Environment { get; init; } = [];

    /// <summary>Rewrite the caller's own entry even when it matches.</summary>
    public bool Replace { get; init; }

    /// <summary>Replace a foreign entry.</summary>
    public bool TakeOver { get; init; }

    /// <summary>Decide and report, and run nothing that writes.</summary>
    public bool DryRun { get; init; }

    /// <summary>The budget for the whole run.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(CommandLine.DefaultTimeoutSeconds);

    /// <summary>Executables named with <c>--client-exe</c>.</summary>
    public IReadOnlyDictionary<ClientId, string> ClientExecutables { get; init; } = new Dictionary<ClientId, string>();

    /// <summary>The server's command, or null when none followed <c>--</c>.</summary>
    public string? Command { get; init; }

    /// <summary>The server's arguments.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

/// <summary>What a command line asks for.</summary>
internal enum Outcome
{
    /// <summary>Run a verb.</summary>
    Run,

    /// <summary>Print the version.</summary>
    Version,

    /// <summary>Print help text.</summary>
    Help,

    /// <summary>Report a usage error.</summary>
    Usage,
}

/// <summary>The parser's answer.</summary>
/// <param name="Outcome">What to do.</param>
/// <param name="Request">The request, for <see cref="Outcome.Run"/>.</param>
/// <param name="Verb">The verb the text or error is about, when one was recognised.</param>
/// <param name="Error">The usage error.</param>
/// <param name="ExitCode">The exit code for help text: 0 when asked for, 2 when no arguments were given.</param>
internal sealed record Parsed(Outcome Outcome, Request? Request = null, VerbSpec? Verb = null, string? Error = null, int ExitCode = 0);

/// <summary>
/// The command-line table and its parser. One table feeds parsing, the help text and
/// <c>describe</c>, so the three cannot disagree.
/// </summary>
internal static partial class CommandLine
{
    /// <summary>The default budget, in seconds.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>The longest budget accepted, in seconds.</summary>
    public const int MaximumTimeoutSeconds = 3600;

    /// <summary>Every option, in the order help lists them.</summary>
    public static IReadOnlyList<OptionSpec> Options { get; } =
    [
        new("--name", "server", "The server's name in the client. Letters, digits, '-' and '_'."),
        new("--client", "id", "claude-code, codex or all. Repeatable. Default for status: all.", Repeatable: true, Values: ["claude-code", "codex", "all"]),
        new("--scope", "scope", "user (every project of this Windows user) or project (one folder). Default for status: user.", Values: ["user", "project"]),
        new("--project", "dir", "The folder for --scope project. Required with it; never guessed."),
        new("--owned-root", "dir", "An entry whose command resolves to a file under <dir> is the caller's own. Repeatable. An entry naming exactly <command> is also the caller's own. Every other entry is foreign and is never changed.", Repeatable: true),
        new("--env", "KEY=VALUE", "An environment variable for the server. Repeatable. Values are never echoed.", Repeatable: true),
        new("--replace", null, "Rewrite the caller's own entry even when it already matches."),
        new("--take-over", null, "Replace a foreign entry. Pass it only when a person has just asked for exactly that."),
        new("--dry-run", null, "Decide and report; run nothing that writes."),
        new("--timeout", "seconds", "Budget for the whole run. Default 30."),
        new("--client-exe", "id=path", "Use this executable for a client, in place of the search. Repeatable.", Repeatable: true),
    ];

    /// <summary>Every verb, in the order help lists them.</summary>
    public static IReadOnlyList<VerbSpec> Verbs { get; } =
    [
        new(
            Verb.Status,
            "status",
            Writes: false,
            "Report, per client, whether <server> is registered and whose entry it is. Reads only.",
            "--name <server> [options] [-- <command> [args...]]",
            ["--name"],
            ["--client", "--scope", "--project", "--owned-root", "--timeout", "--client-exe"],
            Tail.Optional,
            [["status", "--name", "demo"], ["status", "--name", "demo", "--owned-root", @"C:\Apps\Demo", "--", @"C:\Apps\Demo\demo-mcp.exe"]]),
        new(
            Verb.Register,
            "register",
            Writes: true,
            "Make the entry exist and name <command>. Safe to repeat.",
            "--name <server> --client <id> --scope <scope> [options] -- <command> [args...]",
            ["--name", "--client", "--scope"],
            ["--project", "--owned-root", "--env", "--replace", "--take-over", "--dry-run", "--timeout", "--client-exe"],
            Tail.Required,
            [
                ["register", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", @"C:\Apps\Demo", "--", @"C:\Apps\Demo\demo-mcp.exe"],
                ["register", "--name", "demo", "--client", "claude-code", "--scope", "project", "--project", @"C:\src\repo", "--", "${LOCALAPPDATA}/Demo/demo-mcp.exe"],
            ]),
        new(
            Verb.Unregister,
            "unregister",
            Writes: true,
            "Remove the entry, only if it is the caller's own. Safe to repeat.",
            "--name <server> --client <id> --scope <scope> [options] [-- <command> [args...]]",
            ["--name", "--client", "--scope"],
            ["--project", "--owned-root", "--dry-run", "--timeout", "--client-exe"],
            Tail.Optional,
            [["unregister", "--name", "demo", "--client", "all", "--scope", "user", "--owned-root", @"C:\Apps\Demo"]]),
        new(Verb.Describe, "describe", Writes: false, "Print the verbs, options, clients, exit codes and output schema as JSON.", string.Empty, [], [], Tail.None, [["describe"]]),
        new(Verb.License, "license", Writes: false, "Print the licence terms.", string.Empty, [], [], Tail.None, [["license"]]),
        new(Verb.Help, "help", Writes: false, "Print this text, or one verb's part of it.", "[verb]", [], [], Tail.None, [["help", "register"]]),
    ];

    /// <summary>An option's row.</summary>
    /// <param name="name">The option, with its dashes.</param>
    /// <returns>The row.</returns>
    public static OptionSpec Option(string name) => Options.Single(option => option.Name == name);

    /// <summary>Reads a command line.</summary>
    /// <param name="args">The arguments, as the process received them.</param>
    /// <returns>What they ask for.</returns>
    public static Parsed Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count is 0)
        {
            return new Parsed(Outcome.Help, ExitCode: 2);
        }

        var first = args[0];

        if (first is "--version")
        {
            return args.Count is 1 ? new Parsed(Outcome.Version) : Usage(null, "--version takes nothing after it.");
        }

        if (first is "--help" or "-h")
        {
            return args.Count is 1 ? new Parsed(Outcome.Help) : Usage(null, $"{first} takes nothing after it. For one verb's help, run 'registerai help <verb>'.");
        }

        if (Verbs.FirstOrDefault(verb => verb.Name == first) is not { } spec)
        {
            return Usage(null, $"'{first}' is not a verb. The verbs are {string.Join(", ", Verbs.Select(verb => verb.Name))}. Run 'registerai help'.");
        }

        if (spec.Verb is Verb.Help)
        {
            return args.Count switch
            {
                1 => new Parsed(Outcome.Help),
                2 when Verbs.FirstOrDefault(verb => verb.Name == args[1]) is { } about => new Parsed(Outcome.Help, Verb: about),
                2 => Usage(spec, $"'{args[1]}' is not a verb. The verbs are {string.Join(", ", Verbs.Select(verb => verb.Name))}."),
                _ => Usage(spec, "help takes at most one verb."),
            };
        }

        return ParseVerb(spec, args);
    }

    private static Parsed ParseVerb(VerbSpec spec, IReadOnlyList<string> args)
    {
        var given = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? tail = null;

        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];

            if (argument is "--")
            {
                tail = [.. args.Skip(index + 1)];
                break;
            }

            if (argument is "--help" or "-h")
            {
                return new Parsed(Outcome.Help, Verb: spec);
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                return Usage(spec, $"'{argument}' is not an option. Options come first; the server's command line goes after --.");
            }

            var equals = argument.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? argument : argument[..equals];
            var inline = equals < 0 ? null : argument[(equals + 1)..];

            if (Options.FirstOrDefault(option => option.Name == name) is not { } option)
            {
                return Usage(spec, $"'{name}' is not an option. Run 'registerai help {spec.Name}'.");
            }

            if (!spec.Required.Contains(name) && !spec.Optional.Contains(name))
            {
                return Usage(spec, $"{name} is not an option of {spec.Name}. Run 'registerai help {spec.Name}'.");
            }

            string value;

            if (option.Value is null)
            {
                if (inline is not null)
                {
                    return Usage(spec, $"{name} takes no value.");
                }

                value = "true";
            }
            else if (inline is not null)
            {
                value = inline;
            }
            else if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++index];
            }
            else
            {
                return Usage(spec, $"{name} needs a value: {name} <{option.Value}>.");
            }

            if (!given.TryGetValue(name, out var values))
            {
                values = [];
                given[name] = values;
            }
            else if (!option.Repeatable)
            {
                return Usage(spec, $"{name} was given more than once.");
            }

            values.Add(value);
        }

        if (tail is not null && spec.Tail is Tail.None)
        {
            return Usage(spec, $"{spec.Name} takes no command line.");
        }

        if (spec.Tail is Tail.Required && (tail is null || tail.Count is 0 || tail[0].Length is 0))
        {
            return Usage(spec, $"{spec.Name} needs the server's command line after --: registerai {spec.Name} {spec.Usage}");
        }

        if (tail is { Count: > 0 } && tail[0].Length is 0)
        {
            return Usage(spec, "The command after -- is empty.");
        }

        if (spec.Required.FirstOrDefault(required => !given.ContainsKey(required)) is { } missing)
        {
            return Usage(spec, $"{spec.Name} needs {missing} <{Option(missing).Value}>.");
        }

        return spec.Verb is Verb.Describe or Verb.License
            ? new Parsed(Outcome.Run, new Request { Verb = spec.Verb }, spec)
            : Validate(spec, given, tail ?? []);
    }

    private static Parsed Validate(VerbSpec spec, Dictionary<string, List<string>> given, List<string> tail)
    {
        string? one(string name) => given.TryGetValue(name, out var values) ? values[0] : null;
        IReadOnlyList<string> many(string name) => given.TryGetValue(name, out var values) ? values : [];

        var name = one("--name")!;

        if (!ServerName().IsMatch(name))
        {
            return Usage(spec, $"'{name}' is not a server name. Use letters, digits, '-' and '_'.");
        }

        var clientWords = many("--client");
        var unknownClient = clientWords.FirstOrDefault(word => word is not "all" && Clients.All.All(client => client.Word != word));

        if (unknownClient is not null)
        {
            return Usage(spec, $"'{unknownClient}' is not a client. Use claude-code, codex or all.");
        }

        var all = clientWords.Count is 0 || clientWords.Contains("all");
        IReadOnlyList<ClientId> clients = all
            ? [.. Clients.All.Select(client => client.Id)]
            : [.. Clients.All.Where(client => clientWords.Contains(client.Word)).Select(client => client.Id)];

        var scopeWord = one("--scope") ?? "user";

        if (scopeWord is not ("user" or "project"))
        {
            return Usage(spec, $"'{scopeWord}' is not a scope. Use user or project.");
        }

        var scope = scopeWord is "user" ? Scope.User : Scope.Project;
        var project = one("--project");

        if (scope is Scope.Project && project is null)
        {
            return Usage(spec, "--scope project needs --project <dir>. The folder is never guessed.");
        }

        if (scope is Scope.User && project is not null)
        {
            return Usage(spec, "--project is only for --scope project.");
        }

        if (project is not null)
        {
            if (!Path.IsPathFullyQualified(project))
            {
                return Usage(spec, $"--project '{project}' is not a full path.");
            }

            if (!Directory.Exists(project))
            {
                return Usage(spec, $"--project '{project}' is not a folder that exists.");
            }

            project = Path.GetFullPath(project);
        }

        var roots = new List<string>();

        foreach (var root in many("--owned-root"))
        {
            if (!Path.IsPathFullyQualified(root))
            {
                return Usage(spec, $"--owned-root '{root}' is not a full path.");
            }

            roots.Add(Path.GetFullPath(root));
        }

        var environment = new List<KeyValuePair<string, string>>();

        foreach (var pair in many("--env"))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? pair : pair[..equals];

            if (equals < 0 || !EnvironmentName().IsMatch(key))
            {
                return Usage(spec, "--env needs KEY=VALUE, with a KEY of letters, digits and '_' that does not start with a digit.");
            }

            if (environment.Any(existing => string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                return Usage(spec, $"--env {key} was given more than once.");
            }

            environment.Add(new(key, pair[(equals + 1)..]));
        }

        var timeout = DefaultTimeoutSeconds;

        if (one("--timeout") is { } seconds
            && (!int.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout < 1 || timeout > MaximumTimeoutSeconds))
        {
            return Usage(spec, $"--timeout '{seconds}' is not a whole number of seconds from 1 to {MaximumTimeoutSeconds}.");
        }

        var executables = new Dictionary<ClientId, string>();

        foreach (var pair in many("--client-exe"))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var word = equals < 0 ? pair : pair[..equals];
            var path = equals < 0 ? string.Empty : pair[(equals + 1)..];

            if (Clients.All.FirstOrDefault(client => client.Word == word) is not { } client)
            {
                return Usage(spec, $"--client-exe needs <id>=<path> with an id of claude-code or codex; '{word}' is not one.");
            }

            if (!Path.IsPathFullyQualified(path))
            {
                return Usage(spec, $"--client-exe {word}= needs a full path to the executable.");
            }

            if (!executables.TryAdd(client.Id, Path.GetFullPath(path)))
            {
                return Usage(spec, $"--client-exe {word} was given more than once.");
            }
        }

        return new Parsed(
            Outcome.Run,
            new Request
            {
                Verb = spec.Verb,
                Name = name,
                Clients = clients,
                AllClients = all,
                Scope = scope,
                Project = project,
                OwnedRoots = roots,
                Environment = environment,
                Replace = given.ContainsKey("--replace"),
                TakeOver = given.ContainsKey("--take-over"),
                DryRun = given.ContainsKey("--dry-run"),
                Timeout = TimeSpan.FromSeconds(timeout),
                ClientExecutables = executables,
                Command = tail.Count > 0 ? tail[0] : null,
                Arguments = [.. tail.Skip(1)],
            },
            spec);
    }

    private static Parsed Usage(VerbSpec? spec, string error) => new(Outcome.Usage, Verb: spec, Error: error);

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex ServerName();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvironmentName();
}
