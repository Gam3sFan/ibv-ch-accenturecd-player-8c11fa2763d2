param(
    # Empty => auto-increment from the current MainForm.APP_VERSION.
    # Pass an explicit value (e.g. 1.2.0) to force a specific version.
    [string]$Version,

    # Base URL of the static server. Ends up in <zipUrl> inside the manifest.
    [string]$BaseUrl = "http://localhost:8080",

    # "patch" (1.0.0 -> 1.0.1) or "minor" (1.0.0 -> 1.1.0). Used only when -Version is empty.
    [ValidateSet("patch", "minor")]
    [string]$Increment = "patch",

    # Build output to package. Defaults to ..\Player\bin\Release.
    [string]$ReleaseDir,

    # Where to write the ZIP + manifest. Defaults to this script's folder.
    [string]$OutputDir,

    # Manifest path. Defaults to <OutputDir>\player-update.xml.
    [string]$ManifestPath,

    # Advanced: skip the rebuild and package the existing build as-is.
    # WARNING: the packaged Player.exe must already carry the matching APP_VERSION,
    # otherwise clients will update in a loop. Normally leave this off.
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$scriptDir = if (![string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
}
else {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}

$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
$mainFormPath = Join-Path $repoRoot "Player\MainForm.cs"
$assemblyInfoPath = Join-Path $repoRoot "Player\Properties\AssemblyInfo.cs"
$solutionPath = Join-Path $repoRoot "Player.sln"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Test-Utf8Bom {
    param([byte[]]$Bytes)
    return ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF)
}

# Edits a file in place, preserving its UTF-8 BOM and line endings, and records
# the original bytes so the change can be reverted if a later step fails.
function Edit-FileText {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Replacement,
        [bool]$Required
    )

    if (!(Test-Path -LiteralPath $Path)) {
        if ($Required) { throw "File not found: $Path" }
        return
    }

    $originalBytes = [System.IO.File]::ReadAllBytes($Path)
    $text = [System.IO.File]::ReadAllText($Path)
    $newText = [regex]::Replace($text, $Pattern, $Replacement)

    if ($newText -eq $text) {
        if ($Required) { throw "Pattern '$Pattern' not found in $Path" }
        return
    }

    # Back up each file only on its first edit, so a revert restores the true original
    # even when the same file is edited more than once.
    if (-not ($script:EditedFiles | Where-Object { $_.Path -eq $Path })) {
        $script:EditedFiles += [pscustomobject]@{ Path = $Path; Bytes = $originalBytes }
    }

    $enc = New-Object System.Text.UTF8Encoding((Test-Utf8Bom $originalBytes))
    [System.IO.File]::WriteAllText($Path, $newText, $enc)
}

function Restore-EditedFiles {
    foreach ($f in $script:EditedFiles) {
        [System.IO.File]::WriteAllBytes($f.Path, $f.Bytes)
    }
}

function Find-MSBuild {
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe" 2>$null | Select-Object -First 1
        if ($found -and (Test-Path $found)) { return $found }
    }

    throw "MSBuild not found. Install Visual Studio 2022 / Build Tools, or run this from a 'Developer Command Prompt for VS 2022'."
}

# ---------------------------------------------------------------------------
# 1. Read the current player version
# ---------------------------------------------------------------------------

if (!(Test-Path -LiteralPath $mainFormPath)) {
    throw "Could not find $mainFormPath"
}

$mainFormText = [System.IO.File]::ReadAllText($mainFormPath)
$match = [regex]::Match($mainFormText, 'APP_VERSION\s*=\s*"([^"]+)"')
if (-not $match.Success) {
    throw "Could not find APP_VERSION in $mainFormPath"
}
$currentVersion = $match.Groups[1].Value

# ---------------------------------------------------------------------------
# 2. Decide the new version (explicit, or auto-increment)
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($Version)) {
    $v = [Version]$currentVersion
    $minor = if ($v.Minor -lt 0) { 0 } else { $v.Minor }
    $patch = if ($v.Build -lt 0) { 0 } else { $v.Build }

    if ($Increment -eq "minor") {
        $Version = "{0}.{1}.0" -f $v.Major, ($minor + 1)
    }
    else {
        $Version = "{0}.{1}.{2}" -f $v.Major, $minor, ($patch + 1)
    }

    Write-Host "Auto-increment ($Increment): $currentVersion -> $Version"
}
else {
    Write-Host "Explicit version: $currentVersion -> $Version"
}

