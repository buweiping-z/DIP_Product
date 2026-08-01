using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using DIP.Api.Data;
using DIP.Api.Services;
using DIP.Api.Controllers;
using DIP.Api.Converters;

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
        policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 1.5 登录限流
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 200;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"code\":429,\"data\":null,\"message\":\"请求过于频繁，请稍后再试\"}", ct);
    };
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// 2. 数据库
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))));

// 3. JWT 认证
var jwtSecret = builder.Configuration["Jwt:Secret"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "DIP.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "DIP.Client";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 禁止 JWT claim 映射：否则 "role" → ClaimTypes.Role URI，导致 FindFirstValue("role") 找不到
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
    });

// 4. 注册服务
builder.Services.AddMemoryCache();
builder.Services.AddScoped<JwtTokenService>();
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
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<OutboundService>();
builder.Services.AddScoped<RefillService>();
builder.Services.AddScoped<SubstituteService>();
builder.Services.AddScoped<ChangeoverService>();
builder.Services.AddScoped<MaterialRequestService>();

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
    options.JsonSerializerOptions.Converters.Add(new LocalDateTimeConverter());
    options.JsonSerializerOptions.Converters.Add(new NullableLocalDateTimeConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 确保新表存在（EnsureCreated 不会给已有 DB 建新表）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.ExecuteSqlRawAsync(
        "CREATE TABLE IF NOT EXISTS inline_changeovers (" +
        "id BIGINT AUTO_INCREMENT PRIMARY KEY, " +
        "product_name VARCHAR(200) NOT NULL, " +
        "part_no VARCHAR(200) NOT NULL, " +
        "operator_id BIGINT NOT NULL DEFAULT 0, " +
        "scanned_at DATETIME NOT NULL, " +
        "tenant_id BIGINT NOT NULL DEFAULT 0, " +
        "created_at DATETIME NOT NULL, " +
        "updated_at DATETIME NULL, " +
        "created_by BIGINT NULL, " +
        "updated_by BIGINT NULL, " +
        "is_deleted TINYINT NOT NULL DEFAULT 0)");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE TABLE IF NOT EXISTS changeover_batches (" +
        "id BIGINT AUTO_INCREMENT PRIMARY KEY, " +
        "batch_no VARCHAR(50) NOT NULL, " +
        "product_name VARCHAR(200) NOT NULL, " +
        "bom_json LONGTEXT NOT NULL, " +
        "scanned_json LONGTEXT NOT NULL, " +
        "status INT NOT NULL DEFAULT 1, " +
        "operator_id BIGINT NOT NULL DEFAULT 0, " +
        "tenant_id BIGINT NOT NULL DEFAULT 0, " +
        "created_at DATETIME NOT NULL, " +
        "updated_at DATETIME NULL, " +
        "created_by BIGINT NULL, " +
        "updated_by BIGINT NULL, " +
        "is_deleted TINYINT NOT NULL DEFAULT 0)");
    // 叫料申请表
    await db.Database.ExecuteSqlRawAsync(
        "CREATE TABLE IF NOT EXISTS material_requests (" +
        "id BIGINT AUTO_INCREMENT PRIMARY KEY, " +
        "part_no VARCHAR(200) NOT NULL, " +
        "part_id BIGINT NOT NULL DEFAULT 0, " +
        "location_code VARCHAR(500) NOT NULL DEFAULT '', " +
        "status INT NOT NULL DEFAULT 0, " +
        "operator_id BIGINT NOT NULL DEFAULT 0, " +
        "tenant_id BIGINT NOT NULL DEFAULT 0, " +
        "created_at DATETIME NOT NULL, " +
        "updated_at DATETIME NULL, " +
        "created_by BIGINT NULL, " +
        "updated_by BIGINT NULL, " +
        "is_deleted TINYINT NOT NULL DEFAULT 0)");
    // 补建后期新增列
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE inline_changeovers ADD COLUMN batch_no VARCHAR(50) NOT NULL DEFAULT ''"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE inline_changeovers ADD COLUMN operator_id BIGINT NOT NULL DEFAULT 0"); } catch { }
    // 生连：生产月份 BOM 版本管理
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE product_boms ADD COLUMN production_month VARCHAR(7) NULL"); } catch { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE production_orders ADD COLUMN production_month VARCHAR(7) NULL"); } catch { }

    // 机种-生连索引表（加速新建订单产品列表加载）
    await db.Database.ExecuteSqlRawAsync(
        "CREATE TABLE IF NOT EXISTS product_month_index (" +
        "id BIGINT AUTO_INCREMENT PRIMARY KEY, " +
        "product_name VARCHAR(200) NOT NULL, " +
        "production_month VARCHAR(7) NULL, " +
        "bom_count INT NOT NULL DEFAULT 0, " +
        "tenant_id BIGINT NOT NULL DEFAULT 0, " +
        "created_at DATETIME NOT NULL, " +
        "updated_at DATETIME NULL, " +
        "created_by BIGINT NULL, " +
        "updated_by BIGINT NULL, " +
        "is_deleted TINYINT NOT NULL DEFAULT 0, " +
        "INDEX idx_pmi_name_month (product_name, production_month))");

    // 清理软删除残留 + 已取消订单：旧版 DeleteAsync 只标记 IsDeleted=1/Status=4，现改为硬删除
    async Task HardDeleteOrderAsync(AppDbContext ctx, long orderId)
    {
        var orderProducts = await ctx.OrderProducts.IgnoreQueryFilters()
            .Where(op => op.OrderId == orderId).ToListAsync();
        ctx.OrderProducts.RemoveRange(orderProducts);

        var bomItems = await ctx.BomItems.IgnoreQueryFilters()
            .Where(b => b.OrderId == orderId).ToListAsync();
        ctx.BomItems.RemoveRange(bomItems);

        var closure = await ctx.OrderClosures.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ProductionOrderId == orderId);
        if (closure != null) ctx.OrderClosures.Remove(closure);

        var preps = await ctx.PrepOrders.IgnoreQueryFilters()
            .Where(p => p.ProductionOrderId == orderId).ToListAsync();
        foreach (var prep in preps)
        {
            var details = await ctx.PrepDetails.IgnoreQueryFilters()
                .Where(d => d.PrepOrderId == prep.Id).ToListAsync();
            var detailIds = details.Select(d => d.Id).ToList();

            var scans = await ctx.PrepScanRecords.IgnoreQueryFilters()
                .Where(s => detailIds.Contains(s.PrepDetailId)).ToListAsync();
            ctx.PrepScanRecords.RemoveRange(scans);

            ctx.PrepDetails.RemoveRange(details);

            var confirms = await ctx.OnlineConfirms.IgnoreQueryFilters()
                .Where(c => c.PrepOrderId == prep.Id).ToListAsync();
            ctx.OnlineConfirms.RemoveRange(confirms);
        }
        ctx.PrepOrders.RemoveRange(preps);
    }

    // 清理 IsDeleted=1 的订单
    var deletedOrders = await db.ProductionOrders.IgnoreQueryFilters()
        .Where(o => o.IsDeleted).ToListAsync();
    foreach (var order in deletedOrders)
    {
        await HardDeleteOrderAsync(db, order.Id);
        db.ProductionOrders.Remove(order);
    }
    if (deletedOrders.Any())
    {
        await db.SaveChangesAsync();
        Console.WriteLine($"已清理 {deletedOrders.Count} 条软删除残留订单及关联数据");
    }

    // 清理 Status=4（已取消）的订单
    var cancelledOrders = await db.ProductionOrders
        .Where(o => o.Status == 4).ToListAsync();
    foreach (var order in cancelledOrders)
    {
        await HardDeleteOrderAsync(db, order.Id);
        db.ProductionOrders.Remove(order);
    }
    if (cancelledOrders.Any())
    {
        await db.SaveChangesAsync();
        Console.WriteLine($"已清理 {cancelledOrders.Count} 条已取消订单及关联数据");
    }
}

// 启动时重建机种-生连索引 + 每小时定时刷新
using (var scope = app.Services.CreateScope())
{
    var orderSvc = scope.ServiceProvider.GetRequiredService<OrderService>();
    await orderSvc.RebuildProductMonthIndexAsync();
    Console.WriteLine("机种-生连索引表已重建");
}
var indexTimer = new System.Threading.Timer(async _ =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<OrderService>();
        await svc.RebuildProductMonthIndexAsync();
    }
    catch { /* 定时刷新失败不影响业务 */ }
}, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

// 6. 中间件管道
app.UseCors("AllowAll");
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// 静态文件（前端 build 产物）
app.UseDefaultFiles();
app.UseStaticFiles();

// API 路由
app.MapControllers();

// SPA fallback: 非 API 非静态文件的请求 → index.html
app.MapFallbackToFile("index.html");

app.Run();
