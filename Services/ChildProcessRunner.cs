using System.Diagnostics;

namespace StreamGuard.Services;

internal sealed record ChildProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ChildProcessRunner
{
    internal static ChildProcessResult Run(ProcessStartInfo startInfo, int timeoutMilliseconds)
    {
        try
        {
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            // Both pipes must be drained while the child runs, otherwise verbose commands deadlock.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); process.WaitForExit(1000); } catch { }
                return new(-1, "", "Timed out");
            }
            // A descendant can inherit a pipe and keep it open even after the direct child exits.
            if (!Task.WhenAll(output, error).Wait(1000)) return new(-1, "", "Output stream did not close");
            return new(process.ExitCode, output.Result, error.Result);
        }
        catch (Exception error) { return new(-1, "", error.Message); }
    }
}
