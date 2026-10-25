# Windows Avalonia test-shortcut icon

`llplayer-avalonia-blue.ico` is a blue variant of the existing root `LLPlayer.png`, for the local Windows Avalonia
desktop-test shortcut. The original WPF icon and shared Windows/Linux application branding are unchanged.

The PNG edit used the built-in `image_gen` tool, followed by one targeted transparent-exterior cleanup edit.
Pillow only downsampled the selected result to 256 pixels and encoded 32-bit DIB/BMP ICO frames at 16, 24, 32, 48, 64, 128 and 256 pixels.
It did not recolor, crop, mask or redraw the image. The 256-pixel ICO frame decodes to exactly the PNG pixels.
The frames omit the 1-bpp AND mask (each `bytesInRes` is `40 + 4*w*h` with `biHeight = 2h`); Windows 10/11 relies on
the 32-bpp alpha channel and renders them correctly. This container deviation is recorded for completeness and is not
the established cause of the earlier white icon; see `icon-provenance.json`.

Full generation prompts, source/output hashes and verification facts are in `icon-provenance.json`.
The speech-bubble silhouette, white play triangle, neutral shadow, blue gradient and transparency were inspected visually.
This generative variant is not claimed to be pixel-identical to the pink source.

At the earlier DIB-conversion checkpoint on 2026-10-25, the ICO was re-encoded from the unchanged PNG with `bitmap_format="bmp"` after a user report that Explorer Desktop displayed a generic paper icon for a correctly launching shortcut. All frames use a 40-byte DIB header. At that checkpoint the cause was not proven and the desktop retest was `NOT_RUN`. Earlier managed payload icons are preserved by the installer.

A subsequent native `GetFinalPathNameByHandle` diagnosis found that Codex MSIX virtualized the logical AppData executable and icon paths into physical `OpenAI.Codex` LocalCache paths. Explorer could not resolve the icon through its logical AppData name. The shortcut was repaired to use its physical icon path and physical executable target. The local [path-fix receipt](../../../artifacts/windows-avalonia-desktop-test-build-2026-10-25/desktop-msix-path-fix.json) records that repair; user confirmation is recorded in the local [manual checkpoint](../../../artifacts/windows-avalonia-desktop-test-build-2026-10-25/user-manual-checkpoint.json).

The user had already confirmed direct shortcut startup and Open File Location, then confirmed that the repaired icon became blue. These results apply only to payload `4756c2ea357a-20261025T135110782Z-57b566a3` and its shortcut repair. They do not establish DIB encoding as the cause or cure. At this checkpoint, a fresh schema-2 deployment under the new `%USERPROFILE%/Applications/LLPlayer-Avalonia-TestBuild` default with a direct physical executable target was still pending separate verification. Full GUI, DPI, media, subtitle, real-model, GPU and Windows/Linux parity acceptance remains outside this icon checkpoint.
