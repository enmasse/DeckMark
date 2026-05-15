using System.Text;
using System.Text.RegularExpressions;
using DeckMark.Core.Model;

namespace DeckMark.Core.Parser;

/// <summary>
/// Parses DeckMark source text into a <see cref="DeckDocument"/>.
/// </summary>
public static partial class DeckMarkParser
{
    public static DeckDocument Parse(string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        int pos = 0;

        var header = ParseDeckHeader(lines, ref pos);
        var slides = ParseSlides(lines, pos);

        return new DeckDocument { Header = header, Slides = slides };
    }

    // ── Deck header ──────────────────────────────────────────────────────────

    private static DeckHeader ParseDeckHeader(string[] lines, ref int pos)
    {
        // Skip blank lines before :::deck
        while (pos < lines.Length && string.IsNullOrWhiteSpace(lines[pos]))
            pos++;

        if (pos >= lines.Length || lines[pos].Trim() != ":::deck")
            return new DeckHeader();

        pos++; // consume :::deck

        var kvs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (pos < lines.Length && lines[pos].Trim() != ":::")
        {
            var line = lines[pos++];
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                kvs[key] = value;
            }
        }
        if (pos < lines.Length) pos++; // consume closing :::

        return new DeckHeader
        {
            Title = kvs.GetValueOrDefault("title", string.Empty),
            Subtitle = kvs.GetValueOrDefault("subtitle", string.Empty),
            Author = kvs.GetValueOrDefault("author", string.Empty),
            Event = kvs.GetValueOrDefault("event", string.Empty),
            Theme = kvs.GetValueOrDefault("theme", string.Empty),
            Aspect = kvs.GetValueOrDefault("aspect", "16:9"),
            Footer = kvs.GetValueOrDefault("footer", string.Empty),
            Language = kvs.GetValueOrDefault("language", "en-US"),
            Company = kvs.GetValueOrDefault("company", string.Empty),
        };
    }

    // ── Slides ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<Slide> ParseSlides(string[] lines, int start)
    {
        // Split on top-level "---" lines
        var segments = new List<List<string>>();
        List<string>? current = null;

        for (int i = start; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                current = [];
                segments.Add(current);
            }
            else
            {
                current?.Add(lines[i]);
            }
        }

        return segments.Select(ParseSlide).ToList();
    }

    private static Slide ParseSlide(List<string> lines)
    {
        int pos = 0;

        // Skip leading blanks
        while (pos < lines.Count && string.IsNullOrWhiteSpace(lines[pos]))
            pos++;

        // Title: first # heading
        string title = string.Empty;
        if (pos < lines.Count && lines[pos].StartsWith("# "))
        {
            title = lines[pos][2..].Trim();
            pos++;
        }

        // Directives: consecutive @key: value lines
        string? id = null, layout = "content", background = null, transition = null, build = null, footer = null;
        while (pos < lines.Count && lines[pos].TrimStart().StartsWith('@'))
        {
            var directive = lines[pos++].Trim();
            var colon = directive.IndexOf(':');
            if (colon < 0) continue;
            var key = directive[1..colon].Trim();
            var value = directive[(colon + 1)..].Trim();
            switch (key)
            {
                case "id": id = value; break;
                case "layout": layout = value; break;
                case "background": background = value; break;
                case "transition": transition = value; break;
                case "build": build = value; break;
                case "footer": footer = value; break;
            }
        }

        // Body
        var bodyLines = lines.Skip(pos).ToList();
        var (body, notes) = ParseBody(bodyLines);

        return new Slide
        {
            Title = title,
            Id = id,
            Layout = layout,
            Background = background,
            Transition = transition,
            Build = build,
            Footer = footer,
            Body = body,
            Notes = notes,
        };
    }

    // ── Body parser ──────────────────────────────────────────────────────────

    private static (IReadOnlyList<ContentBlock> body, IReadOnlyList<ContentBlock> notes) ParseBody(List<string> lines)
    {
        var body = new List<ContentBlock>();
        var notes = new List<ContentBlock>();

        int pos = 0;
        while (pos < lines.Count)
        {
            var line = lines[pos];

            // Fenced code / mermaid block
            if (line.TrimStart().StartsWith("```"))
            {
                var block = ReadFencedCodeBlock(lines, ref pos);
                body.Add(block);
                continue;
            }

            // ::: directive blocks
            if (line.TrimStart().StartsWith(":::"))
            {
                var tag = line.Trim()[3..].Trim();
                if (tag == "notes")
                {
                    var content = ReadDirectiveBlock(lines, ref pos, "notes");
                    notes.Add(new ContentBlock { Kind = BlockKind.Notes, RawContent = content });
                }
                else if (tag == "columns")
                {
                    var block = ReadColumnsBlock(lines, ref pos);
                    body.Add(block);
                }
                else if (tag.StartsWith("callout"))
                {
                    var block = ReadCalloutBlock(lines, ref pos, tag);
                    body.Add(block);
                }
                else
                {
                    // Unknown directive – skip
                    ReadDirectiveBlock(lines, ref pos, tag.Split(' ')[0]);
                }
                continue;
            }

            // Image line: ![alt](url){attrs}
            if (TryParseImageLine(line, out var imgBlock))
            {
                body.Add(imgBlock!);
                pos++;
                continue;
            }

            // Accumulate general Markdown lines into a raw paragraph/list/etc.
            var mdBlock = ReadMarkdownBlock(lines, ref pos);
            if (mdBlock is not null)
                body.Add(mdBlock);
        }

        return (body, notes);
    }

    // ── Fenced code block ────────────────────────────────────────────────────

    private static ContentBlock ReadFencedCodeBlock(List<string> lines, ref int pos)
    {
        var openFence = lines[pos].TrimStart();
        var lang = openFence[3..].Trim().ToLowerInvariant();
        pos++;
        var sb = new StringBuilder();
        while (pos < lines.Count && !lines[pos].TrimStart().StartsWith("```"))
            sb.AppendLine(lines[pos++]);
        if (pos < lines.Count) pos++; // consume closing ```

        var kind = lang == "mermaid" ? BlockKind.MermaidBlock : BlockKind.CodeBlock;
        return new ContentBlock { Kind = kind, Language = lang, RawContent = sb.ToString().TrimEnd() };
    }

    // ── Generic ::: directive block reader ───────────────────────────────────

    private static string ReadDirectiveBlock(List<string> lines, ref int pos, string expectedTag)
    {
        pos++; // consume opening :::tag
        var sb = new StringBuilder();
        int depth = 1;
        while (pos < lines.Count)
        {
            var line = lines[pos++];
            if (line.TrimStart().StartsWith(":::"))
            {
                var inner = line.Trim()[3..].Trim();
                if (string.IsNullOrEmpty(inner))
                    depth--;
                else
                    depth++;
                if (depth == 0) break;
            }
            else
            {
                sb.AppendLine(line);
            }
        }
        return sb.ToString().TrimEnd();
    }

    // ── Columns block ────────────────────────────────────────────────────────

    private static ContentBlock ReadColumnsBlock(List<string> lines, ref int pos)
    {
        pos++; // consume :::columns
        var leftLines = new List<string>();
        var centerLines = new List<string>();
        var rightLines = new List<string>();
        List<string>? active = null;
        int depth = 1;

        while (pos < lines.Count)
        {
            var line = lines[pos++];
            var trimmed = line.Trim();
            if (trimmed.StartsWith(":::"))
            {
                var tag = trimmed[3..].Trim();
                if (string.IsNullOrEmpty(tag))
                {
                    depth--;
                    if (depth == 0) break;
                    active = null;
                }
                else if (tag == "left") { active = leftLines; depth++; }
                else if (tag == "center") { active = centerLines; depth++; }
                else if (tag == "right") { active = rightLines; depth++; }
                else depth++;
            }
            else
            {
                active?.Add(line);
            }
        }

        var (leftBody, _) = ParseBody(leftLines);
        var (centerBody, _) = ParseBody(centerLines);
        var (rightBody, _) = ParseBody(rightLines);

        return new ContentBlock
        {
            Kind = BlockKind.Columns,
            Left = leftBody,
            Center = centerBody,
            Right = rightBody,
        };
    }

    // ── Callout block ────────────────────────────────────────────────────────

    private static readonly Regex CalloutTypeRe = new(@"type=(\S+)", RegexOptions.Compiled);
    private static readonly Regex CalloutTitleRe = new(@"title=""([^""]+)""", RegexOptions.Compiled);

    private static ContentBlock ReadCalloutBlock(List<string> lines, ref int pos, string tagLine)
    {
        var calloutType = CalloutTypeRe.Match(tagLine).Groups[1].Value;
        var calloutTitle = CalloutTitleRe.Match(tagLine).Groups[1].Value;
        var content = ReadDirectiveBlock(lines, ref pos, "callout");
        return new ContentBlock
        {
            Kind = BlockKind.Callout,
            CalloutType = string.IsNullOrEmpty(calloutType) ? null : calloutType,
            CalloutTitle = string.IsNullOrEmpty(calloutTitle) ? null : calloutTitle,
            RawContent = content,
        };
    }

    // ── Image line ───────────────────────────────────────────────────────────

    private static readonly Regex ImageRe = new(
        @"^!\[(?<alt>[^\]]*)\]\((?<url>[^)]+)\)(?:\{(?<attrs>[^}]*)\})?",
        RegexOptions.Compiled);

    private static bool TryParseImageLine(string line, out ContentBlock? block)
    {
        var m = ImageRe.Match(line.Trim());
        if (!m.Success) { block = null; return false; }

        var attrs = ParseAttrs(m.Groups["attrs"].Value);
        block = new ContentBlock
        {
            Kind = BlockKind.Image,
            AltText = m.Groups["alt"].Value,
            ImageUrl = m.Groups["url"].Value,
            ImageWidth = attrs.GetValueOrDefault("width"),
            ImageHeight = attrs.GetValueOrDefault("height"),
            ImageAlign = attrs.GetValueOrDefault("align"),
        };
        return true;
    }

    private static Dictionary<string, string> ParseAttrs(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(raw, @"(\w+)=([^\s}]+)"))
            result[m.Groups[1].Value] = m.Groups[2].Value;
        return result;
    }

    // ── Generic Markdown block ───────────────────────────────────────────────

    private static ContentBlock? ReadMarkdownBlock(List<string> lines, ref int pos)
    {
        // Skip leading blank lines
        while (pos < lines.Count && string.IsNullOrWhiteSpace(lines[pos]))
            pos++;

        if (pos >= lines.Count) return null;

        var firstLine = lines[pos];

        // A heading line is always a single-line block
        if (firstLine.TrimStart().StartsWith('#'))
        {
            pos++;
            return new ContentBlock { Kind = BlockKind.Heading, RawContent = firstLine.Trim() };
        }

        var sb = new StringBuilder();
        var startKind = DetermineMarkdownKind(firstLine);

        // Accumulate until blank line, heading, or special line
        while (pos < lines.Count)
        {
            var line = lines[pos];

            if (string.IsNullOrWhiteSpace(line))
            {
                pos++; // consume the blank line
                break;
            }
            if (line.TrimStart().StartsWith(":::") ||
                line.TrimStart().StartsWith("```") ||
                ImageRe.IsMatch(line.Trim()))
                break;

            // A heading mid-block ends the current block (heading stays for next call)
            if (line.TrimStart().StartsWith('#') && sb.Length > 0)
                break;

            sb.AppendLine(line);
            pos++;
        }

        var raw = sb.ToString().TrimEnd();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var kind = DetermineMarkdownKind(raw);
        return new ContentBlock { Kind = kind, RawContent = raw };
    }

    private static BlockKind DetermineMarkdownKind(string raw)
    {
        var first = raw.TrimStart();
        if (first.StartsWith("- ") || first.StartsWith("* ") || first.StartsWith("+ "))
            return BlockKind.BulletList;
        if (Regex.IsMatch(first, @"^\d+\. "))
            return BlockKind.NumberedList;
        if (first.StartsWith("> "))
            return BlockKind.BlockQuote;
        if (first.StartsWith("#"))
            return BlockKind.Heading;
        if (first.Contains('|') && first.Contains('-'))
            return BlockKind.Table;
        return BlockKind.Paragraph;
    }
}
