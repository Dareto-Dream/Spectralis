[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Version,
    [string]$Channel = "win-x64",
    [string]$CdnDrive = "Z:",
    [string]$CdnPath  = "spectralis",
    [string]$WebDavUrl = "https://cdn.deltavdevs.com/webdav/",
    [string]$WebDavUser = "Admin",
    [string]$WebDavPassword = $env:SPECTRALIS_WEBDAV_PASSWORD,
    [switch]$UseMappedDrive,
    [switch]$SkipSquirrel,
    [switch]$SkipVelopack,
    [switch]$RequireLinux,
    [switch]$SyncSquirrelHistory,
    [switch]$ForceUpload
)

# Uploads Squirrel, Velopack, and Linux release artifacts to the CDN WebDAV
# endpoint. Pass -UseMappedDrive to fall back to the old network drive copy.
# All feeds land in the same target folder - filenames are distinct so they coexist
# without conflict (RELEASES vs releases.win-x64.json vs *.AppImage, etc.).
#
# https://cdn.deltavdevs.com/spectralis is the CDN root that
# VelopackUpdateService and ReleaseFeedClient both point at.

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::Expect100Continue = $false
$securityProtocol = [Net.SecurityProtocolType]::Tls12
if ([Enum]::GetNames([Net.SecurityProtocolType]) -contains "Tls13") {
    $securityProtocol = $securityProtocol -bor [Net.SecurityProtocolType]::Tls13
}
[Net.ServicePointManager]::SecurityProtocol = $securityProtocol

$repoRoot   = $PSScriptRoot
$cdnTargetPath = if ([string]::IsNullOrWhiteSpace($CdnPath)) { $CdnDrive } else { Join-Path $CdnDrive $CdnPath }

function Join-WebDavUrl([string]$BaseUrl, [string]$RelativePath) {
    $base = $BaseUrl.TrimEnd('/') + '/'
    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return $base
    }

    $segments = $RelativePath -split '[\\/]' |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { [System.Uri]::EscapeDataString($_) }

    return $base + ($segments -join '/')
}

$cdnTargetUrl = Join-WebDavUrl $WebDavUrl $CdnPath
$cdnTarget = if ($UseMappedDrive) { $cdnTargetPath } else { $cdnTargetUrl }

function New-WebDavCredential {
    if ([string]::IsNullOrWhiteSpace($WebDavPassword)) {
        throw "Missing WebDAV password. Set SPECTRALIS_WEBDAV_PASSWORD or pass -WebDavPassword."
    }

    $securePassword = ConvertTo-SecureString $WebDavPassword -AsPlainText -Force
    return [pscredential]::new($WebDavUser, $securePassword)
}

function New-WebDavCredentialCache([string]$Uri, [pscredential]$Credential) {
    $cache = [System.Net.CredentialCache]::new()
    $cache.Add([Uri]$Uri, "Basic", $Credential.GetNetworkCredential())
    return $cache
}

function Set-WebDavBasicAuth([System.Net.HttpWebRequest]$Request, [pscredential]$Credential) {
    $networkCredential = $Credential.GetNetworkCredential()
    $tokenBytes = [Text.Encoding]::UTF8.GetBytes("$($networkCredential.UserName):$($networkCredential.Password)")
    $Request.Headers["Authorization"] = "Basic $([Convert]::ToBase64String($tokenBytes))"
}

function Assert-PathExists([string]$Path, [string]$Description, [string]$Hint) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing $Description`: $Path`n$Hint"
    }
}

function Ensure-Directory([string]$Path) {
    if (Test-Path -LiteralPath $Path) { return }

    $parent = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        throw "CDN parent path not found: $parent - mount the network drive first."
    }

    Write-Host "Creating $Path..."
    if ($PSCmdlet.ShouldProcess($Path, "Create directory")) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Invoke-WebDavRequest([string]$Method, [string]$Uri, [pscredential]$Credential) {
    $request = [System.Net.HttpWebRequest]::Create($Uri)
    $request.Method = $Method
    $request.AllowAutoRedirect = $false
    $request.Credentials = New-WebDavCredentialCache $Uri $Credential
    $request.PreAuthenticate = $true
    Set-WebDavBasicAuth $request $Credential
    $request.UserAgent = "SpectralisDeploy/1.0"

    try {
        $response = $request.GetResponse()
        return $response
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response) {
            return $_.Exception.Response
        }
        throw
    }
}

