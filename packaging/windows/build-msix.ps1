# Spectralis Windows MSIX packaging script
param(
    [string]$Version = "5.0.0",
    [string]$OutDir = "dist/windows"
)

$publishDir = "publish/win-x64"
$appxManifest = "packaging/windows/AppxManifest.xml"

Write-Host "Building Spectralis $Version for Windows..."

dotnet publish Spectralis.App/Spectralis.App.csproj `
    -r win-x64 `
    -c Release `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { exit 1 }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Copy-Item $appxManifest "$publishDir/AppxManifest.xml"
Copy-Item "packaging/windows/Assets" "$publishDir/Assets" -Recurse -Force

$msixPath = "$OutDir/Spectralis-$Version-x64.msix"
& "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe" `
    pack /d $publishDir /p $msixPath /overwrite

Write-Host "MSIX built: $msixPath"
