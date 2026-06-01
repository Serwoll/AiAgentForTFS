using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;

var chatClient = new OllamaChatClient(
        new Uri("http://localhost:11434"),
        modelId: "qwen3:8b");
AIAgent agent = chatClient.AsAIAgent(
        instructions: "You are a friendly assistant. Keep your answers brief.");

Console.WriteLine(await agent.RunAsync("What is the largest city in France?"));

