namespace BuffInspector.Domain;

internal sealed record SkinInfo(
    string Title,
    string? Image,
    string? NameTag,
    int? DefinitionIndex,
    int PaintIndex,
    int PaintSeed,
    float PaintWear,
    IReadOnlyList<Decoration> Stickers,
    IReadOnlyList<Decoration> Keychains
);

internal sealed record Decoration(
    int Id,
    int Slot,
    string Name,
    float Wear = 0,
    float OffsetX = 0,
    float OffsetY = 0,
    float OffsetZ = 0,
    float Rotation = 0,
    int Seed = 0,
    float Scale = 1
);
