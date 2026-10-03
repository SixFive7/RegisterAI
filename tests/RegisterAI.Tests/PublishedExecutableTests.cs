// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Reflection.PortableExecutable;
using System.Text;
using RegisterAI.Tests.Harness;

namespace RegisterAI.Tests;

/// <summary>
/// The published executable is one file that needs nothing Windows does not provide.
/// </summary>
internal sealed class PublishedExecutableTests
{
    /// <summary>
    /// Every library the executable imports, directly or delay-loaded, is an API set or a
    /// file in the Windows system folder: no .NET runtime and no C++ redistributable.
    /// </summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ItImportsOnlyLibrariesWindowsProvides()
    {
        var imports = Imports(Published.Require());
        var foreign = imports.Where(name => !IsProvidedByWindows(name)).ToList();

        await Assert.That(string.Join(", ", foreign)).IsEmpty();
        await Assert.That(imports).Contains("KERNEL32.dll");

        // The control: the reader finds imports in a binary that has them, and the rule
        // refuses a library Windows does not ship.
        await Assert.That(IsProvidedByWindows("vcruntime140.dll")).IsFalse();
        await Assert.That(IsProvidedByWindows("api-ms-win-crt-runtime-l1-1-0.dll")).IsTrue();
    }

    /// <summary>The published folder holds the executable and the debug files nothing ships, and no other file.</summary>
    /// <returns>The test.</returns>
    [Test]
    public async Task ThePublishedFolderHoldsOneExecutableAndNothingItNeeds()
    {
        var folder = Path.GetDirectoryName(Published.Require())!;
        var files = Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal);

        await Assert.That(string.Join(' ', files)).IsEqualTo("RegisterAI.exe RegisterAI.pdb RegisterAI.xml");
    }

    /// <summary>The library names in a PE file's import and delay-import tables.</summary>
    private static List<string> Imports(string file)
    {
        using var stream = File.OpenRead(file);
        using var reader = new PEReader(stream);
        var header = reader.PEHeaders.PEHeader!;
        var names = new List<string>();

        // An import descriptor is 20 bytes with the name's address at 12; a delay-import
        // descriptor is 32 bytes with it at 4. Both tables end with a descriptor of zeros.
        foreach (var (directory, size, at) in new[] { (header.ImportTableDirectory, 20, 12), (header.DelayImportTableDirectory, 32, 4) })
        {
            if (directory.RelativeVirtualAddress is 0)
            {
                continue;
            }

            var table = reader.GetSectionData(directory.RelativeVirtualAddress).GetContent();

            for (var offset = 0; offset + size <= table.Length; offset += size)
            {
                var name = BitConverter.ToInt32(table.AsSpan(offset + at, 4));

                if (name is 0)
                {
                    break;
                }

                var text = reader.GetSectionData(name).GetContent();
                var end = text.IndexOf((byte)0);

                names.Add(Encoding.ASCII.GetString(text.AsSpan()[..end]));
            }
        }

        return names;
    }

    /// <summary>
    /// Whether Windows itself ships a library: an API set, or one of the system libraries
    /// a NativeAOT program links against. A file in the system folder is not enough,
    /// because redistributable installers put their libraries there too.
    /// </summary>
    private static bool IsProvidedByWindows(string library) =>
        library.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase)
        || WindowsLibraries.Contains(library, StringComparer.OrdinalIgnoreCase);

    private static readonly string[] WindowsLibraries =
    [
        "KERNEL32.dll", "ADVAPI32.dll", "bcrypt.dll", "ole32.dll", "OLEAUT32.dll", "USER32.dll", "WS2_32.dll",
        "CRYPT32.dll", "ncrypt.dll", "SECUR32.dll", "ntdll.dll", "SHELL32.dll", "VERSION.dll", "IPHLPAPI.dll",
        "NORMALIZ.dll", "ucrtbase.dll",
    ];
}
