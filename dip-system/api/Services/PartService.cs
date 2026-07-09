using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using DIP.Api.Data;
using DIP.Api.Models;

namespace DIP.Api.Services;

public class PartService
{
    private readonly AppDbContext _db;

    public PartService(AppDbContext db) { _db = db; }

    public async Task<object> GetListAsync(string? keyword = null, string? partNo = null, string? partName = null,
        long? supplierId = null, int? partType = null, int? status = null, int page = 1, int pageSize = 20)
    {
        var query = _db.Parts.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(p => p.PartNo.Contains(keyword) || p.PartName.Contains(keyword));
        if (!string.IsNullOrEmpty(partNo)) query = query.Where(p => p.PartNo.Contains(partNo));
        if (!string.IsNullOrEmpty(partName)) query = query.Where(p => p.PartName.Contains(partName));
        if (supplierId.HasValue) query = query.Where(p => p.SupplierId == supplierId);
        if (partType.HasValue) query = query.Where(p => p.PartType == partType);
        if (status.HasValue) query = query.Where(p => p.Status == status);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total, page, page_size = pageSize, items = items.Select(ToDict) };
    }

    public async Task<object> GetByIdAsync(long partId)
    {
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        if (part == null) throw AppException.NotFound($"物料 {partId} 不存在");
        return ToDict(part);
    }

    public async Task<object> CreateAsync(Dictionary<string, object?> data)
    {
        var partNo = data.GetStr("part_no");
        if (string.IsNullOrWhiteSpace(partNo)) throw AppException.Business("料号不能为空");

        if (await _db.Parts.AnyAsync(p => p.PartNo == partNo && !p.IsDeleted))
            throw AppException.Business($"料号 {partNo} 已存在");

        var part = new Part
        {
            PartNo = partNo,
            PartName = data.GetStr("part_name") ?? "",
            SupplierId = data.GetLong("supplier_id"),
            SupplierName = data.GetStr("supplier_name") ?? "",
            PartType = data.GetInt("part_type") ?? 1,
            Unit = data.GetStr("unit") ?? "PCS",
            UnitPrice = data.GetDecimal("unit_price"),
            Specification = data.GetStr("specification") ?? "",
            MslLevel = data.GetInt("msl_level") ?? 0,
            MinStock = data.GetInt("min_stock"),
            MaxStock = data.GetInt("max_stock"),
            BarcodeRule = data.GetStr("barcode_rule"),
            ImageUrl = data.GetStr("image_url"),
            Status = 1
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return ToDict(part);
    }

    public async Task<object> UpdateAsync(long partId, Dictionary<string, object?> data)
    {
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        if (part == null) throw AppException.NotFound($"物料 {partId} 不存在");

        data.ApplyTo(part, new[] { "part_name", "supplier_id", "supplier_name", "part_type", "unit",
            "unit_price", "specification", "msl_level", "min_stock", "max_stock",
            "barcode_rule", "status", "image_url" });

        await _db.SaveChangesAsync();
        return ToDict(part);
    }

    public async Task DeleteAsync(long partId)
    {
        var part = await _db.Parts.FirstOrDefaultAsync(p => p.Id == partId);
        if (part == null) throw AppException.NotFound($"物料 {partId} 不存在");
        part.IsDeleted = true;
        await _db.SaveChangesAsync();
    }

    public async Task<int> ImportFromExcelAsync(byte[] fileBytes)
    {
        using var wb = new XLWorkbook(new MemoryStream(fileBytes));
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().Skip(1);
        int count = 0;
        foreach (var row in rows)
        {
            var pn = row.Cell(1).GetString().Trim();
            var name = row.Cell(2).GetString().Trim();
            if (string.IsNullOrEmpty(pn) || string.IsNullOrEmpty(name)) continue;

            var spec = row.Cell(3).GetString().Trim();
            var unit = row.Cell(4).GetString().Trim();
            if (string.IsNullOrEmpty(unit)) unit = "PCS";
            var type = row.Cell(5).TryGetValue(out int tp) ? tp : 1;
            var msl = row.Cell(6).TryGetValue(out int ms) ? ms : 0;

            var existing = await _db.Parts.FirstOrDefaultAsync(p => p.PartNo == pn);
            if (existing != null)
            {
                existing.PartName = name;
                existing.Specification = string.IsNullOrEmpty(spec) ? existing.Specification : spec;
                existing.Unit = unit;
                existing.PartType = type;
                existing.MslLevel = msl;
            }
            else
            {
                _db.Parts.Add(new Part { PartNo = pn, PartName = name, Specification = spec, Unit = unit, PartType = type, MslLevel = msl, Status = 1 });
            }
            count++;
        }
        await _db.SaveChangesAsync();
        return count;
    }

    public async Task<List<object>> GetSubstitutesAsync(long partId)
    {
        var subs = await _db.PartSubstitutes.Where(s => s.OriginalPartId == partId && s.Status == 1).ToListAsync();
        var result = new List<object>();
        foreach (var sub in subs)
        {
            var p = await _db.Parts.FirstOrDefaultAsync(p => p.Id == sub.SubstitutePartId);
            if (p != null) result.Add(ToDict(p));
        }
        return result;
    }

    public async Task<byte[]> ExportTemplateAsync()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("物料导入模板");
        ws.Cell(1, 1).Value = "料号";
        ws.Cell(1, 2).Value = "名称";
        ws.Cell(1, 3).Value = "规格";
        ws.Cell(1, 4).Value = "单位";
        ws.Cell(1, 5).Value = "类型";
        ws.Cell(1, 6).Value = "MSL等级";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static object ToDict(Part p) => new
    {
        p.Id, part_no = p.PartNo, part_name = p.PartName,
        supplier_id = p.SupplierId, supplier_name = p.SupplierName,
        part_type = p.PartType, unit = p.Unit, unit_price = p.UnitPrice,
        specification = p.Specification, msl_level = p.MslLevel,
        min_stock = p.MinStock, max_stock = p.MaxStock,
        barcode_rule = p.BarcodeRule, status = p.Status, image_url = p.ImageUrl
    };
}
