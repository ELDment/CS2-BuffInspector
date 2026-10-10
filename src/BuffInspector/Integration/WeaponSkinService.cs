using System.Collections.Frozen;
using BuffInspector.Domain;
using SwiftlyS2.Shared.Players;
using WeaponSkins.Shared;

namespace BuffInspector.Integration;

internal sealed class WeaponSkinService
{
    // WeaponSkins v0.5.5 exposes six sticker entries and one keychain entry.
    internal const int StickerCount = 6;
    private readonly IWeaponSkinAPI api;
    private readonly (int Index, string Name)[] items;
    private readonly FrozenDictionary<string, int> stickers;
    private readonly FrozenDictionary<string, int> keychains;

    public WeaponSkinService(IWeaponSkinAPI api)
    {
        this.api = api;
        items = api
            .Items.Values.SelectMany(item => item.LocalizedNames.Values.Select(name => (item.Index, Name: name)))
            .OrderByDescending(item => item.Name.Length)
            .ToArray();
        stickers = api
            .StickerCollections.Values.SelectMany(collection => collection.Stickers)
            .SelectMany(sticker => sticker.LocalizedNames.Values.Select(name => (Name: name, sticker.Index)))
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        keychains = api
            .Keychains.Values.SelectMany(keychain => keychain.LocalizedNames.Values.Select(name => (Name: name, keychain.Index)))
            .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
    }

    public SkinInfo Resolve(SkinInfo info)
    {
        var title = info.Title.Replace("StatTrak™", "", StringComparison.OrdinalIgnoreCase).Replace("★", "", StringComparison.Ordinal).Trim();
        var definition = info.DefinitionIndex ?? items.FirstOrDefault(item => title.StartsWith(item.Name, StringComparison.OrdinalIgnoreCase)).Index;
        if (definition <= 0 || definition > ushort.MaxValue || !api.Items.Values.Any(item => item.Index == definition))
        {
            throw new BuffException("无法识别该武器，请确认 WeaponSkins 数据已更新。");
        }
        if (info.Stickers.Count > StickerCount || info.Keychains.Count > 1)
        {
            throw new BuffException("该饰品的贴纸或挂件数量超出 WeaponSkins 支持范围。");
        }
        return info with
        {
            DefinitionIndex = definition,
            Stickers = info.Stickers.Select(item => ResolveDecoration(item, stickers)).ToArray(),
            Keychains = info.Keychains.Select(item => ResolveDecoration(item, keychains)).ToArray(),
        };
    }

    public void Apply(ulong steamId, Team team, SkinInfo info)
    {
        var definition = checked((ushort)info.DefinitionIndex!.Value);
        // These boundaries match WeaponSkins v0.5.5 Utilities.IsKnife/IsGlove.
        if (definition is 42 or 59 or >= 500 and <= 599)
        {
            api.UpdateKnifeSkin(
                steamId,
                team,
                skin =>
                {
                    skin.DefinitionIndex = definition;
                    skin.Paintkit = info.PaintIndex;
                    skin.PaintkitSeed = info.PaintSeed;
                    skin.PaintkitWear = info.PaintWear;
                    skin.Nametag = info.NameTag;
                }
            );
        }
        else if (definition > 5000)
        {
            api.UpdateGloveSkin(
                steamId,
                team,
                skin =>
                {
                    skin.DefinitionIndex = definition;
                    skin.Paintkit = info.PaintIndex;
                    skin.PaintkitSeed = info.PaintSeed;
                    skin.PaintkitWear = info.PaintWear;
                }
            );
        }
        else
        {
            api.UpdateWeaponSkin(steamId, team, definition, skin => PopulateWeapon(skin, info));
        }
    }

    internal static void PopulateWeapon(WeaponSkinData skin, SkinInfo info)
    {
        skin.Paintkit = info.PaintIndex;
        skin.PaintkitSeed = info.PaintSeed;
        skin.PaintkitWear = info.PaintWear;
        skin.Nametag = info.NameTag;
        for (var index = 0; index < StickerCount; index++)
        {
            var sticker = index < info.Stickers.Count ? info.Stickers[index] : null;
            skin.SetSticker(
                index,
                sticker is null || sticker.Id <= 0
                    ? null
                    : new StickerData
                    {
                        Id = sticker.Id,
                        Schema = sticker.Slot,
                        Wear = sticker.Wear,
                        OffsetX = sticker.OffsetX,
                        OffsetY = sticker.OffsetY,
                        Rotation = sticker.Rotation,
                        Scale = sticker.Scale,
                    }
            );
        }
        var keychain = info.Keychains.FirstOrDefault();
        skin.SetKeychain(
            0,
            keychain is null || keychain.Id <= 0
                ? null
                : new KeychainData
                {
                    Id = keychain.Id,
                    Seed = keychain.Seed,
                    OffsetX = keychain.OffsetX,
                    OffsetY = keychain.OffsetY,
                    OffsetZ = keychain.OffsetZ,
                }
        );
    }

    private static Decoration ResolveDecoration(Decoration item, FrozenDictionary<string, int> names)
    {
        if (item.Slot < 0 || item.Wear is < 0 or > 1 || !float.IsFinite(item.Wear))
        {
            throw new BuffException("BUFF 返回的饰品槽位或磨损无效。");
        }
        return item.Id > 0 ? item : item with { Id = names.GetValueOrDefault(item.Name, -1) };
    }
}
