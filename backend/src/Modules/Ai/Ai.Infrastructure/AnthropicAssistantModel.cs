using System.Text.Json;
using Ai.Application.Assistant;
using Ai.Contracts;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using ClaudeModel = Anthropic.Models.Messages.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ai.Infrastructure;

/// <summary>
/// Claude via the official Anthropic SDK: a manual tool-use loop whose only tools are the gateway's catalogue,
/// filtered by the assistant client's scopes (read-only unless the owner grants a write scope). Disabled unless an API key is configured. Refused requests are retried server-side on a fallback model.
/// </summary>
internal sealed class AnthropicAssistantModel(IConfiguration config, ILogger<AnthropicAssistantModel> logger) : IAssistantModel
{
    private const int MaxRounds = 6;
    private readonly string? _apiKey = config["Assistant:AnthropicApiKey"] is { Length: > 0 } key ? key
        : config["ANTHROPIC_API_KEY"] is { Length: > 0 } env ? env : null;

    public bool Enabled => _apiKey is not null;

    public string ModelName => config["Assistant:Model"] ?? "claude-opus-5";

    public async Task<AssistantAnswer> AnswerAsync(string system, IReadOnlyList<ChatTurn> conversation,
        IReadOnlyList<ToolDefinition> tools, ToolExecutor execute, CancellationToken ct)
    {
        if (_apiKey is null)
        {
            throw new InvalidOperationException("The assistant is not configured.");
        }

        var client = new AnthropicClient { ApiKey = _apiKey };
        var messages = conversation
            .Select(t => new BetaMessageParam { Role = t.Role == "assistant" ? Role.Assistant : Role.User, Content = t.Text })
            .ToList();
        var toolDefs = tools.Select(ToBetaTool).ToList();

        for (var round = 0; round < MaxRounds; round++)
        {
            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = ModelName,
                MaxTokens = 16000,
                System = system,
                Thinking = new BetaThinkingConfigAdaptive(),
                OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                Tools = toolDefs,
                Messages = messages,
                // Server-side refusal fallback: a declined request is re-served by the fallback model inside the same
                // call instead of simply stopping.
                Betas = [AnthropicBeta.ServerSideFallback2026_06_01],
                Fallbacks = new List<BetaFallbackParam> { new(ClaudeModel.ClaudeOpus4_8) },
            }, ct);

            if (response.StopReason == "refusal")
            {
                return new AssistantAnswer("I can't help with that request.", [], "refusal");
            }

            var assistantContent = new List<BetaContentBlockParam>();
            var toolResults = new List<BetaContentBlockParam>();
            var text = new List<string>();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var t))
                {
                    text.Add(t.Text);
                    assistantContent.Add(new BetaTextBlockParam { Text = t.Text });
                }
                else if (block.TryPickThinking(out var thinking))
                {
                    // Thinking blocks are echoed back unchanged (signature included) within the same conversation.
                    assistantContent.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out var redacted))
                {
                    assistantContent.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    assistantContent.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    var input = JsonSerializer.SerializeToElement(toolUse.Input);
                    (string content, bool isError) = await execute(toolUse.Name, input, ct);
                    toolResults.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = content, IsError = isError });
                }
            }

            if (toolResults.Count == 0)
            {
                return new AssistantAnswer(string.Join("\n\n", text).Trim(), [], response.StopReason?.ToString());
            }

            messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = assistantContent });
            messages.Add(new BetaMessageParam { Role = Role.User, Content = toolResults });
        }

        logger.LogWarning("Assistant stopped after {Rounds} tool rounds", MaxRounds);
        return new AssistantAnswer("That needed more steps than I allow for one question. Try a narrower question.", [], "max_rounds");
    }

    private static BetaToolUnion ToBetaTool(ToolDefinition t) => new BetaTool
    {
        Name = t.Name,
        Description = t.Description,
        InputSchema = new()
        {
            Properties = t.Parameters.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(
                p.Enum is { } values
                    ? new { type = p.Type, description = p.Description, @enum = values }
                    : (object)new { type = p.Type, description = p.Description })),
            Required = t.Parameters.Where(p => p.Required).Select(p => p.Name).ToList(),
        },
    };
}
