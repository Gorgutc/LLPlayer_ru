# Migration From Source Repos

The source repositories `Gorgutc/PL_RU` and `Gorgutc/codex` provide the Codex-first structure:

- `AGENTS.md` as source of truth.
- repo-local plugin and skills.
- hooks and verification scripts.
- read-only review agents.
- docs for orchestration, verification, quality, and code review.

Only the structure and governance model are portable.

Do not copy web-specific gates into LLPlayer:

- Next/React/Blueprint/SCSS rules.
- `package.json`, `pnpm`, `npm`.
- Playwright, Lighthouse, pa11y.
- ESLint, Stylelint, HTMLHint.
- Knip, dependency-cruiser, JS visual snapshots.

LLPlayer replacements are PowerShell, `dotnet`, WPF/XAML review, native asset checks, and Windows publish smoke.
