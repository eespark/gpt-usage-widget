$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$buildRoot = Join-Path $projectRoot 'artifacts\release-build'
& (Join-Path $projectRoot 'build.ps1') -OutputDirectory $buildRoot

$exePath = Join-Path $buildRoot 'GPTUsageWidget.exe'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application version.' }
$distRoot = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$stageRoot = Join-Path $distRoot ('stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageRoot | Out-Null
$zipName = 'GPTUsageWidget-v' + $version + '-win-x64.zip'
$zipPath = Join-Path $distRoot $zipName
try {
    # 지정한 파일만 포함하여 개인 설정·기록·진단 결과를 배포에서 제외합니다.
    Copy-Item -LiteralPath $exePath -Destination (Join-Path $stageRoot 'GPTUsageWidget.exe')
    foreach ($name in @('사용 가이드.md', '분석 기간과 예측.md', 'LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $stageRoot $name)
    }
    Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zipPath -Force
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $distRoot ($zipName + '.sha256')), $hash + '  ' + $zipName + [Environment]::NewLine, [Text.Encoding]::ASCII)
    Write-Host ('Package: ' + $zipPath)
    Write-Host ('SHA256: ' + $hash)
} finally {
    # 임시 경로가 이 프로젝트의 dist 내부인지 확인한 뒤에만 삭제합니다.
    $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
    $allowedPrefix = [IO.Path]::GetFullPath($distRoot).TrimEnd('\') + '\stage-'
    if (-not $resolvedStage.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging cleanup path.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}

# 같은 소스에서 영어판을 별도로 빌드하고 두 번째 배포 파일을 생성합니다.
$englishBuildRoot = Join-Path $projectRoot 'artifacts\release-build-en'
& (Join-Path $projectRoot 'build.ps1') -OutputDirectory $englishBuildRoot -English
$englishStage = Join-Path $distRoot ('stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $englishStage | Out-Null
$englishZipName = 'GPTUsageWidget-v' + $version + '-win-x64-en.zip'
$englishZipPath = Join-Path $distRoot $englishZipName
try {
    Copy-Item -LiteralPath (Join-Path $englishBuildRoot 'GPTUsageWidget.en.exe') -Destination $englishStage
    foreach ($name in @('README.md', 'LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $englishStage
    }
    Compress-Archive -Path (Join-Path $englishStage '*') -DestinationPath $englishZipPath -Force
    $englishHash = (Get-FileHash -LiteralPath $englishZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $distRoot ($englishZipName + '.sha256')), $englishHash + '  ' + $englishZipName + [Environment]::NewLine, [Text.Encoding]::ASCII)
    Write-Host ('Package: ' + $englishZipPath)
} finally {
    $resolvedEnglishStage = [IO.Path]::GetFullPath($englishStage)
    $allowedEnglishPrefix = [IO.Path]::GetFullPath($distRoot).TrimEnd('\') + '\stage-'
    if (-not $resolvedEnglishStage.StartsWith($allowedEnglishPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging cleanup path.' }
    Remove-Item -LiteralPath $resolvedEnglishStage -Recurse -Force
}

& (Join-Path $projectRoot 'tools\CheckRelease.ps1')
