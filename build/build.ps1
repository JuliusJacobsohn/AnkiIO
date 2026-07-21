param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$testResults = Join-Path $repository "artifacts/test-results"
$coverageOutput = Join-Path $repository "artifacts/coverage"
$packageOutput = Join-Path $repository "artifacts/packages"

function Assert-NativeSuccess([string]$operation) {
    if ($LASTEXITCODE -ne 0) { throw "$operation failed with exit code $LASTEXITCODE." }
}

function Reset-GeneratedDirectory([string]$path) {
    $resolvedParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $path))
    $artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repository "artifacts"))
    if (-not $resolvedParent.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset generated directory outside '$artifactRoot': '$path'."
    }

    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

Push-Location $repository
try {
    dotnet restore AnkiIO.sln --locked-mode
    Assert-NativeSuccess "Solution restore"
    dotnet restore samples/AnkiIO.Samples/AnkiIO.Samples.csproj --locked-mode
    Assert-NativeSuccess "Sample restore"
    dotnet restore samples/AnkiIO.GermanEnglishShowcase/AnkiIO.GermanEnglishShowcase.csproj --locked-mode
    Assert-NativeSuccess "German-English showcase restore"
    dotnet restore benchmarks/AnkiIO.Benchmarks/AnkiIO.Benchmarks.csproj --locked-mode
    Assert-NativeSuccess "Benchmark restore"
    ./build/verify-dependencies.ps1 @(
        "AnkiIO.sln",
        "samples/AnkiIO.Samples/AnkiIO.Samples.csproj",
        "samples/AnkiIO.GermanEnglishShowcase/AnkiIO.GermanEnglishShowcase.csproj",
        "benchmarks/AnkiIO.Benchmarks/AnkiIO.Benchmarks.csproj"
    )
    if (-not $?) { throw "Dependency vulnerability audit failed." }

    dotnet format AnkiIO.sln --verify-no-changes --no-restore
    Assert-NativeSuccess "Formatting verification"
    dotnet build AnkiIO.sln --configuration $Configuration --no-restore
    Assert-NativeSuccess "Solution build"
    dotnet build samples/AnkiIO.Samples/AnkiIO.Samples.csproj --configuration $Configuration --no-restore
    Assert-NativeSuccess "Sample build"
    dotnet build samples/AnkiIO.GermanEnglishShowcase/AnkiIO.GermanEnglishShowcase.csproj --configuration $Configuration --no-restore
    Assert-NativeSuccess "German-English showcase build"
    dotnet run --project samples/AnkiIO.GermanEnglishShowcase/AnkiIO.GermanEnglishShowcase.csproj --configuration $Configuration --no-build -- artifacts/samples/AnkiIO-German-English-Showcase.apkg
    Assert-NativeSuccess "German-English showcase generation and verification"
    dotnet build benchmarks/AnkiIO.Benchmarks/AnkiIO.Benchmarks.csproj --configuration $Configuration --no-restore
    Assert-NativeSuccess "Benchmark build"

    Reset-GeneratedDirectory $testResults
    Reset-GeneratedDirectory $coverageOutput
    dotnet test AnkiIO.sln --configuration $Configuration --no-build --no-restore --filter "Category!=LocalAnkiCompatibility" --collect:"XPlat Code Coverage" --settings coverlet.runsettings --results-directory $testResults
    Assert-NativeSuccess "Portable test run"
    dotnet tool restore
    Assert-NativeSuccess "Local tool restore"
    dotnet tool run reportgenerator "-reports:$testResults/**/coverage.cobertura.xml" "-targetdir:$coverageOutput" "-reporttypes:Cobertura;Html"
    Assert-NativeSuccess "Coverage report generation"
    ./build/verify-coverage.ps1 (Join-Path $coverageOutput "Cobertura.xml")
    if (-not $?) { throw "Coverage verification failed." }

    Reset-GeneratedDirectory $packageOutput
    dotnet pack src/AnkiIO/AnkiIO.csproj --configuration $Configuration --no-build --output $packageOutput
    Assert-NativeSuccess "NuGet package creation"
    $packageVersionOutput = & dotnet msbuild src/AnkiIO/AnkiIO.csproj -nologo -getProperty:PackageVersion
    Assert-NativeSuccess "Package version resolution"
    $packageVersion = ($packageVersionOutput | Select-Object -Last 1).Trim()
    if ([string]::IsNullOrWhiteSpace($packageVersion)) { throw "Package version resolution returned an empty value." }
    $packagePath = Join-Path $packageOutput "AnkiIO.$packageVersion.nupkg"
    ./build/validate-package.ps1 $packagePath $packageVersion
    if (-not $?) { throw "NuGet package validation failed." }
    ./build/test-package-consumer.ps1 $packageOutput $packageVersion
    if (-not $?) { throw "Clean package-consumer test failed." }

    ./build/build-docs.ps1 -NoRestore
    if (-not $?) { throw "Documentation build failed." }
}
finally {
    Pop-Location
}
