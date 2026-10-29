# Revenue validation and review

The reusable table/candidate/validation services run for every single-company search and every bulk item. Production extraction code contains no company numbers, names, fixed years, page numbers or company-specific financial amounts.

## Automatic interpretation

`PdfTableRevenueReader` retains a candidate for each year/entity column rather than discarding an unvalidated row. Candidates include original row text, nearby headings, source page/fact, bounding coordinates, year, currency, full-unit amount, scale, exact/rounded classification, statement/narrative classification, entity and OCR confidence. These are saved with the result and displayed in the Results evidence viewer.

Adjacent number fragments such as `1 234 567` are joined within the aligned amount cell; a detached note reference is a separate cluster and is excluded. Current and comparative figures are assigned using column positions and the filing period year. Repeated years are retained for group/company tables. A single-year first-period statement is supported where a genuine year-column heading is present. Headings tolerate OCR-joined forms such as `PROFITAND LOSSACCOUNT`.

Explicit consolidated/group, company/individual/parent headings establish scope. Side-by-side group/company labels are mapped to their year columns using coordinates. Under the latest annual revenue policy, established consolidated Group revenue is primary; Company and prior-year columns remain evidence only. Missing or uncertain current Group revenue cannot be replaced with Company revenue. Unknown scope or uncertain alignment requires review. Arbitrary names in a page header are not assumed to prove company-only accounting.

Comma/space-separated numbers, parentheses, signs, currency symbols and thousands/millions scaling are normalized. OCR letter-to-digit corrections are retained as review issues rather than silently accepted. Tesseract stdout/stderr now explicitly use UTF-8; this fixes console-encoding corruption of currency symbols. The OCR cache format version changed, so existing cached text from the old decoder is regenerated once.

Statement amounts have priority over narrative summaries. Rounded M/K figures can corroborate an exact statement figure within their rounding precision, but cannot supply the final value. Narrative exact values are supporting evidence, not substitutes for a year-aligned statement table. Conflicting exact statement values, missing reporting entity/currency/scale, low confidence or independent OCR-pass disagreement block automatic verification. An audited label is retained as evidence; it is not an assurance that this application independently audited the filing.

`RevenueValidationService` assigns **Verified** only when one exact, aligned current-year amount has known currency, scale and reporting entity, adequate number/year/currency/label/entity confidence, and no unresolved competing statement candidates. Otherwise it returns **Needs Review**, retaining candidates without publishing an unverified amount. Explicit omission evidence is still required for **Not Disclosed**. Internal `Found` means a candidate table/fact was detected; newly processed production results go through the stricter Verified gate. Older saved results should be retried to apply the new rules.

XBRL/iXBRL uses the same candidate model and validation gate. Recognized consolidation-axis members distinguish group, company, parent and unconsolidated contexts. Unresolved dimensions remain unverified. Structured facts without sufficient reporting-entity context require review; a supported advertised PDF is attempted where available.

## Evidence viewer and manual confirmation

1. Select a result, then expand **Revenue evidence and manual review**.
2. Select a candidate to see its full-unit amount, year, entity, exact/rounded status, raw OCR/text, headings, coordinates, confidence and issues.
3. Open the original PDF. New PDF searches retain a hash-addressed copy under `%LOCALAPPDATA%\PrasaResearch\source-documents` for reproducible review, independently of the optional extra copy in the Downloads folder. Existing results may need Retry Extraction before a saved source/evidence is available.
4. Inspect the actual page and enter the exact full-unit amount, three-letter currency, Company/Group scope, filing period end, source page and review reason. Check the explicit source-inspection confirmation.
5. Select **Confirm manually verified revenue**.

The service checks the file SHA-256, PDF page range, matching filing period, required fields and source attestation. A manually confirmed value is labeled **Verified / Manual review / Manually verified — user confirmation**, distinct from automatic evidence validation. The original PDF remains the authority; this is an explicit user review decision, not a claim of independent audit.

SQLite commits the current value and an append-only review entry in one transaction. The entry stores the reviewer (Windows username), UTC timestamp, amount, currency, period, entity, page, source URL, document hash, reason, source evidence and the previous result snapshot. The audit history is shown below the form. Retry may replace the current extraction result after an explicit reprocess request, but previous manual decisions remain in the audit table. Clear All clears result rows, not review history or retained source PDFs. Source copies/cache/history currently have no automatic retention policy.

## Validation performed and limits

Deterministic tests cover fragmented numbers and note references, current/comparative and single-year statements, repeated group/company year columns, rounded corroboration, conflicting exact amounts, unknown scope, OCR substitutions, XBRL consolidation members, source tampering, persisted manual history, and the actual manual-review UI command.

Real Companies House documents were downloaded and analyzed for:

- A full-accounts company: 32-page OCR report. The reported exact turnover and comparative were matched to their respective year headings. Low label confidence/unknown entity scope correctly preserved Needs Review.
- A small company using total-exemption-full accounts: seven-page report. Accounting-policy wording was not treated as revenue; no verified amount was invented.
- A micro-entity: three-page report. Missing revenue evidence remained Needs Review rather than being declared Not Disclosed merely because a revenue label was absent.
- A listed consolidated group: a historical 25-page annual filing. Group candidates were retained, with unresolved currency/OCR evidence flagged for review. Historical filings are used only as test fixtures; production still selects the latest relevant reporting period.

Latest large group downloads also timed out during live checks. Their financial extraction is not certified. A passing corpus runner records an outcome for each input, including incomplete downloads; it does not assert that every input produced a verified revenue value. Local saved-document regression results are stored separately in `artifacts/real-accounts/local-analysis-report.json`; these artifacts contain public source evidence and are ignored by source control.

The live corpus is configured with `COMPANIES_HOUSE_TEST_API_KEY` and `COMPANIES_HOUSE_REAL_ACCOUNTS` (comma-separated numbers), with optional `COMPANIES_HOUSE_REAL_PERIOD` for a historical fixture. Saved-document tests use `COMPANIES_HOUSE_LOCAL_CORPUS`, a JSON manifest of PDF paths, period ends and optional independently supplied candidate expectations. Company identities and expected amounts belong only in test inputs, not extraction algorithms.

This remains a conservative heuristic interpreter, not a guarantee for every UK taxonomy or report layout. Rotated/poor scans, decimal-comma layouts, multi-line cells, ambiguous multi-entity columns and unresolved currency/scale can need manual review. Automated amounts are never inferred from profit/assets/equity. No universal production-accuracy claim is made from this limited real-document corpus.
