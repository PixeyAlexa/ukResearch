# Cross-platform migration and release guide

The original `PrasaResarch/` WPF project and its tests are preserved. `packaging/wpf-preserved.sha256.json` records the WPF source hashes. The solution adds projects without replacing the original startup project.

## Safe migration plan and source layout

1. Preserve WPF sources and regression tests.
2. Extract Models, Services and Data into `PrasaResearch.Core`, retaining namespaces and JSON compatibility. Replace platform paths and credential adapters there.
3. Add `PrasaResearch.Desktop`, an Avalonia 12 / .NET 10 UI referencing that one shared core for Windows and macOS.
4. Port commands, dispatching and native dialogs; run shared tests and Windows native UI checks.
5. Publish self-contained win-x64, osx-x64 and osx-arm64 binaries.
6. Bundle OCR on native packaging hosts, test the Windows installer, and execute macOS bundle/DMG validation on matching runners.

WPF remains a frozen legacy implementation rather than being relinked to the new core. This deliberately avoids changing the working original. Future cross-platform engine changes belong in the new core; bringing WPF onto it would be a separate migration.

## Run

```powershell
dotnet run --project PrasaResearch.Desktop
dotnet test PrasaResearch.Core.Tests -c Release --filter "Category!=Live&Category!=RealDocuments"
```

In Visual Studio choose PrasaResearch.Desktop for Avalonia or PrasaResarch for the preserved WPF app. The new UI retains all six sections: dashboard, single search, bulk import, results, export and settings. It includes native pickers, drag/drop, column preview, bounded processing, pause/resume/cancel/retry, SQLite resume, seven-field latest-revenue results, filtering/sorting, source downloads, evidence/manual review and audit, CSV/XLSX exports, settings and real API connection testing. Commands use the shared services; no mock company results are inserted.

The custom navy/blue/violet logo and installer art are generated from brand geometry by packaging/Generate-Branding.ps1. Native Avalonia controls provide rounded panels, gradients, hover transitions and custom move/resize/window controls. Platform-native file dialogs and typography can differ.

## Credentials and user data

- Windows Avalonia data: `%LOCALAPPDATA%\PrasaResearch\Desktop`. A one-time read-only import copies legacy settings and DPAPI credentials and uses SQLite BackupDatabase for a consistent jobs/audit snapshot from the WPF data folder. The apps then keep independent settings/databases; the original WPF files are never overwritten. Existing source-file references remain valid in retained user data.
- macOS: `~/Library/Application Support/PrasaResearch`. API keys use native Security.framework Keychain APIs under service `com.prasaresearch.desktop`; the key is never passed to a shell command.
- macOS OCR cache: `~/Library/Caches/PrasaResearch/ocr-cache`. Temporary OCR images use the configured OS temporary folder.
- `PRASARESEARCH_DATA_DIR` isolates test data. The installer does not set it for normal users.
- API keys and user databases are never copied into publish directories or installers. Upgrades/uninstall preserve settings, history, source evidence and secure credentials.

The Windows installer bundles Tesseract DLLs, English/osd data and configuration files as an option enabled by default. macOS packaging copies native Homebrew Tesseract/Leptonica libraries, relocates them with dylibbundler and includes language data. PDFium/SkiaSharp assets come through NuGet. No cloud OCR service is used. Native dependency execution on macOS still requires its runner validation.

## Windows installer

```powershell
winget install --id JRSoftware.InnoSetup --exact --source winget
./scripts/Install-Ocr.ps1
./packaging/Build-Windows.ps1 -Version 1.0.0
./packaging/Test-WindowsInstaller.ps1
```

Output: `Releases/Windows/PrasaResearch-Setup-Windows.exe`.

The branded dark Inno Setup installer has welcome, license/notice, location, options, progress and completion screens; application icon/sidebar graphics; per-user installation; optional desktop shortcut; Start Menu entry; bundled OCR; stable AppId; uninstaller; and optional launch. Framework-native button shapes use the supported dark style rather than fragile third-party skinning. Publishing is self-contained. No uninstall rule targets user data.

The compiler installed here is Inno Setup 6.7.3 and reports **Non-commercial use only**. This deliverable is a development/evaluation package, not an appropriately licensed commercial installer release. Arrange the appropriate compiler licensing/build environment or replace the installer framework before commercial distribution. No publisher EULA was found: packaging/Distribution-Notice.txt explicitly states that approved commercial terms are still needed. Third-party notices and available NuGet license files/metadata are collected into licenses/. Publisher license review remains a release task.

