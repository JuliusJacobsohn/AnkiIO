param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
dotnet restore AnkiIO.sln --locked-mode
dotnet format AnkiIO.sln --verify-no-changes --no-restore
dotnet build AnkiIO.sln --configuration $Configuration --no-restore
dotnet build samples/AnkiIO.Samples/AnkiIO.Samples.csproj --configuration $Configuration
dotnet build benchmarks/AnkiIO.Benchmarks/AnkiIO.Benchmarks.csproj --configuration $Configuration
dotnet test AnkiIO.sln --configuration $Configuration --no-build --no-restore --filter "Category!=LocalAnkiCompatibility"
dotnet pack src/AnkiIO/AnkiIO.csproj --configuration $Configuration --no-build --output artifacts/packages
./build/validate-package.ps1 "artifacts/packages/AnkiIO.0.1.0-alpha.1.nupkg"
./build/test-package-consumer.ps1 artifacts/packages 0.1.0-alpha.1
