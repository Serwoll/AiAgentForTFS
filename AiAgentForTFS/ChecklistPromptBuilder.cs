using System.Text;

namespace AiAgentForTFS;

public static class ChecklistPromptBuilder
{
    public static string Build(WorkItemContext workItem)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("Generate a QA checklist for this Azure DevOps/TFS User Story.");
        prompt.AppendLine("Use Russian language.");
        prompt.AppendLine("Return Markdown only.");
        prompt.AppendLine("Use checkboxes for checklist items.");
        prompt.AppendLine("Use checkboxes only inside QA Checklist, Regression Areas, and Open Questions. Do not use checkboxes in Source User Story or Context Summary.");
        prompt.AppendLine("Do not invent missing business rules. Put unclear points into Open Questions.");
        prompt.AppendLine("Group QA checklist items by stages or functional areas when the User Story contains stages, modules, settings, or components.");
        prompt.AppendLine("When a checklist item is based on actual PR metadata, commit messages, or changed file paths, add the marker [code-context].");
        prompt.AppendLine("When a checklist item is based only on User Story text or acceptance criteria, do not add that marker.");
        prompt.AppendLine();
        prompt.AppendLine("Required structure:");
        prompt.AppendLine("# QA Checklist");
        prompt.AppendLine("## Source User Story");
        prompt.AppendLine("## Context Summary");
        prompt.AppendLine("## QA Checklist");
        prompt.AppendLine("## Regression Areas");
        prompt.AppendLine("## Open Questions");
        prompt.AppendLine();
        prompt.AppendLine("User Story context:");
        prompt.AppendLine($"Id: {workItem.Id}");
        prompt.AppendLine($"Type: {workItem.Type}");
        prompt.AppendLine($"Title: {workItem.Title}");
        prompt.AppendLine($"State: {workItem.State}");
        prompt.AppendLine($"Assigned To: {workItem.AssignedTo ?? "Not assigned"}");
        prompt.AppendLine($"Tags: {workItem.Tags ?? "None"}");
        prompt.AppendLine();
        prompt.AppendLine("Description:");
        prompt.AppendLine(EmptyIfMissing(workItem.Description));
        prompt.AppendLine();
        prompt.AppendLine("Acceptance Criteria:");
        prompt.AppendLine(EmptyIfMissing(workItem.AcceptanceCriteria));
        prompt.AppendLine();
        prompt.AppendLine("Additional text fields from the User Story, including possible code or implementation context:");

        if (workItem.TextFields.Count == 0)
        {
            prompt.AppendLine("No extra text fields were found.");
        }
        else
        {
            foreach (var field in workItem.TextFields)
            {
                var marker = field.LooksLikeCodeContext ? " [code-context-source]" : "";
                prompt.AppendLine($"### {field.Name}{marker}");
                prompt.AppendLine(field.Value);
                prompt.AppendLine();
            }
        }

        prompt.AppendLine("Related links, code links, pull requests, commits, changesets, attachments:");

