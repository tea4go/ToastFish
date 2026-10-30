<#
.SYNOPSIS
    发布 ToastFish 新版本：把已有的 MSI 传成 Gitee release 附件。

.DESCRIPTION
    只负责发版本，不构建——MSI 用 buildmsi.ps1 事先打好，这里取 bin\Release 里最新的那个，
    版本号（也就是 tag）从 MSI 文件名里取，不重复推导。
    同一个 tag 已经有 release 时不报错，改成「更新标题/正文 + 删掉同名附件再重传」，重发不卡壳。
    Gitee 令牌从 .env 里读（GITEE_TOKEN），不写在命令行上。
    发完会回头查一次 /releases/latest —— 这一路走的是带令牌的 API（限流按令牌算，跟匿名请求不是一回事），
    确认发上去的 tag 和附件都对；程序那边读的是 release 页面的 HTML，不受匿名 API 限流影响。

.EXAMPLE
    .\putver.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$Owner = 'tea4go'
$Repo = 'toastfish'
$Api = "https://gitee.com/api/v5/repos/$Owner/$Repo"

$curl = (Get-Command curl.exe -ErrorAction SilentlyContinue).Source
if (-not $curl) { $curl = 'curl.exe' }

# .env 是 KEY=VALUE，忽略空行和 # 注释
function Read-EnvFile($path) {
    $map = @{}
    if (-not (Test-Path $path)) { return $map }
    foreach ($line in Get-Content $path -Encoding UTF8) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        $at = $t.IndexOf('=')
        if ($at -le 0) { continue }
        $map[$t.Substring(0, $at).Trim()] = $t.Substring($at + 1).Trim()
    }
    return $map
}

# 读接口：GET，令牌放 query。匿名请求会撞 Gitee 的频率限制，只回一句 403 Rate Limit Exceeded。
function Invoke-GiteeGet {
    param([string]$Uri)
    try {
        return Invoke-RestMethod -Uri $Uri -TimeoutSec 30
    }
    catch {
        $status = 0
        $detail = ''
        $resp = $_.Exception.Response
        if ($resp) {
            try { $status = [int]$resp.StatusCode } catch { }
            try {
                $reader = New-Object IO.StreamReader($resp.GetResponseStream())
                $detail = $reader.ReadToEnd()
                $reader.Dispose()
            } catch { }
        }
        throw "Gitee GET 失败：HTTP $status $($_.Exception.Message) $detail"
    }
}

# 写接口：走 curl。Windows PowerShell 5.1 的 Invoke-RestMethod 发表单会被 Gitee 回 403，
# 中文 body 还会被编成乱码；curl 的 --data-urlencode 是 UTF-8，出错时也能拿到服务端原文。
function Invoke-GiteeWrite {
    param(
        [string]$Uri,
        [ValidateSet('Post', 'Patch', 'Delete')]
        [string]$Method,
        [hashtable]$Fields = @{}
    )
    # 少了 id 的退化 URL（.../attach_files/）Gitee 不回 404 而是 405，很难看出是少了 id，
    # 所以在这里先拦一下，出问题时直接说是 URL 的问题。
    if ($Uri -match '/$') { throw "请求 URL 末尾是斜杠，多半是拼 URL 时 id 为空：$Uri" }
    $tmp = [IO.Path]::GetTempFileName()
    $argv = @('-sS', '-m', '120', '-X', $Method.ToUpperInvariant(), '-o', $tmp, '-w', '%{http_code}')
    foreach ($k in $Fields.Keys) { $argv += @('--data-urlencode', "$k=$($Fields[$k])") }
    $status = & $curl @argv $Uri
    $body = ''
    if (Test-Path $tmp) {
        $body = [IO.File]::ReadAllText($tmp, [Text.Encoding]::UTF8)
        Remove-Item $tmp -Force
    }
    if ([int]$status -ge 400) { throw "Gitee $Method 失败：HTTP $status $body" }
    if (-not $body.Trim()) { return $null }
    try { return ($body | ConvertFrom-Json) } catch { return $null }
}

# 查询指定 tag 的 release。Gitee 对「不存在的 tag」返回的是 200 + 正文 null（不是 404），
# 正文 "null" 到了 PowerShell 手里是个真值字符串，所以必须确认拿到的确实是 release 对象（有 id）。
function Get-GiteeReleaseByTag {
    param(
        [string]$Tag,
        [string]$AccessToken
    )
    try {
        # 不要写成 "$Api/releases/tags/$Tag?access_token=$AccessToken"：
        # PowerShell 会把 $Tag?access_token 整个当成变量名，$Tag 变成空串，URL 就成了 /tags/?...
        $uri = "$Api/releases/tags/$Tag" + '?access_token=' + $AccessToken
        $r = Invoke-RestMethod -Uri $uri -TimeoutSec 30
    }
    catch {
        $status = 0
        if ($_.Exception.Response) { try { $status = [int]$_.Exception.Response.StatusCode } catch { } }
        if ($status -eq 404) { return $null }
        throw
    }
    if ($null -eq $r) { return $null }
    if (-not ($r.PSObject.Properties.Name -contains 'id')) { return $null }
    if (-not $r.id) { return $null }
    return $r
}

