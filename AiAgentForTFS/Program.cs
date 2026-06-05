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
            "chat" => await RunChatAsync(),
            "agent-demo" => await RunAgentDemoAsync(args),
            _ => PrintUsageAndReturn()
        };
    }
    catch (ConfigurationException ex)
    {
        Console.Error.WriteLine($"Configuration error: {ex.Message}");
        return 2;
    }
    catch (AgentException ex)
    {
        Console.Error.WriteLine($"Agent error: {ex.Message}");
        return 3;
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Network error: {ex.Message}");
        return 4;
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine("Request timed out. Check Azure DevOps/TFS and Ollama availability.");
        return 5;
    }
}

static async Task<int> RunAgentDemoAsync(string[] args)
{
    if (args.Length != 2)
    {
        PrintUsage();
        return 1;
    }
    if (!int.TryParse(args[1], out var workItemId) || workItemId <= 0)
    {
        PrintUsage();
        return 1;
    }
    var config = AppConfig.FromEnvironment();
    using var httpClient = CreateHttpClient();
    var gitClient = new AzureDevOpsGitClient(httpClient, config);
    var tfsClient = new TfsWorkItemClient(httpClient, config, gitClient);
    var chatClient = new OllamaChatClient(config.OllamaUrl, config.OllamaModel);
    var tools = new TfsAgentTools(tfsClient);
    AIAgent agent = chatClient.AsAIAgent(
        instructions:
            """
            You are a senior QA engineer.

            Rules:
            - Answer in Russian.
            - Use Markdown only.
            - Use checklist items in "- [ ]" format.
            - Use only facts from tool results.
            - Mark PR/commit/file based checks with [code-context].
            - Do not add Prepared by or Date.
            - If information is missing, add it to Open Questions instead of inventing requirements.
            """,
        tools:
        [
            AIFunctionFactory.Create(tools.GetUserStoryById)
        ]);
    var response = await agent.RunAsync($"Сгенерируй QA чек-лист по User Story {workItemId}. Используй доступные tools");
    Console.WriteLine(response);
    return 0;
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
        Console.Error.WriteLine("Work item id must be a positive number.");
        return 1;
    }

    var config = AppConfig.FromEnvironment();

    using var httpClient = CreateHttpClient();
    var gitClient = new AzureDevOpsGitClient(httpClient, config);
    var tfsClient = new TfsWorkItemClient(httpClient, config, gitClient);

    Console.WriteLine($"Fetching User Story {workItemId}...");
    var workItem = await tfsClient.GetWorkItemAsync(workItemId);

    return await GenerateAndSaveAsync(workItem, config);
}

static async Task<int> RunChatAsync()
{
    var config = AppConfig.FromEnvironment();

    Console.WriteLine("Paste User Story text. Finish with a line containing only `done`.");
    var userStoryText = ReadMultilineInput();

    if (string.IsNullOrWhiteSpace(userStoryText))
    {
        Console.Error.WriteLine("User Story text is empty.");
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine("Paste optional PR/commit/code links or notes. Finish with `done`.");
    var linkText = ReadMultilineInput();
    var manualInput = new ManualUserStoryInput(
        userStoryText,
        linkText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    var workItem = CreateManualWorkItem(manualInput);

    using var httpClient = CreateHttpClient();
    var gitClient = new AzureDevOpsGitClient(httpClient, config);
    var enrichedWorkItem = await gitClient.EnrichAsync(workItem);

    return await GenerateAndSaveAsync(enrichedWorkItem, config);
}

static async Task<int> GenerateAndSaveAsync(WorkItemContext workItem, AppConfig config)
{
    var chatClient = new OllamaChatClient(config.OllamaUrl, config.OllamaModel);
    AIAgent agent = chatClient.AsAIAgent(
        instructions: "You are a senior QA engineer. Return practical Markdown only.");

    var prompt = ChecklistPromptBuilder.Build(workItem);

    Console.WriteLine("Generating QA checklist...");
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
    Console.WriteLine($"Response time: {FormatElapsed(responseTimer.Elapsed)}");
    Console.WriteLine($"Saved to: {Path.GetFullPath(outputPath)}");

    return 0;
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
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project .\\AiAgentForTFS\\AiAgentForTFS.csproj -- generate-qa-checklist <workItemId>");
    Console.WriteLine("  dotnet run --project .\\AiAgentForTFS\\AiAgentForTFS.csproj -- chat");
    Console.WriteLine("  dotnet run --project .\\AiAgentForTFS\\AiAgentForTFS.csproj -- agent-demo <workItemId>");
    Console.WriteLine();
    Console.WriteLine("Environment variables:");
    Console.WriteLine("  AZURE_DEVOPS_BASE_URL  Example: https://dev.azure.com/org/project");
    Console.WriteLine("  AZURE_DEVOPS_PAT       Personal Access Token with Work Items read access");
    Console.WriteLine("  AZURE_DEVOPS_API_VERSION optional, default: 7.0");
    Console.WriteLine("  OLLAMA_URL             optional, default: http://localhost:11434");
    Console.WriteLine("  OLLAMA_MODEL           optional, default: qwen3:8b");
}
