namespace BuffInspector;

internal sealed record Sticker(int Id, int Slot, float Wear, float OffsetX, float OffsetY, string Name)
{
    public override string ToString()
    {
        return $"[{Slot}] {Name} (ID:{Id}, Wear:{Wear:P0})";
    }
}

internal sealed record SkinInfo(string Title, string? Image, string? NameTag, int DefIndex, int PaintIndex, int PaintSeed, float PaintWear)
{
    public List<Sticker> Stickers { get; set; } = [];

    public override string ToString()
    {
        return $"{Title} | DefIndex:{DefIndex} PaintIndex:{PaintIndex} Seed:{PaintSeed} Wear:{PaintWear:F10} {(NameTag != null ? $" NameTag:\"{NameTag}\"" : "")} {(Stickers.Count > 0 ? $" Stickers:[{string.Join(", ", Stickers)}]" : "")}";
    }
}