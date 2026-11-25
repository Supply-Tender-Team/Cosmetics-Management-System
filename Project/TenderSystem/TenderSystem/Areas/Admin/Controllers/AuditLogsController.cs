using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TenderSystem.Data;
using TenderSystem.Infrastructure;

namespace TenderSystem.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public class AuditLogsController : Controller
{
    // OWNER: Abdallah
    private const int PageSize = 20;
    private readonly ApplicationDbContext _db;

    public AuditLogsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? search, string? from, string? to, int page = 1)
    {
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];

        var model = new AuditLogsPageModel { Search = search, From = from, To = to };
        DateTime? fromDate = null;
        DateTime? toDate = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                fromDate = parsed;
            else
                model.Error = "Enter a valid From date.";
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                toDate = parsed;
            else
                model.Error = "Enter a valid To date.";
        }
        if (fromDate > toDate && toDate.HasValue)
            model.Error = "From date must be on or before To date.";
        if (toDate == DateTime.MaxValue.Date)
            model.Error = "Choose an earlier To date.";
        if (model.Error is not null) return View(model);

        var query = _db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // the page shows a tender by its number and title, so the search must find it that way too
            var tenderIds = (await _db.Tenders.AsNoTracking()
                .Where(t => t.TenderNumber.Contains(search) || t.Title.Contains(search))
                .Select(t => t.Id).Take(200).ToListAsync()).Select(id => id.ToString()).ToList();
            query = query.Where(log => log.Action.Contains(search) || log.EntityName.Contains(search) ||
                log.EntityId.Contains(search) || (log.UserId != null && log.UserId.Contains(search)) ||
                (log.User != null && log.User.FullName.Contains(search)) ||
                (log.EntityName == "Tender" && tenderIds.Contains(log.EntityId)));
        }
        if (fromDate.HasValue) query = query.Where(log => log.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(log => log.CreatedAt < toDate.Value.AddDays(1));

        model.TotalCount = await query.CountAsync();
        model.PageCount = Math.Max(1, (int)Math.Ceiling(model.TotalCount / (double)PageSize));
        model.Page = Math.Min(Math.Max(page, 1), model.PageCount);
        model.Logs = await query.OrderByDescending(log => log.CreatedAt).ThenByDescending(log => log.Id)
            .Skip((model.Page - 1) * PageSize).Take(PageSize)
            .Select(log => new AuditLogRow
            {
                Actor = log.User != null ? log.User.FullName : (log.UserId ?? "Unknown"),
                Action = log.Action,
                EntityName = log.EntityName,
                EntityId = log.EntityId,
                CreatedAt = log.CreatedAt,
                HasHiddenDetails = log.NewValues != null || log.OldValues != null
            }).ToListAsync();

        var labels = await LabelsAsync(model.Logs.Select(row => (row.EntityName, row.EntityId)));
        foreach (var row in model.Logs)
            if (labels.TryGetValue((row.EntityName, row.EntityId), out var label)) row.EntityLabel = label;
        return View(model);
    }

    /// <summary>
    /// The audit log stores WHICH record an action touched as a technical id (an account is a GUID, a tender is its database number).
    /// People need a name: this finds it for the rows shown on the page ("Tender TND-2026-0007: Packaging", "Account: Omar Officer").
    /// A record with no readable name is shown as before (its type and id).
    /// </summary>
    private async Task<Dictionary<(string Entity, string Id), string>> LabelsAsync(IEnumerable<(string Entity, string Id)> records)
    {
        var list = records.Distinct().ToList();
        var labels = new Dictionary<(string Entity, string Id), string>();
        List<int> Ids(string entity) => list.Where(r => r.Entity == entity && int.TryParse(r.Id, out _)).Select(r => int.Parse(r.Id)).ToList();
        static string Cut(string? text, int max) => string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..(max - 1)] + "…";

        var userIds = list.Where(r => r.Entity == "ApplicationUser").Select(r => r.Id).ToList();
        if (userIds.Count > 0)
            foreach (var u in await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToListAsync())
                labels[("ApplicationUser", u.Id)] = "Account: " + Cut(u.FullName, 40);

        var tenderIds = Ids("Tender");
        if (tenderIds.Count > 0)
            foreach (var t in await _db.Tenders.AsNoTracking().Where(t => tenderIds.Contains(t.Id)).Select(t => new { t.Id, t.TenderNumber, t.Title }).ToListAsync())
                labels[("Tender", t.Id.ToString())] = $"Tender {t.TenderNumber}: {Cut(t.Title, 40)}";

        var requestIds = Ids("PurchaseRequisition");
        if (requestIds.Count > 0)
            foreach (var r in await _db.PurchaseRequisitions.AsNoTracking().Where(r => requestIds.Contains(r.Id)).Select(r => new { r.Id, r.RequestNumber, r.Title }).ToListAsync())
                labels[("PurchaseRequisition", r.Id.ToString())] = $"Request {r.RequestNumber}: {Cut(r.Title, 40)}";

        var bidIds = Ids("Bid");
        if (bidIds.Count > 0)
            foreach (var b in await _db.Bids.AsNoTracking().Where(b => bidIds.Contains(b.Id)).Select(b => new { b.Id, Supplier = b.SupplierProfile.CompanyName, b.Tender.TenderNumber }).ToListAsync())
                labels[("Bid", b.Id.ToString())] = $"Offer of {Cut(b.Supplier, 40)} on tender {b.TenderNumber}";

        var supplierIds = Ids("SupplierProfile");
        if (supplierIds.Count > 0)
            foreach (var s in await _db.SupplierProfiles.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).Select(s => new { s.Id, s.CompanyName }).ToListAsync())
                labels[("SupplierProfile", s.Id.ToString())] = "Supplier: " + Cut(s.CompanyName, 50);

        var invitationIds = Ids("Invitation");
        if (invitationIds.Count > 0)
            foreach (var i in await _db.Invitations.AsNoTracking().Where(i => invitationIds.Contains(i.Id)).Select(i => new { i.Id, Supplier = i.SupplierProfile.CompanyName, i.Tender.TenderNumber }).ToListAsync())
                labels[("Invitation", i.Id.ToString())] = $"Invitation of {Cut(i.Supplier, 40)} to tender {i.TenderNumber}";

        var reportIds = Ids("EvaluationReport");
        if (reportIds.Count > 0)
            foreach (var r in await _db.EvaluationReports.AsNoTracking().Where(r => reportIds.Contains(r.Id)).Select(r => new { r.Id, r.Tender.TenderNumber }).ToListAsync())
                labels[("EvaluationReport", r.Id.ToString())] = $"Evaluation report of tender {r.TenderNumber}";

        var approvalIds = Ids("TenderApproval");
        if (approvalIds.Count > 0)
            foreach (var a in await _db.TenderApprovals.AsNoTracking().Where(a => approvalIds.Contains(a.Id)).Select(a => new { a.Id, a.Level, a.Decision, a.Tender.TenderNumber }).ToListAsync())
                labels[("TenderApproval", a.Id.ToString())] = $"{a.Level} decision ({a.Decision}) on tender {a.TenderNumber}";

        var checkIds = Ids("ComplianceCheck");
        if (checkIds.Count > 0)
            foreach (var c in await _db.ComplianceChecks.AsNoTracking().Where(c => checkIds.Contains(c.Id)).Select(c => new { c.Id, Supplier = c.Bid.SupplierProfile.CompanyName, c.Bid.Tender.TenderNumber }).ToListAsync())
                labels[("ComplianceCheck", c.Id.ToString())] = $"Compliance review of {Cut(c.Supplier, 40)} on tender {c.TenderNumber}";

        var departmentIds = Ids("Department");
        if (departmentIds.Count > 0)
            foreach (var d in await _db.Departments.AsNoTracking().Where(d => departmentIds.Contains(d.Id)).Select(d => new { d.Id, d.Name }).ToListAsync())
                labels[("Department", d.Id.ToString())] = "Department: " + Cut(d.Name, 50);

        var documentIds = Ids("KnowledgeDocument");
        if (documentIds.Count > 0)
            foreach (var d in await _db.KnowledgeDocuments.AsNoTracking().Where(d => documentIds.Contains(d.Id)).Select(d => new { d.Id, d.Title }).ToListAsync())
                labels[("KnowledgeDocument", d.Id.ToString())] = "Assistant document: " + Cut(d.Title, 50);

        return labels;
    }
}

public class AuditLogsPageModel
{
    public string? Search { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Error { get; set; }
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public int TotalCount { get; set; }
    public List<AuditLogRow> Logs { get; set; } = [];
}

public class AuditLogRow
{
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string EntityId { get; set; } = "";
    /// <summary>A readable name of the record ("Tender TND-2026-0007: Packaging"). Empty when none is known: the page then shows the type and the id.</summary>
    public string EntityLabel { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool HasHiddenDetails { get; set; }
}
