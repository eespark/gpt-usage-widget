$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$translationMap = [IO.File]::ReadAllText((Join-Path $projectRoot 'assets\translations.en.json')) | ConvertFrom-Json
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs') + @(Get-Item -LiteralPath (Join-Path $projectRoot 'tests\Tests.cs'))
foreach ($sourceFile in $sourceFiles) {
    $sourceText = [IO.File]::ReadAllText($sourceFile.FullName)
    $literalPattern = '(?s)//[^\r\n]*|/\*.*?\*/|@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"'
    foreach ($literal in [regex]::Matches($sourceText, $literalPattern)) {
        if ($literal.Value.StartsWith('"') -and $literal.Value -match '[\uAC00-\uD7A3]' -and
            ($literal.Index -lt 8 -or $sourceText.Substring($literal.Index - 8, 8) -ne 'Ui.Text(')) {
            throw ('Untranslated UI literal in ' + $sourceFile.Name + ': ' + $literal.Value)
        }
    }
    foreach ($match in [regex]::Matches($sourceText, 'Ui\.Text\(("(?:\\.|[^"\\])*")\)')) {
        $translationKey = $match.Groups[1].Value | ConvertFrom-Json
        if (-not $translationMap.PSObject.Properties[$translationKey]) { throw ('Missing English translation: ' + $translationKey) }
    }
}
Write-Host 'Translation coverage verified.'
