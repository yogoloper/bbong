using System;
using System.IO;
using System.Security.Claims;
using System.Text.Json.Serialization;
using BbongServer.Application;
using BbongServer.Domain.Auth;
using BbongServer.Domain.Shop;
using BbongServer.Infrastructure;
using BbongServer.Infrastructure.Auth;
using BbongServer.Infrastructure.Persistence;
using BbongServer.Infrastructure.Social;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

// 개발 편의: 루트 .env가 있으면 환경변수로 로드(레포 루트로 거슬러 탐색).
// 운영은 .env 없이 호스팅 시크릿 매니저가 환경변수를 직접 주입.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// JWT 설정: appsettings → 환경변수 → (개발 전용) fallback. 프로덕션은 BBONG_JWT_KEY 필수.
var jwt = new JwtSettings();
builder.Configuration.GetSection("Jwt").Bind(jwt);
jwt.Key = builder.Configuration["Jwt:Key"]
          ?? Environment.GetEnvironmentVariable("BBONG_JWT_KEY")
          ?? "dev-only-insecure-signing-key-change-me-32+bytes";

builder.Services.AddSingleton(jwt);

// PostgreSQL: appsettings → 환경변수 → (개발 전용) fallback. 운영은 BBONG_DB_CONN.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
                       ?? Environment.GetEnvironmentVariable("BBONG_DB_CONN")
                       ?? "Host=localhost;Port=5432;Database=bbong;Username=bbong;Password=bbong_dev";
builder.Services.AddDbContext<BbongDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IAccountStore, EfAccountStore>();
builder.Services.AddScoped<ILedgerStore, EfLedgerStore>();
builder.Services.AddScoped<IAdRewardStore, EfAdRewardStore>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddScoped<IMatchStore, EfMatchStore>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<ShopService>();
builder.Services.AddScoped<MatchService>();
builder.Services.AddSingleton<BbongServer.Realtime.RoomRegistry>(); // 친구방(인메모리, 단일 프로세스)
builder.Services.AddSingleton<BbongServer.Realtime.IStakeBank, ScopedStakeBank>(); // 판돈 방 자금 흐름(§9)
// 정산이 오지 않은 입장료 회수 — 크래시·비정상 종료로 묶인 포인트를 돌려준다
builder.Services.AddHostedService<BbongServer.Infrastructure.UnsettledStakeService>();
builder.Services.AddSingleton<BbongServer.Realtime.IGameHistoryStore,
    BbongServer.Infrastructure.Persistence.ScopedGameHistoryStore>(); // 게임 히스토리(CS/디버깅)

// 소셜 검증기: 개발은 bypass(앱 등록 전), 운영은 실제 provider 검증기로 교체 예정.
var socialBypass = string.Equals(
    Environment.GetEnvironmentVariable("BBONG_SOCIAL_DEV_BYPASS"), "true", StringComparison.OrdinalIgnoreCase);
builder.Services.AddSingleton<ISocialTokenVerifier>(_ =>
    socialBypass ? new DevBypassSocialVerifier() : new NotConfiguredSocialVerifier());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = jwt.ValidationParameters();
        // 브라우저(WebGL) WebSocket은 Authorization 헤더를 못 실음 → /ws 한정 쿼리 토큰 병행 허용
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/ws"))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// WebGL(GitHub Pages) 클라 허용. 쿠키 미사용(Bearer 토큰)이라 AnyOrigin 무방.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// enum을 JSON 문자열로(요청의 provider="Google" 바인딩, 응답 가독성)
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// 시작 시 마이그레이션 적용(통합 테스트는 DbContext를 교체하므로 null → 스킵).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetService<BbongDbContext>()?.Database.Migrate();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets();
// ── 게임 히스토리 조회(CS/디버깅) ──
app.MapGet("/me/games", async (ClaimsPrincipal user, HttpContext ctx) =>
{
    var db = ctx.RequestServices.GetService<BbongServer.Infrastructure.Persistence.BbongDbContext>();
    if (db is null)
    {
        return Results.Ok(Array.Empty<object>()); // 인메모리 구성(테스트)에선 히스토리 비활성
    }

    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub)!);
    var games = await (
        from p in db.GamePlayers
        join g in db.Games on p.GameId equals g.Id
        where p.UserId == userId
        orderby g.StartedAtUtc descending
        select new
        {
            gameId = g.Id,
            g.RoomCode,
            g.Stake,
            g.TargetPlayers,
            g.StartedAtUtc,
            g.EndedAtUtc,
            g.WinnerSeats,
            mySeat = p.Seat,
            myFinalDebt = p.FinalDebt,
            myPayout = p.Payout
        }).Take(50).ToListAsync();
    return Results.Ok(games);
}).RequireAuthorization();

