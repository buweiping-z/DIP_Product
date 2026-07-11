namespace DIP.Api.Models;

/// <summary>
/// 统一 API 响应格式
/// </summary>
public class ApiResponse<T>
{
    public int Code { get; set; } = 0;
    public T? Data { get; set; }
    public string Message { get; set; } = "ok";

    public static ApiResponse<T> Ok(T data, string message = "ok")
        => new() { Code = 0, Data = data, Message = message };

    public static ApiResponse<T> Fail(int code, string message)
        => new() { Code = code, Data = default, Message = message };
}

public class ApiResponse : ApiResponse<object?>
{
    public static new ApiResponse Ok(object? data = null, string message = "ok")
        => new() { Code = 0, Data = data, Message = message };

    public static new ApiResponse Fail(int code, string message)
        => new() { Code = code, Data = null, Message = message };
}
