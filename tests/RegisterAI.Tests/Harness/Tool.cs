// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Json;

namespace RegisterAI.Tests.Harness;

/// <summary>
/// Runs the published RegisterAI.exe the way a caller does, in an environment that
/// points every client at scratch folders.
/// </summary>
internal static class Tool
{
    /// <summary>How long one run of the tool may take before the test calls it hung.</summary>
    public static TimeSpan Budget { get; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The variables every run gets: the client configuration folders and the profile
    /// variables point into the scratch folder, so neither the tool nor a client it
    /// starts can reach a real configuration through them.
    /// </summary>
    /// <param name="scratch">The test's scratch folder.</param>
    /// <returns>The variables.</returns>
    public static Dictionary<string, string?> Sandbox(Scratch scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);

        var profile = Directory.CreateDirectory(scratch.In("profile")).FullName;
        var claude = Directory.CreateDirectory(scratch.In("profile", ".claude")).FullName;
        var codex = Directory.CreateDirectory(scratch.In("profile", ".codex")).FullName;

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLAUDE_CONFIG_DIR"] = claude,
            ["CODEX_HOME"] = codex,
            ["USERPROFILE"] = profile,
            ["HOME"] = profile,
            ["DISABLE_AUTOUPDATER"] = "1",
        };
    }

    /// <summary>Runs the published executable.</summary>
    /// <param name="environment">The variables, from <see cref="Sandbox"/>.</param>
    /// <param name="arguments">The command line.</param>
    /// <returns>What it did.</returns>
    public static Task<ChildResult> RunAsync(IReadOnlyDictionary<string, string?> environment, params string[] arguments) =>
        Child.RunAsync(Published.Require(), arguments, Budget, environment: environment);

    /// <summary>
    /// Reads stdout as exactly one JSON document: UTF-8 with no byte order mark, LF
    /// line ends, one trailing line feed, and nothing before or after it.
    /// </summary>
    /// <param name="result">The run.</param>
    /// <returns>The document.</returns>
    public static JsonDocument Document(ChildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var bytes = result.OutputBytes;

        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            throw new InvalidOperationException("stdout starts with a byte order mark.");
        }

        if (bytes.Contains((byte)'\r'))
        {
            throw new InvalidOperationException("stdout carries a carriage return.");
        }

        if (bytes is not [.., (byte)'}', (byte)'\n'])
        {
            throw new InvalidOperationException($"stdout does not end in '}}' and one line feed: {result.Output}");
        }

        // JsonDocument refuses anything after the first document, so a second
        // document or a stray line is an exception here and not a pass.
        return JsonDocument.Parse(bytes);
    }
}
