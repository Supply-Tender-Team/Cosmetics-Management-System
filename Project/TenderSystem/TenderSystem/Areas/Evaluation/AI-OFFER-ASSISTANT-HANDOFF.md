# Evaluation offer advisor integration

Connected to Wadea's `AiOfferAdvisorService`; see `Services/Ai/AI-OFFER-ADVISOR-CONTRACT.md` for its provider contract. Only Evaluation files and its own JavaScript were changed.

`OfferAssistantController.Analyze` is an authenticated, antiforgery-protected POST. It checks actual tender committee membership, publication, the same evaluation states and release notification used by Assessments. Facts come from SQL, never browser-supplied prices or criteria. It includes every submitted Compliant/Accepted bid, dynamic Technical/Financial criteria, buyer requirements, item specifications, costs, delivery/validity, payment terms and document names. More than 12 offers is refused without silently sampling. Shortened text is disclosed. File contents, samples, scans and guarantee validity are not analyzed.

The action logs `OfferAdviceRequested` with tender ID and counts only. It calls AnalyzeAsync once with user ID and cancellation, then rechecks access and current facts. No evaluation score, Ready confirmation, vote, official recommendation or report is saved by this feature. SuggestedBidId is nullable; supplier names are resolved from the authorized request.

The last successful result is cached for 20 minutes under user ID plus tender ID. `Current` rechecks access and fingerprint on page open/tab focus, without a model call. Changed facts display a stale notice. The fingerprint helper is guarded for zero/too many offers. Failed generations preserve the previous advice; cancellation aborts the fetch. Alternatives expand locally. Razor encodes all service text, including coverage notes. No export is implied.

The pearlescent card remains in the assessment sidebar, independent of the asynchronously replaced member panel. Member navigation, unsaved-form protection and the scoring flow are unchanged.

Verification: zero-error build; real SQL read checks for eligibility and dynamic facts; controlled provider tests for suggestion/no-suggestion/rejection, cache ownership/staleness and audit-only writes (suppressed in tests); actual MVC/Razor/browser checks for antiforgery, role and membership denial, cancel, preserved output and expansion without a new provider call. The external production model is not part of these deterministic tests.
