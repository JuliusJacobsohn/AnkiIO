param(
    [string]$PackageDirectory = "artifacts/packages",
    [string]$Version
)
$ErrorActionPreference = "Stop"
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionOutput = & dotnet msbuild (Join-Path $repository "src/AnkiIO/AnkiIO.csproj") -nologo -getProperty:PackageVersion
    if ($LASTEXITCODE -ne 0) { throw "Package version resolution failed." }
    $Version = ($versionOutput | Select-Object -Last 1).Trim()
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "Package version resolution returned an empty value." }
}

$resolvedPackages = (Resolve-Path -LiteralPath $PackageDirectory).Path
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("AnkiIO-consumer-" + [guid]::NewGuid().ToString("N"))
$consumerPackages = Join-Path $workspace ".nuget/packages"
New-Item -ItemType Directory -Path $workspace | Out-Null
try {
    dotnet new console --framework net8.0 --output $workspace --force --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Clean consumer project creation failed." }
    dotnet add $workspace package AnkiIO --version $Version --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Adding the AnkiIO $Version package reference failed." }
    $nugetConfig = Join-Path $workspace "NuGet.Config"
    $escapedPackages = [System.Security.SecurityElement]::Escape($resolvedPackages)
    $configText = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="AnkiIO local package" value="$escapedPackages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="AnkiIO local package">
      <package pattern="AnkiIO" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
    [System.IO.File]::WriteAllText($nugetConfig, $configText, [System.Text.UTF8Encoding]::new($false))
    $restoreArguments = @(
        "restore",
        $workspace,
        "--packages", $consumerPackages,
        "--configfile", $nugetConfig,
        "--no-cache")
    & dotnet @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw "Restoring AnkiIO $Version from the isolated local package source failed." }
    $program = @'
using AnkiIO;
var deck = new AnkiDeck("Consumer");
deck.AddBasicNote("front", "back");
var path = Path.Combine(Path.GetTempPath(), $"AnkiIO-consumer-{Guid.NewGuid():N}.apkg");
try
{
    await AnkiPackageWriter.WriteAsync(deck, path);
    var package = await AnkiPackageReader.ReadAsync(path);
    if (package.Notes.Count() != 1) throw new InvalidOperationException("Consumer round trip failed.");
    Console.WriteLine("PACKAGE_CONSUMER_OK");
}
finally
{
    if (File.Exists(path)) File.Delete(path);
}
'@
    [System.IO.File]::WriteAllText((Join-Path $workspace "Program.cs"), $program, [System.Text.UTF8Encoding]::new($false))
    dotnet run --project $workspace --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Clean package-consumer execution failed." }
}
finally {
    if ([System.IO.Directory]::Exists($workspace)) { [System.IO.Directory]::Delete($workspace, $true) }
}
