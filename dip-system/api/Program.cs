using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pomelo.EntityFrameworkCore.MySql;
using DIP.Api.Data;
using DIP.Api.Services;
using DIP.Api.Controllers;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "html"
});

// 固定监听地址
builder.WebHost.UseUrls("http://0.0.0.0:8800");

// 1. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 2. 数据库
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))));

// 3. JWT 认证
var jwtSecret = builder.Configuration["Jwt:Secret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 禁止 JWT claim 映射：否则 "role" → ClaimTypes.Role URI，导致 FindFirstValue("role") 找不到
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
    });

// 4. 注册服务
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PartService>();
builder.Services.AddScoped<LocationService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<PrepService>();
builder.Services.AddScoped<ShelvingService>();
builder.Services.AddScoped<OnlineService>();
builder.Services.AddScoped<ReturnService>();
builder.Services.AddScoped<StockCountService>();
builder.Services.AddScoped<AbnormalService>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<UserService>();

// 5. Controllers + Swagger
builder.Services.AddControllers(options =>
{
    options.Filters.Add<AppExceptionFilter>();
    options.Filters.Add<RequireManagerFilter>();
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 6. 启动时自动创建数据库表 + 种子数据
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // 种子角色（幂等 — 已存在则跳过）
    if (!db.Roles.Any(r => r.RoleCode == "admin"))
        db.Roles.Add(new DIP.Api.Models.Role { RoleCode = "admin", RoleName = "系统管理员", Status = 1 });
    if (!db.Roles.Any(r => r.RoleCode == "operator"))
        db.Roles.Add(new DIP.Api.Models.Role { RoleCode = "operator", RoleName = "操作员", Status = 1 });
    if (!db.Roles.Any(r => r.RoleCode == "leader"))
        db.Roles.Add(new DIP.Api.Models.Role { RoleCode = "leader", RoleName = "班组长", Status = 1 });
    db.SaveChanges();

    var adminRoleId = db.Roles.First(r => r.RoleCode == "admin").Id;

    // 种子管理员账号（不存在则创建，存在则修正RoleId和密码）
    var adminUser = db.Operators.FirstOrDefault(o => o.Username == "admin");
    if (adminUser == null)
    {
        db.Operators.Add(new DIP.Api.Models.Operator
        {
            Username = "admin",
            RealName = "系统管理员",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
            RoleId = adminRoleId,
            Status = 1
        });
    }
    else
    {
        if (adminUser.RoleId != adminRoleId)
            adminUser.RoleId = adminRoleId;
        // 每次启动重置密码为 123456
        adminUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");
    }
    db.SaveChanges();

    // 清理僵尸库存记录：关联的物料或库位已被软删除的 Inventory
    var deletedPartIds = db.Parts.IgnoreQueryFilters().Where(p => p.IsDeleted).Select(p => p.Id).ToList();
    var deletedLocIds = db.WarehouseLocations.IgnoreQueryFilters().Where(l => l.IsDeleted).Select(l => l.Id).ToList();

    if (deletedPartIds.Any() || deletedLocIds.Any())
    {
        var zombieInvs = db.Inventories.Where(i =>
            deletedPartIds.Contains(i.PartId) || deletedLocIds.Contains(i.LocationId)).ToList();

        if (zombieInvs.Any())
        {
            // 扣减受影响库位的 CurrentQty（仅库位未被删除的情况）
            var affectedLocIds = zombieInvs
                .Where(i => !deletedLocIds.Contains(i.LocationId))
                .Select(i => i.LocationId).Distinct().ToList();
            var affectedLocs = db.WarehouseLocations.Where(l => affectedLocIds.Contains(l.Id)).ToList();

            foreach (var inv in zombieInvs)
            {
                var loc = affectedLocs.FirstOrDefault(l => l.Id == inv.LocationId);
                if (loc != null) loc.CurrentQty -= inv.TotalQty;
                inv.IsDeleted = true;
            }

            // 同步清理关联的批次记录
            var zombieLots = db.InventoryLots.Where(l =>
                deletedPartIds.Contains(l.PartId) || deletedLocIds.Contains(l.LocationId)).ToList();
            foreach (var lot in zombieLots) lot.IsDeleted = true;

            db.SaveChanges();
        }
    }

    // 修复缺失的 InventoryLot：有可用库存但无对应批次记录的，补建默认批次
    var invsWithoutLots = db.Inventories.Where(i => i.AvailableQty > 0).ToList()
        .Where(i => !db.InventoryLots.Any(l => l.InventoryId == i.Id)).ToList();
    foreach (var inv in invsWithoutLots)
    {
        db.InventoryLots.Add(new DIP.Api.Models.InventoryLot
        {
            InventoryId = inv.Id, PartId = inv.PartId, LocationId = inv.LocationId,
            BatchNo = $"REPAIR-{DateTime.UtcNow:yyyyMMdd}", Quantity = inv.AvailableQty,
            Status = 1, ReceiptDate = DateTime.UtcNow, OriginType = 1
        });
    }
    if (invsWithoutLots.Any()) db.SaveChanges();

    // 修复批次数量与可用库存不一致的（之前 TotalQty 造成的错误）
    var brokenLots = db.InventoryLots.Where(l => l.Status == 1 && l.Quantity > 0).ToList()
        .Where(l => { var inv = db.Inventories.FirstOrDefault(i => i.Id == l.InventoryId); return inv != null && l.Quantity > inv.AvailableQty; }).ToList();
    foreach (var lot in brokenLots)
    {
        var inv = db.Inventories.First(i => i.Id == lot.InventoryId);
        lot.Quantity = inv.AvailableQty;
        if (lot.Quantity <= 0) lot.Status = 3;
    }
    if (brokenLots.Any()) db.SaveChanges();

    // 修复僵尸冻结库存：根据活跃备料扫描记录重建 FrozenQty
    var frozenInvs = db.Inventories.Where(i => i.FrozenQty > 0).ToList();
    foreach (var inv in frozenInvs)
    {
        // 计算该库位+部品实际应冻结的数量：来自未取消的备料单扫描
        var expectedFrozen = db.PrepScanRecords
            .Where(s => s.SourceLocationId == inv.LocationId
                && db.PrepDetails.Any(d => d.Id == s.PrepDetailId && d.PartId == inv.PartId
                    && db.PrepOrders.Any(p => p.Id == d.PrepOrderId && p.Status != 3)))
            .Sum(s => (decimal?)s.Quantity) ?? 0m;

        if (inv.FrozenQty > expectedFrozen)
        {
            var excess = inv.FrozenQty - expectedFrozen;
            inv.FrozenQty = expectedFrozen;
            inv.AvailableQty += excess;
        }
    }
    db.SaveChanges();
}

// 7. 中间件管道
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// 静态文件
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
