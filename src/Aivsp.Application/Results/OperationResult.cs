using System;

namespace Aivsp.Application.Results;

public class OperationResult
{
    public bool IsSuccess { get; }
    public ErrorCode ErrorCode { get; }
    public string? ErrorMessage { get; }

    protected OperationResult(bool isSuccess, ErrorCode errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static OperationResult Success() => new OperationResult(true, ErrorCode.None, null);
    public static OperationResult Failure(ErrorCode code, string message) => new OperationResult(false, code, message);
}

public class OperationResult<T> : OperationResult
{
    private readonly T? _value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Result is a failure.");

    private OperationResult(bool isSuccess, T? value, ErrorCode errorCode, string? errorMessage) 
        : base(isSuccess, errorCode, errorMessage)
    {
        _value = value;
    }

    public static OperationResult<T> Success(T value) => new OperationResult<T>(true, value, ErrorCode.None, null);
    public static new OperationResult<T> Failure(ErrorCode code, string message) => new OperationResult<T>(false, default, code, message);
}
