param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"

function Assert-NativeSuccess([string]$operation) {
    if ($LASTEXITCODE -ne 0) { throw "$operation failed with exit code $LASTEXITCODE." }
}

dotnet restore AnkiIO.sln --locked-mode
Assert-NativeSuccess "Solution restore"
dotnet format AnkiIO.sln --verify-no-changes --no-restore
Assert-NativeSuccess "Formatting verification"
dotnet build AnkiIO.sln --configuration $Configuration --no-restore
Assert-NativeSuccess "Solution build"
dotnet build samples/AnkiIO.Samples/AnkiIO.Samples.csproj --configuration $Configuration
Assert-NativeSuccess "Sample build"
dotnet restore samples/AnkiIO.GermanEnglishShowcase/AnkiIO.GermanEnglishShowcase.csproj --locked-mode
Assert-NativeSuccess "German-English showcase restore"
dotnet build samples/AnkiIO.GermanEnglishShowcase/AnkiIO.GermanEnglishShowcase.csproj --configuration $Configuration --no-restore
Assert-NativeSuccess "German-English showcase build"
dotnet run --project samples/AnkiIO.GermanEnglishShowcase/AnkiIO.GermanEnglishShowcase.csproj --configuration $Configuration --no-build -- artifacts/samples/AnkiIO-German-English-Showcase.apkg
Assert-NativeSuccess "German-English showcase generation and verification"
dotnet build benchmarks/AnkiIO.Benchmarks/AnkiIO.Benchmarks.csproj --configuration $Configuration
Assert-NativeSuccess "Benchmark build"
dotnet test AnkiIO.sln --configuration $Configuration --no-build --no-restore --filter "Category!=LocalAnkiCompatibility"
Assert-NativeSuccess "Portable test run"
dotnet pack src/AnkiIO/AnkiIO.csproj --configuration $Configuration --no-build --output artifacts/packages
Assert-NativeSuccess "NuGet package creation"
$packageVersionOutput = & dotnet msbuild src/AnkiIO/AnkiIO.csproj -nologo -getProperty:PackageVersion
Assert-NativeSuccess "Package version resolution"
$packageVersion = ($packageVersionOutput | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($packageVersion)) { throw "Package version resolution returned an empty value." }
./build/validate-package.ps1 "artifacts/packages/AnkiIO.$packageVersion.nupkg"
if (-not $?) { throw "NuGet package validation failed." }
./build/test-package-consumer.ps1 artifacts/packages $packageVersion
if (-not $?) { throw "Clean package-consumer test failed." }
./build/build-docs.ps1
if (-not $?) { throw "Documentation build failed." }
