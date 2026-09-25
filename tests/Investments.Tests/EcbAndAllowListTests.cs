using System.Net;
using System.Text;
using Investments.Infrastructure.Fx;
using SharedKernel.Http;

namespace Investments.Tests;

public sealed class EcbAndAllowListTests
{
    [Fact]
    public void Parses_ecb_reference_rates()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
              <Cube>
                <Cube time="2026-09-24"><Cube currency="USD" rate="1.1234"/><Cube currency="GBP" rate="0.8456"/></Cube>
                <Cube time="2026-09-23"><Cube currency="USD" rate="1.1200"/></Cube>
              </Cube>
            </gesmes:Envelope>
            """;

        var rates = EcbFxRateSource.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        rates.Count.ShouldBe(3);
        var usd = rates.First(r => r.Quote == "USD" && r.Date == new DateOnly(2026, 9, 24));
        usd.Rate.ShouldBe(1.1234m);
        decimal.Round(usd.ToEurFactor, 6).ShouldBe(0.890155m);
    }

    [Fact]
    public void Xml_with_a_dtd_is_refused() =>
        Should.Throw<System.Xml.XmlException>(() => EcbFxRateSource.Parse(new MemoryStream(Encoding.UTF8.GetBytes(
            """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><Cube time="2026-01-01">&e;</Cube>"""))));

    private sealed class Terminal : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, Terminal Terminal) Client()
    {
        var terminal = new Terminal();
        var guard = new AllowListHttpHandler(
        [
            AllowedRequest.Get("live.trading212.com", "/api/v0/equity/positions"),
            AllowedRequest.Post("live.trading212.com", "/api/v0/equity/history/exports"),
        ]) { InnerHandler = terminal };
        return (new HttpClient(guard), terminal);
    }

    [Fact]
    public async Task Allowed_read_requests_pass()
    {
        var (client, terminal) = Client();

        await client.GetAsync(new Uri("https://live.trading212.com/api/v0/equity/positions"));

        terminal.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("POST", "https://live.trading212.com/api/v0/equity/orders/market")]
    [InlineData("POST", "https://live.trading212.com/api/v0/equity/orders/limit")]
    [InlineData("DELETE", "https://live.trading212.com/api/v0/equity/orders/123")]
    [InlineData("GET", "https://evil.example.com/api/v0/equity/positions")]
    [InlineData("GET", "http://live.trading212.com/api/v0/equity/positions")]
    [InlineData("PUT", "https://live.trading212.com/api/v0/equity/positions")]
    public async Task Anything_else_is_blocked_before_leaving_the_process(string method, string url)
    {
        var (client, terminal) = Client();

        await Should.ThrowAsync<BlockedRequestException>(() =>
            client.SendAsync(new HttpRequestMessage(new HttpMethod(method), new Uri(url))));
        terminal.Calls.ShouldBe(0);
    }
}
