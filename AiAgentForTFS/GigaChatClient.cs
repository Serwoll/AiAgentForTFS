using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace AiAgentForTFS;


public sealed class GigaChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;
    private readonly string _authKey;

    public GigaChatClient(HttpClient httpClient, AppConfig config)
    {
        _httpClient = httpClient;
        _config = config;

        if (config.GigaChatAuthKey == null)
            throw new ConfigurationException("Пустой ключ авторозации");
        _authKey = config.GigaChatAuthKey;
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        var message = new ChatMessage(ChatRole.Assistant, await GetAccessTokenAsync());
        var response = new ChatResponse(message);
        return response;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        throw new NotSupportedException("Метод будет реализован позже");
    }

    public object? GetService(Type serviceType, object? serviceKey)
    {
        return null;
    }

    public void Dispose()
    {

    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default(CancellationToken))
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.GigaChatAuthUrl);
        request.Headers.Authorization = CreatePatHeader(_authKey);

        var content = new KeyValuePair<string, string>("scope", _config.GigaChatScope);
        request.Content = new FormUrlEncodedContent([content]);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new AgentException($"GigaChat API вернул {(int)response.StatusCode} {response.ReasonPhrase}: {TrimError(body)}");

        var dto = JsonSerializer.Deserialize<AuthBodyDto>(body, JsonOptions.Default) ??
            throw new AgentException("Не удалось прочитать ответ авторизации GigaChat");

        if (string.IsNullOrWhiteSpace(dto.AccessToken))
            throw new AgentException("GigaChat не вернул access_token в ответе авторизации");

        return dto.AccessToken;


    }
    private static AuthenticationHeaderValue CreatePatHeader(string pat)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return new AuthenticationHeaderValue("Basic", token);
    }
    private static string TrimError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty response body";
        }

        return body.Length <= 500 ? body : body[..500] + "...";
    }

    private sealed record AuthBodyDto(
        [property: JsonPropertyName("access_token")]
        string AccessToken,
        [property: JsonPropertyName("expires_at")]
        int ExpiresAt);
}