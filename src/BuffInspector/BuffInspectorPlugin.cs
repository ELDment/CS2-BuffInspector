using System.Text.Json;
using BuffInspector.Communication;
using BuffInspector.Domain;
using BuffInspector.Integration;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using WeaponSkins.Shared;

namespace BuffInspector;

[PluginMetadata(Id = "BuffInspector", Version = "1.0.0", Author = "Ambr0se")]
public sealed class BuffInspectorPlugin(ISwiftlyCore core) : BasePlugin(core)
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<ulong> pending = [];
    private HttpClient? http;
    private BuffClient? client;
    private WeaponSkinService? skins;
    private BuffOptions options = new();

    public override void Load(bool hotReload)
    {
        const string configFile = "buff-inspector.jsonc";
        Core.Configuration.InitializeJsonWithModel<BuffOptions>(configFile, "Main", addMissingKeys: true);
        using var document = JsonDocument.Parse(
            File.ReadAllText(Core.Configuration.GetConfigPath(configFile)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
        );
        options = document.RootElement.GetProperty("Main").Deserialize<BuffOptions>() ?? throw new InvalidOperationException("BUFF 配置为空。");
        options.Validate();
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        skins = new WeaponSkinService(interfaceManager.GetSharedInterface<IWeaponSkinAPI>("WeaponSkins.API"));
    }

    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        http?.Dispose();
        http = BuffClient.CreateHttpClient(options);
        client = new BuffClient(http, Core.Logger, options);
    }

    public override void Unload()
    {
        lifetime.Cancel();
        http?.Dispose();
        http = null;
        client = null;
        skins = null;
        pending.Clear();
        lifetime.Dispose();
    }

    [Command("buff")]
    public void BuffCommand(ICommandContext context)
    {
        var player = context.Sender;
        if (player?.IsValid != true || player.PlayerPawn?.IsValid != true)
        {
            return;
        }
        if (client is null || skins is null)
        {
            context.Reply("皮肤服务尚未就绪。");
            return;
        }
        if (context.Args.Length != 1 || string.IsNullOrWhiteSpace(context.Args[0]))
        {
            context.Reply("用法：!buff <BUFF 分享链接>");
            return;
        }
        var sessionId = player.SessionId;
        if (!pending.Add(sessionId))
        {
            context.Reply("正在解析上一条链接，请稍候。");
            return;
        }
        var steamId = player.Controller.SteamID;
        var team = player.Controller.Team;
        _ = FetchAndApplyAsync(context, client, sessionId, steamId, team, lifetime.Token);
    }

    private async Task FetchAndApplyAsync(
        ICommandContext context,
        BuffClient requestClient,
        ulong sessionId,
        ulong steamId,
        Team team,
        CancellationToken stopping
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.OperationTimeoutSeconds));
        SkinInfo? info = null;
        string? failure = null;
        try
        {
            info = await requestClient.FetchAsync(context.Args[0], timeout.Token);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return;
        }
        catch (BuffException error)
        {
            failure = error.Message;
        }
        catch (Exception error)
        {
            failure = error is OperationCanceledException ? "BUFF 请求超时，请稍后重试。" : "BUFF 解析失败，请稍后重试。";
            Core.Logger.LogWarning("BUFF 请求失败：{ErrorType}", error.GetType().Name);
        }
        if (stopping.IsCancellationRequested)
        {
            return;
        }
        try
        {
            await Core.Scheduler.NextTickAsync(() =>
            {
                if (stopping.IsCancellationRequested)
                {
                    return;
                }
                pending.Remove(sessionId);
                var player = Core.PlayerManager.GetPlayerFromSessionId(sessionId);
                if (
                    stopping.IsCancellationRequested
                    || player?.IsValid != true
                    || player.PlayerPawn?.IsValid != true
                    || player.Controller.SteamID != steamId
                    || player.Controller.Team != team
                )
                {
                    return;
                }
                try
                {
                    if (failure is not null)
                    {
                        context.Reply(failure);
                        return;
                    }
                    if (!ReferenceEquals(requestClient, client) || skins is null)
                    {
                        context.Reply("皮肤服务已重新加载，请重新发送链接。");
                        return;
                    }
                    var resolved = skins.Resolve(info!);
                    skins.Apply(player, Core.Helpers, resolved, options.AutoEquipWeapon);
                    SkinPresenter.Reply(resolved, context.Reply);
                    if (options.PreviewDurationMilliseconds > 0 && SkinPresenter.ImageHtml(resolved.Image, options.PreviewImageWidth) is { } image)
                    {
                        player.SendCenterHTML(image, options.PreviewDurationMilliseconds);
                    }
                }
                catch (BuffException error)
                {
                    context.Reply(error.Message);
                }
                catch (Exception error)
                {
                    Core.Logger.LogError("皮肤应用失败：{ErrorType}", error.GetType().Name);
                    context.Reply("皮肤应用失败，请检查服务器日志。");
                }
            });
        }
        catch (Exception error)
        {
            if (!stopping.IsCancellationRequested)
            {
                Core.Logger.LogError("皮肤回调调度失败：{ErrorType}", error.GetType().Name);
            }
        }
    }
}
