namespace Kiri;

/// <summary>Finds git.exe and kicad-cli.exe on this PC.</summary>
public static class Tools
{
    /// <summary>The oldest KiCad release KiRI supports.</summary>
    public const int MinKiCadMajor = 9;

    public static string? FindGit(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return File.Exists(overridePath) ? overridePath : null;

        var onPath = FindOnPath("git.exe");
        if (onPath != null)
            return onPath;

        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
        };

        // GitHub Desktop bundles its own git in a versioned folder
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitHubDesktop");
        if (Directory.Exists(desktop))
        {
            candidates.AddRange(Directory.GetDirectories(desktop, "app-*")
                .OrderByDescending(dir => ParseVersion(Path.GetFileName(dir)["app-".Length..]))
                .Select(dir => Path.Combine(dir, "resources", "app", "git", "cmd", "git.exe")));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Finds the newest kicad-cli.exe of KiCad 9 or later.</summary>
    public static string? FindKiCadCli(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return File.Exists(overridePath) ? overridePath : null;

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KiCad"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KiCad"),
        };

        var installed = roots
            .Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories)
            .Select(dir => (Version: ParseVersion(Path.GetFileName(dir)), Cli: Path.Combine(dir, "bin", "kicad-cli.exe")))
            .Where(item => item.Version.Major >= MinKiCadMajor && File.Exists(item.Cli))
            .OrderByDescending(item => item.Version)
            .Select(item => item.Cli)
            .FirstOrDefault();

        return installed ?? FindOnPath("kicad-cli.exe");
    }

    /// <summary>The KiCad project manager that belongs to a kicad-cli.exe.</summary>
    public static string? FindKiCad(string kicadCli)
    {
        var kicad = Path.Combine(Path.GetDirectoryName(kicadCli) ?? "", "kicad.exe");
        return File.Exists(kicad) ? kicad : null;
    }

    public static async Task<string> GetVersionAsync(string exe, CancellationToken ct = default)
    {
        var args = Path.GetFileNameWithoutExtension(exe).Equals("kicad-cli", StringComparison.OrdinalIgnoreCase)
            ? new[] { "version" }
            : new[] { "--version" };
        try
        {
            var result = await ProcessRunner.RunAsync(exe, args, null, ct);
            return result.StdOut.Trim();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return "";
        }
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries
            }
        }
        return null;
    }

    private static Version ParseVersion(string text)
    {
        var digits = new string(text.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        if (!digits.Contains('.'))
            digits += ".0";
        return Version.TryParse(digits, out var version) ? version : new Version(0, 0);
    }
}
