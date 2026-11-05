using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plutus.Application.Accounts;

namespace Plutus.Application;

public static class DependencyInjection
{
    /// <remarks>
    /// Os casos de uso são Scoped porque dependem do repositório e da unidade de trabalho, que terão uma
    /// conexão por operação. O relógio é Singleton: não guarda estado e pode ser compartilhado.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<LedgerOptions>()
            .Validate(
                options => options.DefaultCurrency.Length == 3
                    && options.MaxOverdraftLimit >= 0m
                    && options.MaxStatementDays > 0,
                "Configuração inválida na seção Ledger.")
            .ValidateOnStart();

        services.AddScoped<OpenAccount>();
        services.AddScoped<DepositMoney>();
        services.AddScoped<WithdrawMoney>();
        services.AddScoped<TransferMoney>();
        services.AddScoped<GetStatement>();

        return services;
    }
}
