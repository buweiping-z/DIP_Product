using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using DIP.Api.Data;

namespace DIP.Api.Services;

public class ReportService
{
    private readonly AppDbContext _db;
    private static bool _fontRegistered;

    public ReportService(AppDbContext db) { _db = db; }

    public async Task<byte[]> GenerateDailyReportAsync(DateTime date)
    {
        var dayStart = date.Date.AddHours(-8);
        var dayEnd = dayStart.AddDays(1);

        var data = await CollectDataAsync(dayStart, dayEnd);
        return RenderPdf(data, date);
    }

    private async Task<DailyReportData> CollectDataAsync(DateTime start, DateTime end)
    {
        var orders = await _db.ProductionOrders
            .Where(o => !o.IsDeleted && o.CreatedAt >= start && o.CreatedAt < end)
            .ToListAsync();

        var completedOrders = await _db.ProductionOrders
            .Where(o => !o.IsDeleted && o.Status == 3 && o.UpdatedAt >= start && o.UpdatedAt < end)
            .ToListAsync();

        var preps = await _db.PrepOrders
            .Where(p => !p.IsDeleted && p.CompletedAt >= start && p.CompletedAt < end)
            .ToListAsync();

        var prepScans = await _db.PrepDetails
            .CountAsync(d => !d.IsDeleted && d.Status == 2 && d.UpdatedAt >= start && d.UpdatedAt < end);

        var pendingReplenish = await _db.PrepDetails
            .CountAsync(d => !d.IsDeleted && d.Status == 3);

        var onlineConfirms = await _db.OnlineConfirms
            .Where(o => !o.IsDeleted && o.CreatedAt >= start && o.CreatedAt < end)
            .ToListAsync();

        var abnormals = await _db.AbnormalRecords
            .Where(a => !a.IsDeleted && a.CreatedAt >= start && a.CreatedAt < end)
            .ToListAsync();

        var movements = await _db.StockMovements
            .Where(m => m.CreatedAt >= start && m.CreatedAt < end)
            .ToListAsync();

        var refills = await _db.RefillRecords
            .Where(r => !r.IsDeleted && r.CreatedAt >= start && r.CreatedAt < end)
            .ToListAsync();

        var changeovers = await _db.ChangeoverBatches
            .Where(c => !c.IsDeleted && c.CreatedAt >= start && c.CreatedAt < end)
            .ToListAsync();

        var lowStock = await _db.Inventories
            .CountAsync(i => !i.IsDeleted && i.AvailableQty > 0 && i.AvailableQty < 10);
        var outOfStock = await _db.Inventories
            .CountAsync(i => !i.IsDeleted && i.AvailableQty == 0);

        var allActiveOrders = await _db.ProductionOrders
            .Where(o => !o.IsDeleted && (o.Status == 1 || o.Status == 2))
            .CountAsync();

        return new DailyReportData
        {
            NewOrders = orders.Where(o => o.Status != 4).ToList(),
            CompletedOrders = completedOrders,
            InProgressCount = allActiveOrders,
            PrepCompleted = preps,
            PrepScanCount = prepScans,
            PendingReplenish = pendingReplenish,
            OnlineConfirmCount = onlineConfirms.Count,
            OnlinePartKinds = onlineConfirms.Select(o => o.PartNo).Distinct().Count(),
            Abnormals = abnormals,
            MovementIn = movements.Count(m => m.MovementType == 1 || m.MovementType == 2),
            MovementOut = movements.Count(m => m.MovementType == 3 || m.MovementType == 4),
            RefillCount = refills.Select(r => r.BatchNo).Distinct().Count(),
            Changeovers = changeovers,
            LowStock = lowStock,
            OutOfStock = outOfStock
        };
    }

    private byte[] RenderPdf(DailyReportData data, DateTime date)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        RegisterFont();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(40);
                page.MarginVertical(30);
                page.DefaultTextStyle(x => x.FontFamily("SimHei").FontSize(10));

