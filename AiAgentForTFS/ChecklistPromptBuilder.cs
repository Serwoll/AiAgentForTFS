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
        prompt.AppendLine("Do not invent missing business rules. Put unclear points into Open Questions.");
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
                prompt.AppendLine($"### {field.Name}");
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
                prompt.AppendLine($"- Relation: {link.Relation}");
                prompt.AppendLine($"  Name: {link.Name ?? "Unknown"}");
                prompt.AppendLine($"  Url: {link.Url}");
                prompt.AppendLine($"  Comment: {link.Comment ?? "None"}");
            }
        }

        return prompt.ToString();
    }

    public static string NormalizeMarkdown(WorkItemContext workItem, string markdown)
    {
        var result = markdown.Trim();

        if (!result.StartsWith("#", StringComparison.Ordinal))
        {
            result = $"# QA Checklist for US {workItem.Id}: {workItem.Title}{Environment.NewLine}{Environment.NewLine}{result}";
        }

        return result + Environment.NewLine;
    }

    private static string EmptyIfMissing(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Not provided." : value;
    }
}
