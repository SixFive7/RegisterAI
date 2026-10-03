// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text;

namespace RegisterAI;

/// <summary>The help text, rendered from the same tables the parser and <c>describe</c> read.</summary>
internal static class HelpText
{
    private const int Width = 100;
    private const int OptionColumn = 28;

    /// <summary>The whole contract, as a person reads it.</summary>
    /// <returns>The text, ending in a line feed.</returns>
    public static string Full()
    {
        var text = new StringBuilder();

        _ = text.Append("RegisterAI ").Append(ToolVersion.Text).Append('\n')
            .Append("Registers a local MCP server with coding agents by running each agent's own command.\n")
            .Append("It never edits an agent's configuration file, never prompts, and never reads stdin.\n")
            .Append("It changes the user PATH only when asked to, with path add and path remove.\n");

        Section(text, "USAGE");
        foreach (var verb in CommandLine.Verbs)
        {
            _ = text.Append(("  registerai " + verb.Name.PadRight(10) + " " + verb.Usage).TrimEnd()).Append('\n');
        }

        _ = text.Append("  registerai --version\n");

        Section(text, "VERBS");
        foreach (var verb in CommandLine.Verbs)
        {
            Row(text, "  " + verb.Name.PadRight(12), verb.Summary);
        }

        Section(text, "OPTIONS");
        Options(text, CommandLine.Options);

        Section(text, "STATES");
        foreach (var (_, word, meaning) in Vocabulary.States)
        {
            Row(text, "  " + word.PadRight(12), meaning);
        }

        Section(text, "ACTIONS");
        foreach (var (_, word, meaning) in Vocabulary.Actions)
        {
            Row(text, "  " + word.PadRight(20), meaning);
        }

        Section(text, "ADVICE");
        foreach (var (code, meaning) in Vocabulary.Advice)
        {
            Row(text, "  " + code.PadRight(24), meaning);
        }

        Section(text, "USER PATH");
        Paragraph(text, "path add and path remove change " + UserPathWhere + " and nothing else. path add puts a folder that exists after the entries already there, and writes nothing when the folder is there already. path remove takes off every entry naming exactly that folder, case and a trailing separator aside. Each write is read back before it counts, and then announced to running programs, which pick it up only if they listen for the announcement.");
        Paragraph(text, "status lists the entries of the user PATH that name a folder that does not exist, each with the path remove line that takes it off. It removes none of them itself.");

        Section(text, "OUTPUT");
        Paragraph(text, "status, register, unregister, path add, path remove, describe and every usage error write exactly one JSON document to stdout and nothing else. help, license and --version write text. Diagnostics go to stderr. Every JSON document carries \"schema\": " + Vocabulary.Schema + "; 'registerai describe' prints the schema.");

        Section(text, "EXIT CODES");
        foreach (var (code, _, meaning) in Vocabulary.ExitCodes)
        {
            Row(text, "  " + code + "  ", meaning);
        }

        Paragraph(text, "With several clients, the code is the first that applies of " + string.Join(", ", Vocabulary.ExitCodeOrder) + ". A dry run of register or unregister returns 0, 3, 4 or 5 and never 1. path add and path remove return 0, 2, or 1 when the PATH could not be read, written or confirmed.");

        Section(text, "QUOTING");
        Paragraph(text, "Everything after -- reaches the client unchanged. Your shell must not change it first.");
        Paragraph(text, "PowerShell and bash: single quotes, '${LOCALAPPDATA}/App/server.exe'.");
        Paragraph(text, "cmd.exe expands %NAME% even inside double quotes; it leaves ${NAME} alone.");
        Paragraph(text, "Run with --dry-run to see exactly what arrived.");

        Section(text, "EXAMPLES");
        foreach (var example in CommandLine.Verbs.Where(verb => verb.Verb is Verb.Status or Verb.Register or Verb.Unregister or Verb.PathAdd or Verb.PathRemove).SelectMany(verb => verb.Examples))
        {
            _ = text.Append("  ").Append(Example(example)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Where the real user PATH is, as every document names it.</summary>
    private static string UserPathWhere => RegistryUserPathStore.User.Where;

    /// <summary>One verb's part of the help text.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>The text, ending in a line feed.</returns>
    public static string For(VerbSpec verb)
    {
        ArgumentNullException.ThrowIfNull(verb);

        var text = new StringBuilder();

        _ = text.Append("registerai ").Append(verb.Name);

        if (verb.Usage.Length > 0)
        {
            _ = text.Append(' ').Append(verb.Usage);
        }

        _ = text.Append("\n\n");
        Paragraph(text, verb.Summary, indent: string.Empty);

        var options = CommandLine.Options.Where(option => verb.Required.Contains(option.Name) || verb.Optional.Contains(option.Name)).ToList();

        if (options.Count > 0 || verb.Tail is not Tail.None)
        {
            Section(text, "OPTIONS");
            Options(text, options, verb.Tail is not Tail.None);
        }

        Section(text, "EXAMPLES");
        foreach (var example in verb.Examples)
        {
            _ = text.Append("  ").Append(Example(example)).Append('\n');
        }

        _ = text.Append("\nRun 'registerai help' for states, actions, advice, output and exit codes.\n");

        return text.ToString();
    }

    /// <summary>An option's value as help shows it: <c>&lt;server&gt;</c>, <c>KEY=VALUE</c>, <c>&lt;id&gt;=&lt;path&gt;</c>.</summary>
    /// <param name="option">The option.</param>
    /// <returns>The placeholder, or an empty string for a flag.</returns>
    public static string Placeholder(OptionSpec option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return option.Value is null
            ? string.Empty
            : string.Join('=', option.Value.Split('=').Select(part => part.Any(char.IsLower) ? "<" + part + ">" : part));
    }

    /// <summary>An argument vector as a PowerShell or bash line, single-quoting what a shell would change.</summary>
    /// <param name="arguments">The arguments after the tool's name.</param>
    /// <returns>The line.</returns>
    public static string Example(IEnumerable<string> arguments) =>
        "registerai " + string.Join(' ', arguments.Select(Quote));

    private static string Quote(string argument) =>
        argument.Length > 0 && argument.All(character => char.IsAsciiLetterOrDigit(character) || "-_.:/=".Contains(character, StringComparison.Ordinal))
            ? argument
            : "'" + argument.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static void Options(StringBuilder text, IEnumerable<OptionSpec> options, bool withTail = true)
    {
        foreach (var option in options)
        {
            var left = Placeholder(option) is { Length: > 0 } placeholder ? option.Name + " " + placeholder : option.Name;

            Row(text, "  " + left.PadRight(OptionColumn - 2), option.Summary);
        }

        if (withTail)
        {
            Row(text, "  " + "-- <command> [args...]".PadRight(OptionColumn - 2), "The server's command line, passed on argument by argument.");
        }
    }

    private static void Section(StringBuilder text, string title) => text.Append('\n').Append(title).Append('\n');

    private static void Paragraph(StringBuilder text, string sentence, string indent = "  ") =>
        Wrap(text, indent, indent, sentence);

    private static void Row(StringBuilder text, string left, string meaning) =>
        Wrap(text, left, new string(' ', left.Length), meaning);

    private static void Wrap(StringBuilder text, string first, string rest, string words)
    {
        var line = new StringBuilder(first);
        var prefixLength = first.Length;

        foreach (var word in words.Split(' '))
        {
            if (line.Length > prefixLength && line.Length + 1 + word.Length > Width)
            {
                _ = text.Append(line.ToString().TrimEnd()).Append('\n');
                _ = line.Clear().Append(rest);
                prefixLength = rest.Length;
            }

            if (line.Length > prefixLength)
            {
                _ = line.Append(' ');
            }

            _ = line.Append(word);
        }

        _ = text.Append(line.ToString().TrimEnd()).Append('\n');
    }
}
