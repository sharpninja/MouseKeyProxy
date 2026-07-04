# MouseKeyProxy-Fresh Sunset Archive

Product workspace: `F:\GitHub\MouseKeyProxy`
Sunset source workspace: `F:\GitHub\MouseKeyProxy-Fresh`
Prepared: 2026-07-04

## Status

`MouseKeyProxy` is the active Product workspace. `MouseKeyProxy-Fresh` is retained as historical input only and should not be used for new implementation work after this migration is accepted.

## Migrated Artifacts

The following Fresh artifacts are archived under this directory:

- `docs/DELEGATION-PLAN.md` -> `docs/sunset-fresh/docs/DELEGATION-PLAN.md`
- `docs/receipts-import-plan.txt` -> `docs/sunset-fresh/docs/receipts-import-plan.txt`
- `docs/requirements/requirements-wiki-documents.zip` -> `docs/sunset-fresh/requirements/requirements-wiki-documents.zip`
- Fresh GitHub wiki export -> `docs/sunset-fresh/requirements/wiki/github/`
- Fresh Azure wiki export -> `docs/sunset-fresh/requirements/wiki/azure/`
- `prompts/transition-lab-e2e-codex.md` -> Product `prompts/transition-lab-e2e-codex.md`
- `scripts/Invoke-Agents.psm1` -> Product `scripts/Invoke-Agents.psm1`, then updated for Product defaults and observable agent invocation
- Fresh `.version` and `.gitignore` -> `docs/sunset-fresh/root-metadata/`
- Transition lab logs -> `docs/sunset-fresh/artifacts/*.log.txt`

## Not Migrated

Generated local bootstrap debris was intentionally not copied into the archive as source artifacts:

- `AGENTS-README-FIRST.yaml.deleted-*`
- `todo-bootstrap.marker`

Both workspaces now ignore those generated files.

## Active Requirements

Product requirement documents in `docs/Project/` and Product wiki mirrors now consolidate Product requirements, Fresh exported requirements, and the July 4, 2026 chat requirements. Fresh wiki mirrors were also updated with the same consolidated set for sunset traceability.

The active Product requirements include the hard project gates:

- Completion requires `payton-legion2` paired with `payton-desktop`, with Legion2 visibly controlling Desktop.
- Agent UI must be a real tray/dashboard experience, not a diagnostic placeholder.
- Branding centers on a hacker mouse typing on a keyboard at a desk surrounded by monitors.
- Codex owns design, testing, review, and receipts; Claude owns implementation code.
- `Invoke-Codex` and `Invoke-Claude` must print parameter summaries, echo the complete call signature, stream output to the host, log output, support dry-run, and throw on nonzero exit.
- Moq is banned; .NET tests use NSubstitute or explicit fakes/stubs.

## Acceptance Notes

This archive prepares Fresh for sunset but does not delete the Fresh workspace. Final project completion still requires implementation and validation of the Product runtime, including the two-machine paired-control proof.
