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

    public DbSet<AccountSocialRow> AccountSocials => Set<AccountSocialRow>();

    public DbSet<LedgerRow> Ledger => Set<LedgerRow>();

    public DbSet<AdRewardRow> AdRewards => Set<AdRewardRow>();

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<GameRow> Games => Set<GameRow>();

    public DbSet<GamePlayerRow> GamePlayers => Set<GamePlayerRow>();

    public DbSet<GameEventRow> GameEvents => Set<GameEventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(account =>
        {
            account.ToTable("accounts");
            account.HasKey(a => a.Id);
            account.Property(a => a.Nickname).HasMaxLength(BbongCore.Config.GameConfig.MaxNicknameLength);
            account.Property(a => a.CreatedAt);
            account.Ignore(a => a.Provider);       // account_socials의 첫 항목에서 파생
            account.Ignore(a => a.SocialSubject);
            account.Ignore(a => a.Socials);        // 별도 테이블로 읽고 쓴다
            account.Property(a => a.ResumeSecretHash); // 기기 재개 자격(해시만 보관)
            account.Property(a => a.Status).HasConversion<string>();
            account.Property(a => a.LastLoginAt);
            account.Property(a => a.DeletionRequestedAt);
            // 탈퇴 유예 만료 처리와 휴면 계정 조회
            account.HasIndex(a => new { a.Status, a.DeletionRequestedAt });
            account.Ignore(a => a.IsGuest); // 계산 속성(Provider null 여부)

        });

        modelBuilder.Entity<AccountSocialRow>(social =>
        {
            social.ToTable("account_socials");
            social.HasKey(e => e.Id);
            social.Property(e => e.Id).ValueGeneratedOnAdd();
            social.Property(e => e.Provider).HasConversion<string>();
            social.HasIndex(e => e.AccountId);
            // 하나의 소셜 신원은 한 계정에만 붙는다
            social.HasIndex(e => new { e.Provider, e.Subject }).IsUnique();
            // 한 계정에 같은 provider를 두 번 붙일 수 없다
            social.HasIndex(e => new { e.AccountId, e.Provider }).IsUnique();
        });

        modelBuilder.Entity<LedgerRow>(ledger =>
        {
            ledger.ToTable("ledger");
            ledger.HasKey(e => e.Id);
            ledger.Property(e => e.Id).ValueGeneratedOnAdd();
            ledger.Property(e => e.Reason).HasConversion<string>(); // enum을 가독성 위해 문자열로
            ledger.Property(e => e.Kind).HasConversion<string>();
            ledger.Property(e => e.OccurredAt);
            ledger.Property(e => e.BalanceAfter);
            ledger.Property(e => e.RefType);
            ledger.Property(e => e.RefId);
            ledger.HasIndex(e => e.UserId); // 잔액 계산 = 유저별 조회
            // 유저별 최신 행(현재 잔액)과 기간 조회를 한 인덱스로 처리
            ledger.HasIndex(e => new { e.UserId, e.OccurredAt });
            // 게임별 에스크로 ↔ 배당 대조(정산 누락 감지)
            ledger.HasIndex(e => new { e.RefType, e.RefId }).HasFilter("\"RefId\" IS NOT NULL");
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
