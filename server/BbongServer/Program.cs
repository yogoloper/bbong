using System;
using System.IO;
using System.Security.Claims;
using BbongServer.Application;
using BbongServer.Infrastructure.Auth;
using BbongServer.Infrastructure.Persistence;
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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = jwt.ValidationParameters());
builder.Services.AddAuthorization();

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
