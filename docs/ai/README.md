# AI-assisted development – playbooks

Start with the root [`AGENTS.md`](../../AGENTS.md) (map, commands, definition of done) and the nested `AGENTS.md` of
the folder you change. These playbooks describe recurring tasks end to end, so an assistant (or a human) touches every
place that must change and verifies the result.

| Task | Playbook |
|---|---|
| New CRUD entity (API + both frontends) | [add-crud-entity.md](add-crud-entity.md) |
| New layout widget (spec → .NET → Blazor → Angular → static HTML) | [add-layout-widget.md](add-layout-widget.md) |
| New or changed workflow (definition, views, layouts) | [add-workflow.md](add-workflow.md) |
| Workflow partitioning & flow-specific JSON indexing | [workflow-partitioning-and-indexing.md](workflow-partitioning-and-indexing.md) |
| Change the portable expression language | [change-expression-language.md](change-expression-language.md) |
| Security review checklist for every change | [security-checklist.md](security-checklist.md) |
| Row-level security & branch authorization best practices | [../row-level-security-best-practices.md](../row-level-security-best-practices.md) |
| How to test and verify (unit, spec, builds, E2E) | [testing.md](testing.md) |
| Known pitfalls and how they showed up | [gotchas.md](gotchas.md) |

## How to work with an assistant on this repo

1. **Describe the outcome and the scope** ("add a Brand screen to both frontends", "make the grid sortable by …").
   Mention whether contracts may change.
2. Let it **read before writing**: the nested `AGENTS.md`, the closest existing example (e.g. `CategoryController`,
   `bike-category.json`, an existing widget) and the matching tests.
3. Prefer **small vertical slices** that end green (build + tests) over big bang changes.
4. For changes spanning .NET and TypeScript, split by folder ownership (server / bff+client / blazor / angular+ui-core);
   the layering tests and the shared `spec/` keep the halves consistent.
5. Always finish with the [definition of done](../../AGENTS.md#definition-of-done-every-change) – `/verify` in Claude Code.
