using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Infrastructure;
using BbongServer.Domain.Wallet;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

[TestFixture]
public class AccountServiceTests
{
    private AccountService _service = null!;
    private InMemoryAccountStore _accounts = null!;
    private InMemoryLedgerStore _ledger = null!;

    [SetUp]
    public void SetUp()
    {
        _accounts = new InMemoryAccountStore();
        _ledger = new InMemoryLedgerStore();
        _service = new AccountService(_accounts, _ledger, new StubSocialVerifier(), new SystemClock());
    }

    // 게스트 등록 테스트는 소셜을 안 쓰므로 호출되면 실패하는 스텁.
    private sealed class StubSocialVerifier : BbongServer.Application.ISocialTokenVerifier
    {
        public System.Threading.Tasks.Task<BbongServer.Domain.Auth.SocialIdentity> VerifyAsync(
            BbongServer.Domain.Auth.SocialProvider provider, string idToken) =>
            throw new System.NotSupportedException();
    }

    [Test]
    public async Task RegisterGuest_creates_guest_account()
    {
        var account = (await _service.RegisterGuestAsync()).Account;

        Assert.That(account.IsGuest, Is.True);
        Assert.That(account.Id, Is.Not.EqualTo(System.Guid.Empty));
        Assert.That(await _accounts.GetByIdAsync(account.Id), Is.Not.Null);
    }

    [Test]
    public async Task RegisterGuest_grants_starting_balance()
    {
        var account = (await _service.RegisterGuestAsync()).Account;

        var wallet = await _ledger.LoadWalletAsync(account.Id);
        Assert.That(wallet.Balance, Is.EqualTo(AccountService.StartingGrant));
        Assert.That(wallet.Entries, Has.Count.EqualTo(1));
        Assert.That(wallet.Entries[0].Reason, Is.EqualTo(LedgerReason.Welcome));
    }

    [Test]
    public async Task RegisterGuest_assigns_valid_nickname()
    {
        var account = (await _service.RegisterGuestAsync()).Account;

        Assert.That(BbongCore.Config.GameConfig.IsValidNickname(account.Nickname), Is.True);
    }

    [Test]
    public async Task RegisterGuest_assigns_unique_ids_and_nicknames()
    {
        var a = (await _service.RegisterGuestAsync()).Account;
        var b = (await _service.RegisterGuestAsync()).Account;

        Assert.That(a.Id, Is.Not.EqualTo(b.Id));
        Assert.That(a.Nickname, Is.Not.EqualTo(b.Nickname));
    }
}
