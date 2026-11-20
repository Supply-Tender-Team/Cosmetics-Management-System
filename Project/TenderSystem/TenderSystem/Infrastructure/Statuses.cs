namespace TenderSystem.Infrastructure;

/// <summary>
/// Every allowed status / type value of the database, as constants.
/// Use these instead of typing strings: Statuses.TenderStatus.Published (never "Published" by hand).
/// Generated from the design in docs-site (Schema Diagram). If a value must change, tell the lead.
/// </summary>
public static class Statuses
{
    public static class RequestPriority
    {
        public const string Low = "Low";
        public const string Medium = "Medium";
        public const string High = "High";
        public const string Urgent = "Urgent";
    }

    public static class RequestStatus
    {
        public const string Draft = "Draft";
        public const string Submitted = "Submitted";
        public const string UnderReview = "Under Review";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Returned = "Returned";
        public const string ConvertedToTender = "Converted to Tender";
    }

    public static class TenderStatus
    {
        public const string Draft = "Draft";
        public const string Published = "Published";
        public const string Open = "Open";
        public const string Closed = "Closed";
        public const string UnderEvaluation = "Under Evaluation";
        public const string AwaitingManagerReview = "Awaiting Manager Review";
        public const string AwaitingCommitteeDecision = "Awaiting Committee Decision";
        public const string Awarded = "Awarded";
        public const string Cancelled = "Cancelled";
        public const string Rejected = "Rejected";
        public const string RetenderRequired = "Retender Required";
    }

    public static class ParticipationStatus
    {
        public const string Selected = "Selected";
        public const string Participating = "Participating";
        public const string Withdrawn = "Withdrawn";
    }

    public static class InvitationStatus
    {
        public const string Pending = "Pending";
        public const string Accepted = "Accepted";
        public const string Declined = "Declined";
        public const string Expired = "Expired";
    }

    public static class CriterionType
    {
        public const string Technical = "Technical";
        public const string Financial = "Financial";
    }

    public static class CommitteeRole
    {
        public const string SourcingManager = "SourcingManager";
        public const string Department = "Department";
        public const string Financial = "Financial";
        public const string Technical = "Technical";
    }

    public static class BidStatus
    {
        public const string Draft = "Draft";
        public const string Submitted = "Submitted";
        public const string UnderReview = "Under Review";
        public const string Compliant = "Compliant";
        public const string NonCompliant = "Non-Compliant";
        public const string Accepted = "Accepted";
        public const string Rejected = "Rejected";
        public const string Withdrawn = "Withdrawn";
    }

    public static class ComplianceResult
    {
        public const string Compliant = "Compliant";
        public const string NonCompliant = "Non-Compliant";
        public const string PendingClarification = "Pending Clarification";
    }

    public static class ReportStatus
    {
        public const string RecommendationPending = "Recommendation Pending";
        public const string ManagerApproved = "Manager Approved";
        public const string ManagerRejected = "Manager Rejected";
        public const string ReturnedForReview = "Returned for Review";
        public const string CommitteeApproved = "Committee Approved";
        public const string CommitteeRejected = "Committee Rejected";
    }

    public static class ApprovalLevel
    {
        public const string Manager = "Manager";
        public const string TopCommittee = "TopCommittee";
    }

    public static class ApprovalDecision
    {
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Returned = "Returned";
        public const string RetenderRequired = "Retender Required";
    }

    public static class VoteType
    {
        public const string Approve = "Approve";
        public const string Reject = "Reject";
        public const string NeedMoreInfo = "NeedMoreInfo";
        public const string Retender = "Retender";
    }

    public static class SupplierStatus
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Suspended = "Suspended";
    }

    public static class ClarificationStatus
    {
        public const string Pending = "Pending";
        public const string Answered = "Answered";
        public const string Resolved = "Resolved";
    }

    public static class UserTypes
    {
        public const string Employee = "Employee";
        public const string Supplier = "Supplier";
    }

    public static class OnboardingStatus
    {
        public const string NotStarted = "NotStarted";
        public const string InProgress = "InProgress";
        public const string Completed = "Completed";
    }

    public static class AuthMethod
    {
        public const string Face = "Face";
        public const string Fingerprint = "Fingerprint";
        public const string Password = "Password";
    }

    public static class AuthenticatorType
    {
        public const string Platform = "platform";
        public const string CrossPlatform = "cross-platform";
    }

    public static class DocumentAction
    {
        public const string View = "View";
        public const string Download = "Download";
    }
}
