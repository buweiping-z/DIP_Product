using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Models;
using DIP.Api.Services;

namespace DIP.Api.Controllers;

/// <summary>
/// 全局异常过滤器：业务异常 → HTTP 200 + 业务错误码，系统异常 → 友好提示
/// </summary>
public class AppExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is AppException ex)
        {
            context.Result = new JsonResult(ApiResponse.Fail(ex.Code, ex.Message))
            {
                StatusCode = 200
            };
            context.ExceptionHandled = true;
        }
        else if (context.Exception is DbUpdateException dbEx)
        {
            var msg = dbEx.InnerException?.Message ?? dbEx.Message;
            // 提取关键信息：唯一约束冲突、外键冲突等
            if (msg.Contains("Duplicate entry") || msg.Contains("duplicate"))
                msg = "数据已存在，请检查是否重复";
            else if (msg.Contains("foreign key") || msg.Contains("FOREIGN KEY"))
                msg = "关联数据不存在，请检查引用";
            else if (msg.Contains("cannot be null") || msg.Contains("NULL"))
                msg = "必填字段不能为空";
            else
                msg = "数据保存失败: " + msg;

            context.Result = new JsonResult(ApiResponse.Fail(500, msg))
            {
                StatusCode = 200
            };
            context.ExceptionHandled = true;
        }
        else
        {
            context.Result = new JsonResult(ApiResponse.Fail(500, "服务器内部错误: " + context.Exception.Message))
            {
                StatusCode = 200
            };
            context.ExceptionHandled = true;
        }
    }
}
