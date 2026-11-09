# F-13 Commands, service screens and media backends — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan with explicit spawned owners and independent reviewers. Steps use checkbox syntax. This is one coordinated owner-approved product block, one integration and delivery ritual; modules below are ownership boundaries, not new backlog cards or separate product stages.

**Goal:** Complete the existing F-13 command/service-screen and hardware-decode/GPU-presentation/capture implementation in the shared Windows/Linux Avalonia UI while preserving supported WPF and current defaults.

**Architecture:** Reuse live engine/app command owners for palette/help/menu, add small shared service views, and extend portable Flyleaf with FFmpeg hardware decoding plus a timed planar OpenGL presentation seam and structured capture adapters. Keep software playback as the default and reliable fallback; expose requested, initialized/effective and unavailable state separately. Settings save before live apply; decode preferences take effect on the next actual decoder open, whereas presentation may switch live.

**Tech Stack:** C#/.NET10; existing Avalonia12.1.3/Fluent/shadcn/CommunityToolkit; Flyleaf.FFmpeg.Bindings8.0.1 and existing native payloads; Avalonia.OpenGL and existing ANGLE. Preserve WPF/Prism/DryIoc and its packaging.

**Spec:** Existing `docs/agent/backlog.md` F-13 and current owner sequence. Preparation references: `artifacts/f13-second-block-2026-11-02/{SCOPE,MEDIA-DESIGN,API,NATIVE-PREFLIGHT,NATIVE-MATRIX}.md`. API.md SHA256 `830595e91d1866a1ebd2fba10c91ace2e860f8664aede2d176fcb80609f4eddc` identifies the historical preimplementation API note; current source and the owner steering below govern; concrete decisions needed to execute it are retained below. FIRST delivery completed at main28ced28d with fresh main CI/Desktop and scoped Vault commit75778a13. This plan implements the already approved SECOND block; the preparation references are supplemental and the decisions below travel with this plan.

## November 9 disposition

Tasks1–5 implementation is complete and independently reviewed in frozen source03 (130files), including
strict capture parsing, explicit file-URI diagnostic redaction and constant-time UI-published debug counts.
The unchecked historical planning steps below do not authorize reimplementation. Task6 delivery is evidenced
by fresh `nov9-*` receipts: full WPF and Windows gates, guarded ship smoke, package contracts, QA publish and
actual plugin checks. The final GitHub/main-CI/Desktop/Vault identities belong to the delivery closeout/PR.
Full-F13 Computer Use and native acceptance still require separate owner agreement; that section stays deferred.

## Owner steering — 2026-11-03

Resumed from the verified 87-file pause checkpoint. Complete this same SECOND block and its ordinary full
delivery ritual. The newer owner instruction defers Computer Use on the entire F-13 to separate agreement;
it supersedes every older native-execution authorization below. No player launch or Computer Use in this delivery.

Integration ruling: `VideoHardwareDevice` persists the stable `PortableVideoAdapterInfo.Id`; only the actual
decoder policy resolves that identity to the current native `DeviceSelector`. UI selection/rebuild and tests
must use that same identity. This corrects the preimplementation API note calling the persisted value a native
selector; those new fields have not shipped. Unavailable/cross-OS identities remain retained rather than guessed.

## Global constraints

