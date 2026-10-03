// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI;

/// <summary>Reads the command line and writes the one document the run produces.</summary>
internal static class Cli
{
    /// <summary>Runs one command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="terminal">Where the document goes.</param>
    /// <returns>The exit code.</returns>
    public static Task<int> RunAsync(IReadOnlyList<string> args, Terminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        var parsed = CommandLine.Parse(args);

        switch (parsed.Outcome)
        {
            case Outcome.Version:
                terminal.WriteText(ToolVersion.Text + "\n");
                return Task.FromResult(0);

            case Outcome.Help:
                terminal.WriteText(parsed.Verb is { } verb ? HelpText.For(verb) : HelpText.Full());
                return Task.FromResult(parsed.ExitCode);

            case Outcome.Usage:
                Report.WriteUsage(terminal, parsed.Verb?.Name, parsed.Error!);
                return Task.FromResult(2);

            default:
                break;
        }

        var request = parsed.Request!;

        switch (request.Verb)
        {
            case Verb.Describe:
                Describe.Write(terminal);
                return Task.FromResult(0);

            case Verb.License:
                terminal.WriteText(LicenseText.Read());
                return Task.FromResult(0);

            default:
                Report.WriteStopped(terminal, request, 1, $"{Report.Name(request.Verb)} is not implemented in this build.");
                return Task.FromResult(1);
        }
    }
}