function Send-WebDavFile([System.IO.FileInfo]$Source, [string]$DestinationUri, [pscredential]$Credential) {
    if (-not $PSCmdlet.ShouldProcess($DestinationUri, "Upload $($Source.Name)")) {
        return
    }

    $networkCredential = $Credential.GetNetworkCredential()
    $tokenBytes = [Text.Encoding]::UTF8.GetBytes("$($networkCredential.UserName):$($networkCredential.Password)")
    $headers = @{
        Authorization = "Basic $([Convert]::ToBase64String($tokenBytes))"
    }

    $oldProgressPreference = $ProgressPreference
    $ProgressPreference = "SilentlyContinue"
    try {
        $response = Invoke-WebRequest `
            -Uri $DestinationUri `
            -Method Put `
            -InFile $Source.FullName `
            -Headers $headers `
            -ContentType "application/octet-stream" `
            -UseBasicParsing

        $statusCode = [int]$response.StatusCode
        if ($statusCode -notin @(200, 201, 204)) {
            throw "WebDAV upload failed ($statusCode): $DestinationUri"
        }
    }
    finally {
        $ProgressPreference = $oldProgressPreference
    }
}

function Get-WebDavContentLength([string]$DestinationUri, [pscredential]$Credential) {
    $response = Invoke-WebDavRequest "HEAD" $DestinationUri $Credential
    try {
        $statusCode = [int]$response.StatusCode
        if ($statusCode -lt 200 -or $statusCode -ge 300) {
            return -1
        }
        return $response.ContentLength
    }
    finally {
        $response.Close()
    }
}

function Test-WebDavFileLength([System.IO.FileInfo]$Source, [string]$DestinationUri, [pscredential]$Credential) {
    if ($WhatIfPreference) {
        return
    }

    $remoteLength = Get-WebDavContentLength $DestinationUri $Credential
    if ($remoteLength -ne $Source.Length) {
        throw "WebDAV verification failed for $($Source.Name): remote has $remoteLength bytes, expected $($Source.Length)."
    }
}

function Test-WebDavTarget([string]$Uri, [pscredential]$Credential) {
    $probeUri = $Uri.TrimEnd('/') + '/'
    $response = Invoke-WebDavRequest "OPTIONS" $probeUri $Credential
    try {
        $statusCode = [int]$response.StatusCode
        $allow = [string]$response.Headers["Allow"]
        $dav = [string]$response.Headers["DAV"]

        if ($statusCode -lt 200 -or $statusCode -ge 300) {
            throw "WebDAV preflight failed ($statusCode): $probeUri"
        }
        if ($allow -notmatch "(^|,\s*)PUT(\s*,|$)" -and $dav -eq "") {
            throw "WebDAV preflight did not advertise writes at $probeUri. Allow='$allow' DAV='$dav'"
        }
    }
    finally {
        $response.Close()
    }
}

function Assert-FileContains([string]$Path, [string]$Needle, [string]$Description, [string]$Hint) {
    if (-not (Select-String -LiteralPath $Path -SimpleMatch $Needle -Quiet)) {
        throw "$Description does not reference '$Needle': $Path`n$Hint"
    }
}

