using BbongServer.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace BbongServer.Infrastructure.Persistence;

/// <summary>EF Core DbContext. PostgreSQL. 계정 + 원장(append-only).</summary>
public sealed class BbongDbContext : DbContext
{
    public BbongDbContext(DbContextOptions<BbongDbContext> options) : base(options)
    {
    }

    public DbSet<UserAccount> Accounts => Set<UserAccount>();

    public DbSet<LedgerRow> Ledger => Set<LedgerRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(account =>
        {
            account.ToTable("accounts");
            account.HasKey(a => a.Id);
            account.Property(a => a.Nickname).HasMaxLength(BbongCore.Config.GameConfig.MaxNicknameLength);
            account.Property(a => a.CreatedAt);
        });

        modelBuilder.Entity<LedgerRow>(ledger =>
        {
            ledger.ToTable("ledger");
            ledger.HasKey(e => e.Id);
            ledger.Property(e => e.Id).ValueGeneratedOnAdd();
            ledger.Property(e => e.Reason).HasConversion<string>(); // enum을 가독성 위해 문자열로
            ledger.HasIndex(e => e.UserId); // 잔액 계산 = 유저별 조회
        });
    }
}
