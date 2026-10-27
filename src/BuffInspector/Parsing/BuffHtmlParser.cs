using System.Globalization;
using System.Text.RegularExpressions;
using BuffInspector.Domain;
using HtmlAgilityPack;

namespace BuffInspector.Parsing;

internal static partial class BuffHtmlParser
{
    public static ItemData Parse(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var root = document.DocumentNode;
        var paragraphs = root.Descendants("p").Select(Text).ToArray();
        var stickers = new List<Decoration>();
        var keychains = new List<Decoration>();
        foreach (var card in root.Descendants("div").Where(node => node.HasClass("stickers-card-item")))
        {
            var section = card.Ancestors("div").FirstOrDefault(node => node.HasClass("stickers-card"));
            var header = Text(section?.Descendants().FirstOrDefault(node => node.HasClass("stickers-card-header")));
            var text = Text(card) ?? "";
            var isKeychain =
                header?.Contains("挂件", StringComparison.Ordinal) == true
                || header?.Contains("charm", StringComparison.OrdinalIgnoreCase) == true
                || header?.Contains("keychain", StringComparison.OrdinalIgnoreCase) == true
                || text.Contains("挂件模板", StringComparison.Ordinal);
            var nameNode =
                card.Descendants().FirstOrDefault(node => node.HasClass("name"))
                ?? card.Descendants().FirstOrDefault(node => node.HasClass("stickers-card-item-info"))?.Elements("div").FirstOrDefault();
            var target = isKeychain ? keychains : stickers;
            var percent = FindNumber([text], "印花磨损") ?? FindNumber([text], "贴纸磨损");
            // BUFF's HTML percentage denotes remaining sticker condition; API wear is damage.
            var wear = percent is null ? 0 : Math.Clamp(1 - percent.Value / 100, 0, 1);
            target.Add(new Decoration(0, target.Count, Text(nameNode) ?? "未知饰品", wear, Seed: (int)(FindNumber([text], "挂件模板") ?? 0)));
        }
        var tag = Text(root.Descendants("p").FirstOrDefault(node => node.HasClass("name_tag")));
        if (tag is not null)
        {
            var separator = tag.IndexOfAny([':', '：']);
            tag = (separator >= 0 ? tag[(separator + 1)..] : tag).Trim().Trim('"', '\'', '“', '”');
        }
        return new ItemData
        {
            Title = Text(root.Descendants("h3").FirstOrDefault()),
            Image = HtmlEntity.DeEntitize(root.Descendants("img").FirstOrDefault(node => node.HasClass("show_inspect_img"))?.GetAttributeValue("src", null)),
            NameTag = string.IsNullOrEmpty(tag) ? null : tag,
            PaintIndex = ToInteger(FindNumber(paragraphs, "paint index")),
            PaintSeed = ToInteger(FindNumber(paragraphs, "paint seed")),
            PaintWear = FindNumber(paragraphs.Where(text => text?.StartsWith("磨损", StringComparison.Ordinal) == true), "磨损"),
            Stickers = stickers,
            Keychains = keychains,
        };
    }

    private static int? ToInteger(float? value) => value is >= 0 and <= int.MaxValue && value == MathF.Truncate(value.Value) ? (int)value.Value : null;

    private static float? FindNumber(IEnumerable<string?> texts, string label)
    {
        foreach (var text in texts)
        {
            var start = text?.IndexOf(label, StringComparison.OrdinalIgnoreCase) ?? -1;
            if (start < 0)
            {
                continue;
            }
            var match = NumberPattern().Match(text!, start + label.Length);
            if (float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value))
            {
                return value;
            }
        }
        return null;
    }

    private static string? Text(HtmlNode? node)
    {
        var text = node is null ? null : WebWhitespace().Replace(HtmlEntity.DeEntitize(node.InnerText), " ").Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    [GeneratedRegex(@"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WebWhitespace();
}
