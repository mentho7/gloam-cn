# Release runbook

中文构建勘测、GitHub 仓库管理与首版发布指南：[Windows 打包与首次发布](docs/windows-build-and-first-release.zh-CN.md)。

Gloam ships as a signed, self-updating Windows app built with [Velopack](https://velopack.io)
and code-signed by [Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/).
Releases are cut by **pushing a `vX.Y.Z` tag**; CI builds, signs, and publishes a GitHub
Release, and installed apps auto-update from it.

## How a release flows

1. You push a tag `vX.Y.Z` to `main`.
2. `.github/workflows/build.yml` runs on `windows-latest` in the **`release`** environment:
   - `package.ps1 -Version X.Y.Z -PublishOnly` (self-contained multi-file publish + bundled ArgyllCMS).
   - `azure/login` via OIDC (no stored secret), then `vpk pack --azureTrustedSignFile` signs the
     installer + all bundled exes, then `vpk upload github` publishes the release.
3. `GloamApp-win-Setup.exe` (auto-updating, per-user install) and
   `GloamApp-win-Portable.zip` are attached to the release. Installed clients pick up the update on
   next launch.

The version comes from the tag. `UpdateService.RepoUrl` and the CI upload target are both
`github.com/halideworks/gloam` - keep them identical.

## One-time setup (must exist before the first tag)

### GitHub repo (`halideworks/gloam`)
- **Settings -> Environments -> New environment: `release`** (no protection rules required; add a
  required reviewer if you want a manual gate before each signed release).
- **Settings -> Secrets and variables -> Actions -> Variables** (these are *Variables*, not Secrets):

  | Variable | Value |
  |---|---|
  | `AZURE_SIGN_ENDPOINT` | `https://eus.codesigning.azure.net` (abbreviated region, e.g. `eus` = East US) |
  | `AZURE_SIGN_ACCOUNT` | `gloam-sign` |
  | `AZURE_SIGN_PROFILE` | `gloam-public-trust` |
  | `AZURE_SUBSCRIPTION_ID` | *(subscription holding the signing account)* |
  | `AZURE_CLIENT_ID` | *(the `gloam-github-signing` Entra app)* |
  | `AZURE_TENANT_ID` | *(the directory/tenant)* |

  > The three GUIDs are intentionally not stored in this repo. Set them here as Variables.

### Azure (one-time, already done)
- Trusted Signing account `gloam-sign` (East US) + cert profile `gloam-public-trust` (Public Trust),
  bound to a completed **individual** identity validation.
- Entra app `gloam-github-signing` with:
  - the **Artifact Signing Certificate Profile Signer** role on the `gloam-sign` account, and
  - a **federated credential** (scenario: GitHub Actions, entity type **Environment**, org
    `halideworks`, repo `gloam`, environment `release`) -> subject
    `repo:halideworks/gloam:environment:release`, which the workflow's `release` environment matches.

## Cutting a release

1. Make sure `main` has everything you want shipped and the build is green.
2. Pick the version `X.Y.Z`. Set `<Version>` in `Gloam.csproj` to match and commit it.
   CI overrides it from the tag anyway, but keeping them in sync means a local
   `.\package.ps1` with no arguments builds the same version the tag will.
3. Write the release notes at `docs/release-notes/vX.Y.Z.md` (see below).
4. Push `main`, then tag and push:
   ```bash
   git push origin main
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```
5. Watch the Actions run. On success the GitHub Release is published with the Setup.exe + portable zip,
   both signed and timestamped.
6. Attach the notes to the published release:
   ```bash
   gh release edit vX.Y.Z --notes-file docs/release-notes/vX.Y.Z.md
   ```

### Release notes

`vpk upload github` publishes with an empty body, so notes are a separate step. Releases
through v1.8.0 have no notes at all for this reason. Write them for someone deciding
whether to care, not for someone reading the diff: what changed for them, what they need
to do about it (usually nothing), and anything that changes measured behavior.

State plainly when a change affects calibration math or measured output. That is the one
category of change where a user may want to re-run a calibration rather than let the
update land silently.

## Pre-release validation checklist

- [ ] Build green, full test suite passing.
- [ ] Strict build clean: `dotnet build src/Gloam.sln -c Release -p:GloamStrictBuild=true`.
      The `validate` job now sets `GloamStrictBuild=true`, so CI enforces this too — run it locally
      first to see the diagnostics without waiting for a runner.
- [ ] `<Version>` in `Gloam.csproj` matches the tag being pushed. Checked by
      `scripts/Test-UpdateFeedConsistency.ps1`, which fails when `<Version>` and the newest
      `docs/release-notes/vX.Y.Z.md` disagree.
- [ ] Release notes written at `docs/release-notes/vX.Y.Z.md`.
- [ ] **Colorimeter re-validation**, only when the release changes measured behavior
      (calibration math, LUT generation, drift handling, refinement). Verify the specific
      changed path on real hardware and record the before/after in the release notes.
      Skip for releases that do not touch those paths, and say so rather than leaving the
      box ambiguously unticked.

## Verifying a signed release

- The release assets show a valid Authenticode signature (publisher = your validated legal name) and a
  trusted timestamp.
- Running `Setup.exe` installs per-user to `%LocalAppData%\GloamApp` with no "Unknown Publisher" warning
  (SmartScreen reputation still warms up over the first weeks/installs - expected).
- An installed older build detects the new version, downloads it, and applies on restart.
  For a local end-to-end check against the official GitHub/Velopack feed, run:
  ```powershell
  .\scripts\Test-VelopackInstalledUpdate.ps1 -OlderSetupPath .\path\to\older\GloamApp-win-Setup.exe -ExpectedVersion X.Y.Z -IUnderstandThisModifiesLocalInstall
  ```

> Note: app data (settings, logs, calibration reports) lives under `%LocalAppData%\Gloam`, separate
> from the Velopack install root `%LocalAppData%\GloamApp`, so it survives updates and uninstalls.

## Bad release playbook

Velopack only moves forward. Withdrawing a release removes it from the feed for people who have
not installed yet, but a client already running the bad build sees only "is the feed version
higher than mine?" — so nothing you do to the published release pulls that build back off a
machine. The only exit is a **higher** version number.

This is also why the client cannot save us: the updater verifies each package's checksum against
the feed's own manifest (`Velopack.UpdateManager.VerifyPackageChecksumAsync`), which proves the
download isn't corrupt, not that it is ours. There is no pinned-key check on the client (see
P1-4 in `docs/reviews/defect-register-2026-11-02.zh-CN.md`). A bad package reaching the feed is
therefore applied by every installed client on its next check.

