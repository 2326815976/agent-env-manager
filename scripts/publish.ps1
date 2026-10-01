param(
    [string]$Configuration = "Release",
    [string]$Version = "dev",
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\artifacts")
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$output = [IO.Path]::GetFullPath($OutputRoot)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$portableRoot = Join-Path $output "portable"
$cliRoot = Join-Path $output "cli"
$setupRoot = Join-Path $output "setup"

if (-not $output.StartsWith(
        $artifactsRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "发布输出必须位于仓库 artifacts 目录下: $artifactsRoot"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path `
    $portableRoot, $cliRoot, $setupRoot | Out-Null

dotnet publish `
    (Join-Path $repoRoot "src\AgentEnvManager.Wpf\AgentEnvManager.Wpf.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $portableRoot

foreach ($required in @(
        "AgentEnvManager.Wpf.exe",
        "coreclr.dll",
        "hostfxr.dll",
        "hostpolicy.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $portableRoot $required))) {
        throw "自包含发布缺少文件: $required"
    }
}

dotnet publish `
    (Join-Path $repoRoot "src\AgentEnvManager.Cli\AgentEnvManager.Cli.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $cliRoot

foreach ($required in @(
        "AgentEnvManager.Cli.exe",
        "coreclr.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $cliRoot $required))) {
        throw "自包含 CLI 发布缺少文件: $required"
    }
}

$portableZip = Join-Path $output (
    "AgentEnvManager-$Version-win-x64-portable.zip")
Compress-Archive `
    -Path (Join-Path $portableRoot "*") `
    -DestinationPath $portableZip `
    -Force

Copy-Item `
    -Path $portableRoot `
    -Destination (Join-Path $setupRoot "app") `
    -Recurse
Copy-Item `
    -Path (Join-Path $PSScriptRoot "install.ps1") `
    -Destination $setupRoot
Copy-Item `
    -Path (Join-Path $PSScriptRoot "uninstall.ps1") `
    -Destination $setupRoot
Copy-Item `
    -Path (Join-Path $PSScriptRoot "clear-data.ps1") `
    -Destination $setupRoot

$setupZip = Join-Path $output (
    "AgentEnvManager-$Version-win-x64-setup.zip")
Compress-Archive `
    -Path (Join-Path $setupRoot "*") `
    -DestinationPath $setupZip `
    -Force

Write-Output "Portable: $portableZip"
Write-Output "Setup:    $setupZip"
Write-Output "CLI:      $cliRoot"
