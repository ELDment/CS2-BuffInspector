using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BuffInspector;

internal sealed class BuffConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly string[] NetworkKeys =
    [
        nameof(BuffNetworkOptions.RequestTimeoutSeconds),
        nameof(BuffNetworkOptions.OperationTimeoutSeconds),
        nameof(BuffNetworkOptions.MaxRedirects),
        nameof(BuffNetworkOptions.MaxResponseBytes),
    ];

    public BuffOptions Main { get; init; } = new();
    public BuffNetworkOptions Network { get; init; } = new();

    internal static BuffConfiguration Parse(string text)
    {
        var configuration = JsonSerializer.Deserialize<BuffConfiguration>(text, JsonOptions);
        if (configuration?.Main is null || configuration.Network is null)
        {
            throw new InvalidOperationException("BUFF 配置为空，Main 和 Network 必须是配置对象。");
        }
        configuration.Main.Validate();
        configuration.Network.Validate();
        return configuration;
    }

    internal static void MigrateNetwork(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var document =
            JsonNode.Parse(
                File.ReadAllText(path),
                new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
            ) as JsonObject
            ?? throw new InvalidOperationException("BUFF 配置必须是 JSON 对象。");
        if (document["Main"] is not JsonObject main || !NetworkKeys.Any(main.ContainsKey))
        {
            return;
        }

        var network = document["Network"] as JsonObject;
        if (network is null)
        {
            if (document.ContainsKey("Network"))
            {
                throw new InvalidOperationException("BUFF Network 配置必须是 JSON 对象。");
            }
            network = new JsonObject(new JsonNodeOptions { PropertyNameCaseInsensitive = true });
            document["Network"] = network;
        }
        foreach (var key in NetworkKeys)
        {
            if (main.TryGetPropertyValue(key, out var value))
            {
                // Explicit Network values take precedence over legacy Main values.
                if (!network.ContainsKey(key))
                {
                    network[key] = value?.DeepClone();
                }
                main.Remove(key);
            }
        }

        var migrated = document.ToJsonString(JsonOptions);
        _ = Parse(migrated);
        var backupPath = path + ".before-network.bak";
        if (!File.Exists(backupPath))
        {
            File.Copy(path, backupPath);
        }
        File.WriteAllText(path, migrated.ReplaceLineEndings("\r\n") + "\r\n", new UTF8Encoding(false));
    }
}
