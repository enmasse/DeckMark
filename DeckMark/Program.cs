using DeckMark.Core.Converter;
using DeckMark.Core.Mermaid;
using DeckMark.Core.Parser;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: deckmark <input.deck.md> [output.pptx] [--mermaid placeholder|ink|cli] [--mmdc <path>]");
    return 1;
}

string inputPath = args[0];
string outputPath = args.Length >= 2 && !args[1].StartsWith("--")
    ? args[1]
    : Path.ChangeExtension(inputPath, ".pptx");

string mermaidMode = "ink";
string mmdcPath = "mmdc";

for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--mermaid" && i + 1 < args.Length)
        mermaidMode = args[++i];
    else if (args[i] == "--mmdc" && i + 1 < args.Length)
        mmdcPath = args[++i];
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input file not found: {inputPath}");
    return 2;
}

IMermaidRenderer mermaid = mermaidMode switch
{
    "placeholder" => new MermaidPlaceholderRenderer(),
    "cli"         => new MermaidCliRenderer(mmdcPath),
    _             => new MermaidInkRenderer(),
};

Console.WriteLine($"Converting {inputPath} -> {outputPath} (mermaid: {mermaidMode})");

var source = await File.ReadAllTextAsync(inputPath);
var doc = DeckMarkParser.Parse(source);
var converter = new PptxConverter(mermaid);
await converter.ConvertAsync(doc, outputPath);

Console.WriteLine($"Done. {doc.Slides.Count} slide(s) written.");
return 0;
