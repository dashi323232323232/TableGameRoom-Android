# 通过 GitHub REST API 推送本地 commit（git push 被墙时用）
# 用法: pwsh -File tools/api-push.ps1 -Repo owner/name -Branch main -Tag vX.Y

param(
    [Parameter(Mandatory = $true)][string]$Repo,
    [string]$Branch = "main",
    [string]$Tag = ""
)

$ErrorActionPreference = "Stop"
$token = (Get-Content 'C:\Users\qy130\.dsh-gh-token' -Raw).Trim()
$headers = @{
    Authorization          = "token $token"
    "User-Agent"           = "dsh"
    Accept                 = "application/vnd.github+json"
}
$api = "https://api.github.com/repos/$Repo"

function Invoke-GitHub($method, $uri, $body = $null) {
    $p = @{
        Uri         = $uri
        Headers     = $headers
        Method      = $method
        TimeoutSec  = 120
        ContentType = "application/json"
    }
    if ($body -ne $null) { $p.Body = ($body | ConvertTo-Json -Depth 10 -Compress) }
    return Invoke-RestMethod @p
}

# ---------- 1. 读取本地 HEAD ----------
$sha = (git rev-parse HEAD).Trim()
# git log 多行输出会被当成数组，必须显式拼成单个字符串
$message = ((git log -1 --pretty=%B) | Out-String).Trim()
$parentSha = (git rev-parse HEAD~1).Trim()

Write-Host "本地 HEAD: $sha"
Write-Host "远程父提交: $parentSha"

# ---------- 2. 取远程当前 ref，以它作为父提交 ----------
# 注意：不能直接用本地 HEAD~1 当父提交 —— 本地可能有多个尚未推送的 commit，
# 那样建出来的 commit 在远程找不到父对象（404）。这里一律以远程 HEAD 为准。
try {
    $remoteRef = Invoke-GitHub GET "$api/git/ref/heads/$Branch"
    $remoteSha = $remoteRef.object.sha
    Write-Host "远程 $Branch = $remoteSha"
} catch {
    throw "无法读取远程 $Branch：$($_.Exception.Message)"
}

$parentSha = $remoteSha

# ---------- 3. 找出「远程 HEAD 之后所有本地改动」 ----------
# 本地历史可能因为之前断网被 reset，导致本地父提交在远程不存在。
# 所以这里退化成「本地工作区里所有比远程新的文件」，不依赖 git 历史一致性。
$changed = @(git status --porcelain | ForEach-Object {
    # 形如 " M path" / "?? path" / "A  path"
    $_.Substring(3).Trim().Trim('"')
} | Where-Object { $_ -ne "" })

# 再补上本地 HEAD 相对远程实际不同的文件（历史对不上时的兜底）
$diffFromRemote = @(git diff --name-only "$parentSha" HEAD 2>$null)
foreach ($f in $diffFromRemote) { if ($changed -notcontains $f) { $changed += $f } }

# 兜底：把整个项目目录下受版本控制的文件都比一遍
if ($changed.Count -eq 0) {
    $changed = @(git ls-files)
}

Write-Host "待检查文件: $($changed.Count) 个"

$entries = @()
# 仓库根目录可能与当前目录不同（本仓库根在 D:\dsh\android），必须先定位
$repoRoot = (git rev-parse --show-toplevel).Trim() -replace '/', '\'
Write-Host "仓库根: $repoRoot"

foreach ($path in $changed) {
    $local = Join-Path $repoRoot ($path -replace '/', '\')
    if (-not (Test-Path -LiteralPath $local)) {
        Write-Host "  跳过(已删除): $path"
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($local)
    $b64 = [Convert]::ToBase64String($bytes)

    $blob = Invoke-GitHub POST "$api/git/blobs" @{
        content  = $b64
        encoding = "base64"
    }
    Write-Host "  已上传 $path -> $($blob.sha)"
    $entries += @{ path = $path; mode = "100644"; type = "blob"; sha = $blob.sha }
}

# ---------- 4. 基于远程树建新 tree ----------
$remoteTree = (Invoke-GitHub GET "$api/git/commits/$parentSha").tree.sha
$newTree = Invoke-GitHub POST "$api/git/trees" @{
    base_tree = $remoteTree
    tree      = $entries
}
Write-Host "新 tree: $($newTree.sha)"

# ---------- 5. 建 commit ----------
$newCommit = Invoke-GitHub POST "$api/git/commits" @{
    message = $message
    tree    = $newTree.sha
    parents = @($parentSha)
}
Write-Host "新 commit: $($newCommit.sha)"

# ---------- 6. 更新分支 ----------
Invoke-GitHub PATCH "$api/git/refs/heads/$Branch" @{
    sha   = $newCommit.sha
    force = $false
} | Out-Null
Write-Host "✓ $Branch 已更新"

# ---------- 7. 打 tag ----------
if ($Tag -ne "") {
    try {
        Invoke-GitHub POST "$api/git/refs" @{
            ref = "refs/tags/$Tag"
            sha = $newCommit.sha
        } | Out-Null
        Write-Host "✓ tag $Tag 已创建"
    } catch {
        # 已存在则强制更新
        Invoke-GitHub PATCH "$api/git/refs/tags/$Tag" @{
            sha   = $newCommit.sha
            force = $true
        } | Out-Null
        Write-Host "✓ tag $Tag 已更新"
    }
}

Write-Host ""
Write-Host "完成: https://github.com/$Repo/commit/$($newCommit.sha)"
