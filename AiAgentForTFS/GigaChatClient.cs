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
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

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
        var token = await GetAccessTokenAsync(cancellationToken);
        var requestDto = new GigaChatChatRequest(
            Model: _config.GigaChatModel,
            Messages: CreateGigaChatMessages(messages),
            Stream: false
        );
        var requestJson = JsonSerializer.Serialize(requestDto);
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.GigaChatApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(requestJson, Encoding.UTF8, mediaType: "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new AgentException($"GigaChat API вернул {(int)response.StatusCode} {response.ReasonPhrase}: {TrimError(body)}");

        var choises = JsonSerializer.Deserialize<GigaChatChatResponse>(body, JsonOptions.Default) ??
           throw new AgentException("Не удалось прочитать ответ авторизации GigaChat");

        if (choises.Choices.Count == 0)
            throw new AgentException("GigaChat не вернул варианты ответа");
        var chatMessage = new ChatMessage(ChatRole.Assistant, choises.Choices[0].Message.Content);
        return new ChatResponse(chatMessage);
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
        request.Headers.Authorization = CreateAuthHeader(_authKey);
        request.Headers.Add("RqUID", Guid.NewGuid().ToString());

        var content = new KeyValuePair<string, string>("scope", _config.GigaChatScope);
        request.Content = new FormUrlEncodedContent([content]);

        if (!string.IsNullOrEmpty(_accessToken) && (DateTimeOffset.UtcNow < _accessTokenExpiresAt))
            return _accessToken;

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new AgentException($"GigaChat API вернул {(int)response.StatusCode} {response.ReasonPhrase}: {TrimError(body)}");

        var dto = JsonSerializer.Deserialize<AuthBodyDto>(body, JsonOptions.Default) ??
            throw new AgentException("Не удалось прочитать ответ авторизации GigaChat");

        if (string.IsNullOrWhiteSpace(dto.AccessToken))
            throw new AgentException("GigaChat не вернул access_token в ответе авторизации");

        _accessTokenExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.ExpiresAt).AddMinutes(-1);
        _accessToken = dto.AccessToken;
        return _accessToken;


    }
    private static List<GigaChatMessage> CreateGigaChatMessages(IEnumerable<ChatMessage> chatMessages)
    {
        return chatMessages.Where(content => !string.IsNullOrWhiteSpace(content.Text)).Select(content => new GigaChatMessage(CreateGigaChatRole(content.Role), content.Text)).ToList();
    }

    private static string CreateGigaChatRole(ChatRole chatRole)
    {
        if (chatRole == ChatRole.User)
            return "user";
        if (chatRole == ChatRole.Assistant)
            return "assistant";
        if (chatRole == ChatRole.System)
            return "system";
        throw new AgentException($"Используется неподдерживаемая роль {chatRole}");
    }
    private static AuthenticationHeaderValue CreateAuthHeader(string authKey)
    {
        return new AuthenticationHeaderValue("Basic", authKey);
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
        long ExpiresAt);

    private sealed record GigaChatChatRequest(
        [property: JsonPropertyName("model")]
        string Model,
        [property: JsonPropertyName("messages")]
        IReadOnlyList<GigaChatMessage> Messages,
        [property: JsonPropertyName("stream")]
        bool Stream);

    private sealed record GigaChatMessage(
        [property: JsonPropertyName("role")]
        string Role,
        [property: JsonPropertyName("content")]
        string Content);

    private sealed record GigaChatChatResponse(
        [property: JsonPropertyName("choices")]
        IReadOnlyList<GigaChatChoice> Choices);

    private sealed record GigaChatChoice(
        [property: JsonPropertyName("message")]
        GigaChatMessage Message);
}
