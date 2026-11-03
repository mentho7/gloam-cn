param(
    [string]$ExpectedRepoUrl = "https://github.com/mentho7/gloam-cn",
    [string]$ExpectedPackId = "GloamApp",
    [string]$ReleaseTag = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path

function Read-RepoFile([string]$RelativePath) {
    Get-Content -Raw -LiteralPath (Join-Path $repoRoot $RelativePath)
}

$failures = New-Object System.Collections.Generic.List[string]

$updateService = Read-RepoFile "src/Gloam/Infrastructure/Platform/UpdateService.cs"
if ($updateService -notmatch 'private const string RepoUrl = "([^"]+)"') {
    $failures.Add("UpdateService.RepoUrl was not found.")
} elseif ($Matches[1] -ne $ExpectedRepoUrl) {
    $failures.Add("UpdateService.RepoUrl is '$($Matches[1])', expected '$ExpectedRepoUrl'.")
}

$releaseDoc = Read-RepoFile "RELEASE.md"
if ($releaseDoc -notmatch [regex]::Escape($ExpectedRepoUrl.Replace("https://", ""))) {
    $failures.Add("RELEASE.md does not mention the canonical repo '$ExpectedRepoUrl'.")
}

$packageScript = Read-RepoFile "package.ps1"
if ($packageScript -notmatch "--packId\s+`"$([regex]::Escape($ExpectedPackId))`"") {
    $failures.Add("package.ps1 does not pack with --packId '$ExpectedPackId'.")
}
if ($packageScript -notmatch '\[string\]\$Version = ""') {
    $failures.Add("package.ps1 should default -Version to the project <Version>, not a throwaway value.")
}

$workflow = Read-RepoFile ".github/workflows/build.yml"
if ($workflow -notmatch "--packId\s+$([regex]::Escape($ExpectedPackId))") {
    $failures.Add("build.yml does not pack with --packId '$ExpectedPackId'.")
}
if ($workflow -notmatch 'vpk download github\s+--repoUrl https://github.com/\$\{\{ github\.repository \}\}') {
    $failures.Add("build.yml download step should use the current GitHub repository as the Velopack feed.")
}
if ($workflow -notmatch 'vpk upload github\s+[\s\S]*--repoUrl https://github.com/\$\{\{ github\.repository \}\}') {
    $failures.Add("build.yml upload step should publish to the current GitHub repository as the Velopack feed.")
}

# Version governance. CI passes the tag as /p:Version, which masks a stale <Version> in the
# project file: a local `.\package.ps1` build and the tray/Dashboard version readout would then
# claim a version nobody tagged. Tie the project file to the release notes so the two cannot
# drift apart without a red build.
$csproj = Read-RepoFile "src/Gloam/Gloam.csproj"
if ($csproj -notmatch '<Version>([^<]+)</Version>') {
    $failures.Add("Gloam.csproj does not declare a <Version>.")
} else {
    $projectVersion = $Matches[1].Trim()
    $stableVersionPattern = '(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)'
    if ($projectVersion -notmatch "^$stableVersionPattern`$") {
        $failures.Add("Project version must be a stable X.Y.Z version without leading zeroes.")
    }
    if ($ReleaseTag) {
        if ($ReleaseTag -cnotmatch "^v$stableVersionPattern`$") {
            $failures.Add("Release tag '$ReleaseTag' must be vX.Y.Z; prerelease channels are not configured.")
        } elseif ($ReleaseTag.Substring(1) -ne $projectVersion) {
            $failures.Add("Release tag '$ReleaseTag' does not match project version '$projectVersion'.")
        }
    }
    $exactNotes = @("docs/release-notes/v$projectVersion.md", "docs/release-notes/v$projectVersion.zh-CN.md")
    if (-not ($exactNotes | Where-Object {
        $path = Join-Path $repoRoot $_
        (Test-Path -LiteralPath $path -PathType Leaf) -and
            -not [string]::IsNullOrWhiteSpace((Get-Content -Raw -LiteralPath $path))
    })) {
        $failures.Add("Project version '$projectVersion' needs nonempty, single-version release notes.")
    }
    $notesVersions = Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs/release-notes") -Filter "v*.md" |
        ForEach-Object {
            [regex]::Matches($_.Name, '^v(\d+\.\d+\.\d+)(?:\.zh-CN)?\.md$') | ForEach-Object { [version]$_.Groups[1].Value }
        }

    if (-not $notesVersions) {
        $failures.Add("docs/release-notes contains no vX.Y.Z.md files to check the version against.")
    } else {
        $latestNotesVersion = ($notesVersions | Sort-Object -Descending | Select-Object -First 1).ToString()
        if ($projectVersion -ne $latestNotesVersion) {
            $failures.Add("Gloam.csproj <Version> is '$projectVersion' but the newest release notes are for " +
                "'$latestNotesVersion'. Bump <Version> and write the matching notes in the same commit.")
        }
    }
}

if ($failures.Count -gt 0) {
    throw "Update feed consistency failed:`n - $($failures -join "`n - ")"
}

Write-Host "Update feed consistency passed."
