/* =====================================================================
   Sourcing & Tender Management System - SQL Server reference script
   Generated from the same design used by the Schema and ER diagrams.

   NOTE: In the real project the database is created by EF Core migrations
   (dotnet ef database update). Use this script to READ and discuss the
   design, or to build a quick playground database.
   ===================================================================== */

IF DB_ID(N'TenderSystemDb') IS NULL CREATE DATABASE TenderSystemDb;
GO
USE TenderSystemDb;
GO

/* ---------------------------------------------------------------
   USERS & IDENTITY
   --------------------------------------------------------------- */
-- Company departments such as Production, Packaging, R&D, Marketing, Finance.
CREATE TABLE dbo.[Departments] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [Name]                   nvarchar(100) NOT NULL,
    [Code]                   nvarchar(20) NOT NULL,
    [IsActive]               bit NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT 1,
    CONSTRAINT PK_Departments PRIMARY KEY ([Id]),
    CONSTRAINT UQ_Departments_Name UNIQUE ([Name]),
    CONSTRAINT UQ_Departments_Code UNIQUE ([Code])
);
GO

-- Every login account (employees and suppliers). Managed by ASP.NET Core Identity plus our own columns.
CREATE TABLE dbo.[AspNetUsers] (
    [Id]                     nvarchar(450) NOT NULL,
    [UserName]               nvarchar(256) NOT NULL,
    [NormalizedUserName]     nvarchar(256) NOT NULL,
    [Email]                  nvarchar(256) NOT NULL,
    [NormalizedEmail]        nvarchar(256) NOT NULL,
    [EmailConfirmed]         bit NOT NULL CONSTRAINT DF_AspNetUsers_EmailConfirmed DEFAULT 0,
    [PasswordHash]           nvarchar(max) NULL,
    [SecurityStamp]          nvarchar(max) NULL,
    [ConcurrencyStamp]       nvarchar(max) NULL,
    [PhoneNumber]            nvarchar(30) NULL,
    [TwoFactorEnabled]       bit NOT NULL CONSTRAINT DF_AspNetUsers_TwoFactorEnabled DEFAULT 0,
    [LockoutEnd]             datetimeoffset NULL,
    [LockoutEnabled]         bit NOT NULL CONSTRAINT DF_AspNetUsers_LockoutEnabled DEFAULT 1,
    [AccessFailedCount]      int NOT NULL,
    [FullName]               nvarchar(150) NOT NULL,
    [UserType]               nvarchar(20) NOT NULL,
    [DepartmentId]           int NULL,
    [JobTitle]               nvarchar(100) NULL,
    [IsActive]               bit NOT NULL CONSTRAINT DF_AspNetUsers_IsActive DEFAULT 1,
    [MustChangePassword]     bit NOT NULL CONSTRAINT DF_AspNetUsers_MustChangePassword DEFAULT 0,
    [OnboardingStatus]       nvarchar(30) NOT NULL CONSTRAINT DF_AspNetUsers_OnboardingStatus DEFAULT 'NotStarted',
    [BiometricConsentAt]     datetime2 NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_AspNetUsers_CreatedAt DEFAULT SYSUTCDATETIME(),
    [LastLoginAt]            datetime2 NULL,
    CONSTRAINT PK_AspNetUsers PRIMARY KEY ([Id]),
    CONSTRAINT UQ_AspNetUsers_NormalizedUserName UNIQUE ([NormalizedUserName]),
    CONSTRAINT UQ_AspNetUsers_Email UNIQUE ([Email]),
    CONSTRAINT CK_AspNetUsers_UserType CHECK ([UserType] IN (N'Employee', N'Supplier')),
    CONSTRAINT CK_AspNetUsers_OnboardingStatus CHECK ([OnboardingStatus] IN (N'NotStarted', N'InProgress', N'Completed'))
);
GO

-- Roles: Admin, Requester, SourcingOfficer, EvaluationCommittee, SourcingManager, TopCommittee, Supplier.
CREATE TABLE dbo.[AspNetRoles] (
    [Id]                     nvarchar(450) NOT NULL,
    [Name]                   nvarchar(256) NOT NULL,
    [NormalizedName]         nvarchar(256) NOT NULL,
    [ConcurrencyStamp]       nvarchar(max) NULL,
    CONSTRAINT PK_AspNetRoles PRIMARY KEY ([Id]),
    CONSTRAINT UQ_AspNetRoles_Name UNIQUE ([Name])
);
GO

-- Links users to roles (many-to-many).
CREATE TABLE dbo.[AspNetUserRoles] (
    [UserId]                 nvarchar(450) NOT NULL,
    [RoleId]                 nvarchar(450) NOT NULL,
    CONSTRAINT PK_AspNetUserRoles PRIMARY KEY ([UserId], [RoleId])
);
GO

-- Extra data every employee fills during onboarding.
CREATE TABLE dbo.[EmployeeProfiles] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [UserId]                 nvarchar(450) NOT NULL,
    [EmployeeNumber]         nvarchar(30) NOT NULL,
    [HireDate]               date NOT NULL,
    [WorkPhone]              nvarchar(30) NOT NULL,
    [PreferredLanguage]      nvarchar(10) NOT NULL CONSTRAINT DF_EmployeeProfiles_PreferredLanguage DEFAULT 'en',
    [ProfilePhotoPath]       nvarchar(300) NULL,
    [SignaturePath]          nvarchar(300) NULL,
    [OnboardingCompletedAt]  datetime2 NULL,
    CONSTRAINT PK_EmployeeProfiles PRIMARY KEY ([Id]),
    CONSTRAINT UQ_EmployeeProfiles_UserId UNIQUE ([UserId]),
    CONSTRAINT UQ_EmployeeProfiles_EmployeeNumber UNIQUE ([EmployeeNumber])
);
GO

