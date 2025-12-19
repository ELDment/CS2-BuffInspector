using System.Net;
using System.Web;
using System.Collections.Frozen;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WeaponSkins.Shared;

namespace BuffInspector;

internal sealed partial class Scraper(HttpClient http, IWeaponSkinAPI? weaponSkinApi) : IScraper
{
    [GeneratedRegex(@"\d+")] private static partial Regex IntPattern();
    [GeneratedRegex(@"[\d.]+")] private static partial Regex FloatPattern();

    private static readonly string[] AssetParams = ["classid", "instanceid", "contextid", "assetid"];

    private readonly Lazy<FrozenDictionary<string, int>> weaponsNames = new(() =>
        weaponSkinApi?.Items.Values
            .Where(x => x.LocalizedNames.ContainsKey("schinese"))
            .DistinctBy(x => x.LocalizedNames["schinese"])
            .ToFrozenDictionary(x => x.LocalizedNames["schinese"], x => x.Index)
        ?? FrozenDictionary<string, int>.Empty);

    private readonly Lazy<FrozenDictionary<string, int>> stickersNames = new(() =>
        weaponSkinApi?.StickerCollections
            .SelectMany(x => x.Value.Stickers)
            .Where(x => x.LocalizedNames.ContainsKey("schinese"))
            .DistinctBy(x => x.LocalizedNames["schinese"])
            .ToFrozenDictionary(x => x.LocalizedNames["schinese"], x => x.Index)
        ?? FrozenDictionary<string, int>.Empty);

    private readonly Lazy<FrozenDictionary<string, int>> keychainsNames = new(() =>
        weaponSkinApi?.Keychains.Values
            .Where(x => x.LocalizedNames.ContainsKey("schinese"))
            .DistinctBy(x => x.LocalizedNames["schinese"])
            .ToFrozenDictionary(x => x.LocalizedNames["schinese"], x => x.Index)
        ?? FrozenDictionary<string, int>.Empty);

    private readonly CancellationTokenSource cts = new();
    private volatile bool disposed;

    ~Scraper()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        cts.Cancel();
        cts.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<SkinInfo> ScrapeAsync(string url, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
        var path = ParseBuffUrl(url);
        var query = await ResolveAssetQueryAsync(path, linked.Token);
        var html = await http.GetStringAsync($"/market/m/item_detail?game=csgo&{query}", linked.Token);
        return ParseSkinFromHtml(html);
    }

    private static string ParseBuffUrl(string url)
    {
        var cleaned = url.Trim().Replace("https://", string.Empty).Replace("http://", string.Empty);
        return cleaned.StartsWith("buff.163.com") ? cleaned["buff.163.com".Length..] : throw new ScrapeException($"Invalid Buff share link: [{cleaned}]");
    }

    private async Task<string> ResolveAssetQueryAsync(string path, CancellationToken cancellationToken)
    {
        var index = path.IndexOf('?');
        if (index < 0 || !AssetParams.All(p => !string.IsNullOrEmpty(HttpUtility.ParseQueryString(path[index..])[p])))
        {
            using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Found)
            {
                path = response.Headers.Location?.ToString() ?? throw new ScrapeException("Empty redirect");
                index = path.IndexOf('?');
            }
        }

        if (index < 0)
        {
            throw new ScrapeException($"No query in: {path}");
        }

