using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Controllers;

/// <summary>
/// 全局权限过滤器：POST/PUT/DELETE 操作需要登录，但不再限制角色（手机端操作员也能用）
/// </summary>
public class RequireManagerFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (method != "POST" && method != "PUT" && method != "DELETE")
        {
            await next();
            return;
        }

        // AuthController 的登录/刷新无需验证
        if (context.Controller is AuthController)
        {
            await next();
            return;
        }

        var user = context.HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr))
        {
            context.Result = new JsonResult(ApiResponse.Fail(401, "请先登录"));
            return;
        }

        await next();
    }
}
