# PrasaResearch — Companies House Revenue Explorer

Native WPF / MVVM application in the existing `PrasaResarch` solution and namespace, targeting **.NET 10 for Windows**. Production code contains no sample company results or simulated API responses. Test fixtures live only in the test project.

## Run and configure

1. Open `PrasaResarch.slnx` in Visual Studio with the .NET desktop development workload and .NET 10 SDK.
2. Set `PrasaResarch` as the startup project and press F5, or run `dotnet run --project PrasaResarch`.
3. Open **Settings**, enter a Companies House REST API key, and select **Save Settings** followed by **Test API Connection**. The test makes a real authenticated profile request. Register an application/key through https://developer.company-information.service.gov.uk/.
4. Enter a registration number in **Single Company Search** and select **Get Revenue**. Select the resulting row to inspect its address, extraction evidence and any error. Use **Open Original Filing** or **Download Source** to inspect the source.
5. In **Bulk Import**, browse/drop an `.xlsx` or `.csv` file, select its company-number column and inspect valid/invalid entries. The preview displays at most 30 entries per category. Select **Add to Queue**, then **Start Processing**. Existing company numbers are skipped.
6. **Pause** stops admission of new companies; requests already in flight finish and save. **Resume** continues. **Cancel** cancels requests and leaves unfinished rows Pending. After restarting, use **Start Processing** to resume these persisted rows. Select a row and **Retry Extraction**, or deselect rows to retry failed/unavailable results.
7. **Results** supports name/number search, status filtering, column sorting, virtualization, row evidence and filing actions. **Export** selects all/filtered/successful/failed rows and writes Excel or CSV using a save dialog.

## Data and credentials

User data lives in `%LOCALAPPDATA%\PrasaResearch`:

- `credential.bin`: API key protected by Windows DPAPI, scoped to the current Windows user.
- `settings.json`: timeout, concurrency, rate, download folder, optional source PDF saving and theme. No API key.
- `jobs.db`: SQLite WAL database with the persistent queue and latest result per company. This is a single continuing workspace; there is no historical multi-job browser. Clear All removes its rows.
- `failures.log`: timestamp, company number, exception type and failure status; no request headers or credentials. `startup.log` records startup exceptions.

Documents are bounded to 80 MB. OCR page images use unique directories under the configured temporary folder and are cleaned after use. Original source PDFs are saved only when enabled. Results and evidence are persisted in SQLite.

## Extraction behavior and limits

The pipeline prefers XBRL/iXBRL, validates PDF downloads, reads native text per page, and uses local Tesseract OCR only for scanned or unusable-text pages. Financial tables are checked against reporting years, currency, scale, word positions and conflicting evidence. OCR candidates also require high confidence and agreement between two layout passes. Missing OCR dependencies produce OCR Required; partial coverage and ambiguous figures produce Needs Review. Settings supports all-pages OCR (maximum 0) or any positive page budget. Page prioritization, persistent OCR caching and two global OCR workers support long reports. Download activity shows actual streamed byte counts and percentages when available.

See [Revenue extraction: setup, methods, diagnostics, tests and limits](docs/Revenue-Extraction.md) for full details. OCR is installed and tested on this machine; other machines need Tesseract 5 with English language data. Run `scripts/Install-Ocr.ps1` to install it. PDFtoImage/PDFium/SkiaSharp are restored with NuGet. Settings exposes the OCR toggle, engine path and page budget.

The shared [financial validation and manual review workflow](docs/Revenue-Validation-and-Review.md) retains year/entity candidates, uses rounded figures only as corroboration, and applies a strict Verified gate to new production results. Results includes an expandable evidence viewer and manual confirmation form with original-PDF hash checks and persistent review history.

The [latest annual revenue policy](docs/Latest-Annual-Revenue.md) selects one latest reporting period per company, prefers established consolidated group revenue, excludes prior-year comparatives from the primary result, and limits the table/exports to seven primary fields. Earlier years remain audit evidence only.

