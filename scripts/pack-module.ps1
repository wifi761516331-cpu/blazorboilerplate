#BlazorBoilerplate 模块打包脚本
# 用法示例：
#   powershell -File scripts\pack-module.ps1 -ModuleDir "src\Modules\Catalog\BlazorBoilerplate.Modules.Catalog"
# 产物：<ModuleDir>\bin\module-package\<id>-<version>.zip（可直接在 /modules 页面上传）
#
# 包结构（与 modules/installed/<id>/<version> 一致）：
#   manifest.json          模块清单（id/name/displayName/version/entryAssembly...）
#   backend\<入口 dll + deps.json>   托管代码（Collectible ALC 加载）
#   frontend/ migrations/ localization/   可选约定目录
#   checksums.sha256       可选，入口程序集 SHA-256
param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleDir   # 模块项目目录（根目录含 manifest.json）
)

$ErrorActionPreference = 'Stop'

$ModuleDir = (Resolve-Path $ModuleDir).Path
$manifestPath = Join-Path $ModuleDir 'manifest.json'
if (-not (Test-Path $manifestPath)) { throw "未找到 $manifestPath —— 打包目录根必须含 manifest.json" }

# 1) 读取 manifest（去 BOM 解析）
$raw = [IO.File]::ReadAllText($manifestPath)
$manifest = $raw | ConvertFrom-Json
$id, $version = $manifest.id, $manifest.version
if (-not $id -or -not $version) { throw "manifest 必须声明 id 与 version" }
Write-Host "打包模块 $id v$version（$($manifest.displayName)）" -ForegroundColor Cyan

# 2) dotnet build 入口项目（从 entryAssembly 反推 csproj）
$csproj = Get-ChildItem $ModuleDir -Filter '*.csproj' | Select-Object -First 1
if ($csproj) {
    Write-Host "构建 $($csproj.FullName)（v$version）..." -ForegroundColor DarkCyan
    # 注入 -p:Version：程序集身份随包版本变化，WebHost 重启后才能按版本优先加载包内前端组件
    dotnet build $csproj.FullName -c Release -p:Version=$version --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败" }
}

# 3) 组装包暂存目录
$stage = Join-Path $ModuleDir "bin\module-package\$id-$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item $stage -ItemType Directory -Force | Out-Null

Copy-Item $manifestPath (Join-Path $stage 'manifest.json')

# backend/<入口 dll + deps.json>：优先 Release 输出
$backend = New-Item (Join-Path $stage 'backend') -ItemType Directory -Force
$entryName = [IO.Path]::GetFileName($manifest.entryAssembly)
if ($csproj) {
    $buildOut = Join-Path $ModuleDir "bin\Release\net10.0"
    if (-not (Test-Path $buildOut)) { $buildOut = Get-ChildItem (Join-Path $ModuleDir 'bin\Release') -Directory | Select-Object -First 1 -ExpandProperty FullName }
    $deps = Join-Path $buildOut "$([IO.Path]::GetFileNameWithoutExtension($entryName)).deps.json"
    Copy-Item (Join-Path $buildOut $entryName) $backend.FullName
    if (Test-Path $deps) { Copy-Item $deps $backend.FullName }
} else {
    Copy-Item (Join-Path $ModuleDir "backend\$entryName") $backend.FullName
}

# 可选约定目录
foreach ($folder in 'frontend', 'migrations', 'localization') {
    $src = Join-Path $ModuleDir $folder
    if (Test-Path $src) { Copy-Item $src (Join-Path $stage $folder) -Recurse }
}

# 独立前端 RCL 程序集（若 manifest.frontend.assembly 已声明）
if ($manifest.frontend -and $manifest.frontend.assembly) {
    $frontendAsm = [IO.Path]::GetFileName($manifest.frontend.assembly)
    $frontendBase = [IO.Path]::GetFileNameWithoutExtension($frontendAsm)
    $frontendDir = Join-Path (Split-Path $ModuleDir -Parent) $frontendBase
    $frontendCsproj = if (Test-Path $frontendDir) { Get-ChildItem $frontendDir -Filter '*.csproj' | Select-Object -First 1 } else { $null }
    if ($frontendCsproj) {
        Write-Host "构建前端 RCL $($frontendCsproj.FullName)（v$version）..." -ForegroundColor DarkCyan
        dotnet build $frontendCsproj.FullName -c Release -p:Version=$version --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "前端 RCL dotnet build 失败" }
        $frontendOut = Join-Path $frontendDir 'bin\Release\net10.0'
        $frontendStage = New-Item (Join-Path $stage 'frontend') -ItemType Directory -Force
        Copy-Item (Join-Path $frontendOut $frontendAsm) $frontendStage.FullName
        $frontendDeps = Join-Path $frontendOut "$frontendBase.deps.json"
        if (Test-Path $frontendDeps) { Copy-Item $frontendDeps $frontendStage.FullName }
        $scopedCss = Join-Path $frontendDir "obj\Release\net10.0\scopedcss\projectbundle\$frontendBase.bundle.scp.css"
        if (Test-Path $scopedCss) { Copy-Item $scopedCss (Join-Path $frontendStage.FullName "$frontendBase.styles.css") }
    }
}

# checksums.sha256（若模块声明了 checksumSha256 则必须包含入口程序集哈希）
$checksums = Join-Path $ModuleDir 'checksums.sha256'
if (Test-Path $checksums) { Copy-Item $checksums $stage }

# 4) 压缩为 .zip（.NET ZipArchive 保证 '/' 分隔条目，Windows bash 下也安全）
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zipPath = "$stage.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath,
    [IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "打包完成：$zipPath" -ForegroundColor Green
Write-Host "上传方式：/modules 页面 →「上传模块」选择该 zip（归档至 MinIO 并自动安装）。"
