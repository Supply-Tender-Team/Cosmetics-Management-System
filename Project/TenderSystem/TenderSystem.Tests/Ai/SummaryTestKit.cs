using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Bids;
using TenderSystem.Data.Entities.Collaboration;
using TenderSystem.Data.Entities.Evaluation;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Data.Entities.Requests;
using TenderSystem.Data.Entities.Tenders;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Ai;
using TenderSystem.Services.Audit;

namespace TenderSystem.Tests.Ai;

/// <summary>
/// A small tender for the tests of the two writers (the tender summary and the bid analysis): an in-memory database, and a model that is only
/// a fake web server which answers with the text the test gives it (so the real Ollama code, the JSON check, the retry and the time limit all run).
/// Tender tnd-9 is "Awaiting Manager Review" with two eligible offers (Alpha, Beta), one rejected offer (Gamma) and a committee of two members.
/// </summary>
internal sealed class SummaryTestKit
{
    public ApplicationDbContext Db { get; }
    public FakeModel Model { get; } = new();
    public AiGenerationService Gen { get; }
    public AiDocumentReader Docs { get; }
    public AuditService Audit { get; }

    public SummaryTestKit(bool allFormsReady = true)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Db = new ApplicationDbContext(options);
        Audit = new AuditService(Db, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Seed(allFormsReady);

        var chat = Options.Create(new AiChatOptions { Provider = "ollama" });
        var ollama = Options.Create(new OllamaOptions { BaseUrl = "http://fake-model", Model = "fake", TimeoutSeconds = 20 });
        var http = new FakeHttpFactory(Model);
        var providers = new IAiChatProvider[] { new OllamaChatProvider(http, ollama) };
        Gen = new AiGenerationService(providers, http, Options.Create(new AiGenerationOptions()), chat, ollama,
            Options.Create(new AiAnalysisOptions { Provider = "ollama", TimeoutSeconds = 20 }), NullLogger<AiGenerationService>.Instance);
        Docs = new AiDocumentReader(null!, new MemoryCache(new MemoryCacheOptions()), Options.Create(new AiDocOptions()), chat, Audit, NullLogger<AiDocumentReader>.Instance);
    }

    public AiTenderSummaryService Summary() => new(Db, Audit, Options.Create(new AiSummaryOptions { TimeoutSeconds = 20 }), Docs, Gen);

    public AiBidAnalysisService Analysis() => new(Db, Audit, Options.Create(new AiAnalysisOptions { Provider = "ollama", TimeoutSeconds = 20 }), Docs, Gen);

