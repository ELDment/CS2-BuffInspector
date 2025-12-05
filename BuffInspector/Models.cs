namespace BuffInspector;

internal enum SkinType : uint
{
    Weapon,
    Knife,
    Glove
}

internal sealed record Sticker(int Id, int Slot, float Wear, float OffsetX, float OffsetY, string Name)
{
    public override string ToString()
    {
        return $"[{Slot}] {Name} (ID:{Id}, Wear:{Wear:P0}) Offset: ({OffsetX}, {OffsetY})";
    }
}

internal sealed record Keychain(int Id, int Slot, int Seed, float OffsetX, float OffsetY, float OffsetZ, string Name)
{
    public override string ToString()
    {
        return $"[{Slot}] {Name} (ID:{Id}, Seed:{Seed}) Offset: ({OffsetX}, {OffsetY}, {OffsetZ})";
    }
}

internal sealed record SkinInfo(string Title, string? Image, string? NameTag, SkinType Type, int DefinitionIndex, int PaintIndex, int PaintSeed, float PaintWear)
{
    public List<Sticker> Stickers { get; init; } = [];
    public List<Keychain> Keychains { get; init; } = [];

    public override string ToString()
    {
        return string.Join("\n",
            $"{Title} | Type:{Type} DefinitionIndex:{DefinitionIndex} PaintIndex:{PaintIndex} Seed:{PaintSeed} Wear:{PaintWear:F10}",
            $"Image: {Image}",
            $"NameTag:\"{NameTag}\"",
            Stickers.Count > 0 ? $" Stickers:[{string.Join(", ", Stickers)}]" : string.Empty,
            Keychains.Count > 0 ? $" Keychains:[{string.Join(", ", Keychains)}]" : string.Empty
        );
    }
}