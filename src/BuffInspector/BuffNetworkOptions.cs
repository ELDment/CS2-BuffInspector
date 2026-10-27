namespace BuffInspector;

public sealed class BuffNetworkOptions
{
    public int RequestTimeoutSeconds { get; init; } = 15;
    public int OperationTimeoutSeconds { get; init; } = 45;
    public int MaxRedirects { get; init; } = 5;
    public int MaxResponseBytes { get; init; } = 2 * 1024 * 1024;

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
        )
        {
            throw new InvalidOperationException("BUFF 网络配置无效：请检查超时、跳转次数和响应大小。");
        }
    }
}
