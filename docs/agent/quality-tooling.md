# Quality Tooling

LLPlayer quality tooling is PowerShell/.NET-first.

## Allowed Baseline

- `dotnet restore -warnaserror`
- `dotnet build -warnaserror`
- `dotnet test`
- PowerShell verification scripts
- GitHub Actions on Windows runners
- Future Roslyn analyzers or `dotnet format` only when added intentionally

## Not Baseline

- `npm`, `pnpm`, Next.js, React, TypeScript gates
- Playwright browser smoke
- Lighthouse
- pa11y
- ESLint, Stylelint, HTMLHint
- Knip, dependency-cruiser, JS visual regression

If a future web surface is introduced, add separate scoped gates instead of changing LLPlayer desktop gates.
