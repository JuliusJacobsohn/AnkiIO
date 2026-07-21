param([Parameter(Mandatory = $true)][string]$PackagePath)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$resolved = (Resolve-Path -LiteralPath $PackagePath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolved)
try {
    $names = $archive.Entries.FullName
    foreach ($required in @("lib/net8.0/AnkiIO.dll", "lib/net8.0/AnkiIO.xml", "README.md", "icon.png", "LICENSE")) {
        if ($required -notin $names) { throw "Package is missing $required" }
    }
    if (($archive.Entries | Where-Object FullName -eq "icon.png").Length -gt 1MB) { throw "Package icon exceeds 1 MB." }
    Write-Output "PACKAGE_CONTENT_OK"
}
finally {
    $archive.Dispose()
}
