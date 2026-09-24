using System.Formats.Tar;
using System.Text;

namespace Kiri;

/// <summary>A commit shown in the viewer's commit list. The working copy is the "_local_" commit.</summary>
public sealed record Commit(string Hash, string Date, string Author, string Subject, string Refs,
    bool SchematicChanged, bool LayoutChanged, bool OtherChanged)
{
    public const string LocalHash = "_local_";

    public bool IsLocal => Hash == LocalHash;

    /// <summary>The subject with the branch and tag names git's %d would add, e.g. "Fix (HEAD -> main)".</summary>
    public string Message => Refs.Length > 0 ? $"{Subject} ({Refs})" : Subject;
}

public sealed class Git
{
    private readonly string _git;

    public Git(string gitExe) { _git = gitExe; }

    private Task<ProcessResult> RunAsync(string dir, CancellationToken ct, params string[] args) =>
        ProcessRunner.RunAsync(_git, new[] { "-c", "core.quotepath=off", "-c", "i18n.logOutputEncoding=UTF-8" }.Concat(args), dir, ct);

    public async Task<string?> GetRepositoryRootAsync(string dir, CancellationToken ct = default)
    {
        var result = await RunAsync(dir, ct, "rev-parse", "--show-toplevel");
        return result.Ok ? Path.GetFullPath(result.StdOut.Trim()) : null;
    }

    public async Task<string> GetUserNameAsync(string dir, CancellationToken ct = default)
    {
        var result = await RunAsync(dir, ct, "config", "user.name");
        var name = result.StdOut.Trim();
        return name.Length > 0 ? name : Environment.UserName;
    }

    /// <summary>
    /// Lists commits, newest first, with which kinds of project files each one changed.
    /// Only files in <paramref name="projectDir"/> are considered.
    /// </summary>
    /// <param name="revisions">Revisions to list, e.g. "--branches --remotes" or "main".</param>
    public async Task<List<Commit>> GetCommitsAsync(string projectDir, IEnumerable<string> revisions, CancellationToken ct = default)
    {
        // Records start with \x1e and fields are separated by \0, so any text can
        // appear in a commit message; the changed files follow each record
        var args = new List<string>
        {
            "log", "--date=format:%Y-%m-%d %H:%M:%S",
            "--pretty=format:%x1e%h%x00%ad%x00%an%x00%s%x00%D%x00",
            "--name-only", "--relative",
        };
        args.AddRange(revisions);
        args.Add("--");

        var result = await RunAsync(projectDir, ct, args.ToArray());
        if (!result.Ok)
            throw new KiriException($"git log failed: {result.StdErr.Trim()}");

        var commits = new List<Commit>();
        foreach (var record in result.StdOut.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\0');
            if (fields.Length < 6)
                continue;
            var files = fields[5].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var (sch, pcb, other) = Classify(files);
            commits.Add(new Commit(fields[0], fields[1], fields[2], fields[3], fields[4], sch, pcb, other));
        }
        return commits;
    }

    /// <summary>The working copy as a commit, or null when no schematic or layout file has uncommitted changes.</summary>
    public async Task<Commit?> GetLocalChangesAsync(string projectDir, CancellationToken ct = default)
    {
        var result = await RunAsync(projectDir, ct, "status", "--porcelain", "--untracked-files=no", "--", ".");
        if (!result.Ok)
            return null;

        // Porcelain lines look like " M path" or "R  old -> new"
        var files = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length > 3)
            .Select(line => line[3..].Split(" -> ").Last().Trim().Trim('"'))
            .ToArray();

        var (sch, pcb, other) = Classify(files);
        if (!sch && !pcb)
            return null;

