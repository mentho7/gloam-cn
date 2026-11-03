# Security policy

## Reporting a vulnerability

**Please do not open a public issue.**

Report privately through GitHub:
[Security → Report a vulnerability](https://github.com/halideworks/gloam/security/advisories/new).
That opens a private advisory visible only to the maintainers.

Include what you would want if you were fixing it: what you did, what happened, what you
expected, and (if you have one) a proof of concept. If the issue involves a file Gloam
parses, attach the file.

You will get an acknowledgement. If a report turns out not to be a security issue, we will
say so and why rather than leaving it open indefinitely.

## Supported versions

The latest release. Gloam auto-updates by default when installed with the setup package,
and fixes ship in a new release rather than as backports to older versions.

## In scope

These are the places Gloam handles input it did not create, and where a vulnerability would
be most consequential:

- **Profile and settings parsing.** `settings.json`, calibration profiles, saved report
  snapshots, and update state, all under `%LOCALAPPDATA%\Gloam`. Malformed or hostile input
  should fail closed, not execute or corrupt.
- **Downloaded CCSS/CCMX handling.** Meter corrections fetched from the DisplayCAL
  community database are third-party files from a remote source, parsed locally.
- **ArgyllCMS invocation.** Gloam launches `spotread`, `dispwin`, and related tools as
  child processes, and locates the binaries at runtime. Argument handling, path resolution,
  and anything that could result in executing an unexpected binary is in scope.
- **The updater.** Velopack feed handling, checksum and version checks, and the install
  path under `%LOCALAPPDATA%\GloamApp`.
- **Elevation.** The only elevated operation is launching the ArgyllCMS USB driver installer
  (`UsbDriverHelper`), which requests admin rights with `Verb="runas"`. Gloam verifies that
  binary against a pinned SHA-256 before the UAC prompt is shown; anything that gets an
  unverified file past that check is in scope.
- **ICC/MHC2 profile generation and installation**, including what Gloam writes into the
  Windows color store.

Bundled ArgyllCMS is third-party. Report vulnerabilities in ArgyllCMS itself upstream. But
if Gloam's *use* of it is what makes something exploitable, that is ours, so tell us.

## Out of scope

- Findings that require an attacker who already has code execution as the user. Gloam is a
  per-user desktop application with no service; the one elevated operation is the ArgyllCMS
  USB driver install, and that path is gated on a pinned hash (see "Known limitations").
- Missing hardening flags with no demonstrated impact.
- SmartScreen reputation warnings on new releases. Expected, and covered in
  [docs/troubleshooting.md](docs/troubleshooting.md).

## Known limitations

**The auto-updater does not verify a cryptographic signature on the client.** The only integrity
check is a checksum comparison: `Velopack.UpdateManager.VerifyPackageChecksumAsync` (Velopack
1.2.0) compares the downloaded package's SHA-1/SHA-256 against the `releases.win.json` manifest
fetched from the same GitHub feed. That catches corrupted or truncated downloads. It does not
establish authenticity, because whoever can publish to the feed can publish a matching manifest.
There is no Ed25519 or pinned-public-key verification code in the Velopack 1.2.0 client at all,
so `vpk pack --signParams` cannot be validated on this side, and the library exposes no hook for
us to add our own check.

Consequence, stated plainly: **whoever can publish a release to `halideworks/gloam` can push
code to every installed client**, and the clients will apply it on their next update check. The
CI side signs artifacts with Azure Trusted Signing, but server-side signing is not client-side
verification.

Compensating controls, which is what currently carries this risk:

- The GitHub account and the `release` environment are the trust anchor. Two-factor
  authentication, branch protection on `main`, and a required reviewer on the `release`
  environment are the actual security boundary here.
- Artifacts are Authenticode-signed and verified before upload
  (`scripts/Test-SignedReleaseArtifacts.ps1`), so a mis-signed or unsigned build fails the
  release rather than shipping.
- Releases are cut only from tags by CI. Hand-uploaded assets are not part of the process.

Closing the gap properly means either replacing the update mechanism or pinning keys in a
library that supports it; until then this is an accepted, documented risk rather than a solved
problem. Tracked as P1-4 in the internal release checklist.

## Prior review

The codebase was audited in July 2026; the findings, the verification performed, and the
stated residual risks are in
[docs/codebase-hardening-audit-2026-07.md](docs/codebase-hardening-audit-2026-07.md). It is
worth reading before reporting, because it is explicit about what that audit could not cover, and
those gaps are where something is most likely still hiding.
