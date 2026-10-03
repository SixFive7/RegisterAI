// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Globalization;

namespace RegisterAI.Tests.Harness;

/// <summary>
/// This machine's real Claude Code and Codex, copied into the repository's scratch
/// folder and run from there, never from where they are installed.
/// </summary>
/// <remarks>
/// <para>
/// Every run of a real client goes through <see cref="Tool.Sandbox"/>, so
/// <c>CLAUDE_CONFIG_DIR</c>, <c>CODEX_HOME</c>, <c>USERPROFILE</c> and <c>HOME</c>
/// point into the test's scratch folder and the client's own updater is off. The
/// person's real configuration is read once at the end of each test, only to show that
/// this run's folder is not in it.
/// </para>
/// <para>
/// A machine without one of the clients skips these tests, loudly, and this is the one
/// place in the suite allowed to skip at run time. With <c>REGISTERAI_RELEASE_RUN=1</c>
/// a missing client fails instead, so no release is cut without them.
/// </para>
/// </remarks>
internal static class RealClients
{
    /// <summary>The variable that turns a missing client from a skip into a failure.</summary>
    public const string ReleaseVariable = "REGISTERAI_RELEASE_RUN";

    private static readonly SemaphoreSlim CopyGate = new(1, 1);

    /// <summary>Both real clients, copied into scratch, or a skip when this machine lacks one.</summary>
    /// <returns>The copies.</returns>
    public static async Task<FakeClients> RequireAsync()
    {
        var plain = new Request { Verb = Verb.Status };
        var machine = Machine.Real();
        var claude = ClientRules.Locate(ClientId.ClaudeCode, plain, machine);
        var codex = ClientRules.Locate(ClientId.Codex, plain, machine);

        if (claude is null || codex is null)
        {
            var missing = $"This machine has no {(claude is null ? "claude.exe" : "codex.exe")} where RegisterAI looks, so the real-client tests cannot run.";

            if (Environment.GetEnvironmentVariable(ReleaseVariable) is "1")
            {
                throw new InvalidOperationException(missing + " A release run needs both clients.");
            }

            Skip.Test(missing);
        }

        await CopyGate.WaitAsync();

        try
        {
            return new FakeClients(Copy(claude!), Copy(codex!));
        }
        finally
        {
            _ = CopyGate.Release();
        }
    }

    /// <summary>
    /// Seeds a scratch Claude Code configuration folder with what Claude Code's own
    /// throwaway configurations hold, so a first run never takes the folder for a new
    /// installation that has to be set up.
    /// </summary>
    /// <param name="sandbox">The sandbox from <see cref="Tool.Sandbox"/>.</param>
    public static void SeedClaude(IReadOnlyDictionary<string, string?> sandbox)
    {
        ArgumentNullException.ThrowIfNull(sandbox);

        File.WriteAllText(
            Path.Combine(sandbox["CLAUDE_CONFIG_DIR"]!, ".claude.json"),
            "{\"hasCompletedOnboarding\":true,\"autoUpdates\":false,\"bypassPermissionsModeAccepted\":false}");
    }

    /// <summary>
    /// The person's real Claude Code and Codex configuration, read and never written,
    /// for a test to show that its own scratch folder appears in neither.
    /// </summary>
    /// <returns>Both files' text, or what exists of them.</returns>
    public static async Task<string> RealConfigurationAsync()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = string.Empty;

        foreach (var file in new[] { Path.Combine(profile, ".claude.json"), Path.Combine(profile, ".codex", "config.toml") })
        {
            if (!File.Exists(file))
            {
                continue;
            }

            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            text += await reader.ReadToEndAsync();
        }

        return text;
    }

    /// <summary>Copies a client into scratch once per installed build, keyed by its size and time.</summary>
    private static string Copy(string source)
    {
        var info = new FileInfo(source);
        var folder = Path.Combine(
            RepositoryTree.Root,
            ".work",
            "real-clients",
            string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(source)}-{info.Length}-{info.LastWriteTimeUtc.Ticks}"));
        var copy = Path.Combine(folder, info.Name);

        if (!File.Exists(copy) || new FileInfo(copy).Length != info.Length)
        {
            _ = Directory.CreateDirectory(folder);
            File.Copy(source, copy + ".partial", overwrite: true);
            File.Move(copy + ".partial", copy, overwrite: true);
        }

        return copy;
    }
}
