param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectPath = [IO.Path]::GetFullPath($ProjectRoot)
$koreanBuild = Join-Path $projectPath 'artifacts\release-build\GPTUsageWidget.exe'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($koreanBuild).ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }

function Get-EntryHash($Entry) {
    $stream = $Entry.Open()
    $digest = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($digest.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $digest.Dispose(); $stream.Dispose() }
}

foreach ($language in @('ko', 'en')) {
    $suffix = if ($language -eq 'en') { '-en' } else { '' }
    $archiveName = 'GPTUsageWidget-v' + $version + '-win-x64' + $suffix + '.zip'
    $archivePath = Join-Path $projectPath ('dist\' + $archiveName)
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $declared = [IO.File]::ReadAllText($archivePath + '.sha256').Trim()
    if ($declared -cne ($actualHash + '  ' + $archiveName)) { throw ('Checksum mismatch: ' + $archiveName) }

    $sources = @{}
    if ($language -eq 'ko') {
        $sources['GPTUsageWidget.exe'] = $koreanBuild
        foreach ($name in @('사용 가이드.md', '분석 기간과 예측.md')) { $sources[$name] = Join-Path $projectPath $name }
    } else {
        $sources['GPTUsageWidget.en.exe'] = Join-Path $projectPath 'artifacts\release-build-en\GPTUsageWidget.en.exe'
        $sources['README.md'] = Join-Path $projectPath 'README.md'
        if ([Diagnostics.FileVersionInfo]::GetVersionInfo($sources['GPTUsageWidget.en.exe']).ProductVersion -ne $version) { throw 'Language versions differ.' }
    }
    $sources['LICENSE'] = Join-Path $projectPath 'LICENSE'
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        if ($archive.Entries.Count -ne $sources.Count) { throw ('Unexpected entry count: ' + $archiveName) }
        $seen = @{}
        foreach ($entry in $archive.Entries) {
            if (!$sources.ContainsKey($entry.FullName) -or $seen.ContainsKey($entry.FullName)) { throw ('Unexpected or duplicate entry: ' + $archiveName) }
            $seen[$entry.FullName] = $true
            $sourceHash = (Get-FileHash -LiteralPath $sources[$entry.FullName] -Algorithm SHA256).Hash.ToLowerInvariant()
            if ((Get-EntryHash $entry) -cne $sourceHash) { throw ('Stale or damaged package file: ' + $entry.FullName) }
        }
    } finally { $archive.Dispose() }
    Write-Host ('Verified package, checksum and current contents: ' + $archiveName)
}
