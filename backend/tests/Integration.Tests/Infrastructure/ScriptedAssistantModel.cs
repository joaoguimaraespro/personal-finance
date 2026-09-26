using System.Text.Json;
using Ai.Application.Assistant;
using Ai.Contracts;

namespace Integration.Tests.Infrastructure;

/// <summary>Stands in for Claude: makes the scripted tool calls through the real executor, then answers.</summary>
public sealed class ScriptedAssistantModel : IAssistantModel
{
    public bool Enabled => true;

    public string ModelName => "scripted-test-model";

    public List<(string Tool, object Args)> NextCalls { get; } = [];

    public List<string> Results { get; } = [];

    public IReadOnlyList<string> OfferedTools { get; private set; } = [];

    public async Task<AssistantAnswer> AnswerAsync(string system, IReadOnlyList<ChatTurn> conversation,
        IReadOnlyList<ToolDefinition> tools, ToolExecutor execute, CancellationToken ct)
    {
        OfferedTools = tools.Select(t => t.Name).ToList();
        Results.Clear();
        foreach (var (tool, args) in NextCalls)
        {
            var (content, _) = await execute(tool, JsonSerializer.SerializeToElement(args), ct);
            Results.Add(content);
        }

        NextCalls.Clear();
        return new AssistantAnswer("scripted answer", [], "end_turn");
    }
}