// 내 전적(맞춤게임만 집계 — 친구방은 상대를 고를 수 있어 승률이 의미를 잃는다)
app.MapGet("/me/stats", async (ClaimsPrincipal user, HttpContext ctx) =>
{
    var db = ctx.RequestServices.GetService<BbongServer.Infrastructure.Persistence.BbongDbContext>();
    if (db is null)
    {
        return Results.Ok(BbongServer.Infrastructure.Persistence.PlayerStats.Empty);
    }

    return Results.Ok(await BbongServer.Infrastructure.Persistence.PlayerStats.ForAsync(db, CurrentUserId(user)));
}).RequireAuthorization();

app.MapGet("/games/{gameId:guid}/events", async (Guid gameId, ClaimsPrincipal user, HttpContext ctx) =>
{
    var db = ctx.RequestServices.GetService<BbongServer.Infrastructure.Persistence.BbongDbContext>();
    if (db is null)
    {
        return Results.NotFound();
    }

    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub)!);
    var participated = await db.GamePlayers.AnyAsync(p => p.GameId == gameId && p.UserId == userId);
    if (!participated)
    {
        return Results.NotFound(); // 본인이 참여한 게임만 조회 가능
    }

    var players = await db.GamePlayers.Where(p => p.GameId == gameId)
        .OrderBy(p => p.Seat)
        .Select(p => new { p.Seat, p.Nickname, p.IsBot, p.FinalDebt, p.Payout })
        .ToListAsync();
    var events = await db.GameEvents.Where(e => e.GameId == gameId)
        .OrderBy(e => e.Id)
        .Select(e => new { e.RoundIndex, e.Seat, e.Type, e.DataJson, e.AtUtc })
        .ToListAsync();
    return Results.Ok(new { players, events });
}).RequireAuthorization();

BbongServer.Realtime.WsEndpoint.Map(app); // /ws — 친구방 실시간(JWT 재사용)

// 게스트 등록 → 계정 생성 + 초기 지급 + 액세스 토큰 발급
app.MapPost("/auth/guest", async (AccountService accounts, ITokenIssuer tokens) =>
{
    var registration = await accounts.RegisterGuestAsync();
    var account = registration.Account;
    return Results.Ok(new
    {
        accessToken = tokens.IssueAccessToken(account.Id),
        userId = account.Id,
        nickname = account.Nickname,
        resumeSecret = registration.ResumeSecret // 기기에 보관 — 재설치 전까지 같은 계정으로 복귀
    });
});

// 기기에 보관된 자격으로 계정 복귀 → 새 액세스 토큰 발급(게스트 계정이 재시작마다 갈리는 것 방지)
app.MapPost("/auth/resume", async (ResumeRequest req, AccountService accounts, ITokenIssuer tokens) =>
{
    var account = await accounts.ResumeGuestAsync(req.UserId, req.ResumeSecret);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        accessToken = tokens.IssueAccessToken(account.Id),
        userId = account.Id,
        nickname = account.Nickname
    });
});

// 계정 삭제 요청/취소 — 스토어 정책상 앱 안에 삭제 경로가 있어야 한다.
// 즉시 지우지 않고 표시만 남겨 유예 기간 내 복구가 가능하고, 정산 기록은 보존된다.
app.MapPost("/me/deletion", async (ClaimsPrincipal user, AccountService accounts) =>
{
    await accounts.RequestDeletionAsync(CurrentUserId(user));
    return Results.Ok(new { status = "PendingDeletion" });
}).RequireAuthorization();

app.MapDelete("/me/deletion", async (ClaimsPrincipal user, AccountService accounts) =>
{
    await accounts.CancelDeletionAsync(CurrentUserId(user));
    return Results.Ok(new { status = "Active" });
}).RequireAuthorization();

