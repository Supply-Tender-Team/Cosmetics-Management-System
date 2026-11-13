/*
  Single source of truth for the database design.
  Schema page, ER page and SQL page all read from here.

  Column mini-format:  "Name type FLAGS..."
    PK        primary key (two PK columns = composite key)
    UQ        unique
    NULL      nullable (everything else is NOT NULL)
    FK>Table  foreign key to Table.Id
    CK:A|B_C  CHECK constraint with allowed values ("_" = space)
    DEF:xxx   default value
    ~verb_x   relationship name used in the ER diagram
*/
(function (root) {
  const domains = {
    identity:  { label: 'Users & Identity',        color: '#1d4ed8' },
    biometric: { label: 'Biometrics (required)',   color: '#be185d' },
    request:   { label: 'Purchase Requests',       color: '#c2410c' },
    tender:    { label: 'Tenders',                 color: '#15803d' },
    bid:       { label: 'Supplier Bids',           color: '#7e22ce' },
    eval:      { label: 'Evaluation & Decisions',  color: '#0f766e' },
    collab:    { label: 'Communication & Audit',   color: '#b45309' },
  };

  const raw = [
    // ───────────── Users & Identity ─────────────
    ['Departments', 'identity', 'Company departments such as Production, Packaging, R&D, Marketing, Finance.', [
      'Id int PK', 'Name nvarchar(100) UQ', 'Code nvarchar(20) UQ', 'IsActive bit',
    ]],
    ['AspNetUsers', 'identity', 'Every login account (employees and suppliers). Managed by ASP.NET Core Identity plus our own columns.', [
      'Id nvarchar(450) PK', 'UserName nvarchar(256)', 'NormalizedUserName nvarchar(256) UQ',
      'Email nvarchar(256) UQ', 'NormalizedEmail nvarchar(256)', 'EmailConfirmed bit',
      'PasswordHash nvarchar(max) NULL', 'SecurityStamp nvarchar(max) NULL', 'ConcurrencyStamp nvarchar(max) NULL',
      'PhoneNumber nvarchar(30) NULL', 'TwoFactorEnabled bit', 'LockoutEnd datetimeoffset NULL',
      'LockoutEnabled bit DEF:1', 'AccessFailedCount int',
      'FullName nvarchar(150)', 'UserType nvarchar(20) CK:Employee|Supplier',
      'DepartmentId int FK>Departments NULL ~works_in', 'JobTitle nvarchar(100) NULL',
      'IsActive bit', 'MustChangePassword bit',
      'OnboardingStatus nvarchar(30) CK:NotStarted|InProgress|Completed DEF:\'NotStarted\'',
      'BiometricConsentAt datetime2 NULL', 'CreatedAt datetime2', 'LastLoginAt datetime2 NULL',
    ]],
    ['AspNetRoles', 'identity', 'Roles: Admin, Requester, SourcingOfficer, EvaluationCommittee, SourcingManager, TopCommittee, Supplier.', [
      'Id nvarchar(450) PK', 'Name nvarchar(256) UQ', 'NormalizedName nvarchar(256)', 'ConcurrencyStamp nvarchar(max) NULL',
    ]],
    ['AspNetUserRoles', 'identity', 'Links users to roles (many-to-many).', [
      'UserId nvarchar(450) PK FK>AspNetUsers ~has_role', 'RoleId nvarchar(450) PK FK>AspNetRoles ~assigned_to',
    ]],
    ['EmployeeProfiles', 'identity', 'Extra data every employee fills during onboarding.', [
      'Id int PK', 'UserId nvarchar(450) UQ FK>AspNetUsers ~has_profile', 'EmployeeNumber nvarchar(30) UQ',
      'HireDate date', 'WorkPhone nvarchar(30)', 'PreferredLanguage nvarchar(10) DEF:\'en\'',
      'ProfilePhotoPath nvarchar(300) NULL', 'SignaturePath nvarchar(300) NULL',
      'OnboardingCompletedAt datetime2 NULL',
    ]],
    ['SupplierProfiles', 'identity', 'Supplier company details and Admin verification status.', [
      'Id int PK', 'UserId nvarchar(450) UQ FK>AspNetUsers ~owns_company',
      'CompanyName nvarchar(200)', 'TaxNumber nvarchar(50) UQ', 'CommercialRegNumber nvarchar(50)',
      'Country nvarchar(80)', 'City nvarchar(80)', 'Address nvarchar(300)',
      'ContactPerson nvarchar(150)', 'ContactPhone nvarchar(30)', 'Website nvarchar(200) NULL',
      'Category nvarchar(100)', 'Status nvarchar(20) CK:Pending|Approved|Rejected|Suspended DEF:\'Pending\'',
      'StatusReason nvarchar(500) NULL', 'ReviewedByUserId nvarchar(450) FK>AspNetUsers NULL ~reviewed_by',
      'ReviewedAt datetime2 NULL', 'CreatedAt datetime2',
    ]],
    ['SupplierDocuments', 'identity', 'Legal files uploaded by a supplier (license, tax certificate, commercial register).', [
      'Id int PK', 'SupplierProfileId int FK>SupplierProfiles ~uploads', 'DocType nvarchar(50)',
      'FilePath nvarchar(300)', 'ExpiryDate date NULL', 'UploadedAt datetime2',
    ]],

    // ───────────── Biometrics ─────────────
    ['BiometricCredentials', 'biometric', 'FINGERPRINT (and device unlock). One row per registered WebAuthn passkey. Only the PUBLIC key is stored.', [
      'Id int PK', 'UserId nvarchar(450) FK>AspNetUsers ~registers', 'CredentialId varbinary(900) UQ',
      'PublicKey varbinary(max)', 'SignCount bigint', 'DeviceName nvarchar(100)',
      'AuthenticatorType nvarchar(30) CK:platform|cross-platform', 'Transports nvarchar(100) NULL',
      'CreatedAt datetime2', 'LastUsedAt datetime2 NULL', 'IsRevoked bit',
    ]],
    ['FaceEnrollments', 'biometric', 'FACE. Stores an encrypted face template (numeric vector), never the photo.', [
      'Id int PK', 'UserId nvarchar(450) FK>AspNetUsers ~enrolls_face', 'FaceTemplate varbinary(max)',
      'ModelVersion nvarchar(50)', 'LivenessScore decimal(5,4)', 'CreatedAt datetime2',
      'UpdatedAt datetime2 NULL', 'IsActive bit DEF:1',
    ]],
    ['BiometricAuthLogs', 'biometric', 'Every login attempt (face, fingerprint, password) with result, for security and audit.', [
      'Id bigint PK', 'UserId nvarchar(450) FK>AspNetUsers NULL ~attempted_by',
      'Method nvarchar(20) CK:Face|Fingerprint|Password', 'Success bit', 'FailureReason nvarchar(200) NULL',
      'IpAddress nvarchar(45)', 'UserAgent nvarchar(300) NULL', 'CreatedAt datetime2',
    ]],

    // ───────────── Purchase Requests ─────────────
    ['PurchaseRequisitions', 'request', 'A purchase request created by a department.', [
      'Id int PK', 'RequestNumber nvarchar(30) UQ', 'Title nvarchar(200)', 'Reason nvarchar(1000)',
      'Priority nvarchar(10) CK:Low|Medium|High|Urgent', 'TargetDate date',
      'Status nvarchar(30) CK:Draft|Submitted|Under_Review|Approved|Rejected|Returned|Converted_to_Tender DEF:\'Draft\'',
      'DepartmentId int FK>Departments ~raised_by', 'RequestedByUserId nvarchar(450) FK>AspNetUsers ~created_by',
      'ReviewedByUserId nvarchar(450) FK>AspNetUsers NULL ~reviewed_by', 'ReviewNote nvarchar(1000) NULL',
      'EstimatedBudget decimal(18,2) NULL', 'CreatedAt datetime2', 'SubmittedAt datetime2 NULL',
    ]],
    ['PurchaseRequisitionItems', 'request', 'The lines of a request: item, quantity, specification.', [
      'Id int PK', 'RequisitionId int FK>PurchaseRequisitions ~contains', 'ItemName nvarchar(200)',
      'Quantity decimal(18,2)', 'Unit nvarchar(30)', 'Specifications nvarchar(max)',
    ]],

    // ───────────── Tenders ─────────────
    ['Tenders', 'tender', 'A tender created from an approved request.', [
      'Id int PK', 'TenderNumber nvarchar(30) UQ', 'RequisitionId int FK>PurchaseRequisitions ~created_from',
      'Title nvarchar(200)', 'Description nvarchar(max)', 'Terms nvarchar(max)',
      'Status nvarchar(40) CK:Draft|Published|Open|Closed|Under_Evaluation|Awaiting_Manager_Review|Awaiting_Committee_Decision|Awarded|Cancelled DEF:\'Draft\'',
      'PublishDate datetime2 NULL', 'SubmissionDeadline datetime2', 'EstimatedValue decimal(18,2)',
      'RequiresTopCommittee bit', 'CreatedByUserId nvarchar(450) FK>AspNetUsers ~managed_by',
      'AwardedSupplierId int FK>SupplierProfiles NULL ~awarded_to', 'CreatedAt datetime2',
    ]],
    ['TenderSuppliers', 'tender', 'Which suppliers were selected for a tender (many-to-many).', [
      'TenderId int PK FK>Tenders ~includes', 'SupplierProfileId int PK FK>SupplierProfiles ~is_selected_in',
      'AddedAt datetime2', 'ParticipationStatus nvarchar(20) CK:Selected|Participating|Withdrawn DEF:\'Selected\'',
    ]],
    ['Invitations', 'tender', 'The invitation sent to a supplier and the supplier\'s answer.', [
      'Id int PK', 'TenderId int FK>Tenders ~sends', 'SupplierProfileId int FK>SupplierProfiles ~receives',
      'Status nvarchar(20) CK:Pending|Accepted|Declined|Expired DEF:\'Pending\'',
      'SentAt datetime2', 'ExpiresAt datetime2', 'RespondedAt datetime2 NULL', 'DeclineReason nvarchar(500) NULL',
    ]],
    ['EvaluationCriteria', 'tender', 'Scoring criteria and weights. Locked before bids are opened.', [
      'Id int PK', 'TenderId int FK>Tenders ~defines', 'Name nvarchar(150)',
      'Type nvarchar(20) CK:Technical|Financial', 'WeightPercent decimal(5,2)', 'MaxScore int', 'IsLocked bit',
    ]],
    ['CommitteeMembers', 'tender', 'The 4 evaluation committee members of a tender.', [
      'Id int PK', 'TenderId int FK>Tenders ~has_committee', 'UserId nvarchar(450) FK>AspNetUsers ~serves_on',
      'MemberRole nvarchar(30) CK:SourcingManager|Department|Financial|Technical',
    ]],

    // ───────────── Bids ─────────────
    ['Bids', 'bid', 'One supplier offer for one tender.', [
      'Id int PK', 'TenderId int FK>Tenders ~receives', 'SupplierProfileId int FK>SupplierProfiles ~submits',
      'Status nvarchar(20) CK:Draft|Submitted|Under_Review|Compliant|Non-Compliant|Accepted|Rejected|Withdrawn DEF:\'Draft\'',
      'TotalPrice decimal(18,2)', 'Currency nvarchar(3)', 'DeliveryDays int', 'ValidityDays int',
      'PaymentTerms nvarchar(300)', 'TechnicalSummary nvarchar(max)', 'SubmittedAt datetime2 NULL',
    ]],
    ['BidItems', 'bid', 'Itemized prices inside a bid.', [
      'Id int PK', 'BidId int FK>Bids ~itemizes', 'RequisitionItemId int FK>PurchaseRequisitionItems ~prices',
      'UnitPrice decimal(18,2)', 'Quantity decimal(18,2)', 'TaxAmount decimal(18,2)',
      'ShippingCost decimal(18,2)', 'InstallationCost decimal(18,2) NULL', 'OfferedSpecifications nvarchar(max)',
    ]],
    ['BidDocuments', 'bid', 'Files attached to a bid (datasheets, images, videos, samples).', [
      'Id int PK', 'BidId int FK>Bids ~attaches', 'DocType nvarchar(50)', 'FileName nvarchar(255)',
      'FilePath nvarchar(300)', 'ContentType nvarchar(100)', 'SizeBytes bigint', 'UploadedAt datetime2',
    ]],
    ['ComplianceChecks', 'bid', 'Mandatory-requirement check result for a bid.', [
      'Id int PK', 'BidId int FK>Bids ~is_checked_by', 'CheckedByUserId nvarchar(450) FK>AspNetUsers ~performs',
      'Result nvarchar(30) CK:Compliant|Non-Compliant|Pending_Clarification', 'Reason nvarchar(1000) NULL', 'CheckedAt datetime2',
    ]],

    // ───────────── Evaluation & Decisions ─────────────
    ['TechnicalEvaluations', 'eval', 'Technical score per bid and per criterion.', [
      'Id int PK', 'BidId int FK>Bids ~scored_in', 'CriterionId int FK>EvaluationCriteria ~uses',
      'EvaluatorUserId nvarchar(450) FK>AspNetUsers ~scores', 'Score decimal(5,2)', 'Notes nvarchar(1000) NULL', 'EvaluatedAt datetime2',
    ]],
    ['FinancialEvaluations', 'eval', 'Financial analysis of a bid (total cost, taxes, logistics).', [
      'Id int PK', 'BidId int FK>Bids ~analysed_in', 'EvaluatorUserId nvarchar(450) FK>AspNetUsers ~analyses',
      'Score decimal(5,2)', 'TotalCost decimal(18,2)', 'TaxesAndFees decimal(18,2)', 'LogisticsCost decimal(18,2)',
      'Notes nvarchar(1000) NULL', 'EvaluatedAt datetime2',
    ]],
    ['AiBidAnalyses', 'eval', 'Ollama output. Request and response JSON are stored as text (no MongoDB needed).', [
      'Id int PK', 'BidId int FK>Bids ~analysed_by_ai', 'TenderId int FK>Tenders ~ai_for',
      'Model nvarchar(100)', 'RequestJson nvarchar(max)', 'ResponseJson nvarchar(max)',
      'ExtractedPrice decimal(18,2) NULL', 'ExtractedDeliveryDays int NULL', 'MissingFiles nvarchar(max) NULL',
      'Strengths nvarchar(max) NULL', 'Weaknesses nvarchar(max) NULL', 'AiRecommendation nvarchar(max) NULL',
      'RequestedByUserId nvarchar(450) FK>AspNetUsers ~requested_by', 'CreatedAt datetime2',
    ]],
    ['EvaluationReports', 'eval', 'The ONE unified report with the committee recommendation.', [
      'Id int PK', 'TenderId int FK>Tenders ~summarised_in',
      'Status nvarchar(30) CK:Recommendation_Pending|Manager_Approved|Manager_Rejected|Returned_for_Review|Committee_Approved|Committee_Rejected DEF:\'Recommendation_Pending\'',
      'RecommendedBidId int FK>Bids NULL ~recommends', 'TechnicalSummary nvarchar(max)', 'FinancialSummary nvarchar(max)',
      'Justification nvarchar(max)', 'PreparedByUserId nvarchar(450) FK>AspNetUsers ~prepared_by',
      'CreatedAt datetime2', 'SubmittedAt datetime2 NULL',
    ]],
    ['TenderApprovals', 'eval', 'Manager and Top Committee decisions with comments.', [
      'Id int PK', 'TenderId int FK>Tenders ~is_decided_in', 'ReportId int FK>EvaluationReports ~decides_on',
      'DecidedByUserId nvarchar(450) FK>AspNetUsers ~decides', 'Level nvarchar(20) CK:Manager|TopCommittee',
      'Decision nvarchar(20) CK:Approved|Rejected|Returned', 'SelectedBidId int FK>Bids NULL ~selects',
      'Comment nvarchar(1000)', 'SignatureHash nvarchar(200) NULL', 'DecidedAt datetime2',
    ]],
    ['TopCommitteeVotes', 'eval', 'Each Top Committee member\'s vote (group decision record).', [
      'Id int PK', 'TenderId int FK>Tenders ~is_voted_in', 'MemberUserId nvarchar(450) FK>AspNetUsers ~casts',
      'SelectedBidId int FK>Bids NULL ~votes_for', 'Vote nvarchar(20) CK:Approve|Reject|NeedMoreInfo',
      'Comment nvarchar(1000) NULL', 'VotedAt datetime2',
    ]],

    // ───────────── Communication & Audit ─────────────
    ['Clarifications', 'collab', 'A question between two allowed roles, linked to a request, tender or bid.', [
      'Id int PK', 'Subject nvarchar(200)', 'Description nvarchar(2000)',
      'Status nvarchar(20) CK:Pending|Answered|Resolved DEF:\'Pending\'',
      'SenderUserId nvarchar(450) FK>AspNetUsers ~asks', 'ReceiverUserId nvarchar(450) FK>AspNetUsers ~answers',
      'RequisitionId int FK>PurchaseRequisitions NULL ~about_request', 'TenderId int FK>Tenders NULL ~about_tender',
      'BidId int FK>Bids NULL ~about_bid', 'CreatedAt datetime2', 'ResolvedAt datetime2 NULL',
    ]],
    ['ClarificationMessages', 'collab', 'The replies and follow-ups inside a clarification thread.', [
      'Id int PK', 'ClarificationId int FK>Clarifications ~has_messages', 'SenderUserId nvarchar(450) FK>AspNetUsers ~writes',
      'Body nvarchar(2000)', 'AttachmentPath nvarchar(300) NULL', 'SentAt datetime2',
    ]],
    ['Notifications', 'collab', 'In-app alerts for users.', [
      'Id int PK', 'UserId nvarchar(450) FK>AspNetUsers ~notifies', 'Type nvarchar(40)', 'Title nvarchar(150)',
      'Message nvarchar(500)', 'Link nvarchar(300) NULL', 'IsRead bit', 'CreatedAt datetime2',
    ]],
    ['AuditLogs', 'collab', 'Who did what and when (every offer, decision, approval).', [
      'Id bigint PK', 'UserId nvarchar(450) FK>AspNetUsers NULL ~performs_action', 'Action nvarchar(100)',
      'EntityName nvarchar(100)', 'EntityId nvarchar(50)', 'OldValues nvarchar(max) NULL', 'NewValues nvarchar(max) NULL',
      'IpAddress nvarchar(45)', 'CreatedAt datetime2',
    ]],
    ['DocumentAccessLogs', 'collab', 'Optional: who viewed or downloaded a document (used by the watermark feature).', [
      'Id bigint PK', 'UserId nvarchar(450) FK>AspNetUsers ~accesses', 'DocumentKind nvarchar(40)',
      'DocumentId int', 'Action nvarchar(20) CK:View|Download', 'CreatedAt datetime2',
    ]],
  ];

  function parseCol(s) {
    const p = s.split(' ');
    const c = { name: p[0], type: p[1], pk: false, uq: false, nullable: false, fk: null, ck: null, def: null, verb: null };
    for (const t of p.slice(2)) {
      if (t === 'PK') c.pk = true;
      else if (t === 'UQ') c.uq = true;
      else if (t === 'NULL') c.nullable = true;
      else if (t.startsWith('FK>')) c.fk = t.slice(3);
      else if (t.startsWith('CK:')) c.ck = t.slice(3).split('|').map(x => x.replace(/_/g, ' '));
      else if (t.startsWith('DEF:')) c.def = t.slice(4).replace(/Recommendation_Pending/, 'Recommendation Pending');
      else if (t.startsWith('~')) c.verb = t.slice(1).replace(/_/g, ' ');
    }
    return c;
  }

  const tables = raw.map(([name, domain, desc, cols]) => ({ name, domain, desc, cols: cols.map(parseCol) }));
  const byName = Object.fromEntries(tables.map(t => [t.name, t]));

  const relations = [];
  for (const t of tables)
    for (const c of t.cols)
      if (c.fk) relations.push({
        child: t.name, childCol: c.name, parent: c.fk, parentCol: 'Id',
        oneToOne: c.uq, nullable: c.nullable, verb: c.verb || 'references',
      });

  const api = { domains, tables, byName, relations };
  if (typeof module !== 'undefined') module.exports = api; else root.DB = api;
})(typeof window !== 'undefined' ? window : globalThis);
