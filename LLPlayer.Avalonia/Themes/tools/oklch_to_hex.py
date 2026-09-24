#!/usr/bin/env python3
"""Convert the shadcn/ui "neutral" theme tokens (OKLCH) to sRGB hex for the LLPlayer.Avalonia theme.

Source of truth (MIT, (c) shadcn): https://github.com/shadcn-ui/ui
  file   apps/v4/registry/themes.ts  (entry name "neutral"; identical to apps/v4/public/r/colors/neutral.json cssVarsV4)
  commit 98a1fe67b439324ddc857f47fbdce056600a4329 (main, fetched 2026-09-24)
The radius ratios (sm = 0.6, md = 0.8, lg = 1, xl = 1.4 x --radius) come from apps/v4/app/globals.css
(`@theme inline`) at the same commit.

Conversion (exact, no lookup tables):
  OKLCH -> OKLab (a = C cos h, b = C sin h) -> LMS (cube of Ottosson's M2^-1) -> linear sRGB (M1^-1)
  -> sRGB transfer function (IEC 61966-2-1) -> per-channel clip to [0, 1] (the neutral palette is in gamut;
  clipping matches Chromium's CSS behaviour for the two red tokens, which are in gamut as well)
  -> 8-bit channel = floor(v * 255 + 0.5)  (round half up; documented so the C# test can reproduce it)
  Alpha ("/ 10%") -> floor(a * 255 + 0.5) and the colour is written as Avalonia #AARRGGBB.

Usage:
  python3 oklch_to_hex.py            # rewrite shadcn-neutral.tokens.json and the generated regions of
                                     # ../ShadcnTokens.axaml and ../ShadcnTheme.axaml (Fluent palettes)
  python3 oklch_to_hex.py --check    # exit 1 if any generated file is out of date
"""

import json
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
THEMES = os.path.dirname(HERE)

SOURCE = {
    "repository": "https://github.com/shadcn-ui/ui",
    "file": "apps/v4/registry/themes.ts",
    "theme": "neutral",
    "commit": "98a1fe67b439324ddc857f47fbdce056600a4329",
}

# Token -> (dark, light), copied verbatim from the "neutral" entry of apps/v4/registry/themes.ts.
# destructive-foreground is not part of the v4 neutral theme: the v4 Button "destructive" variant renders its
# label with `text-white`, so LLPlayer defines it as oklch(1 0 0) in both themes.
TOKENS = {
    "background":                 ("oklch(0.145 0 0)",           "oklch(1 0 0)"),
    "foreground":                 ("oklch(0.985 0 0)",           "oklch(0.145 0 0)"),
    "card":                       ("oklch(0.205 0 0)",           "oklch(1 0 0)"),
    "card-foreground":            ("oklch(0.985 0 0)",           "oklch(0.145 0 0)"),
    "popover":                    ("oklch(0.205 0 0)",           "oklch(1 0 0)"),
    "popover-foreground":         ("oklch(0.985 0 0)",           "oklch(0.145 0 0)"),
    "primary":                    ("oklch(0.922 0 0)",           "oklch(0.205 0 0)"),
    "primary-foreground":         ("oklch(0.205 0 0)",           "oklch(0.985 0 0)"),
    "secondary":                  ("oklch(0.269 0 0)",           "oklch(0.97 0 0)"),
    "secondary-foreground":       ("oklch(0.985 0 0)",           "oklch(0.205 0 0)"),
    "muted":                      ("oklch(0.269 0 0)",           "oklch(0.97 0 0)"),
    "muted-foreground":           ("oklch(0.708 0 0)",           "oklch(0.556 0 0)"),
    "accent":                     ("oklch(0.269 0 0)",           "oklch(0.97 0 0)"),
    "accent-foreground":          ("oklch(0.985 0 0)",           "oklch(0.205 0 0)"),
    "destructive":                ("oklch(0.704 0.191 22.216)",  "oklch(0.577 0.245 27.325)"),
    "destructive-foreground":     ("oklch(1 0 0)",               "oklch(1 0 0)"),
    "border":                     ("oklch(1 0 0 / 10%)",         "oklch(0.922 0 0)"),
    "input":                      ("oklch(1 0 0 / 15%)",         "oklch(0.922 0 0)"),
    "ring":                       ("oklch(0.556 0 0)",           "oklch(0.708 0 0)"),
    "sidebar":                    ("oklch(0.205 0 0)",           "oklch(0.985 0 0)"),
    "sidebar-foreground":         ("oklch(0.985 0 0)",           "oklch(0.145 0 0)"),
    "sidebar-accent":             ("oklch(0.269 0 0)",           "oklch(0.97 0 0)"),
    "sidebar-accent-foreground":  ("oklch(0.985 0 0)",           "oklch(0.205 0 0)"),
    "sidebar-border":             ("oklch(1 0 0 / 10%)",         "oklch(0.922 0 0)"),
}

