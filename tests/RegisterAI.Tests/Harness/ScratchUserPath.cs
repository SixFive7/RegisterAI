// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI.Tests.Harness;

/// <summary>
/// A user PATH kept in memory, so a test can drive <c>path add</c>, <c>path remove</c>
/// and status without the person's own PATH. Every write and announcement is counted.
/// </summary>
internal sealed class ScratchUserPath : IUserPathStore
{
    /// <summary>The value, or null when there is none.</summary>
    public UserPathValue? Value { get; set; }

    /// <summary>How many times a value was written or deleted.</summary>
    public int Writes { get; private set; }

    /// <summary>How many times a change was announced.</summary>
    public int Announcements { get; private set; }

    /// <summary>When set, a write is accepted and then lost, the way a store that does not hold a write behaves.</summary>
    public bool LosesWrites { get; set; }

    /// <inheritdoc/>
    public string Where => @"SCRATCH\Environment\Path";

    /// <inheritdoc/>
    public UserPathValue? Read() => Value;

    /// <inheritdoc/>
    public void Write(UserPathValue value)
    {
        Writes++;

        if (!LosesWrites)
        {
            Value = value;
        }
    }

    /// <inheritdoc/>
    public void Delete()
    {
        Writes++;

        if (!LosesWrites)
        {
            Value = null;
        }
    }

    /// <inheritdoc/>
    public bool Announce()
    {
        Announcements++;

        return true;
    }
}