    private void Seed(bool allFormsReady)
    {
        Db.Departments.Add(new Department { Id = 1, Name = "Packaging", Code = "PKG" });
        foreach (var user in new[] { "oscar", "m1", "m2" }) Db.Users.Add(new ApplicationUser { Id = user, UserName = user, FullName = user });
        Db.PurchaseRequisitions.Add(new PurchaseRequisition { Id = 1, RequestNumber = "pr-9", Title = "Glass boxes", Status = Statuses.RequestStatus.ConvertedToTender, DepartmentId = 1, RequestedByUserId = "oscar", Priority = "High", TargetDate = new DateTime(2027, 1, 1) });
        Db.PurchaseRequisitionItems.Add(new PurchaseRequisitionItem { Id = 1, RequisitionId = 1, ItemName = "Glass box 50ml", Quantity = 1000, Unit = "pcs", Specifications = "clear glass" });
        Db.SupplierProfiles.AddRange(
            new SupplierProfile { Id = 1, UserId = "oscar", CompanyName = "Alpha Co", Status = Statuses.SupplierStatus.Approved },
            new SupplierProfile { Id = 2, UserId = "m1", CompanyName = "Beta Co", Status = Statuses.SupplierStatus.Approved },
            new SupplierProfile { Id = 3, UserId = "m2", CompanyName = "Gamma Co", Status = Statuses.SupplierStatus.Approved });
        Db.Tenders.Add(new Tender
        {
            Id = 1, TenderNumber = "tnd-9", RequisitionId = 1, Title = "Glass boxes tender", Description = "Glass boxes for creams", Terms = "Delivery within 30 days. Payment net 30.",
            Status = Statuses.TenderStatus.AwaitingManagerReview, PublishDate = new DateTime(2026, 10, 1), SubmissionDeadline = new DateTime(2026, 11, 1), EstimatedValue = 5000, CreatedByUserId = "oscar",
        });
        Db.EvaluationCriteria.AddRange(
            new EvaluationCriterion { Id = 1, TenderId = 1, Name = "Material quality", Type = Statuses.CriterionType.Technical, WeightPercent = 40, MaxScore = 5 },
            new EvaluationCriterion { Id = 2, TenderId = 1, Name = "Sample approval", Type = Statuses.CriterionType.Technical, WeightPercent = 40, MaxScore = 5 },
            new EvaluationCriterion { Id = 3, TenderId = 1, Name = "Price", Type = Statuses.CriterionType.Financial, WeightPercent = 20, MaxScore = 5 });
        Db.CommitteeMembers.AddRange(
            new CommitteeMember { Id = 1, TenderId = 1, UserId = "m1", MemberRole = Statuses.CommitteeRole.Technical },
            new CommitteeMember { Id = 2, TenderId = 1, UserId = "m2", MemberRole = Statuses.CommitteeRole.Financial });
        Db.Bids.AddRange(
            new Bid { Id = 1, TenderId = 1, SupplierProfileId = 1, Status = Statuses.BidStatus.Compliant, TotalPrice = 4800, Currency = "USD", DeliveryDays = 20, ValidityDays = 30, PaymentTerms = "Net 30", TechnicalSummary = "Clear glass, tested", SubmittedAt = new DateTime(2026, 10, 20) },
            new Bid { Id = 2, TenderId = 1, SupplierProfileId = 2, Status = Statuses.BidStatus.Accepted, TotalPrice = 5200, Currency = "USD", DeliveryDays = 35, ValidityDays = 15, PaymentTerms = "", TechnicalSummary = "Frosted glass", SubmittedAt = new DateTime(2026, 10, 21) },
            new Bid { Id = 3, TenderId = 1, SupplierProfileId = 3, Status = Statuses.BidStatus.NonCompliant, TotalPrice = 4000, Currency = "USD", DeliveryDays = 10, ValidityDays = 10, PaymentTerms = "Cash", TechnicalSummary = "Plastic", SubmittedAt = new DateTime(2026, 10, 22) });
        Db.BidItems.AddRange(
            new BidItem { Id = 1, BidId = 1, RequisitionItemId = 1, UnitPrice = 4.8m, Quantity = 1000, OfferedSpecifications = "clear glass 50ml" },
            new BidItem { Id = 2, BidId = 2, RequisitionItemId = 1, UnitPrice = 5.2m, Quantity = 900, OfferedSpecifications = "" });
        Db.BidDocuments.Add(new BidDocument { Id = 1, BidId = 1, DocType = "Datasheet", FileName = "alpha.pdf", FilePath = "x/alpha.pdf", ContentType = "application/pdf", UploadedAt = new DateTime(2026, 10, 20) });
        Db.ComplianceChecks.Add(new ComplianceCheck { Id = 1, BidId = 3, CheckedByUserId = "oscar", Result = "Non-Compliant", Reason = "Plastic is not allowed", CheckedAt = new DateTime(2026, 10, 25) });

        // every member scored every question for both eligible offers
        var id = 1;
        foreach (var bid in new[] { 1, 2 })
            foreach (var member in new[] { "m1", "m2" })
            {
                foreach (var crit in new[] { 1, 2 })
                    Db.TechnicalEvaluations.Add(new TechnicalEvaluation { Id = id++, BidId = bid, CriterionId = crit, EvaluatorUserId = member, Score = bid == 1 ? 4 : 3, Notes = crit == 1 ? $"{member} note on bid {bid}" : null, EvaluatedAt = new DateTime(2026, 11, 2) });
                var ready = allFormsReady || !(bid == 2 && member == "m2");
                Db.FinancialEvaluations.Add(new FinancialEvaluation { Id = bid * 10 + (member == "m1" ? 1 : 2), BidId = bid, EvaluatorUserId = member, Notes = $"{member} finance note", EvaluatedAt = new DateTime(2026, 11, 2), ReadyAt = ready ? new DateTime(2026, 11, 3) : null });
            }
        Db.Clarifications.Add(new Clarification { Id = 1, TenderId = 1, Subject = "Sample size", Description = "Which size is meant?", Status = "Answered", SenderUserId = "m1", ReceiverUserId = "oscar" });
        Db.EvaluationReports.Add(new EvaluationReport { Id = 1, TenderId = 1, Status = "Submitted", TechnicalSummary = "Alpha is stronger technically.", FinancialSummary = "Alpha is cheaper.", Justification = "Recommend Alpha.", PreparedByUserId = "m1", SubmittedAt = new DateTime(2026, 11, 4) });
        Db.SaveChanges();
    }

    // ---------------------------------------------------------------- a fake model: a web server that gives the next prepared answer

    public sealed class FakeModel
    {
        public List<string> Answers { get; } = [];
        public int Calls { get; private set; }

        public string Respond()
        {
            var text = Calls < Answers.Count ? Answers[Calls] : Answers.LastOrDefault() ?? "";
            Calls++;
            return text;
        }
    }

    private sealed class FakeHttpFactory(FakeModel model) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(model)) { BaseAddress = new Uri("http://fake-model") };
    }

    private sealed class Handler(FakeModel model) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = System.Text.Json.JsonSerializer.Serialize(new { message = new { role = "assistant", content = model.Respond() }, done = true });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
        }
    }
}
