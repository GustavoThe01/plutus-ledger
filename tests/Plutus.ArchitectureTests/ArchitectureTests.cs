using System.Reflection;
using NetArchTest.Rules;
using Plutus.Application.Abstractions;
using Plutus.Domain.Common;

namespace Plutus.ArchitectureTests;

/// <summary>
/// As regras de arquitetura viram testes. Se alguém fizer o domínio usar o Dapper, ou deixar o repositório público,
/// o build quebra com o nome do tipo culpado, e a regra não depende só de revisão de código.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(IDomainEvent).Assembly;
    private static readonly Assembly Application = typeof(IAccountRepository).Assembly;
    private static readonly Assembly Infrastructure = typeof(Plutus.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Cli = typeof(Plutus.Cli.LedgerConsole).Assembly;

    private static void AssertPasses(NetArchTest.Rules.TestResult result, string rule)
    {
        var offenders = result.FailingTypes?.Select(type => type.FullName) ?? [];
        Assert.True(result.IsSuccessful, $"{rule}. Tipos fora da regra: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Domain_DependsOnNothingFromOtherLayers_NorOnInfrastructureLibraries()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Plutus.Application", "Plutus.Infrastructure", "Plutus.Cli",
                "Dapper", "Microsoft.Data.Sqlite", "Microsoft.Extensions", "Spectre.Console")
            .GetResult();

        AssertPasses(result, "O domínio não conhece aplicação, banco, DI nem interface");
    }

    [Fact]
    public void Application_DoesNotKnowTheDatabaseNorTheInterface()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Plutus.Infrastructure", "Plutus.Cli", "Dapper", "Microsoft.Data.Sqlite", "Spectre.Console")
            .GetResult();

        AssertPasses(result, "A aplicação fala com o banco só pelas portas (IAccountRepository, IUnitOfWork)");
    }

    [Fact]
    public void Infrastructure_DoesNotKnowTheInterface()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("Plutus.Cli", "Spectre.Console")
            .GetResult();

        AssertPasses(result, "A infraestrutura não conhece o console");
    }

    [Fact]
    public void Adapters_AreHiddenBehindThePorts()
    {
        var result = Types.InAssembly(Infrastructure)
            .That()
            .ImplementInterface(typeof(IAccountRepository))
            .Or()
            .ImplementInterface(typeof(IUnitOfWork))
            .Should()
            .NotBePublic()
            .GetResult();

        AssertPasses(result, "Repositório e unidade de trabalho são internos: quem usa conhece só a interface");
    }

    [Fact]
    public void DomainEvents_AreSealed()
    {
        var result = Types.InAssembly(Domain)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .BeSealed()
            .GetResult();

        AssertPasses(result, "Eventos de domínio são selados: um fato que aconteceu não ganha subtipos");
    }

    [Fact]
    public void UseCases_AreSealed_AndLiveInTheApplication()
    {
        var result = Types.InAssembly(Application)
            .That()
            .ResideInNamespace("Plutus.Application.Accounts")
            .And()
            .AreClasses()
            .And()
            .AreNotStatic()
            .Should()
            .BeSealed()
            .GetResult();

        // Classes estáticas (ApplicationErrors, StatementReport...) já não podem ser herdadas; a regra é para o resto.
        AssertPasses(result, "Casos de uso e DTOs da aplicação são selados");
    }

    [Fact]
    public void TheConsole_IsTheOnlyLayerThatKnowsSpectre()
    {
        var others = Types.InAssemblies([Domain, Application, Infrastructure])
            .That()
            .HaveDependencyOnAny("Spectre.Console")
            .GetTypes();

        Assert.Empty(others);
        Assert.NotEmpty(Types.InAssembly(Cli).That().HaveDependencyOnAny("Spectre.Console").GetTypes());
    }
}
