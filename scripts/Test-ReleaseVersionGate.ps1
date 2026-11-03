# Exercise release-gate failures in an isolated fixture, never by editing the checkout.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$fixture = Join-Path $tempRoot ('gloam-release-gate-' + [guid]::NewGuid().ToString('N'))

function Assert-Gate([string]$Tag, [string]$ExpectedError = '') {
    try {
        & (Join-Path $fixture 'scripts/Test-UpdateFeedConsistency.ps1') -ReleaseTag $Tag
    } catch {
        if ($ExpectedError -and $_.Exception.Message.Contains($ExpectedError)) { return }
        throw
    }
    if ($ExpectedError) { throw "Gate unexpectedly accepted '$Tag': expected $ExpectedError" }
}

try {
    foreach ($relative in @(
        'scripts/Test-UpdateFeedConsistency.ps1',
        'src/Gloam/Infrastructure/Platform/UpdateService.cs',
        'src/Gloam/Gloam.csproj', 'RELEASE.md', 'package.ps1', '.github/workflows/build.yml'
    )) {
        $destination = Join-Path $fixture $relative
        New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot $relative) -Destination $destination
    }
    $projectPath = Join-Path $fixture 'src/Gloam/Gloam.csproj'
    $project = Get-Content -Raw -LiteralPath $projectPath
    if ($project -notmatch '<Version>([^<]+)</Version>') { throw 'Missing project version.' }
    $version = $Matches[1]
    $notesDir = Join-Path $fixture 'docs/release-notes'
    New-Item -ItemType Directory -Force $notesDir | Out-Null
    $notesPath = Join-Path $notesDir "v$version.zh-CN.md"
    Set-Content -LiteralPath $notesPath -Value '# Release fixture'

    Assert-Gate ''
    Assert-Gate "v$version"
    Assert-Gate 'v999.0.0' 'does not match project version'
    Assert-Gate "v$version-rc.1" 'prerelease channels are not configured'
    Assert-Gate 'v01.13.0' 'must be vX.Y.Z'
    Assert-Gate "V$version" 'must be vX.Y.Z'

    Set-Content -LiteralPath $notesPath -Value ' '
    Assert-Gate "v$version" 'needs nonempty, single-version release notes'
    Move-Item -LiteralPath $notesPath -Destination (Join-Path $notesDir "v1.0.0-v$version.zh-CN.md")
    Assert-Gate "v$version" 'needs nonempty, single-version release notes'

    Set-Content -LiteralPath $notesPath -Value '# Release fixture'
    Set-Content -LiteralPath (Join-Path $notesDir 'v999.0.0.md') -Value '# Future notes'
    Assert-Gate "v$version" 'newest release notes'
    Write-Host 'Release version gate: 9 scenarios passed.'
} finally {
    $resolved = [System.IO.Path]::GetFullPath($fixture)
    $prefix = $tempRoot.TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolved -Leaf) -notlike 'gloam-release-gate-*') {
        throw "Refusing to remove unexpected fixture path: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
