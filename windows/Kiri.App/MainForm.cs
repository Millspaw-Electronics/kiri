using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Kiri.App;

public sealed class MainForm : Form
{
    // The app's own pages and the generated viewer are served from two virtual hosts
    private const string AppHost = "app.kiri.local";
    private const string ViewerHost = "viewer.kiri.local";
    private const string StartPage = $"https://{AppHost}/start.html";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Settings _settings = Settings.Load();
    private readonly string? _initialPath;

    private readonly ToolStripMenuItem _recentMenu = new("Open &Recent");
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _rebuildItem;
    private readonly ToolStripMenuItem _outputItem;
    private readonly ToolStripMenuItem _closeItem;

    private KiCadProject? _project;
    private string? _outputDir;
    private CancellationTokenSource? _cancel;
    private TaskCompletionSource? _startPageReady;
    private readonly List<GeneratorProgress> _log = new();

    public MainForm(string? initialPath)
    {
        _initialPath = initialPath;

        Text = "KiRI";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        // Sizes are in pixels, so scale them for the display
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 1000);
        MinimumSize = LogicalToDeviceUnits(new Size(900, 600));
        Size = new Size(area.Width * 9 / 10, area.Height * 9 / 10);
        StartPosition = FormStartPosition.CenterScreen;
        if (_settings.Maximized)
            WindowState = FormWindowState.Maximized;

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&Open Repository...", null, (_, _) => BrowseForFolder(), Keys.Control | Keys.O));
        file.DropDownItems.Add(_recentMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        _refreshItem = new ToolStripMenuItem("&Refresh", null, (_, _) => RefreshProject(rebuild: false), Keys.F5);
        _rebuildItem = new ToolStripMenuItem("Re&build All Revisions", null, (_, _) => RefreshProject(rebuild: true), Keys.Control | Keys.Shift | Keys.F5);
        _outputItem = new ToolStripMenuItem("Show &Generated Files", null, (_, _) => OpenOutputFolder());
        _closeItem = new ToolStripMenuItem("&Close Project", null, (_, _) => CloseProject(), Keys.Control | Keys.W);
        file.DropDownItems.AddRange(new ToolStripItem[] { _refreshItem, _rebuildItem, _outputItem, _closeItem, new ToolStripSeparator() });
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close(), Keys.Alt | Keys.F4));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(new ToolStripMenuItem("&Keyboard Shortcuts", null, (_, _) => ShowShortcuts(), Keys.F1));
        help.DropDownItems.Add(new ToolStripMenuItem("KiRI on &GitHub", null, (_, _) => OpenUrl("https://github.com/leoheck/kiri")));
        help.DropDownItems.Add(new ToolStripMenuItem("&About KiRI", null, (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, help });
        MainMenuStrip = menu;

        Controls.Add(_web);
        Controls.Add(menu);

        file.DropDownOpening += (_, _) => UpdateMenus();
        UpdateMenus();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "WebView2"));
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "KiRI needs the Microsoft Edge WebView2 Runtime, which is part of Windows 11.\n\n" +
                "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and start KiRI again.",
                "KiRI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("KIRI_DEVTOOLS") == "1";
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = core.Settings.AreDevToolsEnabled;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += (_, args) =>
        {
            // Links with target="_blank" open in the default browser
            args.Handled = true;
            OpenUrl(args.Uri);
        };
        core.DocumentTitleChanged += (_, _) => UpdateTitle();

        await ShowStartPageAsync();
        if (!string.IsNullOrEmpty(_initialPath))
            OpenPath(_initialPath);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cancel?.Cancel();
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        _settings.Save();
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------
    // Pages

    private async Task ShowStartPageAsync()
    {
        if (_web.CoreWebView2 == null)
            return;
        if (_web.Source?.Host == AppHost && _startPageReady?.Task.IsCompleted == true)
            return;

        _startPageReady = new TaskCompletionSource();
        _web.CoreWebView2.Navigate(StartPage);
        await _startPageReady.Task;
    }

    private void Post(object message)
    {
        if (_web.CoreWebView2 != null && _web.Source?.Host == AppHost)
            _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private async void PostState()
    {
        var git = Tools.FindGit(_settings.GitPath);
        var cli = Tools.FindKiCadCli(_settings.KiCadCliPath);
        var recent = _settings.RecentProjects
            .Select(p => new { path = p, name = Path.GetFileNameWithoutExtension(p), folder = Path.GetDirectoryName(p), exists = File.Exists(p) })
            .ToList();

        Post(new
        {
            type = "state",
            version = Application.ProductVersion.Split('+')[0],
            recent,
            busy = _cancel != null,
            git = new { path = git, version = git != null ? await Tools.GetVersionAsync(git) : null, custom = _settings.GitPath != null },
            kicad = new { path = cli, version = cli != null ? await Tools.GetVersionAsync(cli) : null, custom = _settings.KiCadCliPath != null, min = Tools.MinKiCadMajor },
        });
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? message;
        try { message = JsonNode.Parse(e.WebMessageAsJson); }
        catch (JsonException) { return; }

        var action = message?["action"]?.GetValue<string>();
        var path = message?["path"]?.GetValue<string>();
        switch (action)
        {
            case "ready":
                _startPageReady?.TrySetResult();
                PostState();
                break;
            case "open_folder":
                BrowseForFolder();
                break;
            case "open_project" when path != null:
                OpenPath(path);
                break;
            case "remove_recent" when path != null:
                _settings.RemoveRecent(path);
                UpdateMenus();
                PostState();
                break;
            case "cancel":
                _cancel?.Cancel();
                break;
            case "set_tool":
                ChooseTool(message?["tool"]?.GetValue<string>());
                break;
            case "reset_tool":
                if (message?["tool"]?.GetValue<string>() == "git") _settings.GitPath = null; else _settings.KiCadCliPath = null;
                _settings.Save();
                PostState();
                break;
            case "open_url":
                OpenUrl(message?["url"]?.GetValue<string>() ?? "");
                break;
            case "launch_kicad":
                LaunchKiCad(message?["hash"]?.GetValue<string>());
                break;
        }
    }

    // ------------------------------------------------------------------
    // Opening projects

    private void BrowseForFolder()
    {
        if (_cancel != null)
            return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a Git repository folder that contains a KiCad project",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (_project != null)
            dialog.InitialDirectory = _project.RepoRoot;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            OpenPath(dialog.SelectedPath);
    }

    private async void OpenPath(string path)
    {
        if (_cancel != null)
            return;
        await ShowStartPageAsync();

        path = Path.GetFullPath(path.Trim('"'));
        if (File.Exists(path) && path.EndsWith(".kicad_pro", StringComparison.OrdinalIgnoreCase))
        {
            await GenerateAsync(path, rebuild: false);
            return;
        }
        if (!Directory.Exists(path))
        {
            Post(new { type = "error", message = $"{path} doesn't exist." });
            return;
        }

        var projects = Generator.FindProjects(path);
        var top = projects.Where(p => string.Equals(Path.GetDirectoryName(p), path, StringComparison.OrdinalIgnoreCase)).ToList();
        if (projects.Count == 0)
            Post(new { type = "error", message = $"No KiCad project (.kicad_pro) was found in {path}." });
        else if (top.Count == 1 || projects.Count == 1)
            await GenerateAsync(top.Count == 1 ? top[0] : projects[0], rebuild: false);
        else
            Post(new
            {
                type = "choose",
                folder = path,
                projects = projects.Select(p => new { path = p, name = Path.GetRelativePath(path, p) }),
            });
    }

    private void RefreshProject(bool rebuild)
    {
        if (_project != null && _cancel == null)
            _ = GenerateAsync(_project.ProjectFile, rebuild);
    }

    private async Task GenerateAsync(string projectFile, bool rebuild)
    {
        await ShowStartPageAsync();

        _cancel = new CancellationTokenSource();
        _log.Clear();
        UpdateMenus();
        Post(new { type = "busy", project = Path.GetFileNameWithoutExtension(projectFile), folder = Path.GetDirectoryName(projectFile) });

        var progress = new Progress<GeneratorProgress>(p =>
        {
            _log.Add(p);
            Post(new { type = "progress", message = p.Message, fraction = p.Fraction, isError = p.IsError });
        });

        var generator = new Generator(new GeneratorOptions
        {
            ProjectPath = projectFile,
            GitPath = _settings.GitPath,
            KiCadCliPath = _settings.KiCadCliPath,
            Rebuild = rebuild,
            AssetsDir = Path.Combine(AppContext.BaseDirectory, "assets"),
        }, progress);

        try
        {
            var token = _cancel.Token;
            var result = await Task.Run(() => generator.RunAsync(token), token);
            _project = result.Project;
            _outputDir = result.OutputDir;
            _settings.AddRecent(result.Project.ProjectFile);

            // Commit folders never change, but the working copy's drawings do
            await _web.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            _web.CoreWebView2.ClearVirtualHostNameToFolderMapping(ViewerHost);
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(ViewerHost, result.OutputDir, CoreWebView2HostResourceAccessKind.Allow);
            _web.CoreWebView2.Navigate($"https://{ViewerHost}/web/index.html");
        }
        catch (OperationCanceledException)
        {
            Post(new { type = "error", message = "Cancelled." });
        }
        catch (Exception e) when (e is KiriException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Post(new { type = "error", message = e.Message });
        }
        finally
        {
            _cancel.Dispose();
            _cancel = null;
            UpdateMenus();
            UpdateTitle();
        }
    }

    private async void CloseProject()
    {
        if (_cancel != null)
            return;
        _project = null;
        _outputDir = null;
        await ShowStartPageAsync();
        PostState();
        UpdateMenus();
        UpdateTitle();
    }

    // ------------------------------------------------------------------
    // Actions

    private void LaunchKiCad(string? hash)
    {
        var cli = Tools.FindKiCadCli(_settings.KiCadCliPath);
        var kicad = cli != null ? Tools.FindKiCad(cli) : null;
        if (_project == null || _outputDir == null || string.IsNullOrEmpty(hash) || kicad == null)
            return;

        // The working copy opens the real project; commits open their exported copy
        var projectFile = hash == Commit.LocalHash
            ? _project.ProjectFile
            : Path.Combine(_outputDir, hash, Path.GetFileName(_project.ProjectFile));
        if (!File.Exists(projectFile))
        {
            MessageBox.Show(this, $"{projectFile} doesn't exist.", "KiRI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo(kicad) { ArgumentList = { projectFile }, UseShellExecute = false });
    }

    private void ChooseTool(string? tool)
    {
        var isGit = tool == "git";
        using var dialog = new OpenFileDialog
        {
            Title = isGit ? "Choose git.exe" : "Choose kicad-cli.exe (in KiCad's bin folder)",
            Filter = isGit ? "git.exe|git.exe|Programs|*.exe" : "kicad-cli.exe|kicad-cli.exe|Programs|*.exe",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        if (isGit) _settings.GitPath = dialog.FileName; else _settings.KiCadCliPath = dialog.FileName;
        _settings.Save();
        PostState();
    }

    private void OpenOutputFolder()
    {
        if (_outputDir != null && Directory.Exists(_outputDir))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { _outputDir } });
    }

    private async void ShowShortcuts()
    {
        if (_web.CoreWebView2 != null && _web.Source?.Host == ViewerHost)
            await _web.CoreWebView2.ExecuteScriptAsync("show_info_popup()");
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"KiRI {Application.ProductVersion.Split('+')[0]}\nKiCad Revision Inspector for Windows\n\n" +
            "Based on KiRI by Leandro Heck (https://github.com/leoheck/kiri).\nMIT licence.",
            "About KiRI", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------
    // Menus and title

    private void UpdateMenus()
    {
        var idle = _cancel == null;
        var open = _project != null && idle;
        _refreshItem.Enabled = open;
        _rebuildItem.Enabled = open;
        _outputItem.Enabled = _outputDir != null;
        _closeItem.Enabled = open;

        _recentMenu.DropDownItems.Clear();
        foreach (var path in _settings.RecentProjects)
        {
            var item = new ToolStripMenuItem(path.Replace("&", "&&"), null, (_, _) => OpenPath(path)) { Enabled = idle };
            _recentMenu.DropDownItems.Add(item);
        }
        _recentMenu.Enabled = idle && _recentMenu.DropDownItems.Count > 0;
    }

    private void UpdateTitle()
    {
        Text = _project != null && _web.Source?.Host == ViewerHost
            ? $"{_project.RepoName} ({_project.Name}) - KiRI"
            : "KiRI";
    }
}
