using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgentForTFS;

public sealed class ConfluenceClient(HttpClient httpClient, AppConfig config)
{
    private const int MaxPages = 3;
    private const int MaxContentLength = 5000;

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>'""]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PageIdQueryRegex = new(
        @"[?&]pageId=(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PagesPathRegex = new(
        @"/pages/(?<id>\d+)(/|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool IsConfigured =>
        config.ConfluenceBaseUrl is not null
        && !string.IsNullOrWhiteSpace(config.ConfluenceToken);

    public async Task<WorkItemContext> EnrichAsync(WorkItemContext context, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return context;
        }

        var sources = new List<ConfluenceContextSource>(context.ConfluenceContextSources);
        var warnings = new List<string>(context.ConfluenceContextWarnings);

        foreach (var pageRef in FindPageReferences(context).Take(MaxPages))
        {
            try
            {
                var page = await GetPageAsync(pageRef, cancellationToken);
                sources.Add(page);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or AgentException)
            {
                warnings.Add($"Confluence page {pageRef.PageId} was not loaded: {ex.Message}");
            }
        }

        return context with
        {
            ConfluenceContextSources = sources,
            ConfluenceContextWarnings = warnings
        };
    }

    private async Task<ConfluenceContextSource> GetPageAsync(ConfluencePageReference pageRef, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildContentUrl(pageRef.PageId));
        AddAuthHeader(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AgentException($"Confluence returned {(int)response.StatusCode} {response.ReasonPhrase}: {TrimError(body)}");
        }

        var dto = JsonSerializer.Deserialize<ConfluenceContentDto>(body, JsonOptions.Default)
            ?? throw new AgentException("Confluence returned an invalid page response.");

        var content = CleanHtml(dto.Body?.Storage?.Value);
        var truncated = content.Length > MaxContentLength;

        return new ConfluenceContextSource(
            dto.Id ?? pageRef.PageId,
            pageRef.Url,
            dto.Title,
            dto.Space?.Key,
            truncated ? content[..MaxContentLength] : content,
            truncated);
    }

    private string BuildContentUrl(string pageId)
    {
        var baseUrl = config.ConfluenceBaseUrl!.ToString().TrimEnd('/');
        return $"{baseUrl}/rest/api/content/{Uri.EscapeDataString(pageId)}?expand=body.storage,space,version";
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (config.ConfluenceAuthMode.Equals("Basic", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(config.ConfluenceUsername))
            {
                throw new AgentException("Confluence:Username is required when Confluence:AuthMode is Basic.");
            }

            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.ConfluenceUsername}:{config.ConfluenceToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ConfluenceToken);
    }

    private IEnumerable<ConfluencePageReference> FindPageReferences(WorkItemContext context)
    {
        var rawValues = new List<string?>();
        rawValues.Add(context.Description);
        rawValues.Add(context.AcceptanceCriteria);
        rawValues.AddRange(context.TextFields.Select(field => field.Value));
        rawValues.AddRange(context.Links.Select(link => link.Url));
        rawValues.AddRange(context.Links.Select(link => link.Comment));

        var seenPageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in rawValues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (Match urlMatch in UrlRegex.Matches(value))
            {
                var url = urlMatch.Value.TrimEnd('.', ',', ')', ']');

                if (!LooksLikeConfiguredConfluenceUrl(url))
                {
                    continue;
                }

                var pageId = ExtractPageId(url);
                if (pageId is null || !seenPageIds.Add(pageId))
                {
                    continue;
                }

                yield return new ConfluencePageReference(pageId, url);
            }
        }
    }

    private bool LooksLikeConfiguredConfluenceUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Host.Equals(config.ConfluenceBaseUrl!.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractPageId(string url)
    {
        var queryMatch = PageIdQueryRegex.Match(url);
        if (queryMatch.Success)
        {
            return queryMatch.Groups["id"].Value;
        }

        var pathMatch = PagesPathRegex.Match(url);
        return pathMatch.Success ? pathMatch.Groups["id"].Value : null;
    }

    private static string CleanHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withLineBreaks = Regex.Replace(value, "<\\s*br\\s*/?>|</\\s*p\\s*>|</\\s*div\\s*>|</\\s*li\\s*>", "\n", RegexOptions.IgnoreCase);
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

    private sealed record ConfluencePageReference(string PageId, string Url);

    private sealed record ConfluenceContentDto(
        string? Id,
        string? Title,
        ConfluenceBodyDto? Body,
        ConfluenceSpaceDto? Space);

    private sealed record ConfluenceBodyDto(ConfluenceStorageDto? Storage);

    private sealed record ConfluenceStorageDto(string? Value);

    private sealed record ConfluenceSpaceDto(string? Key);
}
