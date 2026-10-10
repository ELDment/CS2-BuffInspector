using System.Web;
using BuffInspector.Domain;

namespace BuffInspector.Communication;

internal sealed record BuffLink(Uri Uri, string GoodsId, string AssetId, string ClassId, string InstanceId, string ContextId, string SellOrderId)
{
    public static readonly Uri Site = new("https://buff.163.com/");

    public static BuffLink Parse(string input)
    {
        var value = input.Trim();
        if (value.StartsWith("buff.163.com/", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new BuffException("请输入有效的 BUFF 分享链接。");
        }
        Validate(uri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        if ((query["appid"] is { } appId && appId != "730") || (query["game"] is { } game && game != "csgo"))
        {
            throw new BuffException("仅支持 CS2 饰品链接。");
        }
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        var goodsId = segments is ["goods", var id] ? id : query["goods_id"] ?? query["goodsId"] ?? "";
        return new BuffLink(
            uri,
            goodsId,
            query["assetid"] ?? "",
            query["classid"] ?? "",
            query["instanceid"] ?? "",
            query["contextid"] ?? "2",
            query["sell_order_id"] ?? ""
        );
    }

    public static void Validate(Uri uri)
    {
        if (
            uri.Scheme is not ("https" or "http")
            || !uri.IsDefaultPort
            || !string.Equals(uri.Host, Site.Host, StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0
        )
        {
            throw new BuffException("仅支持 buff.163.com 的官方链接和跳转。");
        }
    }

    public Uri DetailUri =>
        BuildUri(
            "/market/m/item_detail",
            new Dictionary<string, string>
            {
                ["game"] = "csgo",
                ["appid"] = "730",
                ["goods_id"] = GoodsId,
                ["assetid"] = AssetId,
                ["classid"] = ClassId,
                ["instanceid"] = InstanceId,
                ["contextid"] = ContextId,
                ["sell_order_id"] = SellOrderId,
                ["origin"] = "null",
            }
        );

    public static Uri BuildUri(string path, IReadOnlyDictionary<string, string> query) =>
        new(Site, path + "?" + string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")));
}