-- Supplier company details and Admin verification status.
CREATE TABLE dbo.[SupplierProfiles] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [UserId]                 nvarchar(450) NOT NULL,
    [CompanyName]            nvarchar(200) NOT NULL,
    [TaxNumber]              nvarchar(50) NOT NULL,
    [CommercialRegNumber]    nvarchar(50) NOT NULL,
    [Country]                nvarchar(80) NOT NULL,
    [City]                   nvarchar(80) NOT NULL,
    [Address]                nvarchar(300) NOT NULL,
    [ContactPerson]          nvarchar(150) NOT NULL,
    [ContactPhone]           nvarchar(30) NOT NULL,
    [Website]                nvarchar(200) NULL,
    [Category]               nvarchar(100) NOT NULL,
    [Status]                 nvarchar(20) NOT NULL CONSTRAINT DF_SupplierProfiles_Status DEFAULT 'Pending',
    [StatusReason]           nvarchar(500) NULL,
    [ReviewedByUserId]       nvarchar(450) NULL,
    [ReviewedAt]             datetime2 NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_SupplierProfiles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_SupplierProfiles PRIMARY KEY ([Id]),
    CONSTRAINT UQ_SupplierProfiles_UserId UNIQUE ([UserId]),
    CONSTRAINT UQ_SupplierProfiles_TaxNumber UNIQUE ([TaxNumber]),
    CONSTRAINT CK_SupplierProfiles_Status CHECK ([Status] IN (N'Pending', N'Approved', N'Rejected', N'Suspended'))
);
GO

-- Legal files uploaded by a supplier (license, tax certificate, commercial register).
CREATE TABLE dbo.[SupplierDocuments] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [SupplierProfileId]      int NOT NULL,
    [DocType]                nvarchar(50) NOT NULL,
    [FilePath]               nvarchar(300) NOT NULL,
    [ExpiryDate]             date NULL,
    [UploadedAt]             datetime2 NOT NULL,
    CONSTRAINT PK_SupplierDocuments PRIMARY KEY ([Id])
);
GO

/* ---------------------------------------------------------------
   BIOMETRICS (REQUIRED)
   --------------------------------------------------------------- */
-- FINGERPRINT (and device unlock). One row per registered WebAuthn passkey. Only the PUBLIC key is stored.
CREATE TABLE dbo.[BiometricCredentials] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [UserId]                 nvarchar(450) NOT NULL,
    [CredentialId]           varbinary(900) NOT NULL,
    [PublicKey]              varbinary(max) NOT NULL,
    [SignCount]              bigint NOT NULL,
    [DeviceName]             nvarchar(100) NOT NULL,
    [AuthenticatorType]      nvarchar(30) NOT NULL,
    [Transports]             nvarchar(100) NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_BiometricCredentials_CreatedAt DEFAULT SYSUTCDATETIME(),
    [LastUsedAt]             datetime2 NULL,
    [IsRevoked]              bit NOT NULL CONSTRAINT DF_BiometricCredentials_IsRevoked DEFAULT 0,
    CONSTRAINT PK_BiometricCredentials PRIMARY KEY ([Id]),
    CONSTRAINT UQ_BiometricCredentials_CredentialId UNIQUE ([CredentialId]),
    CONSTRAINT CK_BiometricCredentials_AuthenticatorType CHECK ([AuthenticatorType] IN (N'platform', N'cross-platform'))
);
GO

-- FACE. Stores an encrypted face template (numeric vector), never the photo.
CREATE TABLE dbo.[FaceEnrollments] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [UserId]                 nvarchar(450) NOT NULL,
    [FaceTemplate]           varbinary(max) NOT NULL,
    [ModelVersion]           nvarchar(50) NOT NULL,
    [LivenessScore]          decimal(5,4) NOT NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_FaceEnrollments_CreatedAt DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]              datetime2 NULL,
    [IsActive]               bit NOT NULL CONSTRAINT DF_FaceEnrollments_IsActive DEFAULT 1,
    CONSTRAINT PK_FaceEnrollments PRIMARY KEY ([Id])
);
GO

-- Every login attempt (face, fingerprint, password) with result, for security and audit.
CREATE TABLE dbo.[BiometricAuthLogs] (
    [Id]                     bigint IDENTITY(1,1) NOT NULL,
    [UserId]                 nvarchar(450) NULL,
    [Method]                 nvarchar(20) NOT NULL,
    [Success]                bit NOT NULL CONSTRAINT DF_BiometricAuthLogs_Success DEFAULT 0,
    [FailureReason]          nvarchar(200) NULL,
    [IpAddress]              nvarchar(45) NOT NULL,
    [UserAgent]              nvarchar(300) NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_BiometricAuthLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BiometricAuthLogs PRIMARY KEY ([Id]),
    CONSTRAINT CK_BiometricAuthLogs_Method CHECK ([Method] IN (N'Face', N'Fingerprint', N'Password'))
);
GO

/* ---------------------------------------------------------------
   PURCHASE REQUESTS
   --------------------------------------------------------------- */
-- A purchase request created by a department.
CREATE TABLE dbo.[PurchaseRequisitions] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [RequestNumber]          nvarchar(30) NOT NULL,
    [Title]                  nvarchar(200) NOT NULL,
    [Reason]                 nvarchar(1000) NOT NULL,
    [Priority]               nvarchar(10) NOT NULL,
    [TargetDate]             date NOT NULL,
    [Status]                 nvarchar(30) NOT NULL CONSTRAINT DF_PurchaseRequisitions_Status DEFAULT 'Draft',
    [DepartmentId]           int NOT NULL,
    [RequestedByUserId]      nvarchar(450) NOT NULL,
    [ReviewedByUserId]       nvarchar(450) NULL,
    [ReviewNote]             nvarchar(1000) NULL,
    [EstimatedBudget]        decimal(18,2) NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_PurchaseRequisitions_CreatedAt DEFAULT SYSUTCDATETIME(),
    [SubmittedAt]            datetime2 NULL,
    CONSTRAINT PK_PurchaseRequisitions PRIMARY KEY ([Id]),
    CONSTRAINT UQ_PurchaseRequisitions_RequestNumber UNIQUE ([RequestNumber]),
    CONSTRAINT CK_PurchaseRequisitions_Priority CHECK ([Priority] IN (N'Low', N'Medium', N'High', N'Urgent')),
    CONSTRAINT CK_PurchaseRequisitions_Status CHECK ([Status] IN (N'Draft', N'Submitted', N'Under Review', N'Approved', N'Rejected', N'Returned', N'Converted to Tender'))
);
GO