- FIRST's full ritual is complete at main28ced28d (PR198+199). Existing GO authorizes SECOND and its full delivery; full F-13 Computer Use now awaits separate owner agreement.
- One shared UI/Core on Windows/Linux, WPF supported. No new provider/recorder/installer/platform card; preserve the reviewed PCM correction and existing subtitle/audio/input/language behavior.
- `PortableDecodeMode=Software`; `PortableHardwareDevice=""`. Host defaults: `VideoHardwareAccelerationEnabled=false`, remembered `VideoHardwareDecodeMode=Automatic`, `VideoHardwareDevice=""`, `VideoPresentationMode=Software`.
- Keep legacy `Config.Video.VideoAcceleration` default/present value unchanged on startup, disabled request and unrelated Save, including its existing true default. New Software mode suppresses hardware. Only explicit enable/retained Enabled=true ensures the legacy allowance is true; disable selects Software without forcing the legacy bit false.
- Preserve `FFmpegLoadProfile.Filters`. Capture requires the actual `All` startup profile/avdevice support and honest restart/unavailable UI; no lazy profile reload, startup capture probing or implicit setting change.
- Hardware preference changes do not reopen media, reset generation/position/play-pause/subtitles or alter audio. GL presentation selection applies live after successful save without waiting for UI under a native lock.
- Existing FFmpeg avdevice/hardware exports and ANGLE already ship. No new FFmpeg/GL download or dependency upgrade. If needed, root adds only focused conditional Windows Vortice.MediaFoundation/DXGI3.7.6-beta and required managed closure/notices, excluded on Linux.
- Root is the sole .NET/gates/Git/GitHub/Desktop/Vault runner. No concurrent builds. Workers are not alone, must preserve others' files and remain within explicit ownership.
- Deterministic compiled RED where a safe seam exists; missing-type/compiler/analyzer failures are not behavioral RED. Keep original FAIL/UNKNOWN evidence. Real driver/device/shader/GUI acceptance waits until AFTER SECOND ritual.

## Review focus

1. Rapid hardware toggles or failed persistence must not diverge checked/saved/config state; default and rebound engine delegates, palette/help and menu must use the same fresh admitted mutation. Pin in root integration tests.
2. Early planar preparation must not publish before timed Present; snapshot/preprocessor reuse between preparation and presentation must not invalidate pixels. Pin owned pending-frame and timing tests.
3. GL unavailable/context loss/clear/detach must not resurrect an old frame, leak a frame/texture or leave a black selected view; retain the same native-resolution snapshot source. Pin GL/mailbox and real generated-media fallback tests.
4. Native capture identity containing Unicode, `&`, `?`, `=`, `%` or `:` must round-trip without becoming an option or another device; stale/duplicate identities and untyped V4L2 entries must be handled accurately. Pin URL/parser/catalog tests.
5. New/clone/restart and service dialogs must preserve current batch/resource/ASR drain and singleton/owner behavior; failed spawn leaves the old application alive. Pin injected process/lifetime and close-before-activate tests.

## Files and exclusive ownership

| Owner | Exclusive responsibility/files | Consumes / hands off |
|---|---|---|
| Root integration | `FlyleafLib/Engine/Config.cs`; new portable `MediaRenderer/PortableVideoBackendContracts.cs`; `IPlaybackController.cs`, `FlyleafPlaybackController.cs` and new `.MediaBackends.cs`; shared `PlaybackSettings.cs`; AppHost/MainWindow/VM/IAppShell/AppOptions integration; csproj/publishers/notices; shared Demuxer fmt parser unless explicitly delegated | Seeds stable contracts, single action resolver/persistence owner; composes all modules and runs gates |
| Commands/service worker | New `Services/ExecutableCommands.cs`, palette/About/debug/process-planning services and tests; new palette/About/debug views/VMs; existing ShortcutActionCatalog/AppShortcuts and CheatSheet VM/view | Root supplies actual action resolver, app command owners and process/lifetime host; worker does not edit root controller/MainWindow/AppHost |
| Hardware worker | `MediaFramework/MediaDecoder/VideoDecoder.cs`; hardware/fill/reset sections of `Platform/Portable/MediaRenderer/Renderer.Portable.cs`; new portable hardware decoder/catalog partials and engine tests; narrow Windows adapter implementation agreed with root | Root config/contracts; publishes immutable DecodeStatus and real native adapter catalog. Does not edit Convert, VideoView, settings or shared Config |
| GL worker | `Renderer.Portable.Convert.cs`; new `Renderer.Portable.Planar.cs`, `IVideoPlanarSurface.cs`; `VideoView.cs`, new GL child/mailbox and tests | Root public statuses/settings; existing renderer private fields via partial; never edits GPU-owned Renderer.Portable.cs/VideoDecoder or root MainWindow |
| Capture worker | Portable Engine.Video/Audio capture sections; portable device/catalog/format contracts/providers; additive DeviceBase overloads; Windows MF/Linux V4L2 format adapters; capture tests and URL codec | Root handles shared fmt parser and dependencies. Preserve existing output recovery and WPF constructors; coordinate any parser protocol before edits |
| Optional settings UI worker | Existing PlaybackSettingsViewModel/window plus new `.MediaBackends.cs` partials/UI sections and their tests, if root explicitly transfers these files | Consumes root-owned settings/runtime/controller APIs; does not introduce another persistence owner |

