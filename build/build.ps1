param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
dotnet restore AnkiIO.sln --locked-mode
dotnet format AnkiIO.sln --verify-no-changes --no-restore
dotnet build AnkiIO.sln --configuration $Configuration --no-restore
dotnet test AnkiIO.sln --configuration $Configuration --no-build --no-restore --filter "Category!=LocalAnkiCompatibility"
dotnet pack src/AnkiIO/AnkiIO.csproj --configuration $Configuration --no-build --output artifacts/packages
