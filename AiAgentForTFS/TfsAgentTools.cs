using System.ComponentModel;
using System.Text;

namespace AiAgentForTFS;

public sealed class TfsAgentTools
{
    private readonly TfsWorkItemClient _tfsClient;

    public TfsAgentTools(TfsWorkItemClient tfsClient)
    {
        _tfsClient = tfsClient;
    }

    [Description("Gets Azure DevOps/TFS User Story by work item id and returns title, description, acceptance criteria, links, pull request context, commit messages, and changed files summary.")]
    public async Task<string> GetUserStoryById([Description("Azure DevOps/TFS work item id of the User Story.")] int workItemId)
    {
        var workItem = await _tfsClient.GetWorkItemAsync(workItemId);
        var links = string.Join(Environment.NewLine, workItem.Links.Select(link => $"- {link.Name}: {link.Url}"));
        var codeContext = Build(workItem);

        return
        $"""
        Id: {workItem.Id}
        Title: {workItem.Title}
        Type: {workItem.Type}
        State: {workItem.State}
        Assigned To: {workItem.AssignedTo}
        Tags: {workItem.Tags}
        Description: {workItem.Description}

        Links: 
        {links}
        
        Pull Request Context: 
        {codeContext}
        """;
    }

    private static string Build(WorkItemContext workItem)
    {
        var build = new StringBuilder();
        if (workItem.CodeContextSources.Count == 0)
            return "No code context found";

        foreach (var source in workItem.CodeContextSources)
        {
            build.AppendLine($"PR id: {source.PullRequestId}");
            build.AppendLine($"Title: {source.Title}");
            build.AppendLine($"Branches: {source.SourceBranch} -> {source.TargetBranch}");
            if (source.CommitMessages.Count == 0)
            {
                build.AppendLine("No commits loaded");
            }
            else
            {
                foreach (var message in source.CommitMessages)
                {
                    build.AppendLine($"Message: {message}");
                }
            }
            build.AppendLine($"Changed files count: {source.ChangedFiles.Count}");
            build.AppendLine($"Changed files:");
            foreach (var changedFile in source.ChangedFiles)
            {
                build.AppendLine($"- {changedFile}");
            }
        }
        return build.ToString();
    }
}