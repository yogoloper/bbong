using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>
/// EF 디자인타임(migrations add/update)용 DbContext 팩토리.
/// 이게 있으면 EF 도구가 Program 호스트를 실행하지 않아 시작 시 Migrate()를 거치지 않음.
/// </summary>
public sealed class BbongDbContextFactory : IDesignTimeDbContextFactory<BbongDbContext>
{
    public BbongDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BBONG_DB_CONN")
            ?? "Host=localhost;Port=5432;Database=bbong;Username=bbong;Password=bbong_dev";

        var options = new DbContextOptionsBuilder<BbongDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BbongDbContext(options);
    }
}
