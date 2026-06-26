param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "publish-normal",
    [switch]$SkipDotnetInstall
)

# Quick local build of Spectralis (the Avalonia app): a runnable self-contained
# executable without creating an installer package.
#
# The legacy WinForms equivalent lives at legacy\setup.ps1.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$appProjectPath = Join-Path $repoRoot "Spectralis.App\Spectralis.App.csproj"
$globalJsonPath = Join-Path $repoRoot "global.json"
$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
}
else {
    Join-Path $repoRoot $OutputDirectory
}

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-LastExitCode([string]$CommandName) {
    if ($LASTEXITCODE -ne 0) {
        throw "$CommandName failed with exit code $LASTEXITCODE."
    }
}

function Test-CompatibleDotnetSdk {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) {
        return $false
    }

    Push-Location $repoRoot
    try {
        & $command.Source --version *> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        Pop-Location
    }
}

function Ensure-DotnetSdk {
    if (Test-CompatibleDotnetSdk) {
        $activeVersion = dotnet --version
        Write-Step ".NET SDK ready ($activeVersion)"
        return
    }

    if ($SkipDotnetInstall) {
        $globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
        throw "A compatible .NET SDK for global.json version $($globalJson.sdk.version) is required."
    }

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "winget is required to install the .NET SDK automatically. Install it manually or rerun with a compatible SDK on PATH."
    }

    Write-Step "Installing Microsoft .NET SDK 10.0"
    & $winget.Source install --id "Microsoft.DotNet.SDK.10" --exact --accept-package-agreements --accept-source-agreements --silent
    Assert-LastExitCode "winget install Microsoft.DotNet.SDK.10"

    if (-not (Test-CompatibleDotnetSdk)) {
        throw "Installed .NET SDK could not be activated in this session."
    }

    Write-Step ".NET SDK ready ($(dotnet --version))"
}

Ensure-DotnetSdk

Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

Write-Step "Publishing Spectralis.App to $resolvedOutputDirectory"
dotnet publish $appProjectPath -c $Configuration -r $RuntimeIdentifier --self-contained true -o $resolvedOutputDirectory
Assert-LastExitCode "dotnet publish"

$publishedExePath = Join-Path $resolvedOutputDirectory "Spectralis.App.exe"
if (-not (Test-Path -LiteralPath $publishedExePath)) {
    throw "Expected publish output was not found: $publishedExePath"
}

Write-Step "Build complete: $publishedExePath"
