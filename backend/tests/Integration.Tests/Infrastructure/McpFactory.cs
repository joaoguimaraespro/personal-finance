extern alias mcp;

using GatewayClient = mcp::Host.Mcp.GatewayClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Infrastructure;

/// <summary>The real MCP host, wired to the in-memory API: MCP client → Host.Mcp → AI gateway → PostgreSQL.</summary>
public sealed class McpFactory(ApiFactory api) : WebApplicationFactory<GatewayClient>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Gateway:BaseUrl", "http://localhost");
        builder.ConfigureServices(services => services.AddHttpClient<GatewayClient>()
            .ConfigurePrimaryHttpMessageHandler(() => api.Server.CreateHandler()));
    }
}
