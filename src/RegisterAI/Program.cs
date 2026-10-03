// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using RegisterAI;

using var terminal = new Terminal();

return await Cli.RunAsync(args, terminal).ConfigureAwait(false);
