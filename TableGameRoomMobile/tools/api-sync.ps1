# 通过 GitHub Contents API 直接推送文件内容（完全不依赖本地 git 历史）
#
# 适用场景：git push 连不上 GitHub，且本地历史已和远程分叉。
# 它会拿本地文件内容覆盖远程对应文件，然后一次性提交。
#
# 用法: pwsh -File tools/api-sync.ps1 -Repo owner/name -Branch main -Tag vX.Y

param(
    [Parameter(Mandatory = $true)][string]$Repo,
    [string]$Branch = "main",
    [string]$Tag = "",
    # 只同步这些前缀下的文件（相对仓库根）；留空表示全部
    [string[]]$Only = @()
)

$ErrorActionPreference = "Stop"
$token = (Get-Content 'C:\Users\qy130\.dsh-gh-token' -Raw).Trim()
$headers = @{
    Authorization = "token $token"
    "User-Agent"  = "dsh"
    Accept        = "application/vnd.github+json"
}
$api = "https://api.github.com/repos/$Repo"

function Gh($method, $uri, $body = $null) {
    $p = @{ Uri = $uri; Headers = $headers; Method = $method; TimeoutSec = 120; ContentType = "application/json" }
    if ($null -ne $body) { $p.Body = ($body | ConvertTo-Json -Depth 10 -Compress) }
    return Invoke-RestMethod @p
}

$repoRoot = (git rev-parse --show-toplevel).Trim() -replace '/', '\'
Write-Host "仓库根: $repoRoot"

# ---------- 1. 取远程当前 commit 作为父 ----------
$ref = Gh GET "$api/git/ref/heads/$Branch"
$parentSha = $ref.object.sha
$parentTree = (Gh GET "$api/git/commits/$parentSha").tree.sha
Write-Host "远程 $Branch = $parentSha"

# ---------- 2. 枚举本地受控文件 ----------
# 注意：git ls-files 输出的是「相对当前目录」的路径，而 GitHub 上的路径
# 是「相对仓库根」的。本仓库根在 D:\dsh\android，当前目录在 TableGameRoomMobile，
# 所以必须补上这段前缀，否则比对时永远匹配不上、误判成「没有差异」。
$cwd = (Get-Location).Path -replace '\\', '/'
$rootFwd = $repoRoot -replace '\\', '/'
$prefix = ""
if ($cwd -ne $rootFwd) {
    $prefix = $cwd.Substring($rootFwd.Length).TrimStart('/')
    if ($prefix -ne "") { $prefix += "/" }
}
Write-Host "路径前缀: '$prefix'"

$files = @(git ls-files | ForEach-Object { $prefix + $_ })
if ($Only.Count -gt 0) {
    $files = @($files | Where-Object {
        $f = $_
        ($Only | Where-Object { $f.StartsWith($_) }).Count -gt 0
    })
}
Write-Host "本地文件: $($files.Count) 个"

# ---------- 3. 逐个比对内容，只上传有差异的 ----------
$entries = @()
$uploaded = 0

# 先一次性取远程整棵树，避免每个文件都发一次请求（容易触发限流）
$remoteTree = Gh GET "$api/git/trees/$parentTree`?recursive=1"
$remoteBlobs = @{}
foreach ($item in $remoteTree.tree) {
    if ($item.type -eq "blob") { $remoteBlobs[$item.path] = $item.sha }
}
Write-Host "远程 blob: $($remoteBlobs.Count) 个"

$sha1 = [System.Security.Cryptography.SHA1]::Create()

foreach ($path in $files) {
    # path 是「相对仓库根」的路径；本地实际文件要去掉前缀
    $rel = if ($prefix -ne "" -and $path.StartsWith($prefix)) { $path.Substring($prefix.Length) } else { $path }
    $local = Join-Path $cwd ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $local)) { continue }

    $bytes = [System.IO.File]::ReadAllBytes($local)

    # git blob 哈希 = sha1("blob <len>\0" + content)
    $header = [System.Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $all = New-Object byte[] ($header.Length + $bytes.Length)
    [Array]::Copy($header, 0, $all, 0, $header.Length)
    [Array]::Copy($bytes, 0, $all, $header.Length, $bytes.Length)
    $gitBlobHash = (($sha1.ComputeHash($all)) | ForEach-Object { $_.ToString("x2") }) -join ""

    if ($remoteBlobs.ContainsKey($path) -and $remoteBlobs[$path] -eq $gitBlobHash) {
        continue   # 内容一致，跳过
    }

    $blob = Gh POST "$api/git/blobs" @{
        content  = [Convert]::ToBase64String($bytes)
        encoding = "base64"
    }
    $entries += @{ path = $path; mode = "100644"; type = "blob"; sha = $blob.sha }
    $uploaded++
    Write-Host "  上传 $path"
}

if ($uploaded -eq 0) {
    Write-Host "没有差异，直接用远程 HEAD 打 tag"
    $newSha = $parentSha
} else {
    Write-Host "已上传 $uploaded 个文件，建 tree / commit ..."

    $newTree = Gh POST "$api/git/trees" @{ base_tree = $parentTree; tree = $entries }
    $msg = ((git log -1 --pretty=%B) | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($msg)) { $msg = "sync" }

    $newCommit = Gh POST "$api/git/commits" @{
        message = $msg
        tree    = $newTree.sha
        parents = @($parentSha)
    }
    $newSha = $newCommit.sha

    Gh PATCH "$api/git/refs/heads/$Branch" @{ sha = $newSha; force = $false } | Out-Null
    Write-Host "✓ $Branch 已更新"
}

# ---------- 4. 打 tag ----------
if ($Tag -ne "") {
    try {
        Gh POST "$api/git/refs" @{ ref = "refs/tags/$Tag"; sha = $newSha } | Out-Null
        Write-Host "✓ tag $Tag 已创建"
    } catch {
        Gh PATCH "$api/git/refs/tags/$Tag" @{ sha = $newSha; force = $true } | Out-Null
        Write-Host "✓ tag $Tag 已更新"
    }
}

Write-Host ""
Write-Host "完成: https://github.com/$Repo/commit/$newSha"
