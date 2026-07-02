namespace AiAgentForTFS;

using System.Text.Json;

public sealed record AppConfig(
    string AzureDevOpsBaseUrl,
    string AzureDevOpsPat,
    string AzureDevOpsApiVersion,
    Uri OllamaUrl,
    string OllamaModel,
    string LlmProvider,
    Uri? ConfluenceBaseUrl,
    string? ConfluenceUsername,
    string? ConfluenceToken,
    string ConfluenceAuthMode,
    string? GigaChatAuthKey,
    string GigaChatModel,
    string GigaChatScope,
    Uri GigaChatAuthUrl,
    Uri GigaChatApiUrl)
{
    public static AppConfig FromEnvironment()
    {
        var settings = AppSettings.Load();

        var baseUrl = GetOptional("AZURE_DEVOPS_BASE_URL", "TFS_BASE_URL")
            ?? settings.AzureDevOps?.BaseUrl;
        var pat = GetOptional("AZURE_DEVOPS_PAT", "TFS_PAT")
            ?? settings.AzureDevOps?.Pat;
        var apiVersion = GetOptional("AZURE_DEVOPS_API_VERSION", "TFS_API_VERSION")
            ?? settings.AzureDevOps?.ApiVersion
            ?? "7.0";
        var ollamaUrlText = Environment.GetEnvironmentVariable("OLLAMA_URL")
            ?? settings.Ollama?.Url
            ?? "http://localhost:11434";
        var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
            ?? settings.Ollama?.Model
            ?? "qwen3:8b";
        var llmProvider = Environment.GetEnvironmentVariable("LLM_PROVIDER")
            ?? settings.Llm?.Provider
            ?? "Ollama";
        var confluenceBaseUrlText = Environment.GetEnvironmentVariable("CONFLUENCE_BASE_URL")
            ?? settings.Confluence?.BaseUrl;
        var confluenceUsername = Environment.GetEnvironmentVariable("CONFLUENCE_USERNAME")
            ?? settings.Confluence?.Username;
        var confluenceToken = Environment.GetEnvironmentVariable("CONFLUENCE_TOKEN")
            ?? settings.Confluence?.Token;
        var confluenceAuthMode = Environment.GetEnvironmentVariable("CONFLUENCE_AUTH_MODE")
            ?? settings.Confluence?.AuthMode
            ?? "Bearer";
        var gigaChatAuthKey = Environment.GetEnvironmentVariable("GIGACHAT_AUTH_KEY")
            ?? settings.GigaChat?.AuthKey;
        var gigaChatModel = Environment.GetEnvironmentVariable("GIGACHAT_MODEL")
            ?? settings.GigaChat?.Model
            ?? "GigaChat";
        var gigaChatScope = Environment.GetEnvironmentVariable("GIGACHAT_SCOPE")
            ?? settings.GigaChat?.Scope
            ?? "GIGACHAT_API_PERS";
        var gigaChatAuthUrlText = Environment.GetEnvironmentVariable("GIGACHAT_AUTH_URL")
            ?? settings.GigaChat?.AuthUrl
            ?? "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
        var gigaChatApiUrlText = Environment.GetEnvironmentVariable("GIGACHAT_API_URL")
            ?? settings.GigaChat?.ApiUrl
            ?? "https://gigachat.devices.sberbank.ru/api/v1/chat/completions";

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ConfigurationException("Не задан AzureDevOps:BaseUrl в appsettings.json или AZURE_DEVOPS_BASE_URL.");
        }

        if (string.IsNullOrWhiteSpace(pat))
        {
            throw new ConfigurationException("Не задан AzureDevOps:Pat в appsettings.json или AZURE_DEVOPS_PAT.");
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out var azureDevOpsBaseUrl))
        {
            throw new ConfigurationException("AzureDevOps:BaseUrl должен быть абсолютным URL.");
        }

        if (!Uri.TryCreate(ollamaUrlText.TrimEnd('/'), UriKind.Absolute, out var ollamaUrl))
        {
            throw new ConfigurationException("Ollama:Url должен быть абсолютным URL.");
        }

        Uri? confluenceBaseUrl = null;

        if (!string.IsNullOrWhiteSpace(confluenceBaseUrlText)
            && !Uri.TryCreate(confluenceBaseUrlText.TrimEnd('/'), UriKind.Absolute, out confluenceBaseUrl))
        {
            throw new ConfigurationException("Confluence:BaseUrl должен быть абсолютным URL.");
        }

        if (!Uri.TryCreate(gigaChatAuthUrlText.TrimEnd('/'), UriKind.Absolute, out var gigaChatAuthUrl))
        {
            throw new ConfigurationException("GigaChat:AuthUrl должен быть абсолютным URL.");
        }

        if (!Uri.TryCreate(gigaChatApiUrlText.TrimEnd('/'), UriKind.Absolute, out var gigaChatApiUrl))
        {
            throw new ConfigurationException("GigaChat:ApiUrl должен быть абсолютным URL.");
        }

        return new AppConfig(
            azureDevOpsBaseUrl.ToString().TrimEnd('/'),
            pat,
            apiVersion,
            ollamaUrl,
            ollamaModel,
            llmProvider,
            confluenceBaseUrl,
            string.IsNullOrWhiteSpace(confluenceUsername) ? null : confluenceUsername,
            string.IsNullOrWhiteSpace(confluenceToken) ? null : confluenceToken,
            confluenceAuthMode,
            string.IsNullOrWhiteSpace(gigaChatAuthKey) ? null : gigaChatAuthKey,
            gigaChatModel,
            gigaChatScope,
            gigaChatAuthUrl,
            gigaChatApiUrl);
    }

    private static string? GetOptional(string primaryName, string fallbackName)
    {
        var value = Environment.GetEnvironmentVariable(primaryName)
            ?? Environment.GetEnvironmentVariable(fallbackName);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record AppSettings(
        AzureDevOpsSettings? AzureDevOps,
        OllamaSettings? Ollama,
        ConfluenceSettings? Confluence,
        LlmSettings? Llm,
        GigaChatSettings? GigaChat)
    {
        public static AppSettings Load()
        {
            var path = FindSettingsFile();

            if (path is null)
            {
                return new AppSettings(null, null, null, null, null);
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions.Default)
                ?? new AppSettings(null, null, null, null, null);
        }

        private static string? FindSettingsFile()
        {
            var candidates = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
                Path.Combine(AppContext.BaseDirectory, "appsettings.json")
            };

            return candidates.FirstOrDefault(File.Exists);
        }
    }

    private sealed record AzureDevOpsSettings(
        string? BaseUrl,
        string? Pat,
        string? ApiVersion);

    private sealed record OllamaSettings(
        string? Url,
        string? Model);

    private sealed record ConfluenceSettings(
        string? BaseUrl,
        string? Username,
        string? Token,
        string? AuthMode);

    private sealed record LlmSettings(
        string? Provider);

    private sealed record GigaChatSettings(
        string? AuthKey,
        string? Model,
        string? Scope,
        string? AuthUrl,
        string? ApiUrl);
}
