namespace DIP.Api.Services;

/// <summary>
/// 业务异常，HTTP 200 + 业务错误码
/// </summary>
public class AppException : Exception
{
    public int Code { get; set; }

    public AppException(int code, string message) : base(message)
    {
        Code = code;
    }

    public AppException(string message) : base(message)
    {
        Code = 400;
    }

    public static AppException NotFound(string message) => new(404, message);
    public static AppException Business(string message) => new(400, message);
    public static AppException Unauthorized(string message) => new(401, message);
}
