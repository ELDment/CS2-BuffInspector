using BuffInspector.Domain;

namespace BuffInspector.Parsing;

internal sealed record ItemData
{
    public string? Title { get; init; }
    public string? Image { get; init; }
    public string? NameTag { get; init; }
    public int? DefinitionIndex { get; init; }
    public int? PaintIndex { get; init; }
    public int? PaintSeed { get; init; }
    public float? PaintWear { get; init; }
    public IReadOnlyList<Decoration>? Stickers { get; init; }
    public IReadOnlyList<Decoration>? Keychains { get; init; }

    public static SkinInfo Merge(ItemData html, ItemData metadata, ItemData inspect)
    {
        var title = html.Title ?? metadata.Title;
        var paintIndex = html.PaintIndex ?? metadata.PaintIndex ?? inspect.PaintIndex;
        var paintSeed = html.PaintSeed ?? metadata.PaintSeed ?? inspect.PaintSeed;
        var paintWear = html.PaintWear ?? metadata.PaintWear ?? inspect.PaintWear;
        if (string.IsNullOrWhiteSpace(title) || paintIndex is not >= 0 || paintSeed is not >= 0 || paintWear is not >= 0 or > 1)
        {
            throw new BuffException("BUFF 未返回完整的皮肤信息，物品可能已下架或访问受限。");
        }
        return new SkinInfo(
            title,
            html.Image ?? metadata.Image,
            inspect.NameTag ?? metadata.NameTag ?? html.NameTag,
            inspect.DefinitionIndex ?? metadata.DefinitionIndex,
            paintIndex.Value,
            paintSeed.Value,
            paintWear.Value,
            MergeDecorations(metadata.Stickers ?? html.Stickers, inspect.Stickers),
            MergeDecorations(metadata.Keychains ?? html.Keychains, inspect.Keychains)
        );
    }

    private static IReadOnlyList<Decoration> MergeDecorations(IReadOnlyList<Decoration>? metadata, IReadOnlyList<Decoration>? inspected)
    {
        if (inspected is null)
        {
            return metadata ?? [];
        }
        // Inspect contains the actual placement. Metadata supplies display names only.
        return inspected
            .Select((decoration, index) => decoration with { Name = metadata is not null && index < metadata.Count ? metadata[index].Name : decoration.Name })
            .ToArray();
    }
}