# Derived state colours (Tailwind opacity modifiers used by the shadcn v4 components), per theme:
# name -> (base token, alpha) for (dark, light).
DERIVED = {
    "primary-hover":      (("primary", 0.90), ("primary", 0.90)),        # hover:bg-primary/90
    "secondary-hover":    (("secondary", 0.80), ("secondary", 0.80)),    # hover:bg-secondary/80
    "destructive-fill":   (("destructive", 0.60), ("destructive", 1.0)), # dark:bg-destructive/60
    "destructive-hover":  (("destructive", 0.50), ("destructive", 0.90)),# hover:bg-destructive/90
    "ghost-hover":        (("accent", 0.50), ("accent", 1.0)),           # dark:hover:bg-accent/50
    "input-fill":         (("input", 0.30), ("input", 0.0)),             # dark:bg-input/30, light: transparent
    "input-hover":        (("input", 0.50), ("accent", 1.0)),            # outline hover: dark:bg-input/50
    "ring-focus":         (("ring", 0.50), ("ring", 0.50)),              # focus-visible:ring-ring/50
}

# FluentTheme resource keys re-pointed at shadcn brushes so untouched Fluent control templates already follow the
# tokens. Value = shadcn token/derived name, "@Transparent", or a (dark, light) pair of those.
FLUENT_OVERRIDES = {
    # Slider (shadcn slider: bg-muted track, bg-primary range). The thumb is a solid primary dot: Fluent's slider
    # template sets the thumb's BorderThickness in the template, which styles cannot override, so shadcn's
    # "bg-background + border-primary" ring is approximated by a filled primary circle.
    "SliderTrackFill": "muted", "SliderTrackFillPointerOver": "muted", "SliderTrackFillPressed": "muted",
    "SliderTrackFillDisabled": "muted",
    "SliderTrackValueFill": "primary", "SliderTrackValueFillPointerOver": "primary",
    "SliderTrackValueFillPressed": "primary", "SliderTrackValueFillDisabled": "muted-foreground",
    "SliderThumbBackground": "primary", "SliderThumbBackgroundPointerOver": "primary-hover",
    "SliderThumbBackgroundPressed": "primary", "SliderThumbBackgroundDisabled": "muted-foreground",
    "SliderContainerBackground": "@Transparent", "SliderContainerBackgroundPointerOver": "@Transparent",
    "SliderContainerBackgroundPressed": "@Transparent", "SliderContainerBackgroundDisabled": "@Transparent",
    # ToggleSwitch (shadcn switch)
    "ToggleSwitchFillOff": "input", "ToggleSwitchFillOffPointerOver": "input", "ToggleSwitchFillOffPressed": "input",
    "ToggleSwitchStrokeOff": "@Transparent", "ToggleSwitchStrokeOffPointerOver": "@Transparent",
    "ToggleSwitchStrokeOffPressed": "@Transparent", "ToggleSwitchStrokeOffDisabled": "@Transparent",
    "ToggleSwitchFillOn": "primary", "ToggleSwitchFillOnPointerOver": "primary-hover",
    "ToggleSwitchFillOnPressed": "primary", "ToggleSwitchFillOnDisabled": "muted",
    "ToggleSwitchStrokeOn": "@Transparent", "ToggleSwitchStrokeOnPointerOver": "@Transparent",
    "ToggleSwitchStrokeOnPressed": "@Transparent", "ToggleSwitchStrokeOnDisabled": "@Transparent",
    "ToggleSwitchKnobFillOff": ("foreground", "background"), "ToggleSwitchKnobFillOffPointerOver": ("foreground", "background"),
    "ToggleSwitchKnobFillOffPressed": ("foreground", "background"), "ToggleSwitchKnobFillOffDisabled": "muted-foreground",
    "ToggleSwitchKnobFillOn": ("primary-foreground", "background"), "ToggleSwitchKnobFillOnPointerOver": ("primary-foreground", "background"),
    "ToggleSwitchKnobFillOnPressed": ("primary-foreground", "background"), "ToggleSwitchKnobFillOnDisabled": "background",
    "ToggleSwitchContentForeground": "foreground",
    # CheckBox (shadcn checkbox)
    "CheckBoxForegroundUnchecked": "foreground", "CheckBoxForegroundChecked": "foreground",
    "CheckBoxCheckBackgroundStrokeUnchecked": "input", "CheckBoxCheckBackgroundStrokeUncheckedPointerOver": "ring",
    "CheckBoxCheckBackgroundStrokeUncheckedPressed": "ring",
    "CheckBoxCheckBackgroundFillUnchecked": "input-fill", "CheckBoxCheckBackgroundFillUncheckedPointerOver": "input-fill",
    "CheckBoxCheckBackgroundFillUncheckedPressed": "input-fill",
    "CheckBoxCheckBackgroundFillChecked": "primary", "CheckBoxCheckBackgroundFillCheckedPointerOver": "primary-hover",
    "CheckBoxCheckBackgroundFillCheckedPressed": "primary",
    "CheckBoxCheckBackgroundStrokeCheckedPointerOver": "primary", "CheckBoxCheckBackgroundStrokeCheckedPressed": "primary",
    "CheckBoxCheckGlyphForegroundChecked": "primary-foreground", "CheckBoxCheckGlyphForegroundCheckedPointerOver": "primary-foreground",
    "CheckBoxCheckGlyphForegroundCheckedPressed": "primary-foreground",
    # TextBox (shadcn input)
    "TextControlBackground": "input-fill", "TextControlBackgroundPointerOver": "input-fill",
    "TextControlBackgroundFocused": "input-fill", "TextControlBackgroundDisabled": "input-fill",
    "TextControlBorderBrush": "input", "TextControlBorderBrushPointerOver": "input",
    "TextControlBorderBrushFocused": "ring", "TextControlBorderBrushDisabled": "input",
    "TextControlForeground": "foreground", "TextControlForegroundPointerOver": "foreground",
    "TextControlForegroundFocused": "foreground",
    "TextControlPlaceholderForeground": "muted-foreground", "TextControlPlaceholderForegroundPointerOver": "muted-foreground",
    "TextControlPlaceholderForegroundFocused": "muted-foreground",
    "TextControlButtonForeground": "muted-foreground", "TextControlButtonBackgroundPointerOver": "accent",
    # ComboBox (shadcn select)
    "ComboBoxBackground": "input-fill", "ComboBoxBackgroundPointerOver": "input-hover", "ComboBoxBackgroundPressed": "input-hover",
    "ComboBoxBackgroundDisabled": "input-fill", "ComboBoxBackgroundUnfocused": "input-fill",
    "ComboBoxBorderBrush": "input", "ComboBoxBorderBrushPointerOver": "input", "ComboBoxBorderBrushPressed": "ring",
    "ComboBoxBorderBrushDisabled": "input", "ComboBoxBackgroundBorderBrushFocused": "ring",
    "ComboBoxForeground": "foreground", "ComboBoxPlaceHolderForeground": "muted-foreground",
    "ComboBoxDropDownGlyphForeground": "muted-foreground", "ComboBoxDropDownBackground": "popover",
    "ComboBoxDropDownBorderBrush": "border",
    "ComboBoxItemForeground": "popover-foreground", "ComboBoxItemForegroundPointerOver": "accent-foreground",
    "ComboBoxItemForegroundSelected": "accent-foreground", "ComboBoxItemForegroundSelectedPointerOver": "accent-foreground",
    "ComboBoxItemBackgroundPointerOver": "accent", "ComboBoxItemBackgroundPressed": "accent",
    "ComboBoxItemBackgroundSelected": "accent", "ComboBoxItemBackgroundSelectedPointerOver": "accent",
    "ComboBoxItemBackgroundSelectedPressed": "accent",
    # Menu / ContextMenu / MenuFlyout (shadcn dropdown-menu / popover)
    "MenuFlyoutPresenterBackground": "popover", "MenuFlyoutPresenterBorderBrush": "border",
    "MenuFlyoutItemBackgroundPointerOver": "accent", "MenuFlyoutItemBackgroundPressed": "accent",
    "MenuFlyoutItemForeground": "popover-foreground", "MenuFlyoutItemForegroundPointerOver": "accent-foreground",
    "MenuFlyoutItemForegroundPressed": "accent-foreground", "MenuFlyoutItemForegroundDisabled": "muted-foreground",
    "MenuFlyoutItemKeyboardAcceleratorTextForeground": "muted-foreground",
    "MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver": "muted-foreground",
    "MenuFlyoutSubItemChevron": "muted-foreground", "MenuFlyoutSubItemChevronPointerOver": "accent-foreground",
    "MenuFlyoutSubItemChevronSubMenuOpened": "accent-foreground",
    "FlyoutPresenterBackground": "popover",
    # ToolTip (shadcn tooltip: bg-primary text-primary-foreground)
    "ToolTipBackground": "primary", "ToolTipForeground": "primary-foreground", "ToolTipBorderBrush": "primary",
    # ScrollBar (thin, muted thumb)
    "ScrollBarThumbFillPointerOver": "muted-foreground", "ScrollBarThumbFillPressed": "muted-foreground",
    "ScrollBarTrackFill": "@Transparent", "ScrollBarTrackFillPointerOver": "@Transparent",
    "ScrollBarButtonArrowForeground": "muted-foreground",
}

