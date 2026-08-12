using System;
using System.Threading;
using System.Threading.Tasks;
using BbongServer.Application;
using BbongServer.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BbongServer.Infrastructure;

/// <summary>
/// 미정산 입장료 회수를 주기적으로 돌린다. 서버가 죽거나 방이 비정상 종료되면 에스크로만
/// 남는데, 그대로 두면 유저 포인트가 조용히 사라진다.
/// </summary>
public sealed class UnsettledStakeService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<UnsettledStakeService> _log;

    public UnsettledStakeService(IServiceScopeFactory scopes, ILogger<UnsettledStakeService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 기동 직후엔 지난 크래시의 잔재가 남아 있을 수 있으니 한 번 돌고 시작한다.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var provider = scope.ServiceProvider;
                var sweeper = new UnsettledStakeSweeper(
                    provider.GetRequiredService<ILedgerStore>(),
                    provider.GetRequiredService<IStakeBank>(),
                    provider.GetRequiredService<IClock>());

                var refunded = await sweeper.SweepAsync();
                if (refunded > 0)
                {
                    _log.LogWarning("미정산 입장료 {Count}건 회수", refunded);
                }
            }
            catch (Exception ex)
            {
                // 회수 실패로 서버가 죽으면 안 된다. 다음 주기에 다시 시도한다.
                _log.LogError(ex, "미정산 회수 실패");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
