# CLAUDE.md

@AGENTS.md

## Claude Code specifics

- Nested `CLAUDE.md` files (they import the folder's `AGENTS.md`) load automatically when you work in that folder.
- Slash commands in `.claude/commands/`: `/verify` (full verification), `/add-entity`, `/add-widget`, `/add-workflow`.
- For broad changes spanning .NET + TypeScript, split work by folder (e.g. one agent for `sdk/server`, one for
  `sdk/client/angular`) – folders have clear ownership and the layering tests catch cross-layer mistakes.
- Before declaring done, run the checks of "Definition of done"; report failures with output instead of guessing.
- The user writes Turkish; answer in Turkish unless asked otherwise. Code, comments and docs stay English
  (UI texts Turkish).
