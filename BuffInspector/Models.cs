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
        return $"[{Slot}] {Name} (ID:{Id}, Wear:{Wear:P0})";
    }
}

internal sealed record SkinInfo(string Title, string? Image, string? NameTag, SkinType Type, int DefinitionIndex, int PaintIndex, int PaintSeed, float PaintWear)
{
    public List<Sticker> Stickers { get; set; } = [];

    public override string ToString()
    {
        return $"{Title} | Type:{Type} DefinitionIndex:{DefinitionIndex} PaintIndex:{PaintIndex} Seed:{PaintSeed} Wear:{PaintWear:F10} {(NameTag != null ? $" NameTag:\"{NameTag}\"" : string.Empty)} {(Stickers.Count > 0 ? $" Stickers:[{string.Join(", ", Stickers)}]" : string.Empty)}";
    }
}