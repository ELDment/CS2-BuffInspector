using System.Collections.Frozen;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Commands;
using WeaponSkins.Shared;

namespace BuffInspector;

[PluginMetadata(Id = "BuffInspector", Version = "1.0.0", Author = "Ambr0se")]
public class BuffInspectorPlugin(ISwiftlyCore core) : BasePlugin(core)
{
    private IWeaponSkinAPI? weaponSkinApi;
    private HttpClient? httpClient;
    private IScraper? scraper;
    private FrozenDictionary<string, int>? stickerNameToId;

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        weaponSkinApi = interfaceManager.GetSharedInterface<IWeaponSkinAPI>("WeaponSkins.API");
        stickerNameToId = weaponSkinApi?.StickerCollections
            .SelectMany(x => x.Value.Stickers)
            .Where(x => x.LocalizedNames.ContainsKey("schinese"))
            .DistinctBy(x => x.LocalizedNames["schinese"])
            .ToFrozenDictionary(x => x.LocalizedNames["schinese"], x => x.Index);
    }

    public override void Load(bool hotReload)
    {
        httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("https://buff.163.com")
        };
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 16_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) buff iPhone");

        scraper = new Scraper(httpClient);
    }

    public override void Unload()
    {
        httpClient?.Dispose();
        httpClient = null;
        scraper?.Dispose();
        scraper = null;
    }

    [Command("buff")]
    public void BuffCommand(ICommandContext context)
    {
        if (scraper == null)
        {
            context.Reply("Scraper not initialized!");
            return;
        }

        if (context.Args.Length < 1)
        {
            context.Reply("Usage: buff <url>");
            return;
        }

        var url = context.Args[0];
        _ = Task.Run(async () =>
        {
            try
            {
                var skinInfo = await scraper.ScrapeAsync(url);
                context.Reply($"Title: {skinInfo.Title} ({skinInfo.Type})");
                context.Reply($"NameTag: {skinInfo.NameTag}");
                context.Reply($"DefIndex: {skinInfo.DefIndex}, PaintIndex: {skinInfo.PaintIndex}");
                context.Reply($"Seed: {skinInfo.PaintSeed}, Wear: {skinInfo.PaintWear:F10}");
                context.Reply($"Image: {skinInfo.Image}");
                if (skinInfo.Stickers.Count > 0)
                {
                    context.Reply($"Stickers: {string.Join(", ", skinInfo.Stickers.Select(s => s.Name))}");
                }

                if (weaponSkinApi == null || !(context.Sender?.IsValid ?? false) || !(context.Sender?.PlayerPawn?.IsValid ?? false))
                {
                    return;
                }

                var steamId = context.Sender!.Controller.SteamID;
                var team = context.Sender!.Controller.Team;

                switch (skinInfo.Type)
                {
                    case SkinType.Weapon:
                        weaponSkinApi.UpdateWeaponSkin(steamId, team, (ushort)skinInfo.DefIndex, skin =>
                        {
                            skin.Paintkit = skinInfo.PaintIndex;
                            skin.PaintkitSeed = skinInfo.PaintSeed;
                            skin.PaintkitWear = skinInfo.PaintWear;
                            skin.Nametag = skinInfo.NameTag;
                            foreach (var sticker in skinInfo.Stickers)
                            {
                                if (stickerNameToId?.TryGetValue(sticker.Name.Trim(), out var id) ?? false)
                                {
                                    skin.SetSticker(sticker.Slot, new StickerData
                                    {
                                        Id = id,
                                        Wear = sticker.Wear,
                                        OffsetX = sticker.OffsetX,
                                        OffsetY = sticker.OffsetY
                                    });
                                }
                            }
                        });
                        break;
                    case SkinType.Knife:
                        weaponSkinApi.UpdateKnifeSkin(steamId, team, skin =>
                        {
                            skin.DefinitionIndex = (ushort)skinInfo.DefIndex;
                            skin.Paintkit = skinInfo.PaintIndex;
                            skin.PaintkitSeed = skinInfo.PaintSeed;
                            skin.PaintkitWear = skinInfo.PaintWear;
                            skin.Nametag = skinInfo.NameTag;
                        });
                        break;
                    case SkinType.Glove:
                        weaponSkinApi.UpdateGloveSkin(steamId, team, skin =>
                        {
                            skin.DefinitionIndex = (ushort)skinInfo.DefIndex;
                            skin.Paintkit = skinInfo.PaintIndex;
                            skin.PaintkitSeed = skinInfo.PaintSeed;
                            skin.PaintkitWear = skinInfo.PaintWear;
                        });
                        break;
                }

                context.Reply("Skin applied!");
            }
            catch (Exception ex)
            {
                context.Reply($"Error: {ex.Message}");
            }
        });
    }
}