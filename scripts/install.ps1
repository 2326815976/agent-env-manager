param(
    [string]$Version = "dev",
    [string]$InstallRoot = (
        Join-Path $env:LOCALAPPDATA "Programs\AgentEnvManager"),
    [string]$SourcePath = (Join-Path $PSScriptRoot "app"),
    [switch]$SkipShortcut
)

$ErrorActionPreference = "Stop"
$source = [IO.Path]::GetFullPath($SourcePath)
$installRoot = [IO.Path]::GetFullPath($InstallRoot)
$appRoot = Join-Path $installRoot "app"
$stagingRoot = Join-Path $installRoot "app.staging"
$backupRoot = Join-Path $installRoot "app.previous"
$stateRoot = Join-Path $env:LOCALAPPDATA "AgentEnvManager"
$executable = Join-Path $appRoot "AgentEnvManager.Wpf.exe"

foreach ($required in @(
        "AgentEnvManager.Wpf.exe",
        "coreclr.dll",
        "hostfxr.dll",
        "hostpolicy.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $required))) {
        throw "安装源不是自包含发布，缺少文件: $required"
    }
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
Copy-Item `
    -Path (Join-Path $source "*") `
    -Destination $stagingRoot `
    -Recurse `
    -Force

foreach ($required in @(
        "AgentEnvManager.Wpf.exe",
        "coreclr.dll",
        "hostfxr.dll",
        "hostpolicy.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $stagingRoot $required))) {
        throw "staging 安装校验失败，缺少文件: $required"
    }
}

if (Test-Path -LiteralPath $backupRoot) {
    Remove-Item -LiteralPath $backupRoot -Recurse -Force
}

if (Test-Path -LiteralPath $appRoot) {
    Move-Item -LiteralPath $appRoot -Destination $backupRoot
}

try {
    Move-Item -LiteralPath $stagingRoot -Destination $appRoot
}
catch {
    if (Test-Path -LiteralPath $appRoot) {
        Remove-Item -LiteralPath $appRoot -Recurse -Force
    }

    if (Test-Path -LiteralPath $backupRoot) {
        Move-Item -LiteralPath $backupRoot -Destination $appRoot
    }

    throw
}

if (Test-Path -LiteralPath $backupRoot) {
    Remove-Item -LiteralPath $backupRoot -Recurse -Force
}

Copy-Item `
    -Path (Join-Path $PSScriptRoot "uninstall.ps1") `
    -Destination $installRoot `
    -Force
Copy-Item `
    -Path (Join-Path $PSScriptRoot "clear-data.ps1") `
    -Destination $installRoot `
    -Force

if (-not $SkipShortcut) {
    $shortcutPath = Join-Path `
        ([Environment]::GetFolderPath(
            [Environment+SpecialFolder]::Programs)) `
        "AgentEnvManager.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $appRoot
    $shortcut.Description = "AgentEnvManager $Version"
    $shortcut.Save()
}

Write-Output "AgentEnvManager $Version 已安装到: $appRoot"
Write-Output "状态目录保留: $stateRoot"
