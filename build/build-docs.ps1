param([switch]$NoRestore)

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path

Push-Location $repository
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "Tool restore failed." }

    if (-not $NoRestore) {
        dotnet restore src/AnkiIO/AnkiIO.csproj --locked-mode
        if ($LASTEXITCODE -ne 0) { throw "Library restore failed." }
    }

    dotnet build src/AnkiIO/AnkiIO.csproj --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Library build for API documentation failed." }

    dotnet docfx docs/docfx.json --warningsAsErrors
    if ($LASTEXITCODE -ne 0) { throw "Conceptual documentation build failed." }

    Push-Location (Join-Path $repository "docs")
    try {
        if (-not $NoRestore) {
            dotnet restore AnkiIO.shfbproj --locked-mode
            if ($LASTEXITCODE -ne 0) { throw "API documentation restore failed." }
        }

        $buildOutput = @(& dotnet build AnkiIO.shfbproj --configuration Release --no-restore 2>&1)
        $buildExitCode = $LASTEXITCODE
        $buildOutput | ForEach-Object { Write-Output $_ }
        if ($buildExitCode -ne 0) { throw "API documentation build failed." }

        $documentationWarnings = $buildOutput | Select-String -Pattern 'SHFB\s*:\s*warning' -CaseSensitive:$false
        $buildLog = Join-Path $repository "artifacts/docs-api-build.log"
        if (Test-Path -LiteralPath $buildLog) {
            $documentationWarnings += Select-String -LiteralPath $buildLog -Pattern 'SHFB\s*:\s*warning' -CaseSensitive:$false
        }

        if ($documentationWarnings) {
            throw "API documentation produced Sandcastle warnings. Missing XML sections and unresolved references are release-blocking."
        }

        $apiRoot = Join-Path $repository "artifacts/docs/api"
        $apiIndex = Join-Path $apiRoot "index.html"
        $apiTopics = Join-Path $apiRoot "html"
        $searchIndex = Join-Path $apiRoot "fti"
        if (-not (Test-Path -LiteralPath $apiIndex) -or
            -not (Select-String -LiteralPath $apiIndex -Pattern 'R_Project_AnkiIO\.htm' -Quiet) -or
            -not (Test-Path -LiteralPath $apiTopics) -or
            -not (Get-ChildItem -LiteralPath $apiTopics -Filter '*.htm' -File | Select-Object -First 1) -or
            -not (Test-Path -LiteralPath $searchIndex) -or
            -not (Get-ChildItem -LiteralPath $searchIndex -File | Select-Object -First 1)) {
            throw "API documentation output is incomplete or missing its searchable Sandcastle site."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}
