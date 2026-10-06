<#
.SYNOPSIS
    ToastFish 本地构建脚本。

.DESCRIPTION
    自动定位 MSBuild，还原 NuGet 包并编译解决方案，可选清理与打包为 zip。
    与 GitHub Actions 的构建流程保持一致（restore -> msbuild -> package）。

.PARAMETER Configuration
    构建配置，Debug 或 Release，默认 Release。

.PARAMETER Platform
    目标平台，Any CPU / x64 / x86，默认 Any CPU。

.PARAMETER Clean
    编译前删除 bin、obj 目录。

.PARAMETER Package
    编译后把产物打包为 zip（自动排除重复的 app.publish）。

.PARAMETER OutputZip
    压缩包输出路径，默认 <仓库根目录>\ToastFish-<Configuration>.zip。相对路径以仓库根目录为基准。

.PARAMETER NoRestore
    跳过 NuGet 还原。

.PARAMETER Verbosity
    MSBuild 输出详细级别，默认 minimal。

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Configuration Debug -Platform x64 -Clean

.EXAMPLE
    .\build.ps1 -Package -OutputZip dist\ToastFish.zip
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('Any CPU', 'x64', 'x86')]
    [string]$Platform = 'Any CPU',

    [switch]$Clean,

    [switch]$Package,

    [string]$OutputZip,

    [switch]$NoRestore,

    [ValidateSet('quiet', 'minimal', 'normal', 'detailed', 'diagnostic')]
    [string]$Verbosity = 'minimal'
)

$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$Solution = Join-Path $RepoRoot 'ToastFish.sln'
$OutDir   = Join-Path $RepoRoot "bin\$Configuration"

function Get-MSBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
                           -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($found) { return $found }
    }
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw '未找到 MSBuild，请安装 Visual Studio 或将其加入 PATH。'
}

try {
    if (-not (Test-Path $Solution)) { throw "未找到解决方案：$Solution" }

    $msbuild = Get-MSBuildPath
    Write-Host "[build] MSBuild : $msbuild"
    Write-Host "[build] 配置    : $Configuration"
    Write-Host "[build] 平台    : $Platform"

    if ($Clean) {
        Write-Host '[build] 清理 bin、obj ...'
        foreach ($name in @('bin', 'obj')) {
            $path = Join-Path $RepoRoot $name
            if (Test-Path $path) { Remove-Item -Recurse -Force $path }
        }
    }

    if (-not $NoRestore) {
        Write-Host '[build] 还原 NuGet 包 ...'
        & $msbuild $Solution -t:Restore "-p:Configuration=$Configuration" "-v:$Verbosity" -nologo
        if ($LASTEXITCODE -ne 0) { throw "NuGet 还原失败，退出码 $LASTEXITCODE。" }
    }

    Write-Host '[build] 编译 ...'
    & $msbuild $Solution "-p:Configuration=$Configuration" "-p:Platform=$Platform" "-v:$Verbosity" -nologo
    if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE。" }

    $exe = Join-Path $OutDir 'ToastFish.exe'
    if (Test-Path $exe) {
        Write-Host "[build] 产物：$exe"
    } else {
        Write-Warning "[build] 未找到预期产物 $exe"
    }

    if ($Package) {
        if (-not $OutputZip) {
            $OutputZip = Join-Path $RepoRoot "ToastFish-$Configuration.zip"
        } elseif (-not [System.IO.Path]::IsPathRooted($OutputZip)) {
            $OutputZip = Join-Path $RepoRoot $OutputZip
        }
        $zipPath = [System.IO.Path]::GetFullPath($OutputZip)
        $zipDir  = Split-Path -Parent $zipPath
        if ($zipDir -and -not (Test-Path $zipDir)) {
            New-Item -ItemType Directory -Force -Path $zipDir | Out-Null
        }

        Write-Host '[build] 打包 ...'
        $publish = Join-Path $OutDir 'app.publish'
        if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
        Compress-Archive -Path (Join-Path $OutDir '*') -DestinationPath $zipPath -Force
        Write-Host "[build] 压缩包：$zipPath"
    }

    Write-Host '[build] 完成。'
    exit 0
}
catch {
    Write-Host "[build] 错误：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
