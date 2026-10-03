// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text;
using RegisterAI.FakeClient;

var outcome = ClientModel.Run(
    Environment.ProcessPath ?? "fake.exe",
    args,
    Environment.CurrentDirectory,
    Environment.GetEnvironmentVariable);

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

using (var output = Console.OpenStandardOutput())
{
    output.Write(utf8.GetBytes(outcome.Output));
}

using (var error = Console.OpenStandardError())
{
    error.Write(utf8.GetBytes(outcome.Error));
}

if (outcome.SleepMilliseconds > 0)
{
    await Task.Delay(outcome.SleepMilliseconds).ConfigureAwait(false);
}

return outcome.ExitCode;
