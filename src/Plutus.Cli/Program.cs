using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plutus.Application;
using Plutus.Cli;
using Plutus.Infrastructure;
using Plutus.Infrastructure.Persistence;
using Spectre.Console;

// Composition root: o único lugar que conhece todas as camadas.
// O Generic Host traz configuração (appsettings.json + appsettings.{Ambiente}.json + variáveis de ambiente),
// injeção de dependência e logging, os mesmos de uma API ASP.NET Core.
Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Plutus")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Plutus no appsettings.json.");

builder.Services.AddOptions<LedgerOptions>().Bind(builder.Configuration.GetSection(LedgerOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddSingleton(AnsiConsole.Console);
builder.Services.AddSingleton<LedgerConsole>();

using var host = builder.Build();

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
await host.StartAsync(lifetime.ApplicationStopping);

var applied = await DatabaseMigrator.MigrateAsync(connectionString, lifetime.ApplicationStopping);
if (applied.Count > 0)
{
    AnsiConsole.MarkupLineInterpolated($"[grey]Migrations aplicadas: {string.Join(", ", applied)}[/]");
}

await host.Services.GetRequiredService<LedgerConsole>().RunAsync(lifetime.ApplicationStopping);
await host.StopAsync();
