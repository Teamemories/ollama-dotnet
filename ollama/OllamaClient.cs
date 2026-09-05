using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ollama_dotnet;

public class OllamaClient : IDisposable
{
    protected readonly HttpClient Http;
    protected static readonly JsonSerializerOptions Json = OllamaJson.Options;
    private readonly bool _ownsHttpClient;

    public OllamaClient(string? host = null, HttpClient? httpClient = null, bool followRedirects = true,
        TimeSpan? timeout = null, IDictionary<string, string>? headers = null)
    {
        Http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = followRedirects });
        _ownsHttpClient = httpClient is null;
        if (timeout.HasValue) Http.Timeout = timeout.Value;
        Http.BaseAddress =
            new Uri(OllamaUtils.ParseHost(host ?? Environment.GetEnvironmentVariable("OLLAMA_HOST")));
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            $"ollama-dotnet/{typeof(OllamaClient).Assembly.GetName().Version} ({Environment.OSVersion.Platform})");
        if (headers != null)
            foreach (var h in headers)
                Http.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
        var key = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
        if (!Http.DefaultRequestHeaders.Contains("Authorization") && !string.IsNullOrEmpty(key))
            Http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {key}");
    }

    public GenerateResponse Generate(GenerateRequest request) =>
        Send<GenerateResponse>(HttpMethod.Post, "/api/generate", RequireRequest(request));

    public IEnumerable<GenerateResponse> GenerateStream(GenerateRequest request) =>
        Stream<GenerateResponse>(HttpMethod.Post, "/api/generate", RequireRequest(request));

    public ChatResponse Chat(ChatRequest request) =>
        Send<ChatResponse>(HttpMethod.Post, "/api/chat", RequireRequest(request));

    public IEnumerable<ChatResponse> ChatStream(ChatRequest request) =>
        Stream<ChatResponse>(HttpMethod.Post, "/api/chat", RequireRequest(request));

    public EmbedResponse Embed(EmbedRequest request) =>
        Send<EmbedResponse>(HttpMethod.Post, "/api/embed", RequireRequest(request));

    public EmbeddingsResponse Embeddings(EmbeddingsRequest request) =>
        Send<EmbeddingsResponse>(HttpMethod.Post, "/api/embeddings", RequireRequest(request));

    public ProgressResponse Pull(PullRequest request) =>
        Send<ProgressResponse>(HttpMethod.Post, "/api/pull", RequireRequest(request));

    public IEnumerable<ProgressResponse> PullStream(PullRequest request) =>
        Stream<ProgressResponse>(HttpMethod.Post, "/api/pull", RequireRequest(request));

    public ProgressResponse Push(PushRequest request) =>
        Send<ProgressResponse>(HttpMethod.Post, "/api/push", RequireRequest(request));

    public IEnumerable<ProgressResponse> PushStream(PushRequest request) =>
        Stream<ProgressResponse>(HttpMethod.Post, "/api/push", RequireRequest(request));

    public ProgressResponse Create(CreateRequest request) =>
        Send<ProgressResponse>(HttpMethod.Post, "/api/create", RequireRequest(request));

    public IEnumerable<ProgressResponse> CreateStream(CreateRequest request) =>
        Stream<ProgressResponse>(HttpMethod.Post, "/api/create", RequireRequest(request));

    public ListResponse List() => Send<ListResponse>(HttpMethod.Get, "/api/tags");

    public ShowResponse Show(string model) =>
        Send<ShowResponse>(HttpMethod.Post, "/api/show", new ShowRequest { Model = RequireModel(model) });

    public ProcessResponse Ps() => Send<ProcessResponse>(HttpMethod.Get, "/api/ps");

    public StatusResponse Delete(string model) =>
        SendStatus(HttpMethod.Delete, "/api/delete", new DeleteRequest { Model = RequireModel(model) });

    public StatusResponse Copy(string source, string destination) => SendStatus(HttpMethod.Post, "/api/copy",
        new CopyRequest { Source = source, Destination = destination });

    public WebSearchResponse WebSearch(string query, int maxResults = 3)
    {
        ConfigureApiKey();
        OllamaUtils.RequireApiKey(Http);
        return Send<WebSearchResponse>(HttpMethod.Post, "https://ollama.com/api/web_search",
            new WebSearchRequest { Query = query, MaxResults = maxResults });
    }

    public WebFetchResponse WebFetch(string url)
    {
        ConfigureApiKey();
        OllamaUtils.RequireApiKey(Http);
        return Send<WebFetchResponse>(HttpMethod.Post, "https://ollama.com/api/web_fetch",
            new WebFetchRequest { Url = url });
    }

    public string CreateBlob(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        stream.Position = 0;
        using var content = new StreamContent(stream);
        SendRaw(HttpMethod.Post, $"/api/blobs/{digest}", content);
        return digest;
    }

    protected void ConfigureApiKey()
    {
        var key = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            return;

        Http.DefaultRequestHeaders.Remove("Authorization");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + key);
    }

    private T Send<T>(HttpMethod method, string path, object? body = null)
    {
        using var response = SendRaw(method, path, body == null ? null : JsonContent.Create(body, options: Json));
        return Deserialize<T>(response);
    }

    private StatusResponse SendStatus(HttpMethod method, string path, object body)
    {
        using var response = SendRaw(method, path, JsonContent.Create(body, options: Json));
        return new StatusResponse { Status = response.StatusCode == HttpStatusCode.OK ? "success" : "error" };
    }

    private HttpResponseMessage SendRaw(HttpMethod method, string path, HttpContent? content = null)
    {
        try
        {
            ConfigureApiKey();
            using var request = new HttpRequestMessage(method, path);
            request.Content = content;
            var response = Http.Send(request);
            if (!response.IsSuccessStatusCode)
                throw new ResponseException(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                    (int)response.StatusCode);
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException(OllamaConstants.ConnectionErrorMessage, ex);
        }
    }

    private IEnumerable<T> Stream<T>(HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Content = JsonContent.Create(body, options: Json);
        HttpResponseMessage response;
        try
        {
            response = Http.Send(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException(OllamaConstants.ConnectionErrorMessage, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ResponseException(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                    (int)response.StatusCode);
            using var reader = new StreamReader(response.Content.ReadAsStream());
            while (reader.ReadLine() is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("error", out var error))
                    throw new ResponseException(error.GetString() ?? error.ToString());
                yield return doc.Deserialize<T>(Json)!;
            }
        }
    }

    private static T Deserialize<T>(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<T>(Json).GetAwaiter().GetResult() ??
        throw new JsonException("The response body was empty.");

    public void Dispose()
    {
        if (_ownsHttpClient)
            Http.Dispose();
    }

    protected static T RequireRequest<T>(T request) where T : class =>
        request ?? throw new ArgumentNullException(nameof(request));

    protected static string RequireModel(string model) =>
        string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("A model name is required.", nameof(model))
            : model;
}

public class AsyncOllamaClient(
    string? host = null,
    HttpClient? httpClient = null,
    bool followRedirects = true,
    TimeSpan? timeout = null,
    IDictionary<string, string>? headers = null)
    : OllamaClient(host, httpClient, followRedirects, timeout, headers)
{
    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null,
        CancellationToken cancellationToken = default)
    {
        ConfigureApiKey();
        using var request = new HttpRequestMessage(method, path);
        request.Content = body == null ? null : JsonContent.Create(body, options: Json);
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new ResponseException(await response.Content.ReadAsStringAsync(cancellationToken),
                    (int)response.StatusCode);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken) ??
                   throw new JsonException("The response body was empty.");
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException(OllamaConstants.ConnectionErrorMessage, ex);
        }
    }

    public Task<GenerateResponse>
        GenerateAsync(GenerateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<GenerateResponse>(HttpMethod.Post, "/api/generate", RequireRequest(request), cancellationToken);

    public IAsyncEnumerable<GenerateResponse> GenerateStreamAsync(GenerateRequest request,
        CancellationToken cancellationToken = default) =>
        StreamAsync<GenerateResponse>(HttpMethod.Post, "/api/generate", RequireRequest(request), cancellationToken);

    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ChatResponse>(HttpMethod.Post, "/api/chat", RequireRequest(request), cancellationToken);

    public IAsyncEnumerable<ChatResponse> ChatStreamAsync(ChatRequest request,
        CancellationToken cancellationToken = default) =>
        StreamAsync<ChatResponse>(HttpMethod.Post, "/api/chat", RequireRequest(request), cancellationToken);

    public Task<EmbedResponse> EmbedAsync(EmbedRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<EmbedResponse>(HttpMethod.Post, "/api/embed", RequireRequest(request), cancellationToken);

    public Task<EmbeddingsResponse> EmbeddingsAsync(EmbeddingsRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<EmbeddingsResponse>(HttpMethod.Post, "/api/embeddings", RequireRequest(request), cancellationToken);

    public Task<ProgressResponse> PullAsync(PullRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ProgressResponse>(HttpMethod.Post, "/api/pull", RequireRequest(request), cancellationToken);

    public IAsyncEnumerable<ProgressResponse> PullStreamAsync(PullRequest request,
        CancellationToken cancellationToken = default) =>
        StreamAsync<ProgressResponse>(HttpMethod.Post, "/api/pull", RequireRequest(request), cancellationToken);

    public Task<ProgressResponse> PushAsync(PushRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ProgressResponse>(HttpMethod.Post, "/api/push", RequireRequest(request), cancellationToken);

    public IAsyncEnumerable<ProgressResponse> PushStreamAsync(PushRequest request,
        CancellationToken cancellationToken = default) =>
        StreamAsync<ProgressResponse>(HttpMethod.Post, "/api/push", RequireRequest(request), cancellationToken);

    public Task<ProgressResponse> CreateAsync(CreateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ProgressResponse>(HttpMethod.Post, "/api/create", RequireRequest(request), cancellationToken);

    public IAsyncEnumerable<ProgressResponse> CreateStreamAsync(CreateRequest request,
        CancellationToken cancellationToken = default) =>
        StreamAsync<ProgressResponse>(HttpMethod.Post, "/api/create", RequireRequest(request), cancellationToken);

    public Task<ListResponse> ListAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ListResponse>(HttpMethod.Get, "/api/tags", null, cancellationToken);

    public Task<ShowResponse> ShowAsync(string model, CancellationToken cancellationToken = default) =>
        SendAsync<ShowResponse>(HttpMethod.Post, "/api/show", new ShowRequest { Model = model }, cancellationToken);

    public Task<ProcessResponse> PsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ProcessResponse>(HttpMethod.Get, "/api/ps", null, cancellationToken);

    public async Task<StatusResponse> DeleteAsync(string model, CancellationToken cancellationToken = default)
    {
        using var response = await SendResponseAsync(HttpMethod.Delete, "/api/delete",
            new DeleteRequest { Model = model }, cancellationToken);
        return new StatusResponse { Status = response.StatusCode == HttpStatusCode.OK ? "success" : "error" };
    }

    public async Task<StatusResponse> CopyAsync(string source, string destination,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendResponseAsync(HttpMethod.Post, "/api/copy",
            new CopyRequest { Source = source, Destination = destination }, cancellationToken);
        return new StatusResponse { Status = response.StatusCode == HttpStatusCode.OK ? "success" : "error" };
    }

    private async Task<HttpResponseMessage> SendResponseAsync(HttpMethod method, string path, object body,
        CancellationToken cancellationToken)
    {
        ConfigureApiKey();
        using var request = new HttpRequestMessage(method, path);
        request.Content = JsonContent.Create(body, options: Json);
        try
        {
            var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new ResponseException(await response.Content.ReadAsStringAsync(cancellationToken),
                    (int)response.StatusCode);
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException(OllamaConstants.ConnectionErrorMessage, ex);
        }
    }

    private async IAsyncEnumerable<T> StreamAsync<T>(HttpMethod method, string path, object body,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ConfigureApiKey();
        using var request = new HttpRequestMessage(method, path);
        request.Content = JsonContent.Create(body, options: Json);
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException(OllamaConstants.ConnectionErrorMessage, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ResponseException(await response.Content.ReadAsStringAsync(cancellationToken),
                    (int)response.StatusCode);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("error", out var error))
                    throw new ResponseException(error.GetString() ?? error.ToString());
                yield return doc.Deserialize<T>(Json)!;
            }
        }
    }
}

public class ConnectionException(string message, Exception inner) : Exception(message, inner);