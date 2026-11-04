namespace Plutus.Domain.Common;

/// <summary>Falha de negócio esperada, com um código estável para quem precisa tratá-la.</summary>
public sealed record Error(string Code, string Message);

/// <summary>
/// Resultado de uma operação que pode falhar por regra de negócio.
/// </summary>
/// <remarks>
/// Saldo insuficiente não é um erro inesperado: faz parte do dia a dia de um banco.
/// Por isso volta como valor, e o compilador deixa claro para quem chama que existe um caminho de falha.
/// </remarks>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }
}

/// <summary>Resultado que carrega um valor quando a operação dá certo.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error) => _value = value;

    /// <summary>O valor da operação. Lê-lo depois de uma falha é erro de programação.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"O resultado é uma falha ({Error!.Code}) e não tem valor.");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }
}
