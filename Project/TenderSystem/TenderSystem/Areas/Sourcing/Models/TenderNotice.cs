namespace TenderSystem.Areas.Sourcing.Models;

public class TenderNotice
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Terms { get; set; } = "";
    public string Department { get; set; } = "";
    public DateTime Deadline { get; set; }
    public List<TenderNoticeItem> Items { get; set; } = [];
}

public class TenderNoticeItem
{
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public string? Specifications { get; set; }
}
