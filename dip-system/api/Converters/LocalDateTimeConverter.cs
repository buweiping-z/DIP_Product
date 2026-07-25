using System.Text.Json;
using System.Text.Json.Serialization;

namespace DIP.Api.Converters;

/// <summary>
/// JSON 序列化时自动将 DateTime 转为本地时间（UTC+8），格式化 yyyy-MM-dd HH:mm:ss。
/// MySQL 读回的 DateTime Kind=Unspecified，需主动指定为 UTC 后再转本地时间。
/// </summary>
public class LocalDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var local = value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime(),
            _ => value // Already local, no conversion
        };
        writer.WriteStringValue(local.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}

/// <summary>
/// Nullable DateTime 版本。
/// </summary>
public class NullableLocalDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        var local = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToLocalTime(),
            _ => value.Value
        };
        writer.WriteStringValue(local.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
