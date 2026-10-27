using System.Collections.Frozen;
using BuffInspector.Domain;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
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

    public void Apply(IPlayer player, IHelpers helpers, SkinInfo info, bool autoEquipWeapon)
    {
        var definition = checked((ushort)info.DefinitionIndex!.Value);
        var steamId = player.Controller.SteamID;
        var team = player.Controller.Team;
        if (!autoEquipWeapon || definition > 5000)
        {
            ApplySkin(steamId, team, info);
            return;
        }
        if (
            !player.IsAlive
            || player.PlayerPawn is not { IsValid: true } pawn
            || pawn.WeaponServices is not { IsValid: true } weaponServices
            || pawn.ItemServices is not { IsValid: true } itemServices
        )
        {
            throw new BuffException("请在存活时使用 !buff 切换武器。");
        }

        // Give the default knife so WeaponSkins can apply the selected knife model.
        var isKnife = IsKnife(definition);
        var className = isKnife ? "weapon_knife" : helpers.GetClassnameByDefinitionIndex(definition);
        var weaponData = helpers.GetWeaponCSDataFromKey(isKnife ? 42 : definition);
        if (string.IsNullOrWhiteSpace(className) || weaponData is not { IsValid: true } || weaponData.GearSlot == gear_slot_t.GEAR_SLOT_INVALID)
        {
            throw new BuffException("无法获取该武器的实体或槽位信息，请检查 SwiftlyS2 数据。");
        }

        // Remove before updating skins: WeaponSkins otherwise queues a second regive for existing weapons.
        foreach (var handle in weaponServices.MyWeapons.ToArray())
        {
            if (handle.Value is not { IsValid: true } weapon)
            {
                continue;
            }
            if (
                ShouldReplaceWeapon(
                    definition,
                    weaponData.GearSlot,
                    weapon.AttributeManager.Item.ItemDefinitionIndex,
                    weapon.As<CCSWeaponBase>().WeaponBaseVData.GearSlot
                )
            )
            {
                weaponServices.RemoveWeapon(weapon);
            }
        }

        ApplySkin(steamId, team, info);
        var newWeapon = itemServices.GiveItem<CBasePlayerWeapon>(className);
        if (newWeapon is not { IsValid: true })
        {
            throw new BuffException("皮肤已更新，但武器发放失败，请重试或检查服务器日志。");
        }
        weaponServices.SelectWeapon(newWeapon);
    }

    internal static bool ShouldReplaceWeapon(int definition, gear_slot_t slot, int currentDefinition, gear_slot_t currentSlot)
    {
        if (definition == currentDefinition)
        {
            return true;
        }
        if (IsKnife(definition))
        {
            return IsKnife(currentDefinition);
        }
        // The taser may share a gear slot with another weapon; only replace another taser.
        if (definition == (int)ItemDefinitionIndex.Taser || currentDefinition == (int)ItemDefinitionIndex.Taser || IsKnife(currentDefinition))
        {
            return false;
        }
        return slot is gear_slot_t.GEAR_SLOT_RIFLE or gear_slot_t.GEAR_SLOT_PISTOL && currentSlot == slot;
    }

    private static bool IsKnife(int definition) => definition is 42 or 59 or >= 500 and <= 599;

    private void ApplySkin(ulong steamId, Team team, SkinInfo info)
    {
        var definition = checked((ushort)info.DefinitionIndex!.Value);
        // These boundaries match WeaponSkins v0.5.5 Utilities.IsKnife/IsGlove.
        if (IsKnife(definition))
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
