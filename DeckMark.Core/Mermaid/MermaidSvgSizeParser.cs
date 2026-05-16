using System.Globalization;
using System.Xml.Linq;

namespace DeckMark.Core.Mermaid;

internal static class MermaidSvgSizeParser
{
    public static MermaidRenderSize Parse(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            var document = XDocument.Load(stream);
            var root = document.Root;
            if (root is null)
                return new MermaidRenderSize(1f, 1f);

            float? width = TryParseDimension(root.Attribute("width")?.Value);
            float? height = TryParseDimension(root.Attribute("height")?.Value);
            if (width is > 0f && height is > 0f)
                return new MermaidRenderSize(width.Value, height.Value);

            var viewBox = root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrWhiteSpace(viewBox))
            {
                var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 4 &&
                    float.TryParse(parts[2], CultureInfo.InvariantCulture, out float viewBoxWidth) &&
                    float.TryParse(parts[3], CultureInfo.InvariantCulture, out float viewBoxHeight) &&
                    viewBoxWidth > 0f && viewBoxHeight > 0f)
                {
                    return new MermaidRenderSize(viewBoxWidth, viewBoxHeight);
                }
            }
        }
        catch
        {
        }

        return new MermaidRenderSize(1f, 1f);
    }

    private static float? TryParseDimension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var numeric = new string(value.Where(ch => char.IsDigit(ch) || ch is '.' or '-' or '+').ToArray());
        if (float.TryParse(numeric, CultureInfo.InvariantCulture, out float result) && result > 0f)
            return result;

        return null;
    }
}
