using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;

var chatClient = new OllamaChatClient(
        new Uri("http://localhost:11434"),
        modelId: "qwen3:8b");
AIAgent agent = chatClient.AsAIAgent(
        instructions: "You are a friendly assistant. Keep your answers brief.");

while (true)
{
        Console.WriteLine("Ask agent");
        string? promt = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(promt))
        {
                Console.WriteLine("Promt is empty");
                return;
        }
        if (promt.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;
        Console.WriteLine(await agent.RunAsync(promt));
}