                page.Header().Element(c => ComposeHeader(c, date));
                page.Content().Element(c => ComposeContent(c, data));
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("第 ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                    t.Span(" 页");
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static void RegisterFont()
    {
        if (_fontRegistered) return;
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "simhei.ttf");
        if (File.Exists(fontPath))
        {
            QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead(fontPath));
            _fontRegistered = true;
        }
    }

    private void ComposeHeader(IContainer container, DateTime date)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text("生产作业日报")
                .FontSize(20).Bold();
            col.Item().Height(4);
            col.Item().AlignCenter().Text($"报告日期：{date:yyyy年MM月dd日}")
                .FontSize(11).FontColor(Colors.Grey.Darken1);
            col.Item().Height(12);
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            col.Item().Height(12);
        });
    }

    private void ComposeContent(IContainer container, DailyReportData data)
    {
        container.Column(col =>
        {
            col.Spacing(16);
            col.Item().Element(c => ComposeOverview(c, data));
            col.Item().Element(c => ComposeOrderSection(c, data));
            col.Item().Element(c => ComposeAbnormalSection(c, data));
            if (data.Changeovers.Any())
                col.Item().Element(c => ComposeChangeoverSection(c, data));
            col.Item().Element(c => ComposeSummarySection(c, data));
        });
    }

    private void ComposeOverview(IContainer container, DailyReportData data)
    {
        var effective = data.NewOrders.Count + data.CompletedOrders.Count + data.InProgressCount;
        var completionRate = effective > 0
            ? Math.Round((double)data.CompletedOrders.Count / effective * 100, 1)
            : 0;

        container.Column(col =>
        {
            col.Item().Text("一、今日总览").FontSize(13).Bold();
            col.Item().Height(6);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn();
                    cd.RelativeColumn();
                    cd.RelativeColumn();
                    cd.RelativeColumn();
                });

                table.Header(h =>
                {
                    h.Cell().Element(c => HeaderCell(c, "指标"));
                    h.Cell().Element(c => HeaderCell(c, "数值"));
                    h.Cell().Element(c => HeaderCell(c, "指标"));
                    h.Cell().Element(c => HeaderCell(c, "数值"));
                });

                table.Cell().Element(c => BodyCell(c, "今日新建订单"));
                table.Cell().Element(c => BodyCell(c, $"{data.NewOrders.Count} 单"));
                table.Cell().Element(c => BodyCell(c, "今日完成订单"));
                table.Cell().Element(c => BodyCell(c, $"{data.CompletedOrders.Count} 单"));

                table.Cell().Element(c => BodyCell(c, "在制订单"));
                table.Cell().Element(c => BodyCell(c, $"{data.InProgressCount} 单"));
                table.Cell().Element(c => BodyCell(c, "订单完成率"));
                table.Cell().Element(c => BodyCell(c, $"{completionRate}%"));

                table.Cell().Element(c => BodyCell(c, "备料完成"));
                table.Cell().Element(c => BodyCell(c, $"{data.PrepCompleted.Count} 单"));
                table.Cell().Element(c => BodyCell(c, "库存预警"));
                table.Cell().Element(c => BodyCell(c, $"低库存 {data.LowStock} / 缺料 {data.OutOfStock}"));
            });
        });
    }

    private void ComposeOrderSection(IContainer container, DailyReportData data)
    {
        container.Column(col =>
        {
            col.Item().Text("二、今日订单明细").FontSize(13).Bold();
            col.Item().Height(6);

            if (!data.NewOrders.Any() && !data.CompletedOrders.Any())
            {
                col.Item().Text("今日无订单变动。").FontColor(Colors.Grey.Medium);
                return;
            }

            if (data.NewOrders.Any())
            {
                col.Item().Text("【新建订单】").FontSize(10).Bold();
                col.Item().Height(4);
                col.Item().Element(c => ComposeOrderTable(c, data.NewOrders));
                col.Item().Height(8);
            }

            if (data.CompletedOrders.Any())
            {
                col.Item().Text("【完成订单】").FontSize(10).Bold();
                col.Item().Height(4);
                col.Item().Element(c => ComposeOrderTable(c, data.CompletedOrders));
            }
        });
    }

    private void ComposeOrderTable(IContainer container, List<Models.ProductionOrder> orders)
    {
        var statusMap = new[] { "", "待备料", "待上线", "已完成", "已取消" };
        container.Table(table =>
        {
            table.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn();
                cd.RelativeColumn(2);
                cd.RelativeColumn();
                cd.ConstantColumn(50);
                cd.ConstantColumn(55);
            });

            table.Header(h =>
            {
                h.Cell().Element(c => HeaderCell(c, "订单号"));
                h.Cell().Element(c => HeaderCell(c, "产品名称"));
                h.Cell().Element(c => HeaderCell(c, "生连"));
                h.Cell().Element(c => HeaderCell(c, "数量"));
                h.Cell().Element(c => HeaderCell(c, "状态"));
            });

            foreach (var o in orders)
            {
                table.Cell().Element(c => BodyCell(c, o.OrderNo));
                table.Cell().Element(c => BodyCell(c, o.ProductName));
                table.Cell().Element(c => BodyCell(c, o.ProductionMonth ?? "-"));
                table.Cell().Element(c => BodyCell(c, o.PlanQty.ToString()));
                table.Cell().Element(c => BodyCell(c, o.Status < statusMap.Length ? statusMap[o.Status] : o.Status.ToString()));
            }
        });
    }

    private void ComposeAbnormalSection(IContainer container, DailyReportData data)
    {
        container.Column(col =>
        {
            col.Item().Text("三、今日异常记录").FontSize(13).Bold();
            col.Item().Height(6);

            if (!data.Abnormals.Any())
            {
                col.Item().Text("今日无异常。").FontColor(Colors.Grey.Medium);
                return;
            }

            var typeMap = new[] { "", "物料", "设备", "质量", "其他" };
            var sevMap = new[] { "", "低", "中", "高", "紧急" };
            var stMap = new[] { "", "待处理", "处理中", "已解决" };

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(45);
                    cd.RelativeColumn(3);
                    cd.ConstantColumn(55);
                    cd.ConstantColumn(55);
                });

                table.Header(h =>
                {
                    h.Cell().Element(c => HeaderCell(c, "类型"));
                    h.Cell().Element(c => HeaderCell(c, "描述"));
                    h.Cell().Element(c => HeaderCell(c, "严重程度"));
                    h.Cell().Element(c => HeaderCell(c, "状态"));
                });

                foreach (var a in data.Abnormals)
                {
                    table.Cell().Element(c => BodyCell(c, a.Type < typeMap.Length ? typeMap[a.Type] : a.Type.ToString()));
                    table.Cell().Element(c => BodyCell(c, a.Description.Length > 40 ? a.Description[..40] + "..." : a.Description));
                    table.Cell().Element(c => BodyCell(c, a.Severity < sevMap.Length ? sevMap[a.Severity] : a.Severity.ToString()));
                    table.Cell().Element(c => BodyCell(c, a.Status < stMap.Length ? stMap[a.Status] : a.Status.ToString()));
                }
            });
        });
    }

    private void ComposeChangeoverSection(IContainer container, DailyReportData data)
    {
        container.Column(col =>
        {
            col.Item().Text("四、今日换线记录").FontSize(13).Bold();
            col.Item().Height(6);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn();
                    cd.RelativeColumn(2);
                    cd.ConstantColumn(60);
                });

                table.Header(h =>
                {
                    h.Cell().Element(c => HeaderCell(c, "批次号"));
                    h.Cell().Element(c => HeaderCell(c, "产品"));
                    h.Cell().Element(c => HeaderCell(c, "状态"));
                });

                foreach (var c in data.Changeovers)
                {
                    table.Cell().Element(x => BodyCell(x, c.BatchNo));
                    table.Cell().Element(x => BodyCell(x, c.ProductName));
                    table.Cell().Element(x => BodyCell(x, c.Status == 2 ? "已完成" : "进行中"));
                }
            });
        });
    }

    private void ComposeSummarySection(IContainer container, DailyReportData data)
    {
        var sectionNo = data.Changeovers.Any() ? "五" : "四";
        container.Column(col =>
        {
            col.Item().Text($"{sectionNo}、作业汇总统计").FontSize(13).Bold();
            col.Item().Height(6);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn();
                    cd.RelativeColumn();
                });

                table.Header(h =>
                {
                    h.Cell().Element(c => HeaderCell(c, "项目"));
                    h.Cell().Element(c => HeaderCell(c, "数值"));
                });

                void Row(string label, string value)
                {
                    table.Cell().Element(c => BodyCell(c, label));
                    table.Cell().Element(c => BodyCell(c, value));
                }

                Row("备料完成", $"{data.PrepCompleted.Count} 单");
                Row("备料扫码确认", $"{data.PrepScanCount} 次");
                Row("待补货项", $"{data.PendingReplenish} 项");
                Row("上线确认", $"{data.OnlineConfirmCount} 次 / {data.OnlinePartKinds} 种物料");
                Row("库存入库", $"{data.MovementIn} 次");
                Row("库存出库", $"{data.MovementOut} 次");
                Row("补货批次", $"{data.RefillCount} 批");
                Row("低库存预警", $"{data.LowStock} 项");
                Row("缺料预警", $"{data.OutOfStock} 项");
            });
        });
    }

    private static void HeaderCell(IContainer container, string text)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
            .Background(Colors.Grey.Lighten4)
            .Padding(5).Text(text).FontSize(9).Bold();
    }

    private static void BodyCell(IContainer container, string text)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
            .Padding(4).Text(text).FontSize(9);
    }

    private class DailyReportData
    {
        public List<Models.ProductionOrder> NewOrders { get; set; } = new();
        public List<Models.ProductionOrder> CompletedOrders { get; set; } = new();
        public int InProgressCount { get; set; }
        public List<Models.PrepOrder> PrepCompleted { get; set; } = new();
        public int PrepScanCount { get; set; }
        public int PendingReplenish { get; set; }
        public int OnlineConfirmCount { get; set; }
        public int OnlinePartKinds { get; set; }
        public List<Models.AbnormalRecord> Abnormals { get; set; } = new();
        public int MovementIn { get; set; }
        public int MovementOut { get; set; }
        public int RefillCount { get; set; }
        public List<Models.ChangeoverBatch> Changeovers { get; set; } = new();
        public int LowStock { get; set; }
        public int OutOfStock { get; set; }
    }
}
