/*
  Story data for the Flow page.
  Each cycle = actors (nodes) + steps (arrows).
  A step says: who sends what to whom, what is inside the request, what happens, and what changes in the database.
*/
window.FLOW = (function () {
  const actors = {
    admin:     { name: 'Admin',                      color: '#1d4ed8' },
    employee:  { name: 'Employee',                   color: '#be185d' },
    requester: { name: 'Requesting Dept (Sara)',     color: '#c2410c' },
    officer:   { name: 'Sourcing Officer (Omar)',    color: '#15803d' },
    supplier:  { name: 'Supplier (BottlePro)',       color: '#7e22ce' },
    committee: { name: 'Evaluation Committee',       color: '#0f766e' },
    manager:   { name: 'Sourcing Manager (Nour)',    color: '#b45309' },
    top:       { name: 'Top Committee',              color: '#be123c' },
    user:      { name: 'User',                       color: '#be185d' },
    web:       { name: 'Web App (ASP.NET MVC)',      color: '#0f172a' },
    db:        { name: 'SQL Server',                 color: '#166534' },
    device:    { name: 'Device (fingerprint / camera)', color: '#db2777' },
    ai:        { name: 'Ollama AI',                  color: '#6d28d9' },
    mail:      { name: 'Notifications / Email',      color: '#a16207' },
  };

  const s = (from, to, label, payload, what, db, status) => ({ from, to, label, payload, what, db, status });

  const cycles = [
    // ───────────────────────── 0. BIG PICTURE ─────────────────────────
    {
      id: 'big', title: 'Big picture',
      story: 'Lumière Cosmetics needs 50,000 airless pump bottles for a new serum. Follow the request from the first idea to the awarded supplier.',
      actors: ['requester', 'officer', 'supplier', 'committee', 'manager', 'top'],
      steps: [
        s('requester', 'officer', 'Purchase Request', { title: '50,000 airless pump bottles 30ml', priority: 'High', targetDate: '2026-12-15' },
          'Sara from Packaging describes the need: what, how many, why and by when.', 'PurchaseRequisitions + Items', 'Request: Draft → Submitted'),
        s('officer', 'requester', 'Review result', { decision: 'Approved', note: 'Specs are clear' },
          'Omar checks the request. He can approve, return it for changes, or reject it.', 'PurchaseRequisitions.Status', 'Request: Submitted → Approved'),
        s('officer', 'supplier', 'Tender invitation', { tender: 'TND-2026-014', deadline: '2026-11-30', criteria: 'Technical 60% / Financial 40%' },
          'Omar turns the request into a tender, locks the scoring criteria, and invites approved suppliers.', 'Tenders, EvaluationCriteria, Invitations', 'Tender: Draft → Published · Invitation: Pending'),
        s('supplier', 'officer', 'Accept + submit bid', { totalPrice: 91500, currency: 'USD', deliveryDays: 35, files: ['datasheet.pdf', 'sample.jpg'] },
          'BottlePro accepts, then submits a price, delivery time and documents before the deadline. Other suppliers cannot see it.', 'Invitations, Bids, BidItems, BidDocuments', 'Invitation: Accepted · Bid: Submitted'),
        s('officer', 'committee', 'Compliant bids', { compliant: 3, nonCompliant: 1, pendingClarification: 1 },
          'Omar checks the mandatory documents. The tender closes at the deadline and the evaluation committee receives the compliant bids.', 'ComplianceChecks', 'Tender: Closed → Under Evaluation'),
        s('committee', 'manager', 'Unified report', { recommendedBid: 'BottlePro', technicalScore: 86, financialScore: 91, justification: 'Best value, 35-day delivery' },
          'Four members score technical and financial parts. They send ONE combined report with a recommendation (Ollama gave an initial opinion, humans decide).', 'EvaluationReports', 'Report: Recommendation Pending'),
        s('manager', 'top', 'Forward (high value)', { decision: 'Approved', forwardToTopCommittee: true, comment: 'Value is above the policy limit' },
          'Nour approves the recommendation. Because the tender is high value, it goes to the Top Committee.', 'TenderApprovals (Manager)', 'Tender: Awaiting Manager Review → Awaiting Committee Decision'),
        s('top', 'supplier', 'Award announcement', { winner: 'BottlePro', votes: '3 approve / 1 reject', closed: true },
          'The Top Committee votes, the winner is recorded, the tender is closed and everyone is notified.', 'TopCommitteeVotes, TenderApprovals, Tenders', 'Tender: Awarded'),
      ],
    },

    // ───────────────────────── 1. EMPLOYEE ONBOARDING ─────────────────────────
    {
      id: 'onboarding', title: 'Employee onboarding',
      story: 'Layla joins the Quality Control team. The Admin creates her account. Before she can enter any dashboard she must register BOTH her fingerprint and her face.',
      actors: ['admin', 'employee', 'web', 'device', 'db'],
      steps: [
        s('admin', 'web', 'POST /Admin/Users/Create', { fullName: 'Layla Hassan', email: 'layla@lumiere.com', department: 'Quality Control', role: 'EvaluationCommittee' },
          'Admin fills the form. The system generates a temporary password.', null, null),
        s('web', 'db', 'INSERT user + role', { AspNetUsers: { MustChangePassword: true, OnboardingStatus: 'NotStarted' }, AspNetUserRoles: 'EvaluationCommittee' },
          'The account is saved but locked into onboarding.', 'AspNetUsers, AspNetUserRoles', 'Onboarding: NotStarted'),
        s('web', 'employee', 'Welcome email', { to: 'layla@lumiere.com', link: '/Account/Login', tempPassword: '********' },
          'Layla receives her login link and temporary password.', null, null),
        s('employee', 'web', 'POST /Onboarding/ChangePassword', { currentPassword: '********', newPassword: '********', confirm: '********' },
          'First login forces a new password. Every other URL redirects to the onboarding wizard until it is finished.', 'AspNetUsers.PasswordHash', 'Onboarding: InProgress'),
        s('employee', 'web', 'POST /Onboarding/Profile', { employeeNumber: 'QC-0142', hireDate: '2026-11-01', workPhone: '+20 100 000 0000', preferredLanguage: 'en', photo: 'file' },
          'Layla completes her profile data.', 'EmployeeProfiles', null),
        s('web', 'device', 'WebAuthn challenge', { challenge: 'k3F...random', rp: 'localhost', userVerification: 'required' },
          'FINGERPRINT. The browser asks the laptop or phone to create a passkey. The device asks for her fingerprint.', null, null),
        s('device', 'web', 'POST /Biometrics/Fingerprint/Register', { credentialId: 'AQID...', publicKey: 'MFkw...', deviceName: 'Layla iPhone', authenticatorType: 'platform' },
          'The fingerprint NEVER leaves the device. Only a public key and a credential ID are sent. The server verifies it and keeps it.', 'BiometricCredentials', 'Fingerprint: registered'),
        s('device', 'web', 'POST /Biometrics/Face/Enroll', { embedding: '[128 numbers]', livenessScore: 0.97, challengeActions: ['blink', 'turn-left'] },
          'FACE. The camera opens and asks her to blink and turn her head (liveness), so a static photo is rejected. Only a numeric face template is sent, not the picture.', null, null),
        s('web', 'db', 'INSERT FaceEnrollments', { UserId: 'layla', FaceTemplate: 'encrypted bytes', ModelVersion: 'face-v1', LivenessScore: 0.97 },
          'The template is encrypted before it is saved.', 'FaceEnrollments', 'Face: registered'),
        s('web', 'employee', 'Onboarding complete', { fingerprint: true, face: true, onboardingStatus: 'Completed', redirect: '/Evaluation/Dashboard' },
          'The system checks that BOTH biometrics exist. Only then is the dashboard unlocked.', 'AspNetUsers.OnboardingStatus', 'Onboarding: Completed'),
      ],
    },

    // ───────────────────────── 2. LOGIN ─────────────────────────
    {
      id: 'login', title: 'Login (3 ways)',
      story: 'Layla can log in with email and password, with her fingerprint, or with her face. Every attempt is recorded.',
      actors: ['user', 'web', 'device', 'db'],
      steps: [
        s('user', 'web', 'GET /Account/Login', {}, 'The login page shows three choices: password, fingerprint, face.', null, null),
        s('user', 'web', 'A) POST /Account/Login', { email: 'layla@lumiere.com', password: '********' },
          'PATH A, PASSWORD. Identity checks the password hash. Wrong passwords count toward lockout.', 'AspNetUsers.AccessFailedCount', null),
        s('user', 'web', 'B) POST /Account/Passkey/Options', { email: 'layla@lumiere.com' },
          'PATH B, FINGERPRINT. The server looks up her registered passkeys and creates a one-time challenge.', 'BiometricCredentials (read)', null),
        s('web', 'device', 'B) WebAuthn challenge', { challenge: 'n8Q...random', allowCredentials: ['AQID...'], userVerification: 'required' },
          'The browser asks the device to sign the challenge. The device asks for her fingerprint.', null, null),
        s('device', 'web', 'B) POST /Account/Passkey/Verify', { credentialId: 'AQID...', signature: 'MEUC...', authenticatorData: '...', clientDataJSON: '...' },
          'The server checks the signature with the stored public key, checks the origin and the signCount, then signs her in.', 'BiometricCredentials.SignCount', null),
        s('device', 'web', 'C) POST /Account/Face/Verify', { email: 'layla@lumiere.com', embedding: '[128 numbers]', livenessProof: 'blink+turn', nonce: 'one-time' },
          'PATH C, FACE. The camera does a liveness check. A static image or video replay is rejected. The server compares the new template with the stored one.', 'FaceEnrollments (read)', null),
        s('web', 'db', 'Log the attempt', { Method: 'Face', Success: true, IpAddress: '10.0.0.8' },
          'Success or failure, the attempt is saved. Too many failures lock the account.', 'BiometricAuthLogs', null),
        s('web', 'user', 'Sign in + redirect', { cookie: 'authenticated', role: 'EvaluationCommittee', redirect: '/Evaluation/Dashboard' },
          'A secure cookie is issued and the user lands on the dashboard of his or her role.', 'AspNetUsers.LastLoginAt', null),
      ],
    },

    // ───────────────────────── 3. SUPPLIER REGISTRATION ─────────────────────────
    {
      id: 'supplier', title: 'Supplier registration',
      story: 'BottlePro, a packaging manufacturer, wants to join the system. It registers itself, then waits for the Admin to verify its documents.',
      actors: ['supplier', 'web', 'db', 'admin', 'mail'],
      steps: [
        s('supplier', 'web', 'POST /Account/RegisterSupplier', { companyName: 'BottlePro Co.', taxNumber: '123-456-789', commercialReg: 'CR-889', contactPerson: 'Ali', email: 'sales@bottlepro.com', password: '********', documents: ['license.pdf', 'tax.pdf'] },
          'Self-registration form with Data Annotation validation. Files are checked (type, size) before saving.', null, null),
        s('web', 'db', 'INSERT supplier', { AspNetUsers: { UserType: 'Supplier', IsActive: false }, SupplierProfiles: { Status: 'Pending' }, SupplierDocuments: 2 },
          'The account exists but cannot log in until the Admin approves it.', 'AspNetUsers, SupplierProfiles, SupplierDocuments', 'Supplier: Pending'),
        s('web', 'admin', 'Notify Admin', { type: 'NewSupplier', title: 'BottlePro Co. is waiting for review' },
          'The Admin sees a new item in the notification bell and on the dashboard.', 'Notifications', null),
        s('admin', 'web', 'GET /Admin/Suppliers/Review/12', {}, 'The Admin opens the company data and the uploaded legal documents.', 'SupplierProfiles (read)', null),
        s('admin', 'web', 'POST /Admin/Suppliers/Decide', { supplierId: 12, status: 'Approved', reason: 'Documents valid until 2027' },
          'Approved, Rejected or Suspended. A reason is always required for Rejected and Suspended.', 'SupplierProfiles.Status, AspNetUsers.IsActive, AuditLogs', 'Supplier: Pending → Approved'),
        s('web', 'mail', 'Email the supplier', { to: 'sales@bottlepro.com', subject: 'Your account is approved' },
          'The supplier can now log in. On the first login it must register fingerprint AND face (same onboarding as employees) before it can receive tender invitations.', 'Notifications', null),
      ],
    },

    // ───────────────────────── 4. PURCHASE REQUEST ─────────────────────────
    {
      id: 'request', title: 'Purchase request',
      story: 'Sara from Packaging creates a request. Omar returns it once because the specs are unclear, then approves it.',
      actors: ['requester', 'web', 'db', 'officer', 'mail'],
      steps: [
        s('requester', 'web', 'POST /Requester/Requests/Create', { title: 'Airless pump bottles 30ml', reason: 'New serum launch', priority: 'High', targetDate: '2026-12-15', items: [{ name: 'Bottle 30ml', qty: 50000, unit: 'pcs', specs: 'PP, airless, matte white' }] },
          'The form is validated on the client and again on the server.', 'PurchaseRequisitions, PurchaseRequisitionItems', 'Request: Draft'),
        s('requester', 'web', 'POST /Requester/Requests/Submit/45', { id: 45 }, 'Sara sends it for review. A Draft can still be edited, a Submitted request cannot.', 'PurchaseRequisitions.Status', 'Request: Draft → Submitted'),
        s('web', 'officer', 'Notify officer', { type: 'RequestSubmitted', link: '/Sourcing/Requests/45' }, 'Omar gets an alert and sees the request in his list.', 'Notifications', null),
        s('officer', 'web', 'POST /Sourcing/Requests/Review', { id: 45, decision: 'Returned', note: 'Please add the cap material' },
          'Returned means the requester must edit and resubmit. The note is saved in the history.', 'PurchaseRequisitions.ReviewNote, AuditLogs', 'Request: Under Review → Returned'),
        s('requester', 'web', 'POST /Requester/Requests/Submit/45', { id: 45, updatedSpecs: 'PP, airless, matte white, aluminium cap' }, 'Sara fixes it and resubmits.', 'PurchaseRequisitionItems', 'Request: Returned → Submitted'),
        s('officer', 'web', 'POST /Sourcing/Requests/Review', { id: 45, decision: 'Approved' },
          'Approved requests become available for creating a tender.', 'PurchaseRequisitions.Status, AuditLogs', 'Request: Submitted → Approved'),
        s('web', 'requester', 'Notify requester', { type: 'RequestApproved' }, 'Sara is told it was approved. She can follow it until award.', 'Notifications', null),
      ],
    },

    // ───────────────────────── 5. TENDER ─────────────────────────
    {
      id: 'tender', title: 'Tender & invitations',
      story: 'Omar converts the approved request into a tender, sets scoring weights, and invites suppliers.',
      actors: ['officer', 'web', 'db', 'supplier', 'mail'],
      steps: [
        s('officer', 'web', 'POST /Sourcing/Tenders/Create', { requisitionId: 45, title: 'Airless bottles 30ml', terms: 'Delivery to Cairo plant', submissionDeadline: '2026-11-30 17:00', requiresTopCommittee: true },
          'The tender is created from the approved request.', 'Tenders', 'Request: Approved → Converted to Tender · Tender: Draft'),
        s('officer', 'web', 'POST /Sourcing/Tenders/Criteria', { criteria: [{ name: 'Material quality', type: 'Technical', weight: 35 }, { name: 'Compliance & samples', type: 'Technical', weight: 25 }, { name: 'Total cost', type: 'Financial', weight: 40 }] },
          'Weights must add up to 100%. They are locked at publishing and cannot change after bids arrive.', 'EvaluationCriteria', null),
        s('officer', 'web', 'POST /Sourcing/Tenders/Suppliers', { tenderId: 14, supplierIds: [12, 17, 23, 31] }, 'Only suppliers with status Approved can be selected.', 'TenderSuppliers', null),
        s('officer', 'web', 'POST /Sourcing/Tenders/Publish/14', { tenderId: 14 }, 'Criteria are locked and invitations are created.', 'Tenders.Status, Invitations, EvaluationCriteria.IsLocked', 'Tender: Draft → Published · Invitations: Pending'),
        s('web', 'mail', 'Send invitations', { recipients: 4, subject: 'You are invited: TND-2026-014' }, 'Each supplier gets an in-app notification and an email.', 'Notifications', null),
        s('supplier', 'web', 'POST /Supplier/Invitations/Respond', { invitationId: 88, response: 'Accepted' }, 'Accept or decline. After the expiry date an unanswered invitation becomes Expired.', 'Invitations.Status', 'Invitation: Pending → Accepted'),
      ],
    },

    // ───────────────────────── 6. BID ─────────────────────────
    {
      id: 'bid', title: 'Supplier bid',
      story: 'BottlePro prepares its offer. The system makes sure no supplier can ever see another supplier\'s bid.',
      actors: ['supplier', 'web', 'db', 'officer', 'mail'],
      steps: [
        s('supplier', 'web', 'GET /Supplier/Tenders/14', {}, 'The supplier sees only tenders it was invited to. A different supplier would get 404.', 'Invitations (read)', null),
        s('supplier', 'web', 'POST /Supplier/Offers/SaveDraft', { tenderId: 14, items: [{ requisitionItemId: 101, unitPrice: 1.75, tax: 0.12, shipping: 0.05 }], deliveryDays: 35, validityDays: 60, paymentTerms: '30% advance' },
          'Drafts can be saved and edited many times.', 'Bids, BidItems', 'Bid: Draft'),
        s('supplier', 'web', 'POST /Supplier/Offers/Upload', { bidId: 301, files: ['datasheet.pdf', 'sample.jpg', 'iso.pdf'] },
          'Secure upload: allowed types only, size limit, random file name, stored outside the public web folder.', 'BidDocuments', null),
        s('supplier', 'web', 'POST /Supplier/Offers/Submit', { bidId: 301 },
          'The server checks: invitation accepted, deadline not passed, required fields and documents present.', 'Bids.Status, Bids.SubmittedAt, AuditLogs', 'Bid: Draft → Submitted'),
        s('web', 'officer', 'Notify officer', { type: 'BidReceived', tender: 'TND-2026-014' }, 'Omar sees the counter go up, but bids stay sealed until the deadline.', 'Notifications', null),
        s('web', 'db', 'Deadline job', { now: '2026-11-30 17:00:01' }, 'A background job closes the tender automatically at the deadline. Late submissions are rejected.', 'Tenders.Status', 'Tender: Open → Closed'),
      ],
    },

    // ───────────────────────── 7. COMPLIANCE + AI ─────────────────────────
    {
      id: 'compliance', title: 'Compliance & AI analysis',
      story: 'After the deadline Omar checks mandatory documents, then asks Ollama for a first analysis of each bid.',
      actors: ['officer', 'web', 'db', 'ai', 'committee'],
      steps: [
        s('officer', 'web', 'GET /Sourcing/Offers/Compare/14', {}, 'Side-by-side table: price, delivery, specs, documents.', 'Bids, BidItems (read)', null),
        s('officer', 'web', 'POST /Sourcing/Compliance/Check', { bidId: 301, result: 'Compliant' },
          'Compliant, Non-Compliant (reason required) or Pending Clarification.', 'ComplianceChecks, Bids.Status', 'Bid: Submitted → Compliant'),
        s('officer', 'web', 'POST /Sourcing/Ai/Analyze', { tenderId: 14, bidIds: [301, 302, 303] }, 'The officer presses "Analyze with AI".', null, null),
        s('web', 'ai', 'POST :11434/api/generate', { model: 'llama3', format: 'json', prompt: 'Extract price, specs, delivery, missing files. Compare bids. Return JSON.', bids: '[bid data as JSON]' },
          'The web app sends the bid data as JSON to the local Ollama server. Nothing leaves the company.', null, null),
        s('ai', 'web', 'JSON answer', { price: 91500, deliveryDays: 35, missingFiles: ['ISO certificate'], strengths: ['Lowest price'], weaknesses: ['No sample'], recommendation: 'BottlePro' },
          'The model replies with structured JSON.', null, null),
        s('web', 'db', 'INSERT AiBidAnalyses', { RequestJson: '...', ResponseJson: '...', Model: 'llama3' },
          'Request and answer are stored as JSON text in SQL Server.', 'AiBidAnalyses', null),
        s('web', 'committee', 'Show AI panel', { banner: 'AI is an assistant. The committee decides.' }, 'The committee sees the analysis with a clear label. It never replaces the human decision.', null, 'Tender: Closed → Under Evaluation'),
      ],
    },

    // ───────────────────────── 8. EVALUATION ─────────────────────────
    {
      id: 'evaluation', title: 'Evaluation & unified report',
      story: 'The four committee members score the bids using the locked criteria and produce ONE combined report.',
      actors: ['committee', 'web', 'db', 'officer', 'manager'],
      steps: [
        s('committee', 'web', 'POST /Evaluation/Technical/Score', { bidId: 301, scores: [{ criterionId: 1, score: 31, notes: 'Good PP quality' }, { criterionId: 2, score: 22 }] },
          'The technical member scores each criterion and writes observations.', 'TechnicalEvaluations', null),
        s('committee', 'web', 'POST /Evaluation/Financial/Save', { bidId: 301, totalCost: 91500, taxesAndFees: 4200, logisticsCost: 2800, score: 36 },
          'The financial member reviews price, taxes, transport, payment terms and validity.', 'FinancialEvaluations', null),
        s('committee', 'officer', 'Clarification request', { subject: 'Missing ISO certificate', bidId: 302 },
          'If something is unclear, the committee asks the officer, who asks the supplier.', 'Clarifications', null),
        s('committee', 'web', 'POST /Evaluation/Report/Create', { tenderId: 14, recommendedBidId: 301, technicalSummary: '...', financialSummary: '...', justification: 'Best value for money' },
          'After all four members approve, the single unified report is created with scores, non-compliance, comparison and recommendation.', 'EvaluationReports', 'Report: Recommendation Pending'),
        s('web', 'manager', 'Notify manager', { type: 'ReportReady', tender: 'TND-2026-014' }, 'The Sourcing Manager is notified.', 'Notifications', 'Tender: Under Evaluation → Awaiting Manager Review'),
      ],
    },

    // ───────────────────────── 9. DECISION ─────────────────────────
    {
      id: 'decision', title: 'Manager & Top Committee',
      story: 'Nour reviews the report. Because the value is high, the Top Committee takes the final decision.',
      actors: ['manager', 'web', 'db', 'top', 'supplier'],
      steps: [
        s('manager', 'web', 'GET /Manager/Reports/14', {}, 'Nour reads the report, scores, AI analysis and clarification history.', 'EvaluationReports (read)', null),
        s('manager', 'web', 'POST /Manager/Decisions/Submit', { reportId: 9, decision: 'Approved', comment: 'Agree with committee', forwardToTopCommittee: true },
          'Approve, reject, or return with comments. Return sends it back to the committee.', 'TenderApprovals (Manager), EvaluationReports.Status', 'Report: Manager Approved · Tender: Awaiting Committee Decision'),
        s('web', 'top', 'Notify Top Committee', { type: 'DecisionRequired', tender: 'TND-2026-014' }, 'All four members get the package: request, bids, report, manager notes.', 'Notifications', null),
        s('top', 'web', 'POST /Committee/Decisions/Vote', { tenderId: 14, vote: 'Approve', selectedBidId: 301, comment: 'Reliable supplier' },
          'Each member votes. Every vote and its justification are saved. Members can also ask for more details.', 'TopCommitteeVotes', null),
        s('web', 'db', 'Count votes + sign', { approve: 3, reject: 1, signatureHash: 'sha256:...' }, 'When the votes are in, the final decision is recorded (optionally digitally signed).', 'TenderApprovals (TopCommittee), Tenders.AwardedSupplierId', 'Report: Committee Approved · Tender: Awarded'),
        s('web', 'supplier', 'Award notification', { winner: 'BottlePro Co.', result: 'Awarded' }, 'The winner and the other suppliers are notified. The tender is closed and archived, with a full audit trail.', 'Notifications, AuditLogs', null),
      ],
    },

    // ───────────────────────── 10. CLARIFICATION ─────────────────────────
    {
      id: 'clarification', title: 'Clarification request',
      story: 'Omar needs a missing certificate from BottlePro. The system only allows communication between roles that are directly involved at this stage.',
      actors: ['officer', 'web', 'db', 'supplier', 'mail'],
      steps: [
        s('officer', 'web', 'GET /Shared/Clarifications/Allowed', { tenderId: 14, myRole: 'SourcingOfficer' },
          'The system calculates who this user may contact, based on role and process stage (for example Officer ↔ Supplier of this tender).', 'Invitations, CommitteeMembers (read)', null),
        s('officer', 'web', 'POST /Shared/Clarifications/Create', { receiverId: 'bottlepro-user', tenderId: 14, bidId: 301, subject: 'Missing ISO certificate', description: 'Please upload the ISO 9001 certificate.' },
          'A request with subject and details, linked to the related tender and bid.', 'Clarifications', 'Clarification: Pending'),
        s('web', 'mail', 'Notify receiver', { type: 'ClarificationReceived' }, 'The supplier gets an in-app notification.', 'Notifications', null),
        s('supplier', 'web', 'POST /Shared/Clarifications/Reply', { clarificationId: 21, body: 'Uploaded, please check.', attachment: 'iso9001.pdf' },
          'Only the two people involved (and authorized roles) can see this thread.', 'ClarificationMessages, Clarifications.Status', 'Clarification: Pending → Answered'),
        s('officer', 'web', 'POST /Shared/Clarifications/Resolve', { clarificationId: 21 },
          'The sender closes the thread. Everything stays in the history of the tender.', 'Clarifications.Status, ResolvedAt, AuditLogs', 'Clarification: Answered → Resolved'),
      ],
    },
  ];

  return { actors, cycles };
})();
