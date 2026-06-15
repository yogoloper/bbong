using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Infrastructure.InMemory;
using BbongServer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace BbongServer.Tests.Api;

[TestFixture]
public class AuthEndpointsTests
{
    private WebApplicationFactory<Program> _factory = null!;

    // 엔드포인트 동작만 검증 — 저장소를 인메모리로 교체해 PostgreSQL 없이도 빠르게 통과.
    [SetUp]
    public void SetUp() => _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<BbongDbContext>>();
            services.RemoveAll<BbongDbContext>();
            services.RemoveAll<IAccountStore>();
            services.RemoveAll<ILedgerStore>();
            services.RemoveAll<IAdRewardStore>();
            services.RemoveAll<ISocialTokenVerifier>();
            services.AddSingleton<IAccountStore, InMemoryAccountStore>();
            services.AddSingleton<ILedgerStore, InMemoryLedgerStore>();
            services.AddSingleton<IAdRewardStore, InMemoryAdRewardStore>();
            services.AddSingleton<ISocialTokenVerifier, BbongServer.Infrastructure.Social.DevBypassSocialVerifier>();
        }));

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

    [Test]
    public async Task Social_login_returns_non_guest_account()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/social",
            new { provider = "Google", idToken = "google-sub-x" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(body.GetProperty("isGuest").GetBoolean(), Is.False);
        Assert.That(body.GetProperty("accessToken").GetString(), Is.Not.Empty);
    }

    [Test]
    public async Task Rename_changes_nickname_and_persists()
    {
        var client = _factory.CreateClient();
        var guest = await (await client.PostAsync("/auth/guest", null)).Content.ReadFromJsonAsync<JsonElement>();
        var token = guest.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var patch = await client.PatchAsJsonAsync("/me/nickname", new { nickname = "명랑한 수달" });
        Assert.That(patch.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.That(me.GetProperty("nickname").GetString(), Is.EqualTo("명랑한 수달"));
    }

    [Test]
    public async Task Rename_with_invalid_nickname_is_bad_request()
    {
        var client = _factory.CreateClient();
        var guest = await (await client.PostAsync("/auth/guest", null)).Content.ReadFromJsonAsync<JsonElement>();
        var token = guest.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var patch = await client.PatchAsJsonAsync("/me/nickname", new { nickname = "" });

        Assert.That(patch.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Standard_ad_reward_adds_2000_then_cooldown_blocks()
    {
        var client = _factory.CreateClient();
        var guest = await (await client.PostAsync("/auth/guest", null)).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", guest.GetProperty("accessToken").GetString());

        var first = await client.PostAsJsonAsync("/shop/ad-reward", new { kind = "Standard" });
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(firstBody.GetProperty("balance").GetInt64(), Is.EqualTo(12_000)); // 10000 + 2000

        var second = await client.PostAsJsonAsync("/shop/ad-reward", new { kind = "Standard" });
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest)); // 쿨다운
    }

    [Test]
    public async Task Link_promotes_guest_to_social()
    {
        var client = _factory.CreateClient();
        var guest = await (await client.PostAsync("/auth/guest", null)).Content.ReadFromJsonAsync<JsonElement>();
        var token = guest.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/auth/link",
            new { provider = "Kakao", idToken = "kakao-sub-y" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(body.GetProperty("isGuest").GetBoolean(), Is.False);
        Assert.That(body.GetProperty("userId").GetGuid(), Is.EqualTo(guest.GetProperty("userId").GetGuid()));
    }
}
