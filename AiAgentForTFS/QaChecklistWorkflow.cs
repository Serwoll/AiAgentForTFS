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
        return "# Extracted Conditions\n\n" + condition;
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

}