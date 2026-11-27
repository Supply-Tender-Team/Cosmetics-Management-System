using TenderSystem.Infrastructure;
using TenderSystem.Services.Ai;

namespace TenderSystem.Tests.Ai;

public class AiLookupCatalogTests
{
    [Theory]
    [InlineData(Roles.Admin, "audit", true)]
    [InlineData(Roles.Admin, "offers", false)]
    [InlineData(Roles.Requester, "requests", true)]
    [InlineData(Roles.Requester, "audit", false)]
    [InlineData(Roles.Requester, "users", false)]
    [InlineData(Roles.Supplier, "invitations", true)]
    [InlineData(Roles.Supplier, "audit", false)]
    [InlineData(Roles.Supplier, "requests", false)]
    [InlineData(Roles.Supplier, "users", false)]
    [InlineData(Roles.TopCommittee, "votes", true)]
    [InlineData(Roles.EvaluationCommittee, "evaluation_progress", true)]
    [InlineData(Roles.EvaluationCommittee, "votes", false)]
    public void Each_role_gets_only_its_own_tools(string role, string tool, bool allowed)
    {
        Assert.Equal(allowed, AiLookupCatalog.Allows(role, false, tool));
    }

    [Fact]
    public void A_requester_with_a_committee_seat_also_gets_the_committee_tools()
    {
        Assert.False(AiLookupCatalog.Allows(Roles.Requester, false, "evaluation_progress"));
        Assert.True(AiLookupCatalog.Allows(Roles.Requester, true, "evaluation_progress"));
    }

    [Fact]
    public void A_seat_never_opens_the_admin_tools()
    {
        Assert.False(AiLookupCatalog.Allows(Roles.Requester, true, "audit"));
        Assert.False(AiLookupCatalog.Allows(Roles.SourcingManager, true, "users"));
    }

    [Fact]
    public void An_unknown_role_gets_no_tools()
    {
        Assert.Empty(AiLookupCatalog.For("Nobody", false));
    }

    [Fact]
    public void Every_role_with_tools_has_examples()
    {
        foreach (var role in new[] { Roles.Admin, Roles.Requester, Roles.SourcingOfficer, Roles.EvaluationCommittee, Roles.SourcingManager, Roles.TopCommittee, Roles.Supplier })
            Assert.False(string.IsNullOrWhiteSpace(AiLookupCatalog.Examples(role)), role);
    }
}
