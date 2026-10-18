<div align="center">

# VRCNext + FewTags

**FewTags in VRCNext player profiles, installed with one small Windows app.**

[![Build status](https://github.com/ResKyuu/FewTags-patcher-VRCNext/actions/workflows/release-patcher.yml/badge.svg)](https://github.com/ResKyuu/FewTags-patcher-VRCNext/actions/workflows/release-patcher.yml)
[![Latest release](https://img.shields.io/github/v/release/ResKyuu/FewTags-patcher-VRCNext?label=latest%20patcher)](https://github.com/ResKyuu/FewTags-patcher-VRCNext/releases/latest)

<br>

### [Download the latest patcher](https://github.com/ResKyuu/FewTags-patcher-VRCNext/releases/latest)

One EXE. No Visual Studio, .NET install, or companion ZIP required.

</div>

---

## Install or update

1. Download **`VRCNext-FewTags-Patcher.exe`** from the [latest release](https://github.com/ResKyuu/FewTags-patcher-VRCNext/releases/latest).
2. Run it. It looks for VRCNext in its default install folder: `%LOCALAPPDATA%\VRCNext\current`.
3. If it is installed elsewhere, select the folder containing `VRCNext.exe`.
4. Click **Update VRCNext**. Close VRCNext first if the patcher asks you to.

Your existing files that need replacing are backed up in the VRCNext folder. Launch VRCNext as usual when the patch completes.

## What updates automatically

- **FewTags data:** the patcher downloads and validates the newest FewTags databases each time you apply the integration.
- **Patcher:** when a newer patcher release is available, the app asks before downloading it. It checks the release's SHA-256 checksum before replacing itself.
- **VRCNext integration:** this repository checks for a VRCNext release every four hours. A compatible build is published automatically after the integration patch and build succeed. The patcher prompts before installing its own update; it will not replace a newer VRCNext install with an older build.

If VRCNext changes in a way that breaks the integration, the build stops and no new patcher release is published until the integration is fixed.

## How the release is built

GitHub Actions checks out the selected VRCNext release, applies [`integration/fewtags-vrcnext.patch`](integration/fewtags-vrcnext.patch), downloads the current FewTags data, and builds both VRCNext and the single-file Windows patcher. The binaries are released only after the build succeeds.

The build job has read-only repository permission. A separate job receives release permission only to publish the completed files.

## Security notes

- Releases are built from public upstream VRCNext source and public FewTags data. Review the source and release notes before installing if you want to inspect what changed.
- The patcher verifies the downloaded EXE against a SHA-256 file published in the same GitHub release. This detects download corruption; it does not independently prove who created a release.
- Windows may show a SmartScreen warning because the EXE is not signed with a commercial code-signing certificate.
- The installer changes files in the selected VRCNext directory and keeps backups of replaced files. It does not request administrator access.

## For contributors

The integration patch is in [`integration/`](integration/), and the Windows Forms patcher is in [`patcher/`](patcher/). To create a release manually, run **Build and publish latest VRCNext FewTags patcher** from the repository's Actions page using **Run workflow**.

## Upstream projects

- [VRCNext](https://github.com/shinyflvre/VRCNext)
- [FewTags database](https://github.com/Fewdys/FewTags)
- [FewTags source and animated tag formats](https://github.com/Fewdys/FewTags-VRC-Source)
