# Windows Avalonia test-shortcut icon

`llplayer-avalonia-blue.ico` is a blue variant of the existing root `LLPlayer.png`, for the local Windows Avalonia
desktop-test shortcut. The original WPF icon and shared Windows/Linux application branding are unchanged.

The PNG edit used the built-in `image_gen` tool, followed by one targeted transparent-exterior cleanup edit.
Pillow only downsampled the selected result to 256 pixels and encoded ICO frames at 16, 24, 32, 48, 64, 128 and 256 pixels.
It did not recolor, crop, mask or redraw the image. The 256-pixel ICO frame decodes to exactly the PNG pixels.

Full generation prompts, source/output hashes and verification facts are in `icon-provenance.json`.
The speech-bubble silhouette, white play triangle, neutral shadow, blue gradient and transparency were inspected visually.
This generative variant is not claimed to be pixel-identical to the pink source.