`extraction.jsonl` in the application data folder records download, parsing, OCR and financial-identification stages without credentials or document text. Select a result and use Retry Extraction to run the entire pipeline again.

Exports retain numeric revenue and text company numbers in XLSX. CSV files retain the company-number text, but spreadsheet applications may auto-convert it on opening; XLSX avoids that behavior. No real financial figures are invented or inserted into production results.

## API contract

The implementation uses HTTP Basic authentication (key as username, empty password), HTTPS, paginated filing history, returned metadata/content links, and content negotiation. Downloads follow server redirects through HttpClient, which strips Authorization on redirects. Invalid credentials and permanent errors are not retried. Transient failures have bounded exponential retries and honor Retry-After. A shared sliding-window limiter and bounded company concurrency apply to all API requests.

Official documentation:

- https://developer-specs.company-information.service.gov.uk/companies-house-public-data-api/reference/filing-history/list
- https://developer-specs.company-information.service.gov.uk/document-api/reference/document-metadata/fetch-a-documents-metadata
- https://developer-specs.company-information.service.gov.uk/document-api/reference/document-location/fetch-a-document
- https://developer.company-information.service.gov.uk/developer-guidelines

## Build and tests

```powershell
dotnet restore PrasaResarch.slnx
dotnet build PrasaResarch.slnx -c Release
dotnet test PrasaResarch.slnx -c Release --filter "Category!=Live"
```

Offline tests use deterministic API handlers and document fixtures: normalization, API parsing/errors/retries, filing period selection, XBRL sign/scale/currency/comparatives/ambiguity, real generated PDF byte parsing, coordinate table parsing, missing/scanned revenue, CSV/XLSX imports, exports, cancellation/restart/pause, rate-limit cancellation, end-to-end metadata retrieval, and WPF window navigation/rendering. The window test writes `artifacts/dashboard.png`.

Live integration is separate and skipped unless `COMPANIES_HOUSE_TEST_API_KEY` is supplied. Set it outside source control, then run `dotnet test --filter "Category=Live"`. Never put a real key in a command committed to source or in the test fixtures. Live profile/filing/document metadata behavior cannot be certified from offline tests alone.

## Main files and dependencies

- `MainWindow.xaml`, `App.xaml`: native dashboard, templates, navigation, theme and controls.
- `ViewModels/MainViewModel.cs`, `Commands/RelayCommand.cs`: bindings, application actions and async command handling.
- `Models/Domain.cs`: company/result/settings/import models and company-number normalization.
- `Services/CompaniesHouseApiService.cs`: API interface/client, throttling, retries and response parsing.
- `Services/RevenueExtractionService.cs`, `Services/PdfTableRevenueReader.cs`: document metadata, processing, XBRL and PDF extraction.
- `Services/BulkImportService.cs`, `Services/ExportService.cs`: actual file import/export.
- `Services/BulkProcessingService.cs`, `Data/JobPersistenceService.cs`: bounded processing and persistent results/queue.
- `Services/SettingsService.cs`: settings and DPAPI credential storage.
- `PrasaResarch.Tests/`: offline tests, WPF rendering test, opt-in live integration test.

NuGet: ClosedXML 0.105.1, CsvHelper 33.1.0, Microsoft.Data.Sqlite 10.0.14, Microsoft.Extensions.DependencyInjection 10.0.14, PdfPig 0.1.16, PDFtoImage 5.4.0 (PDFium/SkiaSharp transitive native dependencies). DPAPI comes from the Windows desktop target. Tests use xUnit, Microsoft.NET.Test.Sdk, the Visual Studio xUnit adapter and coverlet.


The original WPF application is preserved. The new PrasaResearch.Desktop Avalonia app and PrasaResearch.Core library target Windows/macOS. See [cross-platform migration and installer instructions](docs/Cross-Platform-Migration.md) for builds, tested results, native macOS packaging, and release prerequisites.
