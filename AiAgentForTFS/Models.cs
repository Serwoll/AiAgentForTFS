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
    IReadOnlyList<WorkItemLink> Links);

public sealed record WorkItemTextField(
    string Name,
    string Value);

public sealed record WorkItemLink(
    string Relation,
    string Url,
    string? Name,
    string? Comment);
