# Third-party notices — LLPlayer.Avalonia (Linux app)

LLPlayer.Avalonia is part of LLPlayer (GPL-3.0, see `../LICENSE`). It contains or is derived from the following
third-party material. NuGet package dependencies (Avalonia, CommunityToolkit.Mvvm, ...) keep their own licences and are
not copied into this folder.

## shadcn/ui — design tokens (MIT)

The colour, radius, spacing and typography tokens in `Themes/ShadcnTokens.axaml` (generated) and the component
variants in `Themes/Controls/*.axaml` follow shadcn/ui's "neutral" theme and component classes. No shadcn/ui source code
is copied; the OKLCH token values were taken from:

- repository: https://github.com/shadcn-ui/ui
- file: `apps/v4/registry/themes.ts`, entry `neutral` (same values as `apps/v4/public/r/colors/neutral.json`,
  `cssVarsV4`); radius ratios from `apps/v4/app/globals.css` (`@theme inline`)
- commit: `98a1fe67b439324ddc857f47fbdce056600a4329`

`destructive-foreground` is not part of the v4 neutral theme; LLPlayer uses `oklch(1 0 0)` (the v4 Button
"destructive" variant renders its label with `text-white`). Conversion to sRGB: `Themes/tools/oklch_to_hex.py`.

```
MIT License

Copyright (c) 2023 shadcn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Lucide icons (ISC; Feather-derived icons MIT)

`Themes/Icons.axaml` contains path data of these Lucide icons (the SVG `rect` / `circle` / `line` elements were
converted to equivalent path data): captions, check, chevron-down, chevron-right, circle-alert, copy, folder-open, gauge,
info, keyboard, languages, maximize, minimize, moon, panel-right, pause, play, search, skip-back, skip-forward, square,
sun, volume-2, volume-x, x — from https://github.com/lucide-icons/lucide, commit
`66d8f9fc394b8530377e5f6112f0b8908ba01280`. check, chevron-down, chevron-right, info, maximize, minimize, moon, search,
square and x are derived from the Feather project.

```
ISC License

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

```
The MIT License (MIT) (for the Feather-derived icons listed above)

Copyright (c) 2013-present Cole Bemis

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Avalonia FluentTheme (MIT) — referenced, not copied

The control styles override FluentTheme resource keys (e.g. `SliderTrackFill`, `MenuFlyoutPresenterBackground`) and
target template part names of Avalonia 12.1.3's Fluent templates; no Avalonia template code is copied. ShadUI /
ShadowUI code is not used.

## Inter font (SIL Open Font License 1.1)

Shipped by the `Avalonia.Fonts.Inter` NuGet package (not copied here).

## FFmpeg 8.1 shared libraries (GPL) — bundled in the Linux package

The Linux package ships FFmpeg 8.1 shared libraries in `FFmpeg/` (they are not in this repository; they are fetched by
`scripts/linux/fetch-ffmpeg.sh`). They are the GPL build `ffmpeg-n8.1-latest-linux64-gpl-shared-8.1.tar.xz` from
[BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds), sha256-verified against that release's
`checksums.sha256`. The package records the exact asset and its sha256 in `FFmpeg/SOURCE.txt` and carries the FFmpeg
license text in `FFmpeg/LICENSE.txt`. FFmpeg source: <https://git.ffmpeg.org/ffmpeg.git> (the n8.1 build follows the
`release/8.1` branch); build recipe: <https://github.com/BtbN/FFmpeg-Builds>. A formal GPL source offer for
release packages is part of the Linux release integration (backlog F-13, parity item 11).