-- The lines of a request: item, quantity, specification.
CREATE TABLE dbo.[PurchaseRequisitionItems] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [RequisitionId]          int NOT NULL,
    [ItemName]               nvarchar(200) NOT NULL,
    [Quantity]               decimal(18,2) NOT NULL,
    [Unit]                   nvarchar(30) NOT NULL,
    [Specifications]         nvarchar(max) NOT NULL,
    CONSTRAINT PK_PurchaseRequisitionItems PRIMARY KEY ([Id])
);
GO

/* ---------------------------------------------------------------
   TENDERS
   --------------------------------------------------------------- */
-- A tender created from an approved request.
CREATE TABLE dbo.[Tenders] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [TenderNumber]           nvarchar(30) NOT NULL,
    [RequisitionId]          int NOT NULL,
    [Title]                  nvarchar(200) NOT NULL,
    [Description]            nvarchar(max) NOT NULL,
    [Terms]                  nvarchar(max) NOT NULL,
    [Status]                 nvarchar(40) NOT NULL CONSTRAINT DF_Tenders_Status DEFAULT 'Draft',
    [PublishDate]            datetime2 NULL,
    [SubmissionDeadline]     datetime2 NOT NULL,
    [EstimatedValue]         decimal(18,2) NOT NULL,
    [RequiresTopCommittee]   bit NOT NULL CONSTRAINT DF_Tenders_RequiresTopCommittee DEFAULT 0,
    [CreatedByUserId]        nvarchar(450) NOT NULL,
    [AwardedSupplierId]      int NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_Tenders_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Tenders PRIMARY KEY ([Id]),
    CONSTRAINT UQ_Tenders_TenderNumber UNIQUE ([TenderNumber]),
    CONSTRAINT CK_Tenders_Status CHECK ([Status] IN (N'Draft', N'Published', N'Open', N'Closed', N'Under Evaluation', N'Awaiting Manager Review', N'Awaiting Committee Decision', N'Awarded', N'Cancelled'))
);
GO

-- Which suppliers were selected for a tender (many-to-many).
CREATE TABLE dbo.[TenderSuppliers] (
    [TenderId]               int NOT NULL,
    [SupplierProfileId]      int NOT NULL,
    [AddedAt]                datetime2 NOT NULL,
    [ParticipationStatus]    nvarchar(20) NOT NULL CONSTRAINT DF_TenderSuppliers_ParticipationStatus DEFAULT 'Selected',
    CONSTRAINT PK_TenderSuppliers PRIMARY KEY ([TenderId], [SupplierProfileId]),
    CONSTRAINT CK_TenderSuppliers_ParticipationStatus CHECK ([ParticipationStatus] IN (N'Selected', N'Participating', N'Withdrawn'))
);
GO

-- The invitation sent to a supplier and the supplier's answer.
CREATE TABLE dbo.[Invitations] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [TenderId]               int NOT NULL,
    [SupplierProfileId]      int NOT NULL,
    [Status]                 nvarchar(20) NOT NULL CONSTRAINT DF_Invitations_Status DEFAULT 'Pending',
    [SentAt]                 datetime2 NOT NULL,
    [ExpiresAt]              datetime2 NOT NULL,
    [RespondedAt]            datetime2 NULL,
    [DeclineReason]          nvarchar(500) NULL,
    CONSTRAINT PK_Invitations PRIMARY KEY ([Id]),
    CONSTRAINT CK_Invitations_Status CHECK ([Status] IN (N'Pending', N'Accepted', N'Declined', N'Expired'))
);
GO

-- Scoring criteria and weights. Locked before bids are opened.
CREATE TABLE dbo.[EvaluationCriteria] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [TenderId]               int NOT NULL,
    [Name]                   nvarchar(150) NOT NULL,
    [Type]                   nvarchar(20) NOT NULL,
    [WeightPercent]          decimal(5,2) NOT NULL,
    [MaxScore]               int NOT NULL,
    [IsLocked]               bit NOT NULL CONSTRAINT DF_EvaluationCriteria_IsLocked DEFAULT 0,
    CONSTRAINT PK_EvaluationCriteria PRIMARY KEY ([Id]),
    CONSTRAINT CK_EvaluationCriteria_Type CHECK ([Type] IN (N'Technical', N'Financial'))
);
GO

-- The 4 evaluation committee members of a tender.
CREATE TABLE dbo.[CommitteeMembers] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [TenderId]               int NOT NULL,
    [UserId]                 nvarchar(450) NOT NULL,
    [MemberRole]             nvarchar(30) NOT NULL,
    CONSTRAINT PK_CommitteeMembers PRIMARY KEY ([Id]),
    CONSTRAINT CK_CommitteeMembers_MemberRole CHECK ([MemberRole] IN (N'SourcingManager', N'Department', N'Financial', N'Technical'))
);
GO

/* ---------------------------------------------------------------
   SUPPLIER BIDS
   --------------------------------------------------------------- */
-- One supplier offer for one tender.
CREATE TABLE dbo.[Bids] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [TenderId]               int NOT NULL,
    [SupplierProfileId]      int NOT NULL,
    [Status]                 nvarchar(20) NOT NULL CONSTRAINT DF_Bids_Status DEFAULT 'Draft',
    [TotalPrice]             decimal(18,2) NOT NULL,
    [Currency]               nvarchar(3) NOT NULL,
    [DeliveryDays]           int NOT NULL,
    [ValidityDays]           int NOT NULL,
    [PaymentTerms]           nvarchar(300) NOT NULL,
    [TechnicalSummary]       nvarchar(max) NOT NULL,
    [SubmittedAt]            datetime2 NULL,
    CONSTRAINT PK_Bids PRIMARY KEY ([Id]),
    CONSTRAINT CK_Bids_Status CHECK ([Status] IN (N'Draft', N'Submitted', N'Under Review', N'Compliant', N'Non-Compliant', N'Accepted', N'Rejected', N'Withdrawn'))
);
GO

