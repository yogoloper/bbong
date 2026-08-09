using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
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
public class WsEndpointTests
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

    private async Task<string> GuestTokenAsync()
    {
        var client = _factory.CreateClient();
        var guest = await (await client.PostAsync("/auth/guest", null)).Content.ReadFromJsonAsync<JsonElement>();
        return guest.GetProperty("accessToken").GetString()!;
    }

    private async Task<WebSocket> ConnectAsync(string token)
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers.Authorization = "Bearer " + token;
        return await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws"), CancellationToken.None);
    }

    private static async Task SendAsync(WebSocket socket, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>지정 type 메시지가 올 때까지 수신(그 외는 스킵). 기본 5초 타임아웃.</summary>
    private static async Task<JsonElement> ReceiveUntilAsync(WebSocket socket, string type, int timeoutSeconds = 5)
    {
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException($"'{type}' 수신 전에 소켓이 닫혔습니다.");
            }

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var element = JsonDocument.Parse(json).RootElement.Clone();
            if (element.GetProperty("type").GetString() == type)
            {
                return element;
            }
        }
    }

    [Test]
    public void Connect_without_token_is_rejected()
    {
        var wsClient = _factory.Server.CreateWebSocketClient();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws"), CancellationToken.None));
    }

    [Test]
    public async Task Connect_receives_welcome()
    {
        var socket = await ConnectAsync(await GuestTokenAsync());

        var welcome = await ReceiveUntilAsync(socket, "welcome");

        Assert.That(welcome.GetProperty("userId").GetString(), Is.Not.Empty);
    }

    [Test]
    public async Task Connect_with_query_token_is_accepted()
    {
        // 브라우저(WebGL) WebSocket은 Authorization 헤더 불가 → ?access_token= 병행 허용
        var token = await GuestTokenAsync();
        var wsClient = _factory.Server.CreateWebSocketClient();

        var socket = await wsClient.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, $"/ws?access_token={token}"), CancellationToken.None);
        var welcome = await ReceiveUntilAsync(socket, "welcome");

        Assert.That(welcome.GetProperty("userId").GetString(), Is.Not.Empty);
    }

    [Test]
    public async Task Create_then_join_syncs_member_list()
    {
        var host = await ConnectAsync(await GuestTokenAsync());
        await ReceiveUntilAsync(host, "welcome");
        await SendAsync(host, new { type = "createRoom" });
        var created = await ReceiveUntilAsync(host, "roomUpdate");
        var code = created.GetProperty("code").GetString();

        var guest = await ConnectAsync(await GuestTokenAsync());
        await ReceiveUntilAsync(guest, "welcome");
        await SendAsync(guest, new { type = "joinRoom", code });

        var hostView = await ReceiveUntilAsync(host, "roomUpdate");
        var guestView = await ReceiveUntilAsync(guest, "roomUpdate");
        Assert.That(hostView.GetProperty("members").GetArrayLength(), Is.EqualTo(2));
        Assert.That(guestView.GetProperty("members").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task Join_with_unknown_code_returns_error()
    {
        var socket = await ConnectAsync(await GuestTokenAsync());
        await ReceiveUntilAsync(socket, "welcome");

        await SendAsync(socket, new { type = "joinRoom", code = "000000" });

        var error = await ReceiveUntilAsync(socket, "error");
        Assert.That(error.GetProperty("code").GetString(), Is.EqualTo("room_not_found"));
    }

    private async Task<(WebSocket host, WebSocket guest)> StartedGameAsync()
    {
        var host = await ConnectAsync(await GuestTokenAsync());
        await ReceiveUntilAsync(host, "welcome");
        await SendAsync(host, new { type = "createRoom" });
        var code = (await ReceiveUntilAsync(host, "roomUpdate")).GetProperty("code").GetString();

        var guest = await ConnectAsync(await GuestTokenAsync());
        await ReceiveUntilAsync(guest, "welcome");
        await SendAsync(guest, new { type = "joinRoom", code });
        await ReceiveUntilAsync(guest, "roomUpdate");

        await SendAsync(host, new { type = "startGame" });
        return (host, guest);
    }

    [Test]
    public async Task Start_deals_hidden_hands_per_seat()
    {
        var (host, guest) = await StartedGameAsync();

        var hostStarted = await ReceiveUntilAsync(host, "gameStarted");
        Assert.That(hostStarted.GetProperty("yourSeat").GetInt32(), Is.EqualTo(0));
        Assert.That((await ReceiveUntilAsync(guest, "gameStarted")).GetProperty("yourSeat").GetInt32(), Is.EqualTo(1));

        // 선(seat0)의 자동 드로우 후: 본인 뷰 손패 6장, 상대 뷰는 장수만
        // 첫 선은 랜덤(§2) — 선만 6장, 상대는 5장 + 선의 장수만 공개
        var hostDrew = await ReceiveUntilAsync(host, "drewCard");
        var dealer = hostDrew.GetProperty("seat").GetInt32();
        var hostHand = hostDrew.GetProperty("view").GetProperty("myHand").GetArrayLength();
        Assert.That(hostHand, Is.EqualTo(dealer == 0 ? 6 : 5));

        var guestDrew = await ReceiveUntilAsync(guest, "drewCard");
        Assert.That(guestDrew.GetProperty("view").GetProperty("myHand").GetArrayLength(), Is.EqualTo(dealer == 1 ? 6 : 5));
        Assert.That(guestDrew.GetProperty("view").GetProperty("seats")[dealer].GetProperty("handCount").GetInt32(), Is.EqualTo(6));
    }

    [Test]
    public async Task Discard_round_trips_to_both_players()
    {
        var (host, guest) = await StartedGameAsync();
        var drew = await ReceiveUntilAsync(host, "drewCard");
        var dealer = drew.GetProperty("seat").GetInt32();
        var actor = dealer == 0 ? host : guest;   // 랜덤 선 소켓이 버린다
        var observer = dealer == 0 ? guest : host;
        var actorDrew = dealer == 0 ? drew : await ReceiveUntilAsync(guest, "drewCard");
        var first = actorDrew.GetProperty("view").GetProperty("myHand")[0];

        await SendAsync(actor, new
        {
            type = "discard",
            card = new { number = first.GetProperty("number").GetInt32(), color = first.GetProperty("color").GetInt32() }
        });

        var seen = await ReceiveUntilAsync(observer, "discarded");
        Assert.That(seen.GetProperty("seat").GetInt32(), Is.EqualTo(dealer));
        Assert.That(seen.GetProperty("card").GetProperty("number").GetInt32(), Is.EqualTo(first.GetProperty("number").GetInt32()));
    }

    [Test]
    public async Task Abrupt_close_during_game_keeps_game_running_for_others()
    {
        var (host, guest) = await StartedGameAsync();
        var drew = await ReceiveUntilAsync(guest, "drewCard");
        var dealer = drew.GetProperty("seat").GetInt32();
        var leaver = dealer == 0 ? host : guest;   // 선(랜덤)의 소켓을 끊는다
        var survivor = dealer == 0 ? guest : host;

        leaver.Abort(); // 강제 종료(게임 중 끊김)

        // 방은 유지되고, 끊긴 선의 턴은 5초 룰이 자동 버림으로 진행시킨다(§9-4)
        var discarded = await ReceiveUntilAsync(survivor, "discarded", timeoutSeconds: 8);
        Assert.That(discarded.GetProperty("seat").GetInt32(), Is.EqualTo(dealer));
    }
}