try {
    [void][Version]$Version
}
catch {
    throw "Version '$Version' is not a valid .NET version. Use values like 1.0.1 or 1.2.3."
}

# ---------------------------------------------------------------------------
# 3. Output / release / manifest paths
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($OutputDir)) { $OutputDir = $scriptDir }
if (!(Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }
$resolvedOutputDir = (Resolve-Path $OutputDir).Path

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $resolvedOutputDir "player-update.xml"
}

if ([string]::IsNullOrWhiteSpace($ReleaseDir)) {
    $ReleaseDir = Join-Path $repoRoot "Player\bin\Release"
}

$script:EditedFiles = @()

try {
    # -----------------------------------------------------------------------
    # 4. Stamp the new version into the source (single source of truth)
    # -----------------------------------------------------------------------
    Edit-FileText -Path $mainFormPath `
        -Pattern '(APP_VERSION\s*=\s*")[^"]*(")' `
        -Replacement ('${1}' + $Version + '${2}') `
        -Required $true

    # Keep the Windows file-version metadata aligned (best-effort, 4-part).
    Edit-FileText -Path $assemblyInfoPath `
        -Pattern '(AssemblyVersion\(")[^"]*("\))' `
        -Replacement ('${1}' + "$Version.0" + '${2}') `
        -Required $false
    Edit-FileText -Path $assemblyInfoPath `
        -Pattern '(AssemblyFileVersion\(")[^"]*("\))' `
        -Replacement ('${1}' + "$Version.0" + '${2}') `
        -Required $false

    Write-Host "Stamped APP_VERSION = $Version"

    # -----------------------------------------------------------------------
    # 5. Rebuild Release so the packaged Player.exe carries the new version
    # -----------------------------------------------------------------------
    if (-not $SkipBuild) {
        $nuget = Get-Command nuget -ErrorAction SilentlyContinue
        if ($nuget) {
            Write-Host "Restoring NuGet packages..."
            & $nuget.Source restore $solutionPath
            if ($LASTEXITCODE -ne 0) { throw "nuget restore failed (exit $LASTEXITCODE)." }
        }
        else {
            Write-Warning "nuget.exe not on PATH - skipping restore. If the build fails with missing packages, run 'nuget restore Player.sln' first."
        }

        $msbuild = Find-MSBuild
        Write-Host "Building Release with: $msbuild"
        & $msbuild $solutionPath /p:Configuration=Release /t:Rebuild /m /nologo /verbosity:minimal
        if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
    }
    else {
        Write-Warning "SkipBuild is set - packaging the existing build. Make sure its APP_VERSION already matches $Version."
    }

    # -----------------------------------------------------------------------
    # 6. Package the build output
    # -----------------------------------------------------------------------
    if (!(Test-Path $ReleaseDir)) {
        throw "Release folder not found: $ReleaseDir. The build did not produce output."
    }
    $resolvedReleaseDir = (Resolve-Path $ReleaseDir).Path
    $playerExe = Join-Path $resolvedReleaseDir "Player.exe"
    if (!(Test-Path $playerExe)) {
        throw "Player.exe was not found in '$resolvedReleaseDir'."
    }

    $zipName = "Player-$Version.zip"
    $zipPath = Join-Path $resolvedOutputDir $zipName
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $resolvedReleaseDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

    $sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    $zipUrl = ($BaseUrl.TrimEnd("/") + "/" + $zipName)

    # -----------------------------------------------------------------------
    # 7. Update the manifest XML
    # -----------------------------------------------------------------------
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
}
catch {
    Write-Warning "Failed - reverting source version changes."
    Restore-EditedFiles
    throw
}

Write-Host ""
Write-Host "Done."
Write-Host "  Version:  $Version"
Write-Host "  Zip:      $zipPath"
Write-Host "  Manifest: $ManifestPath"
Write-Host "  Zip URL:  $zipUrl"
Write-Host "  SHA256:   $sha256"
