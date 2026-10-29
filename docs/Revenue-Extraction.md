# Revenue extraction pipeline

## What changed

The former PDF implementation counted text across the whole document, grouped word baselines into fixed rounded buckets, and stopped with a generic OCR-not-configured error for scanned accounts. A usable text layer on one page could also mask an unreadable financial statement on another page. There was no renderer, OCR process, or OCR language data.

The pipeline now follows these stages:

1. Retrieve the latest relevant annual accounts filing by reporting-period end and filing date. Follow its document metadata/content links. When metadata includes the owning company number, verify it against the requested company.
2. Prefer metadata-advertised XML/XHTML. Parse recognized XBRL/iXBRL revenue concepts, duration contexts, current period, currency units, signs and inline scale. Unsupported transforms and inconsistent facts require review. A malformed or inaccessible structured representation falls back to another advertised representation.
3. Validate downloaded PDF bytes: PDF header, end marker, and actual parser opening. HTML/JSON error payloads, truncated files and corrupt/encrypted/unsupported PDFs cannot be treated as scanned accounts.
4. Read each PDF page with PdfPig. Assess word count, text length, character quality and replacement characters per page. Preserve text coordinates; cluster nearby baselines by tolerance rather than a rounding boundary.
5. OCR only pages with insufficient usable text. PDFtoImage/PDFium renders the page at 300 DPI, then a local Tesseract 5 subprocess reads English text and word boxes with TSV confidence. OCR coordinates are converted into PDF points so the same table reader handles both paths.
6. Identify an exact turnover/revenue/sales row in an income statement, align monetary columns with reporting years, choose the known latest accounts year, and apply explicit currency and thousands/millions notation. Additional comparative-year columns and note-reference numbers are handled by column positions. Profit, equity and assets are not revenue labels. Repeated year columns and conflicting figures are not guessed.
7. For an OCR-derived amount, require high confidence on critical table words and agreement between Tesseract's PSM 6 and PSM 3 layout passes. Low confidence or disagreement returns Needs Review without an amount.
8. Preserve the filing URL, page/fact evidence, extraction method and validation description in the saved result. Retry Extraction starts the selected company through this pipeline again, including downloading its current latest filing; it does not reuse a cached extraction result.

## Install and configure OCR

This development machine has **Tesseract 5.4.0.20240606** and `eng.traineddata` installed and verified. The automated image-only PDF test executes that engine and checks the extracted amount, currency, source page and agreement status.

On another Windows machine, run `scripts/Install-Ocr.ps1`, or install the official-documentation-recommended UB Mannheim Windows distribution:

```powershell
winget install --id UB-Mannheim.TesseractOCR --exact --source winget --silent --accept-package-agreements --accept-source-agreements
```

Default engine: `C:\Program Files\Tesseract-OCR\tesseract.exe`.
Required English data: `C:\Program Files\Tesseract-OCR\tessdata\eng.traineddata`.
The installer supplies the engine's native DLL dependencies. Check `tesseract --version` and `tesseract --list-langs` if a subprocess fails.

The application restores **PDFtoImage 5.4.0** through NuGet. Its transitive packages provide PDFium, SkiaSharp and their native assets; deploy the native runtime files with the application. NuGet restore alone does not install Tesseract or its language data. No paid library, cloud OCR account or OCR API key is required.

In Settings, enable offline OCR, check the Tesseract executable path and select **Process all pages requiring OCR**. The maximum is **0 for all pages**, which is the default; any positive integer is a deliberate page budget. There is no 30-page or 200-page clamp. Save Settings, then select a result and Retry Extraction. The temporary folder is used for unique OCR page-image directories; each is cleaned in a finally block, including cancellation/failure paths. A failed cleanup is logged. Only the directory created by that OCR invocation is removed.

Before OCR, the application inventories native text across the document. Income statement, profit-and-loss, turnover and revenue headings, their neighboring pages, and page numbers in contents/reference lines receive priority. Text recognized on an OCR page updates the priority queue too, allowing scanned contents pages to guide later processing. Printed/PDF page offsets are accounted for heuristically by prioritizing nearby pages. Fully scanned documents without readable hints initially use page order. Prioritization changes order, never invents evidence or marks unseen pages as checked.

Pages with sufficient native text are skipped by OCR. Full-document mode continues through every remaining OCR candidate and preserves conflict checking across pages. If a deliberate positive budget leaves pages unread, the result is **Needs Review / Incomplete page coverage**, with no published amount and instructions to enable all pages. **OCR Required** is reserved for disabled/unavailable OCR support.

OCR words and confidence are cached persistently in `%LOCALAPPDATA%\PrasaResearch\ocr-cache`. Cache keys include the entire PDF SHA-256, page, layout pass, 300-DPI/English configuration, and executable/language-data identity. Each retry still downloads/checks the latest filing, then reuses matching OCR pages; a changed PDF or engine/data identity causes a cache miss. Financial validation reruns with current code and settings even when OCR words are reused. Cache writes use atomic replacement; corrupt entries are recomputed. The cache contains local document text, can be removed when the app is stopped, and has no automatic age/size eviction in this version.

