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
builder.Services.AddScoped<OutboundService>();
builder.Services.AddScoped<RefillService>();

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

    // 新增表兼容：EnsureCreated 只在数据库不存在时建表，已存在则需手动补建
    try
    {
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS refill_records (
                id BIGINT AUTO_INCREMENT PRIMARY KEY,
                tenant_id BIGINT NOT NULL DEFAULT 0,
                is_deleted TINYINT(1) NOT NULL DEFAULT 0,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NULL,
                created_by BIGINT NULL,
                updated_by BIGINT NULL,
                prep_order_id BIGINT NOT NULL,
                prep_detail_id BIGINT NOT NULL,
                part_id BIGINT NOT NULL DEFAULT 0,
                part_no VARCHAR(200) NOT NULL,
                part_name VARCHAR(200) NOT NULL DEFAULT '',
                location_code VARCHAR(100) NOT NULL DEFAULT '',
                barcode VARCHAR(200) NOT NULL DEFAULT '',
                step INT NOT NULL DEFAULT 1,
                operator_id BIGINT NOT NULL DEFAULT 0,
                picked_at DATETIME NULL,
                verified_at DATETIME NULL
            )";
        try { cmd.ExecuteNonQuery(); } catch { }

        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'outbound_orders'";
        var exists = (long)cmd.ExecuteScalar()! > 0;
        if (!exists)
        {
            cmd.CommandText = @"
                CREATE TABLE outbound_orders (
                    id BIGINT AUTO_INCREMENT PRIMARY KEY,
                    tenant_id BIGINT NOT NULL DEFAULT 0,
                    is_deleted TINYINT(1) NOT NULL DEFAULT 0,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME NULL,
                    created_by BIGINT NULL,
                    updated_by BIGINT NULL,
                    order_no VARCHAR(100) NOT NULL,
                    part_id BIGINT NOT NULL,
                    part_no VARCHAR(200) NOT NULL,
                    part_name VARCHAR(200) NOT NULL,
                    location_id BIGINT NOT NULL,
                    location_code VARCHAR(100) NOT NULL,
                    quantity DECIMAL(18,4) NOT NULL DEFAULT 0,
                    status INT NOT NULL DEFAULT 1,
                    operator_id BIGINT NOT NULL DEFAULT 0,
                    completed_at DATETIME NULL,
                    UNIQUE INDEX uq_outbound_orders_no (order_no)
                )";
            cmd.ExecuteNonQuery();
        }
        // 补 batch_no 字段（旧表可能没有）
        try
        {
            cmd.CommandText = "ALTER TABLE refill_records ADD COLUMN batch_no VARCHAR(50) NOT NULL DEFAULT ''";
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
    catch { }

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

    // 启动时重建冻结：有活跃订单就按创建时间从早到晚重新冻，没有就全部释放
    var hasActiveOrders = db.ProductionOrders.Any(o => o.Status == 1 || o.Status == 2);
    if (hasActiveOrders)
    {
        var orderSvc = new OrderService(db);
        orderSvc.RefreezeActiveOrdersAsync(0).Wait(); // 0 = 系统操作者
    }
    else
    {
        var frozenInvs = db.Inventories.Where(i => i.FrozenQty > 0).ToList();
        foreach (var inv in frozenInvs) { inv.AvailableQty += inv.FrozenQty; inv.FrozenQty = 0; }
        if (frozenInvs.Any()) db.SaveChanges();
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
