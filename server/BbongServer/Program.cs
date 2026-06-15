using System;
using System.IO;
using System.Security.Claims;
using System.Text.Json.Serialization;
using BbongServer.Application;
using BbongServer.Domain.Auth;
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
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddScoped<AccountService>();

// 소셜 검증기: 개발은 bypass(앱 등록 전), 운영은 실제 provider 검증기로 교체 예정.
var socialBypass = string.Equals(
    Environment.GetEnvironmentVariable("BBONG_SOCIAL_DEV_BYPASS"), "true", StringComparison.OrdinalIgnoreCase);
builder.Services.AddSingleton<ISocialTokenVerifier>(_ =>
    socialBypass ? new DevBypassSocialVerifier() : new NotConfiguredSocialVerifier());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = jwt.ValidationParameters());
builder.Services.AddAuthorization();

// enum을 JSON 문자열로(요청의 provider="Google" 바인딩, 응답 가독성)
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// 시작 시 마이그레이션 적용(통합 테스트는 DbContext를 교체하므로 null → 스킵).
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetService<BbongDbContext>()?.Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();

// 게스트 등록 → 계정 생성 + 초기 지급 + 액세스 토큰 발급
app.MapPost("/auth/guest", async (AccountService accounts, ITokenIssuer tokens) =>
{
    var account = await accounts.RegisterGuestAsync();
    return Results.Ok(new
    {
        accessToken = tokens.IssueAccessToken(account.Id),
        userId = account.Id,
        nickname = account.Nickname
    });
});

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

app.Run();

/// <summary>소셜 로그인/승격 요청 본문.</summary>
public sealed record SocialLoginRequest(SocialProvider Provider, string IdToken);

/// <summary>닉네임 변경 요청 본문.</summary>
public sealed record RenameRequest(string Nickname);

// 통합 테스트(WebApplicationFactory)에서 진입점 참조용
public partial class Program;
