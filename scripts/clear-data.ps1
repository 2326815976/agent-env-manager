param(
    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "AgentEnvManager"),
    [string]$DataRoot = $env:AGENT_ENV_MANAGER_DATA_ROOT,
    [switch]$ConfirmClearData
)

$ErrorActionPreference = "Stop"

if (-not $ConfirmClearData) {
    throw "清除管理器数据需要单独确认。请传入 -ConfirmClearData。"
}

function Assert-SafeDeletePath {
    param(
        [string]$Path,
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    $volumeRoot = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals(
            $fullPath.TrimEnd("\"),
            $volumeRoot.TrimEnd("\"),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label 不能是卷根: $fullPath"
    }

    if ($fullPath.TrimEnd("\").Length -lt 4) {
        throw "$Label 路径过短，拒绝删除: $fullPath"
    }

    return $fullPath
}

$safeStateRoot = Assert-SafeDeletePath `
    -Path $StateRoot `
    -Label "状态目录"
$safeDataRoot = Assert-SafeDeletePath `
    -Path $DataRoot `
    -Label "数据目录"

if ($null -ne $safeStateRoot -and
    (Test-Path -LiteralPath $safeStateRoot)) {
    Remove-Item -LiteralPath $safeStateRoot -Recurse -Force
}

if ($null -ne $safeDataRoot -and
    (Test-Path -LiteralPath $safeDataRoot)) {
    Remove-Item -LiteralPath $safeDataRoot -Recurse -Force
}

Write-Output "已清除管理器状态和受管数据。"