        if (workItem.Links.Count == 0)
        {
            prompt.AppendLine("No related links were returned.");
        }
        else
        {
            foreach (var link in workItem.Links)
            {
                var marker = link.LooksLikeCodeContext ? " [code-context-source]" : "";
                prompt.AppendLine($"- Relation: {link.Relation}{marker}");
                prompt.AppendLine($"  Name: {link.Name ?? "Unknown"}");
                prompt.AppendLine($"  Url: {link.Url}");
                prompt.AppendLine($"  Comment: {link.Comment ?? "None"}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Pull Request Context:");

        if (workItem.CodeContextSources.Count == 0)
        {
            prompt.AppendLine("No pull request metadata, commits, or changed files were loaded.");
        }
        else
        {
            foreach (var source in workItem.CodeContextSources)
            {
                prompt.AppendLine($"## {source.SourceType} {source.PullRequestId}");
                prompt.AppendLine($"Repository: {source.RepositoryId}");
                prompt.AppendLine($"Title: {source.Title ?? "Unknown"}");
                prompt.AppendLine($"Source Branch: {source.SourceBranch ?? "Unknown"}");
                prompt.AppendLine($"Target Branch: {source.TargetBranch ?? "Unknown"}");
                prompt.AppendLine();
                prompt.AppendLine("Commit messages:");

                if (source.CommitMessages.Count == 0)
                {
                    prompt.AppendLine("- None loaded.");
                }
                else
                {
                    foreach (var commitMessage in source.CommitMessages)
                    {
                        prompt.AppendLine($"- {commitMessage}");
                    }
                }

                prompt.AppendLine();
                prompt.AppendLine("Changed files:");

                if (source.ChangedFiles.Count == 0)
                {
                    prompt.AppendLine("- None loaded.");
                }
                else
                {
                    foreach (var changedFile in source.ChangedFiles)
                    {
                        prompt.AppendLine($"- {changedFile}");
                    }
                }

                if (source.CommitLimitReached)
                {
                    prompt.AppendLine("- Warning: commit list was truncated.");
                }

                if (source.ChangedFileLimitReached)
                {
                    prompt.AppendLine("- Warning: changed file list was truncated.");
                }

                prompt.AppendLine();
            }
        }

        prompt.AppendLine("Code Context Warnings:");

        if (workItem.CodeContextWarnings.Count == 0)
        {
            prompt.AppendLine("None.");
        }
        else
        {
            foreach (var warning in workItem.CodeContextWarnings)
            {
                prompt.AppendLine($"- {warning}");
            }
        }

        return prompt.ToString();
    }

    public static string NormalizeMarkdown(WorkItemContext workItem, string markdown, TimeSpan responseTime)
    {
        var result = markdown.Trim();

        if (!result.StartsWith("#", StringComparison.Ordinal))
        {
            result = $"# QA Checklist for US {workItem.Id}: {workItem.Title}{Environment.NewLine}{Environment.NewLine}{result}";
        }

        return result + Environment.NewLine + BuildContextAudit(workItem, responseTime);
    }

    private static string BuildContextAudit(WorkItemContext workItem, TimeSpan responseTime)
    {
        var audit = new StringBuilder();
        var codeFields = workItem.TextFields.Where(field => field.LooksLikeCodeContext).ToList();
        var otherFields = workItem.TextFields.Where(field => !field.LooksLikeCodeContext).ToList();
        var codeLinks = workItem.Links.Where(link => link.LooksLikeCodeContext).ToList();
        var otherLinks = workItem.Links.Where(link => !link.LooksLikeCodeContext).ToList();

        audit.AppendLine();
        audit.AppendLine("---");
        audit.AppendLine();
        audit.AppendLine("## Context used by agent");
        audit.AppendLine();
        audit.AppendLine($"- LLM response time: {FormatElapsed(responseTime)}");
        audit.AppendLine();
        audit.AppendLine("### Code/implementation context sources");

        if (codeFields.Count == 0 && codeLinks.Count == 0 && workItem.CodeContextSources.Count == 0)
        {
            audit.AppendLine("- No explicit code/implementation context sources were detected in the User Story fields or relations.");
        }
        else
        {
            foreach (var field in codeFields)
            {
                audit.AppendLine($"- Field: `{field.Name}`");
            }

            foreach (var link in codeLinks)
            {
                audit.AppendLine($"- Link: `{link.Relation}` - {link.Name ?? link.Url}");
            }

            foreach (var source in workItem.CodeContextSources)
            {
                audit.AppendLine($"- {source.SourceType}: PR `{source.PullRequestId}` - {source.Title ?? "Untitled"}");
                audit.AppendLine($"  - Repository: `{source.RepositoryId}`");
                audit.AppendLine($"  - Branches: `{source.SourceBranch ?? "unknown"}` -> `{source.TargetBranch ?? "unknown"}`");
                audit.AppendLine($"  - Commit messages loaded: {source.CommitMessages.Count}");
                audit.AppendLine($"  - Changed files loaded: {source.ChangedFiles.Count}");

                if (source.CommitLimitReached)
                {
                    audit.AppendLine("  - Commit list was truncated.");
                }

                if (source.ChangedFileLimitReached)
                {
                    audit.AppendLine("  - Changed file list was truncated.");
                }
            }
        }

        if (workItem.CodeContextWarnings.Count > 0)
        {
            audit.AppendLine();
            audit.AppendLine("### Code context warnings");

            foreach (var warning in workItem.CodeContextWarnings)
            {
                audit.AppendLine($"- {warning}");
            }
        }

        audit.AppendLine();
        audit.AppendLine("### Other context sources");
        audit.AppendLine($"- Description: {(string.IsNullOrWhiteSpace(workItem.Description) ? "missing" : "used")}");
        audit.AppendLine($"- Acceptance Criteria: {(string.IsNullOrWhiteSpace(workItem.AcceptanceCriteria) ? "missing" : "used")}");

        foreach (var field in otherFields)
        {
            audit.AppendLine($"- Field: `{field.Name}`");
        }

        foreach (var link in otherLinks)
        {
            audit.AppendLine($"- Link: `{link.Relation}` - {link.Name ?? link.Url}");
        }

        return audit.ToString();
    }

    private static string EmptyIfMissing(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Not provided." : value;
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalSeconds < 60
            ? $"{elapsed.TotalSeconds:F1}s"
            : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
    }
}
