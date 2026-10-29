param([string]$Installer = "$PSScriptRoot/../Releases/Windows/PrasaResearch-Setup-Windows.exe", [string]$PreviousInstaller = '')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$target=Join-Path $repo 'artifacts/installer-test/application'
$data=Join-Path $repo 'artifacts/installer-test/user-data'
$registry='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9AE793C6-A7E0-4DBC-BBFD-05833E025D49}_is1'
if(Test-Path $registry){throw 'PrasaResearch is already installed for this user. Test on a clean runner to avoid replacing that registration.'}
New-Item -ItemType Directory -Force $data | Out-Null
$sentinel=Join-Path $data 'preserve-me.txt'; Set-Content -LiteralPath $sentinel 'user-data-must-survive'
$first = if($PreviousInstaller){$PreviousInstaller}else{$Installer}
$process=Start-Process -FilePath (Resolve-Path $first) -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$target+'"'),'/GROUP=PrasaResearch-PackageTest','/TASKS=offlineocr') -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0){throw "Installation failed: $($process.ExitCode)"}
$env:PRASARESEARCH_DATA_DIR=$data
try {
 $app=Join-Path $target 'PrasaResearch.exe'; $smoke=Join-Path $data 'launch.json'
 $p=Start-Process $app -ArgumentList ('--smoke-output='+$smoke) -Wait -PassThru -WindowStyle Hidden
 if($p.ExitCode -ne 0 -or -not(Test-Path $smoke)){throw 'Installed application launch failed'}
 & (Join-Path $target 'ocr/tesseract.exe') --list-langs
 if($LASTEXITCODE -ne 0){throw 'Bundled OCR runtime failed'}
 # Reinstall with the same AppId exercises the upgrade path.
 $p=Start-Process -FilePath (Resolve-Path $Installer) -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$target+'"'),'/GROUP=PrasaResearch-PackageTest','/TASKS=offlineocr') -Wait -PassThru -WindowStyle Hidden
 if($p.ExitCode -ne 0){throw 'Upgrade/reinstall failed'}
 $p=Start-Process $app -ArgumentList ('--smoke-output='+(Join-Path $data 'upgraded-launch.json')) -Wait -PassThru -WindowStyle Hidden
 if($p.ExitCode -ne 0){throw 'Upgraded application launch failed'}
 if((Get-Content $sentinel -Raw).Trim() -ne 'user-data-must-survive'){throw 'Upgrade changed user data'}
 $p=Start-Process -FilePath (Join-Path $target 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru -WindowStyle Hidden
 if($p.ExitCode -ne 0){throw 'Uninstall failed'}
 if((Get-Content $sentinel -Raw).Trim() -ne 'user-data-must-survive' -or -not(Test-Path (Join-Path $data 'jobs.db'))){throw 'Uninstall removed user data'}
 if(Test-Path $app){throw 'Uninstaller left the application executable'}
 $upgrade = if($PreviousInstaller){'previous-package to current-package passed'}else{'same-version reinstall passed'}
 [pscustomobject]@{Install='passed';Launch='passed';OCR='passed';Upgrade=$upgrade;UpgradedLaunch='passed';Uninstall='passed';DataPreservation='passed'} | ConvertTo-Json | Set-Content (Join-Path $repo 'artifacts/windows-installer-test.json')
 Get-Content (Join-Path $repo 'artifacts/windows-installer-test.json')
} finally { Remove-Item Env:PRASARESEARCH_DATA_DIR -ErrorAction SilentlyContinue }
