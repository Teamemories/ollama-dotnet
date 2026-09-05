using System.Text.Json;
using System.Text.Json.Serialization;

namespace ollama_dotnet;

public abstract class OllamaModel
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

public abstract class Options : OllamaModel
{
    public bool? Numa { get; init; }
    public int? NumCtx { get; init; }
    public int? NumBatch { get; init; }
    public int? NumGpu { get; init; }
    public int? MainGpu { get; init; }
    public bool? LowVram { get; init; }
    public bool? F16Kv { get; init; }
    public bool? LogitsAll { get; init; }
    public bool? VocabOnly { get; init; }
    public bool? UseMmap { get; init; }
    public bool? UseMlock { get; init; }
    public bool? EmbeddingOnly { get; init; }
    public int? NumThread { get; init; }
    public int? NumKeep { get; init; }
    public int? Seed { get; init; }
    public int? NumPredict { get; init; }
    public int? TopK { get; init; }
    public double? TopP { get; init; }
    public double? TfsZ { get; init; }
    public double? TypicalP { get; init; }
    public int? RepeatLastN { get; init; }
    public double? Temperature { get; init; }
    public double? RepeatPenalty { get; init; }
    public double? PresencePenalty { get; init; }
    public double? FrequencyPenalty { get; init; }
    public int? Mirostat { get; init; }
    public double? MirostatTau { get; init; }
    public double? MirostatEta { get; init; }
    public bool? PenalizeNewline { get; init; }
    public IReadOnlyList<string>? Stop { get; init; }
}

public class Image
{
    [JsonIgnore] private object? Value { get; set; }
    public Image(string value) => Value = value;
    public Image(byte[] value) => Value = value;
    public Image(string path, bool filePath) => Value = filePath ? File.ReadAllBytes(path) : path;

