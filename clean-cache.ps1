param(
    [switch]$NoBuild,
    [switch]$NoPause
)

# 不用 Stop：原生命令（dotnet/bash）写入 stderr 的提示会被 PowerShell 当成终止错误。
# 所有关键步骤用 Assert-LastExitCode 显式检查退出码。
$ErrorActionPreference = "Continue"
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
}
catch { }

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$solution = Join-Path $root "BlazorBoilerplate.slnx"
$buildScript = Join-Path $root "scripts\build-module.sh"

# PowerShell 里的裸 bash 可能解析到 WSL 的 bash.exe（会被安全策略拦截，且没有 sha256sum）。
# 由 git.exe 位置推导 Git Bash：<git-root>\bin\bash.exe。
$bashExe = $null
$gitCmd = Get-Command git.exe -ErrorAction SilentlyContinue
if ($null -ne $gitCmd) {
    $gitRoot = Split-Path -Parent (Split-Path -Parent $gitCmd.Source)
    $candidate = Join-Path $gitRoot "bin\bash.exe"
    if (Test-Path $candidate) { $bashExe = $candidate }
}
if ($null -eq $bashExe) {
    $bashExe = @("C:\Program Files\Git\bin\bash.exe", "C:\Program Files (x86)\Git\bin\bash.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ($null -eq $bashExe) {
    throw "找不到 Git Bash（需要从 git.exe 旁推导 bin/bash.exe），无法打包模块。"
}

function Assert-LastExitCode([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$step 失败（退出码 $LASTEXITCODE）"
    }
}

function Invoke-Main {
    Write-Host "=== 0. 关闭 MSBuild 服务器（避免旧 Razor 生成缓存） ===" -ForegroundColor Cyan
    dotnet build-server shutdown 2>$null | Out-Null

    Write-Host "=== 1. 清理构建输出（dotnet clean，不使用 Remove-Item） ===" -ForegroundColor Cyan
    foreach ($config in @("Debug", "Release")) {
        Write-Host "-> clean $config" -ForegroundColor Gray
        dotnet clean $solution -c $config --nologo -v:q
        Assert-LastExitCode "dotnet clean $config"
    }
    Write-Host ""

    if ($NoBuild) {
        Write-Host "已清理，跳过生成。", "" -ForegroundColor Green
        return
    }

    Write-Host "=== 2. 还原解决方案依赖 ===" -ForegroundColor Cyan
    dotnet restore $solution --nologo -v:q
    Assert-LastExitCode "dotnet restore"
    Write-Host ""

    Write-Host "=== 3. 生成解决方案（Release） ===" -ForegroundColor Cyan
    dotnet build $solution -c Release --no-restore --nologo -v:q
    Assert-LastExitCode "dotnet build"
    Write-Host ""

    # 不再直接删除 modules/installed：那会让模块在运行时彻底消失。
    # 改为按各模块 manifest.json 的 id/version 重新打包，build-module.sh 自身会覆盖旧版本目录。
    Write-Host "=== 4. 按 manifest 重新打包全部动态模块 ===" -ForegroundColor Cyan
    $manifests = Get-ChildItem -Path (Join-Path $root "src\Modules") -Filter manifest.json -Recurse -ErrorAction Stop |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.build)\\' } |
        Sort-Object FullName

    foreach ($mf in $manifests) {
        $json = Get-Content -Path $mf.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $id = [string]$json.id
        if ([string]::IsNullOrWhiteSpace($id)) {
            Write-Warning "跳过无 id 的 manifest: $($mf.FullName)"
            continue
        }
        $version = if ($null -ne $json.version -and -not [string]::IsNullOrWhiteSpace([string]$json.version)) {
            [string]$json.version
        }
        else {
            "1.0.0"
        }
        Write-Host "-> $id $version" -ForegroundColor Gray
        & $bashExe --noprofile --norc $buildScript $id $version
        Assert-LastExitCode "打包模块 $id"
    }
    Write-Host "全部清理与按序生成完成！", "" -ForegroundColor Green
}

$exitCode = 0
try {
    Invoke-Main
}
catch {
    $exitCode = 1
    Write-Host ""
    Write-Host "执行失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
}
finally {
    if (-not $NoPause -and ($args -notcontains '--no-pause')) {
        Write-Host "按回车键退出..." -ForegroundColor Gray
        try {
            [void][Console]::ReadLine()
        }
        catch { }
    }
}

if ($exitCode -ne 0) {
    exit $exitCode
}
