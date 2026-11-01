# F-13 configured online video implementation plan

> **For agentic workers:** use Superpowers TDD and scoped subagent implementation/review. Owner approved this complete block on 2026-11-01.

**Goal:** Open one online video through explicitly configured external yt-dlp/Deno from the same Windows/Linux Avalonia app.

**Architecture:** Preserve the WPF plugin target and legacy defaults. Add a portable target and explicit extractor admission, reuse existing stream/caption mapping and app playback pipeline. Each extraction owns its process/cancellation/metadata; direct media never depends on the extractor. Both Avalonia packages ship managed plugin files only.

**Tech Stack:** C#/.NET 10, FlyleafLib, Avalonia, xUnit v3, existing PowerShell/Bash publishers.

**Spec:** the owner-approved scope and constraints below, following the read-only proposal and independent proposal review in this chat.

## Global constraints / approved design

- One shared Avalonia UI/Core on native Windows/Linux; WPF remains supported.
- External standalone yt-dlp and Deno paths are unset by default, validated absolute executable paths and saved only explicitly. No download, install, update, startup process probe or PATH fallback for either tool.
- Explicit Open online URL plus Cancel. Ordinary local/direct HTTP media, including extensionless URLs and safe Ctrl+V, retain their previous route when tools are absent or invalid.
- Preserve existing automatic A/V choice, 720p option default, current VTT automatic-caption/language policy, chapters and request headers. No format editor, authored-caption expansion, cookies/auth, playlist/DRM promise or arbitrary portable ExtraArguments.
- Portable invocation uses structured argv, isolated helper configuration and normal TLS verification. Preserve legacy WPF options/flags and separate WPF publish profile.
- Cancellation, replacement and shutdown terminate the owned extraction with bounded cleanup; old work cannot publish into new playback. No second app-side process queue.
- Runtime tools, user configuration, generated files, logs and packages are never committed. Keep product and agent/tooling changes in separate scoped commits.
- Native/live URL playback acceptance is explicitly deferred by owner; no GUI/player/model launches. Unit/headless/CI are not native acceptance.
- Do not touch retained worktrees, previous payloads, original WPF shortcut or unrelated dirty files.

## Review focus

- Missing/invalid helpers must not hijack ordinary HTTP media.
- Stalled extraction must release replacement and close; repeated identical URL requests remain distinct.
- Nonzero exit, malformed/empty/locked metadata and late callbacks must not leave processes/temp files or publish stale results.
- Spaces/Unicode in tool paths and hostile URLs must preserve argument boundaries and configuration isolation.
- Both archive/plugin targets must load correctly; WPF native/package constraints stay enforced.

## Task 1 — portable plugin and deterministic regressions

Files: Plugins/YoutubeDL/**; FlyleafLib/Platform/Portable online-request contract if needed; FlyleafLibTests/YoutubeDL* and its project reference.

- [ ] Add a compiled behavioral RED before implementation; preserve its complete log separately from infrastructure failures.
- [ ] Dual-target plugin, isolate Visual Studio WPF copy target, provide shared portable request/options contract for host integration.
- [ ] Reuse stream/caption mapping; implement isolated per-run extraction, explicit admission and bounded cancellation/process cleanup.
- [ ] Cover admission/direct-media, argv/config/TLS, missing tools, metadata/A/V/VTT/headers/chapters, stalled replacement/cancel/close and failures.
- [ ] Run focused GREEN and independent runtime/.NET review.

## Task 2 — shared host settings, URL command and cancellation

Files: LLPlayer.Avalonia/** and LLPlayer.Avalonia.Tests/**.

- [ ] Use Task 1 contract; wire packaged plugin before player config, keep explicit extraction separate from existing Open.
- [ ] Add detached explicit-save tool settings, Open online URL and visible Cancel; bridge existing mutation cancellation rather than a parallel queue.
- [ ] Add compiled behavioral RED/GREEN for settings persistence/errors, URL routing, direct media without helpers, request supersession and cancellation.
- [ ] Independent UI/.NET/runtime review.

## Task 3 — packaging, frozen checks and contracts

Files: scripts/windows publish/package validators/tests; scripts/linux publish/verify as needed; scripts/codex/verify-frozen.ps1; matching docs/agent contracts, status and canonical backlog.

- [ ] Publish portable YoutubeDL separately into Plugins/YoutubeDL; require correct managed files and reject helper/runtime binaries and user data in ZIP/TAR.
- [ ] Extend positive/negative/load/archive tests, explicit frameworks/RIDs and Linux casing. Preserve WPF release action/profile.
- [ ] Narrow frozen-validator change enforces both retained WPF and new portable TFM; retain all other restrictions.
- [ ] Record additive config/media/product/dependency/manual-smoke contracts and native NOT_RUN boundary.
- [ ] Independent packaging/native/infra/instruction/reuse reviews.

## Task 4 — verification, delivery and closeout

- [ ] Full unfiltered WPF verify.ps1 and ship.ps1; verify-fast.ps1 for infrastructure changes.
- [ ] Windows verify-avalonia.ps1, test-package-avalonia.ps1 and publish-avalonia.ps1.
- [ ] Actual Linux full verify.sh and publish.sh via available Linux host or fresh CI; never infer results from Windows or old CI.
- [ ] Final independent spawned /review; resolve Critical/Important and rerun affected checks.
- [ ] Scoped commits, authorized push/PR, exact-head required checks, fresh head/open-PR readback, merge/readback, actual-main Windows/Linux push CI.
- [ ] Clean final-main default fresh Desktop updater; verify sourceCommit, immutable payload hashes, current.json, direct exe/empty args/payload cwd/blue icon; preserve previous payloads.
- [ ] Exact-allowlist Second Brain sync, completed session + one month entry/recent window + live index, scoped session-close gate and local commit. No Vault push/dashboard publication/memory writes.
- [ ] Durable closeout with fresh evidence and explicit native NOT_RUN.