Root publishes the declarations first, then tells writers they are stable. If two modules need an owned file, coordinate a narrow transfer; never independently implement competing APIs.

## Task 1: Seed contracts and lock preservation tests

**Files:** root-owned contracts/config/settings/controller; tests `MediaBackendSettingsTests.cs` and `MediaBackendCommandRoutingTests.cs` in Avalonia.Tests, plus portable config/selection tests in FlyleafLibTests. Extend existing types rather than building another config framework.

**Interfaces:** public enums `PortableVideoDecodeMode { Software, Automatic, D3D11VA, VAAPI }` and `PortableVideoPresentationMode { Software, OpenGL }`; immutable `PortableVideoDecodeStatus`, `PortableVideoPresentationStatus`, `PortableVideoAdapterInfo` with exactly API.md fields. EffectiveMode is never Automatic. Snapshot getters perform no native probe. Root exposes `Action? ResolveEngineAction(KeyBindingAction action)` through the playback controller and `Task ToggleVideoAccelerationAsync()`; workers consume this resolver for direct engine execution.

- [ ] Add compiling contract declarations and safe injectable seams, then tests named `Old_json_and_unrelated_save_preserve_legacy_true_and_software_mode`, `Disabled_request_retains_backend_device_and_legacy_allowance`, `Queued_toggles_read_fresh_state_and_failed_save_changes_nothing`, and `Default_and_rebound_commands_use_the_same_hardware_owner`. Assert saved request/config/effective state separately and original chord/action/order metadata equality.
- [ ] Root runs focused tests and retains genuine failing assertions before implementation. No native device creation is required to prove these contracts.
- [ ] Implement per-field fresh settings merge/validation/persist-before-apply. Hardware owner computes the inverse inside existing serialized admission; reuse private save/apply internals rather than recursively queueing public Save. Install the resolver both at startup and on every engine binding rebuild; only ToggleVideoAcceleration overrides the existing engine delegate.
- [ ] Add `Hardware_preference_save_does_not_reopen_or_mutate_both_translated_slots_or_pcm` using the real generated-media seam. Decoder consumes new request only on next real Setup. Software default/disabled paths invoke no hardware factory. Pin GREEN before releasing integrations.

## Task 2: Commands and service screens

**Files:** commands/service owner table above; root integrates its exports in MainWindow/AppHost. Reuse existing ShortcutActionDescriptor/ShortcutInfo and effective engine/app binding snapshots. A command row carries actual `ICommand`/availability plus action identity/description/group/current chords; it never synthesizes key events. Root's resolver handles the hardware enum specially.

- [ ] Write compiled behavioral tests for own-palette exclusion, disabled/current rows, description/action/chord search, first result selection, Enter/double-click activation once, Esc cancellation and close-before-opening an owned singleton. Include effective-binding refresh with the existing filter preserved; metadata alone never adds a default chord.
- [ ] Implement one live command projection shared by palette/help/menu. Reconcile all existing WPF CustomKeyBindingAction names with already implemented shared VM actions: subtitle/copy/sidebar/local navigation/windows/recognition-language workflows and app lifecycle. Expose existing Record/Snapshot commands and recording/availability state explicitly. Reuse actual app owners; do not reimplement prior language/settings workflows.
- [ ] Extend F1 help with executable keyboard rows and complete Mouse guidance from SCOPE's WPF list. Show current pref-dependent click/double-click/wheel rows; document only real shared handlers/accepted DIP adaptation. Test guidance conditions and keep current hotkey refresh semantics.
- [ ] Implement shared About from actual executable informational identity/runtime/architectures, copy and safe project/dependency/notices links; credits distinguish active Avalonia dependencies from WPF-only libraries. Debug reads immutable current player/decoder/audio/subtitle/renderer snapshots at the existing UI refresh cadence, labels unsupported D3D-specific values and triggers no device/provider probe.
- [ ] Implement injectable new/clone/restart process plans using actual apphost or dotnet+assembly as applicable, ArgumentList and original media URL. Tests assert argument fidelity and that failed spawn cannot request exit; successful restart uses existing explicit drain. Root connects real spawn/exit only after review. Add close-during-work/service-owner regression and focused GREEN.

