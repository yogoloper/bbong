using System;
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
public class MatchEndpointsTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<BbongDbContext>>();
            services.RemoveAll<BbongDbContext>();
            services.RemoveAll<IAccountStore>();
            services.RemoveAll<ILedgerStore>();
            services.RemoveAll<IAdRewardStore>();
            services.RemoveAll<IMatchStore>();
            services.AddSingleton<IAccountStore, InMemoryAccountStore>();
            services.AddSingleton<ILedgerStore, InMemoryLedgerStore>();
            services.AddSingleton<IAdRewardStore, InMemoryAdRewardStore>();
            services.AddSingleton<IMatchStore, InMemoryMatchStore>();
        }));

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<HttpClient> GuestClientAsync()
    {
        var client = _factory.CreateClient();
        var guest = await (await client.PostAsync("/auth/guest", null)).Content.ReadFromJsonAsync<JsonElement>();
        var token = guest.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Test]
    public async Task Start_then_result_updates_balance()
    {
        var client = await GuestClientAsync();

        // 시작: 10000 - 1000 에스크로
        var start = await client.PostAsJsonAsync("/match/start", new { stake = 1000, playerCount = 4 });
        Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var started = await start.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(started.GetProperty("balance").GetInt64(), Is.EqualTo(9_000));
        var matchId = started.GetProperty("matchId").GetGuid();

        // 승리 정산: +4000
        var result = await client.PostAsJsonAsync($"/match/{matchId}/result", new { won = true, winnersCount = 1 });
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var settled = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(settled.GetProperty("payout").GetInt64(), Is.EqualTo(4_000));
        Assert.That(settled.GetProperty("balance").GetInt64(), Is.EqualTo(13_000));

        // /me 잔액 정합
        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.That(me.GetProperty("balance").GetInt64(), Is.EqualTo(13_000));
    }

    [Test]
    public async Task Start_with_insufficient_balance_is_bad_request()
    {
        var client = await GuestClientAsync();

        // 게스트 시작 잔액 10000 < 판돈 10000 두 번
        var first = await client.PostAsJsonAsync("/match/start", new { stake = 10_000, playerCount = 2 });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var second = await client.PostAsJsonAsync("/match/start", new { stake = 10_000, playerCount = 2 });
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Start_with_invalid_stake_is_bad_request()
    {
        var client = await GuestClientAsync();

        var response = await client.PostAsJsonAsync("/match/start", new { stake = 123, playerCount = 4 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Result_twice_is_bad_request()
    {
        var client = await GuestClientAsync();
        var started = await (await client.PostAsJsonAsync("/match/start", new { stake = 1000, playerCount = 4 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var matchId = started.GetProperty("matchId").GetGuid();

        await client.PostAsJsonAsync($"/match/{matchId}/result", new { won = false, winnersCount = 1 });
        var second = await client.PostAsJsonAsync($"/match/{matchId}/result", new { won = true, winnersCount = 1 });

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Result_of_other_users_match_is_not_found()
    {
        var owner = await GuestClientAsync();
        var started = await (await owner.PostAsJsonAsync("/match/start", new { stake = 1000, playerCount = 4 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var matchId = started.GetProperty("matchId").GetGuid();

        var stranger = await GuestClientAsync(); // 다른 게스트
        var response = await stranger.PostAsJsonAsync($"/match/{matchId}/result", new { won = true, winnersCount = 1 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Match_endpoints_without_token_are_unauthorized()
    {
        var client = _factory.CreateClient();

        var start = await client.PostAsJsonAsync("/match/start", new { stake = 1000, playerCount = 4 });
        var result = await client.PostAsJsonAsync($"/match/{Guid.NewGuid()}/result", new { won = true, winnersCount = 1 });

        Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
