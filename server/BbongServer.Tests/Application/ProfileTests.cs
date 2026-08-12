using System;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Infrastructure;
using BbongServer.Domain.Auth;
using BbongServer.Infrastructure.InMemory;
using NUnit.Framework;

namespace BbongServer.Tests.Application;

[TestFixture]
public class ProfileTests
{
    private AccountService _service = null!;
    private InMemoryAccountStore _accounts = null!;

    private sealed class StubVerifier : ISocialTokenVerifier
    {
        public Task<SocialIdentity> VerifyAsync(SocialProvider provider, string idToken) =>
            throw new NotSupportedException();
    }

    [SetUp]
    public void SetUp()
    {
        _accounts = new InMemoryAccountStore();
        _service = new AccountService(_accounts, new InMemoryLedgerStore(), new StubVerifier(), new SystemClock());
    }

    [Test]
    public async Task Rename_updates_nickname()
    {
        var guest = (await _service.RegisterGuestAsync()).Account;

        var updated = await _service.RenameAsync(guest.Id, "용감한 두더지");

        Assert.That(updated.Nickname, Is.EqualTo("용감한 두더지"));
        var reloaded = await _accounts.GetByIdAsync(guest.Id);
        Assert.That(reloaded!.Nickname, Is.EqualTo("용감한 두더지"));
    }

    [Test]
    public async Task Rename_rejects_invalid_nickname()
    {
        var guest = (await _service.RegisterGuestAsync()).Account;

        Assert.ThrowsAsync<ArgumentException>(() => _service.RenameAsync(guest.Id, ""));
        Assert.ThrowsAsync<ArgumentException>(() => _service.RenameAsync(guest.Id, "열세글자가되는닉네임이름임"));
    }

    [Test]
    public void Rename_throws_when_account_missing()
    {
        Assert.ThrowsAsync<InvalidOperationException>(() => _service.RenameAsync(Guid.NewGuid(), "아무개"));
    }
}
