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
        var changedFiles = await GetPullRequestChangedFilesAsync(pullRequestRef, cancellationToken);
        var changedFileLimitReached = changedFiles.Count > MaxChangedFiles;
        var limitedChangedFiles = changedFiles.Take(MaxChangedFiles).ToList();

        return new CodeContextSource(
            "Pull Request",
            pullRequestRef.RepositoryId,
            pullRequestRef.PullRequestId,
            pullRequest.Title,
            NormalizeBranch(pullRequest.SourceRefName),
            NormalizeBranch(pullRequest.TargetRefName),
            limitedCommits.Select(commit => commit.Comment).Where(comment => !string.IsNullOrWhiteSpace(comment)).Cast<string>().ToList(),
            limitedChangedFiles,
            commitLimitReached,
            changedFileLimitReached);
    }

    private async Task<IReadOnlyList<string>> GetPullRequestChangedFilesAsync(PullRequestRef pullRequestRef, CancellationToken cancellationToken)
    {
        var iterations = await GetListAsync<PullRequestIterationDto>(
            $"{config.AzureDevOpsBaseUrl}/_apis/git/repositories/{pullRequestRef.RepositoryId}/pullRequests/{pullRequestRef.PullRequestId}/iterations?api-version={Uri.EscapeDataString(config.AzureDevOpsApiVersion)}",
            cancellationToken);

        if (iterations.Count == 0)
        {
            return [];
        }

        var latestIterationId = iterations.Max(iteration => iteration.Id);
        var response = await GetJsonAsync<PullRequestIterationChangesDto>(
            $"{config.AzureDevOpsBaseUrl}/_apis/git/repositories/{pullRequestRef.RepositoryId}/pullRequests/{pullRequestRef.PullRequestId}/iterations/{latestIterationId}/changes?$compareTo=0&api-version={Uri.EscapeDataString(config.AzureDevOpsApiVersion)}",
            cancellationToken);

        var changedFiles = new List<string>();

        foreach (var change in response.ChangeEntries ?? [])
        {
            var path = change.Item?.Path;

            if (string.IsNullOrWhiteSpace(path))
            {
                path = change.OriginalPath;
            }

            if (string.IsNullOrWhiteSpace(path)
                || changedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            changedFiles.Add(path);
        }

        return changedFiles;
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

    private sealed record PullRequestIterationDto(
        int Id);

    private sealed record PullRequestIterationChangesDto(
        List<PullRequestIterationChangeDto>? ChangeEntries);

    private sealed record PullRequestIterationChangeDto(
        GitItemDto? Item,
        string? OriginalPath);


    private sealed record GitItemDto(
        string? Path);
}
