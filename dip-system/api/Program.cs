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

    // 删除 PartNo 唯一索引（软删除后允许重建同号物料；MySQL 不支持 IF EXISTS，catch 忽略报错）
    try { db.Database.ExecuteSqlRaw("ALTER TABLE parts DROP INDEX uq_parts_part_no"); } catch {}

    // 种子角色（幂等 — 已存在则跳过）
    if (!db.Roles.Any(r => r.RoleCode == "admin"))
        db.Roles.Add(new DIP.Api.Models.Role { RoleCode = "admin", RoleName = "系统管理员", Status = 1 });
    if (!db.Roles.Any(r => r.RoleCode == "operator"))
        db.Roles.Add(new DIP.Api.Models.Role { RoleCode = "operator", RoleName = "操作员", Status = 1 });
    if (!db.Roles.Any(r => r.RoleCode == "leader"))
        db.Roles.Add(new DIP.Api.Models.Role { RoleCode = "leader", RoleName = "班组长", Status = 1 });
    db.SaveChanges();

    var adminRoleId = db.Roles.First(r => r.RoleCode == "admin").Id;

    // 种子管理员账号（不存在则创建，存在则修正RoleId）
    var adminUser = db.Operators.FirstOrDefault(o => o.Username == "admin");
    if (adminUser == null)
    {
        db.Operators.Add(new DIP.Api.Models.Operator
        {
            Username = "admin",
            RealName = "系统管理员",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            RoleId = adminRoleId,
            Status = 1
        });
    }
    else if (adminUser.RoleId != adminRoleId)
    {
        adminUser.RoleId = adminRoleId;
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
