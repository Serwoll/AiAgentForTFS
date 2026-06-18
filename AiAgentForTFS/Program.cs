using AiAgentForTFS;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Diagnostics;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    try
    {
        return args[0].ToLowerInvariant() switch
        {
            "generate-qa-checklist" => await RunGenerateQaChecklistAsync(args),
            "generate-qa-checklist-from-confluence" => await RunGenerateQaChecklistFromConfluenceAsync(args),
            "chat" => await RunChatAsync(),
            _ => PrintUsageAndReturn()
        };
    }
    catch (ConfigurationException ex)
    {
        Console.Error.WriteLine($"Ошибка конфигурации: {ex.Message}");
        return 2;
    }
    catch (AgentException ex)
    {
        Console.Error.WriteLine($"Ошибка агента: {ex.Message}");
        return 3;
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Ошибка сети: {ex.Message}");
        return 4;
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine("Запрос превысил время ожидания. Проверь доступность Azure DevOps/TFS, Confluence и Ollama.");
        return 5;
    }
}

static async Task<int> RunGenerateQaChecklistAsync(string[] args)
{
    if (args.Length != 2)
    {
        PrintUsage();
        return 1;
    }

    if (!int.TryParse(args[1], out var workItemId) || workItemId <= 0)
    {
        Console.Error.WriteLine("ID work item должен быть положительным числом.");
        return 1;
    }

    var config = AppConfig.FromEnvironment();

    using var httpClient = CreateHttpClient();
    var gitClient = new AzureDevOpsGitClient(httpClient, config);
    var confluenceClient = new ConfluenceClient(httpClient, config);
    var tfsClient = new TfsWorkItemClient(httpClient, config, gitClient, confluenceClient);
    var chatClient = new OllamaChatClient(config.OllamaUrl, config.OllamaModel);
    var workflow = new QaChecklistWorkflow(tfsClient, chatClient, confluenceClient);

    Console.WriteLine($"Запускаю генерацию QA чек-листа для User Story {workItemId}...");
    var generationTimer = Stopwatch.StartNew();
    var result = await workflow.GenerateForUserStoryAsync(workItemId);
    generationTimer.Stop();

    PrintWorkflowResult(result, generationTimer.Elapsed);

    return 0;
}

static async Task<int> RunGenerateQaChecklistFromConfluenceAsync(string[] args)
{
    if (args.Length != 2)
    {
        PrintUsage();
        return 1;
    }

    var url = args[1];
    if (!Uri.TryCreate(url, UriKind.Absolute, out _))
    {
        Console.Error.WriteLine("Ссылка на Confluence должна быть абсолютным URL.");
        return 1;
    }

    var config = AppConfig.FromEnvironment();

    using var httpClient = CreateHttpClient();
    var gitClient = new AzureDevOpsGitClient(httpClient, config);
    var confluenceClient = new ConfluenceClient(httpClient, config);
    var tfsClient = new TfsWorkItemClient(httpClient, config, gitClient, confluenceClient);
    var chatClient = new OllamaChatClient(config.OllamaUrl, config.OllamaModel);
    var workflow = new QaChecklistWorkflow(tfsClient, chatClient, confluenceClient);

    Console.WriteLine("Запускаю генерацию QA чек-листа по странице Confluence...");
    var generationTimer = Stopwatch.StartNew();
    var result = await workflow.GenerateForConfluenceUrlAsync(url);
    generationTimer.Stop();

    PrintWorkflowResult(result, generationTimer.Elapsed);

    return 0;
}

