param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$BaseUrl = "http://localhost:8080",

    [string]$ReleaseDir,

    [string]$OutputDir,

    [string]$ManifestPath
)

$ErrorActionPreference = "Stop"

$scriptDir = if (![string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
}
else {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}

if ([string]::IsNullOrWhiteSpace($ReleaseDir)) {
    $ReleaseDir = Join-Path $scriptDir "..\Player\bin\Release"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = $scriptDir
}

if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$resolvedOutputDir = (Resolve-Path $OutputDir).Path

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $resolvedOutputDir "player-update.xml"
}

try {
    [void][Version]$Version
}
catch {
    throw "Version '$Version' is not a valid .NET version. Use values like 1.0.1 or 1.2.3.4."
}

$resolvedReleaseDir = (Resolve-Path $ReleaseDir).Path
$playerExe = Join-Path $resolvedReleaseDir "Player.exe"
if (!(Test-Path $playerExe)) {
    throw "Player.exe was not found in '$resolvedReleaseDir'. Build Release before packaging."
}

$zipName = "Player-$Version.zip"
$zipPath = Join-Path $resolvedOutputDir $zipName
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

try {
    Compress-Archive -Path (Join-Path $resolvedReleaseDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
}
catch {
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    throw
}

$sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
$zipUrl = ($BaseUrl.TrimEnd("/") + "/" + $zipName)

$xml = New-Object System.Xml.XmlDocument
$xml.PreserveWhitespace = $false
if (Test-Path $ManifestPath) {
    $xml.Load((Resolve-Path $ManifestPath).Path)
}
else {
    [void]$xml.AppendChild($xml.CreateXmlDeclaration("1.0", "utf-8", $null))
    [void]$xml.AppendChild($xml.CreateElement("update"))
}

if ($xml.DocumentElement -eq $null -or $xml.DocumentElement.Name -ne "update") {
    $xml.RemoveAll()
    [void]$xml.AppendChild($xml.CreateXmlDeclaration("1.0", "utf-8", $null))
    [void]$xml.AppendChild($xml.CreateElement("update"))
}

function Set-UpdateElement {
    param(
        [System.Xml.XmlDocument]$Document,
        [string]$Name,
        [string]$Value
    )

    $root = $Document.DocumentElement
    $node = $root.SelectSingleNode($Name)
    if ($null -eq $node) {
        $node = $Document.CreateElement($Name)
        [void]$root.AppendChild($node)
    }

    $node.InnerText = $Value
}

Set-UpdateElement -Document $xml -Name "version" -Value $Version
Set-UpdateElement -Document $xml -Name "zipUrl" -Value $zipUrl
Set-UpdateElement -Document $xml -Name "sha256" -Value $sha256

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($ManifestPath, $settings)
try {
    $xml.Save($writer)
}
finally {
    $writer.Close()
}

Write-Host "Created: $zipPath"
Write-Host "Updated: $ManifestPath"
Write-Host "Version: $Version"
Write-Host "Zip URL: $zipUrl"
Write-Host "SHA256: $sha256"
