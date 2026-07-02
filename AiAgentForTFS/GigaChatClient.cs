using Microsoft.Extensions.AI;

namespace AiAgentForTFS;

public sealed class GigaChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;

    public GigaChatClient(HttpClient httpClient, AppConfig config)
    {
        _httpClient = httpClient;
        _config = config;

        if (config.GigaChatAuthKey == null)
            throw new ConfigurationException("Пустой ключ авторозации");
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        var message = new ChatMessage(ChatRole.Assistant, "GigaChat fake answer");
        var response = new ChatResponse(message);
        return Task.FromResult(response);
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
}