using System.ComponentModel;

namespace BuffInspector;

public sealed class BuffOptions
{
    [Description("解析成功后自动替换同槽位武器、发放并切换到对应武器；关闭时仅应用皮肤。修改后重载插件生效。")]
    public bool AutoEquipWeapon { get; init; } = true;

    public int PreviewDurationMilliseconds { get; init; } = 8000;
    public int PreviewImageWidth { get; init; } = 300;

    internal void Validate()
    {
        if (PreviewDurationMilliseconds < 0 || PreviewImageWidth <= 0)
        {
            throw new InvalidOperationException("BUFF 功能配置无效：请检查预览时长和图片宽度。");
        }
    }
}
