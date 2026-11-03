param(
    [string]$ExpectedRepoUrl = "https://github.com/halideworks/gloam",
    [string]$ExpectedPackId = "GloamApp"
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
    $notesVersions = Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs/release-notes") -Filter "v*.md" |
        ForEach-Object {
            [regex]::Matches($_.Name, 'v(\d+\.\d+\.\d+)') | ForEach-Object { [version]$_.Groups[1].Value }
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
