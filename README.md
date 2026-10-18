# FewTags-enabled VRCNext

This repository builds a single-file Windows patcher for VRCNext with FewTags shown in player profiles.

## Automatic updates

GitHub Actions checks the latest public VRCNext release every four hours. For a new release it checks out that exact source tag, applies the small FewTags integration patch, downloads and validates both current FewTags databases, then builds VRCNext and the standalone patcher on a Windows runner. A release is published only when every step succeeds. If VRCNext source changes make the integration patch fail, no replacement release is published.

The patcher checks this repository's latest release when it opens. If an updated patcher exists, it asks before downloading it, verifies the published SHA-256 checksum, replaces its own EXE, and restarts. Users do not need Visual Studio, the .NET SDK, or a ZIP beside the patcher.

The patcher still fetches the latest FewTags databases each time it patches VRCNext. It detects the default `%LOCALAPPDATA%\VRCNext\current` installation.

## Building manually

Run the `Build and publish latest VRCNext FewTags patcher` workflow with **workflow_dispatch**. The workflow uses the repository's built-in `GITHUB_TOKEN` with contents write access to create releases; no personal access token or repository secret is required.

The VRCNext integration itself is in `integration/fewtags-vrcnext.patch`. The self-updating Windows Forms patcher is in `patcher/`.
