param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'), [switch]$English)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x C# compiler is required.' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$languageArgs = @('/resource:' + (Join-Path $projectRoot 'assets\translations.en.json') + ',translations.en.json')
$exeName = 'GPTUsageWidget.exe'
if ($English) { $languageArgs += '/define:ENGLISH'; $exeName = 'GPTUsageWidget.en.exe' }
& (Join-Path $projectRoot 'tools\CheckTranslations.ps1')
& $compilerPath /nologo /target:exe /out:"$outputRoot\CreateIcon.exe" /reference:System.Drawing.dll (Join-Path $projectRoot 'tools\CreateIcon.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon generator build failed.' }
& (Join-Path $outputRoot 'CreateIcon.exe') (Join-Path $projectRoot 'assets')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /main:CodexUsageTaskbar.Program "/win32icon:$projectRoot\assets\app.ico" "/win32manifest:$projectRoot\app.manifest" "/out:$outputRoot\$exeName" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $languageArgs $sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
& $compilerPath /nologo /target:exe /platform:x64 /optimize+ /warn:4 /main:CodexUsageTaskbar.Tests "/out:$outputRoot\CodexUsageTaskbar.Tests.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $languageArgs $sourcePaths (Join-Path $projectRoot 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& (Join-Path $outputRoot 'CodexUsageTaskbar.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
Write-Host "Built: $outputRoot\$exeName"
