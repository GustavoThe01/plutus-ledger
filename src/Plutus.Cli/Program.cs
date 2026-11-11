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

// A configuração é lida da pasta do executável, e não da pasta de onde ele foi chamado:
// assim o plutus.exe publicado funciona de qualquer lugar, com o appsettings.json ao lado dele.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

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

// O menu usa as setas do teclado; com a entrada redirecionada não há como escolher uma opção.
if (!AnsiConsole.Profile.Capabilities.Interactive)
{
    AnsiConsole.MarkupLine("[red]O Plutus precisa de um terminal interativo (a entrada não pode vir de um arquivo ou pipe).[/]");
    await host.StopAsync();
    return 1;
}

await host.Services.GetRequiredService<LedgerConsole>().RunAsync(lifetime.ApplicationStopping);
await host.StopAsync();
return 0;
