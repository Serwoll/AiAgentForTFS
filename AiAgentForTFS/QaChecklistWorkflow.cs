using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AiAgentForTFS;

public sealed class QaChecklistWorkflow
{
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
        var condition = await ExtractConditionsAsync(workItem);
        var checklist = await GenerateChecklistAsync(workItem, condition);
        Console.WriteLine(checklist);
        return checklist;
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

    private async Task<string> GenerateChecklistAsync(WorkItemContext workItem, string condition)
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
        {condition}

        Структура чек-листа

        # QA Checklist

        ## User Story
        - ID:
        - Title:
        - State:
        - Tags:

        ## Checklist
        - [ ] ...

        ## Regression
        - [ ] ...

        ## Open Questions
        - ...
        """);
        return response.ToString();
    }
}