using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgentForTFS;

public sealed class AzureDevOpsGitClient(HttpClient httpClient, AppConfig config)
{
    private const int MaxCommits = 20;
    private const int MaxChangedFiles = 100;

    private static readonly Regex PullRequestArtifactRegex = new(
        @"vstfs:///Git/PullRequestId/(?<project>[^%/]+)%2[fF](?<repo>[^%/]+)%2[fF](?<pr>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<WorkItemContext> EnrichAsync(WorkItemContext workItem, CancellationToken cancellationToken = default)
    {
        var warnings = workItem.CodeContextWarnings.ToList();
        var sources = workItem.CodeContextSources.ToList();
        var pullRequests = FindPullRequestReferences(workItem).ToList();

        foreach (var pullRequestRef in pullRequests)
        {
            try
            {
                sources.Add(await GetPullRequestContextAsync(pullRequestRef, cancellationToken));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or AgentException)
            {
                warnings.Add($"Could not enrich PR {pullRequestRef.PullRequestId} in repository {pullRequestRef.RepositoryId}: {ex.Message}");
            }
        }

        return workItem with
        {
            CodeContextSources = sources,
            CodeContextWarnings = warnings
        };
    }

    private IEnumerable<PullRequestRef> FindPullRequestReferences(WorkItemContext workItem)
    {
        var values = workItem.Links
            .SelectMany(link => new[] { link.Url, link.Name, link.Comment })
            .Concat(workItem.TextFields.Select(field => field.Value));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (Match match in PullRequestArtifactRegex.Matches(value))
            {
                var repositoryId = Uri.UnescapeDataString(match.Groups["repo"].Value);
                var pullRequestId = int.Parse(match.Groups["pr"].Value);
                var key = $"{repositoryId}:{pullRequestId}";

                if (seen.Add(key))
                {
                    yield return new PullRequestRef(repositoryId, pullRequestId);
                }
            }
        }
    }

    private async Task<CodeContextSource> GetPullRequestContextAsync(PullRequestRef pullRequestRef, CancellationToken cancellationToken)
    {
        var pullRequest = await GetJsonAsync<PullRequestDto>(
            $"{config.AzureDevOpsBaseUrl}/_apis/git/repositories/{pullRequestRef.RepositoryId}/pullRequests/{pullRequestRef.PullRequestId}?includeCommits=true&api-version={Uri.EscapeDataString(config.AzureDevOpsApiVersion)}",
            cancellationToken);

        var commits = await GetListAsync<CommitDto>(
            $"{config.AzureDevOpsBaseUrl}/_apis/git/repositories/{pullRequestRef.RepositoryId}/pullRequests/{pullRequestRef.PullRequestId}/commits?$top={MaxCommits + 1}&api-version={Uri.EscapeDataString(config.AzureDevOpsApiVersion)}",
            cancellationToken);

        var commitLimitReached = commits.Count > MaxCommits;
        var limitedCommits = commits.Take(MaxCommits).ToList();
        var changedFiles = new List<string>();
        var changedFileLimitReached = false;

        foreach (var commit in limitedCommits)
        {
            if (string.IsNullOrWhiteSpace(commit.CommitId))
            {
                continue;
            }

            var changes = await GetCommitChangesAsync(pullRequestRef.RepositoryId, commit.CommitId, cancellationToken);

            foreach (var changedFile in changes)
            {
                if (changedFiles.Contains(changedFile, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                changedFiles.Add(changedFile);

                if (changedFiles.Count >= MaxChangedFiles)
                {
                    changedFileLimitReached = true;
                    break;
                }
            }

            if (changedFileLimitReached)
            {
                break;
            }
        }

        return new CodeContextSource(
            "Pull Request",
            pullRequestRef.RepositoryId,
            pullRequestRef.PullRequestId,
            pullRequest.Title,
            NormalizeBranch(pullRequest.SourceRefName),
            NormalizeBranch(pullRequest.TargetRefName),
            limitedCommits.Select(commit => commit.Comment).Where(comment => !string.IsNullOrWhiteSpace(comment)).Cast<string>().ToList(),
            changedFiles,
            commitLimitReached,
            changedFileLimitReached);
    }

    private async Task<IReadOnlyList<string>> GetCommitChangesAsync(string repositoryId, string commitId, CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<CommitChangesDto>(
            $"{config.AzureDevOpsBaseUrl}/_apis/git/repositories/{repositoryId}/commits/{commitId}/changes?top={MaxChangedFiles}&api-version={Uri.EscapeDataString(config.AzureDevOpsApiVersion)}",
            cancellationToken);

        return (response.Changes ?? [])
            .Select(change => change.Item?.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();
    }

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = CreatePatHeader(config.AzureDevOpsPat);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AgentException($"Azure DevOps Git returned {(int)response.StatusCode} {response.ReasonPhrase}: {TrimError(body)}");
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions.Default)
            ?? throw new AgentException("Azure DevOps Git returned an invalid JSON response.");
    }

    private async Task<List<T>> GetListAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = CreatePatHeader(config.AzureDevOpsPat);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AgentException($"Azure DevOps Git returned {(int)response.StatusCode} {response.ReasonPhrase}: {TrimError(body)}");
        }

        using var document = JsonDocument.Parse(body);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return document.RootElement.Deserialize<List<T>>(JsonOptions.Default) ?? [];
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return value.Deserialize<List<T>>(JsonOptions.Default) ?? [];
        }

        return [];
    }

    private static AuthenticationHeaderValue CreatePatHeader(string pat)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static string? NormalizeBranch(string? refName)
    {
        return refName?.Replace("refs/heads/", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty response body";
        }

        return body.Length <= 500 ? body : body[..500] + "...";
    }

    private sealed record PullRequestRef(string RepositoryId, int PullRequestId);

    private sealed record PullRequestDto(
        string? Title,
        string? SourceRefName,
        string? TargetRefName);

    private sealed record CommitDto(
        string? CommitId,
        string? Comment);

    private sealed record CommitChangesDto(
        List<GitChangeDto>? Changes);

    private sealed record GitChangeDto(
        GitItemDto? Item);

    private sealed record GitItemDto(
        string? Path);
}
