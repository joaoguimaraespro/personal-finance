using System.Net;
using System.Text.Json;
using Integration.Tests.Infrastructure;

namespace Integration.Tests;

[Collection(ApiCollection.Name)]
public sealed class CategoryRenameTests(ApiFactory factory)
{
    private static JsonElement ByKey(JsonElement[] categories, string key) =>
        categories.Single(c => c.GetProperty("key").GetString() == key);

    [Fact]
    public async Task A_built_in_category_can_be_renamed_and_reset_to_its_default_name()
    {
        var api = await factory.OwnerAsync();
        var gym = ByKey(await api.GetAsync<JsonElement[]>("/api/categories?includeArchived=true"), "gym");
        var id = gym.GetProperty("id").GetGuid();
        var original = gym.GetProperty("name").GetString();
        gym.GetProperty("renamed").GetBoolean().ShouldBeFalse();

        await ApiClient.EnsureAsync(await api.PutAsync($"/api/categories/{id}", new
        {
            name = "Ginásio & Padel", defaultNature = gym.GetProperty("defaultNature").GetString(),
            color = gym.GetProperty("color").GetString(), icon = gym.GetProperty("icon").GetString(),
        }));
        var renamed = ByKey(await api.GetAsync<JsonElement[]>("/api/categories?includeArchived=true"), "gym");
        renamed.GetProperty("name").GetString().ShouldBe("Ginásio & Padel");
        renamed.GetProperty("renamed").GetBoolean().ShouldBeTrue();
        renamed.GetProperty("key").GetString().ShouldBe("gym"); // budgets, reports and AI tools keep working by key

        await ApiClient.EnsureAsync(await api.PostAsync($"/api/categories/{id}/reset-name", new { }));
        var reset = ByKey(await api.GetAsync<JsonElement[]>("/api/categories?includeArchived=true"), "gym");
        reset.GetProperty("name").GetString().ShouldBe(original);
        reset.GetProperty("renamed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Only_built_in_categories_have_a_default_name_to_reset_to()
    {
        var api = await factory.OwnerAsync();
        var id = await api.CreateAsync("/api/categories", new { name = "Side project", type = "Expense", defaultNature = "Variable" });

        (await api.PostAsync($"/api/categories/{id}/reset-name", new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ByKey(await api.GetAsync<JsonElement[]>("/api/categories"), (await api.GetAsync<JsonElement[]>("/api/categories"))
            .Single(c => c.GetProperty("id").GetGuid() == id).GetProperty("key").GetString()!)
            .GetProperty("renamed").GetBoolean().ShouldBeFalse();
    }
}