**Rules, in order of importance**

1. **Never decrement and never re-tag.** Do not move, delete and re-push, or reuse the tag of a
   released version. Version numbers must strictly increase; a reused tag breaks delta generation
   and leaves clients that already cached the old entry with a version they will never leave.
2. **Ship `X.Y.Z+1` immediately.** A one-line fix on top of the bad tag is almost always faster
   and safer than trying to reconstruct the previous state.
3. **Withdraw the bad release from the feed** so no new user installs it. Keep the *previous
   good* release's assets — that Setup.exe and Portable zip are the escape hatch below.
4. **Say so in the release notes**, in plain language: what broke, who is affected, whether
   measured output changed, and what the user should do.

**Cutting the hotfix**

```bash
# 1. Branch from the bad tag, not from main, so the fix carries no unrelated changes.
git switch -c hotfix/X.Y.Z+1 <bad-tag>
# 2. Apply the minimal fix, bump <Version> in Gloam.csproj to X.Y.Z+1, and write
#    docs/release-notes/vX.Y.Z+1.md. Both are required: scripts/Test-UpdateFeedConsistency.ps1
#    fails the build if <Version> and the newest notes disagree.
# 3. Tag and push. CI signs and publishes; do not hand-upload assets.
git tag vX.Y.Z+1
git push origin vX.Y.Z+1
```

Clients pick the hotfix up on their next check — at startup, or within
`UpdateService.SuccessfulCheckInterval` (15 minutes) for a long-running session. Users whose app
crashes at startup never reach an update check: point them at the previous release's
`GloamApp-win-Portable.zip`, which runs without installing and leaves `%LocalAppData%\Gloam`
(settings, calibration profiles, reports) untouched.

**Drill status (must be closed before the next release that touches the update path)**

| Step | Status |
|---|---|
| Previous → bad → hotfix round trip on a test machine | **Not yet rehearsed.** `scripts/Test-VelopackInstalledUpdate.ps1` performs the install/update half, but it modifies the local install and needs explicit opt-in. |
| Downgrade to the previous release via Setup.exe | **Unverified.** Velopack is forward-only; assume this does *not* work until a drill shows otherwise, and treat the Portable zip as the supported path. |
| Rollback of the GitHub Release itself | **Unverified.** Deleting a published release may leave clients mid-update; withdraw by editing the release body and marking it superseded rather than deleting, unless a drill proves deletion safe. |

> Until the drill is done, treat any change to `UpdateService`, the feed, or the packaging as
> needing the drill in the same cycle. An unrehearsed rollback is not a rollback.
