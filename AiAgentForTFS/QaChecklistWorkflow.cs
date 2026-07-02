using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AiAgentForTFS;

public sealed class QaChecklistWorkflow
{
    private const int MaxChangedFilesInToolResult = 15;
    private readonly TfsWorkItemClient _workItemClient;
    private readonly IChatClient _chatClient;
    private readonly ConfluenceClient _confluenceClient;

    public QaChecklistWorkflow(TfsWorkItemClient workItemClient, IChatClient chatClient, ConfluenceClient confluenceClient)
    {
        _workItemClient = workItemClient;
        _chatClient = chatClient;
        _confluenceClient = confluenceClient;
    }

    public async Task<QaChecklistWorkflowResult> GenerateForUserStoryAsync(int workItemId)
    {
        Console.WriteLine("Загружаю User Story и связанный PR-контекст...");
        var workItem = await _workItemClient.GetWorkItemAsync(workItemId);

        return await GenerateForWorkItemContextAsync(workItem, Path.Combine("output", $"us-{workItemId}-qa-checklist.md"));
    }

    public async Task<QaChecklistWorkflowResult> GenerateForConfluenceUrlAsync(string url)
    {
        Console.WriteLine("Загружаю страницу Confluence...");
        var confluencePage = await _confluenceClient.GetPageByUrlAsync(url);
        var workItem = new WorkItemContext(
            0,
            confluencePage.Title ?? "Confluence Page",
            "Confluence Page",
            "Manual",
            null,
            confluencePage.Content,
            null,
            "Confluence",
            [],
            [],
            [confluencePage],
            [],
            [],
            []);

        return await GenerateForWorkItemContextAsync(workItem, Path.Combine("output", $"confluence-{confluencePage.PageId}-qa-checklist.md"));
    }

    private async Task<QaChecklistWorkflowResult> GenerateForWorkItemContextAsync(WorkItemContext workItem, string outputPath)
    {
        Console.WriteLine("Извлекаю явные условия из описания...");
        var conditions = await ExtractConditionsAsync(workItem);

        Console.WriteLine("Генерирую базовый QA чек-лист...");
        var body = await GenerateChecklistAsync(workItem, conditions);
        var finalMarkdown =
        $"""
        # QA Checklist

        ## User Story
        - ID: {workItem.Id}
        - Title: {workItem.Title}
        - State: {workItem.State}
        - Tags: {workItem.Tags}

        {body}
        """;

        Console.WriteLine("Готовлю регрессионные проверки по code context...");
        var codeContext = BuildCodeContextSummary(workItem);
        finalMarkdown = await AddCodeContextRegressionAsync(finalMarkdown, codeContext);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        await File.WriteAllTextAsync(outputPath, finalMarkdown);

        return new QaChecklistWorkflowResult(finalMarkdown, Path.GetFullPath(outputPath));
    }

    private async Task<string> ExtractConditionsAsync(WorkItemContext workItem)
    {
        AIAgent agent = _chatClient.AsAIAgent(instructions: "You extract explicit requirements from User Stories. Return only facts from the provided text.");
        var confluenceContext = BuildConfluenceContextSummary(workItem);
        var response = await agent.RunAsync($"""
        Извлеки только явные условия из User Story.
        Условие должно быть явно написано в Title, Description, Acceptance Criteria или Confluence Context.
        Формат каждого пункта: "- триггер/настройка/состояние -> ожидаемое поведение".
        Не добавляй предположения.
        Не используй code context.
        Верни только Markdown-список.

        Title:
        {workItem.Title}

        Description:
        {workItem.Description}

        Acceptance Criteria:
        {workItem.AcceptanceCriteria ?? "Not provided."}

        Confluence Context:
        {confluenceContext}
        """);
        return response.ToString();
    }

    private async Task<string> GenerateChecklistAsync(WorkItemContext workItem, string conditions)
    {
        AIAgent agent = _chatClient.AsAIAgent(instructions: """
        - Every extracted condition must be covered by at least one checklist item.
        - Do not move extracted conditions to Open Questions unless the condition itself is ambiguous.
        - Do not create automatic state transition checks unless the User Story explicitly says the application must switch automatically.
        - If there are no open questions, write "- Нет открытых вопросов."
        """);
        var response = await agent.RunAsync($"""
        Сгенерируй QA чек-лист на основе User Story и Extracted Conditions.
        Главный источник требований: Extracted Conditions.
        Каждое extracted condition должно быть покрыто минимум одним checklist item.
        Если extracted condition говорит о сборке/пайплайне, добавь QA-проверку доступности результата в установленной сборке, а не вопрос.
        Не добавляй проверки на основе предположений.
        Верни только Markdown.

        User Story:
        ID: {workItem.Id}
        Title: {workItem.Title}
        State: {workItem.State}
        Tags: {workItem.Tags}

        Description:
        {workItem.Description}

        Extracted Conditions:
        {conditions}

        Структура чек-листа

        ## Checklist
        - [ ] ...

        ## Regression
        - [ ] ...

        ## Open Questions
        - ...
        """);
        return response.ToString();
    }

