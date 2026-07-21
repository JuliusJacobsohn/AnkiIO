param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-SafeArchiveEntries([System.IO.Compression.ZipArchive]$archive, [string]$archiveName) {
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace([char]92, [char]47)
        if (-not $seen.Add($name)) { throw "$archiveName contains a duplicate entry '$name'." }
        if ([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith('/') -or $name -match '^[A-Za-z]:') {
            throw "$archiveName contains an unsafe entry name '$name'."
        }

        if (($name -split '/') -contains '..') { throw "$archiveName contains a parent-traversal entry '$name'." }
    }
}

function Read-ArchiveText([System.IO.Compression.ZipArchiveEntry]$entry) {
    $stream = $entry.Open()
    $reader = [System.IO.StreamReader]::new($stream, [System.Text.UTF8Encoding]::new($false), $true)
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

function Read-Nuspec([System.IO.Compression.ZipArchive]$archive, [string]$archiveName) {
    $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' })
    if ($nuspecEntries.Count -ne 1) { throw "$archiveName must contain exactly one root .nuspec file." }

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $stringReader = [System.IO.StringReader]::new((Read-ArchiveText $nuspecEntries[0]))
    $xmlReader = [System.Xml.XmlReader]::Create($stringReader, $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($xmlReader)
        return $document
    }
    finally {
        $xmlReader.Dispose()
        $stringReader.Dispose()
    }
}

function Get-MetadataNode([System.Xml.XmlDocument]$document, [string]$name) {
    $namespace = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespace.AddNamespace("n", $document.DocumentElement.NamespaceURI)
    return $document.SelectSingleNode("/n:package/n:metadata/n:$name", $namespace)
}

$resolved = (Resolve-Path -LiteralPath $PackagePath).Path
if ([System.IO.Path]::GetExtension($resolved) -ne ".nupkg") { throw "Package path must name a .nupkg file." }

$archive = [System.IO.Compression.ZipFile]::OpenRead($resolved)
try {
    Assert-SafeArchiveEntries $archive "NuGet package"
    $entries = @{}
    foreach ($entry in $archive.Entries) { $entries[$entry.FullName] = $entry }

    foreach ($required in @("lib/net8.0/AnkiIO.dll", "lib/net8.0/AnkiIO.xml", "README.md", "icon.png", "LICENSE", "THIRD_PARTY_NOTICES.md")) {
        if (-not $entries.ContainsKey($required)) { throw "Package is missing '$required'." }
        if ($entries[$required].Length -eq 0) { throw "Package entry '$required' is empty." }
    }

    if ($entries["icon.png"].Length -gt 1MB) { throw "Package icon exceeds 1 MB." }
    $xmlDocumentation = Read-ArchiveText $entries["lib/net8.0/AnkiIO.xml"]
    if ($xmlDocumentation -notmatch '<members>') { throw "Package XML documentation does not contain API members." }
    $packageReadme = Read-ArchiveText $entries["README.md"]
    if ($packageReadme -notmatch 'https://juliusjacobsohn\.github\.io/AnkiIO/') { throw "Package README does not link to the API documentation." }

    $nuspec = Read-Nuspec $archive "NuGet package"
    $metadata = @{
        Id = (Get-MetadataNode $nuspec "id").InnerText
        Version = (Get-MetadataNode $nuspec "version").InnerText
        Authors = (Get-MetadataNode $nuspec "authors").InnerText
        Description = (Get-MetadataNode $nuspec "description").InnerText
        ReleaseNotes = (Get-MetadataNode $nuspec "releaseNotes").InnerText
        Tags = (Get-MetadataNode $nuspec "tags").InnerText
        Readme = (Get-MetadataNode $nuspec "readme").InnerText
        Icon = (Get-MetadataNode $nuspec "icon").InnerText
        ProjectUrl = (Get-MetadataNode $nuspec "projectUrl").InnerText
    }
    foreach ($item in $metadata.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace($item.Value)) { throw "Package metadata '$($item.Key)' is missing." }
    }

    if ($metadata.Id -ne "AnkiIO") { throw "Package ID '$($metadata.Id)' is not 'AnkiIO'." }
    if ($metadata.Authors -notmatch 'Julius Jacobsohn') { throw "Package authors do not credit the maintainer." }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $metadata.Version -ne $ExpectedVersion) {
        throw "Package version '$($metadata.Version)' does not match expected version '$ExpectedVersion'."
    }
    if ($metadata.Readme -ne "README.md" -or $metadata.Icon -ne "icon.png") { throw "Package readme or icon metadata is inconsistent with packed files." }
    if ($metadata.ProjectUrl -ne "https://juliusjacobsohn.github.io/AnkiIO/") { throw "Package project URL must point to the API documentation." }

    $license = Get-MetadataNode $nuspec "license"
    if ($null -eq $license -or $license.GetAttribute("type") -ne "expression" -or $license.InnerText -ne "MIT") {
        throw "Package must declare the MIT license expression."
    }
    $repository = Get-MetadataNode $nuspec "repository"
    if ($null -eq $repository -or $repository.GetAttribute("type") -ne "git" -or $repository.GetAttribute("url") -ne "https://github.com/JuliusJacobsohn/AnkiIO") {
        throw "Package repository metadata is missing or incorrect."
    }

    $namespace = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
    $namespace.AddNamespace("n", $nuspec.DocumentElement.NamespaceURI)
    $dependencyNodes = $nuspec.SelectNodes("/n:package/n:metadata/n:dependencies/n:group[@targetFramework='net8.0']/n:dependency", $namespace)
    $dependencies = @{}
    foreach ($dependency in $dependencyNodes) { $dependencies[$dependency.GetAttribute("id")] = $dependency.GetAttribute("version") }
    if ($dependencies["Microsoft.Data.Sqlite"] -ne "8.0.29") { throw "Package must depend on Microsoft.Data.Sqlite 8.0.29." }
    if ($dependencies["SQLitePCLRaw.bundle_e_sqlite3"] -ne "3.0.4") { throw "Package must explicitly select the maintained SQLitePCLRaw 3.0.4 bundle." }
}
finally {
    $archive.Dispose()
}

$symbolPath = [System.IO.Path]::ChangeExtension($resolved, ".snupkg")
if (-not (Test-Path -LiteralPath $symbolPath)) { throw "Symbol package '$symbolPath' is missing." }
$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($symbolPath)
try {
    Assert-SafeArchiveEntries $symbolArchive "Symbol package"
    $pdb = $symbolArchive.GetEntry("lib/net8.0/AnkiIO.pdb")
    if ($null -eq $pdb -or $pdb.Length -eq 0) { throw "Symbol package is missing 'lib/net8.0/AnkiIO.pdb'." }
    $symbolNuspec = Read-Nuspec $symbolArchive "Symbol package"
    $symbolVersion = (Get-MetadataNode $symbolNuspec "version").InnerText
    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $symbolVersion -ne $ExpectedVersion) {
        throw "Symbol package version '$symbolVersion' does not match expected version '$ExpectedVersion'."
    }
}
finally {
    $symbolArchive.Dispose()
}

Write-Output "PACKAGE_CONTENT_OK AnkiIO $($metadata.Version)"
