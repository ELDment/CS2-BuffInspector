using System.Globalization;
using System.Net;
using System.Web;
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
                    ? $"印花 \x0A»\x01 {CleanText(sticker.Name)} \x0F解析失败"
                    : string.Create(CultureInfo.InvariantCulture, $"印花 \x0A»\x01 {CleanText(sticker.Name)} \x05{1 - sticker.Wear:F4}")
            );
        }
        foreach (var keychain in info.Keychains)
        {
            reply(keychain.Id <= 0 ? $"挂件 \x0A»\x01 {CleanText(keychain.Name)} \x0F解析失败" : $"挂件 \x0A»\x01 \x0E{CleanText(keychain.Name)}");
        }
    }

    public static string? ImageHtml(string? image, int width)
    {
        if (image?.StartsWith("//", StringComparison.Ordinal) == true)
        {
            image = "https:" + image;
        }
        if (!Uri.TryCreate(image, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            return null;
        }
        if (uri.Host.EndsWith(".ps.netease.com", StringComparison.OrdinalIgnoreCase))
        {
            // Resize the image itself: CS2's center HTML does not reliably honor img width.
            var query = HttpUtility.ParseQueryString(uri.Query);
            var resize = string.Create(CultureInfo.InvariantCulture, $"imageView/2/w/{width}");
            query["fop"] = string.IsNullOrEmpty(query["fop"]) ? resize : $"{query["fop"]}|{resize}";
            uri = new UriBuilder(uri) { Query = query.ToString() }.Uri;
        }
        return string.Create(CultureInfo.InvariantCulture, $"<img src=\"{WebUtility.HtmlEncode(uri.AbsoluteUri)}\" width=\"{width}\" />");
    }

    private static string CleanText(string value) => new(value.Where(character => !char.IsControl(character)).ToArray());
}
