using System.Globalization;
using System.Net;
using BuffInspector.Domain;

namespace BuffInspector.Integration;

internal static class SkinPresenter
{
    public static void Reply(SkinInfo info, Action<string> reply)
    {
        reply($"» \x03{CleanText(info.Title)}");
        reply(string.Create(CultureInfo.InvariantCulture, $"» 皮肤编号:{info.PaintIndex} | 图案模板:{info.PaintSeed} | \x09{info.PaintWear:F17}"));
        if (!string.IsNullOrWhiteSpace(info.NameTag))
        {
            reply($"» \x10\"{CleanText(info.NameTag)}\"");
        }
        foreach (var sticker in info.Stickers)
        {
            reply(
                sticker.Id <= 0
                    ? $"印花 » {CleanText(sticker.Name)} 解析失败"
                    : string.Create(CultureInfo.InvariantCulture, $"印花 » {CleanText(sticker.Name)} {1 - sticker.Wear:F4}")
            );
        }
        foreach (var keychain in info.Keychains)
        {
            reply(keychain.Id <= 0 ? $"挂件 » {CleanText(keychain.Name)} 解析失败" : $"挂件 » {CleanText(keychain.Name)}");
        }
    }

    public static string? ImageHtml(string? image)
    {
        if (image?.StartsWith("//", StringComparison.Ordinal) == true)
        {
            image = "https:" + image;
        }
        return Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? $"<img src=\"{WebUtility.HtmlEncode(uri.AbsoluteUri)}\" />"
            : null;
    }

    private static string CleanText(string value) => new(value.Where(character => !char.IsControl(character)).ToArray());
}
