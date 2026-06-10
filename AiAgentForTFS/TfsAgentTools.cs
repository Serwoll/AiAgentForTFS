using System.ComponentModel;
using System.Text;

namespace AiAgentForTFS;

public sealed class TfsAgentTools
{
    private const int MaxChangedFilesInToolResult = 30;

    private readonly TfsWorkItemClient _tfsClient;

    public TfsAgentTools(TfsWorkItemClient tfsClient)
    {
        _tfsClient = tfsClient;
    }

    [Description("Gets Azure DevOps/TFS User Story by work item id and returns title, description, acceptance criteria, links, pull request context, commit messages, and changed files summary.")]
    public async Task<string> GetUserStoryById([Description("Azure DevOps/TFS work item id of the User Story.")] int workItemId)
    {
        Console.WriteLine($"TOOL CALLED: GetUserStoryById({workItemId})");
        var workItem = await _tfsClient.GetWorkItemAsync(workItemId);
        var links = string.Join(Environment.NewLine, workItem.Links.Select(link => $"<link>{link.Name}-{link.Url}</link>"));
        var codeContext = Build(workItem);

        return
        $"""
        <tool_result purpose="qa_checklist_grounding_context">
            <user_story>
                <id>{workItem.Id}</id>
                <title>{workItem.Title}</title>
                <state>{workItem.State}</state>
                <tags>{workItem.Tags}</tags>
                <description>
                    {workItem.Description}
                </description>
                <links>
                    {links}
                </links>
            </user_story>

            <pull_request_context usage="context_only">
                {codeContext}
            </pull_request_context>
        </tool_result>
        """;
    }

    private static string Build(WorkItemContext workItem)
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
            build.AppendLine("<code_context_summary  usage=\"context_only\">");
            build.AppendLine($"<changed_files_count>{source.ChangedFiles.Count}</changed_files_count>");
            build.AppendLine("<note>Use file paths only to enrich QA checklist items. Do not summarize files. Do not write implementation steps.</note>");

            foreach (var changedFile in source.ChangedFiles.Take(MaxChangedFilesInToolResult))
            {
                build.AppendLine($"<file>{changedFile}</file>");
            }

            if (source.ChangedFiles.Count > MaxChangedFilesInToolResult || source.ChangedFileLimitReached)
            {
                build.AppendLine("<truncated>true</truncated>");
            }
            build.AppendLine("</code_context_summary >");
            build.AppendLine("</pr>");
        }
        return build.ToString();
    }
}
