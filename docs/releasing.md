# Releasing Degausser

Degausser 3.x is a niche cross-platform desktop utility, so the release format is deliberately simple: one unpackaged, self-contained, single-file executable per platform (Windows, Linux and macOS), each zipped with the `scorefixes/` folder, `LICENSE` and `THIRD-PARTY-NOTICES.md`. Users do not need to install .NET separately.

## Versioning

The product version lives in the repository-root `Directory.Build.props`:

```xml
<Version>3.0.0</Version>
```

`-betaN` versions are for builds shared ad hoc with testers. Public GitHub releases use the plain version (`3.0.0`). The release workflow requires the tag to match exactly, including any suffix (`degausser-v3.0.0`, or `degausser-v3.0.0-beta1` for a tagged beta), and versions with a suffix are published as GitHub prereleases.

The desktop and 3DS applications are versioned independently even though they share one repository. Use product-qualified tags:

```text
degausser-v3.0.0
degausser-v3.0.1
degausser-v3.1.0

degausser3ds-vX.Y.Z
```

The UI calls the desktop major/minor family “Degausser 3.0”, while About displays the full release version. A Degausser3DS tag/release does not imply a desktop release, and vice versa.

## Release assets

A release has one zip per platform, each holding the self-contained executable, the `scorefixes/` folder, `LICENSE` and `THIRD-PARTY-NOTICES.md`:

```text
Degausser-3.0.0-win-x64.zip        Degausser.exe, scorefixes/, LICENSE, THIRD-PARTY-NOTICES.md
Degausser-3.0.0-linux-x64.zip      Degausser,     scorefixes/, LICENSE, THIRD-PARTY-NOTICES.md
Degausser-3.0.0-osx-arm64.zip      Degausser,     scorefixes/, LICENSE, THIRD-PARTY-NOTICES.md
SHA256SUMS.txt
```

The app reads a `scorefixes` folder beside the executable, so English titles and lyrics work straight after unzipping. The Linux and macOS zips keep the executable bit. GitHub supplies the source archives for the tag, so don't upload one.

The macOS build is not signed or notarised, so Gatekeeper blocks it on first run (right-click → Open, or `xattr -d com.apple.quarantine Degausser`). Intel Macs (`osx-x64`) and `linux-arm64` aren't built; add them to the script's default list if people ask.

## Why self-contained single-file?

- no separate .NET 10 runtime installation;
- no folder full of framework/application DLLs;
- no installer required for a portable power-user utility;
- settings and logs are optional and, when the user opts in, live in the per-user application-data folder (`%LOCALAPPDATA%\\Degausser` on Windows), so the executable itself can remain read-only and can be replaced during upgrades;
- GitHub release assets stay understandable years later.

Single-file self-contained builds are runtime-specific. v3.0 ships `win-x64`, `linux-x64` and `osx-arm64`; add `win-arm64`, `osx-x64` or `linux-arm64` only if there is real demand.

Do **not** enable trimming for the Avalonia build yet. Degausser uses Avalonia plus reflection-driven binary serialization, and the modest download-size saving is not worth introducing trim-specific correctness risk. ReadyToRun is also intentionally omitted for now: it increases the release size for little benefit in a small desktop utility.

## Build locally

On any OS with bash and `zip` (Git Bash on Windows):

```bash
./scripts/publish-release.sh                   # win-x64 linux-x64 osx-arm64
./scripts/publish-release.sh --skip-tests linux-x64
```

The finished assets appear under `artifacts/release/`.

For a quick test build to share, a plain RID-specific publish is enough on any OS:

```bash
dotnet publish src/Degausser.Avalonia -c Release -r win-x64 -o out   # or linux-x64, osx-arm64
```

The project file makes any RID-specific publish produce a single compressed, self-contained executable (~50 MB). It also removes the ~105 MB of loose native `.pdb` symbols and other-platform binaries that the SkiaSharp/HarfBuzz/DryWetMIDI packages would otherwise drop beside it. Ship only the executable.

## GitHub release automation

Pushing a tag matching the project version automatically runs the build/tests on Linux, publishes the three zips, creates SHA-256 checksums, and creates the GitHub Release:

```powershell
git tag degausser-v3.0.0
git push origin degausser-v3.0.0
```

The release workflow refuses to publish if the tag and `Directory.Build.props` version disagree.

## Degausser3DS releases

`3ds/Degausser3DS` is a separate native application. It is deliberately outside `Degausser.sln` and does not share the desktop version number. When a new 3DS release is prepared, tag it independently (for example `degausser3ds-v1.2.0`) and attach only the native 3DS artefacts appropriate to that release.

The historical 3DS source currently checked into this repository is preserved as-is apart from repository-path/build-output housekeeping. Degausser 3.0.0 does not claim a new Degausser3DS release. A dedicated 3DS release workflow can be added when that project is next actively maintained and its current devkitPro build is verified.

## Installer / signing / package-manager options

An MSI/MSIX/Inno installer is not part of v3.0. Add one only if users start asking for Start-menu integration, file associations, automatic uninstall, or managed updates. Similarly, a `winget` manifest can be added later without changing the underlying GitHub release format.

Authenticode code signing is valuable if a suitable signing certificate becomes practical: it reduces Windows reputation/security friction. It is independent of whether the release is a single EXE or an installer.
