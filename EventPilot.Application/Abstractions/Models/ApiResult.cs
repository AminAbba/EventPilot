namespace EventPilot.Application.Common.Models;

public class ApiResult<T>
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
    public List<string> Errors { get; init; } = [];

    public static ApiResult<T> Success(T data, string message = "Success")
    {
        return new ApiResult<T>
        {
            IsSuccess = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResult<T> Failure(string message, List<string>? errors = null)
    {
        return new ApiResult<T>
        {
            IsSuccess = false,
            Message = message,
            Errors = errors ?? []
        };
    }
}

public class ApiResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();

    public static ApiResult Success(string message = "Success")
        => new() { IsSuccess = true, Message = message };

    public static ApiResult Failure(string message, List<string>? errors = null)
        => new()
        {
            IsSuccess = false,
            Message = message,
            Errors = errors ?? []
        };
}