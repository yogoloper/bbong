using System;
using System.Threading.Tasks;
using BbongServer.Domain.Accounts;
using BbongServer.Domain.Auth;

namespace BbongServer.Application;

/// <summary>계정 영속 저장소. 첫 골격은 인메모리, 후속 EF Core + PostgreSQL.</summary>
public interface IAccountStore
{
    Task SaveAsync(UserAccount account);

    Task<UserAccount?> GetByIdAsync(Guid id);

    /// <summary>(provider, subject)로 기존 소셜 계정 조회. 없으면 null.</summary>
    Task<UserAccount?> GetBySocialAsync(SocialProvider provider, string subject);
}
