param(
    [string]$PackageDirectory = "artifacts/packages",
    [string]$Version = "0.1.0-alpha.1"
)
$ErrorActionPreference = "Stop"
$resolvedPackages = (Resolve-Path -LiteralPath $PackageDirectory).Path
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("AnkiIO-consumer-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workspace | Out-Null
try {
    dotnet new console --framework net8.0 --output $workspace --force
    dotnet add $workspace package AnkiIO --version $Version --source $resolvedPackages
    $program = @'
using AnkiIO;
var deck = new AnkiDeck("Consumer");
deck.AddNote(AnkiNoteTypes.CreateBasic(), new Dictionary<string, string> { ["Front"] = "front", ["Back"] = "back" });
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
    dotnet run --project $workspace --configuration Release
}
finally {
    if ([System.IO.Directory]::Exists($workspace)) { [System.IO.Directory]::Delete($workspace, $true) }
}
