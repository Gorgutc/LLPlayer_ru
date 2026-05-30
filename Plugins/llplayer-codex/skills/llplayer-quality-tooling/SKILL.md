---
name: llplayer-quality-tooling
description: Use when adding or changing LLPlayer_ru quality scripts, hooks, CI, or analyzer policy.
---

# LLPlayer Quality Tooling

Quality tooling must match a Windows desktop app.

## Preferred Tools

- `dotnet restore`, `dotnet build -warnaserror`, `dotnet test`.
- PowerShell scripts under `scripts/codex`.
- Existing GitHub Actions on `windows-latest`.
- Future C# analyzers only when they do not create broad unrelated churn.

## Avoid

Do not add `npm`, `pnpm`, Playwright, Lighthouse, pa11y, ESLint, Stylelint, Knip, or dependency-cruiser unless the repo gains a real web surface.
