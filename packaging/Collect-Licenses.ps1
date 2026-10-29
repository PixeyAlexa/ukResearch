param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$repo=Split-Path $PSScriptRoot -Parent
$assets=Get-Content (Join-Path $repo 'PrasaResearch.Desktop/obj/project.assets.json') -Raw | ConvertFrom-Json
$cache=($assets.packageFolders.PSObject.Properties | Select-Object -First 1).Name
foreach($item in $assets.libraries.PSObject.Properties) {
 if($item.Value.type -ne 'package') {continue}
 $folder=Join-Path $cache $item.Value.path
 $target=Join-Path $OutputDirectory ($item.Name.Replace('/','-'))
 New-Item -ItemType Directory -Force $target | Out-Null
 Get-ChildItem -LiteralPath $folder -File | Where-Object {$_.Name -match 'license|copying|notice|\.nuspec$'} | Copy-Item -Destination $target
}
