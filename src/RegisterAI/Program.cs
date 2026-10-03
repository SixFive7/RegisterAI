// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using RegisterAI;

using var terminal = new Terminal();

if (args is ["--version"])
{
    terminal.WriteText(ToolVersion.Text + "\n");
    return 0;
}

terminal.WriteDiagnostic("registerai: this build implements --version only.\n");
return 2;
