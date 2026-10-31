---
name: codebase-lessons
description: Curated, reusable lessons about this codebase (build quirks, conventions, gotchas) accumulated across work streams. Preloaded into package agents; read-only for them, curated by the Team Lead.
user-invocable: false
metadata:
  source: codebase-lessons template
---

## Codebase Lessons
**Rules for agents:**
- Apply these when relevant.
- Never edit this file; report new lessons in your handover's Lessons section.

**Rules for the Team Lead (curator):**
- One line per lesson, tagged with the source work stream.
- Keep only reusable, codebase-level lessons; remove superseded or duplicate lines.
- Keep the file under 80 lines.
- This file merges with `merge=union`, so dedupe at release.

### Build & Tooling
- {lesson} (WS-x.y)

### Conventions
- {lesson} (WS-x.y)

### Gotchas
- zKillboard API calls (killmails, statistics, prices, posting, history dumps, R2Z2 feed): the official wiki https://github.com/zKillboard/zKillboard/wiki and its API sub-pages are the source; consult them before implementing or changing any zKill integration, never rely on assumptions (pre-framework)
