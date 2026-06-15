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
        return $"Workflow loaded User Story: {workItem.Id} - {workItem.Title}";
    }

}