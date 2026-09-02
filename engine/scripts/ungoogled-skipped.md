# Ungoogled patches skipped under CEF-first ordering (14 of 109)

Applied CEF patches first (supported path, 0 failures), then ungoogled on top with
`patch --forward --fuzz=3`. These 14 don't apply and are the patch-rebase backlog:

**Irrelevant to a libcef build (ungoogled *browser* UI layer):**
- add-ungoogled-flag-headers, add-components-ungoogled, add-flags-for-existing-switches,
  add-flags-for-referrer-customization, first-run-page, add-credits, add-flag-for-disabling-jit,
  build-with-wasm-rollup
- fix-building-with-prunned-binaries (n/a — we use `--keep-contingent-paths`)

**Worth hand-porting later (real hardening lost for now):**
- replace-google-search-engine-with-nosearch  (mitigated: our shell sets the search engine)
- block-trk-and-subdomains, block-requests      (ungoogled internal request blocking; conflicts with CEF net_service)
- flag-max-connections-per-host
- flag-fingerprinting-canvas-image-data-noise   (canvas fp noise — valuable; needs the flag infra patches too)

The 95 that applied cover the core: no Safe Browsing, no field trials, no metrics/UMA,
no crash reporting, disable-gcm, disable-rlz, strip Google API keys, replace-google-* URLs.