    private static string BuildCodeContextSummary(WorkItemContext workItem)
    {
        var build = new StringBuilder();
        if (workItem.CodeContextSources.Count == 0)
            return "No code context found";

        foreach (var source in workItem.CodeContextSources)
        {
            build.AppendLine("<pr>");
            build.AppendLine($"<id> {source.PullRequestId}</id>");
            build.AppendLine($"<title> {source.Title}</title>");
            build.AppendLine($"<branches> {source.SourceBranch} -> {source.TargetBranch}</branches>");
            if (source.CommitMessages.Count == 0)
            {
                build.AppendLine("<commit_messages usage=\"context_only\">");
                build.AppendLine("<none>No commits loaded</none>");
                build.AppendLine("</commit_messages>");
            }
            else
            {
                build.AppendLine("<commit_messages usage=\"context_only\">");
                foreach (var message in source.CommitMessages)
                {
                    build.AppendLine($"<message>{message}</message>");
                }
                build.AppendLine("</commit_messages>");
            }
            build.AppendLine("<code_context_summary usage=\"context_only\">");
            build.AppendLine($"<changed_files_count>{source.ChangedFiles.Count}</changed_files_count>");
            build.AppendLine("<note>Code context is manual QA coverage evidence only. Convert it into user-visible behavior checks. Do not test files, classes, modules, or services directly.</note>");

            foreach (var changedFile in source.ChangedFiles.Take(MaxChangedFilesInToolResult))
            {
                build.AppendLine($"<file>{changedFile}</file>");
            }

            if (source.ChangedFiles.Count > MaxChangedFilesInToolResult || source.ChangedFileLimitReached)
            {
                build.AppendLine("<truncated>true</truncated>");
            }
            build.AppendLine("</code_context_summary>");
            build.AppendLine("</pr>");
        }
        return build.ToString();
    }

    private static string BuildConfluenceContextSummary(WorkItemContext workItem)
    {
        var build = new StringBuilder();

        if (workItem.ConfluenceContextSources.Count == 0)
        {
            build.AppendLine("No Confluence pages loaded.");
        }

        foreach (var source in workItem.ConfluenceContextSources)
        {
            build.AppendLine("<confluence_page>");
            build.AppendLine($"<id>{source.PageId}</id>");
            build.AppendLine($"<title>{source.Title}</title>");
            build.AppendLine($"<space>{source.SpaceKey}</space>");
            build.AppendLine("<content>");
            build.AppendLine(source.Content);
            build.AppendLine("</content>");

            if (source.ContentTruncated)
            {
                build.AppendLine("<truncated>true</truncated>");
            }

            build.AppendLine("</confluence_page>");
        }

        if (workItem.ConfluenceContextWarnings.Count > 0)
        {
            build.AppendLine("<confluence_warnings>");
            foreach (var warning in workItem.ConfluenceContextWarnings)
            {
                build.AppendLine($"<warning>{warning}</warning>");
            }
            build.AppendLine("</confluence_warnings>");
        }

        return build.ToString();
    }

    private async Task<string> AddCodeContextRegressionAsync(string checklist, string codeContextSummary)
    {
        if (codeContextSummary == "No code context found")
            return checklist;

        AIAgent agent = _chatClient.AsAIAgent(instructions: """
        You generate only additional manual QA regression checklist items from code context.
        Do not rewrite or summarize the existing checklist.
        Do not return file paths, class names, method names, service names, PR names, or commit names as checklist items.
        Convert code context into user-visible or QA-verifiable product behavior.
        If there are no useful regression items, return only "NONE".
        Each item must start with "[code-context]".
        Never output raw code context entries.
        """);

        var response = await agent.RunAsync($"""
        Existing checklist:
        {checklist}

        Code context:
        {codeContextSummary}

        Return only additional regression checklist items.
        Do not return the full checklist.
        """);

        var items = response.ToString().Trim();
        if (items == "NONE")
            return checklist;

        return checklist + "\n\n## Code Context Regression\n" + items;
    }

}
