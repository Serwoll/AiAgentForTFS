namespace AiAgentForTFS;

using System.Text.Json;

public sealed record AppConfig(
    string AzureDevOpsBaseUrl,
    string AzureDevOpsPat,
    string AzureDevOpsApiVersion,
    Uri OllamaUrl,
    string OllamaModel,
    Uri? ConfluenceBaseUrl,
    string? ConfluenceUsername,
    string? ConfluenceToken,
    string ConfluenceAuthMode)
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
        var confluenceBaseUrlText = Environment.GetEnvironmentVariable("CONFLUENCE_BASE_URL")
            ?? settings.Confluence?.BaseUrl;
        var confluenceUsername = Environment.GetEnvironmentVariable("CONFLUENCE_USERNAME")
            ?? settings.Confluence?.Username;
        var confluenceToken = Environment.GetEnvironmentVariable("CONFLUENCE_TOKEN")
            ?? settings.Confluence?.Token;
        var confluenceAuthMode = Environment.GetEnvironmentVariable("CONFLUENCE_AUTH_MODE")
            ?? settings.Confluence?.AuthMode
            ?? "Bearer";

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

        return new AppConfig(
            azureDevOpsBaseUrl.ToString().TrimEnd('/'),
            pat,
            apiVersion,
            ollamaUrl,
            ollamaModel,
            confluenceBaseUrl,
            string.IsNullOrWhiteSpace(confluenceUsername) ? null : confluenceUsername,
            string.IsNullOrWhiteSpace(confluenceToken) ? null : confluenceToken,
            confluenceAuthMode);
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
        ConfluenceSettings? Confluence)
    {
        public static AppSettings Load()
        {
            var path = FindSettingsFile();

            if (path is null)
            {
                return new AppSettings(null, null, null);
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions.Default)
                ?? new AppSettings(null, null, null);
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
}
