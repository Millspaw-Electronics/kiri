using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kiri;

/// <summary>A schematic sheet instance, as listed in the viewer's sch_sheets file.</summary>
/// <param name="FileName">The sheet's file name without folder or extension, e.g. "channel".</param>
/// <param name="RelativePath">The sheet's path relative to the project folder, e.g. "sub/channel.kicad_sch".</param>
/// <param name="InstanceName">The sheet's name in its parent, e.g. "Chan A".</param>
/// <param name="PlotName">The name kicad-cli gives the sheet's SVG, e.g. "board-Chan A".</param>
public sealed record SheetInfo(string FileName, string RelativePath, string InstanceName, string PlotName)
{
    // Format read by the viewer: FILE_NAME|RELATIVE_PATH|UUID|INSTANCE_NAME|PLOT_NAME
    public string ToLine() => $"{FileName}|{RelativePath}||{InstanceName}|{PlotName}";
}

/// <summary>A board layer: KiCad's layer number, internal name and display name.</summary>
public sealed record LayerInfo(int Id, string Name, string DisplayName)
{
    // Format read by the viewer: ID|DISPLAY_NAME
    public string ToLine() => $"{Id}|{DisplayName}";
}

public sealed record TitleBlock(string Title, string Revision, string Date);

public static class KiCadFiles
{
    /// <summary>
    /// Walks a schematic hierarchy from the root sheet. Every instance of a sheet
    /// is listed, so a sheet used twice appears twice (with the same file name).
    /// </summary>
    public static List<SheetInfo> ReadSheets(string projectDir, string rootSchematic)
    {
        var sheets = new List<SheetInfo>();
        var rootName = Path.GetFileNameWithoutExtension(rootSchematic);
        var rootPath = Path.Combine(projectDir, rootSchematic);
        if (!File.Exists(rootPath))
            return sheets;

        sheets.Add(new SheetInfo(rootName, rootSchematic, rootName, rootName));
        Walk(rootPath, rootName, depth: 0);
        return sheets;

        void Walk(string schPath, string plotName, int depth)
        {
            // Guard against a sheet that (directly or not) contains itself
            if (depth > 32)
                return;

            SExpr sch;
            try { sch = SExpr.ParseFile(schPath); }
            catch (Exception e) when (e is FormatException or IOException) { return; }

            foreach (var sheet in sch.Children("sheet"))
            {
                string? name = null, file = null;
                foreach (var property in sheet.Children("property"))
                {
                    switch (property.AtomAt(1))
                    {
                        case "Sheetname": case "Sheet name": name = property.AtomAt(2); break;
                        case "Sheetfile": case "Sheet file": file = property.AtomAt(2); break;
                    }
                }
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(file))
                    continue;

                // KiCad resolves sheet files against the project folder; fall back
                // to the parent sheet's folder for older files
                var childPath = Path.GetFullPath(Path.Combine(projectDir, file));
                if (!File.Exists(childPath))
                    childPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(schPath)!, file));

                var relative = Path.GetRelativePath(projectDir, childPath).Replace('\\', '/');
                var childPlotName = $"{plotName}-{name}";
                sheets.Add(new SheetInfo(Path.GetFileNameWithoutExtension(file), relative, name, childPlotName));

                if (File.Exists(childPath))
                    Walk(childPath, childPlotName, depth + 1);
            }
        }
    }

    /// <summary>Reads the layer table of a .kicad_pcb, in file order.</summary>
    public static List<LayerInfo> ReadLayers(SExpr pcb)
    {
        var layers = new List<LayerInfo>();
        var table = pcb.Child("layers");
        if (table?.Items == null)
            return layers;

        // (9 "F.Adhes" user "F.Adhesive"): number, name, type and an optional display name
        foreach (var layer in table.Items.Skip(1).Where(item => item.IsList))
        {
            if (!int.TryParse(layer.AtomAt(0), out var id) || layer.AtomAt(1) is not { } name)
                continue;
            var displayName = layer.Items!.Count > 3 ? layer.AtomAt(3) ?? name : name;
            layers.Add(new LayerInfo(id, name, displayName));
        }
        return layers;
    }

    public static TitleBlock ReadTitleBlock(SExpr file, IReadOnlyDictionary<string, string> variables)
    {
        var block = file.Child("title_block");
        string Field(string name) => ExpandTextVariables(block?.Child(name)?.AtomAt(1) ?? "", variables);
        return new TitleBlock(Field("title"), Field("rev"), Field("date"));
    }

    /// <summary>The project's text variables, plus the built-in PROJECTNAME.</summary>
    public static Dictionary<string, string> ReadTextVariables(string kicadPro)
    {
        var variables = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(kicadPro));
            if (doc.RootElement.TryGetProperty("text_variables", out var table) && table.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in table.EnumerateObject())
                    variables[entry.Name] = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()! : entry.Value.ToString();
            }
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Leave the variables unexpanded
        }
        variables.TryAdd("PROJECTNAME", Path.GetFileNameWithoutExtension(kicadPro));
        return variables;
    }

    private static readonly Regex TextVariable = new(@"\$\{([^{}]+)\}", RegexOptions.Compiled);

    /// <summary>Expands ${VAR} references, including variables whose values use other variables.</summary>
    public static string ExpandTextVariables(string text, IReadOnlyDictionary<string, string> variables)
    {
        for (int i = 0; i < 10; i++)
        {
            var expanded = TextVariable.Replace(text, m => variables.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
            if (expanded == text)
                break;
            text = expanded;
        }
        return text;
    }

    /// <summary>The file format version, e.g. 20250114, from "(version 20250114)".</summary>
    public static string ReadFormatVersion(SExpr file) => file.Child("version")?.AtomAt(1) ?? "";
}