-- Itemized prices inside a bid.
CREATE TABLE dbo.[BidItems] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [BidId]                  int NOT NULL,
    [RequisitionItemId]      int NOT NULL,
    [UnitPrice]              decimal(18,2) NOT NULL,
    [Quantity]               decimal(18,2) NOT NULL,
    [TaxAmount]              decimal(18,2) NOT NULL,
    [ShippingCost]           decimal(18,2) NOT NULL,
    [InstallationCost]       decimal(18,2) NULL,
    [OfferedSpecifications]  nvarchar(max) NOT NULL,
    CONSTRAINT PK_BidItems PRIMARY KEY ([Id])
);
GO

-- Files attached to a bid (datasheets, images, videos, samples).
CREATE TABLE dbo.[BidDocuments] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [BidId]                  int NOT NULL,
    [DocType]                nvarchar(50) NOT NULL,
    [FileName]               nvarchar(255) NOT NULL,
    [FilePath]               nvarchar(300) NOT NULL,
    [ContentType]            nvarchar(100) NOT NULL,
    [SizeBytes]              bigint NOT NULL,
    [UploadedAt]             datetime2 NOT NULL,
    CONSTRAINT PK_BidDocuments PRIMARY KEY ([Id])
);
GO

-- Mandatory-requirement check result for a bid.
CREATE TABLE dbo.[ComplianceChecks] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [BidId]                  int NOT NULL,
    [CheckedByUserId]        nvarchar(450) NOT NULL,
    [Result]                 nvarchar(30) NOT NULL,
    [Reason]                 nvarchar(1000) NULL,
    [CheckedAt]              datetime2 NOT NULL,
    CONSTRAINT PK_ComplianceChecks PRIMARY KEY ([Id]),
    CONSTRAINT CK_ComplianceChecks_Result CHECK ([Result] IN (N'Compliant', N'Non-Compliant', N'Pending Clarification'))
);
GO

/* ---------------------------------------------------------------
   EVALUATION & DECISIONS
   --------------------------------------------------------------- */
-- Technical score per bid and per criterion.
CREATE TABLE dbo.[TechnicalEvaluations] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [BidId]                  int NOT NULL,
    [CriterionId]            int NOT NULL,
    [EvaluatorUserId]        nvarchar(450) NOT NULL,
    [Score]                  decimal(5,2) NOT NULL,
    [Notes]                  nvarchar(1000) NULL,
    [EvaluatedAt]            datetime2 NOT NULL,
    CONSTRAINT PK_TechnicalEvaluations PRIMARY KEY ([Id])
);
GO

-- Financial analysis of a bid (total cost, taxes, logistics).
CREATE TABLE dbo.[FinancialEvaluations] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [BidId]                  int NOT NULL,
    [EvaluatorUserId]        nvarchar(450) NOT NULL,
    [Score]                  decimal(5,2) NOT NULL,
    [TotalCost]              decimal(18,2) NOT NULL,
    [TaxesAndFees]           decimal(18,2) NOT NULL,
    [LogisticsCost]          decimal(18,2) NOT NULL,
    [Notes]                  nvarchar(1000) NULL,
    [EvaluatedAt]            datetime2 NOT NULL,
    CONSTRAINT PK_FinancialEvaluations PRIMARY KEY ([Id])
);
GO

-- Ollama output. Request and response JSON are stored as text (no MongoDB needed).
CREATE TABLE dbo.[AiBidAnalyses] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [BidId]                  int NOT NULL,
    [TenderId]               int NOT NULL,
    [Model]                  nvarchar(100) NOT NULL,
    [RequestJson]            nvarchar(max) NOT NULL,
    [ResponseJson]           nvarchar(max) NOT NULL,
    [ExtractedPrice]         decimal(18,2) NULL,
    [ExtractedDeliveryDays]  int NULL,
    [MissingFiles]           nvarchar(max) NULL,
    [Strengths]              nvarchar(max) NULL,
    [Weaknesses]             nvarchar(max) NULL,
    [AiRecommendation]       nvarchar(max) NULL,
    [RequestedByUserId]      nvarchar(450) NOT NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_AiBidAnalyses_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_AiBidAnalyses PRIMARY KEY ([Id])
);
GO

-- The ONE unified report with the committee recommendation.
CREATE TABLE dbo.[EvaluationReports] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [TenderId]               int NOT NULL,
    [Status]                 nvarchar(30) NOT NULL CONSTRAINT DF_EvaluationReports_Status DEFAULT 'Recommendation Pending',
    [RecommendedBidId]       int NULL,
    [TechnicalSummary]       nvarchar(max) NOT NULL,
    [FinancialSummary]       nvarchar(max) NOT NULL,
    [Justification]          nvarchar(max) NOT NULL,
    [PreparedByUserId]       nvarchar(450) NOT NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_EvaluationReports_CreatedAt DEFAULT SYSUTCDATETIME(),
    [SubmittedAt]            datetime2 NULL,
    CONSTRAINT PK_EvaluationReports PRIMARY KEY ([Id]),
    CONSTRAINT CK_EvaluationReports_Status CHECK ([Status] IN (N'Recommendation Pending', N'Manager Approved', N'Manager Rejected', N'Returned for Review', N'Committee Approved', N'Committee Rejected'))
);
GO

-- Manager and Top Committee decisions with comments.
CREATE TABLE dbo.[TenderApprovals] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [TenderId]               int NOT NULL,
    [ReportId]               int NOT NULL,
    [DecidedByUserId]        nvarchar(450) NOT NULL,
    [Level]                  nvarchar(20) NOT NULL,
    [Decision]               nvarchar(20) NOT NULL,
    [SelectedBidId]          int NULL,
    [Comment]                nvarchar(1000) NOT NULL,
    [SignatureHash]          nvarchar(200) NULL,
    [DecidedAt]              datetime2 NOT NULL,
    CONSTRAINT PK_TenderApprovals PRIMARY KEY ([Id]),
    CONSTRAINT CK_TenderApprovals_Level CHECK ([Level] IN (N'Manager', N'TopCommittee')),
    CONSTRAINT CK_TenderApprovals_Decision CHECK ([Decision] IN (N'Approved', N'Rejected', N'Returned'))
);
GO