function Copy-Artifact([string]$src, [string]$dstDir, [string]$label) {
    $source = Get-Item -LiteralPath $src
    $destination = if ($UseMappedDrive) { Join-Path $dstDir $source.Name } else { Join-WebDavUrl $dstDir $source.Name }

    if ($UseMappedDrive) {
        if (-not $ForceUpload -and (Test-Path -LiteralPath $destination)) {
            $existing = Get-Item -LiteralPath $destination
            if ($existing.Length -eq $source.Length) {
                $sizeMb = [math]::Round($source.Length / 1MB, 1)
                if ($source.Length -lt 1MB) {
                    Write-Host "  ok $($source.Name) (already current)"
                } else {
                    Write-Host "  ok $($source.Name) ($sizeMb MB, already current)"
                }
                return
            }
        }

        if ($PSCmdlet.ShouldProcess($destination, "Copy $label")) {
            Copy-Item -LiteralPath $source.FullName -Destination $destination -Force

            $copied = Get-Item -LiteralPath $destination -ErrorAction SilentlyContinue
            if (-not $copied) {
                throw "Copy reported success but destination file is missing: $destination"
            }
            if ($copied.Length -ne $source.Length) {
                throw "Copy verification failed for $label`: $destination has $($copied.Length) bytes, expected $($source.Length)."
            }
        }
    }
    elseif (-not $UseMappedDrive) {
        if (-not $ForceUpload -and -not $WhatIfPreference) {
            $remoteLength = Get-WebDavContentLength $destination $webDavCredential
            if ($remoteLength -eq $source.Length) {
                $sizeMb = [math]::Round($source.Length / 1MB, 1)
                if ($source.Length -lt 1MB) {
                    Write-Host "  ok $($source.Name) (already current)"
                } else {
                    Write-Host "  ok $($source.Name) ($sizeMb MB, already current)"
                }
                return
            }
        }

        try {
            Send-WebDavFile $source $destination $webDavCredential
            Test-WebDavFileLength $source $destination $webDavCredential
        }
        catch {
            if (-not (Test-Path -LiteralPath $cdnTargetPath)) {
                throw
            }

            Write-Warning "WebDAV upload failed for $($source.Name): $($_.Exception.Message)"
            Write-Warning "Falling back to mapped target $cdnTargetPath for this file."

            $fallbackDestination = Join-Path $cdnTargetPath $source.Name
            if (-not $ForceUpload -and (Test-Path -LiteralPath $fallbackDestination)) {
                $existing = Get-Item -LiteralPath $fallbackDestination
                if ($existing.Length -eq $source.Length) {
                    $sizeMb = [math]::Round($source.Length / 1MB, 1)
                    if ($source.Length -lt 1MB) {
                        Write-Host "  ok $($source.Name) (already current via mapped fallback)"
                    } else {
                        Write-Host "  ok $($source.Name) ($sizeMb MB, already current via mapped fallback)"
                    }
                    return
                }
            }

            if ($PSCmdlet.ShouldProcess($fallbackDestination, "Copy $label via mapped fallback")) {
                Copy-Item -LiteralPath $source.FullName -Destination $fallbackDestination -Force
                $copied = Get-Item -LiteralPath $fallbackDestination -ErrorAction SilentlyContinue
                if (-not $copied) {
                    throw "Mapped fallback copy reported success but destination file is missing: $fallbackDestination"
                }
                if ($copied.Length -ne $source.Length) {
                    throw "Mapped fallback verification failed for $label`: $fallbackDestination has $($copied.Length) bytes, expected $($source.Length)."
                }
            }

            $sizeMb = [math]::Round($source.Length / 1MB, 1)
            if ($source.Length -lt 1MB) {
                Write-Host "  ok $($source.Name) (mapped fallback)"
            } else {
                Write-Host "  ok $($source.Name) ($sizeMb MB, mapped fallback)"
            }
            return
        }
    }

    $sizeMb = [math]::Round($source.Length / 1MB, 1)
    if ($source.Length -lt 1MB) {
        Write-Host "  ok $($source.Name)"
    } else {
        Write-Host "  ok $($source.Name) ($sizeMb MB)"
    }
}

$webDavCredential = if ($UseMappedDrive) { $null } else { New-WebDavCredential }

# Validate every requested artifact before touching the CDN target.

if (-not $SkipSquirrel) {
    $squirrelDir = Join-Path $repoRoot "releases"
    $squirrelReleases = Join-Path $squirrelDir "RELEASES"
    $squirrelSetup = Join-Path $squirrelDir "Setup.exe"
    $squirrelFull = Join-Path $squirrelDir "Spectralis-$Version-full.nupkg"
    $squirrelDelta = Join-Path $squirrelDir "Spectralis-$Version-delta.nupkg"

    Assert-PathExists $squirrelDir "Squirrel release directory" "Run .\build.ps1 -Version $Version without -SkipSquirrel."
    Assert-PathExists $squirrelReleases "Squirrel RELEASES manifest" "Run .\Spectralis.Installer\Windows\build-squirrel.ps1 -Version $Version."
    Assert-PathExists $squirrelSetup "Squirrel Setup.exe" "Run .\Spectralis.Installer\Windows\build-squirrel.ps1 -Version $Version."
    Assert-PathExists $squirrelFull "Squirrel full package for v$Version" "Run .\Spectralis.Installer\Windows\build-squirrel.ps1 -Version $Version."
    Assert-FileContains $squirrelReleases "Spectralis-$Version-full.nupkg" "Squirrel RELEASES manifest" "Rebuild Squirrel for v$Version before deploying."
}