RADIUS_REM = 0.625
RADIUS_RATIOS = {"sm": 0.6, "md": 0.8, "lg": 1.0, "xl": 1.4}

OKLCH_RE = re.compile(r"^oklch\(\s*([0-9.]+)(%?)\s+([0-9.]+)\s+([0-9.]+)\s*(?:/\s*([0-9.]+)(%?))?\s*\)$")


def parse_oklch(text):
    m = OKLCH_RE.match(text.strip())
    if not m:
        raise ValueError("not an oklch() colour: " + text)
    L = float(m.group(1)) / (100.0 if m.group(2) else 1.0)
    C = float(m.group(3))
    H = float(m.group(4))
    A = 1.0
    if m.group(5) is not None:
        A = float(m.group(5)) / (100.0 if m.group(6) else 1.0)
    return L, C, H, A


def oklch_to_linear_srgb(L, C, H):
    h = math.radians(H)
    a = C * math.cos(h)
    b = C * math.sin(h)
    l_ = L + 0.3963377774 * a + 0.2158037573 * b
    m_ = L - 0.1055613458 * a - 0.0638541728 * b
    s_ = L - 0.0894841775 * a - 1.2914855480 * b
    l, m, s = l_ ** 3, m_ ** 3, s_ ** 3
    r = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s
    g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s
    bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    return r, g, bl