    public string ToBase64()
    {
        if (Value is byte[] bytes) return Convert.ToBase64String(bytes);
        var value = Value as string ?? throw new InvalidOperationException("Image value is required.");
        if (File.Exists(value)) return Convert.ToBase64String(File.ReadAllBytes(value));
        if (Path.GetExtension(value).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(value).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(value).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(value).Equals(".webp", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException($"File {value} does not exist", value);
        try
        {
            Convert.FromBase64String(value);
            return value;
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Invalid image data, expected base64 string or path to image file.", ex);
        }
    }
}

public class Message
{
    public string Role { get; init; } = "";
    public string? Content { get; init; }
    public string? Thinking { get; init; }
    public IReadOnlyList<Image>? Images { get; init; }
    public string? ToolName { get; init; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    public class ToolCall
    {
        public Function Function { get; init; } = new();
    }

    public class Function
    {
        public string Name { get; init; } = "";
        public Dictionary<string, JsonElement> Arguments { get; init; } = new();
    }
}

public class Tool
{
    public string Type { get; init; } = "function";
    public ToolFunction? Function { get; init; }

    public class ToolFunction
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public Parameters? Parameters { get; init; }
    }

    public class Parameters
    {
        public string Type { get; set; } = "object";
        [JsonPropertyName("$defs")] public JsonElement? Defs { get; set; }
        public JsonElement? Items { get; set; }
        public IReadOnlyList<string>? Required { get; set; }
        public Dictionary<string, Property>? Properties { get; set; }
    }

    public class Property
    {
        public object? Type { get; set; }
        public JsonElement? Items { get; set; }
        public string? Description { get; set; }
        public IReadOnlyList<JsonElement>? Enum { get; set; }
    }
}

public class GenerateRequest : OllamaModel
{
    public string Model { get; init; } = "";
    public string? Prompt { get; init; }
    public string? Suffix { get; init; }
    public string? System { get; init; }
    public string? Template { get; init; }
    public IReadOnlyList<int>? Context { get; init; }
    public bool? Stream { get; init; }
    public object? Format { get; init; }
    public IReadOnlyList<Image>? Images { get; init; }
    public Options? Options { get; init; }
    public object? KeepAlive { get; init; }
    public object? Think { get; init; }
    public bool? Logprobs { get; init; }
    public int? TopLogprobs { get; init; }
    public bool? Raw { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Steps { get; init; }
}

public class ChatRequest : GenerateRequest
{
    public IReadOnlyList<Message>? Messages { get; init; }
    public IReadOnlyList<Tool>? Tools { get; init; }
}

public class EmbedRequest : OllamaModel
{
    public string Model { get; init; } = "";
    public object Input { get; init; } = "";
    public bool? Truncate { get; init; }
    public Options? Options { get; init; }
    public object? KeepAlive { get; init; }
    public int? Dimensions { get; init; }
}

public class EmbeddingsRequest : OllamaModel
{
    public string Model { get; init; } = "";
    public string? Prompt { get; init; }
    public Options? Options { get; init; }
    public object? KeepAlive { get; init; }
}

public class PullRequest : OllamaModel
{
    public string Model { get; init; } = "";
    public bool? Insecure { get; init; }
    public bool? Stream { get; init; }
}

public class PushRequest : PullRequest
{
}

public class CreateRequest : PullRequest
{
    public string? Quantize { get; init; }
    [JsonPropertyName("from")] public string? From { get; init; }
    public IReadOnlyDictionary<string, string>? Files { get; init; }
    public IReadOnlyDictionary<string, string>? Adapters { get; init; }
    public string? Template { get; init; }
    public object? License { get; init; }
    public string? System { get; init; }
    public Options? Parameters { get; init; }
    public IReadOnlyList<Message>? Messages { get; init; }
}

public class DeleteRequest
{
    public string Model { get; init; } = "";
}

public class CopyRequest
{
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
}

public class ShowRequest
{
    public string Model { get; init; } = "";
}

public class WebSearchRequest
{
    public string Query { get; set; } = "";
    public int? MaxResults { get; set; }
}

public class WebFetchRequest
{
    public string Url { get; set; } = "";
}

public class GenerateResponse
{
    public string? Model { get; set; }
    public string? CreatedAt { get; set; }
    public bool? Done { get; set; }
    public string? DoneReason { get; set; }
    public long? TotalDuration { get; set; }
    public long? LoadDuration { get; set; }
    public int? PromptEvalCount { get; set; }
    public long? PromptEvalDuration { get; set; }
    public int? EvalCount { get; set; }
    public long? EvalDuration { get; set; }
    public string? Response { get; set; }
    public string? Thinking { get; set; }
    public IReadOnlyList<int>? Context { get; set; }
    public IReadOnlyList<Logprob>? Logprobs { get; set; }
    public string? Image { get; set; }
    public int? Completed { get; set; }
    public int? Total { get; set; }
}

public class ChatResponse : GenerateResponse
{
    public Message Message { get; set; } = new();
}

public class EmbedResponse : GenerateResponse
{
    public IReadOnlyList<IReadOnlyList<float>> Embeddings { get; set; } = [];
}

public class EmbeddingsResponse
{
    public IReadOnlyList<float> Embedding { get; set; } = [];
}

public class TokenLogprob
{
    public string Token { get; set; } = "";
    public double Logprob { get; set; }
}

public class Logprob : TokenLogprob
{
    public IReadOnlyList<TokenLogprob>? TopLogprobs { get; set; }
}

public class StatusResponse
{
    public string? Status { get; set; }
}

public abstract class ProgressResponse : StatusResponse
{
    public int? Completed { get; set; }
    public int? Total { get; set; }
    public string? Digest { get; set; }
}

public abstract class ModelDetails
{
    public string? ParentModel { get; set; }
    public string? Format { get; set; }
    public string? Family { get; set; }
    public IReadOnlyList<string>? Families { get; set; }
    public string? ParameterSize { get; set; }
    public string? QuantizationLevel { get; set; }
}

public abstract class ListResponse
{
    public IReadOnlyList<Model> Models { get; set; } = [];

    public class Model
    {
        [JsonPropertyName("model")] public string? Name { get; set; }
        public DateTimeOffset? ModifiedAt { get; set; }
        public string? Digest { get; set; }
        public long? Size { get; set; }
        public ModelDetails? Details { get; set; }
    }
}

public abstract class ShowResponse
{
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? Template { get; set; }
    public string? Modelfile { get; set; }
    public string? License { get; set; }
    public ModelDetails? Details { get; set; }
    [JsonPropertyName("model_info")] public Dictionary<string, JsonElement>? ModelInfo { get; set; }
    public string? Parameters { get; set; }
    public IReadOnlyList<string>? Capabilities { get; set; }
}

public abstract class ProcessResponse
{
    public IReadOnlyList<Model> Models { get; set; } = [];

    public abstract class Model
    {
        [JsonPropertyName("model")] public string? ModelName { get; set; }
        public string? Name { get; set; }
        public string? Digest { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public long? Size { get; set; }
        public long? SizeVram { get; set; }
        public ModelDetails? Details { get; set; }
        public int? ContextLength { get; set; }
    }
}

public abstract class WebSearchResponse
{
    public IReadOnlyList<WebSearchResult> Results { get; set; } = [];
}

public abstract class WebSearchResult
{
    public string? Content { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
}

public abstract class WebFetchResponse
{
    public string? Title { get; set; }
    public string? Content { get; set; }
    public IReadOnlyList<string>? Links { get; set; }
}

public class RequestException(string message) : Exception(message)
{
    public string Error => Message;
}

public class ResponseException(string error, int statusCode = -1) : Exception(Parse(error))
{
    public int StatusCode { get; } = statusCode;
    public string Error => Message;

    private static string Parse(string error)
    {
        try
        {
            return JsonDocument.Parse(error).RootElement.TryGetProperty("error", out var e)
                ? e.GetString() ?? error
                : error;
        }
        catch (JsonException)
        {
            return error;
        }
    }

    public override string ToString() => $"{Error} (status code: {StatusCode})";
}