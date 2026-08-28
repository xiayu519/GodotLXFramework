namespace LX.Core.Common;

public readonly record struct Error(string Code, string Message, Exception? Exception = null)
{
    public override string ToString() => $"{Code}: {Message}";
}

public readonly record struct Result
{
    private Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(string code, string message, Exception? exception = null) =>
        new(false, new Error(code, message, exception));
}

public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read a failed result: {Error}");

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(string code, string message, Exception? exception = null) =>
        new(false, default, new Error(code, message, exception));
}
