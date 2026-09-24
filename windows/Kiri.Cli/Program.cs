using System.IO.Compression;
using Kiri;

const string Usage = """
    USAGE:

        kiri-cli [OPTIONS] [KICAD_PROJECT]

    Plots every commit of a KiCad project in a Git repository and builds the
    KiRI viewer for them. KICAD_PROJECT is a .kicad_pro file or a folder with
    one; it defaults to the current folder. Open the result in the KiRI app,
    or serve the output folder with any web server.

    OPTIONS:

        -a, --all              Include commits that don't change a schematic or layout
        -g, --git-diff A..B    Compare only two revisions, e.g. HEAD~1..HEAD, v1.0..local
        -n, --newest HASH      Start the list at this commit
        -o, --oldest HASH      End the list at this commit
        -t, --last N           Keep only the newest N commits
        -b, --branches REVS    Revisions to list (default "--branches --remotes"),
                               e.g. -b main or -b "main feature"
        -u, --layout           Open the viewer on the layout instead of the schematic
        -d, --output-dir DIR   Output folder (default %LOCALAPPDATA%\kiri\<project>-<id>)
        -r, --remove           Remove the output folder first, so everything is plotted again
        -x, --archive          Also zip the generated site into the current folder
        -j, --jobs N           Number of kicad-cli runs at a time
            --git PATH         Use this git.exe
            --kicad-cli PATH   Use this kicad-cli.exe
        -v, --version          Show the versions of KiRI and the tools it finds
        -h, --help             Show this help
    """;

string? project = null, outputDir = null, compare = null, newest = null, oldest = null, gitPath = null, cliPath = null;
int? last = null, jobs = null;
IReadOnlyList<string>? revisions = Environment.GetEnvironmentVariable("KIRI_BRANCHES") is { Length: > 0 } envBranches
    ? envBranches.Split(' ', StringSplitOptions.RemoveEmptyEntries)
    : null;
bool all = false, rebuild = false, layout = false, archive = false;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        string Value() => i + 1 < args.Length ? args[++i] : throw new KiriException($"{args[i]} needs a value");
        switch (args[i])
        {
            case "-a": case "--all": all = true; break;
            case "-g": case "--git-diff": compare = Value(); break;
            case "-n": case "--newest": newest = Value(); break;
            case "-o": case "--oldest": oldest = Value(); break;
            case "-t": case "--last": last = int.Parse(Value()); break;
            case "-b": case "--branches": revisions = Value().Split(' ', StringSplitOptions.RemoveEmptyEntries); break;
            case "-u": case "--layout": layout = true; break;
            case "-d": case "--output-dir": outputDir = Value(); break;
            case "-r": case "--remove": rebuild = true; break;
            case "-x": case "--archive": archive = true; break;
            case "-j": case "--jobs": jobs = int.Parse(Value()); break;
            case "--git": gitPath = Value(); break;
            case "--kicad-cli": cliPath = Value(); break;
            case "-v": case "--version": await ShowVersionsAsync(); return 0;
            case "-h": case "--help": Console.WriteLine(Usage); return 0;
            default:
                if (args[i].StartsWith('-'))
                    throw new KiriException($"Unknown option '{args[i]}'. See kiri-cli --help.");
                project = args[i];
                break;
        }
    }

    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

    var progress = new SyncProgress(p =>
    {
        if (p.IsError)
            Console.Error.WriteLine(p.Message);
        else
            Console.WriteLine(p.Message);
    });

    var generator = new Generator(new GeneratorOptions
    {
        ProjectPath = project ?? Environment.CurrentDirectory,
        OutputDir = outputDir,
        GitPath = gitPath,
        KiCadCliPath = cliPath,
        AllCommits = all,
        Revisions = revisions,
        Compare = compare,
        NewestCommit = newest,
        OldestCommit = oldest,
        LastCommits = last,
        Rebuild = rebuild,
        StartOnLayout = layout,
        AssetsDir = Path.Combine(AppContext.BaseDirectory, "assets"),
        Parallelism = jobs,
    }, progress);

    var result = await generator.RunAsync(cancel.Token);
    Console.WriteLine();
    Console.WriteLine($"Viewer: {result.IndexHtml}");

    if (archive)
    {
        var zip = Path.GetFullPath($"{Path.GetFileName(result.OutputDir)}-{DateTime.Now:yyyy.MM.dd-HH'h'mm}.zip");
        Console.WriteLine($"Archiving the generated files in {zip}");
        ZipFile.CreateFromDirectory(result.OutputDir, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
    }
    return 0;
}
catch (KiriException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled");
    return 130;
}

static async Task ShowVersionsAsync()
{
    Console.WriteLine($"kiri-cli {typeof(Generator).Assembly.GetName().Version}");
    var git = Tools.FindGit();
    var cli = Tools.FindKiCadCli();
    Console.WriteLine(git != null ? $"{await Tools.GetVersionAsync(git)} ({git})" : "git: not found");
    Console.WriteLine(cli != null ? $"kicad-cli {await Tools.GetVersionAsync(cli)} ({cli})" : "kicad-cli: not found");
}

/// <summary>Reports progress on the calling thread, so console output stays in order.</summary>
sealed class SyncProgress : IProgress<GeneratorProgress>
{
    private readonly Action<GeneratorProgress> _report;
    private readonly object _lock = new();
    public SyncProgress(Action<GeneratorProgress> report) { _report = report; }
    public void Report(GeneratorProgress value) { lock (_lock) _report(value); }
}
