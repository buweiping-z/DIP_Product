using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Controllers;

/// <summary>
/// 全局权限过滤器：POST/PUT/DELETE 操作仅限 admin/leader 角色
/// </summary>
public class RequireManagerFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> ManagerRoles = new(StringComparer.OrdinalIgnoreCase) { "admin", "leader" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (method != "POST" && method != "PUT" && method != "DELETE")
        {
            await next();
            return;
        }

        // 跳过 AuthController 的登录/刷新（无需角色检查）
        var controller = context.Controller.GetType();
        if (controller == typeof(AuthController))
        {
            await next();
            return;
        }

        // 跳过 UserController 的 change-password（所有用户可用）
        if (controller == typeof(UserController) &&
            context.RouteData.Values["action"]?.ToString() == "ChangePassword")
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

        // 先查 JWT claim
        var jwtRole = user.FindFirstValue("role") ?? "";
        if (ManagerRoles.Contains(jwtRole))
        {
            await next();
            return;
        }

        // JWT 不可信则查数据库
        if (!long.TryParse(userIdStr, out var userId))
        {
            context.Result = new JsonResult(ApiResponse.Fail(401, "无效用户"));
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var op = await db.Operators.FirstOrDefaultAsync(o => o.Id == userId);
        if (op == null)
        {
            context.Result = new JsonResult(ApiResponse.Fail(401, "用户不存在"));
            return;
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == op.RoleId);
        if (role == null || !ManagerRoles.Contains(role.RoleCode))
        {
            context.Result = new JsonResult(ApiResponse.Fail(403, "当前用户无法操作"));
            return;
        }

        await next();
    }
}
