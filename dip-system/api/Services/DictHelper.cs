using System.Text.Json;

namespace DIP.Api.Services;

/// <summary>
/// 帮助方法：从 Dictionary&lt;string, object?&gt; 中安全提取 JsonElement 值
/// 因为 [FromBody] 反序列化时 Dictionary 的 value 是 JsonElement
/// </summary>
public static class DictHelper
{
    public static string? GetStr(this Dictionary<string, object?> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        return v is JsonElement je ? je.GetString() : v.ToString();
    }

    public static int? GetInt(this Dictionary<string, object?> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number) return je.GetInt32();
            if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var iv)) return iv;
            return null;
        }
        return Convert.ToInt32(v);
    }

    public static long? GetLong(this Dictionary<string, object?> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number) return je.GetInt64();
            if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var lv)) return lv;
            return null;
        }
        return Convert.ToInt64(v);
    }

    public static decimal GetDecimal(this Dictionary<string, object?> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return 0;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number) return je.GetDecimal();
            if (je.ValueKind == JsonValueKind.String && decimal.TryParse(je.GetString(), out var dv)) return dv;
            return 0;
        }
        return Convert.ToDecimal(v);
    }

    public static T? GetEnum<T>(this Dictionary<string, object?> d, string key) where T : struct
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        var s = v is JsonElement je ? je.GetString() : v.ToString();
        if (s != null && Enum.TryParse<T>(s, out var result)) return result;
        return null;
    }

    /// <summary>
    /// 将 Dictionary 值设置到实体属性上（自动处理 JsonElement）
    /// </summary>
    public static void ApplyTo<T>(this Dictionary<string, object?> data, T entity, string[] fields) where T : class
    {
        var type = typeof(T);
        foreach (var field in fields)
        {
            if (!data.TryGetValue(field, out var v) || v == null) continue;

            // Convert snake_case field name to PascalCase property name
            var propName = string.Join("", field.Split('_').Select(s =>
                s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : ""));
            var prop = type.GetProperty(propName);
            if (prop == null) continue;

            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object? converted = null;

            if (v is JsonElement je)
            {
                converted = targetType switch
                {
                    Type t when t == typeof(string) => je.GetString(),
                    Type t when t == typeof(int) => je.GetInt32(),
                    Type t when t == typeof(long) => je.GetInt64(),
                    Type t when t == typeof(decimal) => je.GetDecimal(),
                    Type t when t == typeof(double) => je.GetDouble(),
                    Type t when t == typeof(float) => (float)je.GetDouble(),
                    Type t when t == typeof(bool) => je.GetBoolean(),
                    _ => null
                };
            }
            else
            {
                try { converted = Convert.ChangeType(v, targetType); } catch { continue; }
            }

            if (converted != null)
                prop.SetValue(entity, converted);
        }
    }
}