        return new Commit(Commit.LocalHash, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), await GetUserNameAsync(projectDir, ct),
            "Local changes not committed", "", sch, pcb, other);
    }

    /// <summary>Resolves a revision such as "HEAD~1" or a tag to the short hash git log uses.</summary>
    public async Task<Commit?> GetCommitAsync(string projectDir, string revision, CancellationToken ct = default)
    {
        var commits = await GetCommitsAsync(projectDir, new[] { "-n1", revision }, ct);
        return commits.FirstOrDefault();
    }

    /// <summary>
    /// Writes the KiCad files of a commit's project folder into <paramref name="destination"/>,
    /// with the project folder as its root. Large binary files in the repository are skipped.
    /// </summary>
    public async Task ExportProjectFilesAsync(string repoRoot, string nestedPath, string hash, string destination,
        CancellationToken ct = default)
    {
        var tree = string.IsNullOrEmpty(nestedPath) ? hash : $"{hash}:{nestedPath}";
        var lfsPointers = new List<string>();

        var result = await ProcessRunner.RunStreamingAsync(_git, new[] { "archive", "--format=tar", tree }, repoRoot,
            async stdout =>
            {
                using var reader = new TarReader(stdout);
                while (await reader.GetNextEntryAsync(copyData: false, ct) is { } entry)
                {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                        continue;
                    if (!ProjectFiles.IsWanted(entry.Name))
                        continue;

                    var path = Path.Combine(destination, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using (var file = File.Create(path))
                        await entry.DataStream.CopyToAsync(file, ct);

                    if (entry.Length < 1024)
                        lfsPointers.Add(path);
                }

                // Read the padding after the end-of-archive marker too, or git fails writing it
                await stdout.CopyToAsync(Stream.Null, ct);
            }, ct);

        if (!result.Ok)
            throw new KiriException($"git archive {tree} failed: {result.StdErr.Trim()}");

        // Replace Git LFS pointers with the real files
        foreach (var path in lfsPointers)
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            if (!Encoding.ASCII.GetString(bytes).StartsWith("version https://git-lfs.github.com/spec/"))
                continue;
            var (exitCode, content) = await ProcessRunner.RunWithInputAsync(_git, new[] { "lfs", "smudge" }, repoRoot, bytes, ct);
            if (exitCode == 0)
                await File.WriteAllBytesAsync(path, content, ct);
        }
    }

    private static (bool Schematic, bool Layout, bool Other) Classify(IEnumerable<string> files)
    {
        bool sch = false, pcb = false, other = false;
        foreach (var file in files)
        {
            if (file.EndsWith(".kicad_sch", StringComparison.OrdinalIgnoreCase)) sch = true;
            else if (file.EndsWith(".kicad_pcb", StringComparison.OrdinalIgnoreCase)) pcb = true;
            else other = true;
        }
        return (sch, pcb, other);
    }
}

/// <summary>Which files of a project are copied for plotting.</summary>
public static class ProjectFiles
{
    private static readonly string[] Extensions = { ".kicad_pro", ".kicad_sch", ".kicad_pcb", ".kicad_wks", ".kicad_dru" };
    private static readonly string[] Names = { "fp-lib-table", "sym-lib-table" };

    public static bool IsWanted(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        // Skip hidden folders and KiCad's automatic backups
        if (parts[..^1].Any(dir => dir.StartsWith('.') || dir.EndsWith("-backups", StringComparison.OrdinalIgnoreCase)))
            return false;

        var name = parts[^1];
        return Names.Contains(name, StringComparer.OrdinalIgnoreCase)
            || Extensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Copies the wanted files of a working copy, for the "_local_" commit.</summary>
    public static void CopyWorkingFiles(string projectDir, string destination, string? skipDir)
    {
        foreach (var file in Directory.EnumerateFiles(projectDir, "*", SearchOption.AllDirectories))
        {
            if (skipDir != null && file.StartsWith(skipDir, StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = Path.GetRelativePath(projectDir, file);
            if (!IsWanted(relative))
                continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

public sealed class KiriException : Exception
{
    public KiriException(string message) : base(message) { }
}
