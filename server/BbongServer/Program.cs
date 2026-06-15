using System;
using System.Security.Claims;
using BbongServer.Application;
using BbongServer.Infrastructure.Auth;
using BbongServer.Infrastructure.InMemory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

var builder = WebApplication.CreateBuilder(args);

// JWT 설정: appsettings → 환경변수 → (개발 전용) fallback. 프로덕션은 BBONG_JWT_KEY 필수.
var jwt = new JwtSettings();
builder.Configuration.GetSection("Jwt").Bind(jwt);
jwt.Key = builder.Configuration["Jwt:Key"]
          ?? Environment.GetEnvironmentVariable("BBONG_JWT_KEY")
          ?? "dev-only-insecure-signing-key-change-me-32+bytes";

builder.Services.AddSingleton(jwt);
// 첫 골격: 인메모리 저장소(싱글톤으로 상태 유지). 후속 EF Core + PostgreSQL로 교체.
builder.Services.AddSingleton<IAccountStore, InMemoryAccountStore>();
builder.Services.AddSingleton<ILedgerStore, InMemoryLedgerStore>();
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddScoped<AccountService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = jwt.ValidationParameters());
builder.Services.AddAuthorization();

var app = builder.Build();
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

app.Run();

// 통합 테스트(WebApplicationFactory)에서 진입점 참조용
public partial class Program;
