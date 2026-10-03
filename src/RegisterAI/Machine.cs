// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using Microsoft.Win32;

namespace RegisterAI;

/// <summary>
/// Everything the engine reads from outside itself, in one place, so the decision
/// tests can hand it a scratch profile, a scratch PATH, a scratch user PATH store and
/// a fake client.
/// </summary>
internal sealed class Machine
{
    /// <summary>Reads one of this process's environment variables.</summary>
    public required Func<string, string?> Variable { get; init; }

    /// <summary>The profile folder, from the account's token and not from <c>%USERPROFILE%</c>.</summary>
    public required string Profile { get; init; }

    /// <summary>The local application data folder.</summary>
    public required string LocalAppData { get; init; }

    /// <summary>The roaming application data folder.</summary>
    public required string AppData { get; init; }

    /// <summary>
    /// The folders a program started now would search for a bare command: the machine
    /// PATH, then the user PATH, read from the registry and expanded.
    /// </summary>
    public required Func<IReadOnlyList<string>> NewProgramPath { get; init; }

    /// <summary>Runs a client.</summary>
    public required IRunner Runner { get; init; }

    /// <summary>
    /// The user PATH that <c>path add</c> and <c>path remove</c> change and status reads.
    /// Only <see cref="Real"/> hands out the real one.
    /// </summary>
    public required IUserPathStore UserPath { get; init; }

    /// <summary>This machine.</summary>
    /// <returns>The real machine.</returns>
    public static Machine Real() => new()
    {
        Variable = Environment.GetEnvironmentVariable,
        Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        NewProgramPath = RegistryPath,
        Runner = new ProcessRunner(),
        UserPath = RegistryUserPathStore.User,
    };

    /// <summary>
    /// The PATH a newly started program gets, from the registry and not from this
    /// process, which may be older than a change to it.
    /// </summary>
    /// <returns>The folders, machine first.</returns>
    private static List<string> RegistryPath()
    {
        var folders = new List<string>();

        foreach (var (hive, key) in new[]
        {
            (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"),
            (Registry.CurrentUser, "Environment"),
        })
        {
            try
            {
                using var opened = hive.OpenSubKey(key, writable: false);

                if (opened?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string text)
                {
                    folders.AddRange(Environment.ExpandEnvironmentVariables(text)
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A hive this account cannot read adds nothing, which is what a program
                // it starts would find too.
            }
        }

        return folders;
    }
}
