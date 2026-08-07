using System;
using System.Threading.Tasks;
using BbongServer.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace BbongServer.Application;

/// <summary>
/// 방 루프는 요청 스코프 밖(장수명)이라, 호출마다 DI 스코프를 열어 원장 은행을 실행한다.
/// </summary>
public sealed class ScopedStakeBank : IStakeBank
{
    private readonly IServiceScopeFactory _scopes;

    public ScopedStakeBank(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<bool> TryEscrowAsync(Guid userId, int stake)
    {
        using var scope = _scopes.CreateScope();
        return await Bank(scope).TryEscrowAsync(userId, stake);
    }

    public async Task RefundAsync(Guid userId, int stake)
    {
        using var scope = _scopes.CreateScope();
        await Bank(scope).RefundAsync(userId, stake);
    }

    public async Task PayoutAsync(Guid userId, long amount)
    {
        using var scope = _scopes.CreateScope();
        await Bank(scope).PayoutAsync(userId, amount);
    }

    private static LedgerStakeBank Bank(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<ILedgerStore>());
}
