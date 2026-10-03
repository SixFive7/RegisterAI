// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace RegisterAI.Tests.Harness;

/// <summary>
/// The person's own user PATH, read before the first test and after the last, so a run
/// that changed it fails. Read only: nothing here writes it, and no test may.
/// </summary>
/// <remarks>
/// <para>
/// The tests drive <c>path add</c> and <c>path remove</c> against a PATH kept in memory
/// or under a scratch registry key, and the published executable only with
/// <c>--dry-run</c>. This hook is what holds that, for every test at once: a test that
/// reached the real PATH turns the whole run red, naming the value.
/// </para>
/// <para>
/// A session hook, so no filter can leave it out. Another program on the machine that
/// changes the user PATH during a run turns it red too; the message says what moved, so
/// a person can tell that from a test that wrote it.
/// </para>
/// </remarks>
internal static class UserPathGuard
{
    private static string? _before;

    /// <summary>Reads the PATH before the first test.</summary>
    [Before(TestSession)]
    public static void ReadTheUsersPathBeforeTheRun() => _before = Reading();

    /// <summary>Fails the run when the PATH is not what it was before the first test.</summary>
    /// <exception cref="InvalidOperationException">The PATH changed during the run.</exception>
    [After(TestSession)]
    public static void HoldTheUsersPathUnchangedByTheRun()
    {
        if (_before is not null && Difference(_before, Reading()) is { } difference)
        {
            throw new InvalidOperationException(difference);
        }
    }

    /// <summary>
    /// The PATH as one comparable line: its kind, its length and the SHA-256 of its
    /// unexpanded text, or <c>absent</c>.
    /// </summary>
    /// <returns>The line.</returns>
    public static string Reading()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment", writable: false);

        if (key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string text)
        {
            return "absent";
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        return string.Create(CultureInfo.InvariantCulture, $"{key.GetValueKind("Path")} length={text.Length} sha256={hash}");
    }

    /// <summary>What a person reads when the PATH moved, or null when it did not.</summary>
    /// <param name="before">The reading before the run.</param>
    /// <param name="after">The reading after it.</param>
    /// <returns>The sentence, or null.</returns>
    public static string? Difference(string before, string after) =>
        string.Equals(before, after, StringComparison.Ordinal)
            ? null
            : $@"HKEY_CURRENT_USER\Environment\Path changed during this run: it read '{before}' before the first test and '{after}' after the last. No test may write the person's own PATH. If another program changed it during the run, run the suite again.";
}
