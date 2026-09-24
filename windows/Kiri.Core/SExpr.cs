using System.Text;

namespace Kiri;

/// <summary>
/// A node of a KiCad S-expression file: either a list "(name ...)" or an atom
/// (a bare word, a number or a quoted string).
/// </summary>
public sealed class SExpr
{
    public string? Atom { get; }
    public List<SExpr>? Items { get; }

    public bool IsList => Items != null;

    /// <summary>The first atom of a list, e.g. "layers" for "(layers ...)".</summary>
    public string? Name => Items is { Count: > 0 } && Items[0].Atom != null ? Items[0].Atom : null;

    private SExpr(string atom) { Atom = atom; }
    private SExpr(List<SExpr> items) { Items = items; }

    /// <summary>Child lists with the given name, e.g. all "(sheet ...)" of a schematic.</summary>
    public IEnumerable<SExpr> Children(string name) =>
        Items?.Where(item => item.Name == name) ?? Enumerable.Empty<SExpr>();

    public SExpr? Child(string name) => Children(name).FirstOrDefault();

    /// <summary>The atom at the given position of a list, or null.</summary>
    public string? AtomAt(int index) =>
        Items != null && index < Items.Count ? Items[index].Atom : null;

    public static SExpr ParseFile(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    public static SExpr Parse(string text)
    {
        var parser = new Parser(text);
        return parser.ParseRoot();
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text) { _text = text; }

        public SExpr ParseRoot()
        {
            SkipWhitespace();
            if (_pos >= _text.Length || _text[_pos] != '(')
                throw new FormatException("Not an S-expression file");
            return ParseList();
        }

        private SExpr ParseList()
        {
            _pos++; // '('
            var items = new List<SExpr>();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length)
                    throw new FormatException("Unexpected end of file");
                char c = _text[_pos];
                if (c == ')')
                {
                    _pos++;
                    return new SExpr(items);
                }
                items.Add(c == '(' ? ParseList() : c == '"' ? ParseString() : ParseWord());
            }
        }

        private SExpr ParseString()
        {
            _pos++; // opening quote
            var sb = new StringBuilder();
            while (_pos < _text.Length)
            {
                char c = _text[_pos++];
                if (c == '"')
                    return new SExpr(sb.ToString());
                if (c == '\\' && _pos < _text.Length)
                {
                    char e = _text[_pos++];
                    sb.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => e });
                }
                else
                {
                    sb.Append(c);
                }
            }
            throw new FormatException("Unterminated string");
        }

        private SExpr ParseWord()
        {
            int start = _pos;
            while (_pos < _text.Length && !char.IsWhiteSpace(_text[_pos]) && _text[_pos] != '(' && _text[_pos] != ')')
                _pos++;
            return new SExpr(_text.Substring(start, _pos - start));
        }

        private void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                _pos++;
        }
    }
}
