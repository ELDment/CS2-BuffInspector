using System.Net;
using System.Web;
using System.Collections.Frozen;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace BuffInspector;

internal sealed partial class Scraper(HttpClient http) : IScraper
{
    private static readonly string[] AssetParams = ["classid", "instanceid", "contextid", "assetid"];

    private static readonly FrozenDictionary<string, int> WeaponDefIndex = new Dictionary<string, int>
    {
        ["沙漠之鹰"] = 1,
        ["双持贝瑞塔"] = 2,
        ["FN57"] = 3,
        ["格洛克 18 型"] = 4,
        ["P2000"] = 32,
        ["P250"] = 36,
        ["Tec-9"] = 30,
        ["CZ75 自动手枪"] = 63,
        ["USP 消音版"] = 61,
        ["R8 左轮手枪"] = 64,
        ["MAC-10"] = 17,
        ["MP5-SD"] = 23,
        ["MP7"] = 33,
        ["MP9"] = 34,
        ["PP-野牛"] = 26,
        ["P90"] = 19,
        ["UMP-45"] = 24,
        ["AK-47"] = 7,
        ["AUG"] = 8,
        ["AWP"] = 9,
        ["法玛斯"] = 10,
        ["G3SG1"] = 11,
        ["加利尔 AR"] = 13,
        ["M4A4"] = 16,
        ["M4A1 消音型"] = 60,
        ["SCAR-20"] = 38,
        ["SG 553"] = 39,
        ["SSG 08"] = 40,
        ["M249"] = 14,
        ["MAG-7"] = 27,
        ["内格夫"] = 28,
        ["新星"] = 35,
        ["截短霰弹枪"] = 29,
        ["XM1014"] = 25,
        ["刺刀"] = 500,
        ["海豹短刀"] = 503,
        ["折叠刀"] = 505,
        ["穿肠刀"] = 506,
        ["爪子刀"] = 507,
        ["M9 刺刀"] = 508,
        ["猎杀者匕首"] = 509,
        ["弯刀"] = 512,
        ["鲍伊猎刀"] = 514,
        ["蝴蝶刀"] = 515,
        ["暗影双匕"] = 516,
        ["系绳匕首"] = 517,
        ["求生匕首"] = 518,
        ["熊刀"] = 519,
        ["折刀"] = 520,
        ["流浪者匕首"] = 521,
        ["短剑"] = 522,
        ["锯齿爪刀"] = 523,
        ["骷髅匕首"] = 525,
        ["廓尔喀刀"] = 526,
        ["狂牙手套"] = 4725,
        ["血猎手套"] = 5027,
        ["运动手套"] = 5030,
        ["驾驶手套"] = 5031,
        ["手部束带"] = 5032,
        ["摩托手套"] = 5033,
        ["专业手套"] = 5034,
        ["九头蛇手套"] = 5035
    }.ToFrozenDictionary();

    [GeneratedRegex(@"\d+")] private static partial Regex IntPattern();
    [GeneratedRegex(@"[\d.]+")] private static partial Regex FloatPattern();
    [GeneratedRegex(@"(?<="")([^""]+)(?="")")] private static partial Regex QuotedTextPattern();
    [GeneratedRegex(@"/h/\d+")] private static partial Regex ImageHeightPattern();

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
        var cleaned = url.Replace("https://", string.Empty).Replace("http://", string.Empty);
        return cleaned.StartsWith("buff.163.com") ? cleaned["buff.163.com".Length..] : throw new ScrapeException($"Not a buff.163.com URL: {url}");
    }

    private async Task<string> ResolveAssetQueryAsync(string path, CancellationToken cancellationToken)
    {
        using var resp = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var redirect = resp.StatusCode is HttpStatusCode.Found ? resp.Headers.Location?.ToString() ?? throw new ScrapeException("Empty redirect") : throw new ScrapeException($"Expected redirect, got {resp.StatusCode}");

        var index = redirect.IndexOf('?');
        if (index < 0)
        {
            throw new ScrapeException($"No query in redirect: {redirect}");
        }

        var qs = HttpUtility.ParseQueryString(redirect[index..]);
        var missing = AssetParams.Where(p => string.IsNullOrEmpty(qs[p])).ToList();
        return missing.Count > 0 ? throw new ScrapeException($"Missing: {string.Join(", ", missing)}") : string.Join("&", AssetParams.Select(p => $"{p}={qs[p]}"));
    }

    private SkinInfo ParseSkinFromHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//h3")?.InnerText?.Trim() ?? throw new ScrapeException("Title not found");
        var ps = doc.DocumentNode.SelectSingleNode("//div[@class='title-info-wrapper']")?.SelectNodes(".//p")?.Select(p => p.InnerText).ToList() ?? throw new ScrapeException("Info not found");

        var defIndex = WeaponDefIndex.FirstOrDefault(kv => title.StartsWith(kv.Key)).Value is var d and > 0 ? d : throw new ScrapeException($"Unknown weapon: {title}");
        var skinType = defIndex switch
        {
            >= 500 and <= 526 => SkinType.Knife,
            >= 4725 => SkinType.Glove,
            _ => SkinType.Weapon
        };

        return new SkinInfo(
            Title: title,
            Image: NormalizeImageUrl(doc.DocumentNode.SelectSingleNode("//img[@class='show_inspect_img']")?.GetAttributeValue("src", string.Empty)),
            NameTag: ExtractNameTag(doc),
            Type: skinType,
            DefIndex: defIndex,
            PaintIndex: ParseInt(ps, "paint index"),
            PaintSeed: ParseInt(ps, "paint seed"),
            PaintWear: ParseFloat(ps, "磨损")
        )
        {
            Stickers = ParseStickers(doc)
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

    private static string? NormalizeImageUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }
        return ImageHeightPattern().Replace(url, "/h/2600");
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

    private static List<Sticker> ParseStickers(HtmlDocument doc)
    {
        var nodes = doc.DocumentNode.SelectNodes("//div[@class='stickers-card-item']");
        if (nodes == null)
        {
            return [];
        }

        var stickers = new List<Sticker>();
        var slot = 0;
        foreach (var node in nodes)
        {
            var id = int.TryParse(node.GetAttributeValue("data-goods_id", string.Empty), out var gid) ? gid : 0;
            var name = node.SelectSingleNode(".//div[@class='name']")?.InnerText?.Trim() ?? string.Empty;
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
            stickers.Add(new Sticker(id, slot++, Math.Clamp(100f - wear, 0f, 100f) / 100f, 0f, 0f, name));
        }
        return stickers;
    }
}