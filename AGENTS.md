# Figma Design System Rules

These rules apply to all Figma-driven UI work in this repository. Follow them before creating or editing any screen, component, or page.

## Project Overview

- This project is an ASP.NET Core Blazor Web App on `.NET 10` with `AddInteractiveServerComponents()` in [Program.cs](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Program.cs).
- Route-level UI lives under [Components/Pages](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/Pages).
- Shared layout lives under [Components/Layout](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/Layout).
- Global visual tokens and shared datagrid shell rules live in [wwwroot/app.css](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/wwwroot/app.css).
- Styling is primarily CSS isolation with `*.razor.css`. Use page-local styles first, and only promote patterns to `app.css` when they are truly cross-page.

## Component Organization

- Place new route-level screens in `Components/Pages`.
- Place shared layout or navigation elements in `Components/Layout`.
- Reuse existing primitives before adding new ones. This codebase prefers extending existing page patterns over inventing a new component library.
- Keep component names in PascalCase and pair them with a same-name `.razor.css` file when the component owns its styling.
- Prefer adding small reusable helpers only when at least two pages clearly need them.

## Required Figma Flow

1. Run `get_design_context` for the exact Figma node.
2. If the node is too large or unclear, run `get_metadata` to find the correct child node, then rerun `get_design_context`.
3. Run `get_screenshot` for the same node before implementation.
4. Treat the Figma output as design intent only. Translate it into this Blazor app's structure, tokens, and CSS conventions.
5. Reuse existing page shells, datagrid patterns, action buttons, and badge treatments before creating new UI structures.
6. Validate desktop and mobile behavior against the Figma screenshot before considering the task complete.

## Styling Rules

- IMPORTANT: Use the token system already defined in [wwwroot/app.css](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/wwwroot/app.css).
- IMPORTANT: Never hardcode a new color if an existing app token or page token already covers the need.
- Base visual language:
  - glass/surface cards
  - soft gradients
  - rounded corners
  - subtle borders and layered shadows
  - `Plus Jakarta Sans` for body and `Space Grotesk` for headings
- Shared app tokens include:
  - `--app-bg`, `--app-ink`, `--app-muted`
  - `--app-border`, `--app-shadow`, `--app-shadow-soft`, `--app-shadow-deep`
  - `--app-highlight`, `--app-danger-highlight`
  - `--app-accent`, `--app-accent-strong`, `--app-accent-soft`, `--app-accent-border`
- Page-level themes may override local variables such as `--surface-main`, `--text-muted`, or `--accent`. Respect those instead of introducing unrelated colors.
- Keep spacing compact and scan-friendly. This app is intentionally dense, but components must still breathe.

## Forms And Inputs

- IMPORTANT: In isolated CSS, style child form controls with `::deep`.
- Use the same control language already used across pages:
  - rounded inputs
  - light surface gradients
  - soft inner borders
  - focused state driven by the app focus ring
- Prefer existing input groups and form section patterns already used in:
  - [Products.razor.css](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/Pages/Products.razor.css)
  - [Recipes.razor.css](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/Pages/Recipes.razor.css)
  - [Warehouse.razor.css](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/Pages/Warehouse.razor.css)
- Do not leave browser-default form chrome in place when the surrounding UI is custom-styled.
- Use concise helper text. Avoid long explanatory blocks inside operational forms.

## Datagrid Rules

- IMPORTANT: The visual benchmark for all datagrids is the material catalog page in [Products.razor](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/Pages/Products.razor).
- Use the `catalog-datagrid` shell and the shared toolbar, table, mobile-card, and pager rules in [wwwroot/app.css](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/wwwroot/app.css).
- Desktop tables should generally use `QuickGrid` or standard tables styled to match the catalog shell.
- Mobile should not compress desktop tables. Use the existing mobile card-list pattern instead.
- Align columns consistently:
  - identity and labels left
  - numeric values right
  - status and row actions centered
- Pagination must follow the catalog footer pattern:
  - count on the left
  - pager centered
  - spacer on the right
- Reuse the same `Prev / page select / current page / Next` structure and styling everywhere.

## Icon Rules

- IMPORTANT: Do not add third-party icon packages.
- Reuse [AppIcon.razor](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/AppIcon.razor) for navigation and row actions.
- Available icon names already include navigation and action primitives such as `materials`, `warehouse`, `recipe`, `production`, `pos`, `shopping`, `bookkeeping`, `settings`, `account`, `backup`, `edit`, `delete`, `check`, `receipt`, `close`, and `logout`.
- In isolated CSS, target icon internals through `::deep` when needed.
- Keep action buttons circular and compact, matching the catalog page pattern.

## Layout Rules

- Reuse the shell language from:
  - [MainLayout.razor.css](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/Layout/MainLayout.razor.css)
  - [NavMenu.razor.css](C:/Users/hazel/source/repos/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate/Components/Layout/NavMenu.razor.css)
- Desktop pages should read as operational workspaces, not marketing pages.
- On tablet, navigation is icon-only horizontal tabs. Respect that density and avoid adding wide tablet-only labels in the nav.
- On mobile, reduce header height and push primary actions upward. Avoid tall intros before the main work area.

## Responsive Rules

- Desktop and mobile are intentionally different in this app. Do not merely scale desktop down.
- Tablet uses a distinct navigation pattern and often needs tighter spacing than desktop.
- If a table or panel becomes vertically heavy on small screens, collapse details behind a tap target or use card summaries.
- Prefer one active expanded detail on mobile rather than many permanently open details.

## Asset Handling

- Store app assets in `wwwroot`.
- If Figma MCP returns a localhost asset source, use that source directly during implementation.
- Do not invent placeholder illustrations or install icon/image packages when existing repo patterns already solve the need.
- Inline SVG through `AppIcon` is preferred for small UI glyphs.

## Blazor Implementation Rules

- Translate Figma structures into idiomatic Razor markup, not React-style component patterns.
- Keep state and event handlers in the `.razor` component unless extraction clearly improves reuse.
- Use route pages with paired CSS isolation files as the default implementation pattern.
- Preserve existing service-driven data flow and avoid embedding fake data inside final page markup unless the page is explicitly static.

## Accessibility And Interaction

- Interactive elements must remain keyboard reachable and have clear labels or `aria-label` values.
- Do not hide actions behind hover-only affordances on touch layouts.
- Keep contrast aligned with the current app palette and avoid low-contrast pastel-on-pastel text.
- Use motion sparingly and functionally: expand/collapse, hover lift, and subtle emphasis are acceptable; decorative motion should stay restrained.

## Project-Specific Guardrails

- IMPORTANT: Match the existing HPP Ultimate visual language. Do not introduce a new brand direction on one page.
- IMPORTANT: Prefer extending existing classes and shells over creating one-off styles.
- IMPORTANT: When building a new operational table, make it look and behave like the material catalog table unless there is a strong product reason not to.
- IMPORTANT: When styling inputs or child components inside isolated CSS, use `::deep`.
- Avoid purple-heavy generic AI UI. This app uses soft blue, indigo, amber, and neutral glass surfaces in a restrained way.
- Avoid flat white empty pages. Most screens use layered background surfaces and subtle gradients.

## Validation Checklist

Before finishing a Figma-driven task, verify:

- the page uses existing tokens or local theme variables
- datagrid and pager match the catalog pattern when applicable
- action icons use `AppIcon`
- desktop, tablet, and mobile layouts all read cleanly
- `::deep` is used where isolated CSS must style child controls
- no new icon package or ad hoc design system was introduced
