using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuffInspector.Domain;
using BuffInspector.Parsing;
using Microsoft.Extensions.Logging;

namespace BuffInspector.Communication;

internal sealed class BuffClient(HttpClient http, ILogger logger, BuffNetworkOptions? settings = null)
{
    private readonly BuffNetworkOptions options = settings ?? new();

    public static HttpClient CreateHttpClient(BuffNetworkOptions options) =>
        new(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = true,
            }
        )
        {
            Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            MaxResponseContentBufferSize = options.MaxResponseBytes,
        };

    public async Task<SkinInfo> FetchAsync(string input, CancellationToken cancellationToken = default)
    {
        var link = BuffLink.Parse(input);
        if (link.AssetId.Length == 0 && link.GoodsId.Length == 0)
        {
            var page = await GetTextAsync(link.Uri, BuffLink.Site, false, true, cancellationToken);
            link = BuffLink.Parse(page.Uri.AbsoluteUri);
        }

        var handbook = await GetHandbookAsync(link, cancellationToken);
        link = BuffJsonParser.FillLink(link, handbook?["asset_info"] as JsonObject ?? (handbook?["sell_order"] as JsonObject)?["asset_info"] as JsonObject);
        JsonObject? order = null;
        JsonObject? goods = null;
        if ((link.AssetId.Length == 0 || link.SellOrderId.Length == 0) && link.GoodsId.Length > 0)
        {
            var list =
                await GetDataAsync(
                    "/api/market/goods/sell_order",
                    new Dictionary<string, string>
                    {
                        ["game"] = "csgo",
                        ["goods_id"] = link.GoodsId,
                        ["page_num"] = "1",
                        ["sort_by"] = "default",
                        ["mode"] = "",
                    },
                    link.Uri,
                    cancellationToken
                ) as JsonObject;
            if (list is not null)
            {
                (link, order, goods) = BuffJsonParser.SelectOrder(link, list);
                handbook ??= await GetHandbookAsync(link, cancellationToken);
                link = BuffJsonParser.FillLink(
                    link,
                    handbook?["asset_info"] as JsonObject ?? (handbook?["sell_order"] as JsonObject)?["asset_info"] as JsonObject
                );
            }
        }
        if (link.AssetId.Length == 0)
        {
            throw new BuffException("未找到目标资产，请使用具体饰品的分享链接。");
        }

        var html = "";
        try
        {
            html = (await GetTextAsync(link.DetailUri, link.Uri, false, false, cancellationToken)).Text;
        }
        catch (Exception error) when (IsOptionalFailure(error, cancellationToken))
        {
            logger.LogDebug("BUFF HTML 详情不可用：{ErrorType}", error.GetType().Name);
        }

        var inspect = new ItemData();
        var inspectValue = await GetDataAsync(
            "/api/market/cs2_inspect",
            new Dictionary<string, string> { ["assetid"] = link.AssetId },
            link.Uri,
            cancellationToken
        );
        if (inspectValue is JsonValue value && value.TryGetValue<string>(out var inspectLink))
        {
            try
            {
                inspect = InspectDecoder.Decode(inspectLink);
            }
            catch (BuffException)
            {
                logger.LogDebug("BUFF inspect 数据无效，使用详情数据。");
            }
        }
        return ItemData.Merge(BuffHtmlParser.Parse(html), BuffJsonParser.Parse(handbook, order, goods), inspect);
    }

    private async Task<JsonObject?> GetHandbookAsync(BuffLink link, CancellationToken cancellationToken) =>
        link.GoodsId.Length == 0 || link.SellOrderId.Length == 0
            ? null
            : await GetDataAsync(
                "/api/market/handbook_selling_item_desc_detail",
                new Dictionary<string, string> { ["goods_id"] = link.GoodsId, ["sell_order_id"] = link.SellOrderId },
                link.Uri,
                cancellationToken
            ) as JsonObject;

    private async Task<JsonNode?> GetDataAsync(string path, IReadOnlyDictionary<string, string> query, Uri referer, CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetTextAsync(BuffLink.BuildUri(path, query), referer, true, false, cancellationToken);
            if (JsonNode.Parse(response.Text) is not JsonObject document)
            {
                return null;
            }
            if (document["code"]?.ToString() == "OK")
            {
                return document["data"];
            }
            logger.LogDebug("BUFF 接口 {Endpoint} 未返回可用数据。", path);
        }
        catch (Exception error) when (IsOptionalFailure(error, cancellationToken))
        {
            logger.LogDebug("BUFF 接口 {Endpoint} 不可用：{ErrorType}", path, error.GetType().Name);
        }
        return null;
    }

    private async Task<(Uri Uri, string Text)> GetTextAsync(Uri uri, Uri referer, bool json, bool resolving, CancellationToken cancellationToken)
    {
        for (var redirect = 0; redirect <= options.MaxRedirects; redirect++)
        {
            BuffLink.Validate(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(
                json ? "BuffInspector/1.0" : "Mozilla/5.0 (iPhone; CPU iPhone OS 16_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) buff iPhone"
            );
            request.Headers.Accept.ParseAdd(json ? "application/json" : "text/html");
            request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
            request.Headers.Referrer = referer;
            if (json)
            {
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            }
            using var response = await http.SendAsync(request, cancellationToken);
            if (
                response.StatusCode
                is HttpStatusCode.MovedPermanently
                    or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect
                    or HttpStatusCode.PermanentRedirect
            )
            {
                var location = response.Headers.Location ?? throw new BuffException("BUFF 分享链接没有返回跳转地址。");
                uri = new Uri(uri, location);
                BuffLink.Validate(uri);
                if (resolving)
                {
                    var target = BuffLink.Parse(uri.AbsoluteUri);
                    if (target.AssetId.Length > 0 || target.GoodsId.Length > 0)
                    {
                        return (uri, "");
                    }
                }
                continue;
            }
            response.EnsureSuccessStatusCode();
            return (uri, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        throw new BuffException("BUFF 分享链接跳转次数过多。");
    }

    private static bool IsOptionalFailure(Exception error, CancellationToken cancellationToken) =>
        error is HttpRequestException or JsonException or BuffException || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
