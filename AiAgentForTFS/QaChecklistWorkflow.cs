using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AiAgentForTFS;

public sealed class QaChecklistWorkflow
{
    private const int MaxChangedFilesInToolResult = 15;
    private readonly TfsWorkItemClient _workItemClient;
    private readonly OllamaChatClient _ollamaChatClient;

    public QaChecklistWorkflow(TfsWorkItemClient workItemClient, OllamaChatClient ollamaChatClient)
    {
        _workItemClient = workItemClient;
        _ollamaChatClient = ollamaChatClient;
    }

    public async Task<string> GenerateForUserStoryAsync(int workItemId)
    {
        var workItem = await _workItemClient.GetWorkItemAsync(workItemId);
        var conditions = await ExtractConditionsAsync(workItem);
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
        var codeContext = BuildCodeContextSummary(workItem);
        finalMarkdown = await AddCodeContextRegressionAsync(finalMarkdown, codeContext);
        return finalMarkdown;
    }

    private async Task<string> ExtractConditionsAsync(WorkItemContext workItem)
    {
        AIAgent agent = _ollamaChatClient.AsAIAgent(instructions: "You extract explicit requirements from User Stories. Return only facts from the provided text.");
        var response = await agent.RunAsync($"""
        Извлеки только явные условия из User Story.
        Условие должно быть явно написано в Title, Description или Acceptance Criteria.
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
        """);
        return response.ToString();
    }

    private async Task<string> GenerateChecklistAsync(WorkItemContext workItem, string conditions)
    {
        AIAgent agent = _ollamaChatClient.AsAIAgent(instructions: """
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

    private async Task<string> AddCodeContextRegressionAsync(string checklist, string codeContextSummary)
    {
        if (codeContextSummary == "No code context found")
            return checklist;

        AIAgent agent = _ollamaChatClient.AsAIAgent(instructions: """
        You carefully update QA checklists. Preserve existing content unless explicitly asked to add regression checks.
        If you add nothing, return only "NONE".
        """);

        var response = await agent.RunAsync($"""
        У тебя есть уже готовый QA checklist и code context.
        Задача:
        - НЕ переписывай чек-лист с нуля.
        - НЕ меняй секции User Story, Context и Checklist.
        - НЕ удаляй существующие пункты.
        - Верни только дополнительные regression checklist items.
        - Не возвращай полный чек-лист.
        - Каждый пункт должен начинаться с "[code-context]".
        - Если code context не дает понятных manual QA проверок, верни только "NONE".

        Existing checklist:
        {checklist}

        Code context:
        {codeContextSummary}
        """);

        var items = response.ToString().Trim();
        if (items == "NONE")
            return checklist;

        return checklist + "\n\n## Code Context Regression\n" + items;
    }
}