try {
    $envMap = Read-EnvFile (Join-Path $RepoRoot '.env')
    $token = $envMap['GITEE_TOKEN']
    if (-not $token -or $token -eq 'your_gitee_token_here') {
        throw '没读到 GITEE_TOKEN。把 .env.example 复制成 .env 并填上令牌。'
    }

    # 版本号以 MSI 文件名为准：取 buildmsi.ps1 刚打出来的那个
    $msi = Get-ChildItem (Join-Path $RepoRoot 'bin\Release') -Filter 'ToastFish-*-x64.msi' -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $msi) { throw 'bin\Release 里没有 MSI，请先跑 buildmsi.bat 打包。' }
    if ($msi.Name -notmatch '^ToastFish-(\d+\.\d+\.\d+)-x64\.msi$') {
        throw "从 MSI 文件名「$($msi.Name)」里取不出版本号。"
    }
    $version = $Matches[1]
    $tag = "v$version"

    Write-Host "[release] 版本 : $version"
    Write-Host "[release] tag  : $tag"
    Write-Host "[release] 产物 : $($msi.FullName)"

    # Gitee 这边仓库的默认分支是 master（跟 GitHub 上的 main 不是一回事），
    # 填错的话 POST /releases 会回「创建标签失败：vX.Y.Z」
    $fields = @{
        access_token     = $token
        tag_name         = $tag
        name             = $version
        body             = "ToastFish $version 安装包（$($msi.Name)）"
        target_commitish = 'master'
        prerelease       = 'false'
    }

    # 1) 建 release / 复用同 tag 的 release
    $existing = Get-GiteeReleaseByTag -Tag $tag -AccessToken $token
    if ($existing) {
        Write-Host "[release] 已存在同名 release（id $($existing.id)）：更新标题/正文后重传附件"
        $rel = Invoke-GiteeWrite -Uri "$Api/releases/$($existing.id)" -Method Patch -Fields $fields

        # 同名附件先删，否则会挂出两个一样的文件。
        # 附件 id 只在这个接口里有：release 正文里的 assets 是给页面用的，元素只有 name 和
        # browser_download_url，拿 $a.id 拼出来的是 .../attach_files/（空 id），Gitee 回 405。
        $attachments = Invoke-GiteeGet -Uri "$Api/releases/$($existing.id)/attach_files?access_token=$token"
        foreach ($a in @($attachments)) {
            if (-not $a -or $a.name -ne $msi.Name) { continue }
            if (-not $a.id) { throw "附件「$($a.name)」拿不到 id，没法删；请先到网页上手动删掉它。" }
            Invoke-GiteeWrite -Uri "$Api/releases/$($existing.id)/attach_files/$($a.id)" -Method Delete -Fields @{ access_token = $token } | Out-Null
            Write-Host "[release] 删除旧附件 $($a.name)（id $($a.id)）"
        }
    }
    else {
        $rel = Invoke-GiteeWrite -Uri "$Api/releases" -Method Post -Fields $fields
    }
    Write-Host "[release] release id = $($rel.id)"
    if ($rel.html_url) { Write-Host "[release] 页面     = $($rel.html_url)" }

    # 2) 传附件
    Write-Host '[release] 上传 MSI ...'
    $attachTmp = [IO.Path]::GetTempFileName()
    $attachStatus = & $curl -sS -m 600 -X POST "$Api/releases/$($rel.id)/attach_files" `
        -F "access_token=$token" -F "file=@$($msi.FullName)" -o $attachTmp -w '%{http_code}'
    $attachBody = ''
    if (Test-Path $attachTmp) {
        $attachBody = [IO.File]::ReadAllText($attachTmp, [Text.Encoding]::UTF8)
        Remove-Item $attachTmp -Force
    }
    if ([int]$attachStatus -ge 400) { throw "上传附件失败：HTTP $attachStatus $attachBody" }
    $asset = $null
    try { $asset = $attachBody | ConvertFrom-Json } catch { }
    if ($asset -and $asset.name) { Write-Host "[release] 附件 = $($asset.name)" }

    # 3) 核对：程序读的就是这个接口
    $latest = Invoke-GiteeGet -Uri "$Api/releases/latest?access_token=$token"
    Write-Host "[release] /releases/latest -> tag_name = $($latest.tag_name)"
    foreach ($a in $latest.assets) {
        if ($a.name -like '*.msi') { Write-Host "[release]   msi: $($a.browser_download_url)" }
    }
    Write-Host '[release] 完成。'
    exit 0
}
catch {
    Write-Host "[release] 错误：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
