# Domain docs

## Layout: single-context

This repo is **single-context**: one glossary and one ADR directory serve the whole repository.

```
gloam/
├── GLOSSARY.md          # the project's domain vocabulary
└── docs/
    └── adr/             # architecture decision records
```

`GLOSSARY-MAP.md` and per-context `GLOSSARY.md` files are **not** used here and are not created up front. They are only worth introducing if the repository later grows into genuinely separate contexts; create them at that point, together with the first glossary entries, not in advance.

## Where things live

- **Vocabulary**: `GLOSSARY.md` at the repo root. One file for the entire repository.
- **Decisions**: `docs/adr/`, named `NNNN-<slug>.md`, newest highest. Each record states the decision, the context that forced it, and the consequences.
- **Neither file exists yet.** When the first domain term or the first ADR is actually written, create the file at the path above — do not create empty placeholders.

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `GLOSSARY.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal: either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders), but worth reopening because…_

## Keeping these docs current

Both files are living documents, edited directly. A skill that learns a durable domain term or makes a decision with lasting architectural consequence should write it down here rather than leave it in an issue thread or a commit message.
