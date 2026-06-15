using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

namespace BbongServer.Tests.Api;

[TestFixture]
public class AuthEndpointsTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new WebApplicationFactory<Program>();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task Guest_registration_returns_token_and_nickname()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/auth/guest", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(body.GetProperty("accessToken").GetString(), Is.Not.Empty);
        Assert.That(body.GetProperty("nickname").GetString(), Does.StartWith("게스트"));
    }

    [Test]
    public async Task Me_with_guest_token_returns_profile_and_starting_balance()
    {
        var client = _factory.CreateClient();
        var guest = await (await client.PostAsync("/auth/guest", null)).Content.ReadFromJsonAsync<JsonElement>();
        var token = guest.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetFromJsonAsync<JsonElement>("/me");

        Assert.That(me.GetProperty("isGuest").GetBoolean(), Is.True);
        Assert.That(me.GetProperty("balance").GetInt64(), Is.EqualTo(10_000));
        Assert.That(me.GetProperty("nickname").GetString(), Is.EqualTo(guest.GetProperty("nickname").GetString()));
    }

    [Test]
    public async Task Me_without_token_is_unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
