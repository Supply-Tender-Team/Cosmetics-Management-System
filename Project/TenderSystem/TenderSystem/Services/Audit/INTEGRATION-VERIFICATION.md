# Audit integration — 17 November 2026

The existing AuditService and its registration are unchanged. Missing calls were added to existing successful actions; existing calls in controllers and services were retained.

## Added coverage

| Area / controller | Successful operations now recorded |
| --- | --- |
| Admin / Users | Activate and deactivate employee accounts, inside the existing transaction |
| Supplier / Invitations | Accept, decline, and expiration when its status actually changes |
| Supplier / Offers | Save draft, attachment uploaded through either existing upload path, personal CSV export |
| Shared / Profile | Save own profile |
| Shared / Onboarding | Save employee or supplier onboarding profile |
| Shared / Notifications | Mark an unread notification read, once per state change |
| Sourcing / CommitteeBrief | Save technical evaluation, inside the existing transaction |
| Home / Contact | Successfully submitted contact request |

Validation and ownership checks remain before the new calls. Logs identify the actor, entity, saved ID, request IP, and UTC time. Added descriptions exclude private profile fields, contact messages, decline reasons, file contents, passwords, and tokens. Existing Top Committee vote confidentiality is unchanged.

Existing authentication, requests, tenders, compliance, clarification, committee Ready/recommendation, report delivery, Manager/Top Committee decisions, company profile, and Shared Reports audit calls are reused. File-access history remains in the existing DocumentAccessLogs service; no duplicate AuditLogs download tracking was introduced.

## Verification

- Application builds with zero errors; two existing nullable warnings remain in AI files outside this change.
- Actual SQL tests used disposable databases, removed in finally blocks; no testing data was reset in the normal application database.
- 25 audit integration checks cover real controller operations, saved actor/entity IDs, account activation, both onboarding profile paths, draft/attachment saving, export, contact, notification read, duplicate/stale/invalid/foreign requests, and exclusion of private form contents.
- 134 existing flow checks pass across Evaluation saving/Ready/recommendations, Officer delivery, Manager forwarding/approval, Top Committee, and award.
- Two additional controller checks verify that old Evaluation report URLs redirect an Officer to Sourcing Evaluation tracking, retaining the tender ID.
- These are backend/controller and database checks; a full browser test of every team's page was not performed.

## Notification/sidebar investigation

The shared sidebar chooses its menu from the current MVC area. Evaluation is an allowed area for an Officer, so opening an old /Evaluation/Report URL can show the Evaluation menu even though the account remains an Officer. The new report-notification producer already targets /Sourcing/Evaluation?tenderId=... . Old stored URLs can survive in either demo or real notifications.

ReportController now redirects Officers visiting its standalone Evaluation report route into Sourcing Evaluation tracking. The existing embedded EvaluationResultsReport component is unchanged, so Officer report display remains within the Officer page. No shared sidebar or Sourcing page was changed for this correction.

## Team handoff

No new package, migration, configuration, or AuditService change is needed. Do not add another audit call to an operation already logged by its underlying service. Future successful write actions should call the same service after business saving, before committing an existing transaction if there is one.