def encode(v):
    v = 12.92 * v if v <= 0.0031308 else 1.055 * (v ** (1.0 / 2.4)) - 0.055
    return min(1.0, max(0.0, v))


def to_byte(v):
    return int(math.floor(v * 255.0 + 0.5))


def oklch_to_hex(text):
    L, C, H, A = parse_oklch(text)
    r, g, b = (to_byte(encode(c)) for c in oklch_to_linear_srgb(L, C, H))
    a = to_byte(A)
    return "#%02X%02X%02X%02X" % (a, r, g, b)


def with_alpha(hexcolor, alpha):
    a = int(hexcolor[1:3], 16) / 255.0
    return "#%02X%s" % (to_byte(a * alpha), hexcolor[3:])


def pascal(name):
    return "".join(p.capitalize() for p in name.split("-"))


def build():
    data = {"source": SOURCE, "rounding": "channel = floor(v * 255 + 0.5) after clip to [0,1]", "tokens": {}, "derived": {}}
    for name, (dark, light) in TOKENS.items():
        data["tokens"][name] = {
            "Dark": {"oklch": dark, "hex": oklch_to_hex(dark)},
            "Light": {"oklch": light, "hex": oklch_to_hex(light)},
        }
    for name, (dark, light) in DERIVED.items():
        entry = {}
        for theme, (base, alpha) in (("Dark", dark), ("Light", light)):
            entry[theme] = {"base": base, "alpha": alpha, "hex": with_alpha(data["tokens"][base][theme]["hex"], alpha)}
        data["derived"][name] = entry
    data["radius"] = {k: round(RADIUS_REM * 16 * v, 3) for k, v in RADIUS_RATIOS.items()}
    return data


