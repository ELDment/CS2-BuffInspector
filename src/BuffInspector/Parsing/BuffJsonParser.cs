using System.Globalization;
using System.Text.Json.Nodes;
using BuffInspector.Communication;
using BuffInspector.Domain;

namespace BuffInspector.Parsing;

internal static class BuffJsonParser
{
    public static (BuffLink Link, JsonObject? Order, JsonObject? Goods) SelectOrder(BuffLink link, JsonObject data)
    {
        var items = (data["items"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
        var order = items.FirstOrDefault(item =>
            (link.AssetId.Length == 0 || ReadText(item["asset_info"] as JsonObject, "assetid") == link.AssetId)
            && (link.SellOrderId.Length == 0 || ReadText(item, "id") == link.SellOrderId)
        );
        // A specific order must never silently resolve to another seller's item.
        if (order?["asset_info"] is JsonObject asset)
        {
            link = FillLink(link, asset) with { SellOrderId = ReadText(order, "id") ?? link.SellOrderId };
        }
        return (link, order, (data["goods_infos"] as JsonObject)?[link.GoodsId] as JsonObject);
    }

    public static BuffLink FillLink(BuffLink link, JsonObject? asset)
    {
        var assetId = ReadText(asset, "assetid");
        if (link.AssetId.Length > 0 && assetId is not null && assetId != link.AssetId)
        {
            throw new BuffException("BUFF 返回的资产与分享链接不一致。");
        }
        return link with
        {
            AssetId = assetId ?? link.AssetId,
            ClassId = ReadText(asset, "classid") ?? link.ClassId,
            InstanceId = ReadText(asset, "instanceid") ?? link.InstanceId,
            ContextId = ReadText(asset, "contextid") ?? link.ContextId,
        };
    }

    public static ItemData Parse(JsonObject? handbook, JsonObject? order = null, JsonObject? goods = null)
    {
        var asset =
            handbook?["asset_info"] as JsonObject ?? (handbook?["sell_order"] as JsonObject)?["asset_info"] as JsonObject ?? order?["asset_info"] as JsonObject;
        var info = asset?["info"] as JsonObject;
        goods = handbook?["goods_info"] as JsonObject ?? goods;
        return new ItemData
        {
            Title = ReadText(goods, "name"),
            Image = ReadText(info, "icon_url") ?? ReadText(goods, "icon_url"),
            NameTag = ReadText(info, "custom_name") ?? ReadText(info, "nametag"),
            DefinitionIndex = ReadInteger(info, "defindex"),
            PaintIndex = ReadInteger(info, "paintindex"),
            PaintSeed = ReadInteger(info, "paintseed"),
            PaintWear = ReadNumber(asset, "paintwear"),
            Stickers = ParseDecorations(info?["stickers"] as JsonArray),
            Keychains = ParseDecorations(info?["keychains"] as JsonArray),
        };
    }

    private static Decoration[]? ParseDecorations(JsonArray? items) =>
        items
            ?.OfType<JsonObject>()
            .Select(
                (item, index) =>
                    new Decoration(
                        ReadInteger(item, "sticker_id") ?? ReadInteger(item, "id") ?? 0,
                        ReadInteger(item, "slot") ?? index,
                        ReadText(item, "name") ?? "",
                        ReadNumber(item, "wear") ?? 0,
                        ReadNumber(item, "offset_x") ?? 0,
                        ReadNumber(item, "offset_y") ?? 0,
                        ReadNumber(item, "offset_z") ?? 0,
                        ReadNumber(item, "rotation") ?? 0,
                        ReadInteger(item, "pattern") ?? ReadInteger(item, "seed") ?? 0,
                        ReadNumber(item, "scale") ?? 1
                    )
            )
            .ToArray();

    private static string? ReadText(JsonObject? data, string key) =>
        data?[key] is JsonValue value && !string.IsNullOrWhiteSpace(value.ToString()) ? value.ToString() : null;

    private static int? ReadInteger(JsonObject? data, string key) =>
        int.TryParse(ReadText(data, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static float? ReadNumber(JsonObject? data, string key) =>
        float.TryParse(ReadText(data, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) ? value : null;
}
