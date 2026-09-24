using System.Net;
using System.Text;

namespace Kiri;

/// <summary>Copies the viewer into the output folder and fills in the project's details.</summary>
public static class SiteBuilder
{
    private const string EmptyIcon = """<span class="iconify" style="padding-left: 0px; padding-right: 0px; width: 14px; height: 14px; color: #ff0000;" data-inline="false"; data-icon="bx:bx-x"></span>""";
    private const string SchIcon = """<span class="iconify" style="padding-left: 0px; padding-right: 0px; width: 14px; height: 14px; color: #A6E22E;" data-inline="false"; data-icon="carbon:schematics"></span>""";
    private const string PcbIcon = """<span class="iconify" style="padding-left: 0px; padding-right: 0px; width: 14px; height: 14px; color: #F92672;" data-inline="false"; data-icon="codicon:circuit-board"></span>""";
    private const string TxtIcon = """<span class="iconify" style="padding-left: 0px; padding-right: 0px; width: 14px; height: 14px; color: #888888;" data-inline="false"; data-icon="bi:file-earmark-text"></span>""";

    /// <summary>Builds &lt;outputDir&gt;/web/index.html and returns its path.</summary>
    public static string Build(string assetsDir, string outputDir, KiCadProject project, IReadOnlyList<Commit> commits,
        bool startOnLayout)
    {
        var webDir = Path.Combine(outputDir, "web");
        Directory.CreateDirectory(webDir);

        foreach (var file in new[] { "kiri.js", "kiri.css", "blank.svg", "favicon.ico" })
            File.Copy(Path.Combine(assetsDir, file), Path.Combine(webDir, file), overwrite: true);
        CopyDirectory(Path.Combine(assetsDir, "vendor"), Path.Combine(webDir, "vendor"));
        File.Copy(Path.Combine(assetsDir, "redirect.html"), Path.Combine(outputDir, "index.html"), overwrite: true);

        var newest = commits[0];
        var html = File.ReadAllText(Path.Combine(assetsDir, "index.html"), Encoding.UTF8);

        html = InsertAfter(html, "FILL_COMMITS_HERE", CommitsHtml(commits));
        html = InsertAfter(html, "FILL_PAGES_HERE", PagesHtml(outputDir, commits));
        html = InsertAfter(html, "FILL_LAYERS_HERE", LayersHtml(outputDir, commits));

        // The page header shows the newest revision's title blocks
        var newestDir = Path.Combine(outputDir, newest.Hash);
        var proFile = Directory.Exists(newestDir) ? Directory.GetFiles(newestDir, "*.kicad_pro").FirstOrDefault() : null;
        var variables = proFile != null ? KiCadFiles.ReadTextVariables(proFile) : new Dictionary<string, string>();
        var name = proFile != null ? Path.GetFileNameWithoutExtension(proFile) : project.Name;
        var sch = ReadTitleBlock(Path.Combine(newestDir, name + ".kicad_sch"), variables);
        var pcb = ReadTitleBlock(Path.Combine(newestDir, name + ".kicad_pcb"), variables);

        string Or(string value, string fallback) => WebUtility.HtmlEncode(value.Length > 0 ? value : fallback);

        html = html
            .Replace("[PROJECT_TITLE]", WebUtility.HtmlEncode(project.RepoName))
            .Replace("[SCH_TITLE]", "Sch | " + Or(sch.Title, "[title]"))
            .Replace("[PCB_TITLE]", "PCB | " + Or(pcb.Title, "[title]"))
            .Replace("[SCH_REVISION]", Or(sch.Revision, "[rev]"))
            .Replace("[PCB_REVISION]", Or(pcb.Revision, "[rev]"))
            .Replace("[SCH_DATE]", Or(sch.Date, "[date]"))
            .Replace("[PCB_DATE]", Or(pcb.Date, "[date]"))
            .Replace("[COMMIT_1_KICAD_PRO]", WebUtility.HtmlEncode(Path.Combine(outputDir, newest.Hash, name + ".kicad_pro")))
            .Replace("[COMMIT_2_KICAD_PRO]", WebUtility.HtmlEncode(Path.Combine(outputDir, commits[1].Hash, name + ".kicad_pro")));
        File.WriteAllText(Path.Combine(webDir, "index.html"), html, new UTF8Encoding(false));

        var js = File.ReadAllText(Path.Combine(webDir, "kiri.js"), Encoding.UTF8);
        var hasSchematic = commits.Any(c => File.Exists(Path.Combine(outputDir, c.Hash, "_KIRI_", "sch", name + ".svg")));
        if (startOnLayout || !hasSchematic)
            js = js.Replace("selected_view = \"schematic\";", "selected_view = \"layout\";");
        File.WriteAllText(Path.Combine(webDir, "kiri.js"), js, new UTF8Encoding(false));

        return Path.Combine(webDir, "index.html");
    }

