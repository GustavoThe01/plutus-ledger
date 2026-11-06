using Microsoft.Extensions.DependencyInjection;
using Plutus.Application.Abstractions;
using Plutus.Infrastructure.Persistence;

namespace Plutus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddScoped(_ => new DbSession(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAccountRepository, AccountRepository>();

        return services;
    }
}
