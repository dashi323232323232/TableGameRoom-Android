# 发布一个版本：上传到 GitHub，然后本地只留 2 个版本
#
# 用法：
#   pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\release.ps1 -Message "v2.6 描述"
#   pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\release.ps1   # 交互式输入
#
# 它做的事：
#   1. 提交当前所有改动（如果有）
#   2. 自动算下一个版本号并打 tag
#   3. 推送到 GitHub（分支 + 全部 tag）
#   4. 本地精简到最新 2 个提交
#
# GitHub 上会永久保留所有版本（靠 tag 锚定），本地只有 2 个。

param(
    [string]$Message = "",
    [string]$Version = "",
    [int]$KeepLocal = 2,
    [string]$Proxy = "http://127.0.0.1:7897"
)

$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)

try {
    if (-not (Test-Path .git)) { throw "当前目录不是 git 仓库" }

    $repoName = Split-Path (Get-Location).Path -Leaf
    Write-Host "=== $repoName ==="

    # ---------- 1. 提交改动 ----------
    $dirty = @(git status --porcelain)
    if ($dirty.Count -gt 0) {
        Write-Host "有 $($dirty.Count) 项改动，准备提交"

        if ([string]::IsNullOrWhiteSpace($Message)) {
            $Message = Read-Host "提交信息"
            if ([string]::IsNullOrWhiteSpace($Message)) {
                $Message = "更新 $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
            }
        }

        git add -A
        git commit --quiet -m $Message
        Write-Host "✓ 已提交"
    }
    elseif ([string]::IsNullOrWhiteSpace($Message)) {
        # 没有改动：给当前 HEAD 补打 tag 即可
        $Message = (git log -1 --format=%s)
        Write-Host "工作区干净，将归档当前 HEAD：$Message"
    }

    # ---------- 2. 版本号 ----------
    $head = (git rev-parse HEAD)
    $headTags = @(git tag --points-at $head)

    if ($headTags.Count -gt 0 -and [string]::IsNullOrWhiteSpace($Version)) {
        $tag = $headTags[0]
        Write-Host "当前 HEAD 已有 tag：$tag"
    }
    else {
        if ([string]::IsNullOrWhiteSpace($Version)) {
            # 自动递增：找最大的 vX.Y
            $existing = @(git tag -l 'v*' | Where-Object { $_ -match '^v\d+\.\d+$' })
            $next = "v0.1"
            if ($existing.Count -gt 0) {
                $versions = $existing | ForEach-Object {
                    $p = $_ -replace '^v', '' -split '\.'
                    [pscustomobject]@{ Major = [int]$p[0]; Minor = [int]$p[1] }
                } | Sort-Object Major, Minor
                $last = $versions[-1]
                $next = "v$($last.Major).$($last.Minor + 1)"
            }
            $Version = $next
        }

        git tag $Version $head
        $tag = $Version
        Write-Host "✓ 已打 tag：$tag"
    }

    # ---------- 3. 推送到 GitHub ----------
    $branch = (git rev-parse --abbrev-ref HEAD)
    Write-Host ""
    Write-Host "推送到 GitHub..."

    & git -c "http.proxy=$Proxy" push origin $branch --force 2>&1 |
        Where-Object { $_ -notmatch '^remote:' } | ForEach-Object { Write-Host "  $_" }
    & git -c "http.proxy=$Proxy" push origin --tags --force 2>&1 |
        Where-Object { $_ -notmatch '^remote:' } | ForEach-Object { Write-Host "  $_" }

    # 校验远端
    $remoteTags = @(& git -c "http.proxy=$Proxy" ls-remote --tags origin 2>&1 |
        ForEach-Object { if ($_ -match 'refs/tags/(\S+)$') { $Matches[1] } } |
        Where-Object { $_ -notmatch '\^\{\}$' })
    Write-Host "✓ GitHub 上共 $($remoteTags.Count) 个版本：$($remoteTags -join ', ')"

    # ---------- 4. 本地精简 ----------
    $count = [int](git rev-list --count HEAD)
    Write-Host ""
    if ($count -le $KeepLocal) {
        Write-Host "本地已有 $count 个版本，无需精简"
    }
    else {
        Write-Host "本地精简：$count → $KeepLocal"

        $keep = @(git rev-list --reverse HEAD | Select-Object -Last $KeepLocal)
        $subjects = @()
        foreach ($h in $keep) {
            $s = (git log -1 --format=%s $h)
            if ([string]::IsNullOrWhiteSpace($s)) { $s = "版本 $($h.Substring(0,8))" }
            $subjects += $s
        }

        $tagMap = @{}
        foreach ($t in @(git tag -l)) {
            $th = (git rev-list -n 1 $t)
            if ($keep -contains $th) { $tagMap[$th] = $t }
        }

        git checkout --orphan __rel_tmp $keep[0] --quiet 2>&1 | Out-Null
        git reset --mixed --quiet
        git add -A
        git commit --quiet -m $subjects[0]
        $new = @((git rev-parse HEAD))

        for ($i = 1; $i -lt $keep.Count; $i++) {
            git rm -r --cached --quiet . 2>&1 | Out-Null
            Get-ChildItem -Force | Where-Object { $_.Name -ne '.git' } |
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            git checkout $keep[$i] -- . 2>&1 | Out-Null
            git add -A
            git commit --quiet -m $subjects[$i]
            $new += (git rev-parse HEAD)
        }

        git checkout -B $branch --quiet
        git branch -D __rel_tmp --quiet 2>&1 | Out-Null

        foreach ($t in @(git tag -l)) { git tag -d $t | Out-Null }
        for ($i = 0; $i -lt $new.Count; $i++) {
            if ($tagMap.ContainsKey($keep[$i])) { git tag $tagMap[$keep[$i]] $new[$i] }
        }

        git reflog expire --expire=now --all 2>&1 | Out-Null
        git gc --prune=now --quiet 2>&1 | Out-Null
        Write-Host "✓ 本地已精简"
    }

    # ---------- 5. 汇总 ----------
    Write-Host ""
    Write-Host "=== 本地 ==="
    git log --oneline --decorate
    Write-Host ""
    Write-Host "=== GitHub（全部版本，永久保留）==="
    Write-Host ($remoteTags -join ', ')
}
finally {
    Pop-Location
}
