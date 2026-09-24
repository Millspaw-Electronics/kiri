using System.Security.Cryptography;
using System.Text;

namespace Kiri;

public sealed class GeneratorOptions
{
    /// <summary>A .kicad_pro file, or a folder containing exactly one.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Where the generated site goes. Defaults to %LOCALAPPDATA%\kiri\&lt;project&gt;-&lt;id&gt;.</summary>
    public string? OutputDir { get; init; }

    public string? GitPath { get; init; }
    public string? KiCadCliPath { get; init; }

    /// <summary>Include commits that don't change a schematic or layout.</summary>
    public bool AllCommits { get; init; }

    /// <summary>Revisions to list; defaults to all local and remote branches.</summary>
    public IReadOnlyList<string>? Revisions { get; init; }

    /// <summary>Compare only these two revisions ("A..B"); "local" means the working copy.</summary>
    public string? Compare { get; init; }

    public string? NewestCommit { get; init; }
    public string? OldestCommit { get; init; }
    public int? LastCommits { get; init; }

    /// <summary>Delete the output folder first, so every commit is plotted again.</summary>
    public bool Rebuild { get; init; }

    /// <summary>Open the viewer on the layout instead of the schematic.</summary>
    public bool StartOnLayout { get; init; }

    /// <summary>The folder with the viewer's files (index.html, kiri.js, ...).</summary>
    public required string AssetsDir { get; init; }

    public int? Parallelism { get; init; }
}

public sealed record GeneratorProgress(string Message, double? Fraction = null, bool IsError = false);

public sealed record GeneratorResult(string OutputDir, string IndexHtml, KiCadProject Project, IReadOnlyList<Commit> Commits);

/// <summary>The KiCad project KiRI is working on.</summary>
/// <param name="ProjectFile">The full path of the .kicad_pro in the working copy.</param>
/// <param name="RepoRoot">The root of the git repository.</param>
/// <param name="NestedPath">The project folder relative to the repository root, with "/" separators ("" at the root).</param>
public sealed record KiCadProject(string ProjectFile, string RepoRoot, string NestedPath)
{
    public string Name => Path.GetFileNameWithoutExtension(ProjectFile);
    public string ProjectDir => Path.GetDirectoryName(ProjectFile)!;
    public string RepoName => Path.GetFileName(RepoRoot.TrimEnd(Path.DirectorySeparatorChar));
}

public sealed class Generator
{
    private readonly GeneratorOptions _options;
    private readonly IProgress<GeneratorProgress> _progress;

    public Generator(GeneratorOptions options, IProgress<GeneratorProgress>? progress = null)
    {
        _options = options;
        _progress = progress ?? new Progress<GeneratorProgress>();
    }

    private void Report(string message, double? fraction = null) => _progress.Report(new GeneratorProgress(message, fraction));
    private void ReportError(string message) => _progress.Report(new GeneratorProgress(message, IsError: true));