-- Each Top Committee member's vote (group decision record).
CREATE TABLE dbo.[TopCommitteeVotes] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [TenderId]               int NOT NULL,
    [MemberUserId]           nvarchar(450) NOT NULL,
    [SelectedBidId]          int NULL,
    [Vote]                   nvarchar(20) NOT NULL,
    [Comment]                nvarchar(1000) NULL,
    [VotedAt]                datetime2 NOT NULL,
    CONSTRAINT PK_TopCommitteeVotes PRIMARY KEY ([Id]),
    CONSTRAINT CK_TopCommitteeVotes_Vote CHECK ([Vote] IN (N'Approve', N'Reject', N'NeedMoreInfo'))
);
GO

/* ---------------------------------------------------------------
   COMMUNICATION & AUDIT
   --------------------------------------------------------------- */
-- A question between two allowed roles, linked to a request, tender or bid.
CREATE TABLE dbo.[Clarifications] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [Subject]                nvarchar(200) NOT NULL,
    [Description]            nvarchar(2000) NOT NULL,
    [Status]                 nvarchar(20) NOT NULL CONSTRAINT DF_Clarifications_Status DEFAULT 'Pending',
    [SenderUserId]           nvarchar(450) NOT NULL,
    [ReceiverUserId]         nvarchar(450) NOT NULL,
    [RequisitionId]          int NULL,
    [TenderId]               int NULL,
    [BidId]                  int NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_Clarifications_CreatedAt DEFAULT SYSUTCDATETIME(),
    [ResolvedAt]             datetime2 NULL,
    CONSTRAINT PK_Clarifications PRIMARY KEY ([Id]),
    CONSTRAINT CK_Clarifications_Status CHECK ([Status] IN (N'Pending', N'Answered', N'Resolved'))
);
GO

-- The replies and follow-ups inside a clarification thread.
CREATE TABLE dbo.[ClarificationMessages] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [ClarificationId]        int NOT NULL,
    [SenderUserId]           nvarchar(450) NOT NULL,
    [Body]                   nvarchar(2000) NOT NULL,
    [AttachmentPath]         nvarchar(300) NULL,
    [SentAt]                 datetime2 NOT NULL,
    CONSTRAINT PK_ClarificationMessages PRIMARY KEY ([Id])
);
GO

-- In-app alerts for users.
CREATE TABLE dbo.[Notifications] (
    [Id]                     int IDENTITY(1,1) NOT NULL,
    [UserId]                 nvarchar(450) NOT NULL,
    [Type]                   nvarchar(40) NOT NULL,
    [Title]                  nvarchar(150) NOT NULL,
    [Message]                nvarchar(500) NOT NULL,
    [Link]                   nvarchar(300) NULL,
    [IsRead]                 bit NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT 0,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Notifications PRIMARY KEY ([Id])
);
GO

-- Who did what and when (every offer, decision, approval).
CREATE TABLE dbo.[AuditLogs] (
    [Id]                     bigint IDENTITY(1,1) NOT NULL,
    [UserId]                 nvarchar(450) NULL,
    [Action]                 nvarchar(100) NOT NULL,
    [EntityName]             nvarchar(100) NOT NULL,
    [EntityId]               nvarchar(50) NOT NULL,
    [OldValues]              nvarchar(max) NULL,
    [NewValues]              nvarchar(max) NULL,
    [IpAddress]              nvarchar(45) NOT NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_AuditLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_AuditLogs PRIMARY KEY ([Id])
);
GO

-- Optional: who viewed or downloaded a document (used by the watermark feature).
CREATE TABLE dbo.[DocumentAccessLogs] (
    [Id]                     bigint IDENTITY(1,1) NOT NULL,
    [UserId]                 nvarchar(450) NOT NULL,
    [DocumentKind]           nvarchar(40) NOT NULL,
    [DocumentId]             int NOT NULL,
    [Action]                 nvarchar(20) NOT NULL,
    [CreatedAt]              datetime2 NOT NULL CONSTRAINT DF_DocumentAccessLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_DocumentAccessLogs PRIMARY KEY ([Id]),
    CONSTRAINT CK_DocumentAccessLogs_Action CHECK ([Action] IN (N'View', N'Download'))
);
GO

/* ---------------------------------------------------------------
   FOREIGN KEYS (added after all tables exist)
   --------------------------------------------------------------- */
ALTER TABLE dbo.[AspNetUsers] ADD CONSTRAINT FK_AspNetUsers_DepartmentId
    FOREIGN KEY ([DepartmentId]) REFERENCES dbo.[Departments] ([Id]);
ALTER TABLE dbo.[AspNetUserRoles] ADD CONSTRAINT FK_AspNetUserRoles_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[AspNetUserRoles] ADD CONSTRAINT FK_AspNetUserRoles_RoleId
    FOREIGN KEY ([RoleId]) REFERENCES dbo.[AspNetRoles] ([Id]);
