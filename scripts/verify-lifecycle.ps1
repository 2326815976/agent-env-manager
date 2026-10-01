$ErrorActionPreference = "Stop"
$root = Join-Path $env:TEMP (
    "agent-env-manager-lifecycle-" + [Guid]::NewGuid().ToString("N"))
$source = Join-Path $root "source"
$installRoot = Join-Path $root "install"
$stateRoot = Join-Path $root "state"
$dataRoot = Join-Path $root "data"

try {
    New-Item -ItemType Directory -Force -Path `
        $source, $stateRoot, $dataRoot | Out-Null
    foreach ($file in @(
            "AgentEnvManager.Wpf.exe",
            "coreclr.dll",
            "hostfxr.dll",
            "hostpolicy.dll")) {
        Set-Content -LiteralPath (Join-Path $source $file) -Value $file
    }

    Set-Content -LiteralPath (Join-Path $stateRoot "sentinel.txt") `
        -Value "state"
    Set-Content -LiteralPath (Join-Path $dataRoot "sentinel.txt") `
        -Value "data"

    & (Join-Path $PSScriptRoot "install.ps1") `
        -Version "verify" `
        -InstallRoot $installRoot `
        -SourcePath $source `
        -SkipShortcut
    if (-not (Test-Path -LiteralPath (
                Join-Path $installRoot "app\AgentEnvManager.Wpf.exe"))) {
        throw "安装后缺少应用入口。"
    }

    & (Join-Path $PSScriptRoot "install.ps1") `
        -Version "verify-upgrade" `
        -InstallRoot $installRoot `
        -SourcePath $source `
        -SkipShortcut
    if (-not (Test-Path -LiteralPath (
                Join-Path $stateRoot "sentinel.txt"))) {
        throw "升级不应修改管理器状态。"
    }

    & (Join-Path $PSScriptRoot "uninstall.ps1") `
        -InstallRoot $installRoot `
        -SkipShortcut
    if (Test-Path -LiteralPath (Join-Path $installRoot "app")) {
        throw "默认卸载应移除程序目录。"
    }

    $statePreserved = Test-Path -LiteralPath (
        Join-Path $stateRoot "sentinel.txt")
    $dataPreserved = Test-Path -LiteralPath (
        Join-Path $dataRoot "sentinel.txt")
    if (-not $statePreserved -or -not $dataPreserved) {
        throw "默认卸载应保留状态和数据。"
    }

    try {
        & (Join-Path $PSScriptRoot "clear-data.ps1") `
            -StateRoot $stateRoot `
            -DataRoot $dataRoot
        throw "清除数据未确认时不应成功。"
    }
    catch {
        if ($_.Exception.Message -notmatch "单独确认") {
            throw
        }
    }

    & (Join-Path $PSScriptRoot "clear-data.ps1") `
        -StateRoot $stateRoot `
        -DataRoot $dataRoot `
        -ConfirmClearData
    $stateRemoved = -not (Test-Path -LiteralPath $stateRoot)
    $dataRemoved = -not (Test-Path -LiteralPath $dataRoot)
    if (-not $stateRemoved -or -not $dataRemoved) {
        throw "确认后应清除状态和数据。"
    }

    Write-Output "生命周期验收通过。"
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
