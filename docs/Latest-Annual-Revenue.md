# Latest annual revenue policy

The common Companies House processing service is used by single searches and bulk imports. Each company has one current result in SQLite; the primary key is the registration number. Evidence and manual-review history are separate from the primary revenue value.

## Selection and validation

- Select the relevant annual accounts with the latest known reporting-period end, breaking ties by filing date. A newer accounts filing whose period is missing is flagged for review rather than silently replaced with an older dated filing.
- Process only that filing's advertised supported representations. No earlier accounts document is searched as a fallback for revenue.
- Match the exact statement amount to the year of the selected accounts period. Comparative years remain audit candidates only. A newer statement year conflicting with the filing period blocks verification; the filing year is not selected from that newer statement's comparative column.
- Rounded narrative M/K figures may corroborate an exact amount but cannot supply the result.
- When consolidated accounts are established by statement headings or structured group contexts, prefer the current-year Group value. Individual Company columns do not compete with that primary group value. Missing/unverified current group turnover does not fall back to current company turnover or prior group turnover.
- Verify currency, scale, exactness, year alignment and entity confidence. Unknown or conflicting evidence produces Needs Review with no primary amount.
- Explicit evidence of non-disclosure produces Not Disclosed. Missing labels or unresolved OCR alone do not establish non-disclosure. Revenue is never estimated or substituted from profit/assets/equity.
- Manual confirmation must match the filing period and must use Group when consolidated accounts are established. Selecting a comparative or rounded candidate does not prefill a confirmable revenue amount.

The internal evidence viewer still contains comparative values so reviewers can audit the column mapping. They are not extra company-result rows or export values. Older saved Found results and verified individual-company results with established group evidence are marked Needs Review on loading; Retry Extraction applies the current policy while retaining existing evidence/history.

## Primary table and exports

Both the results table and Excel/CSV exports use exactly these seven fields:

1. Company Registration Number
2. Company Name
3. Latest Revenue
4. Currency
5. Financial Year
6. Extraction Status
7. Source Document

Verified status includes the reporting basis, for example Verified (Consolidated Group) or Verified (Individual Company). It is also shown in the details/evidence area. Unverified revenue is blank/dashed in the primary output. Excel keeps revenue numeric and registration numbers as text. Export deduplicates company numbers by latest reporting period/filing date; an older verified result cannot replace a newer Not Disclosed or Needs Review result. Detailed source, method, confidence, prior-year candidates and manual audit data remain internal rather than becoming export columns.

## Tests

Latest-only tests cover candidate ordering, large prior-year comparatives, missing current revenue, rounded-only evidence, conflicting newer statement years, current group preference, no company/prior-group fallback, newer undated accounts, latest-document-only retrieval, and seven-column deduplicated XLSX/CSV output. The WPF rendering test verifies primary column headers. All existing OCR, manual audit and financial-validation tests remain in the solution.

Run `dotnet test PrasaResarch.slnx -c Release --filter "Category!=Live&Category!=RealDocuments"` for deterministic tests. Live and saved-real-document corpus runners remain separately configured as documented in Revenue-Validation-and-Review.md.
