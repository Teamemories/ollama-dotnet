using System.Text.Json;
using System.Text.Json.Serialization;

namespace ollama_dotnet;

internal static class OllamaJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new ImageJsonConverter());
        return options;
    }
}

internal static class OllamaUtils
{
    public static string ParseHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) value = OllamaConstants.DefaultHost;
        if (!value.Contains("://")) value = "https://" + value;
        var uri = new Uri(value.TrimEnd('/'));
        var scheme = uri.Scheme;
        var port = uri.IsDefaultPort ? (scheme == "https" ? 443 : 11434) : uri.Port;
        var host = uri.Host.Contains(':') ? $"[{uri.Host}]" : uri.Host;
        var path = uri.AbsolutePath.Trim('/');
        return $"{scheme}://{host}:{port}{(path.Length == 0 ? "" : "/" + path)}";
    }

    internal static void RequireApiKey(HttpClient client)
    {
        if (!string.Equals(client.DefaultRequestHeaders.Authorization?.Scheme, "Bearer",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Authorization header with API key is required for web requests.");
    }
}

internal sealed class ImageJsonConverter : JsonConverter<Image>
{
    public override Image Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("Image data cannot be null."));

    public override void Write(Utf8JsonWriter writer, Image value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToBase64());
}