ALTER TABLE dbo.[EmployeeProfiles] ADD CONSTRAINT FK_EmployeeProfiles_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[SupplierProfiles] ADD CONSTRAINT FK_SupplierProfiles_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[SupplierProfiles] ADD CONSTRAINT FK_SupplierProfiles_ReviewedByUserId
    FOREIGN KEY ([ReviewedByUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[SupplierDocuments] ADD CONSTRAINT FK_SupplierDocuments_SupplierProfileId
    FOREIGN KEY ([SupplierProfileId]) REFERENCES dbo.[SupplierProfiles] ([Id]);
ALTER TABLE dbo.[BiometricCredentials] ADD CONSTRAINT FK_BiometricCredentials_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[FaceEnrollments] ADD CONSTRAINT FK_FaceEnrollments_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[BiometricAuthLogs] ADD CONSTRAINT FK_BiometricAuthLogs_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[PurchaseRequisitions] ADD CONSTRAINT FK_PurchaseRequisitions_DepartmentId
    FOREIGN KEY ([DepartmentId]) REFERENCES dbo.[Departments] ([Id]);
ALTER TABLE dbo.[PurchaseRequisitions] ADD CONSTRAINT FK_PurchaseRequisitions_RequestedByUserId
    FOREIGN KEY ([RequestedByUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[PurchaseRequisitions] ADD CONSTRAINT FK_PurchaseRequisitions_ReviewedByUserId
    FOREIGN KEY ([ReviewedByUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[PurchaseRequisitionItems] ADD CONSTRAINT FK_PurchaseRequisitionItems_RequisitionId
    FOREIGN KEY ([RequisitionId]) REFERENCES dbo.[PurchaseRequisitions] ([Id]);
ALTER TABLE dbo.[Tenders] ADD CONSTRAINT FK_Tenders_RequisitionId
    FOREIGN KEY ([RequisitionId]) REFERENCES dbo.[PurchaseRequisitions] ([Id]);
ALTER TABLE dbo.[Tenders] ADD CONSTRAINT FK_Tenders_CreatedByUserId
    FOREIGN KEY ([CreatedByUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[Tenders] ADD CONSTRAINT FK_Tenders_AwardedSupplierId
    FOREIGN KEY ([AwardedSupplierId]) REFERENCES dbo.[SupplierProfiles] ([Id]);
ALTER TABLE dbo.[TenderSuppliers] ADD CONSTRAINT FK_TenderSuppliers_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[TenderSuppliers] ADD CONSTRAINT FK_TenderSuppliers_SupplierProfileId
    FOREIGN KEY ([SupplierProfileId]) REFERENCES dbo.[SupplierProfiles] ([Id]);
ALTER TABLE dbo.[Invitations] ADD CONSTRAINT FK_Invitations_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[Invitations] ADD CONSTRAINT FK_Invitations_SupplierProfileId
    FOREIGN KEY ([SupplierProfileId]) REFERENCES dbo.[SupplierProfiles] ([Id]);
ALTER TABLE dbo.[EvaluationCriteria] ADD CONSTRAINT FK_EvaluationCriteria_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[CommitteeMembers] ADD CONSTRAINT FK_CommitteeMembers_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[CommitteeMembers] ADD CONSTRAINT FK_CommitteeMembers_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[Bids] ADD CONSTRAINT FK_Bids_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[Bids] ADD CONSTRAINT FK_Bids_SupplierProfileId
    FOREIGN KEY ([SupplierProfileId]) REFERENCES dbo.[SupplierProfiles] ([Id]);
ALTER TABLE dbo.[BidItems] ADD CONSTRAINT FK_BidItems_BidId
    FOREIGN KEY ([BidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[BidItems] ADD CONSTRAINT FK_BidItems_RequisitionItemId
    FOREIGN KEY ([RequisitionItemId]) REFERENCES dbo.[PurchaseRequisitionItems] ([Id]);
ALTER TABLE dbo.[BidDocuments] ADD CONSTRAINT FK_BidDocuments_BidId
    FOREIGN KEY ([BidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[ComplianceChecks] ADD CONSTRAINT FK_ComplianceChecks_BidId
    FOREIGN KEY ([BidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[ComplianceChecks] ADD CONSTRAINT FK_ComplianceChecks_CheckedByUserId
    FOREIGN KEY ([CheckedByUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[TechnicalEvaluations] ADD CONSTRAINT FK_TechnicalEvaluations_BidId
    FOREIGN KEY ([BidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[TechnicalEvaluations] ADD CONSTRAINT FK_TechnicalEvaluations_CriterionId
    FOREIGN KEY ([CriterionId]) REFERENCES dbo.[EvaluationCriteria] ([Id]);
ALTER TABLE dbo.[TechnicalEvaluations] ADD CONSTRAINT FK_TechnicalEvaluations_EvaluatorUserId
    FOREIGN KEY ([EvaluatorUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[FinancialEvaluations] ADD CONSTRAINT FK_FinancialEvaluations_BidId
    FOREIGN KEY ([BidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[FinancialEvaluations] ADD CONSTRAINT FK_FinancialEvaluations_EvaluatorUserId
    FOREIGN KEY ([EvaluatorUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[AiBidAnalyses] ADD CONSTRAINT FK_AiBidAnalyses_BidId
    FOREIGN KEY ([BidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[AiBidAnalyses] ADD CONSTRAINT FK_AiBidAnalyses_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[AiBidAnalyses] ADD CONSTRAINT FK_AiBidAnalyses_RequestedByUserId
    FOREIGN KEY ([RequestedByUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[EvaluationReports] ADD CONSTRAINT FK_EvaluationReports_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[EvaluationReports] ADD CONSTRAINT FK_EvaluationReports_RecommendedBidId
    FOREIGN KEY ([RecommendedBidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[EvaluationReports] ADD CONSTRAINT FK_EvaluationReports_PreparedByUserId
    FOREIGN KEY ([PreparedByUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[TenderApprovals] ADD CONSTRAINT FK_TenderApprovals_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[TenderApprovals] ADD CONSTRAINT FK_TenderApprovals_ReportId
    FOREIGN KEY ([ReportId]) REFERENCES dbo.[EvaluationReports] ([Id]);
ALTER TABLE dbo.[TenderApprovals] ADD CONSTRAINT FK_TenderApprovals_DecidedByUserId
    FOREIGN KEY ([DecidedByUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[TenderApprovals] ADD CONSTRAINT FK_TenderApprovals_SelectedBidId
    FOREIGN KEY ([SelectedBidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[TopCommitteeVotes] ADD CONSTRAINT FK_TopCommitteeVotes_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[TopCommitteeVotes] ADD CONSTRAINT FK_TopCommitteeVotes_MemberUserId
    FOREIGN KEY ([MemberUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[TopCommitteeVotes] ADD CONSTRAINT FK_TopCommitteeVotes_SelectedBidId
    FOREIGN KEY ([SelectedBidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[Clarifications] ADD CONSTRAINT FK_Clarifications_SenderUserId
    FOREIGN KEY ([SenderUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[Clarifications] ADD CONSTRAINT FK_Clarifications_ReceiverUserId
    FOREIGN KEY ([ReceiverUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[Clarifications] ADD CONSTRAINT FK_Clarifications_RequisitionId
    FOREIGN KEY ([RequisitionId]) REFERENCES dbo.[PurchaseRequisitions] ([Id]);
ALTER TABLE dbo.[Clarifications] ADD CONSTRAINT FK_Clarifications_TenderId
    FOREIGN KEY ([TenderId]) REFERENCES dbo.[Tenders] ([Id]);
ALTER TABLE dbo.[Clarifications] ADD CONSTRAINT FK_Clarifications_BidId
    FOREIGN KEY ([BidId]) REFERENCES dbo.[Bids] ([Id]);
ALTER TABLE dbo.[ClarificationMessages] ADD CONSTRAINT FK_ClarificationMessages_ClarificationId
    FOREIGN KEY ([ClarificationId]) REFERENCES dbo.[Clarifications] ([Id]);
ALTER TABLE dbo.[ClarificationMessages] ADD CONSTRAINT FK_ClarificationMessages_SenderUserId
    FOREIGN KEY ([SenderUserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[Notifications] ADD CONSTRAINT FK_Notifications_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[AuditLogs] ADD CONSTRAINT FK_AuditLogs_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
ALTER TABLE dbo.[DocumentAccessLogs] ADD CONSTRAINT FK_DocumentAccessLogs_UserId
    FOREIGN KEY ([UserId]) REFERENCES dbo.[AspNetUsers] ([Id]);
GO

/* ---------------------------------------------------------------
   INDEXES on foreign keys (faster joins and filters)
   --------------------------------------------------------------- */
CREATE INDEX IX_AspNetUsers_DepartmentId ON dbo.[AspNetUsers] ([DepartmentId]);
CREATE INDEX IX_SupplierProfiles_ReviewedByUserId ON dbo.[SupplierProfiles] ([ReviewedByUserId]);
CREATE INDEX IX_SupplierDocuments_SupplierProfileId ON dbo.[SupplierDocuments] ([SupplierProfileId]);
CREATE INDEX IX_BiometricCredentials_UserId ON dbo.[BiometricCredentials] ([UserId]);
CREATE INDEX IX_FaceEnrollments_UserId ON dbo.[FaceEnrollments] ([UserId]);
CREATE INDEX IX_BiometricAuthLogs_UserId ON dbo.[BiometricAuthLogs] ([UserId]);
CREATE INDEX IX_PurchaseRequisitions_DepartmentId ON dbo.[PurchaseRequisitions] ([DepartmentId]);
CREATE INDEX IX_PurchaseRequisitions_RequestedByUserId ON dbo.[PurchaseRequisitions] ([RequestedByUserId]);
CREATE INDEX IX_PurchaseRequisitions_ReviewedByUserId ON dbo.[PurchaseRequisitions] ([ReviewedByUserId]);
CREATE INDEX IX_PurchaseRequisitionItems_RequisitionId ON dbo.[PurchaseRequisitionItems] ([RequisitionId]);
CREATE INDEX IX_Tenders_RequisitionId ON dbo.[Tenders] ([RequisitionId]);
CREATE INDEX IX_Tenders_CreatedByUserId ON dbo.[Tenders] ([CreatedByUserId]);
CREATE INDEX IX_Tenders_AwardedSupplierId ON dbo.[Tenders] ([AwardedSupplierId]);
CREATE INDEX IX_Invitations_TenderId ON dbo.[Invitations] ([TenderId]);
CREATE INDEX IX_Invitations_SupplierProfileId ON dbo.[Invitations] ([SupplierProfileId]);
CREATE INDEX IX_EvaluationCriteria_TenderId ON dbo.[EvaluationCriteria] ([TenderId]);
CREATE INDEX IX_CommitteeMembers_TenderId ON dbo.[CommitteeMembers] ([TenderId]);
CREATE INDEX IX_CommitteeMembers_UserId ON dbo.[CommitteeMembers] ([UserId]);
CREATE INDEX IX_Bids_TenderId ON dbo.[Bids] ([TenderId]);
CREATE INDEX IX_Bids_SupplierProfileId ON dbo.[Bids] ([SupplierProfileId]);
CREATE INDEX IX_BidItems_BidId ON dbo.[BidItems] ([BidId]);
CREATE INDEX IX_BidItems_RequisitionItemId ON dbo.[BidItems] ([RequisitionItemId]);
CREATE INDEX IX_BidDocuments_BidId ON dbo.[BidDocuments] ([BidId]);
CREATE INDEX IX_ComplianceChecks_BidId ON dbo.[ComplianceChecks] ([BidId]);
CREATE INDEX IX_ComplianceChecks_CheckedByUserId ON dbo.[ComplianceChecks] ([CheckedByUserId]);
CREATE INDEX IX_TechnicalEvaluations_BidId ON dbo.[TechnicalEvaluations] ([BidId]);
CREATE INDEX IX_TechnicalEvaluations_CriterionId ON dbo.[TechnicalEvaluations] ([CriterionId]);
CREATE INDEX IX_TechnicalEvaluations_EvaluatorUserId ON dbo.[TechnicalEvaluations] ([EvaluatorUserId]);
CREATE INDEX IX_FinancialEvaluations_BidId ON dbo.[FinancialEvaluations] ([BidId]);
CREATE INDEX IX_FinancialEvaluations_EvaluatorUserId ON dbo.[FinancialEvaluations] ([EvaluatorUserId]);
CREATE INDEX IX_AiBidAnalyses_BidId ON dbo.[AiBidAnalyses] ([BidId]);
CREATE INDEX IX_AiBidAnalyses_TenderId ON dbo.[AiBidAnalyses] ([TenderId]);
CREATE INDEX IX_AiBidAnalyses_RequestedByUserId ON dbo.[AiBidAnalyses] ([RequestedByUserId]);
CREATE INDEX IX_EvaluationReports_TenderId ON dbo.[EvaluationReports] ([TenderId]);
CREATE INDEX IX_EvaluationReports_RecommendedBidId ON dbo.[EvaluationReports] ([RecommendedBidId]);
CREATE INDEX IX_EvaluationReports_PreparedByUserId ON dbo.[EvaluationReports] ([PreparedByUserId]);
CREATE INDEX IX_TenderApprovals_TenderId ON dbo.[TenderApprovals] ([TenderId]);
CREATE INDEX IX_TenderApprovals_ReportId ON dbo.[TenderApprovals] ([ReportId]);
CREATE INDEX IX_TenderApprovals_DecidedByUserId ON dbo.[TenderApprovals] ([DecidedByUserId]);
CREATE INDEX IX_TenderApprovals_SelectedBidId ON dbo.[TenderApprovals] ([SelectedBidId]);
CREATE INDEX IX_TopCommitteeVotes_TenderId ON dbo.[TopCommitteeVotes] ([TenderId]);
CREATE INDEX IX_TopCommitteeVotes_MemberUserId ON dbo.[TopCommitteeVotes] ([MemberUserId]);
CREATE INDEX IX_TopCommitteeVotes_SelectedBidId ON dbo.[TopCommitteeVotes] ([SelectedBidId]);
CREATE INDEX IX_Clarifications_SenderUserId ON dbo.[Clarifications] ([SenderUserId]);
CREATE INDEX IX_Clarifications_ReceiverUserId ON dbo.[Clarifications] ([ReceiverUserId]);
CREATE INDEX IX_Clarifications_RequisitionId ON dbo.[Clarifications] ([RequisitionId]);
CREATE INDEX IX_Clarifications_TenderId ON dbo.[Clarifications] ([TenderId]);
CREATE INDEX IX_Clarifications_BidId ON dbo.[Clarifications] ([BidId]);
CREATE INDEX IX_ClarificationMessages_ClarificationId ON dbo.[ClarificationMessages] ([ClarificationId]);
CREATE INDEX IX_ClarificationMessages_SenderUserId ON dbo.[ClarificationMessages] ([SenderUserId]);
CREATE INDEX IX_Notifications_UserId ON dbo.[Notifications] ([UserId]);
CREATE INDEX IX_AuditLogs_UserId ON dbo.[AuditLogs] ([UserId]);
CREATE INDEX IX_DocumentAccessLogs_UserId ON dbo.[DocumentAccessLogs] ([UserId]);
GO

/* ---------------------------------------------------------------
   SEED DATA - roles and departments
   --------------------------------------------------------------- */
INSERT INTO dbo.[AspNetRoles] ([Id], [Name], [NormalizedName]) VALUES
    (CONVERT(nvarchar(450), NEWID()), N'Admin', N'ADMIN'),
    (CONVERT(nvarchar(450), NEWID()), N'Requester', N'REQUESTER'),
    (CONVERT(nvarchar(450), NEWID()), N'SourcingOfficer', N'SOURCINGOFFICER'),
    (CONVERT(nvarchar(450), NEWID()), N'EvaluationCommittee', N'EVALUATIONCOMMITTEE'),
    (CONVERT(nvarchar(450), NEWID()), N'SourcingManager', N'SOURCINGMANAGER'),
    (CONVERT(nvarchar(450), NEWID()), N'TopCommittee', N'TOPCOMMITTEE'),
    (CONVERT(nvarchar(450), NEWID()), N'Supplier', N'SUPPLIER');

INSERT INTO dbo.[Departments] ([Name], [Code]) VALUES
    (N'Production', N'PRD'),
    (N'Packaging', N'PKG'),
    (N'Research & Development', N'RND'),
    (N'Marketing', N'MKT'),
    (N'Quality Control', N'QC'),
    (N'Finance', N'FIN'),
    (N'IT', N'IT'),
    (N'Sourcing', N'SRC');
GO

/* ---------------------------------------------------------------
   EXAMPLE QUERIES (good for the live demo)
   --------------------------------------------------------------- */
-- 1) Bids of one tender, cheapest first
SELECT s.CompanyName, b.TotalPrice, b.Currency, b.DeliveryDays, b.Status
FROM dbo.Bids b JOIN dbo.SupplierProfiles s ON s.Id = b.SupplierProfileId
WHERE b.TenderId = 1 AND b.Status <> N'Draft'
ORDER BY b.TotalPrice;

-- 2) Tenders with zero bids
SELECT t.TenderNumber, t.Title
FROM dbo.Tenders t
WHERE NOT EXISTS (SELECT 1 FROM dbo.Bids b WHERE b.TenderId = t.Id AND b.Status <> N'Draft');

-- 3) Users who finished BOTH biometric registrations
SELECT u.Email
FROM dbo.AspNetUsers u
WHERE EXISTS (SELECT 1 FROM dbo.BiometricCredentials c WHERE c.UserId = u.Id AND c.IsRevoked = 0)
  AND EXISTS (SELECT 1 FROM dbo.FaceEnrollments f WHERE f.UserId = u.Id AND f.IsActive = 1);

-- 4) Read the AI answer stored as JSON
SELECT BidId, JSON_VALUE(ResponseJson, '$.recommendation') AS Recommendation
FROM dbo.AiBidAnalyses WHERE ISJSON(ResponseJson) = 1;

-- 5) Supplier participation per tender
SELECT t.TenderNumber, COUNT(DISTINCT i.SupplierProfileId) AS Invited,
       COUNT(DISTINCT b.SupplierProfileId) AS Bid
FROM dbo.Tenders t
LEFT JOIN dbo.Invitations i ON i.TenderId = t.Id
LEFT JOIN dbo.Bids b ON b.TenderId = t.Id AND b.Status <> N'Draft'
GROUP BY t.TenderNumber;