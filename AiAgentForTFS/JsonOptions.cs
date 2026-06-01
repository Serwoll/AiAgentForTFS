using System.Text.Json;

namespace AiAgentForTFS;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}
