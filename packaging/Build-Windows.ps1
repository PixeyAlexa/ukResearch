param([string]$Version = '1.0.0', [string]$TesseractRoot = "$env:ProgramFiles/Tesseract-OCR", [string]$Iscc = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $repo 'artifacts/publish/win-x64'
dotnet publish (Join-Path $repo 'PrasaResearch.Desktop/PrasaResearch.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
if (-not (Test-Path -LiteralPath (Join-Path $TesseractRoot 'tesseract.exe'))) { throw 'Install Tesseract with scripts/Install-Ocr.ps1 before packaging.' }
$ocr = Join-Path $publish 'ocr'; New-Item -ItemType Directory -Force $ocr,(Join-Path $ocr 'tessdata') | Out-Null
Copy-Item -LiteralPath (Join-Path $TesseractRoot 'tesseract.exe') -Destination $ocr
Get-ChildItem -LiteralPath $TesseractRoot -Filter '*.dll' | Copy-Item -Destination $ocr
Copy-Item -LiteralPath (Join-Path $TesseractRoot 'tessdata/eng.traineddata') -Destination (Join-Path $ocr 'tessdata')
Copy-Item -LiteralPath (Join-Path $TesseractRoot 'tessdata/osd.traineddata') -Destination (Join-Path $ocr 'tessdata')
if (Test-Path -LiteralPath (Join-Path $TesseractRoot 'tessdata/configs')) { Copy-Item -LiteralPath (Join-Path $TesseractRoot 'tessdata/configs') -Destination (Join-Path $ocr 'tessdata') -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $repo 'packaging/THIRD-PARTY-NOTICES.txt') -Destination $publish
if (Test-Path -LiteralPath (Join-Path $TesseractRoot 'doc')) { Copy-Item -LiteralPath (Join-Path $TesseractRoot 'doc') -Destination (Join-Path $ocr 'licenses') -Recurse -Force }
& (Join-Path $PSScriptRoot 'Collect-Licenses.ps1') -OutputDirectory (Join-Path $publish 'licenses')
if (-not $Iscc) {
 foreach ($candidate in @("$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe", "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe")) { if (Test-Path -LiteralPath $candidate) { $Iscc = $candidate; break } }
}
if (-not $Iscc) { throw 'Install Inno Setup 6.7 or pass -Iscc.' }
& $Iscc "/DPublishDir=$publish" "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'windows/PrasaResearch.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$installer = Join-Path $repo 'Releases/Windows/PrasaResearch-Setup-Windows.exe'
Get-FileHash -LiteralPath $installer -Algorithm SHA256 | Format-List
