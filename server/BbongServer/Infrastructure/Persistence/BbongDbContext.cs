using BbongServer.Domain.Accounts;
using BbongServer.Domain.Matches;
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

    public DbSet<AdRewardRow> AdRewards => Set<AdRewardRow>();

    public DbSet<Match> Matches => Set<Match>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(account =>
        {
            account.ToTable("accounts");
            account.HasKey(a => a.Id);
            account.Property(a => a.Nickname).HasMaxLength(BbongCore.Config.GameConfig.MaxNicknameLength);
            account.Property(a => a.CreatedAt);
            account.Property(a => a.Provider).HasConversion<string>(); // enum → 문자열(nullable)
            account.Property(a => a.SocialSubject);
            account.Ignore(a => a.IsGuest); // 계산 속성(Provider null 여부)
            // 같은 (provider, subject)는 한 계정만 — 소셜 계정에만 적용(부분 인덱스)
            account.HasIndex(a => new { a.Provider, a.SocialSubject })
                .IsUnique()
                .HasFilter("\"Provider\" IS NOT NULL");
        });

        modelBuilder.Entity<LedgerRow>(ledger =>
        {
            ledger.ToTable("ledger");
            ledger.HasKey(e => e.Id);
            ledger.Property(e => e.Id).ValueGeneratedOnAdd();
            ledger.Property(e => e.Reason).HasConversion<string>(); // enum을 가독성 위해 문자열로
            ledger.HasIndex(e => e.UserId); // 잔액 계산 = 유저별 조회
        });

        modelBuilder.Entity<AdRewardRow>(reward =>
        {
            reward.ToTable("ad_rewards");
            reward.HasKey(e => e.Id);
            reward.Property(e => e.Id).ValueGeneratedOnAdd();
            reward.Property(e => e.Kind).HasConversion<string>();
            reward.HasIndex(e => new { e.UserId, e.Kind, e.ClaimedAt }); // 쿨다운·일일 제한 조회
        });

        modelBuilder.Entity<Match>(match =>
        {
            match.ToTable("matches");
            match.HasKey(m => m.Id);
            match.Property(m => m.Stake);       // get-only 프로퍼티는 명시 매핑(생성자 바인딩용)
            match.Property(m => m.PlayerCount);
            match.Property(m => m.CreatedAt);
            match.Property(m => m.Status).HasConversion<string>();
            match.HasIndex(m => m.UserId); // 유저별 매치 조회
        });
    }
}
