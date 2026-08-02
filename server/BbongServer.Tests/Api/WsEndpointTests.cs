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

    /// <summary>지정 type 메시지가 올 때까지 수신(그 외는 스킵). 5초 타임아웃.</summary>
    private static async Task<JsonElement> ReceiveUntilAsync(WebSocket socket, string type)
    {
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
        var hostDrew = await ReceiveUntilAsync(host, "drewCard");
        Assert.That(hostDrew.GetProperty("view").GetProperty("myHand").GetArrayLength(), Is.EqualTo(6));

        var guestDrew = await ReceiveUntilAsync(guest, "drewCard");
        Assert.That(guestDrew.GetProperty("view").GetProperty("myHand").GetArrayLength(), Is.EqualTo(5));
        Assert.That(guestDrew.GetProperty("view").GetProperty("seats")[0].GetProperty("handCount").GetInt32(), Is.EqualTo(6));
    }

    [Test]
    public async Task Discard_round_trips_to_both_players()
    {
        var (host, guest) = await StartedGameAsync();
        var drew = await ReceiveUntilAsync(host, "drewCard");
        var first = drew.GetProperty("view").GetProperty("myHand")[0];

        await SendAsync(host, new
        {
            type = "discard",
            card = new { number = first.GetProperty("number").GetInt32(), color = first.GetProperty("color").GetInt32() }
        });

        var seen = await ReceiveUntilAsync(guest, "discarded");
        Assert.That(seen.GetProperty("seat").GetInt32(), Is.EqualTo(0));
        Assert.That(seen.GetProperty("card").GetProperty("number").GetInt32(), Is.EqualTo(first.GetProperty("number").GetInt32()));
    }

    [Test]
    public async Task Abrupt_close_during_game_closes_room_for_others()
    {
        var (host, guest) = await StartedGameAsync();
        await ReceiveUntilAsync(guest, "gameStarted");

        host.Abort(); // 강제 종료(게임 중 끊김)

        var closed = await ReceiveUntilAsync(guest, "roomClosed");
        Assert.That(closed.GetProperty("reason").GetString(), Is.Not.Empty);
    }
}
