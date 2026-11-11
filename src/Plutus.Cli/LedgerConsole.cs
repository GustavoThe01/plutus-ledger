using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plutus.Application.Accounts;
using Plutus.Domain.Accounts;
using Plutus.Domain.Common;
using Spectre.Console;

namespace Plutus.Cli;

/// <summary>
/// Interface de console. Lê a entrada, chama um caso de uso por operação e mostra o resultado.
/// </summary>
/// <remarks>
/// Cada operação abre o próprio escopo de DI, como uma requisição numa API: o repositório e a unidade de trabalho
/// vivem só durante aquela operação. O console recebe <see cref="IAnsiConsole"/> em vez de usar
/// <see cref="AnsiConsole"/> direto, para os testes simularem o teclado e lerem a tela.
/// </remarks>
public sealed partial class LedgerConsole(
    IAnsiConsole console,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<LedgerConsole> logger)
{
    internal enum MenuOption
    {
        OpenAccount,
        ShowAccount,
        Deposit,
        Withdraw,
        Transfer,
        Statement,
        MonthlySummary,
        CloseMonth,
        Exit,
    }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private const int PageSize = 10;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        console.Write(new FigletText("Plutus").Color(Color.Gold1));
        console.MarkupLine("[grey]O deus da riqueza guarda cada centavo.[/]");

        while (!cancellationToken.IsCancellationRequested)
        {
            var option = console.Prompt(new SelectionPrompt<MenuOption>()
                .Title("\nO que você quer fazer?")
                .AddChoices(Enum.GetValues<MenuOption>())
                .UseConverter(Label));

            if (option == MenuOption.Exit)
            {
                console.MarkupLine("[grey]Até logo.[/]");
                return;
            }

            console.Write(new Rule($"[gold1]{Label(option)}[/]").LeftJustified());
            await RunAsync(option, cancellationToken);
        }
    }

    private async Task RunAsync(MenuOption option, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        switch (option)
        {
            case MenuOption.OpenAccount:
                await OpenAccountAsync(services.GetRequiredService<OpenAccount>(), cancellationToken);
                break;
            case MenuOption.ShowAccount:
                await ShowAccountAsync(services.GetRequiredService<GetAccount>(), AskAccount("Conta"), cancellationToken);
                break;
            case MenuOption.Deposit:
                await MoveAsync(services, "Depósito", (number, amount) =>
                    services.GetRequiredService<DepositMoney>().HandleAsync(number, amount, cancellationToken), cancellationToken);
                break;
            case MenuOption.Withdraw:
                await MoveAsync(services, "Saque", (number, amount) =>
                    services.GetRequiredService<WithdrawMoney>().HandleAsync(number, amount, cancellationToken), cancellationToken);
                break;
            case MenuOption.Transfer:
                await TransferAsync(services, cancellationToken);
                break;
            case MenuOption.Statement:
                await StatementAsync(services.GetRequiredService<GetStatement>(), cancellationToken);
                break;
            case MenuOption.MonthlySummary:
                await MonthlySummaryAsync(services.GetRequiredService<GetMonthlySummary>(), cancellationToken);
                break;
            case MenuOption.CloseMonth:
                await CloseMonthAsync(services.GetRequiredService<CloseMonth>(), cancellationToken);
                break;
        }
    }

    private async Task OpenAccountAsync(OpenAccount openAccount, CancellationToken cancellationToken)
    {
        var type = console.Prompt(new SelectionPrompt<AccountType>()
            .Title("Tipo de conta:")
            .AddChoices(AccountType.Checking, AccountType.Savings)
            .UseConverter(TypeName));
        var holder = console.Prompt(new TextPrompt<string>("Nome do titular:"));

        // Poupança não tem cheque especial; nem pergunto.
        var limit = type == AccountType.Checking ? AskAmount("Limite de cheque especial", allowZero: true) : 0m;

        var result = await openAccount.HandleAsync(holder, limit, type, cancellationToken);
        if (Report(result, "Abertura de conta"))
        {
            LogAccountOpened(logger, result.Value.Value);
            console.MarkupLineInterpolated($"[green]✓ Conta {result.Value.Value} aberta para {holder}.[/]");
        }
    }

    private async Task ShowAccountAsync(GetAccount getAccount, string number, CancellationToken cancellationToken)
    {
        var result = await getAccount.HandleAsync(number, cancellationToken);
        if (!Report(result, "Consulta de conta"))
        {
            return;
        }

        var account = result.Value;
        var grid = new Grid().AddColumn().AddColumn(new GridColumn().RightAligned());
        grid.AddRow("Titular", Markup.Escape(account.HolderName));
        grid.AddRow("Tipo", TypeName(account.Type));
        grid.AddRow("Situação", Status(account.Status));
        grid.AddRow("Saldo", Signed(account.Balance));
        grid.AddRow("Limite", Format(account.OverdraftLimit));
        grid.AddRow("[bold]Disponível[/]", $"[bold]{Format(account.Available)}[/]");
        console.Write(new Panel(grid).Header($"Conta {account.Number}").BorderColor(Color.Gold1));
    }

    private async Task MoveAsync(
        IServiceProvider services,
        string operation,
        Func<string, decimal, Task<Result>> handle,
        CancellationToken cancellationToken)
    {
        var number = AskAccount("Conta");
        var amount = AskAmount("Valor", allowZero: false);

        var result = await handle(number, amount);
        if (Report(result, operation))
        {
            LogMoneyMoved(logger, operation, amount, number);
            console.MarkupLineInterpolated($"[green]✓ {operation} de {Format(amount)} concluído.[/]");
            await ShowAccountAsync(services.GetRequiredService<GetAccount>(), number, cancellationToken);
        }
    }

    private async Task TransferAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var source = AskAccount("Conta de origem");
        var destination = AskAccount("Conta de destino");
        var amount = AskAmount("Valor", allowZero: false);

        if (!Confirm($"Transferir {Format(amount)} de {source} para {destination}?"))
        {
            console.MarkupLine("[grey]Transferência cancelada.[/]");
            return;
        }

        var result = await services.GetRequiredService<TransferMoney>().HandleAsync(source, destination, amount, cancellationToken);
        if (Report(result, "Transferência"))
        {
            LogMoneyMoved(logger, "Transferência", amount, $"{source} → {destination}");
            console.MarkupLineInterpolated($"[green]✓ Transferência de {Format(amount)} concluída.[/]");
            await ShowAccountAsync(services.GetRequiredService<GetAccount>(), source, cancellationToken);
        }
    }

    private async Task StatementAsync(GetStatement getStatement, CancellationToken cancellationToken)
    {
        var number = AskAccount("Conta");
        var days = console.Prompt(new TextPrompt<int>("Últimos quantos dias?").DefaultValue(30)
            .Validate(value => value > 0 ? ValidationResult.Success() : ValidationResult.Error("Use um número maior que zero.")));
        var to = clock.GetUtcNow();
        var from = to.AddDays(-days);

        for (var page = 1; ; page++)
        {
            var result = await getStatement.HandleAsync(number, from, to, page, PageSize, cancellationToken);
            if (!Report(result, "Extrato"))
            {
                return;
            }

            var statement = result.Value;
            if (statement.TotalItems == 0)
            {
                console.MarkupLine("[grey]Nenhum lançamento no período.[/]");
                return;
            }

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
                .AddColumn("Data")
                .AddColumn("Descrição")
                .AddColumn(new TableColumn("Valor").RightAligned())
                .AddColumn(new TableColumn("Saldo").RightAligned())
                .Caption($"Página {statement.PageNumber} de {statement.TotalPages} · {statement.TotalItems} lançamentos");

            foreach (var line in statement.Items)
            {
                table.AddRow(
                    line.OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", PtBr),
                    line.Description,
                    Signed(line.Amount),
                    Format(line.BalanceAfter));
            }

            console.Write(table);
            if (!statement.HasNextPage || !Confirm("Ver a próxima página?"))
            {
                return;
            }
        }
    }

    private async Task MonthlySummaryAsync(GetMonthlySummary getMonthlySummary, CancellationToken cancellationToken)
    {
        var number = AskAccount("Conta");
        var to = clock.GetUtcNow();

        var result = await getMonthlySummary.HandleAsync(number, to.AddMonths(-12), to, cancellationToken);
        if (!Report(result, "Resumo mensal"))
        {
            return;
        }

        if (result.Value.Count == 0)
        {
            console.MarkupLine("[grey]Nenhum lançamento nos últimos 12 meses.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .AddColumn("Mês")
            .AddColumn(new TableColumn("Entradas").RightAligned())
            .AddColumn(new TableColumn("Saídas").RightAligned())
            .AddColumn(new TableColumn("Resultado").RightAligned())
            .AddColumn(new TableColumn("Saldo final").RightAligned())
            .AddColumn(new TableColumn("Lançamentos").RightAligned());

        foreach (var month in result.Value)
        {
            table.AddRow(
                month.Month,
                $"[green]{Format(month.Inflows)}[/]",
                $"[red]{Format(month.Outflows)}[/]",
                Signed(month.Net),
                Format(month.ClosingBalance),
                month.Transactions.ToString(PtBr));
        }

        console.Write(table);
    }

    private async Task CloseMonthAsync(CloseMonth closeMonth, CancellationToken cancellationToken)
    {
        var previous = clock.GetUtcNow().AddMonths(-1);
        var answer = console.Prompt(new TextPrompt<string>("Mês a fechar (mm/aaaa):")
            .DefaultValue(previous.ToString("MM/yyyy", PtBr))
            .Validate(text => DateTime.TryParseExact(text, "MM/yyyy", PtBr, DateTimeStyles.None, out _)
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]Use o formato mm/aaaa, como 10/2026.[/]")));
        var month = DateTime.ParseExact(answer, "MM/yyyy", PtBr);

        var result = await closeMonth.HandleAsync(month.Year, month.Month, cancellationToken);
        if (!Report(result, "Fechamento do mês"))
        {
            return;
        }

        if (result.Value.Lines.Count == 0)
        {
            console.MarkupLine("[grey]Nenhuma conta ativa para fechar.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
            .Title($"Fechamento de {result.Value.Month}")
            .AddColumn("Conta")
            .AddColumn("Tipo")
            .AddColumn("Resultado")
            .AddColumn(new TableColumn("Valor").RightAligned())
            .AddColumn("Detalhe");

        foreach (var line in result.Value.Lines)
        {
            table.AddRow(
                line.Number,
                line.Type is { } type ? TypeName(type) : "-",
                Outcome(line.Outcome),
                line.Amount == 0m ? "-" : Format(line.Amount),
                Markup.Escape(line.Detail));
        }

        console.Write(table);
        LogMonthClosed(logger, result.Value.Month, result.Value.Lines.Count);
    }

    /// <summary>Mostra a falha, se houver. A mensagem vem pronta do caso de uso; o código vai para o log.</summary>
    private bool Report(Result result, string operation)
    {
        if (result.IsSuccess)
        {
            return true;
        }

        LogOperationRejected(logger, operation, result.Error!.Code);
        console.MarkupLineInterpolated($"[red]✗ {result.Error.Message}[/]");
        return false;
    }

    /// <summary>Confirmação em português: s para sim, n para não.</summary>
    private bool Confirm(string question) =>
        console.Prompt(new ConfirmationPrompt(question) { Yes = 's', No = 'n', InvalidChoiceMessage = "[red]Responda s ou n.[/]" });

    private string AskAccount(string label) =>
        console.Prompt(new TextPrompt<string>($"{label} (000000-0):"));

    private decimal AskAmount(string label, bool allowZero) =>
        console.Prompt(new TextPrompt<decimal>($"{label} (R$):") { Culture = PtBr }
            .ValidationErrorMessage("[red]Digite um valor como 150,00.[/]")
            .Validate(value => value > 0 || (allowZero && value == 0)
                ? ValidationResult.Success()
                : ValidationResult.Error(allowZero ? "[red]O valor não pode ser negativo.[/]" : "[red]O valor precisa ser maior que zero.[/]")));

    private static string Format(decimal amount) => amount.ToString("C", PtBr);

    private static string Signed(decimal amount) =>
        amount < 0 ? $"[red]{Format(amount)}[/]" : $"[green]{Format(amount)}[/]";

    private static string Status(Domain.Accounts.AccountStatus status) => status switch
    {
        Domain.Accounts.AccountStatus.Active => "[green]Ativa[/]",
        Domain.Accounts.AccountStatus.Blocked => "[yellow]Bloqueada[/]",
        _ => "[grey]Encerrada[/]",
    };

    private static string TypeName(AccountType type) => type switch
    {
        AccountType.Savings => "Poupança",
        _ => "Conta corrente",
    };

    private static string Outcome(ClosingOutcome outcome) => outcome switch
    {
        ClosingOutcome.Charged => "[red]Tarifa cobrada[/]",
        ClosingOutcome.Credited => "[green]Rendimento creditado[/]",
        ClosingOutcome.NothingToPost => "[grey]Nada a lançar[/]",
        ClosingOutcome.AlreadyClosed => "[grey]Já fechado[/]",
        _ => "[yellow]Recusado[/]",
    };

    private static string Label(MenuOption option) => option switch
    {
        MenuOption.OpenAccount => "Abrir conta",
        MenuOption.ShowAccount => "Consultar saldo",
        MenuOption.Deposit => "Depositar",
        MenuOption.Withdraw => "Sacar",
        MenuOption.Transfer => "Transferir",
        MenuOption.Statement => "Extrato",
        MenuOption.MonthlySummary => "Resumo mensal",
        MenuOption.CloseMonth => "Fechar o mês",
        _ => "Sair",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Conta {Number} aberta")]
    private static partial void LogAccountOpened(ILogger logger, string number);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Operation} de {Amount} em {Accounts}")]
    private static partial void LogMoneyMoved(ILogger logger, string operation, decimal amount, string accounts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mês {Month} fechado para {Accounts} contas")]
    private static partial void LogMonthClosed(ILogger logger, string month, int accounts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} recusada: {Code}")]
    private static partial void LogOperationRejected(ILogger logger, string operation, string code);
}
