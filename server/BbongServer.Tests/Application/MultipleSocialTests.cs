using System;
using System.Linq;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Domain.Accounts;
using BbongServer.Domain.Auth;
using BbongServer.Infrastructure;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

/// <summary>
/// 한 계정에 소셜을 여러 개 연결할 수 있어야 한다. 지금은 컬럼 한 쌍뿐이라 1인 1소셜이고,
/// 구글로 가입한 사람이 나중에 애플을 붙일 수 없다. iOS는 소셜 로그인을 제공하면
/// Sign in with Apple을 함께 제공해야 하므로 출시 전에 필요하다.
/// </summary>
[TestFixture]
public class MultipleSocialTests
{
    private AccountService _service = null!;
    private InMemoryAccountStore _accounts = null!;
    private StubVerifier _verifier = null!;

    private sealed class StubVerifier : ISocialTokenVerifier
    {
        public SocialIdentity Next = new(SocialProvider.Google, "sub-google");

        public Task<SocialIdentity> VerifyAsync(SocialProvider provider, string idToken) =>
            Task.FromResult(Next);
    }

    [SetUp]
    public void SetUp()
    {
        _accounts = new InMemoryAccountStore();
        _verifier = new StubVerifier();
        _service = new AccountService(_accounts, new InMemoryLedgerStore(), _verifier, new SystemClock());
    }

    [Test]
    public async Task A_guest_can_link_two_providers()
    {
        var guest = (await _service.RegisterGuestAsync()).Account;

        _verifier.Next = new SocialIdentity(SocialProvider.Google, "g-1");
        await _service.LinkSocialAsync(guest.Id, SocialProvider.Google, "token");
        _verifier.Next = new SocialIdentity(SocialProvider.Apple, "a-1");
        await _service.LinkSocialAsync(guest.Id, SocialProvider.Apple, "token");

        var stored = await _accounts.GetByIdAsync(guest.Id);
        Assert.That(stored!.Socials.Select(s => s.Provider),
            Is.EquivalentTo(new[] { SocialProvider.Google, SocialProvider.Apple }));
        Assert.That(stored.IsGuest, Is.False);
    }

    [Test]
    public async Task Either_provider_logs_into_the_same_account()
    {
        var guest = (await _service.RegisterGuestAsync()).Account;
        _verifier.Next = new SocialIdentity(SocialProvider.Google, "g-1");
        await _service.LinkSocialAsync(guest.Id, SocialProvider.Google, "token");
        _verifier.Next = new SocialIdentity(SocialProvider.Apple, "a-1");
        await _service.LinkSocialAsync(guest.Id, SocialProvider.Apple, "token");

        _verifier.Next = new SocialIdentity(SocialProvider.Apple, "a-1");
        var viaApple = await _service.LoginWithSocialAsync(SocialProvider.Apple, "token");
        _verifier.Next = new SocialIdentity(SocialProvider.Google, "g-1");
        var viaGoogle = await _service.LoginWithSocialAsync(SocialProvider.Google, "token");

        Assert.That(viaApple.Id, Is.EqualTo(guest.Id));
        Assert.That(viaGoogle.Id, Is.EqualTo(guest.Id));
    }

    [Test]
    public async Task The_same_provider_cannot_be_linked_twice_to_one_account()
    {
        var guest = (await _service.RegisterGuestAsync()).Account;
        _verifier.Next = new SocialIdentity(SocialProvider.Google, "g-1");
        await _service.LinkSocialAsync(guest.Id, SocialProvider.Google, "token");

        _verifier.Next = new SocialIdentity(SocialProvider.Google, "g-2");
        Assert.That(async () => await _service.LinkSocialAsync(guest.Id, SocialProvider.Google, "token"),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task A_social_identity_cannot_belong_to_two_accounts()
    {
        var first = (await _service.RegisterGuestAsync()).Account;
        var second = (await _service.RegisterGuestAsync()).Account;
        _verifier.Next = new SocialIdentity(SocialProvider.Google, "shared");
        await _service.LinkSocialAsync(first.Id, SocialProvider.Google, "token");

        Assert.That(async () => await _service.LinkSocialAsync(second.Id, SocialProvider.Google, "token"),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task Signing_up_with_a_social_account_records_the_link()
    {
        _verifier.Next = new SocialIdentity(SocialProvider.Kakao, "k-1");

        var account = await _service.LoginWithSocialAsync(SocialProvider.Kakao, "token");

        Assert.That(account.Socials, Has.Count.EqualTo(1));
        Assert.That(account.Socials[0].Subject, Is.EqualTo("k-1"));
        Assert.That(account.IsGuest, Is.False);
    }
}