if (-not $SkipVelopack) {
    $velopackDir = Join-Path $repoRoot "releases-velopack"
    $velopackFiles = @(
        "releases.$Channel.json",
        "assets.$Channel.json",
        "RELEASES-$Channel",
        "Spectralis-$Version-$Channel-full.nupkg",
        "Spectralis-$Channel-Setup.exe",
        "Spectralis-$Channel-Portable.zip"
    )

    Assert-PathExists $velopackDir "Velopack release directory" "Run .\build.ps1 -Version $Version."
    foreach ($file in $velopackFiles) {
        Assert-PathExists (Join-Path $velopackDir $file) "Velopack artifact '$file'" "Run .\Spectralis.Installer\Windows\build-velopack.ps1 -Version $Version -Channel $Channel."
    }
}

$appImage = Join-Path $repoRoot "releases\Spectralis-$Version-x86_64.AppImage"
$hasLinuxAppImage = Test-Path -LiteralPath $appImage
if ($RequireLinux -and -not $hasLinuxAppImage) {
    throw "Linux AppImage not found: $appImage`nRun .\build.ps1 -Version $Version -Linux before deploying with -RequireLinux."
}

if ($UseMappedDrive) {
    Ensure-Directory $cdnTarget
} else {
    Write-Host "WebDAV target -> $cdnTargetUrl"
    Test-WebDavTarget $cdnTargetUrl $webDavCredential
}

# Squirrel feed
# releases/ is the accumulative Squirrel feed - never wiped locally, so a
# filtered copy keeps all prior nupkgs on CDN while avoiding unrelated files
# that also live in releases/ (for example Linux AppImages).

if ($SkipSquirrel) {
    Write-Host ""
    Write-Host "[Squirrel] skipped"
} else {
    Write-Host ""
    Write-Host "[Squirrel] $squirrelDir"
    Write-Host "        -> $cdnTarget"

    Copy-Artifact $squirrelReleases $cdnTarget "Squirrel RELEASES manifest"
    Copy-Artifact $squirrelSetup $cdnTarget "Squirrel setup"
    Copy-Artifact $squirrelFull $cdnTarget "Squirrel full package"
    if (Test-Path -LiteralPath $squirrelDelta) {
        Copy-Artifact $squirrelDelta $cdnTarget "Squirrel delta package"
    } else {
        Write-Host "  note no delta package for this version"
    }

    if ($SyncSquirrelHistory) {
        Write-Host "  syncing Squirrel history..."
        Get-ChildItem -LiteralPath $squirrelDir -File -Filter "*.nupkg" |
            Where-Object { $_.Name -notin @("Spectralis-$Version-full.nupkg", "Spectralis-$Version-delta.nupkg") } |
            Sort-Object Name |
            ForEach-Object { Copy-Artifact $_.FullName $cdnTarget "Squirrel history package" }
    }
}

# Velopack feed
# releases-velopack/ contains the Velopack manifest and packages.

if ($SkipVelopack) {
    Write-Host ""
    Write-Host "[Velopack] skipped"
} else {
    Write-Host ""
    Write-Host "[Velopack] $velopackDir"
    Write-Host "        -> $cdnTarget"
    foreach ($file in $velopackFiles) {
        Copy-Artifact (Join-Path $velopackDir $file) $cdnTarget "Velopack $Channel artifact"
    }
}

# Linux AppImage

if ($hasLinuxAppImage) {
    Write-Host ""
    Write-Host "[Linux] $appImage"
    Write-Host "     -> $cdnTarget"
    Copy-Artifact $appImage $cdnTarget "Linux AppImage"
} else {
    Write-Warning "Linux AppImage not found - skipping (build with -Linux to produce it)"
}

# Summary

Write-Host ""
$summary = if ($WhatIfPreference) { "Deploy dry run complete" } else { "Deploy complete" }
Write-Host "$summary -> $cdnTarget  (v$Version)"
Write-Host ""
if (-not $SkipSquirrel) {
    Write-Host "  Squirrel feed  : cdn.deltavdevs.com/spectralis/RELEASES"
    Write-Host "  Squirrel setup : cdn.deltavdevs.com/spectralis/Setup.exe"
}
if (-not $SkipVelopack) {
    Write-Host "  Velopack feed  : cdn.deltavdevs.com/spectralis/releases.$Channel.json"
    Write-Host "  Velopack setup : cdn.deltavdevs.com/spectralis/Spectralis-$Channel-Setup.exe"
}
if ($hasLinuxAppImage) {
    Write-Host "  Linux AppImage : cdn.deltavdevs.com/spectralis/Spectralis-$Version-x86_64.AppImage"
} else {
    Write-Host "  Linux AppImage : skipped"
}
