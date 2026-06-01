using AiAgentForTFS;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length != 2 || !args[0].Equals("generate-qa-checklist", StringComparison.OrdinalIgnoreCase))
    {
        PrintUsage();
        return 1;
    }

    if (!int.TryParse(args[1], out var workItemId) || workItemId <= 0)
    {
        Console.Error.WriteLine("Work item id must be a positive number.");
        return 1;
    }

    try
    {
        var config = AppConfig.FromEnvironment();

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        var tfsClient = new TfsWorkItemClient(httpClient, config);

        Console.WriteLine($"Fetching User Story {workItemId}...");
        var workItem = await tfsClient.GetWorkItemAsync(workItemId);

        var chatClient = new OllamaChatClient(config.OllamaUrl, config.OllamaModel);
        AIAgent agent = chatClient.AsAIAgent(
            instructions: "You are a senior QA engineer. Return practical Markdown only.");

        var prompt = ChecklistPromptBuilder.Build(workItem);

        Console.WriteLine("Generating QA checklist...");
        var response = await agent.RunAsync(prompt);
        var markdown = ChecklistPromptBuilder.NormalizeMarkdown(workItem, response.ToString());

        Directory.CreateDirectory("output");
        var outputPath = Path.Combine("output", $"us-{workItem.Id}-qa-checklist.md");
        await File.WriteAllTextAsync(outputPath, markdown);

        Console.WriteLine();
        Console.WriteLine(markdown);
        Console.WriteLine($"Saved to: {Path.GetFullPath(outputPath)}");

        return 0;
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

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project .\\AiAgentForTFS\\AiAgentForTFS.csproj -- generate-qa-checklist <workItemId>");
    Console.WriteLine();
    Console.WriteLine("Environment variables:");
    Console.WriteLine("  AZURE_DEVOPS_BASE_URL  Example: https://dev.azure.com/org/project");
    Console.WriteLine("  AZURE_DEVOPS_PAT       Personal Access Token with Work Items read access");
    Console.WriteLine("  AZURE_DEVOPS_API_VERSION optional, default: 7.1");
    Console.WriteLine("  OLLAMA_URL             optional, default: http://localhost:11434");
    Console.WriteLine("  OLLAMA_MODEL           optional, default: qwen3:8b");
}
