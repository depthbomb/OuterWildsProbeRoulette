[CmdletBinding()]
param(
    [string] $GamePath = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Outer Wilds'),
    [string] $OwmlPath = (Join-Path $env:APPDATA 'OuterWildsModManager\OWML'),
    [string] $OutputDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Codex\ProjectNotes\outer-wilds-probe-roulette\releases'),
    [string] $ReleaseDocsPath = $PSScriptRoot,
    [switch] $PackageOnly
)

$ErrorActionPreference = 'Stop'
$projectPath           = Join-Path $PSScriptRoot 'ProbeRoulette\ProbeRoulette.csproj'
$testProjectPath       = Join-Path $PSScriptRoot 'Targeting.Tests\Targeting.Tests.csproj'
$outputPath            = Join-Path $PSScriptRoot 'ProbeRoulette\bin\Release\net48'
$assemblyPath          = Join-Path $GamePath 'OuterWilds_Data\Managed\Assembly-CSharp.dll'
$modAssemblyPath       = Join-Path $outputPath 'ProbeRoulette.dll'
$sourceManifest        = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ProbeRoulette\manifest.json') -Raw | ConvertFrom-Json
$modId                 = $sourceManifest.uniqueName
if ($modId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw 'The manifest contains an invalid mod folder name.'
}

$packagePath           = Join-Path $OutputDirectory "$($sourceManifest.version)\$modId"
$modsPath              = [System.IO.Path]::GetFullPath((Join-Path $OwmlPath 'Mods'))
$installPath           = Join-Path $modsPath $modId
$legacyInstallPath     = Join-Path $modsPath 'CaprineLogic.ProbeRoulette'
$installedManifestPath = Join-Path $installPath 'manifest.json'

if (!(Test-Path -LiteralPath (Join-Path $OwmlPath 'OWML.ModHelper.dll'))) {
    throw "OWML was not found at $OwmlPath."
}

if (!$PackageOnly -and (Test-Path -LiteralPath $installPath)) {
    if (!(Test-Path -LiteralPath $installedManifestPath)) {
        throw 'The destination exists without a manifest; refusing to overwrite it.'
    }

    $installedManifest = Get-Content -LiteralPath $installedManifestPath -Raw | ConvertFrom-Json
    if ($installedManifest.uniqueName -ne $modId) {
        throw 'The destination belongs to another mod; refusing to overwrite it.'
    }
}

$migrateLegacy = !$PackageOnly -and $modId -eq 'Depthbomb.ProbeRoulette' -and (Test-Path -LiteralPath $legacyInstallPath)
if ($migrateLegacy) {
    if (Test-Path -LiteralPath $installPath) {
        throw 'Both old and new mod folders exist; resolve the duplicate before installing.'
    }

    $legacyManifestPath = Join-Path $legacyInstallPath 'manifest.json'
    $legacyManifest     = Get-Content -LiteralPath $legacyManifestPath -Raw | ConvertFrom-Json
    if ($legacyManifest.uniqueName -ne 'CaprineLogic.ProbeRoulette') {
        throw 'The old folder belongs to an unexpected mod; refusing to migrate it.'
    }
}

& dotnet build $projectPath -c Release "-p:GamePath=$GamePath" "-p:OwmlPath=$OwmlPath"
if ($LASTEXITCODE -ne 0) {
    throw 'Mod build failed.'
}

& dotnet run --project $testProjectPath -c Release "-p:OwmlPath=$OwmlPath" -- $assemblyPath $modAssemblyPath
if ($LASTEXITCODE -ne 0) {
    throw 'Targeting tests failed.'
}

$files = @('ProbeRoulette.dll', 'ProbeRoulette.pdb', 'manifest.json', 'default-config.json')
New-Item -ItemType Directory -Path $packagePath -Force | Out-Null
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $outputPath $file) -Destination (Join-Path $packagePath $file) -Force
}

$packageManifest = Get-Content -LiteralPath (Join-Path $packagePath 'manifest.json') -Raw | ConvertFrom-Json
$zipPath         = Join-Path $OutputDirectory "ProbeRoulette-$($packageManifest.version).zip"
$archiveFiles    = @($files | ForEach-Object { Join-Path $packagePath $_ })
foreach ($document in @('README.md', 'CHANGELOG.md', 'LICENSE')) {
    $documentPath = Join-Path $ReleaseDocsPath $document
    if (Test-Path -LiteralPath $documentPath) {
        Copy-Item -LiteralPath $documentPath -Destination (Join-Path $packagePath $document) -Force
        $archiveFiles += Join-Path $packagePath $document
    }
}

