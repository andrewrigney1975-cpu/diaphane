# diaphane engine

Produces `libcef.dll` and friends from a Chromium checkout with the ungoogled patch set
and Chrome-branded ffmpeg applied. This is a **build artifact**, not day-to-day source.

## Inputs
- Chromium @ the revision pinned by the target CEF branch (`cef/CHROMIUM_BUILD_COMPATIBILITY.txt`)
- `third_party/ungoogled/` — pinned ungoogled-chromium (submodule)
- `patches/diaphane/` — our patches (default prefs, extra network kill-switches)

## GN args (see `build/args.gn`)
```
is_official_build   = true
proprietary_codecs  = true
ffmpeg_branding     = "Chrome"
enable_widevine     = false
safe_browsing_mode  = 0
enable_reporting    = false
use_official_google_api_keys = false
google_api_key = ""  google_default_client_id = ""  google_default_client_secret = ""
```

## Steps
Run the three scripts in `scripts/` from `F:\cef-build\`, in order:
1. `checkout.ps1` — Chromium + CEF source (branch 7922 / Chromium 151), no build.
2. `apply-ungoogled.ps1` — prune, apply the 111-patch ungoogled set, domain-substitute.
3. `build.ps1` — generate projects, merge ungoogled `flags.gn`, `autoninja ... cef`.
4. Gate: `cefsimple` plays an H.264 MP4 and a VP9 WebM.
5. Package `out/Release_GN_x64/` binaries + resources into `nuget/Diaphane.Core.Native/`.

Live status and known risk points: **`STATUS.md`**.

## Ongoing cost
Every Chromium uplift (~4 weeks on CEF stable) can break our patches and CEF's.
Needs a named owner and a nightly CI job. Falling behind = shipping known CVEs.