The Windows EXE is unsigned. No signing credentials were assumed. The test script uses a private install/data directory, an isolated shortcut group, and refuses to run if the installer AppId is already registered. Tests cover same-version reinstall and a development 1.0.0-to-1.0.1 package upgrade, including upgraded launch and data preservation; this is not an exhaustive future schema-migration guarantee.

## macOS packages

On matching native architecture hosts:

```bash
brew install tesseract dylibbundler
bash packaging/Build-macOS.sh osx-x64 1.0.0       # Intel
bash packaging/Build-macOS.sh osx-arm64 1.0.0     # Apple Silicon
```

Expected native-runner outputs:

```text
Releases/macOS/PrasaResearch-Setup-macOS-Intel.dmg
Releases/macOS/PrasaResearch-Setup-macOS-AppleSilicon.dmg
Releases/macOS/PrasaResearch-Intel.app.zip
Releases/macOS/PrasaResearch-AppleSilicon.app.zip
```

The script creates an app bundle, com.prasaresearch.desktop identifier, version metadata, ICNS icon, permissions and bundled OCR. The Finder DMG uses a custom navy/violet background, Applications link and drag-to-install layout. It uses native iconutil, hdiutil, Finder/AppleScript and codesign, and intentionally refuses to manufacture DMGs on Windows. No optional PKG wizard was added because the requested native experience is a DMG.

Without credentials the script applies an **ad-hoc development signature only**. That is not Developer ID signing, notarization, or a public Gatekeeper-approved release. With MACOS_SIGN_IDENTITY it signs Mach-O binaries/app with hardened runtime and .NET JIT entitlements. MACOS_NOTARY_PROFILE enables notarytool submission, success checking and ticket stapling. Public signing/notarization has not run here.

Optional GitHub secrets: MACOS_P12_BASE64, MACOS_P12_PASSWORD, MACOS_SIGN_IDENTITY, APPLE_ID, APPLE_APP_PASSWORD and APPLE_TEAM_ID. They are never committed or packaged. Adjust publisher/bundle identity to your owned brand before public release. Native packaging requires matching Intel/ARM runners because OCR libraries are native.

## GitHub Actions without owning a Mac

.github/workflows/release-desktop.yml supports Run workflow and desktop-v* tags. Windows jobs run shared and original WPF tests, build the installer, install/launch/reinstall/uninstall it and upload artifacts. Separate macos-15-intel and macos-15 Apple Silicon jobs install OCR tools, execute shared tests, package/sign the app, launch it, build/verify DMGs and upload outputs. Optional signing steps are skipped without credentials. Artifacts are uploaded; no public GitHub Release is automatically published.

Push this source/workflow to your GitHub repository and choose Actions > Desktop release packages > Run workflow. GitHub's macOS hosts do the native work, so no personal Mac is necessary. The workflow has been prepared but not dispatched in this session: there is no connected repository runner access or macOS host here. A failed native job is a release blocker, not a successful Mac package.

## Validation actually executed

- Avalonia builds and launches natively on the Windows host; all six sections were visited and a screenshot captured with --smoke-output=path.
- Shared core tests cover API behavior, latest-only extraction, text/scanned PDFs, actual Tesseract, import/export, SQLite jobs, cancellation/resume, manual audit and Windows credential roundtrip/update.
- The preserved WPF regression tests ran without source changes.
- Windows installer compilation, installation, installed launch, bundled OCR, reinstall/upgrade, uninstall and test-data/database preservation passed.
- osx-x64 and osx-arm64 self-contained publish outputs were prepared on Windows. This is cross-publish validation, not a macOS launch test.
- No macOS Keychain/OCR/native UI test, DMG creation/mount, public signing or notarization has executed here. Those require the configured native jobs.
- Windows was tested on the current host, not separate Windows 10 and Windows 11 clean-machine VMs. Interactive native dialogs, accessibility, version-to-version upgrades and each target OS still need commercial-release QA.

API live tests remain opt-in through a separately configured COMPANIES_HOUSE_TEST_API_KEY. Offline fixtures never show as real production company results.

References: [Avalonia macOS deployment](https://docs.avaloniaui.net/docs/deployment/macos), [native file dialogs](https://docs.avaloniaui.net/docs/services/file-dialogs), [Inno Setup styles](https://jrsoftware.org/ishelp/topic_setup_wizardstyle.htm), [GitHub runner architectures](https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job).



Set the optional Actions secret COMPANIES_HOUSE_TEST_API_KEY to execute live authentication/filing tests on each platform. Without it, workflows explicitly report live validation as unconfigured. The final Windows test record covers the 1.0.0-to-1.0.1 package upgrade and user-data preservation.
