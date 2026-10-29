# macOS build status

The existing Windows application, shared core and Avalonia desktop application were uploaded to https://github.com/PixeyAlexa/ukResearch on 2026-10-29.

Successful native macOS build (version 1.0.1, source commit `384a17c`):
https://github.com/PixeyAlexa/ukResearch/actions/runs/40822725141

Both Apple Silicon and Intel jobs completed successfully on macOS 15.7.9. Each passed all 87 shared-core tests with no failures or skips, including native Keychain and OCR tests. Both bundles passed ad-hoc signature verification, application launch/navigation, DMG verification/mount, copying the app out of the DMG and launching that installed copy. Bundled Tesseract and English language data were checked after copying.

Download the installer artifacts:

- [Apple Silicon artifact](https://github.com/PixeyAlexa/ukResearch/actions/runs/40822725141/artifacts/12875101990)
- [Intel artifact](https://github.com/PixeyAlexa/ukResearch/actions/runs/40822725141/artifacts/12874834931)

Local copies are in `Releases/macOS/`:

- `PrasaResearch-Setup-macOS-AppleSilicon.dmg`
- `PrasaResearch-Setup-macOS-Intel.dmg`

Extract the downloaded artifact ZIPs to obtain the corresponding `.dmg` installers. Separate diagnostics artifacts contain launch reports, screenshots and signature information.

Open the matching DMG and drag PrasaResearch.app to Applications. The .NET runtime, native UI/PDF libraries and offline OCR runtime/language data are bundled; clients do not need Homebrew or the .NET SDK.

The signatures are **ad-hoc**, with no TeamIdentifier. These installers are **not Developer ID-signed or notarized**. Gatekeeper approval for public distribution has not been verified. The earlier GitHub billing restriction no longer prevented this successful run. Clean-machine manual UI testing and testing older macOS releases remain outstanding.
