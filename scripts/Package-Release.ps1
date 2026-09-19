# Package dist/<Configuration> into a release .7z a player extracts over their SPT install root.
# The archive IS dist/Release, which already has BepInEx\ and SPT_Runtime\ at the top.
param(
    [string]$Configuration = "Release",
    [string]$OutDir = "P:\",
    [switch]$SkipGates
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repoRoot "dist\$Configuration"
if (-not (Test-Path $dist)) { throw "nothing staged at $dist; build first" }

$plugin = Join-Path $dist "BepInEx\plugins\LennoxP90-MapVariants"
$serverMod = Join-Path $dist "SPT_Runtime\user\mods\LennoxP90-MapVariants"

if (-not $SkipGates) {
    foreach ($required in @(
        (Join-Path $plugin "MapVariants.Client.dll"),
        (Join-Path $plugin "maps.json"),
        (Join-Path $serverMod "MapVariants.Server.dll"),
        (Join-Path $serverMod "MapVariants.Server.deps.json")
    )) { if (-not (Test-Path $required)) { throw "missing from the payload: $required" } }
    Write-Host "gate ok: all four required files are present"
}

$version = ([xml](Get-Content (Join-Path $repoRoot "Directory.Build.props"))).Project.PropertyGroup.ModVersion | Where-Object { $_ }
if (-not $version) { $version = (Get-Item (Join-Path $plugin "MapVariants.Client.dll")).VersionInfo.FileVersion }
$name = "SPT-MapVariants-$version-SPT4.1.7z"
$archive = Join-Path $OutDir $name

if (Test-Path $archive) { Remove-Item $archive -Force }
$sevenZip = @("C:\Program Files\7-Zip\7z.exe", "C:\Programming\7-Zip\7z.exe", "7z") |
    Where-Object { (Get-Command $_ -ErrorAction SilentlyContinue) -or (Test-Path $_) } | Select-Object -First 1
if (-not $sevenZip) { throw "7z.exe not found; install 7-Zip or add it to PATH" }

Write-Host "packing $dist -> $archive"
Push-Location $dist
try {
    & $sevenZip a -t7z -mx9 -bso0 -bsp0 $archive "*" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7z failed ($LASTEXITCODE)" }
}
finally { Pop-Location }

$item = Get-Item $archive
"{0}  {1:N0} bytes  sha256 {2}" -f $item.Name, $item.Length, (Get-FileHash $item.FullName -Algorithm SHA256).Hash
