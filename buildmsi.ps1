<#
.SYNOPSIS
    构建 ToastFish 的 MSI 安装包。

.DESCRIPTION
    先调用 build.ps1 编译 Release 产物（这一步把 build-number.txt 里的版本号 +1），
    再用 WiX v5 打包成 MSI。日常 build.ps1 编译不动版本号，只有打 MSI 才递增。
    产物为全机器安装（装到 Program Files\ToastFish），安装与卸载都需要管理员权限。
    运行时的数据库、日志、缓存由程序自己改落到 %LOCALAPPDATA%\ToastFish。

.PARAMETER SkipBuild
    跳过编译，直接用 bin\Release 里的现有产物打包。此时版本号不递增，MSI 沿用现有产物的版本。

.PARAMETER Version
    手动指定版本号，形如 major.minor.build。默认从 build-number.txt 推导，
    规则与 csproj 的 GenerateBuildInfo 一致。

.PARAMETER Output
    输出 MSI 的路径。默认 bin\Release\ToastFish-<版本>-x64.msi。

.EXAMPLE
    .\buildmsi.ps1

.EXAMPLE
    .\buildmsi.ps1 -SkipBuild -Version 3.0.1
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,

    [string]$Version,

    [string]$Output
)

$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$WxsFile  = Join-Path $RepoRoot 'Installer\ToastFish.wxs'
$OutDir   = Join-Path $RepoRoot 'bin\Release'

function Get-WixPath {
    $cmd = Get-Command wix -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $fallback = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
    if (Test-Path $fallback) { return $fallback }

    throw '未找到 wix 命令。请先安装 WiX v5：dotnet tool install --global wix --version "5.*"'
}

# build-number.txt 里是 1..999 的序号，按三位十进制展开成 x.y.z（与 csproj 的 GenerateBuildInfo 一致）
function Get-ProductVersion {
    $file = Join-Path $RepoRoot 'build-number.txt'
    if (-not (Test-Path $file)) { throw "未找到 $file。" }

    $n = 0
    $raw = (Get-Content $file -Raw).Trim()
    if (-not [int]::TryParse($raw, [ref]$n)) { throw "build-number.txt 的内容「$raw」不是数字。" }
    if ($n -lt 1 -or $n -gt 999) { throw "build-number.txt 的值 $n 超出 1..999 的范围。" }

    # 跟 csproj 的 GenerateBuildInfo 同一套规则：补足三位后逐位取字符。
    # 别用除法 —— PowerShell 的 / 是浮点除法，309/10 得 30.9，再 %10 是 0.9，
    # [int] 四舍五入成 1，中间那位就错了（309 会算成 3.1.9，跟 exe 里的版本对不上）。
    $padded = $n.ToString('000')
    return '{0}.{1}.{2}' -f $padded[0], $padded[1], $padded[2]
}

try {
    if (-not (Test-Path $WxsFile)) { throw "未找到 WiX 源文件：$WxsFile" }

    $wix = Get-WixPath
    Write-Host "[msi] wix  : $wix"

    if (-not $SkipBuild) {
        # 用子进程调用，避免 build.ps1 结尾的 exit 把本脚本一起结束掉
        # -BumpVersion：打 MSI 才递增版本号，普通编译不动
        Write-Host '[msi] 编译 Release 产物（版本号 +1）...'
        $buildScript = Join-Path $RepoRoot 'build.ps1'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript -Configuration Release -BumpVersion
        if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE。" }
    }

    $exe = Join-Path $OutDir 'ToastFish.exe'
    if (-not (Test-Path $exe)) {
        throw "未找到 $exe，请去掉 -SkipBuild 重新编译。"
    }

    # 安装包里只放程序与模板。本机跑过的 Log、Mp3Cache 是用户数据，不能混进去。
    foreach ($name in @('Log', 'Mp3Cache', 'app.publish')) {
        $path = Join-Path $OutDir $name
        if (Test-Path $path) { Remove-Item -Recurse -Force $path }
    }

    # inami.db 的模板库以仓库里 Resources\inami.db 为准：本机运行时改的是 bin\Release 那份，
    # PreserveNewest 不会再覆盖它，直接打包会把用户的 API Key 和背诵进度发给所有人。
    $templateDb = Join-Path $RepoRoot 'Resources\inami.db'
    $outDb = Join-Path $OutDir 'Resources\inami.db'
    if (-not (Test-Path $templateDb)) { throw "未找到模板库：$templateDb" }
    Copy-Item $templateDb $outDb -Force

    if (-not $Version) { $Version = Get-ProductVersion }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "版本号「$Version」格式不对，应为 major.minor.build。" }

    if (-not $Output) { $Output = Join-Path $OutDir "ToastFish-$Version-x64.msi" }

    Write-Host "[msi] 版本  : $Version"
    Write-Host "[msi] 产物  : $Output"

    & $wix build $WxsFile -arch x64 -o $Output `
        -d "ProductVersion=$Version" -d "BuildDir=$OutDir"
    if ($LASTEXITCODE -ne 0) { throw "wix build 失败，退出码 $LASTEXITCODE。" }

    Write-Host '[msi] 完成。'
    exit 0
}
catch {
    Write-Host "[msi] 错误：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
