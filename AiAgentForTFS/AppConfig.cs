namespace AiAgentForTFS;

using System.Text.Json;

public sealed record AppConfig(
    string AzureDevOpsBaseUrl,
    string AzureDevOpsPat,
    string AzureDevOpsApiVersion,
    Uri OllamaUrl,
    string OllamaModel)
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
            ?? "7.1";
        var ollamaUrlText = Environment.GetEnvironmentVariable("OLLAMA_URL")
            ?? settings.Ollama?.Url
            ?? "http://localhost:11434";
        var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
            ?? settings.Ollama?.Model
            ?? "qwen3:8b";

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ConfigurationException("AzureDevOps:BaseUrl is required in appsettings.json or AZURE_DEVOPS_BASE_URL.");
        }

        if (string.IsNullOrWhiteSpace(pat))
        {
            throw new ConfigurationException("AzureDevOps:Pat is required in appsettings.json or AZURE_DEVOPS_PAT.");
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out var azureDevOpsBaseUrl))
        {
            throw new ConfigurationException("AzureDevOps:BaseUrl must be an absolute URL.");
        }

        if (!Uri.TryCreate(ollamaUrlText.TrimEnd('/'), UriKind.Absolute, out var ollamaUrl))
        {
            throw new ConfigurationException("Ollama:Url must be an absolute URL.");
        }

        return new AppConfig(azureDevOpsBaseUrl.ToString().TrimEnd('/'), pat, apiVersion, ollamaUrl, ollamaModel);
    }

    private static string? GetOptional(string primaryName, string fallbackName)
    {
        var value = Environment.GetEnvironmentVariable(primaryName)
            ?? Environment.GetEnvironmentVariable(fallbackName);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record AppSettings(
        AzureDevOpsSettings? AzureDevOps,
        OllamaSettings? Ollama)
    {
        public static AppSettings Load()
        {
            var path = FindSettingsFile();

            if (path is null)
            {
                return new AppSettings(null, null);
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions.Default)
                ?? new AppSettings(null, null);
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
}
