using System.Net;
using System.Text.Json;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class AssistantTests(ApiFactory factory)
{
    private static object Ask(string question) => new { messages = new[] { new { role = "user", text = question } } };

    [Fact]
    public async Task Assistant_tools_run_through_the_gateway_with_its_own_scopes_and_audit()
    {
        var owner = await factory.OwnerAsync();
        var status = await owner.GetJsonAsync("/api/assistant/status");
        status.GetProperty("enabled").GetBoolean().ShouldBeTrue();

        factory.Assistant.NextCalls.Add(("get_financial_overview", new { year = 2037 }));
        factory.Assistant.NextCalls.Add(("get_expense_transactions", new { period = "2037-01" })); // not in default scopes

        var response = await owner.PostAsync("/api/assistant/chat", Ask("How did 2037 go?"));
        await ApiClient.EnsureAsync(response);
        var answer = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

        answer.GetProperty("reply").GetString().ShouldBe("scripted answer");
        var calls = answer.GetProperty("toolCalls").EnumerateArray().ToList();
        calls.Select(c => c.GetProperty("status").GetInt32()).ShouldBe([200, 403]);
        factory.Assistant.OfferedTools.ShouldNotContain("get_expense_transactions"); // not even offered to the model
        factory.Assistant.Results[1].ShouldNotContain("items");

        var audit = await owner.GetAsync<JsonElement[]>("/api/ai-admin/audit?limit=20");
        audit.ShouldContain(e => e.GetProperty("clientName").GetString() == "In-app assistant" && e.GetProperty("decision").GetString() == "Allowed");
        audit.ShouldContain(e => e.GetProperty("clientName").GetString() == "In-app assistant" && e.GetProperty("decision").GetString() == "Denied");
    }

    [Fact]
    public async Task The_internal_assistant_identity_cannot_be_used_from_outside_and_can_be_revoked()
    {
        // Revocation is permanent, so this runs on its own server and database.
        await using var isolated = new ApiFactory();
        await isolated.InitializeAsync();
        var owner = await isolated.OwnerAsync();
        await owner.GetJsonAsync("/api/assistant/status"); // ensures the internal client exists
        var clients = await owner.GetAsync<JsonElement[]>("/api/ai-admin/clients");
        var internalClient = clients.Single(c => c.GetProperty("internal").GetBoolean());

        // Its prefix is public, but no token for it was ever issued: guessing the rest fails.
        using var http = isolated.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/tools/get_net_worth");
        request.Headers.Authorization = new("Bearer", $"{internalClient.GetProperty("tokenPrefix").GetString()}_{new string('A', 43)}");
        (await http.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await ApiClient.EnsureAsync(await owner.PostAsync($"/api/ai-admin/clients/{internalClient.GetProperty("id").GetGuid()}/revoke", new { }));
        (await owner.GetJsonAsync("/api/assistant/status")).GetProperty("enabled").GetBoolean().ShouldBeFalse();
        isolated.Assistant.NextCalls.Add(("get_net_worth", new { }));
        var reply = await owner.PostAsync("/api/assistant/chat", Ask("Net worth?"));
        var body = JsonSerializer.Deserialize<JsonElement>(await reply.Content.ReadAsStringAsync());
        body.GetProperty("reply").GetString()!.ShouldContain("disabled");
        isolated.Assistant.Results.ShouldBeEmpty(); // the model was never invoked with data
    }

    [Fact]
    public async Task Conversation_size_is_bounded()
    {
        var owner = await factory.OwnerAsync();
        var tooLong = await owner.PostAsync("/api/assistant/chat", Ask(new string('x', 2001)));
        var noQuestion = await owner.PostAsync("/api/assistant/chat", new { messages = new[] { new { role = "assistant", text = "hi" } } });

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noQuestion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
