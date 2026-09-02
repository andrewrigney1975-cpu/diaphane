# M1 engine build — status

**Building on:** this machine. 24 cores, 128 GB RAM, F: drive (1.3 TB free).
Workspace: `F:\cef-build\` (outside the repo — the checkout is ~150 GB).

## Target
| | |
|---|---|
| CEF branch | `7922` (stable) |
| Chromium | `151.0.7922.174` |
| ungoogled-chromium tag | `151.0.7922.173-1` (closest; expect minor patch fuzz on `.174`) |
| Toolchain | VS 2026 Community v18.8 + Windows SDK 10.0.26100, `DEPOT_TOOLS_WIN_TOOLCHAIN=0` |
| GN | `is_official_build=true proprietary_codecs=true ffmpeg_branding="Chrome" enable_widevine=false safe_browsing_mode=0` + ungoogled `flags.gn` |

## Pipeline (scripts in `engine/scripts/`, run from `F:\cef-build\`)
1. **`checkout.ps1`** — `automate-git.py --branch=7922 --no-build --no-distrib`. Chromium + CEF source, no build. *(running)*
2. **`apply-ungoogled.ps1`** — `prune_binaries` → `patches.py apply` (111 patches) → `domain_substitution` → stage `flags.gn`.
3. **`build.ps1`** — `gclient_hook.py` (cef_create_projects) → append ungoogled flags to `args.gn` → `gn gen` → `autoninja -C out/Release_GN_x64 cef`.

## Gate
`cefsimple.exe` from `out/Release_GN_x64/` plays an H.264 MP4 and a VP9 WebM.

## Known risk points (resolve as hit)
- **VS 2026 (v18.x):** Chromium 151's `build/vs_toolchain.py` may not recognize toolchain version 18.
  Mitigation in scripts: `GYP_MSVS_VERSION=2022` + `GYP_MSVS_OVERRIDE_PATH`. May still need a one-line
  patch to `vs_toolchain.py` to accept `18.0` as `2022`-equivalent.
- **ungoogled patch fuzz:** `.173` patches onto a `.174` tree — a handful of patches may need `-3` fuzz
  or manual rebasing. `patches.py apply` reports which.
- **`is_official_build` + PGO:** forced `chrome_pgo_phase=0` to skip PGO profile download. Revisit for
  release builds.
- **OS long paths:** not enabled (needs admin). `git core.longpaths=true` set; short workspace root
  chosen to compensate.

## Progress log

> **Lesson 1:** always `gclient sync --revision src@<tag>`. Bare `gclient sync` let
> `src` roll to Chromium main (155). Fixed by `pinsync.ps1` → `refs/tags/151.0.7922.174`.
>
> **Lesson 2 (correct stage order):** run `gclient runhooks` (toolchain download:
> clang, rust) *before* ungoogled prune/patch/domain-substitution. Domain substitution
> rewrites `googleapis.com` inside `tools/clang/scripts/update.py` → clang download URL
> becomes an unresolvable `9oo91eapis.qjz9zk` host. And pruning removes files gclient's
> DEPS still references. `reset-and-hooks.ps1` resets to pristine and does hooks first.

- Set up depot_tools (full clone), git config, workspace.
- Fixed: shallow depot_tools clone broke `automate-git.py` compat-version pin → full clone.
- `chromium/src` main tree checked out OK (~102 GB, 29.3M objects).
- `gclient sync` (sub-deps) friction, resolved in stages:
  - transient `git 128` on first pass → forced `--reset --delete_unversioned_trees` re-sync
  - `third_party/litert/src` git-LFS: googlesource LFS mirror returns HTTP 405 on `batch`.
    Blocked object is an **Android** prebuilt we don't need (Windows build) →
    `GIT_LFS_SKIP_SMUDGE=1` + `filter.lfs.smudge/process --skip` leaves LFS pointers in place.
    (`resync3.ps1`.) Revisit if a *Windows* LFS object turns out to be needed at build.