// 소셜 로그인 → 기존 계정 반환 또는 신규 생성 + 토큰 발급
app.MapPost("/auth/social", async (SocialLoginRequest req, AccountService accounts, ITokenIssuer tokens) =>
{
    try
    {
        var account = await accounts.LoginWithSocialAsync(req.Provider, req.IdToken);
        return Results.Ok(new
        {
            accessToken = tokens.IssueAccessToken(account.Id),
            userId = account.Id,
            nickname = account.Nickname,
            isGuest = account.IsGuest
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// 게스트 → 소셜 승격(기존 id·잔액 유지). 로그인 필요.
app.MapPost("/auth/link", async (ClaimsPrincipal user, SocialLoginRequest req, AccountService accounts, ITokenIssuer tokens) =>
{
    var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    try
    {
        var account = await accounts.LinkSocialAsync(userId, req.Provider, req.IdToken);
        return Results.Ok(new
        {
            userId = account.Id,
            nickname = account.Nickname,
            isGuest = account.IsGuest,
            provider = account.Provider
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

// 내 프로필 + 잔액(로그인 직후 로드)
app.MapGet("/me", async (ClaimsPrincipal user, IAccountStore accounts, ILedgerStore ledger) =>
{
    var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    var account = await accounts.GetByIdAsync(userId);
    if (account is null)
    {
        return Results.NotFound();
    }

    var wallet = await ledger.LoadWalletAsync(userId);
    return Results.Ok(new
    {
        userId = account.Id,
        nickname = account.Nickname,
        isGuest = account.IsGuest,
        balance = wallet.Balance
    });
}).RequireAuthorization();

// 닉네임 변경
app.MapPatch("/me/nickname", async (ClaimsPrincipal user, RenameRequest req, AccountService accounts) =>
{
    var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    try
    {
        var account = await accounts.RenameAsync(userId, req.Nickname);
        return Results.Ok(new { nickname = account.Nickname });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }
}).RequireAuthorization();

// 광고 보상 수령(시청 완료 가정 — 실제 SSV 서버검증은 후속). 새 잔액 반환.
app.MapPost("/shop/ad-reward", async (ClaimsPrincipal user, AdRewardRequest req, ShopService shop, ILedgerStore ledger) =>
{
    var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    try
    {
        if (req.Kind == AdRewardKind.Standard)
        {
            await shop.ClaimStandardAsync(userId);
        }
        else
        {
            await shop.ClaimBankruptcyAsync(userId);
        }

        var wallet = await ledger.LoadWalletAsync(userId);
        return Results.Ok(new { balance = wallet.Balance });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

// 매치 시작: 판돈 에스크로 차감 후 매치 생성(싱글 봇전, 정산은 /match/{id}/result)
app.MapPost("/match/start", async (ClaimsPrincipal user, MatchStartRequest req, MatchService matches) =>
{
    var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    try
    {
        var (matchId, balance) = await matches.StartAsync(userId, req.Stake, req.PlayerCount);
        return Results.Ok(new { matchId, balance });
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

// 매치 정산: 승리 시 몫 적립(공동 1등 절사), 1회만. 남의/없는 매치는 404.
app.MapPost("/match/{id:guid}/result", async (ClaimsPrincipal user, Guid id, MatchResultRequest req, MatchService matches) =>
{
    var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    try
    {
        var (payout, balance) = await matches.SettleAsync(userId, id, req.Won, req.WinnersCount);
        return Results.Ok(new { payout, balance });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.Run();

/// <summary>클레임에서 현재 유저 id 추출(엔드포인트 공통).</summary>
static Guid CurrentUserId(ClaimsPrincipal user) =>
    Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);


/// <summary>소셜 로그인/승격 요청 본문.</summary>
public sealed record SocialLoginRequest(SocialProvider Provider, string IdToken);

/// <summary>광고 보상 수령 요청 본문(Standard | Bankruptcy).</summary>
public sealed record AdRewardRequest(AdRewardKind Kind);

/// <summary>닉네임 변경 요청 본문.</summary>
public sealed record RenameRequest(string Nickname);

/// <summary>기기 재개 요청 본문(앱이 보관한 userId + 발급받은 재개 자격).</summary>
public sealed record ResumeRequest(Guid UserId, string ResumeSecret);

/// <summary>매치 시작 요청 본문(판돈은 GameConfig.StakeOptions 중 하나).</summary>
public sealed record MatchStartRequest(int Stake, int PlayerCount);

/// <summary>매치 정산 요청 본문(공동 1등 수 포함 — 몫은 절사).</summary>
public sealed record MatchResultRequest(bool Won, int WinnersCount);

// 통합 테스트(WebApplicationFactory)에서 진입점 참조용
public partial class Program;
