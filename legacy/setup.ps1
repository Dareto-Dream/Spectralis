param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "publish-normal",
    [string]$SpotifyClientId = $env:SPECTRALIS_SPOTIFY_CLIENT_ID,
    [switch]$SkipDotnetInstall,
    [switch]$SkipWebView2Install
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$legacyRoot = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$startupProjectPath = Join-Path $legacyRoot "Startup\Startup.csproj"
$libraryProjectPath = Join-Path $legacyRoot "Spectralis.csproj"
$globalJsonPath = Join-Path $repoRoot "global.json"
$iconScriptPath = Join-Path $repoRoot "build\Generate-Icon.ps1"
$iconSourcePath = Join-Path $repoRoot "Assets\icon.png"
$temporaryProjectDir = Join-Path $legacyRoot "obj\setup-normal\Startup"
$temporaryProgramPath = Join-Path $temporaryProjectDir "Program.cs"
$temporaryProjectPath = Join-Path $temporaryProjectDir "Startup.NoSquirrel.csproj"
$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
}
else {
    Join-Path $repoRoot $OutputDirectory
}

$script:DotnetExe = $null

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-LastExitCode([string]$CommandName) {
    if ($LASTEXITCODE -ne 0) {
        throw "$CommandName failed with exit code $LASTEXITCODE."
    }
}

function Get-SdkVersionFromGlobalJson {
    if (-not (Test-Path -LiteralPath $globalJsonPath)) {
        return $null
    }

    $globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
    return [string]$globalJson.sdk.version
}

function Get-DotnetExecutablePath {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $defaultPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $defaultPath) {
        return $defaultPath
    }

    return $null
}

function Test-CompatibleDotnetSdk([string]$DotnetPath) {
    if (-not $DotnetPath) {
        return $false
    }

    Push-Location $repoRoot
    try {
        & $DotnetPath --version *> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        Pop-Location
    }
}

function Install-WingetPackage([string]$PackageId) {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "winget is required to install missing prerequisites automatically."
    }

    & $winget.Source install --id $PackageId --exact --accept-package-agreements --accept-source-agreements --silent
    Assert-LastExitCode "winget install $PackageId"
}

function Ensure-DotnetSdk {
    $sdkVersion = Get-SdkVersionFromGlobalJson
    $script:DotnetExe = Get-DotnetExecutablePath

    if (Test-CompatibleDotnetSdk $script:DotnetExe) {
        $activeVersion = & $script:DotnetExe --version
        Write-Step ".NET SDK ready ($activeVersion)"
        return
    }

    if ($SkipDotnetInstall) {
        if ($sdkVersion) {
            throw "A compatible .NET SDK for global.json version $sdkVersion is required."
        }

        throw "A compatible .NET SDK is required."
    }

    Write-Step "Installing Microsoft .NET SDK 10.0"
    Install-WingetPackage "Microsoft.DotNet.SDK.10"

    $script:DotnetExe = Get-DotnetExecutablePath
    if (-not (Test-CompatibleDotnetSdk $script:DotnetExe)) {
        if ($sdkVersion) {
            throw "Installed .NET SDK is still not compatible with global.json version $sdkVersion."
        }

        throw "Installed .NET SDK could not be activated in this session."
    }

    $activeVersion = & $script:DotnetExe --version
    Write-Step ".NET SDK ready ($activeVersion)"
}

function Test-WebView2RuntimeInstalled {
    $clientId = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
    $registryPaths = @(
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\$clientId",
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$clientId",
        "Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\EdgeUpdate\Clients\$clientId"
    )

    foreach ($registryPath in $registryPaths) {
        if (-not (Test-Path -LiteralPath $registryPath)) {
            continue
        }

        $version = (Get-ItemProperty -LiteralPath $registryPath -ErrorAction SilentlyContinue).pv
        if (-not [string]::IsNullOrWhiteSpace($version)) {
            return $true
        }
    }

    return $false
}

function Ensure-WebView2Runtime {
    if (Test-WebView2RuntimeInstalled) {
        Write-Step "WebView2 runtime ready"
        return
    }

    if ($SkipWebView2Install) {
        Write-Warning "WebView2 runtime is not installed. The build will succeed, but the published app needs WebView2 to run."
        return
    }

    Write-Step "Installing Microsoft Edge WebView2 Runtime"
    Install-WingetPackage "Microsoft.EdgeWebView2Runtime"

    if (-not (Test-WebView2RuntimeInstalled)) {
        throw "WebView2 runtime installation completed, but the runtime still was not detected."
    }

    Write-Step "WebView2 runtime ready"
}

