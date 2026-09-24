using System.Diagnostics;
using System.Text;

namespace Kiri;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

public static class ProcessRunner
{
    /// <summary>Runs a program without a console window and collects its output as UTF-8.</summary>
    public static async Task<ProcessResult> RunAsync(string exe, IEnumerable<string> args, string? workingDir = null,
        CancellationToken ct = default)
    {
        using var process = Start(exe, args, workingDir, redirectStdin: false);
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await WaitAsync(process, ct);
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    /// <summary>Runs a program and hands its standard output stream to <paramref name="readStdout"/>.</summary>
    public static async Task<ProcessResult> RunStreamingAsync(string exe, IEnumerable<string> args, string? workingDir,
        Func<Stream, Task> readStdout, CancellationToken ct = default)
    {
        using var process = Start(exe, args, workingDir, redirectStdin: false);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await readStdout(process.StandardOutput.BaseStream);
        await WaitAsync(process, ct);
        return new ProcessResult(process.ExitCode, "", await stderr);
    }

    /// <summary>Runs a program with <paramref name="input"/> on standard input and returns its standard output bytes.</summary>
    public static async Task<(int ExitCode, byte[] Output)> RunWithInputAsync(string exe, IEnumerable<string> args,
        string? workingDir, byte[] input, CancellationToken ct = default)
    {
        using var process = Start(exe, args, workingDir, redirectStdin: true);
        var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.StandardInput.BaseStream.WriteAsync(input, ct);
        process.StandardInput.Close();
        await copy;
        await stderr;
        await WaitAsync(process, ct);
        return (process.ExitCode, output.ToArray());
    }

    private static Process Start(string exe, IEnumerable<string> args, string? workingDir, bool redirectStdin)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStdin,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDir != null)
            psi.WorkingDirectory = workingDir;
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
    }

    private static async Task WaitAsync(Process process, CancellationToken ct)
    {
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }
}
