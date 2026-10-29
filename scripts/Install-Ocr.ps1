$ErrorActionPreference = 'Stop'
# The engine and English language data are installed locally; no account or cloud service is used.
winget install --id UB-Mannheim.TesseractOCR --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) { throw 'Tesseract installation did not complete. Check winget output.' }
$enginePath = Join-Path $env:ProgramFiles 'Tesseract-OCR\tesseract.exe'
$languagePath = Join-Path $env:ProgramFiles 'Tesseract-OCR\tessdata\eng.traineddata'
if (-not (Test-Path -LiteralPath $enginePath) -or -not (Test-Path -LiteralPath $languagePath)) {
    throw 'Tesseract executable or English language data is missing. Set the installed executable path in application Settings.'
}
& $enginePath --version
& $enginePath --list-langs
