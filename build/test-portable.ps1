param(
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "artifacts/test-results"
)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$resolvedResults = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    [System.IO.Path]::GetFullPath($ResultsDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repository $ResultsDirectory))
}
$projects = [ordered]@{
    unit = "tests/AnkiIO.UnitTests/AnkiIO.UnitTests.csproj"
    integration = "tests/AnkiIO.IntegrationTests/AnkiIO.IntegrationTests.csproj"
}

New-Item -ItemType Directory -Path $resolvedResults -Force | Out-Null
Push-Location $repository
try {
    foreach ($entry in $projects.GetEnumerator()) {
        $trxPath = Join-Path $resolvedResults "$($entry.Key).trx"
        if (Test-Path -LiteralPath $trxPath) {
            Remove-Item -LiteralPath $trxPath -Force
        }

        dotnet test $entry.Value `
            --configuration $Configuration `
            --no-build `
            --no-restore `
            --collect:"XPlat Code Coverage" `
            --settings coverlet.runsettings `
            --results-directory $resolvedResults `
            --logger "trx;LogFileName=$($entry.Key).trx"
        if ($LASTEXITCODE -ne 0) {
            throw "Portable $($entry.Key) tests failed with exit code $LASTEXITCODE."
        }

        ./build/verify-test-result.ps1 -TrxPath $trxPath -SuiteName "portable $($entry.Key)"
        if (-not $?) {
            throw "Portable $($entry.Key) test-result verification failed."
        }
    }
}
finally {
    Pop-Location
}
