# RPT-01: evaluation deadline, document verification and final contract

Wadea created the three tables, their services and the migration. This file says who connects what. Nothing here changes a screen by itself: until a screen writes a row, the tables stay empty and the reports must show **Unknown / no record**, never a guessed value.

## 1. Evaluation deadline (`EvaluationSchedules`)

| Property | Meaning |
|---|---|
| `TenderId` | One row per tender (unique). |
| `EvaluationStartedAt` | First successful share with the committee, UTC. |
| `EvaluationDueAt` | `EvaluationStartedAt` + `SlaDays`, UTC. |
| `SlaDays` | The days used. Default **7 calendar days** from `EVALUATION_SLA_DAYS` in `.env` (a proposed default, not a requirement of the amendment: the team must confirm it). Changing the setting never changes a saved row. |

Rules: sharing again does not reset the row. Rework keeps the same deadline. Completion = `EvaluationReport.SubmittedAt`.

**Who writes it (Deyaa / Nada, `Areas/Sourcing/Controllers/ComplianceController.cs`, action `SendToCommittee`):** one line, after the checks pass and before `_audit.LogAsync("Evaluation shared", ...)`, inside the existing transaction:

```csharp
// constructor: EvaluationScheduleService evaluationSchedule  (namespace TenderSystem.Services.Governance)
await _evaluationSchedule.StartAsync(id, cancellationToken);
```

Calling it twice is harmless: the first deadline is kept.

**Reports (Marwa):** unfinished evaluation is overdue when `now > EvaluationDueAt`. Completed evaluation is late when `SubmittedAt > EvaluationDueAt`. No row = Unknown (old tenders).

## 2. Verification (`DocumentVerifications`)

One row = one decision about exactly **one** supplier document (`SupplierDocumentId`) **or** one guarantee (`BidGuaranteeId`). The database refuses rows with both or none.

| Property | Meaning |
|---|---|
| `Status` | `Pending`, `Verified`, `Rejected` (`Statuses.VerificationStatus`). |
| `Method` | `Manual`, `OcrAssisted`, `QrAssisted` (`Statuses.VerificationMethod`). OCR/QR may help the reviewer, they never write a row alone. |
| `Reason` | Required when `Rejected`. |
| `VerifiedByUserId`, `VerifiedAt` | The real reviewer and the time (UTC). |

The table only grows. A changed decision is a **new** row; the old rows are the history. The current decision is the newest row of that document or guarantee: `VerificationService.Current()` returns exactly those rows. No row = **no verification recorded**; show it like that, never as Verified. Supplier approval does not write rows here.

**Who writes it:** the **Admin** verifies supplier legal documents (Abdallah, Admin supplier review). **Sourcing** verifies guarantees (Deyaa, compliance). The controller must check the role; the service does not.

```csharp
// documents:  (row, error) = await _verification.RecordAsync(documentId, null, Statuses.VerificationStatus.Verified, Statuses.VerificationMethod.Manual, reason, userId);
// guarantees: (row, error) = await _verification.RecordAsync(null, guaranteeId, Statuses.VerificationStatus.Rejected, Statuses.VerificationMethod.QrAssisted, "Code does not match", userId);
```

Write an audit row in the controller, like the other actions.

## 3. Final contract (`Contracts`)

| Property | Meaning |
|---|---|
| `ContractNumber` | Unique, `CTR-` + tender number (`-2`, `-3` if an earlier one was cancelled). |
| `TenderId`, `AwardedBidId` | The tender and the winning offer. |
| `Status` | `Draft`, `Signed`, `Cancelled` (`Statuses.ContractStatus`). At most one contract per tender is not Cancelled. |
| `Amount`, `Currency` | Copied from the winning offer when the draft is created, then owned by the contract. A later change of the offer never changes them. |
| `SignedAt` | Signing date, UTC. Only when `Signed`. |
| `DocumentPath` | The stored contract file. Required before `Signed`. |
| `CreatedByUserId`, `CreatedAt` | Who and when the draft was created. |
| `FinalizedByUserId` | Who finalized (Signed) it. |

The final approval may create the **Draft**. It never becomes `Signed` automatically.

Proposed workflow (the team must confirm the owners):

1. **Draft:** created automatically when the tender becomes `Awarded` (Dana, `Areas/Committee/Services/CommitteeDecisionService.cs`, right after the status change): `await _contracts.CreateDraftAsync(tenderId, selectedBidId, userId);`.
2. **Upload:** the **Sourcing Officer** uploads the signed file with the existing upload service (`Services/Files`, Sara) and then calls `SetDocumentAsync(contractId, path)`.
3. **Finalize:** the **Sourcing Manager** calls `MarkSignedAsync(contractId, signedAtUtc, userId)`. It refuses without a stored document or with a future date.
4. **Mistake:** `CancelDraftAsync(contractId)` cancels a Draft so a new one can be created. A Signed contract cannot be cancelled here.

A screen for this (list + upload + finalize button) does not exist yet. Someone must be assigned to build it under Sourcing.

## 4. Reports (Marwa)

* Contract: read `Contracts`. No row = "no contract recorded".
* Repeat rate = suppliers with more than one awarded tender / suppliers with at least one awarded tender x 100. Count **distinct tenders**, dated by final approval. No dates = all time. No supplier with an award = **N/A**, not 0%.
* Verification: show the newest row (`Current()`), and the older rows as history if needed.
* Deadline: see section 1.

## 5. Checks that were run

34 checks on a scratch database (deleted afterwards): the deadline is written once and never reset, the database refuses a second deadline and bad verification rows, a changed verification keeps the old row and `Current()` returns the newest, a contract copies the offer amount and keeps it when the offer changes, cannot be signed without a document, cannot be signed twice, and a cancelled draft can be replaced. The app itself was not started end to end.
