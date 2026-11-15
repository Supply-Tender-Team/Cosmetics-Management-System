namespace TenderSystem.Areas.Committee.Models;

public class TopCommitteeDashboardViewModel
{
    public int PendingPackageCount { get; set; }
    public int SupplierOfferCount { get; set; }
    public decimal EstimatedValueTotal { get; set; }
    public List<TopCommitteePackageListItemViewModel> Packages { get; set; } = [];
}
