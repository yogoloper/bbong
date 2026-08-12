using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Auth;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

[TestFixture]
public class SocialLoginTests
{
    private AccountService _service = null!;
    private InMemoryAccountStore _accounts = null!;
    private InMemoryLedgerStore _ledger = null!;

    // 검증 우회 fake: idToken을 그대로 subject로 사용(BBONG_SOCIAL_DEV_BYPASS 모사).
    private sealed class FakeVerifier : ISocialTokenVerifier
    {
        public Task<SocialIdentity> VerifyAsync(SocialProvider provider, string idToken) =>
            Task.FromResult(new SocialIdentity(provider, idToken));
    }

    [SetUp]
    public void SetUp()
    {
        _accounts = new InMemoryAccountStore();
        _ledger = new InMemoryLedgerStore();
        _service = new AccountService(_accounts, _ledger, new FakeVerifier());
    }

    [Test]
    public async Task Social_login_creates_new_account_with_grant()
    {
        var account = await _service.LoginWithSocialAsync(SocialProvider.Google, "google-sub-1");

        Assert.That(account.IsGuest, Is.False);
        Assert.That(account.Provider, Is.EqualTo(SocialProvider.Google));
        Assert.That(account.SocialSubject, Is.EqualTo("google-sub-1"));
        var wallet = await _ledger.LoadWalletAsync(account.Id);
        Assert.That(wallet.Balance, Is.EqualTo(AccountService.StartingGrant));
    }

    [Test]
    public async Task Social_login_returns_existing_account_for_same_identity()
    {
        var first = await _service.LoginWithSocialAsync(SocialProvider.Kakao, "kakao-42");
        var second = await _service.LoginWithSocialAsync(SocialProvider.Kakao, "kakao-42");

        Assert.That(second.Id, Is.EqualTo(first.Id)); // 중복 생성 안 함
    }

    [Test]
    public async Task Same_subject_on_different_providers_are_distinct_accounts()
    {
        var google = await _service.LoginWithSocialAsync(SocialProvider.Google, "same-sub");
        var kakao = await _service.LoginWithSocialAsync(SocialProvider.Kakao, "same-sub");

        Assert.That(google.Id, Is.Not.EqualTo(kakao.Id));
    }

    [Test]
    public async Task Link_promotes_guest_keeping_id_and_balance()
    {
        var guest = (await _service.RegisterGuestAsync()).Account;

        var linked = await _service.LinkSocialAsync(guest.Id, SocialProvider.Apple, "apple-sub");

        Assert.That(linked.Id, Is.EqualTo(guest.Id));       // 같은 계정
        Assert.That(linked.IsGuest, Is.False);
        Assert.That(linked.Provider, Is.EqualTo(SocialProvider.Apple));
        var wallet = await _ledger.LoadWalletAsync(guest.Id);
        Assert.That(wallet.Balance, Is.EqualTo(AccountService.StartingGrant)); // 잔액 유지(중복 지급 없음)
    }

    [Test]
    public async Task Link_fails_when_social_already_used_by_another_account()
    {
        await _service.LoginWithSocialAsync(SocialProvider.Google, "taken-sub");
        var guest = (await _service.RegisterGuestAsync()).Account;

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.LinkSocialAsync(guest.Id, SocialProvider.Google, "taken-sub"));
    }

    [Test]
    public async Task Link_fails_when_account_is_already_social()
    {
        var social = await _service.LoginWithSocialAsync(SocialProvider.Google, "g-1");

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.LinkSocialAsync(social.Id, SocialProvider.Kakao, "k-1"));
    }
}
