# Build the Mac installers from Windows

The Mac DMGs require successful native macOS builds and installation tests. The intended repository is https://github.com/PixeyAlexa/ukResearch. Local cross-published binaries alone are not validated Mac installers.

Both native builds and installation tests now pass. See [installer downloads and verified status](macOS-Build-Status.md). The instructions below explain how to rebuild them.

## GitHub route: no personal Mac required

1. Create a private GitHub repository, or use the repository that should own this application.
2. Extract `Releases/macOS/PrasaResearch-Mac-Build-Source.zip` on Windows. **This ZIP contains source code, not an application installer.**
3. Upload the extracted files/folders to the repository root, including the hidden `.github` folder. Do not upload the ZIP as a single repository file: GitHub must see `.github/workflows/release-desktop.yml` directly.
4. Open **Actions → Desktop release packages → Run workflow**.
5. Set **platform = macos** and **version = 1.0.1**, then start the run.
6. The two native jobs test the core, bundle OCR, build the app, run the launch check and create the branded DMGs. Each then mounts the actual DMG, copies its app into an isolated Applications folder and tests the installed app. Download the **PrasaResearch-Setup-macOS-Intel** and **PrasaResearch-Setup-macOS-AppleSilicon** artifacts after successful completion. Separate diagnostics artifacts contain screenshots, launch reports, signature output and checksums.
7. Extract the artifact ZIPs. The actual client installation files are:
   - `PrasaResearch-Setup-macOS-Intel.dmg`
   - `PrasaResearch-Setup-macOS-AppleSilicon.dmg`

No Apple credentials are required to attempt development/ad-hoc builds. Public distribution still needs Developer ID signing/notarization; see Cross-Platform-Migration.md for optional Actions secrets and limitations. Do not label an artifact client-ready until its native build/launch/package checks succeed.

If the source is already in a repository, send its URL so repository access and dispatch can be established. Do not paste passwords, API tokens or signing keys into chat.

## Native Mac host route

With .NET 10, PowerShell, Homebrew, Tesseract and dylibbundler available on a matching-architecture Mac:

```bash
brew install tesseract dylibbundler
bash packaging/Build-macOS.sh osx-arm64 1.0.1  # Apple Silicon host
# Or, on an Intel host:
bash packaging/Build-macOS.sh osx-x64 1.0.1
```

The native script intentionally does not run on Windows. Cross-published binaries are not a substitute for these native packaging/validation steps.