static async Task<int> RunChatAsync()
{
    var config = AppConfig.FromEnvironment();

    Console.WriteLine("Вставь текст User Story. Заверши ввод строкой `done`.");
    var userStoryText = ReadMultilineInput();

    if (string.IsNullOrWhiteSpace(userStoryText))
    {
        Console.Error.WriteLine("Текст User Story пустой.");
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine("Вставь дополнительные PR/commit/code/Confluence ссылки или заметки. Заверши ввод строкой `done`.");
    var linkText = ReadMultilineInput();
    var manualInput = new ManualUserStoryInput(
        userStoryText,
        linkText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    var workItem = CreateManualWorkItem(manualInput);

    using var httpClient = CreateHttpClient();
    var gitClient = new AzureDevOpsGitClient(httpClient, config);
    var confluenceClient = new ConfluenceClient(httpClient, config);
    var enrichedWorkItem = await gitClient.EnrichAsync(workItem);
    enrichedWorkItem = await confluenceClient.EnrichAsync(enrichedWorkItem);

    return await GenerateAndSaveAsync(enrichedWorkItem, config);
}

static async Task<int> GenerateAndSaveAsync(WorkItemContext workItem, AppConfig config)
{
    var chatClient = new OllamaChatClient(config.OllamaUrl, config.OllamaModel);
    AIAgent agent = chatClient.AsAIAgent(
        instructions: "You are a senior QA engineer. Return practical Markdown only.");

    var prompt = ChecklistPromptBuilder.Build(workItem);

    Console.WriteLine("Генерирую QA чек-лист...");
    var responseTimer = Stopwatch.StartNew();
    var response = await agent.RunAsync(prompt);
    responseTimer.Stop();

    var markdown = ChecklistPromptBuilder.NormalizeMarkdown(workItem, response.ToString(), responseTimer.Elapsed);

    Directory.CreateDirectory("output");
    var fileName = workItem.Id > 0
        ? $"us-{workItem.Id}-qa-checklist.md"
        : $"manual-us-{DateTime.Now:yyyyMMdd-HHmmss}-qa-checklist.md";
    var outputPath = Path.Combine("output", fileName);
    await File.WriteAllTextAsync(outputPath, markdown);

    Console.WriteLine();
    Console.WriteLine(markdown);
    Console.WriteLine($"Время ответа LLM: {FormatElapsed(responseTimer.Elapsed)}");
    Console.WriteLine($"Сохранено в: {Path.GetFullPath(outputPath)}");

    return 0;
}

static void PrintWorkflowResult(QaChecklistWorkflowResult result, TimeSpan elapsed)
{
    Console.WriteLine();
    Console.WriteLine(result.Markdown);
    Console.WriteLine($"Время генерации чек-листа: {FormatElapsed(elapsed)}");
    Console.WriteLine($"Сохранено в: {result.OutputPath}");
}

static string FormatElapsed(TimeSpan elapsed)
{
    return elapsed.TotalSeconds < 60
        ? $"{elapsed.TotalSeconds:F1}s"
        : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
}

static WorkItemContext CreateManualWorkItem(ManualUserStoryInput input)
{
    var links = input.Links
        .Select(link => new WorkItemLink("Manual", link, "Manual context", null, LooksLikeCodeContext(link)))
        .ToList();

    return new WorkItemContext(
        0,
        ExtractTitle(input.Text),
        "Manual User Story",
        "Manual",
        null,
        input.Text,
        null,
        null,
        [],
        links,
        [],
        [],
        [],
        []);
}

static string ExtractTitle(string text)
{
    var firstLine = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(firstLine))
    {
        return "Manual User Story";
    }

    return firstLine.Length <= 100 ? firstLine : firstLine[..100];
}

static string ReadMultilineInput()
{
    var lines = new List<string>();

    while (true)
    {
        var line = Console.ReadLine();

        if (line is null || line.Equals("done", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        lines.Add(line);
    }

    return string.Join(Environment.NewLine, lines);
}

static HttpClient CreateHttpClient()
{
    return new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(2)
    };
}

static bool LooksLikeCodeContext(string value)
{
    return value.Contains("vstfs:///Git/PullRequestId", StringComparison.OrdinalIgnoreCase)
        || value.Contains("pull request", StringComparison.OrdinalIgnoreCase)
        || value.Contains("commit", StringComparison.OrdinalIgnoreCase)
        || value.Contains("changeset", StringComparison.OrdinalIgnoreCase)
        || value.Contains("branch", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".cs", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".cpp", StringComparison.OrdinalIgnoreCase)
        || value.Contains(".sql", StringComparison.OrdinalIgnoreCase);
}

static int PrintUsageAndReturn()
{
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("Использование:");
    Console.WriteLine("  dotnet run --project .\\AiAgentForTFS\\AiAgentForTFS.csproj -- generate-qa-checklist <workItemId>");
    Console.WriteLine("  dotnet run --project .\\AiAgentForTFS\\AiAgentForTFS.csproj -- generate-qa-checklist-from-confluence <url>");
    Console.WriteLine("  dotnet run --project .\\AiAgentForTFS\\AiAgentForTFS.csproj -- chat");
    Console.WriteLine();
    Console.WriteLine("Переменные окружения:");
    Console.WriteLine("  AZURE_DEVOPS_BASE_URL  пример: https://dev.azure.com/org/project");
    Console.WriteLine("  AZURE_DEVOPS_PAT       Personal Access Token с доступом на чтение Work Items");
    Console.WriteLine("  AZURE_DEVOPS_API_VERSION опционально, по умолчанию: 7.0");
    Console.WriteLine("  OLLAMA_URL             опционально, по умолчанию: http://localhost:11434");
    Console.WriteLine("  OLLAMA_MODEL           опционально, по умолчанию: qwen3:8b");
    Console.WriteLine("  CONFLUENCE_BASE_URL    опционально, пример: https://confluence.company.ru");
    Console.WriteLine("  CONFLUENCE_TOKEN       опционально, token/PAT для чтения страниц");
    Console.WriteLine("  CONFLUENCE_AUTH_MODE   опционально, Bearer или Basic, по умолчанию: Bearer");
    Console.WriteLine("  CONFLUENCE_USERNAME    нужен только при CONFLUENCE_AUTH_MODE=Basic");
}
