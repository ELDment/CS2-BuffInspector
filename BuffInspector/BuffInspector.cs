using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Core.Menus.OptionsBase;
using WeaponSkins.Shared;
using System.Data.Common;

namespace BuffInspector;

[PluginMetadata(Id = "BuffInspector", Version = "1.0.0", Author = "Ambr0se")]
public class BuffInspectorPlugin(ISwiftlyCore core) : BasePlugin(core)
{
    private IWeaponSkinAPI? weaponSkinApi;
    private HttpClient? httpClient;
    private Scraper? scraper;

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        base.UseSharedInterface(interfaceManager);
        weaponSkinApi = interfaceManager.GetSharedInterface<IWeaponSkinAPI>("WeaponSkins.API");
    }

    public override void OnSharedInterfaceInjected(IInterfaceManager interfaceManager)
    {
        base.OnSharedInterfaceInjected(interfaceManager);

        httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("https://buff.163.com")
        };
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 16_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) buff iPhone");

        scraper = new Scraper(httpClient, weaponSkinApi);
    }

    public override void Load(bool hotReload)
    {
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
        if (weaponSkinApi == null || !(context.Sender?.IsValid ?? false) || !(context.Sender?.PlayerPawn?.IsValid ?? false))
        {
            return;
        }

        if (scraper == null)
        {
            context.Reply("Scraper not initialized!");
            return;
        }

        if (context.Args.Length < 1 || string.IsNullOrWhiteSpace(context.Args[0]))
        {
            context.Reply("Usage: buff <url>");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var skinInfo = await scraper.ScrapeAsync(context.Args[0]);
                context.Reply($"» \x03{skinInfo.Title}");
                context.Reply($"» 皮肤编号:{skinInfo.PaintIndex} | 图案模板:{skinInfo.PaintSeed} | \x09{skinInfo.PaintWear:F17}");
                if (!string.IsNullOrWhiteSpace(skinInfo.NameTag))
                {
                    context.Reply($"» \x10\"{skinInfo.NameTag}\"");
                }

                skinInfo.Stickers
                    .Where(s => s.Id != 0)
                    .Select(s => s.Id < 0 ? $"印花 \x0A»\x01 {s.Name} \x0F解析失败" : $"印花 \x0A»\x01 {s.Name} \x05{1f - s.Wear:F4}")
                    .ToList()
                    .ForEach(context.Reply);

                skinInfo.Keychains
                    .Where(s => s.Id != 0)
                    .Select(s => s.Id < 0 ? $"挂件 \x0A»\x01 {s.Name} \x0F解析失败" : $"挂件 \x0A»\x01 \x0E{s.Name}")
                    .ToList()
                    .ForEach(context.Reply);

                if (!string.IsNullOrWhiteSpace(skinInfo.Image))
                {
                    context.Sender!.SendCenterHTML($"<img src='{skinInfo.Image}' />", 8000);
                }

                var steamId = context.Sender!.Controller.SteamID;
                var team = context.Sender!.Controller.Team;

                switch (skinInfo.Type)
                {
                    case SkinType.Weapon:
                        weaponSkinApi.UpdateWeaponSkin(steamId, team, (ushort)skinInfo.DefinitionIndex, skin =>
                        {
                            skin.Paintkit = skinInfo.PaintIndex;
                            skin.PaintkitSeed = skinInfo.PaintSeed;
                            skin.PaintkitWear = skinInfo.PaintWear;
                            skin.Nametag = skinInfo.NameTag;
                            foreach (var sticker in skinInfo.Stickers)
                            {
                                skin.SetSticker(sticker.Slot, new StickerData
                                {
                                    Id = sticker.Id >= 0 ? sticker.Id : 0,
                                    Wear = sticker.Wear,
                                    OffsetX = sticker.OffsetX,
                                    OffsetY = sticker.OffsetY
                                });
                            }
                            foreach (var keychain in skinInfo.Keychains)
                            {
                                skin.SetKeychain(keychain.Slot, new KeychainData
                                {
                                    Id = keychain.Id >= 0 ? keychain.Id : 0,
                                    Seed = keychain.Seed,
                                    OffsetX = keychain.OffsetX,
                                    OffsetY = keychain.OffsetY,
                                    OffsetZ = keychain.OffsetZ
                                });
                            }
                        });
                        break;
                    case SkinType.Knife:
                        weaponSkinApi.UpdateKnifeSkin(steamId, team, skin =>
                        {
                            skin.DefinitionIndex = (ushort)skinInfo.DefinitionIndex;
                            skin.Paintkit = skinInfo.PaintIndex;
                            skin.PaintkitSeed = skinInfo.PaintSeed;
                            skin.PaintkitWear = skinInfo.PaintWear;
                            skin.Nametag = skinInfo.NameTag;
                        });
                        break;
                    case SkinType.Glove:
                        weaponSkinApi.UpdateGloveSkin(steamId, team, skin =>
                        {
                            skin.DefinitionIndex = (ushort)skinInfo.DefinitionIndex;
                            skin.Paintkit = skinInfo.PaintIndex;
                            skin.PaintkitSeed = skinInfo.PaintSeed;
                            skin.PaintkitWear = skinInfo.PaintWear;
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                context.Reply($"Error: {ex.Message}");
            }
        });
    }
}