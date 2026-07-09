using Microsoft.EntityFrameworkCore;
using DIP.Api.Models;

namespace DIP.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // NoTracking disabled — write operations need change tracking
    }

    // ===== 认证 =====
    public DbSet<Role> Roles { get; set; }
    public DbSet<Operator> Operators { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    // ===== 基础数据 =====
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<Part> Parts { get; set; }
    public DbSet<PartSubstitute> PartSubstitutes { get; set; }
    public DbSet<ProductionLine> ProductionLines { get; set; }
    public DbSet<Station> Stations { get; set; }
    public DbSet<WarehouseLocation> WarehouseLocations { get; set; }
    public DbSet<ProductBom> ProductBoms { get; set; }

    // ===== 库存 =====
    public DbSet<Inventory> Inventories { get; set; }
    public DbSet<InventoryLot> InventoryLots { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }

    // ===== 订单 =====
    public DbSet<ProductionOrder> ProductionOrders { get; set; }
    public DbSet<BomItem> BomItems { get; set; }

    // ===== 备料 =====
    public DbSet<PrepOrder> PrepOrders { get; set; }
    public DbSet<PrepDetail> PrepDetails { get; set; }
    public DbSet<PrepScanRecord> PrepScanRecords { get; set; }

    // ===== 上架 =====
    public DbSet<ShelvingBatch> ShelvingBatches { get; set; }
    public DbSet<ShelvingBatchItem> ShelvingBatchItems { get; set; }
    public DbSet<MaterialShelving> MaterialShelvings { get; set; }

    // ===== 上线 =====
    public DbSet<OnlineConfirm> OnlineConfirms { get; set; }

    // ===== 退料 =====
    public DbSet<ReturnOrder> ReturnOrders { get; set; }
    public DbSet<ReturnOrderItem> ReturnOrderItems { get; set; }

    // ===== 调拨 =====
    public DbSet<TransferOrder> TransferOrders { get; set; }
    public DbSet<TransferOrderItem> TransferOrderItems { get; set; }

    // ===== 异常 =====
    public DbSet<AbnormalRecord> AbnormalRecords { get; set; }

    // ===== 盘点 =====
    public DbSet<StockCount> StockCounts { get; set; }
    public DbSet<StockCountItem> StockCountItems { get; set; }

    // ===== 替代料记录 =====
    public DbSet<SubstituteRecord> SubstituteRecords { get; set; }

    // ===== 审计 =====
    public DbSet<ScanRecord> ScanRecords { get; set; }
    public DbSet<SystemLog> SystemLogs { get; set; }
    public DbSet<OrderClosure> OrderClosures { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ===== 全局软删除过滤器 =====
        modelBuilder.Entity<Role>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Operator>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Part>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PartSubstitute>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ProductionLine>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Station>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<WarehouseLocation>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ProductBom>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Inventory>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<InventoryLot>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ProductionOrder>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<BomItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PrepOrder>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<PrepDetail>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ShelvingBatch>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ShelvingBatchItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<MaterialShelving>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<OnlineConfirm>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ReturnOrder>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ReturnOrderItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<TransferOrder>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<TransferOrderItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<AbnormalRecord>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<StockCount>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<StockCountItem>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<OrderClosure>().HasQueryFilter(e => !e.IsDeleted);

        // ===== 表名映射 =====
        modelBuilder.Entity<Role>(e => e.ToTable("roles"));
        modelBuilder.Entity<Operator>(e => e.ToTable("operators"));
        modelBuilder.Entity<RefreshToken>(e => e.ToTable("refresh_tokens"));
        modelBuilder.Entity<Supplier>(e => e.ToTable("suppliers"));
        modelBuilder.Entity<Part>(e => e.ToTable("parts"));
        modelBuilder.Entity<PartSubstitute>(e => e.ToTable("part_substitutes"));
        modelBuilder.Entity<ProductionLine>(e => e.ToTable("production_lines"));
        modelBuilder.Entity<Station>(e => e.ToTable("stations"));
        modelBuilder.Entity<WarehouseLocation>(e => e.ToTable("warehouse_locations"));
        modelBuilder.Entity<ProductBom>(e => e.ToTable("product_boms"));
        modelBuilder.Entity<Inventory>(e => e.ToTable("inventories"));
        modelBuilder.Entity<InventoryLot>(e => e.ToTable("inventory_lots"));
        modelBuilder.Entity<StockMovement>(e => e.ToTable("stock_movements"));
        modelBuilder.Entity<ProductionOrder>(e => e.ToTable("production_orders"));
        modelBuilder.Entity<BomItem>(e => e.ToTable("bom_items"));
        modelBuilder.Entity<PrepOrder>(e => e.ToTable("prep_orders"));
        modelBuilder.Entity<PrepDetail>(e => e.ToTable("prep_details"));
        modelBuilder.Entity<PrepScanRecord>(e => e.ToTable("prep_scan_records"));
        modelBuilder.Entity<ShelvingBatch>(e => e.ToTable("loading_batches"));
        modelBuilder.Entity<ShelvingBatchItem>(e => e.ToTable("loading_batch_items"));
        modelBuilder.Entity<MaterialShelving>(e => e.ToTable("material_loadings"));
        modelBuilder.Entity<OnlineConfirm>(e => e.ToTable("online_confirms"));
        modelBuilder.Entity<ReturnOrder>(e => e.ToTable("return_orders"));
        modelBuilder.Entity<ReturnOrderItem>(e => e.ToTable("return_order_items"));
        modelBuilder.Entity<TransferOrder>(e => e.ToTable("transfer_orders"));
        modelBuilder.Entity<TransferOrderItem>(e => e.ToTable("transfer_order_items"));
        modelBuilder.Entity<AbnormalRecord>(e => e.ToTable("abnormal_records"));
        modelBuilder.Entity<StockCount>(e => e.ToTable("stock_counts"));
        modelBuilder.Entity<StockCountItem>(e => e.ToTable("stock_count_items"));
        modelBuilder.Entity<SubstituteRecord>(e => e.ToTable("substitute_records"));
        modelBuilder.Entity<ScanRecord>(e => e.ToTable("scan_records"));
        modelBuilder.Entity<SystemLog>(e => e.ToTable("system_logs"));
        modelBuilder.Entity<OrderClosure>(e => e.ToTable("order_closures"));

        // ===== 唯一索引 =====
        modelBuilder.Entity<Role>().HasIndex(e => e.RoleCode).IsUnique().HasDatabaseName("uq_roles_code");
        modelBuilder.Entity<Operator>().HasIndex(e => e.Username).IsUnique().HasDatabaseName("uq_operators_username");
        // PartNo 唯一性由应用层保证（软删除后允许重建同号）
        modelBuilder.Entity<Supplier>().HasIndex(e => e.SupplierCode).IsUnique().HasDatabaseName("uq_suppliers_code");
        modelBuilder.Entity<ProductionLine>().HasIndex(e => e.LineNo).IsUnique().HasDatabaseName("uq_lines_no");
        modelBuilder.Entity<WarehouseLocation>().HasIndex(e => e.LocationCode).IsUnique().HasDatabaseName("uq_locations_code");
        modelBuilder.Entity<Inventory>().HasIndex(e => new { e.PartId, e.LocationId }).IsUnique().HasDatabaseName("ix_inventories_part_location");
        modelBuilder.Entity<ProductionOrder>().HasIndex(e => e.OrderNo).IsUnique().HasDatabaseName("uq_orders_no");
        modelBuilder.Entity<PrepOrder>().HasIndex(e => e.OrderNo).IsUnique().HasDatabaseName("uq_prep_orders_no");
        modelBuilder.Entity<ShelvingBatch>().HasIndex(e => e.BatchNo).IsUnique().HasDatabaseName("uq_loading_batches_no");
        modelBuilder.Entity<ReturnOrder>().HasIndex(e => e.OrderNo).IsUnique().HasDatabaseName("uq_return_orders_no");
        modelBuilder.Entity<TransferOrder>().HasIndex(e => e.OrderNo).IsUnique().HasDatabaseName("uq_transfer_orders_no");
        modelBuilder.Entity<StockCount>().HasIndex(e => e.CountNo).IsUnique().HasDatabaseName("uq_stock_counts_no");
        modelBuilder.Entity<OrderClosure>().HasIndex(e => e.ProductionOrderId).IsUnique().HasDatabaseName("uq_order_closures_order");

        // ===== 外键 + 级联删除 =====
        modelBuilder.Entity<BomItem>()
            .HasOne(e => e.Order)
            .WithMany(o => o.BomItems)
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PrepDetail>()
            .HasOne(e => e.PrepOrder)
            .WithMany(o => o.Details)
            .HasForeignKey(e => e.PrepOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShelvingBatchItem>()
            .HasOne(e => e.Batch)
            .WithMany(o => o.Items)
            .HasForeignKey(e => e.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ReturnOrderItem>()
            .HasOne(e => e.ReturnOrder)
            .WithMany(o => o.Items)
            .HasForeignKey(e => e.ReturnOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TransferOrderItem>()
            .HasOne(e => e.TransferOrder)
            .WithMany(o => o.Items)
            .HasForeignKey(e => e.TransferOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StockCountItem>()
            .HasOne(e => e.StockCount)
            .WithMany(o => o.Items)
            .HasForeignKey(e => e.StockCountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InventoryLot>()
            .HasOne(e => e.Inventory)
            .WithMany()
            .HasForeignKey(e => e.InventoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===== RefreshToken 配置 =====
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Token).IsUnique().HasDatabaseName("uq_refresh_tokens_token");
        });
    }
}
