using System.ComponentModel;

namespace BuffInspector;

public sealed class BuffOptions
{
    [Description("解析成功后自动替换同槽位武器、发放并切换到对应武器；关闭时仅应用皮肤。修改后重载插件生效。")]
    public bool AutoEquipWeapon { get; init; } = true;

    public int RequestTimeoutSeconds { get; init; } = 15;
    public int OperationTimeoutSeconds { get; init; } = 45;
    public int MaxRedirects { get; init; } = 5;
    public int MaxResponseBytes { get; init; } = 2 * 1024 * 1024;
    public int PreviewDurationMilliseconds { get; init; } = 8000;
    public int PreviewImageWidth { get; init; } = 300;

    internal void Validate()
    {
        if (
            RequestTimeoutSeconds <= 0
            || RequestTimeoutSeconds > int.MaxValue / 1000
            || OperationTimeoutSeconds < RequestTimeoutSeconds
            || OperationTimeoutSeconds > int.MaxValue / 1000
            || MaxRedirects < 0
            || MaxRedirects == int.MaxValue
            || MaxResponseBytes <= 0
            || PreviewDurationMilliseconds < 0
            || PreviewImageWidth <= 0
        )
        {
            throw new InvalidOperationException("BUFF 配置无效：请检查超时、跳转次数、响应大小、预览时长和图片宽度。");
        }
    }
}