## Task 3: Genuine portable hardware decoding

**Files:** hardware owner table. **Consumes:** root portable config/status types. **Produces:** `Renderer.DecodeStatus`, native adapter catalog; internal `PrepareHardwareDecoding()` at start of portable VideoDecoder.Setup, `HardwarePixelFormat`, `HardwareDeviceType`, `ConfigHWFrames(AVCodecContext*)`, and owned existing ffDevice/ffFrames references. WPF retains its old branches/constants/no-argument ConfigHWFrames.

- [ ] Pin compiling selection/lifetime regressions: Software creates no context; matching backend/pixel format and codec cache keys; scan fallback from original pix-format pointer; create/init/transfer failure drains exactly owned refs and triggers one bounded software fallback, never an opaque CPU frame. Assert HardwareActive only after negotiated frame decoding/transfer, not catalog/context success.
- [ ] Implement Windows D3D11VA/D3d11 and Linux VAAPI/Vaapi using existing FFmpeg APIs. Use actual catalog identity/device selector; an incompatible retained OS selector is visible/unavailable and is never guessed into another device. Capture each new request at real codec Setup, not renderer construction. Cache keys include codec/backend/format for automatic and named codecs.
- [ ] GetFormat uses avcodec_get_hw_frames_parameters on the actual callback context, initializes/ref-counts the real pool and never guesses NV12/depth. Hardware output transfers via av_hwframe_transfer_data plus av_frame_copy_props BEFORE VideoFrame/VideoCache admission. Preserve PTS/color/field metadata and a usable CPU frame for filters/snapshots.
- [ ] Exercise failure/stop/reopen/disposal and generated-media software regressions with injectable native API outcomes. Release codec/frame/device refs in the existing drained order; avoid reentrant reset inside FFmpeg callbacks and per-frame retry loops. Root records focused GREEN; actual driver acceptance remains NOT_RUN until final native stage.

## Task 4: Timed planar OpenGL presentation and fallback

**Files:** GL owner table. **Interfaces:** `IVideoPlanarSurface : IVideoSurface`, `CanPresentPlanar(PlanarVideoPixelFormat)` and `TryPresentPlanar(in PlanarVideoFrame, in VideoPresentationParameters)`. Formats Yuv420P/Nv12; borrowed ref-struct planes have Bytes/Width/Height/RowBytes/positive RowStride/BottomUp. NV12 chroma Width counts pairs, RowBytes=2*ceil(width/2); YUVJ maps Full range. Metadata reuses CropRect and existing Transpose/FlipX/FlipY/ColorSpace/ColorRange. Root connects `VideoView.PresentationMode` and `PresentationStatus` after save.