def xaml_theme_block(data, theme, indent):
    pad = " " * indent
    lines = []
    for name, entry in data["tokens"].items():
        key = "Shadcn." + pascal(name)
        lines.append(f'{pad}<Color x:Key="{key}Color">{entry[theme]["hex"]}</Color>')
        lines.append(f'{pad}<SolidColorBrush x:Key="{key}Brush" Color="{entry[theme]["hex"]}" />')
    for name, entry in data["derived"].items():
        key = "Shadcn." + pascal(name)
        lines.append(f'{pad}<SolidColorBrush x:Key="{key}Brush" Color="{entry[theme]["hex"]}" />')
    lines.append(f"{pad}<!-- FluentTheme keys re-pointed at the tokens above -->")
    for fluent_key, value in FLUENT_OVERRIDES.items():
        if isinstance(value, tuple):
            value = value[0] if theme == "Dark" else value[1]
        if value == "@Transparent":
            lines.append(f'{pad}<SolidColorBrush x:Key="{fluent_key}" Color="Transparent" />')
        else:
            if value not in data["tokens"] and value not in data["derived"]:
                raise SystemExit(f"unknown token {value} for {fluent_key}")
            lines.append(f'{pad}<StaticResource x:Key="{fluent_key}" ResourceKey="Shadcn.{pascal(value)}Brush" />')
    return "\n".join(lines)


def palette_block(data, theme, indent):
    t = {k: v[theme]["hex"] for k, v in data["tokens"].items()}
    opaque_border = t["accent"] if theme == "Dark" else t["border"]
    p = {
        "Accent": t["primary"],
        "RegionColor": t["background"],
        "AltHigh": t["background"], "AltMediumHigh": t["background"], "AltMedium": t["background"],
        "AltMediumLow": t["background"], "AltLow": t["background"],
        "BaseHigh": t["foreground"], "BaseMediumHigh": t["foreground"], "BaseMedium": t["muted-foreground"],
        "BaseMediumLow": t["ring"], "BaseLow": opaque_border,
        "ChromeAltLow": t["foreground"], "ChromeBlackHigh": "#FF000000", "ChromeBlackLow": "#33000000",
        "ChromeBlackMedium": "#99000000", "ChromeBlackMediumLow": "#66000000",
        "ChromeDisabledHigh": opaque_border, "ChromeDisabledLow": t["muted-foreground"],
        "ChromeGray": t["muted-foreground"], "ChromeHigh": t["ring"], "ChromeLow": t["card"],
        "ChromeMedium": t["card"], "ChromeMediumLow": t["popover"], "ChromeWhite": "#FFFFFFFF",
        "ListLow": t["accent"], "ListMedium": t["secondary"], "ErrorText": t["destructive"],
    }
    pad = " " * indent
    attrs = ("\n" + pad + "    ").join(f'{k}="{v}"' for k, v in p.items())
    return f'{pad}<ColorPaletteResources x:Key="{theme}"\n{pad}    {attrs} />'


def replace_region(text, tag, body):
    begin = f"<!-- BEGIN GENERATED {tag} (Themes/tools/oklch_to_hex.py) -->"
    end = f"<!-- END GENERATED {tag} -->"
    i, j = text.find(begin), text.find(end)
    if i < 0 or j < 0:
        raise SystemExit(f"generated region {tag} not found")
    line_start = text.rfind("\n", 0, j) + 1
    return text[: i + len(begin)] + "\n" + body + "\n" + text[line_start:]


def main():
    check = "--check" in sys.argv
    data = build()
    outputs = {}
    outputs[os.path.join(HERE, "shadcn-neutral.tokens.json")] = json.dumps(data, indent=2) + "\n"

    tokens_path = os.path.join(THEMES, "ShadcnTokens.axaml")
    text = open(tokens_path, encoding="utf-8").read()
    for theme in ("Dark", "Light"):
        text = replace_region(text, theme, xaml_theme_block(data, theme, 16))
    outputs[tokens_path] = text

    theme_path = os.path.join(THEMES, "ShadcnTheme.axaml")
    text = open(theme_path, encoding="utf-8").read()
    text = replace_region(text, "Palettes", palette_block(data, "Dark", 6) + "\n" + palette_block(data, "Light", 6))
    outputs[theme_path] = text

    stale = []
    for path, content in outputs.items():
        current = open(path, encoding="utf-8").read() if os.path.exists(path) else None
        if current != content:
            stale.append(path)
            if not check:
                with open(path, "w", encoding="utf-8", newline="\n") as f:
                    f.write(content)
    if check and stale:
        print("out of date:", *stale, sep="\n  ")
        return 1
    for name, entry in data["tokens"].items():
        print(f'{name:28} dark {entry["Dark"]["hex"]}  light {entry["Light"]["hex"]}')
    return 0


if __name__ == "__main__":
    sys.exit(main())
