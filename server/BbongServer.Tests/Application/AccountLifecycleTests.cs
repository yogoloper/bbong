using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Accounts;
using BbongServer.Domain.Auth;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

/// <summary>
/// 계정 생애주기 — 마지막 접속 시각과 상태(정상/정지/탈퇴). 스토어 정책상 계정 삭제 경로가
/// 필요하고, CS 대응에는 "마지막으로 언제 들어왔나"가 있어야 한다. 둘 다 사건 시점에
/// 남기지 않으면 사후에 복원할 수 없다.
/// </summary>
[TestFixture]
public class AccountLifecycleTests
{
    private static readonly DateTimeOffset Created = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private AccountService _service = null!;
    private InMemoryAccountStore _accounts = null!;
    private StubClock _clock = null!;

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private sealed class UnusedSocialVerifier : ISocialTokenVerifier
    {
        public Task<SocialIdentity> VerifyAsync(SocialProvider provider, string idToken) =>
            throw new NotSupportedException();
    }

    [SetUp]
    public void SetUp()
    {
        _accounts = new InMemoryAccountStore();
        _clock = new StubClock();
        _service = new AccountService(_accounts, new InMemoryLedgerStore(), new UnusedSocialVerifier(), _clock);
    }

    [Test]
    public void A_new_account_is_active()
    {
        var account = UserAccount.NewGuest(Guid.NewGuid(), "새 계정", Created);

        Assert.That(account.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That(account.DeletionRequestedAt, Is.Null);
    }

    [Test]
    public async Task Registering_records_the_first_login()
    {
        var registration = await _service.RegisterGuestAsync();

        Assert.That(registration.Account.LastLoginAt, Is.EqualTo(Now));
    }

    [Test]
    public async Task Resuming_updates_the_last_login()
    {
        var registration = await _service.RegisterGuestAsync();
        _clock.UtcNow = Now.AddDays(3);

        await _service.ResumeGuestAsync(registration.Account.Id, registration.ResumeSecret);

        var stored = await _accounts.GetByIdAsync(registration.Account.Id);
        Assert.That(stored!.LastLoginAt, Is.EqualTo(Now.AddDays(3)));
    }

    [Test]
    public async Task Requesting_deletion_marks_the_account_and_the_time()
    {
        var registration = await _service.RegisterGuestAsync();

        await _service.RequestDeletionAsync(registration.Account.Id);

        var stored = await _accounts.GetByIdAsync(registration.Account.Id);
        Assert.That(stored!.Status, Is.EqualTo(AccountStatus.PendingDeletion));
        Assert.That(stored.DeletionRequestedAt, Is.EqualTo(Now));
    }

    /// <summary>탈퇴를 요청한 계정으로는 다시 들어올 수 없어야 한다.</summary>
    [Test]
    public async Task A_deleted_account_cannot_resume()
    {
        var registration = await _service.RegisterGuestAsync();
        await _service.RequestDeletionAsync(registration.Account.Id);

        var resumed = await _service.ResumeGuestAsync(registration.Account.Id, registration.ResumeSecret);

        Assert.That(resumed, Is.Null);
    }

    [Test]
    public async Task A_suspended_account_cannot_resume()
    {
        var registration = await _service.RegisterGuestAsync();
        registration.Account.Suspend();
        await _accounts.SaveAsync(registration.Account);

        var resumed = await _service.ResumeGuestAsync(registration.Account.Id, registration.ResumeSecret);

        Assert.That(resumed, Is.Null);
    }

    /// <summary>탈퇴 요청은 되돌릴 수 있어야 한다(오조작·유예 기간 내 복구).</summary>
    [Test]
    public async Task Deletion_can_be_cancelled()
    {
        var registration = await _service.RegisterGuestAsync();
        await _service.RequestDeletionAsync(registration.Account.Id);

        await _service.CancelDeletionAsync(registration.Account.Id);

        var stored = await _accounts.GetByIdAsync(registration.Account.Id);
        Assert.That(stored!.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That(stored.DeletionRequestedAt, Is.Null);
    }
}
