using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Auth;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

/// <summary>
/// 게스트 재개 — 앱을 껐다 켜도 같은 계정으로 돌아와야 한다.
/// 토큰(60분)만 저장하면 만료 후 새 계정이 생겨 포인트가 사라지므로, 기기에 보관할
/// 장기 자격이 따로 필요하다.
/// </summary>
[TestFixture]
public class GuestResumeTests
{
    private AccountService _service = null!;
    private InMemoryAccountStore _accounts = null!;

    [SetUp]
    public void SetUp()
    {
        _accounts = new InMemoryAccountStore();
        _service = new AccountService(_accounts, new InMemoryLedgerStore(), new UnusedSocialVerifier());
    }

    private sealed class UnusedSocialVerifier : ISocialTokenVerifier
    {
        public Task<SocialIdentity> VerifyAsync(SocialProvider provider, string idToken) =>
            throw new NotSupportedException();
    }

    [Test]
    public async Task Guest_registration_issues_a_resume_secret()
    {
        var registration = await _service.RegisterGuestAsync();

        Assert.That(registration.ResumeSecret, Is.Not.Null.And.Not.Empty);
        Assert.That(registration.Account.IsGuest, Is.True);
    }

    [Test]
    public async Task Resume_with_correct_secret_returns_the_same_account()
    {
        var registration = await _service.RegisterGuestAsync();

        var resumed = await _service.ResumeGuestAsync(registration.Account.Id, registration.ResumeSecret);

        Assert.That(resumed, Is.Not.Null);
        Assert.That(resumed!.Id, Is.EqualTo(registration.Account.Id));
        Assert.That(resumed.Nickname, Is.EqualTo(registration.Account.Nickname));
    }

    [Test]
    public async Task Resume_with_wrong_secret_is_rejected()
    {
        var registration = await _service.RegisterGuestAsync();

        var resumed = await _service.ResumeGuestAsync(registration.Account.Id, "틀린-시크릿");

        Assert.That(resumed, Is.Null);
    }

    [Test]
    public async Task Resume_for_unknown_account_is_rejected()
    {
        var resumed = await _service.ResumeGuestAsync(Guid.NewGuid(), "아무-시크릿");

        Assert.That(resumed, Is.Null);
    }

    [Test]
    public async Task Secret_is_not_stored_in_plaintext()
    {
        var registration = await _service.RegisterGuestAsync();

        var stored = await _accounts.GetByIdAsync(registration.Account.Id);

        Assert.That(stored!.ResumeSecretHash, Is.Not.Null);
        Assert.That(stored.ResumeSecretHash, Is.Not.EqualTo(registration.ResumeSecret));
    }

    [Test]
    public async Task Each_guest_gets_a_distinct_secret()
    {
        var first = await _service.RegisterGuestAsync();
        var second = await _service.RegisterGuestAsync();

        Assert.That(first.ResumeSecret, Is.Not.EqualTo(second.ResumeSecret));
    }

    /// <summary>소셜로 승격한 계정도 기기에 남은 게스트 자격으로 계속 들어올 수 있어야 한다.</summary>
    [Test]
    public async Task Resume_still_works_after_social_link()
    {
        var registration = await _service.RegisterGuestAsync();
        registration.Account.LinkSocial(new SocialIdentity(SocialProvider.Google, "sub-1"));
        await _accounts.SaveAsync(registration.Account);

        var resumed = await _service.ResumeGuestAsync(registration.Account.Id, registration.ResumeSecret);

        Assert.That(resumed, Is.Not.Null);
        Assert.That(resumed!.Id, Is.EqualTo(registration.Account.Id));
    }
}
