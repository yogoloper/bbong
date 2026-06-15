using System;
using System.Threading.Tasks;
using BbongServer.Domain.Accounts;

namespace BbongServer.Application;

/// <summary>계정 영속 저장소. 첫 골격은 인메모리, 후속 EF Core + PostgreSQL.</summary>
public interface IAccountStore
{
    Task SaveAsync(UserAccount account);

    Task<UserAccount?> GetByIdAsync(Guid id);
}