function New-TemporaryStartupProject {
    $startupProjectXml = [xml](Get-Content -LiteralPath $startupProjectPath)
    $version = [string]($startupProjectXml.Project.PropertyGroup.Version | Select-Object -First 1)
    if (-not $version) {
        throw "Startup.csproj does not define a Version."
    }

    Remove-Item -LiteralPath $temporaryProjectDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $temporaryProjectDir -Force | Out-Null

    $programSource = @'
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Spectralis;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var startupPath = args
            .Select(TryGetExistingFilePath)
            .FirstOrDefault(static path => path is not null);

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1(startupPath));
    }

    private static string? TryGetExistingFilePath(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return null;

        try
        {
            var candidatePath = Path.GetFullPath(argument.Trim('"'));
            return File.Exists(candidatePath) ? candidatePath : null;
        }
        catch
        {
            return null;
        }
    }
}
'@

    $projectSource = @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>Spectralis</AssemblyName>
    <Product>Spectralis</Product>
    <Version>__VERSION__</Version>
    <DiscordApplicationId Condition="'$(DiscordApplicationId)' == ''">$([System.Environment]::GetEnvironmentVariable('SPECTRALIS_DISCORD_CLIENT_ID'))</DiscordApplicationId>
    <SpotifyClientId Condition="'$(SpotifyClientId)' == ''">$([System.Environment]::GetEnvironmentVariable('SPECTRALIS_SPOTIFY_CLIENT_ID'))</SpotifyClientId>
    <GeneratedApplicationIcon>$(BaseIntermediateOutputPath)generated\icon.ico</GeneratedApplicationIcon>
    <ApplicationIcon>$(GeneratedApplicationIcon)</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.cs" />
    <AssemblyMetadata Include="DiscordApplicationId" Value="$(DiscordApplicationId)" Condition="'$(DiscordApplicationId)' != ''" />
    <AssemblyMetadata Include="SpotifyClientId" Value="$(SpotifyClientId)" Condition="'$(SpotifyClientId)' != ''" />
    <ProjectReference Include="..\..\..\Spectralis.csproj" />
  </ItemGroup>

  <Target Name="GenerateApplicationIcon" BeforeTargets="PrepareResources" Inputs="..\..\..\..\Assets\icon.png" Outputs="$(GeneratedApplicationIcon)">
    <Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;$(MSBuildProjectDirectory)\..\..\..\..\build\Generate-Icon.ps1&quot; -Source &quot;$(MSBuildProjectDirectory)\..\..\..\..\Assets\icon.png&quot; -Destination &quot;$(MSBuildProjectDirectory)\$(GeneratedApplicationIcon)&quot;" />
  </Target>

</Project>
'@
    $projectSource = $projectSource.Replace('__VERSION__', $version)

    Set-Content -LiteralPath $temporaryProgramPath -Value $programSource -Encoding UTF8
    Set-Content -LiteralPath $temporaryProjectPath -Value $projectSource -Encoding UTF8
}

function Publish-NormalBuild {
    if (-not (Test-Path -LiteralPath $libraryProjectPath)) {
        throw "Project file not found: $libraryProjectPath"
    }

    if (-not (Test-Path -LiteralPath $iconScriptPath)) {
        throw "Icon generation script not found: $iconScriptPath"
    }

    if (-not (Test-Path -LiteralPath $iconSourcePath)) {
        throw "Icon source not found: $iconSourcePath"
    }

    Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

    Write-Step "Publishing plain executable to $resolvedOutputDirectory"
    Push-Location $repoRoot
    try {
        $publishArgs = @(
            "publish",
            $temporaryProjectPath,
            "-c", $Configuration,
            "-r", $RuntimeIdentifier,
            "--self-contained", "true",
            "-o", $resolvedOutputDirectory
        )
        if (-not [string]::IsNullOrWhiteSpace($SpotifyClientId)) {
            $publishArgs += "/p:SpotifyClientId=$SpotifyClientId"
        }

        & $script:DotnetExe @publishArgs
        Assert-LastExitCode "dotnet publish"
    }
    finally {
        Pop-Location
    }

    $publishedExePath = Join-Path $resolvedOutputDirectory "Spectralis.exe"
    if (-not (Test-Path -LiteralPath $publishedExePath)) {
        throw "Expected publish output was not found: $publishedExePath"
    }

    Write-Step "Build complete: $publishedExePath"
}

if ($env:OS -ne "Windows_NT") {
    throw "setup.ps1 only supports Windows."
}

Ensure-DotnetSdk
Ensure-WebView2Runtime
New-TemporaryStartupProject
Publish-NormalBuild
