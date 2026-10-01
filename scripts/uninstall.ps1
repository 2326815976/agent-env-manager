param(
    [string]$InstallRoot = (
        Join-Path $env:LOCALAPPDATA "Programs\AgentEnvManager"),
    [switch]$SkipShortcut
)

$ErrorActionPreference = "Stop"
$installRoot = [IO.Path]::GetFullPath($InstallRoot)
$appRoot = Join-Path $installRoot "app"
$shortcutPath = Join-Path `
    ([Environment]::GetFolderPath(
        [Environment+SpecialFolder]::Programs)) `
    "AgentEnvManager.lnk"

if (Test-Path -LiteralPath $appRoot) {
    Remove-Item -LiteralPath $appRoot -Recurse -Force
}

if (-not $SkipShortcut) {
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

Write-Output "已移除 AgentEnvManager 程序文件。"
Write-Output "状态、工具运行时、恢复点和 Agent 绑定默认保留。"
