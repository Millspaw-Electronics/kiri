namespace Kiri;

/// <summary>Plots schematics and layouts to SVG with kicad-cli, named the way the viewer expects.</summary>
public sealed class KiCadCli
{
    private readonly string _exe;

    public KiCadCli(string exe) { _exe = exe; }

    /// <summary>
    /// Plots every sheet to &lt;outputDir&gt;/&lt;sheet file name&gt;.svg. kicad-cli names the
    /// files after the sheet path ("board-Chan A.svg"), so they're renamed afterwards.
    /// Returns any problems found.
    /// </summary>
    public async Task<IReadOnlyList<string>> PlotSchematicAsync(string schFile, string outputDir,
        IReadOnlyList<SheetInfo> sheets, CancellationToken ct)
    {
        var problems = new List<string>();
        var plotDir = Path.Combine(outputDir, ".plot");
        Directory.CreateDirectory(plotDir);

        var result = await ProcessRunner.RunAsync(_exe, new[]
        {
            "sch", "export", "svg", "--black-and-white", "--no-background-color",
            "--output", plotDir, schFile,
        }, Path.GetDirectoryName(schFile), ct);
        if (!result.Ok)
            problems.Add($"kicad-cli could not plot {Path.GetFileName(schFile)}: {LastLine(result)}");

        var plotted = Directory.GetFiles(plotDir, "*.svg").ToDictionary(file => Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            // kicad-cli replaces characters that aren't allowed in file names
            if (!plotted.TryGetValue(sheet.PlotName, out var source) && !plotted.TryGetValue(SafeFileName(sheet.PlotName), out source))
            {
                if (result.Ok)
                    problems.Add($"sheet \"{sheet.InstanceName}\" ({sheet.RelativePath}) was not plotted");
                continue;
            }
            // A sheet used more than once keeps the last instance's drawing
            File.Copy(source, Path.Combine(outputDir, sheet.FileName + ".svg"), overwrite: true);
        }

        Directory.Delete(plotDir, recursive: true);
        return problems;
    }

    /// <summary>
    /// Plots every layer to &lt;outputDir&gt;/layer-NN.svg in a single kicad-cli run, with the
    /// board outline on each layer. Returns any problems found.
    /// </summary>
    public async Task<IReadOnlyList<string>> PlotLayoutAsync(string pcbFile, string outputDir,
        IReadOnlyList<LayerInfo> layers, CancellationToken ct)
    {
        var problems = new List<string>();
        var plotDir = Path.Combine(outputDir, ".plot");
        Directory.CreateDirectory(plotDir);

        // Starting kicad-cli and loading the board takes most of the time, so plotting
        // all layers in one run is much faster than one run per layer
        var result = await ProcessRunner.RunAsync(_exe, new[]
        {
            "pcb", "export", "svg", "--mode-multi", "--page-size-mode", "0", "--black-and-white",
            "--common-layers", "Edge.Cuts",
            "--layers", string.Join(",", layers.Select(l => l.Name)),
            "--output", plotDir, pcbFile,
        }, Path.GetDirectoryName(pcbFile), ct);
        if (!result.Ok)
            problems.Add($"kicad-cli could not plot {Path.GetFileName(pcbFile)}: {LastLine(result)}");

        // Files are named after the layer's display name, e.g. "board-F_Silkscreen.svg"
        var board = Path.GetFileNameWithoutExtension(pcbFile);
        foreach (var layer in layers)
        {
            var source = Path.Combine(plotDir, $"{board}-{SafeFileName(layer.DisplayName.Replace('.', '_'))}.svg");
            if (!File.Exists(source))
            {
                if (result.Ok)
                    problems.Add($"layer {layer.Name} was not plotted");
                continue;
            }
            File.Move(source, Path.Combine(outputDir, $"layer-{layer.Id:00}.svg"), overwrite: true);
        }

        Directory.Delete(plotDir, recursive: true);
        return problems;
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static string LastLine(ProcessResult result)
    {
        var text = (result.StdErr + "\n" + result.StdOut).Trim();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault(l => !l.StartsWith("Plotted to")) ?? $"exit code {result.ExitCode}";
    }
}