- [ ] Pin RED tests for odd/padded/bottom-up planes and checked bounds, YUV/range/geometry parity, accepted default-color SDR bypassing CPU BGRA, no host call before PresentPlay, snapshot/preprocessor reuse while pending, context change at Present and clear/detach epochs. A fake GL proves ownership/control flow only.
- [ ] ConvertFrame prepares but never publishes: keep one reusable owned pending AVFrame ref plus metadata and None/Bgra/Planar discriminator. At existing timed PresentLocal, borrow validated planes from that owned ref. If capability changed, convert THAT SAME prepared picture through software at that boundary. Release pending ref on replacement/Clear/ReleaseFrameState/SwsDispose. Keep Frames.RendererFrame and normal cache ownership unchanged.
- [ ] Implement common Avalonia GL child/mailbox: copy accepted planes before returning, reusable bounded latest-frame slots, no UI wait under native locks, generation-safe clear/retire. GL calls only in init/render/deinit callbacks to supplied FBO, compatible actual GL/GLES shaders and textures; context loss retires handles safely. Use existing Avalonia/ANGLE packages.
- [ ] Common 8-bit default-color SDR performs shader YUV conversion/crop/orientation/scaling without CPU BGRA/WriteableBitmap. Route nondefault color controls to existing CPU preprocessing unless exact nonlinear software parity is proved. Unsupported HDR/high-depth/formats/context use truthful fallback; do not claim native HDR/D3D VP/VSync/vendor super-resolution equivalence or zero-copy.
- [ ] Expose `Renderer.RequestSurfaceRefresh()` via existing scheduler for ready/lost/live-mode changes. Preserve geometry/pointer/overlay contracts, available image on failed GL and native-resolution snapshot/record source. Test playing/paused software→GL-requested→fallback/software snapshots, same-frame fallback and release; focused GREEN before integration.

## Task 5: Real capture catalogs, formats and safe open protocol

**Files:** capture owner table; root shared `Demuxer` parser addition and host controller. **Interfaces:** `CaptureDeviceKind`, `CaptureAvailability`, immutable `CaptureDeviceInfo`, `CaptureVideoFormatInfo`, `CaptureDeviceSnapshot`; `CaptureInputUrl.Build(device, format?)`; parameterless existing Engine.Video/Audio.RefreshCapDevices plus CancellationToken overload and read-only CaptureSnapshot. DTOs carry native Id/Label/Backend/Input/URL; formats have exact rational FPS/dimensions/codec-pixel option/stable identity.

- [ ] Pin compiled RED for absent profile/driver versus Failed versus successful empty Available; known untyped v4l2 is video and untyped Pulse/ALSA audio, while untyped dshow is not guessed. Test no startup/eager per-camera format query and late refresh cannot replace newer/closed state.
- [ ] Use existing avdevice_list_input_sources/free_list_devices with real input-format discovery. Windows dshow; Linux video4linux2 and actual available Pulse/ALSA inputs. No helper executable or global FFmpeg log interception. Preserve output-device/recovery owner.
- [ ] Add portable factory/overload supplying device URL/initial formats without the WPF eager constructor; WPF constructors/behavior remain. Explicit device expansion queries Windows MF structured formats or Linux x64 V4L2 ioctl formats, exact FPS numerator/denominator and truthful discrete/stepwise ranges; safe handles/error/permission mapping. Keep default open valid when no specific formats exist; never fabricate formats or ambiguously match duplicate friendly names.
- [ ] Agree additive escaped fmt representation with root BEFORE code. Root tests/implements shared parser while retaining every legacy fmt URL. Builder/parser tests round-trip Unicode/spaces/ampersand/question/equals/percent/colon input and options, reject malformed representations and preserve backend/device identity. Default and per-format open share this codec.
- [ ] Root refreshes/opens in existing serialized admission, drains noninterruptible native work, publishes immutable revisions and validates choice token/revision/native identity before open. Test stale device/format rejection without retargeting and normal media unaffected; focused GREEN. Physical source execution stays NOT_RUN before final native stage.

## Task 6: Integrate, review and deliver the one SECOND block