Global concurrency is bounded to **two rendering/OCR workers**, with Tesseract limited to two OpenMP threads per process. Pages within a document run sequentially so priority can adapt and memory remains bounded. Concurrent requests for the same cache key share a lock and reuse its result.

## Status meanings

| Status | Meaning |
|---|---|
| Verified | The production validation gate established one exact current-year statement amount, currency, scale and reporting entity without unresolved competing evidence. Manual confirmation is separately labeled Manual review. Internal/legacy Found is not the new Verified status. |
| Not Disclosed | Positive evidence that the profit and loss account was omitted/not filed; missing labels alone are insufficient. Low-confidence OCR omission text is downgraded to Needs Review. |
| Needs Review | Ambiguous amount/year/scope, unsupported table layout/taxonomy, no reliable label, OCR disagreement, or the overall search budget was reached. |
| OCR Required | A page needs OCR but OCR is disabled or the executable/language data is missing. A deliberate page budget instead produces Needs Review; incomplete coverage never publishes an amount. |
| Document Unavailable | No supported advertised representation/document link, or a requested filing resource returns 404. |
| Extraction Failed | Invalid/truncated/unparseable PDF, renderer/OCR execution failure, no readable text even after OCR, or other unrecoverable processing error. |

The separate No Accounts status means no relevant annual accounts filing was found.

## Diagnostics

`%LOCALAPPDATA%\PrasaResearch\extraction.jsonl` is a rotating JSON-lines log. Stages include document_metadata, document_download, structured_parsing, structured_identification, pdf_parsing, pdf_text, ocr, ocr_cache, ocr_coverage, ocr_summary, ocr_cleanup and processing. The coverage summary runs on success, failure and cancellation, reporting total pages, text-inspected pages, OCR-needed/checked pages, actual engine pages, cached pages, skipped native-text pages, remaining pages and confidence. Cache and engine-page counts can overlap when one layout pass was cached and the other was recomputed. The processing entry records the company and active step on failure. Logs exclude API keys, Authorization headers, signed redirect URLs, OCR command arguments and extracted document text. Evidence remains in the selected result/database.

The existing processing panel and footer show document download activity, bytes/MB, percentage when Content-Length is known, and retry/waiting states. Percentages come from streamed byte counts; no percentage is invented for unknown-size responses. OCR activity reports source page, checked/required counts, skipped native pages and cache counts. The main progress bar continues to represent completed companies.

## Tested and remaining limits

Offline tests cover generated text PDFs, rasterized image-only PDFs, mixed text/scanned PDFs, actual installed Tesseract and persistent cache reuse, 35-page scanned/native documents, budgets greater than 30, priority references beyond page 30, cancellation after 30 pages, coverage diagnostics, streaming download progress, OCR confidence/disagreement, invalid/truncated/corrupt payloads, baseline tolerance, two/three-year tables, currency/scale, structured current/comparative contexts, and UI Retry reprocessing. These synthetic fixtures are isolated in the test project and never displayed as real company results. Tests requiring the actual Tesseract engine are explicitly skipped if it is unavailable.

OCR correctness on clean fixtures does not establish correctness on every filing. Faint scans, handwriting, rotated pages, split labels, unusual fonts, side-by-side group/company statements, repeated current-year columns and non-English accounts can require review. Numeric OCR confidence is an engine score, not an audited probability of correctness. No amounts are inferred from profit/assets or substituted from an older year.

OCR has a 45-second subprocess limit per pass and an optional page budget. **All-pages mode has no overall company deadline** so large reports can finish; requests and OCR subprocesses still have their own timeouts, and Cancel remains available. Limited-page mode keeps a six-minute company deadline with OCR enabled and two minutes with it disabled. An injected shorter budget is used by the live timeout regression test. Native PDF parsing/rendering is synchronous within background work; cancellation is checked between native calls and cannot interrupt a native call already in progress. Large or poor-quality reports can take substantial time or require manual review. Incomplete OCR coverage blocks a Found result.

XBRL support is for well-formed XML/XHTML and known revenue concepts. ZIP packages, JSON financial accounts, malformed HTML iXBRL, unknown taxonomy tags and unresolved segmented/consolidation contexts are not automatically resolved. PDF currency recognition supports GBP, EUR, USD, CAD, AUD, JPY and CHF; a bare dollar sign without an explicit currency code remains ambiguous. Current-year table selection requires the filing's reporting-period end. Multi-line or unusual number formats may require review.

Live API profile/filing/metadata verification and the live timeout regression are separate from document-fixture correctness. Slow or inaccessible Companies House storage downloads can still prevent live extraction, in which case the result reports the failure/limit with retained company details.

References: [Tesseract installation](https://tesseract-ocr.github.io/tessdoc/Installation.html), [Tesseract command-line/TSV documentation](https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html), [PDFtoImage](https://github.com/sungaila/PDFtoImage).