    private static TitleBlock ReadTitleBlock(string file, IReadOnlyDictionary<string, string> variables)
    {
        try
        {
            if (File.Exists(file))
                return KiCadFiles.ReadTitleBlock(SExpr.ParseFile(file), variables);
        }
        catch (FormatException)
        {
            // Fall through to an empty title block
        }
        return new TitleBlock("", "", "");
    }

    private static string CommitsHtml(IReadOnlyList<Commit> commits)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < commits.Count; i++)
        {
            var c = commits[i];
            var hash = WebUtility.HtmlEncode(c.Hash);
            var user = HtmlEscape(c.Author);
            var text = HtmlEscape(c.Message);
            var tooltip = HtmlEscape($"<div>Commit: {hash}</br>Date: {c.Date}</br>Author: {user}</br>Description:</br>{text}</div>");
            var textClass = c.IsLocal ? "text-warning" : "text-info";
            var icons = $"{(c.SchematicChanged ? SchIcon : EmptyIcon)} {(c.LayoutChanged ? PcbIcon : EmptyIcon)} {(c.OtherChanged ? TxtIcon : EmptyIcon)}";

            sb.Append($"""
                <!-- Commit {i + 1} -->
                <input class="chkGroup" type="checkbox" id="{hash}" name="commit" value="{hash}" onchange="update_commits()">
                <label class="text-sm-left list-group-item" style="display: block; width: 445px; margin-left: 0px;" for="{hash}">
                    <table data-toggle="tooltip" title="{tooltip}">
                        <tr>
                            <td rowspan=2 style="vertical-align: top; width: 1.8em;">
                                <svg viewBox="0 0 15 15" fill="none" xmlns="http://www.w3.org/2000/svg" width="15" height="15">
                                    <path d="M7.5 10.5a3 3 0 010-6m0 6a3 3 0 000-6m0 6V15m0-10.5V0" stroke="currentColor"></path>
                                </svg>
                            </td>
                            <td style="white-space:nowrap; overflow: hidden; text-overflow: ellipsis;">
                                <span class="text-muted"> {i + 1:00} | </span> <span class="text-success font-weight-normal">{hash}</span> <span class="text-muted"> | </span> {icons} <span class="text-muted font-weight-normal"> | {c.Date} | {user}</span>
                            </td>
                        </tr>
                        <tr>
                            <td>
                                <em class="{textClass}" style=" line-height: 0.7;">{text}</em>
                            </td>
                        </tr>
                    </table>
                </label>

                """);
        }
        return sb.ToString();
    }

    /// <summary>The sheets of all commits, each sheet file once, in the order they first appear.</summary>
    private static string PagesHtml(string outputDir, IReadOnlyList<Commit> commits)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var line in commits.SelectMany(c => ReadLines(Path.Combine(outputDir, c.Hash, "_KIRI_", "sch_sheets"))))
        {
            var fields = line.Split('|');
            if (fields.Length < 2 || !seen.Add(fields[1]))
                continue;
            var name = WebUtility.HtmlEncode(fields[0]);
            var path = WebUtility.HtmlEncode(fields[1]);
            var file = WebUtility.HtmlEncode(Path.GetFileName(fields[1]));
            var isChecked = seen.Count == 1 ? "checked=\"checked\"" : "";
            sb.Append($"""
                <!-- Page {seen.Count} -->
                <input id="{name}" data-toggle="tooltip" title="{path}" type="radio" value="{file}" name="pages" {isChecked} onchange="change_page()">
                <label for="{name}" data-toggle="tooltip" title="{path}" id="label-{name}" class="rounded text-sm-left list-group-item radio-box" onclick="change_page_onclick()" style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
                    <span data-toggle="tooltip" title="{path}" style="margin-left:0.5em; margin-right:0.1em;" class="iconify" data-icon="bi:file-earmark" data-inline="false"></span>
                    {name}
                </label>

                """);
        }
        return sb.ToString();
    }

    /// <summary>The layers of all commits, each layer number once, in the order they first appear.</summary>
    private static string LayersHtml(string outputDir, IReadOnlyList<Commit> commits)
    {
        var seen = new HashSet<string>();
        var sb = new StringBuilder();
        foreach (var line in commits.SelectMany(c => ReadLines(Path.Combine(outputDir, c.Hash, "_KIRI_", "pcb_layers"))))
        {
            var fields = line.Split('|');
            if (fields.Length < 2 || !int.TryParse(fields[0], out var id) || !seen.Add(fields[0]))
                continue;
            var name = WebUtility.HtmlEncode(fields[1].Replace('.', '_'));
            var isChecked = seen.Count == 1 ? "checked='checked'" : "";
            sb.Append($"""
                <!-- Layer {seen.Count} -->
                <input  id="layer-{id:00}" value="layer-{name}" type="radio" name="layers" onchange="change_layer()" {isChecked}>
                <label for="layer-{id:00}" id="label-layer-{id:00}" data-toggle="tooltip" title="{id}, {name}" class="rounded text-sm-left list-group-item radio-box" onclick="change_layer_onclick()">
                    <span style="margin-left:0.5em; margin-right:0.1em; color: {LayerColor(fields[1])}" class="iconify" data-icon="teenyicons-square-solid" data-inline="false"></span>
                    {name}
                </label>

                """);
        }
        return sb.ToString();
    }

    /// <summary>The viewer's colour for a layer, by name (KiCad 9 renumbered the layers).</summary>
    private static string LayerColor(string name) => name switch
    {
        "F.Cu" => "#952927", "B.Cu" => "#359632",
        "In1.Cu" => "#C2C200", "In2.Cu" => "#C200C2", "In3.Cu" => "#C20000", "In4.Cu" => "#0000C2",
        "F.Adhesive" or "F.Adhes" => "#A74AA8", "B.Adhesive" or "B.Adhes" => "#3545A8",
        "F.Paste" => "#3DC9C9", "B.Paste" => "#969696",
        "F.Silkscreen" or "F.SilkS" => "#339697", "B.Silkscreen" or "B.SilkS" => "#481649",
        "F.Mask" or "B.Mask" => "#943197",
        "User.Drawings" or "Dwgs.User" => "#0364D3", "User.Comments" or "Cmts.User" => "#7AC0F4",
        "User.Eco1" or "Eco1.User" or "User.Eco2" or "Eco2.User" => "#008500",
        "Edge.Cuts" => "#C9C83B", "Margin" => "#D357D2",
        "F.Courtyard" or "F.CrtYd" => "#A7A7A7", "B.Courtyard" or "B.CrtYd" => "#D3D04B",
        "F.Fab" => "#C2C200", "B.Fab" => "#858585",
        _ => "#DBDBDB",
    };

    /// <summary>Escapes text for HTML content and attribute values.</summary>
    private static string HtmlEscape(string text) => text
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;").Replace("\\", "&#92;");

    private static string InsertAfter(string html, string marker, string content)
    {
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return html;
        var lineEnd = html.IndexOf('\n', at);
        return lineEnd < 0 ? html + "\n" + content : html.Insert(lineEnd + 1, content);
    }

    private static IEnumerable<string> ReadLines(string path) =>
        File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0) : Enumerable.Empty<string>();

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
