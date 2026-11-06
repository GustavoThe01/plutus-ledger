using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Plutus.Infrastructure.Persistence;

/// <summary>
/// Aplica, em ordem, os scripts de <c>Migrations/*.sql</c> embutidos no assembly e registra o que já rodou.
/// </summary>
/// <remarks>Cada script roda na própria transação: se falhar, nem ele nem o registro ficam pela metade.</remarks>
public static class DatabaseMigrator
{
    private const string ResourceMarker = ".Migrations.";

    public static async Task<IReadOnlyList<string>> MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS schema_migration (name TEXT PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))) STRICT");

        var applied = (await connection.QueryAsync<string>("SELECT name FROM schema_migration")).ToHashSet(StringComparer.Ordinal);
        var executed = new List<string>();
        var assembly = typeof(DatabaseMigrator).Assembly;

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            var name = resource[(resource.IndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..];
            if (applied.Contains(name))
            {
                continue;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(await ReadAsync(assembly, resource, cancellationToken), transaction: transaction);
            await connection.ExecuteAsync("INSERT INTO schema_migration (name) VALUES (@name)", new { name }, transaction);
            await transaction.CommitAsync(cancellationToken);
            executed.Add(name);
        }

        return executed;
    }

    private static async Task<string> ReadAsync(Assembly assembly, string resource, CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Recurso não encontrado: {resource}.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
