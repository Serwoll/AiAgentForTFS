using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgentForTFS;

public sealed class TfsWorkItemClient(HttpClient httpClient, AppConfig config)
{
    public async Task<WorkItemContext> GetWorkItemAsync(int workItemId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildWorkItemUrl(workItemId));
        request.Headers.Authorization = CreatePatHeader(config.AzureDevOpsPat);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AgentException($"Azure DevOps/TFS returned {(int)response.StatusCode} {response.ReasonPhrase}: {TrimError(body)}");
        }

        var workItem = JsonSerializer.Deserialize<TfsWorkItemDto>(body, JsonOptions.Default)
            ?? throw new AgentException("Azure DevOps/TFS returned an invalid work item response.");

        var fields = workItem.Fields ?? [];

        return new WorkItemContext(
            workItem.Id,
            GetField(fields, "System.Title") ?? $"Work Item {workItem.Id}",
            GetField(fields, "System.WorkItemType") ?? "Unknown",
            GetField(fields, "System.State") ?? "Unknown",
            GetField(fields, "System.AssignedTo"),
            CleanHtml(GetField(fields, "System.Description")),
            CleanHtml(GetField(fields, "Microsoft.VSTS.Common.AcceptanceCriteria")),
            GetField(fields, "System.Tags"),
            (workItem.Relations ?? []).Select(ToWorkItemLink).ToList());
    }

    private string BuildWorkItemUrl(int workItemId)
    {
        return $"{config.AzureDevOpsBaseUrl}/_apis/wit/workitems/{workItemId}?$expand=relations&api-version={Uri.EscapeDataString(config.AzureDevOpsApiVersion)}";
    }

    private static AuthenticationHeaderValue CreatePatHeader(string pat)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static WorkItemLink ToWorkItemLink(TfsRelationDto relation)
    {
        var attributes = relation.Attributes ?? [];

        return new WorkItemLink(
            relation.Rel ?? "Unknown",
            relation.Url ?? "",
            GetField(attributes, "name"),
            GetField(attributes, "comment"));
    }

    private static string? GetField(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object when value.TryGetProperty("displayName", out var displayName) => displayName.GetString(),
            JsonValueKind.Object when value.TryGetProperty("uniqueName", out var uniqueName) => uniqueName.GetString(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }

    private static string? CleanHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var withLineBreaks = Regex.Replace(value, "<\\s*br\\s*/?>|</\\s*p\\s*>|</\\s*div\\s*>", "\n", RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withLineBreaks, "<.*?>", string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return Regex.Replace(decoded, "\\n{3,}", "\n\n").Trim();
    }

    private static string TrimError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty response body";
        }

        return body.Length <= 500 ? body : body[..500] + "...";
    }

    private sealed record TfsWorkItemDto(
        int Id,
        Dictionary<string, JsonElement>? Fields,
        List<TfsRelationDto>? Relations);

    private sealed record TfsRelationDto(
        string? Rel,
        string? Url,
        Dictionary<string, JsonElement>? Attributes);
}