        var qs = HttpUtility.ParseQueryString(path[index..]);
        var missing = AssetParams.Where(p => string.IsNullOrEmpty(qs[p])).ToList();
        return missing.Count > 0 ? throw new ScrapeException($"Missing: {string.Join(", ", missing)}") : string.Join("&", AssetParams.Select(p => $"{p}={qs[p]}"));
    }

    private SkinInfo ParseSkinFromHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//h3")?.InnerText?.Trim() ?? throw new ScrapeException("Title not found");
        var ps = doc.DocumentNode.SelectSingleNode("//div[@class='title-info-wrapper']")?.SelectNodes(".//p")?.Select(p => p.InnerText).ToList() ?? throw new ScrapeException("Info not found");

        var definitionIndex = weaponsNames.Value.FirstOrDefault(kv => title.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)).Value is var d and > 0 ? d : throw new ScrapeException($"Unknown weapon: {title}");
        var skinType = definitionIndex switch
        {
            >= 500 and <= 526 => SkinType.Knife,
            >= 4725 => SkinType.Glove,
            _ => SkinType.Weapon
        };

        return new SkinInfo(
            Title: title,
            Image: doc.DocumentNode.SelectSingleNode("//img[@class='show_inspect_img']")?.GetAttributeValue("src", string.Empty),
            NameTag: ExtractNameTag(doc),
            Type: skinType,
            DefinitionIndex: definitionIndex,
            PaintIndex: ParseInt(ps, "paint index"),
            PaintSeed: ParseInt(ps, "paint seed"),
            PaintWear: ParseFloat(ps, "磨损")
        )
        {
            Stickers = ParseStickers(doc),
            Keychains = ParseKeychains(doc)
        };
    }

    private static int ParseInt(List<string> ps, string key)
    {
        var text = ps.FirstOrDefault(p => p.Contains(key)) ?? string.Empty;
        var match = IntPattern().Match(text);
        if (match.Success)
        {
            if (int.TryParse(match.Value, out var v))
            {
                return v;
            }
        }
        throw new ScrapeException($"Cannot parse {key}");
    }

    private static float ParseFloat(List<string> ps, string key)
    {
        var text = ps.FirstOrDefault(p => p.Contains(key)) ?? string.Empty;
        var match = FloatPattern().Match(text);
        if (match.Success)
        {
            if (float.TryParse(match.Value, out var v))
            {
                return v;
            }
        }
        throw new ScrapeException($"Cannot parse {key}");
    }

    private static string? ExtractNameTag(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//p[@class='name_tag']");
        if (node == null)
        {
            return null;
        }

        var text = node.InnerText;
        var index = text.IndexOf(':');
        if (index < 0)
        {
            index = text.IndexOf('：');
        }

        return index >= 0 ? text[(index + 1)..].Trim() : null;
    }

    private List<Sticker> ParseStickers(HtmlDocument doc)
    {
        var stickers = Enumerable.Range(0, 6)
            .Select(i => new Sticker(0, i, 0f, 0f, 0f, string.Empty))
            .ToList();

        var nodes = doc.DocumentNode.SelectNodes("//div[@class='stickers-card-item']");
        if (nodes == null)
        {
            return stickers;
        }

        var slot = 0;
        foreach (var node in nodes)
        {
            if (slot > 5)
            {
                break;
            }

            // Skip keychains
            if (node.InnerText.Contains("挂件模板"))
            {
                continue;
            }

            var name = node.SelectSingleNode(".//div[@class='name']")?.InnerText?.Trim() ?? string.Empty;
            if (!stickersNames.Value.TryGetValue(name, out var id))
            {
                stickers[slot] = new Sticker(-1, slot, 0f, 0f, 0f, name);
                slot++;
                continue;
            }

            var wearText = node.InnerText;
            var wear = 0f;
            var wearIndex = wearText.IndexOf("印花磨损");
            if (wearIndex >= 0)
            {
                var wearMatch = FloatPattern().Match(wearText, wearIndex);
                if (wearMatch.Success)
                {
                    _ = float.TryParse(wearMatch.Value, out wear);
                }
            }
            stickers[slot] = new Sticker(id, slot, Math.Clamp(100f - wear, 0f, 100f) / 100f, 0f, 0f, name);
            slot++;
        }
        return stickers;
    }

    private List<Keychain> ParseKeychains(HtmlDocument doc)
    {
        var keychains = Enumerable.Range(0, 2)
            .Select(i => new Keychain(0, i, 0, 0f, 0f, 0f, string.Empty))
            .ToList();

        var nodes = doc.DocumentNode.SelectNodes("//div[@class='stickers-card-item']");
        if (nodes == null)
        {
            return keychains;
        }

        var slot = 0;
        foreach (var node in nodes)
        {
            if (slot > 0)
            {
                break;
            }

            var text = node.InnerText;
            if (!text.Contains("挂件模板"))
            {
                continue;
            }

            var name = node.SelectSingleNode(".//div[@class='name']")?.InnerText?.Trim() ?? string.Empty;
            if (!keychainsNames.Value.TryGetValue(name, out var id))
            {
                keychains[slot] = new Keychain(-1, slot, 0, 0f, 0f, 0f, name);
                slot++;
                continue;
            }

            var seedMatch = IntPattern().Match(text, text.IndexOf("挂件模板"));
            var seed = seedMatch.Success && int.TryParse(seedMatch.Value, out var s) ? s : 0;

            keychains[slot] = new Keychain(id, slot, seed, 0f, 0f, 0f, name);
            slot++;
        }
        return keychains;
    }
}