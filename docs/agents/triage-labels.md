# Triage labels

This repo uses the five canonical triage roles. The label string equals the role name.

| Role | Label | Meaning |
|---|---|---|
| `needs-triage` | `needs-triage` | Newly filed, not yet assessed. A ticket carries this label until a maintainer routes it. |
| `needs-info` | `needs-info` | Waiting on the reporter for reproduction steps, environment, logs, or a decision. Blocks all other triage movement. |
| `ready-for-agent` | `ready-for-agent` | Scoped, reproducible, and safe for an agent to implement without further human judgement. |
| `ready-for-human` | `ready-for-human` | Understood and accepted, but needs human work: design calls, hardware/colourimeter verification, signing, or release operations. |
| `wontfix` | `wontfix` | Deliberately not being done. Always paired with a comment explaining why. |

## Rules

- A ticket carries **at most one** of `needs-triage`, `ready-for-agent`, `ready-for-human`, `wontfix`. Remove the previous role label when applying a new one.
- `needs-info` is additive: it stacks on top of the current role label and is removed once the missing information arrives.
- Do not invent additional role labels. If a ticket needs extra context (subsystem, platform, severity), use a descriptive label instead of a new triage state.

## Wayfinder labels

Exploration work uses the wayfinder vocabulary, independent of the triage roles above.

| Label | Used for |
|---|---|
| `wayfinder:map` | The exploration map — the top-level container issue that frames the question and lists its children. |
| `wayfinder:research` | A child that answers a question by reading code, docs, or external sources. |
| `wayfinder:prototype` | A child that answers a question by building a throwaway artefact. |
| `wayfinder:grilling` | A child that answers a question by interrogating the user's intent or constraints. |
| `wayfinder:task` | A child that is a concrete implementation step rather than a question. |

## When a skill says "apply the triage label"

Use exactly the strings in the tables above. If a label is missing on the tracker, create it — do not substitute a near-synonym.
