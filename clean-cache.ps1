param(
    [switch]$NoBuild,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "=== 1. 清理 bin、obj 与 modules\installed 缓存 ===" -ForegroundColor Cyan
$dirs = Get-ChildItem -Path $root -Directory -Recurse -Include bin, obj -ErrorAction SilentlyContinue
$count = 0
foreach ($d in $dirs) {
    if (Test-Path $d.FullName) {
        Write-Host "删除: $($d.FullName)"
        Remove-Item -Path $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
        $count++
    }
}

$installedDir = Join-Path $root "modules\installed"
if (Test-Path $installedDir) {
    $modDirs = Get-ChildItem -Path $installedDir -ErrorAction SilentlyContinue
    foreach ($m in $modDirs) {
        Write-Host "删除已安装模块缓存: $($m.FullName)"
        Remove-Item -Path $m.FullName -Recurse -Force -ErrorAction SilentlyContinue
        $count++
    }
}
Write-Host "清理完成，共移除 $count 项。`n" -ForegroundColor Green

if (-not $NoBuild) {
    function Invoke-BuildProjects([string]$label, [string[]]$projects) {
        Write-Host "=== $label ===" -ForegroundColor Cyan
        foreach ($proj in $projects) {
            if (-not (Test-Path $proj)) {
                throw "找不到工程文件: $proj"
            }
            $rel = $proj.Replace($root, '').TrimStart('\')
            Write-Host "-> 生成 $rel" -ForegroundColor Gray
            dotnet build $proj --no-restore
            if ($LASTEXITCODE -ne 0) {
                throw "生成失败: $rel (退出码 $LASTEXITCODE)"
            }
        }
        Write-Host ""
    }

    function Get-ModuleBuildProjects {
        $modulesRoot = Join-Path $root "src\Modules"
        $manifests = Get-ChildItem -Path $modulesRoot -Filter "manifest.json" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

        $items = @()
        foreach ($mf in $manifests) {
            $json = Get-Content -Path $mf.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $order = if ($null -ne $json.displayOrder) { [int]$json.displayOrder } else { 1000 }
            $entryDir = $mf.Directory.FullName
            $categoryDir = $mf.Directory.Parent.FullName
            $entryCsproj = Get-ChildItem -Path $entryDir -Filter "*.csproj" | Select-Object -First 1
            if ($null -eq $entryCsproj) { continue }

            $projs = New-Object System.Collections.Generic.List[string]
            # 若模块采用 DDD 多类库分层，按 Domain -> Application -> Infrastructure -> Entry 顺序生成
            foreach ($layer in @("Domain", "Application", "Infrastructure")) {
                $layerProj = Get-ChildItem -Path $categoryDir -Recurse -Filter "*.$layer.csproj" -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
                    Select-Object -First 1
                if ($null -ne $layerProj) { $projs.Add($layerProj.FullName) }
            }
            $projs.Add($entryCsproj.FullName)

            if ($null -ne $json.frontend -and -not [string]::IsNullOrWhiteSpace($json.frontend.assembly)) {
                $feName = [System.IO.Path]::GetFileNameWithoutExtension($json.frontend.assembly)
                $feProj = Get-ChildItem -Path $categoryDir -Recurse -Filter "$feName.csproj" -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
                    Select-Object -First 1
                if ($null -ne $feProj) { $projs.Add($feProj.FullName) }
            }

            $items += [PSCustomObject]@{
                Id       = [string]$json.id
                Order    = $order
                Projects = $projs.ToArray()
            }
        }

        $ordered = $items | Sort-Object Order, Id
        $result = New-Object System.Collections.Generic.List[string]
        foreach ($item in $ordered) {
            foreach ($p in $item.Projects) {
                if (-not $result.Contains($p)) { $result.Add($p) }
            }
        }
        return $result.ToArray()
    }

    Write-Host "=== 2. 还原解决方案依赖 ===" -ForegroundColor Cyan
    dotnet restore (Join-Path $root "BlazorBoilerplate.slnx")
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore 失败 (退出码 $LASTEXITCODE)"
    }
    Write-Host ""

    # 第 1 层：BuildingBlocks 基础底座
    $buildingBlocks = @(
        "src\BuildingBlocks\BlazorBoilerplate.Domain\BlazorBoilerplate.Domain.csproj",
        "src\BuildingBlocks\BlazorBoilerplate.Application\BlazorBoilerplate.Application.csproj",
        "src\BuildingBlocks\BlazorBoilerplate.ModuleAbstractions\BlazorBoilerplate.ModuleAbstractions.csproj",
        "src\BuildingBlocks\BlazorBoilerplate.Persistence\BlazorBoilerplate.Persistence.csproj",
        "src\BuildingBlocks\BlazorBoilerplate.Infrastructure\BlazorBoilerplate.Infrastructure.csproj",
        "src\BuildingBlocks\BlazorBoilerplate.ModuleRuntime\BlazorBoilerplate.ModuleRuntime.csproj",
        "src\BuildingBlocks\BlazorBoilerplate.Web.Shared\BlazorBoilerplate.Web.Shared.csproj"
    ) | ForEach-Object { Join-Path $root $_ }
    Invoke-BuildProjects "3. 生成 BuildingBlocks 基础底座" $buildingBlocks

    # 第 2 层：自动扫描 src\Modules 下的全部模块（按 manifest.json displayOrder 与 DDD 分层顺序）
    $moduleProjects = Get-ModuleBuildProjects
    Invoke-BuildProjects "4. 自动发现并生成动态业务模块 ($($moduleProjects.Count) 个工程)" $moduleProjects

    # 第 3 层：Hosts 宿主工程（Client -> WebHost -> ApiHost）
    $hosts = @(
        "src\Hosts\BlazorBoilerplate.WebHost.Client\BlazorBoilerplate.WebHost.Client.csproj",
        "src\Hosts\BlazorBoilerplate.WebHost\BlazorBoilerplate.WebHost.csproj",
        "src\Hosts\BlazorBoilerplate.ApiHost\BlazorBoilerplate.ApiHost.csproj"
    ) | ForEach-Object { Join-Path $root $_ }
    Invoke-BuildProjects "5. 生成 Hosts 宿主工程" $hosts

    Write-Host "全部清理与按序生成完成！" -ForegroundColor Green
}

if (-not $NoPause -and ($args -notcontains '--no-pause')) {
    Read-Host "按回车键退出"
}
