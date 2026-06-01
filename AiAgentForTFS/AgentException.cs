namespace AiAgentForTFS;

public sealed class AgentException(string message) : Exception(message);

public sealed class ConfigurationException(string message) : Exception(message);
