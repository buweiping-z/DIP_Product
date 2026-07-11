namespace DIP.Api.Models;

public class RefillStartItem
{
    public long PrepDetailId { get; set; }
    public long PrepOrderId { get; set; }
    public string PartNo { get; set; } = "";
    public string PartName { get; set; } = "";
    public string LocationCode { get; set; } = "";
}
