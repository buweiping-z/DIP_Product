using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db) { _db = db; }

    public async Task<object> GetListAsync(string? keyword, int? roleId, int page = 1, int pageSize = 20)
    {
        var query = _db.Operators.Include(u => u.Role).Where(u => !u.IsDeleted);
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(u => u.Username.Contains(keyword) || u.RealName.Contains(keyword));
        if (roleId.HasValue)
            query = query.Where(u => u.RoleId == roleId.Value);

        var total = await query.CountAsync();
        var items = await query.OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new
        {
            total, page, page_size = pageSize,
            items = items.Select(u => (object)new
            {
                u.Id, u.Username, real_name = u.RealName,
                role_id = u.RoleId, role_code = u.Role?.RoleCode ?? "",
                role_name = u.Role?.RoleName ?? "", u.LineId,
                u.Status, created_at = u.CreatedAt
            })
        };
    }

    public async Task<object> CreateAsync(string username, string realName, long roleId, string password)
    {
        if (string.IsNullOrWhiteSpace(username)) throw AppException.Business("用户名不能为空");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4) throw AppException.Business("密码至少4位");

        var exist = await _db.Operators.AnyAsync(u => u.Username == username && !u.IsDeleted);
        if (exist) throw AppException.Business("用户名已存在");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        if (role == null) throw AppException.NotFound("角色不存在");

        var user = new Operator
        {
            Username = username,
            RealName = realName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = roleId,
            Status = 1
        };
        _db.Operators.Add(user);
        await _db.SaveChangesAsync();

        return new { id = user.Id, username = user.Username, real_name = user.RealName, role_id = user.RoleId, status = user.Status };
    }

    public async Task<object> UpdateAsync(long id, string? realName, long? roleId, int? status)
    {
        var user = await _db.Operators.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) throw AppException.NotFound("用户不存在");

        if (realName != null) user.RealName = realName;
        if (roleId.HasValue)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId.Value);
            if (role == null) throw AppException.NotFound("角色不存在");
            user.RoleId = roleId.Value;
        }
        if (status.HasValue) user.Status = status.Value;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new { id = user.Id, username = user.Username, real_name = user.RealName, role_id = user.RoleId, status = user.Status };
    }

    public async Task DeleteAsync(long id)
    {
        var user = await _db.Operators.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) throw AppException.NotFound("用户不存在");
        user.IsDeleted = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ResetPasswordAsync(long id, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw AppException.Business("密码至少4位");

        var user = await _db.Operators.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) throw AppException.NotFound("用户不存在");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(long userId, string oldPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw AppException.Business("新密码至少4位");

        var user = await _db.Operators.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);
        if (user == null) throw AppException.NotFound("用户不存在");

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            throw AppException.Business("原密码错误");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<object>> GetRolesAsync()
    {
        var roles = await _db.Roles.Where(r => r.Status == 1).OrderBy(r => r.Id).ToListAsync();
        return roles.Select(r => (object)new { r.Id, r.RoleCode, r.RoleName }).ToList();
    }
}
