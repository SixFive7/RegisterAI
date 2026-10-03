// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

namespace RegisterAI.Tests.Harness;

/// <summary>The fake client's build, installed into a scratch folder as both clients.</summary>
/// <param name="Claude">The fake as <c>claude.exe</c>.</param>
/// <param name="Codex">The fake as <c>codex.exe</c>.</param>
internal sealed record FakeClients(string Claude, string Codex)
{
    /// <summary>The <c>--client-exe</c> arguments that point the tool at both fakes.</summary>
    public string[] Arguments => ["--client-exe", "claude-code=" + Claude, "--client-exe", "codex=" + Codex];

    /// <summary>Copies the fake's build into a scratch folder under both names.</summary>
    /// <param name="scratch">The test's scratch folder.</param>
    /// <returns>The two executables.</returns>
    public static FakeClients Install(Scratch scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);

        // bin\<configuration>\<framework>\ of the test host names the configuration
        // the fake was built in, because the test project builds it first.
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var build = Path.Combine(
            RepositoryTree.Root,
            "tests",
            "FakeClient",
            "bin",
            framework.Parent!.Name,
            framework.Name);
        var target = Directory.CreateDirectory(scratch.In("clients")).FullName;

        foreach (var file in new[] { "FakeClient.dll", "FakeClient.runtimeconfig.json", "FakeClient.deps.json" })
        {
            File.Copy(Path.Combine(build, file), Path.Combine(target, file));
        }

        var claude = Path.Combine(target, "claude.exe");
        var codex = Path.Combine(target, "codex.exe");

        File.Copy(Path.Combine(build, "FakeClient.exe"), claude);
        File.Copy(Path.Combine(build, "FakeClient.exe"), codex);

        return new FakeClients(claude, codex);
    }
}
