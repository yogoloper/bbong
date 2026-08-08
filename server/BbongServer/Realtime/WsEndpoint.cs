using System;
using System.IO;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BbongCore.Online;
using BbongServer.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BbongServer.Realtime;

/// <summary>
/// /ws 실시간 엔드포인트. 업그레이드 요청의 Authorization 헤더로 기존 JWT 파이프라인 그대로 인증.
/// 수신 루프: JSON 파싱 → 방 커맨드로 라우팅. 끊김 → DisconnectCmd.
/// </summary>
public static class WsEndpoint
{
    public static void Map(WebApplication app) =>
        app.Map("/ws", Handle).RequireAuthorization();

    private static async Task Handle(HttpContext context, RoomRegistry registry, IAccountStore accounts, IStakeBank bank)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(sub, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var account = await accounts.GetByIdAsync(userId);
        if (account is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var sink = new WebSocketSessionSink(userId, socket);
        var member = new RoomMember(sink, userId, account.Nickname);
        await sink.SendAsync(new WelcomeMsg { userId = userId.ToString() });

        try
        {
            await ReceiveLoopAsync(socket, registry, bank, sink, member);
        }
        finally
        {
            registry.FindByUser(userId)?.Dispatch(new DisconnectCmd(userId));
        }
    }

    private static async Task ReceiveLoopAsync(WebSocket socket, RoomRegistry registry, IStakeBank bank, WebSocketSessionSink sink, RoomMember member)
    {
        var buffer = new byte[8 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            string json;
            try
            {
                var (closed, text) = await ReadMessageAsync(socket, buffer);
                if (closed)
                {
                    return;
                }

                json = text;
            }
            catch (WebSocketException)
            {
                return; // 비정상 끊김 → finally에서 Disconnect 처리
            }

            var message = RealtimeJson.ParseClientMessage(json);
            if (message is null)
            {
                await sink.SendAsync(new ErrorMsg { code = "bad_message", message = "메시지를 해석할 수 없습니다." });
                continue;
            }

            await RouteAsync(registry, bank, sink, member, message);
        }
    }

    private static async Task RouteAsync(RoomRegistry registry, IStakeBank bank, WebSocketSessionSink sink, RoomMember member, object message)
    {
        var room = registry.FindByUser(member.UserId);
        switch (message)
        {
            case CreateRoomMsg when room is not null:
            case JoinRoomMsg when room is not null:
            case QuickMatchMsg when room is not null:
                _ = sink.SendAsync(new ErrorMsg { code = "already_in_room", message = "이미 방에 있습니다." });
                break;
            case QuickMatchMsg quick:
                if (!BbongCore.Config.GameConfig.IsValidStake(quick.stake) || !BbongCore.Config.GameConfig.IsValidPlayerCount(quick.players))
                {
                    _ = sink.SendAsync(new ErrorMsg { code = "invalid_match", message = "허용되지 않는 매칭 조건입니다." });
                    break;
                }

                if (!await bank.TryEscrowAsync(member.UserId, quick.stake))
                {
                    _ = sink.SendAsync(new ErrorMsg { code = "insufficient_balance", message = "포인트가 부족합니다." });
                    break;
                }

                registry.QuickMatch(member, quick.stake, quick.players, bank);
                break;
            case CreateRoomMsg create:
                if (create.stake != 0 && !BbongCore.Config.GameConfig.IsValidStake(create.stake))
                {
                    _ = sink.SendAsync(new ErrorMsg { code = "invalid_stake", message = "허용되지 않는 입장료입니다." });
                    break;
                }

                if (create.stake > 0 && !await bank.TryEscrowAsync(member.UserId, create.stake))
                {
                    _ = sink.SendAsync(new ErrorMsg { code = "insufficient_balance", message = "포인트가 부족합니다." });
                    break;
                }

                registry.Create(member, stake: create.stake, bank: bank);
                break;
            case JoinRoomMsg join:
                var target = registry.FindByCode(join.code);
                if (target is null)
                {
                    _ = sink.SendAsync(new ErrorMsg { code = "room_not_found", message = "초대코드에 해당하는 방이 없습니다." });
                    break;
                }

                if (target.Stake > 0 && !target.HasSeatFor(member.UserId) && !await bank.TryEscrowAsync(member.UserId, target.Stake))
                {
                    _ = sink.SendAsync(new ErrorMsg { code = "insufficient_balance", message = "포인트가 부족합니다." });
                    break;
                }

                if (!registry.TryJoin(join.code, member))
                {
                    if (target.Stake > 0)
                    {
                        await bank.RefundAsync(member.UserId, target.Stake); // 그 사이 방이 닫힘
                    }

                    _ = sink.SendAsync(new ErrorMsg { code = "room_not_found", message = "초대코드에 해당하는 방이 없습니다." });
                }

                break;
            case LeaveRoomMsg:
                room?.Dispatch(new LeaveCmd(member.UserId));
                break;
            case StartGameMsg:
                room?.Dispatch(new StartGameCmd(member.UserId));
                break;
            case AddBotMsg:
                room?.Dispatch(new AddBotCmd(member.UserId));
                break;
            case RemoveBotMsg:
                room?.Dispatch(new RemoveBotCmd(member.UserId));
                break;
            default:
                // 게임 액션은 방 루프가 좌석 판정 후 세션에 전달
                if (room is null)
                {
                    _ = sink.SendAsync(new ErrorMsg { code = "not_in_room", message = "방에 입장한 상태가 아닙니다." });
                }
                else
                {
                    room.Dispatch(new ActionCmd(member.UserId, message));
                }

                break;
        }
    }

    private static async Task<(bool Closed, string Text)> ReadMessageAsync(WebSocket socket, byte[] buffer)
    {
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                return (true, "");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return (false, Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }
}
