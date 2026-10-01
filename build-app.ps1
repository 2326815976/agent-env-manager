param(
    [string]$Configuration = "Release",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$appRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "app"))
$expectedAppRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "app"))
$cliRoot = Join-Path $appRoot "cli"
$progressActivity = "构建 AgentEnvManager"
$totalSteps = 5
$stopwatch = [Diagnostics.Stopwatch]::StartNew()

if (-not [string]::Equals(
        $appRoot,
        $expectedAppRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean unexpected output path: $appRoot"
}

function Set-BuildProgress {
    param(
        [int]$Step,
        [string]$Status
    )

    $percent = [Math]::Min(
        100,
        [int](($Step - 1) / $totalSteps * 100))
    Write-Progress `
        -Activity $progressActivity `
        -Status $Status `
        -PercentComplete $percent
}

function Write-BuildStep {
    param(
        [int]$Step,
        [string]$Status
    )

    Write-Host "[$Step/$totalSteps] $Status" -ForegroundColor Cyan
    Set-BuildProgress $Step $Status
}

try {
    Write-BuildStep 1 "清理并准备输出目录..."
    if (Test-Path -LiteralPath $appRoot) {
        Remove-Item -LiteralPath $appRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $appRoot, $cliRoot |
        Out-Null

    Write-BuildStep 2 "发布 WPF..."
    dotnet publish `
        (Join-Path $repoRoot "src\AgentEnvManager.Wpf\AgentEnvManager.Wpf.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $appRoot
    if ($LASTEXITCODE -ne 0) {
        throw "WPF publish failed with exit code $LASTEXITCODE."
    }

    Write-BuildStep 3 "发布 CLI..."
    dotnet publish `
        (Join-Path $repoRoot "src\AgentEnvManager.Cli\AgentEnvManager.Cli.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $cliRoot
    if ($LASTEXITCODE -ne 0) {
        throw "CLI publish failed with exit code $LASTEXITCODE."
    }

    Write-BuildStep 4 "写入用户说明书..."
    $docsRoot = Join-Path $appRoot "docs"
    New-Item -ItemType Directory -Force -Path $docsRoot | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot "docs\user-guide.txt") `
        -Destination (Join-Path $docsRoot "使用说明.txt") `
        -Force

    Write-BuildStep 5 "校验发布产物..."
    $requiredFiles = @(
        (Join-Path $appRoot "AgentEnvManager.Wpf.exe"),
        (Join-Path $appRoot "coreclr.dll"),
        (Join-Path $appRoot "hostfxr.dll"),
        (Join-Path $appRoot "hostpolicy.dll"),
        (Join-Path $docsRoot "使用说明.txt"),
        (Join-Path $cliRoot "AgentEnvManager.Cli.exe"),
        (Join-Path $cliRoot "coreclr.dll")
    )

    foreach ($file in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $file)) {
            throw "Published file is missing: $file"
        }
    }

    Write-Progress `
        -Activity $progressActivity `
        -Status "构建完成" `
        -PercentComplete 100
    Write-Host ""
    Write-Host "构建完成" -ForegroundColor Green
    Write-Host "WPF: $appRoot\AgentEnvManager.Wpf.exe" `
        -ForegroundColor Cyan
    Write-Host "CLI: $cliRoot\AgentEnvManager.Cli.exe" `
        -ForegroundColor Cyan
    Write-Host "说明: $docsRoot\使用说明.txt" `
        -ForegroundColor Cyan
    Write-Host (
        "用时: {0:n1} 秒" -f $stopwatch.Elapsed.TotalSeconds
    ) -ForegroundColor DarkGray
}
catch {
    Write-Host ""
    Write-Host "构建失败: $($_.Exception.Message)" `
        -ForegroundColor Red
    throw
}
finally {
    $stopwatch.Stop()
    Write-Progress -Activity $progressActivity -Completed
    if (-not $NoPause -and [Environment]::UserInteractive -and -not $env:CI) {
        Write-Host ""
        Write-Host "按 Enter 关闭窗口..." -ForegroundColor Yellow
        [void](Read-Host)
    }
}
