namespace AiAgentForTFS;

public sealed record WorkItemContext(
    int Id,
    string Title,
    string Type,
    string State,
    string? AssignedTo,
    string? Description,
    string? AcceptanceCriteria,
    string? Tags,
    IReadOnlyList<WorkItemTextField> TextFields,
    IReadOnlyList<WorkItemLink> Links,
    IReadOnlyList<ConfluenceContextSource> ConfluenceContextSources,
    IReadOnlyList<string> ConfluenceContextWarnings,
    IReadOnlyList<CodeContextSource> CodeContextSources,
    IReadOnlyList<string> CodeContextWarnings);

public sealed record WorkItemTextField(
    string Name,
    string Value,
    bool LooksLikeCodeContext);

public sealed record WorkItemLink(
    string Relation,
    string Url,
    string? Name,
    string? Comment,
    bool LooksLikeCodeContext);

public sealed record CodeContextSource(
    string SourceType,
    string RepositoryId,
    int? PullRequestId,
    string? Title,
    string? SourceBranch,
    string? TargetBranch,
    IReadOnlyList<string> CommitMessages,
    IReadOnlyList<string> ChangedFiles,
    bool CommitLimitReached,
    bool ChangedFileLimitReached);

public sealed record ConfluenceContextSource(
    string PageId,
    string Url,
    string? Title,
    string? SpaceKey,
    string Content,
    bool ContentTruncated);

public sealed record ManualUserStoryInput(
    string Text,
    IReadOnlyList<string> Links);

public sealed record QaChecklistWorkflowResult(
    string Markdown,
    string OutputPath);