# List the files explicitly so an old config or stray game DLL can never slip into the release.
Compress-Archive -LiteralPath $archiveFiles -DestinationPath $zipPath -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($file in $archiveFiles) {
        if ([System.IO.Path]::GetFileName($file) -notin $entryNames) {
            throw "Missing release file: $file"
        }
    }

    if ($archive.Entries.Count -ne $archiveFiles.Count -or 'config.json' -in $entryNames) {
        throw 'Release archive contains unexpected files.'
    }
}
finally {
    $archive.Dispose()
}

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$zipPath.sha256", "$zipHash  $([System.IO.Path]::GetFileName($zipPath))`n")

$sourceZipPath = Join-Path $OutputDirectory "ProbeRoulette-$($packageManifest.version)-source.zip"
$sourceStream  = [System.IO.File]::Open($sourceZipPath, [System.IO.FileMode]::Create)
$sourceArchive = [System.IO.Compression.ZipArchive]::new($sourceStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|artifacts|\.git|\.vs)[\\/]' -and
        ($_.Extension -in @('.cs', '.csproj', '.sln', '.ps1') -or $_.Name -in @('.gitignore', 'manifest.json', 'default-config.json'))
    }
    foreach ($file in $sourceFiles) {
        $entryName = $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive, $file.FullName, $entryName) | Out-Null
    }

    foreach ($document in @('README.md', 'CHANGELOG.md', 'LICENSE')) {
        $documentPath = Join-Path $ReleaseDocsPath $document
        if (Test-Path -LiteralPath $documentPath) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive, $documentPath, $document) | Out-Null
        }
    }
}
finally {
    $sourceArchive.Dispose()
    $sourceStream.Dispose()
}

$sourceHash = (Get-FileHash -LiteralPath $sourceZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$sourceZipPath.sha256", "$sourceHash  $([System.IO.Path]::GetFileName($sourceZipPath))`n")

if ($PackageOnly) {
    Write-Host "Verified package: $zipPath"
    Write-Host "Source package: $sourceZipPath"

    return
}

$runningGame = Get-Process -Name OuterWilds -ErrorAction SilentlyContinue
if ($runningGame) {
    throw "The release package is ready at $zipPath. Close Outer Wilds before installing it."
}

if ($migrateLegacy) {
    # Rename the whole folder so settings and any mod-local data come along, with no duplicate mod.
    $sourcePath      = (Resolve-Path -LiteralPath $legacyInstallPath).ProviderPath
    $destinationPath = [System.IO.Path]::GetFullPath($installPath)
    $allowedPrefix   = $modsPath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $sourceItem      = Get-Item -LiteralPath $sourcePath
    if (!$sourcePath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        !$destinationPath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        ($sourceItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw 'The migration paths must be ordinary folders inside the selected OWML Mods directory.'
    }

    Move-Item -LiteralPath $sourcePath -Destination $destinationPath
    Write-Host "Migrated existing mod folder to $modId."
}

New-Item -ItemType Directory -Path $installPath -Force | Out-Null
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $packagePath $file) -Destination (Join-Path $installPath $file) -Force
}

$configPath = Join-Path $installPath 'config.json'
if (!(Test-Path -LiteralPath $configPath)) {
    Copy-Item -LiteralPath (Join-Path $installPath 'default-config.json') -Destination $configPath
}
else {
    $currentConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $defaults      = Get-Content -LiteralPath (Join-Path $installPath 'default-config.json') -Raw | ConvertFrom-Json
    $changed       = $false
    foreach ($setting in $defaults.settings.PSObject.Properties) {
        if ($null -eq $currentConfig.settings.PSObject.Properties[$setting.Name]) {
            $currentConfig.settings | Add-Member -MemberType NoteProperty -Name $setting.Name -Value $setting.Value
            $changed = $true
        }
    }

    if ($changed) {
        Copy-Item -LiteralPath $configPath -Destination (Join-Path $installPath 'config.previous.json') -Force
        $configJson = $currentConfig | ConvertTo-Json -Depth 20
        [System.IO.File]::WriteAllText($configPath, $configJson, [System.Text.UTF8Encoding]::new($false))
    }
}

foreach ($file in $files) {
    $expected = (Get-FileHash -LiteralPath (Join-Path $packagePath $file) -Algorithm SHA256).Hash
    $actual   = (Get-FileHash -LiteralPath (Join-Path $installPath $file) -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        throw "Installed file verification failed: $file"
    }
}

Write-Host "Installed and verified: $installPath"
Write-Host "Package: $zipPath"