    /// <summary>The .kicad_pro files in a folder and its subfolders, skipping hidden folders and backups.</summary>
    public static List<string> FindProjects(string folder, int maxDepth = 4)
    {
        var projects = new List<string>();
        Search(folder, 0);
        return projects.OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar)).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        void Search(string dir, int depth)
        {
            try
            {
                projects.AddRange(Directory.GetFiles(dir, "*.kicad_pro"));
                if (depth >= maxDepth)
                    return;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith('.') || name.EndsWith("-backups", StringComparison.OrdinalIgnoreCase) || name == "node_modules")
                        continue;
                    Search(sub, depth + 1);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // Skip folders we can't read
            }
        }
    }

    public static string DefaultOutputDir(string projectFile)
    {
        // Keep generated files outside the project, so they don't show up as changes
        // in Git. The id is a hash of the project's path, so clones don't mix.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectFile).ToLowerInvariant()));
        var id = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kiri");
        return Path.Combine(root, $"{Path.GetFileNameWithoutExtension(projectFile)}-{id}");
    }

    public async Task<GeneratorResult> RunAsync(CancellationToken ct = default)
    {
        var gitExe = Tools.FindGit(_options.GitPath)
            ?? throw new KiriException("Git was not found. Install Git for Windows or GitHub Desktop.");
        var kicadCliExe = Tools.FindKiCadCli(_options.KiCadCliPath)
            ?? throw new KiriException($"kicad-cli was not found. Install KiCad {Tools.MinKiCadMajor} or newer.");
        var git = new Git(gitExe);
        var cli = new KiCadCli(kicadCliExe);

        var project = await ResolveProjectAsync(git, ct);
        var outputDir = Path.GetFullPath(_options.OutputDir ?? DefaultOutputDir(project.ProjectFile));

        Report($"Project: {project.ProjectFile}");
        Report($"Output folder: {outputDir}");
        Report($"Using {await Tools.GetVersionAsync(gitExe, ct)} and kicad-cli {await Tools.GetVersionAsync(kicadCliExe, ct)}");

        if (_options.Rebuild && Directory.Exists(outputDir))
        {
            Report("Removing the previous output");
            Directory.Delete(outputDir, recursive: true);
        }
        Directory.CreateDirectory(outputDir);

        var commits = await GetCommitListAsync(git, project, ct);
        if (commits.Count < 2)
            throw new KiriException("Fewer than 2 commits change this project's schematic or layout, so there is nothing to compare.");

        var local = commits.Any(c => c.IsLocal) ? " plus local changes" : "";
        Report($"{commits.Count(c => !c.IsLocal)} commits{local}");

        await PlotCommitsAsync(git, cli, project, outputDir, commits, ct);

        Report("Assembling the viewer");
        var index = SiteBuilder.Build(_options.AssetsDir, outputDir, project, commits, _options.StartOnLayout);
        Report("Done", 1.0);

        return new GeneratorResult(outputDir, index, project, commits);
    }

    private async Task<KiCadProject> ResolveProjectAsync(Git git, CancellationToken ct)
    {
        var path = Path.GetFullPath(_options.ProjectPath);
        string projectFile;
        if (Directory.Exists(path))
        {
            var found = FindProjects(path);
            if (found.Count == 0)
                throw new KiriException($"No KiCad project (.kicad_pro) was found in {path}.");

            // Prefer a single project at the top of the folder
            var top = found.Where(p => Path.GetDirectoryName(p) == path).ToList();
            if (top.Count == 1)
                projectFile = top[0];
            else if (found.Count == 1)
                projectFile = found[0];
            else
                throw new KiriException($"{path} contains several KiCad projects; choose one:\n  " + string.Join("\n  ", found));
        }
        else if (File.Exists(path) && path.EndsWith(".kicad_pro", StringComparison.OrdinalIgnoreCase))
        {
            projectFile = path;
        }
        else
        {
            throw new KiriException($"{path} is not a KiCad project file (.kicad_pro) or folder.");
        }

        var projectDir = Path.GetDirectoryName(projectFile)!;
        var repoRoot = await git.GetRepositoryRootAsync(projectDir, ct)
            ?? throw new KiriException($"{projectDir} is not inside a Git repository.");

        var nested = Path.GetRelativePath(repoRoot, projectDir).Replace('\\', '/');
        return new KiCadProject(projectFile, repoRoot, nested == "." ? "" : nested);
    }

    private async Task<List<Commit>> GetCommitListAsync(Git git, KiCadProject project, CancellationToken ct)
    {
        Report("Reading the commit history");

        if (!string.IsNullOrEmpty(_options.Compare))
        {
            var parts = _options.Compare.Split("..", 2);
            var list = new List<Commit>();
            foreach (var revision in new[] { parts[0], parts.Length > 1 ? parts[1] : "local" })
            {
                if (revision.Contains("local", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(await git.GetLocalChangesAsync(project.ProjectDir, ct)
                        ?? new Commit(Commit.LocalHash, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            await git.GetUserNameAsync(project.ProjectDir, ct), "Local changes not committed", "", false, false, false));
                }
                else
                {
                    list.Add(await git.GetCommitAsync(project.ProjectDir, revision, ct)
                        ?? throw new KiriException($"Unknown revision '{revision}'."));
                }
            }
            return list;
        }

        var revisions = _options.Revisions ?? new[] { "--branches", "--remotes" };
        var commits = (await git.GetCommitsAsync(project.ProjectDir, revisions, ct))
            .Where(c => _options.AllCommits || c.SchematicChanged || c.LayoutChanged)
            .ToList();

        var localChanges = await git.GetLocalChangesAsync(project.ProjectDir, ct);
        if (localChanges != null)
            commits.Insert(0, localChanges);

        if (_options.OldestCommit is { } oldest)
        {
            var i = commits.FindIndex(c => c.Hash.StartsWith(oldest, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) commits = commits.Take(i + 1).ToList();
            else ReportError($"Warning: commit {oldest} is not in the commit list");
        }
        if (_options.NewestCommit is { } newest)
        {
            var i = commits.FindIndex(c => c.Hash.StartsWith(newest, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) commits = commits.Skip(i).ToList();
            else ReportError($"Warning: commit {newest} is not in the commit list");
        }
        if (_options.LastCommits is { } last)
            commits = commits.Take(last).ToList();

        return commits;
    }

    private async Task PlotCommitsAsync(Git git, KiCadCli cli, KiCadProject project, string outputDir,
        List<Commit> commits, CancellationToken ct)
    {
        // The working copy changes between runs, so it's always plotted again
        var localDir = Path.Combine(outputDir, Commit.LocalHash);
        if (Directory.Exists(localDir))
            Directory.Delete(localDir, recursive: true);

        var todo = commits.Where(c => !File.Exists(DoneMarker(outputDir, c.Hash))).ToList();
        var cached = commits.Count - todo.Count;
        if (cached > 0)
            Report($"{cached} commits are already plotted");
        if (todo.Count == 0)
            return;

        // kicad-cli spends most of its time starting up and loading files on one core,
        // so several can run side by side
        var parallelism = _options.Parallelism ?? Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
        using var gate = new SemaphoreSlim(parallelism);
        int finished = 0;

        Report($"Plotting {todo.Count} commits ({parallelism} at a time)", 0.0);

        var tasks = todo.Select(async commit =>
        {
            await gate.WaitAsync(ct);
            try
            {
                await PlotCommitAsync(git, cli, project, outputDir, commit, ct);
            }
            finally
            {
                gate.Release();
            }
            var done = Interlocked.Increment(ref finished);
            Report($"Plotted {done}/{todo.Count}: {commit.Hash} {commit.Subject}", (double)done / todo.Count);
        });

        await Task.WhenAll(tasks);
    }

    private static string DoneMarker(string outputDir, string hash) => Path.Combine(outputDir, hash, "_KIRI_", ".done");

    private async Task PlotCommitAsync(Git git, KiCadCli cli, KiCadProject project, string outputDir, Commit commit,
        CancellationToken ct)
    {
        var commitDir = Path.Combine(outputDir, commit.Hash);
        if (Directory.Exists(commitDir))
            Directory.Delete(commitDir, recursive: true);
        var kiriDir = Path.Combine(commitDir, "_KIRI_");
        Directory.CreateDirectory(kiriDir);

        if (commit.IsLocal)
            ProjectFiles.CopyWorkingFiles(project.ProjectDir, commitDir, outputDir + Path.DirectorySeparatorChar);
        else
            await git.ExportProjectFilesAsync(project.RepoRoot, project.NestedPath, commit.Hash, commitDir, ct);

        // The project may have been renamed at some point, so fall back to any .kicad_pro
        var proFile = Path.Combine(commitDir, Path.GetFileName(project.ProjectFile));
        if (!File.Exists(proFile))
            proFile = Directory.GetFiles(commitDir, "*.kicad_pro").FirstOrDefault() ?? proFile;
        var name = Path.GetFileNameWithoutExtension(proFile);
        var schFile = Path.Combine(commitDir, name + ".kicad_sch");
        var pcbFile = Path.Combine(commitDir, name + ".kicad_pcb");

        var sheets = File.Exists(schFile) ? KiCadFiles.ReadSheets(commitDir, name + ".kicad_sch") : new List<SheetInfo>();
        await WriteLinesAsync(Path.Combine(kiriDir, "sch_sheets"), sheets.Select(s => s.ToLine()), ct);

        SExpr? pcb = null;
        if (File.Exists(pcbFile))
        {
            try { pcb = SExpr.ParseFile(pcbFile); }
            catch (FormatException e) { ReportError($"{commit.Hash}: could not read {Path.GetFileName(pcbFile)}: {e.Message}"); }
        }
        var layers = pcb != null ? KiCadFiles.ReadLayers(pcb) : new List<LayerInfo>();
        await WriteLinesAsync(Path.Combine(kiriDir, "pcb_layers"), layers.Select(l => l.ToLine()), ct);

        await WriteLinesAsync(Path.Combine(kiriDir, "pro_infos"), new[]
        {
            proFile,
            $"sch version = {(File.Exists(schFile) ? ReadVersion(schFile) : "")}",
            $"pcb version = {(pcb != null ? KiCadFiles.ReadFormatVersion(pcb) : "")}",
        }, ct);

        var plots = new List<Task<IReadOnlyList<string>>>();
        if (sheets.Count > 0)
            plots.Add(cli.PlotSchematicAsync(schFile, Path.Combine(kiriDir, "sch"), sheets, ct));
        if (layers.Count > 0)
            plots.Add(cli.PlotLayoutAsync(pcbFile, Path.Combine(kiriDir, "pcb"), layers, ct));

        foreach (var problem in (await Task.WhenAll(plots)).SelectMany(p => p))
            ReportError($"{commit.Hash}: {problem}");

        await File.WriteAllTextAsync(DoneMarker(outputDir, commit.Hash), "", ct);
    }

    /// <summary>Writes lines with "\n" endings, which the viewer splits on.</summary>
    private static Task WriteLinesAsync(string path, IEnumerable<string> lines, CancellationToken ct) =>
        File.WriteAllTextAsync(path, string.Concat(lines.Select(line => line + "\n")), ct);

    private static string ReadVersion(string file)
    {
        try { return KiCadFiles.ReadFormatVersion(SExpr.ParseFile(file)); }
        catch (FormatException) { return ""; }
    }
}