- [ ] Root (or explicitly delegated settings UI owner) adds requested backend/device/presentation controls and read-only initialized/effective/pending/fallback lines to existing playback settings. Preserve detached validation/Close/Esc/save-failure retry/fresh unrelated audio/subtitle/runtime fields. No saved request is rewritten because a driver/context fails. Bind menu/key/palette to the same persisted toggle, shared capture entries and existing Record/Snapshot owners.
- [ ] Root integrates all views/AppHost/lifetime, then runs focused command/settings/native-ownership/media regressions serially. Preserve full original stream/chapter/PCM/translated-slot, filters/snapshot/record, modal/input/language regressions. Fix root causes; do not relax timeouts/predicates or retry unchanged FAIL without investigation.
- [ ] Run `scripts/codex/verify-fast.ps1` and full WPF `scripts/codex/verify.ps1`; Windows `scripts/windows/verify-avalonia.ps1`, `test-package-avalonia.ps1`, actual `publish-avalonia.ps1`; Linux full `scripts/linux/verify.sh` and `publish.sh` via actual available host/fresh CI. Use no fabricated Linux local run. Counts are observations, never hard-coded gates.
- [ ] Review actual stage/archive/extracted payload, hashes/content/architecture/licenses and conditional Windows managed dependency closure; Linux excludes Windows adapter assemblies. Actual packaged YoutubeDL plugin loads against stage/extracted portable engine on both OSes. Keep immutable first failures, exact source/evidence and package identity separate from runtime acceptance.
- [ ] Explicit spawned reviews in bounded waves: runtime, .NET, UI, native/dependencies, packaging, contracts/config, reuse/dead code, instructions/stack and final verification_reviewer. Use every required available specialist role; fix all Critical/Important findings. Root verifies final source matches tested/reviewed bytes; reviewed product and docs/tooling commits stay separate.
- [ ] GitHub plugin: fresh target/head checks; PR for this complete block, Windows/Linux checks, independent final review, merge with expected reviewed head. Verify distinct actual-main push CI, without relabeling synthetic PR results. Failures are investigated and corrected through fresh reviewed commits rather than hidden by rerun.
- [ ] On clean final actual main run default fresh `scripts/windows/update-desktop-test-build.ps1`; verify receipt sourceCommit/new payload matches final HEAD, direct blue EXE/no args/payload cwd/physical path/current.json and preservation of earlier payloads/WPF shortcut where observed. Updater neither launches nor stops player.
- [ ] Scoped Second Brain sync by protocol: exact allowlist/living index+Tests/completed session/month index/reference, preserve foreign/dirty/frozen state; actual evidence review, session-close and local Vault commit. No Vault push/Dashboard/Codex-memory writes. Mark SECOND ritual complete only after these actual operations succeed.

## After SECOND ritual: full F-13 native acceptance awaits separate owner agreement

- [ ] Read Computer Use skill and discover actual enabled surfaces. Use the final installed payload and `NATIVE-MATRIX.md` group-by-group for all delivered F-13, including FIRST language scope and earlier settings/subtitle/playlist/input deliveries; compare WPF where meaningful. Preserve owner config/media/models/processes; use recorded isolated acceptance fixtures/profile and test-owned instances.
- [ ] Record visible, functional and persisted outcomes separately with payload/source/platform/steps/evidence. Genuine D3D11VA, VAAPI, GL shader/context, capture inputs, model/provider, Anki, physical audio/input and native Linux require their actual resources. Missing resource/host is explicit NOT_RUN; a CI/package/fake success cannot close native acceptance.
- [ ] Reproduce any actual parity defect, tie it to the existing requirement, add deterministic regression where safe, fix, rerun relevant/full required gates, independent /review and delivery/Desktop/Vault, then retest the new payload while retaining first FAIL. No new feature cards or speculative expansion.
- [ ] End with shutdown/reopen/output/state-preservation checks and a complete matrix of PASS/FAIL/NOT_RUN with exact reasons. Remaining unavailable native rows prevent a claim of full accepted native parity. WPF retirement, Wayland/AppImage/Flatpak/Linux release-source-offer remain separate owner decisions.

## Plan self-review

Coverage: SCOPE command/service and hardware/capture requirements map to Tasks1–6; API default/toggle/next-open/timed-frame/capture rules are explicit; NATIVE-PREFLIGHT requires no replacement FFmpeg/ANGLE and conditional managed closure only; NATIVE-MATRIX owns the later full acceptance. Ownership avoids concurrent edits to Renderer.Portable.cs versus Convert/Planar, Config/controller versus workers and shared Demuxer versus URL builder. All five Review Focus cases have named assertions in their owning task. Implementation remains NOT_STARTED at draft preparation; this plan does not assert tests/gates/native PASS.
