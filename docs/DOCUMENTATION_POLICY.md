# Documentation policy

This document itself follows the policy it describes — treat that as the working example.

## Why

Two audiences, both real, both matter:

1. **Human contributors, developers, and users** need to catch up on this codebase quickly —
   what it's for, how it's structured, why non-obvious decisions were made — without having to
   reconstruct that context from commit history or by asking someone.
2. **AI agents** (Claude Code and others) working in this repo need the same thing, for the
   same reason: to scan the codebase, understand its logic and essence, navigate to the right
   file fast, and understand core concepts and rationale — rather than re-deriving them from
   scratch, or worse, guessing and getting it wrong.

Both audiences fail the same way when documentation lags behind code: they either don't trust
what they find, or they trust something stale. Treating this as a production-grade library
means documentation is not an afterthought bolted on before a release — it's part of what
"done" means for a change.

## Rules

1. **Document as you go, in the same change.** Any change that would puzzle a newcomer without
   context — a new design decision, a non-obvious constraint, a rejected alternative worth
   remembering — gets written into `docs/` as part of that same change, not filed as follow-up
   cleanup. If it's not documented, it's not done.
2. **`docs/INDEX.md` is the map.** Any new doc file gets a one-line entry added there in the
   same change that introduces it. An undiscoverable doc is nearly as bad as a missing one.
3. **Prefer extending an existing doc over creating a new file.** A new file is warranted when
   the topic is a genuinely new, freestanding concern (e.g. a new CLI feature earning its own
   reference page) — not for every small addition to something that already has a home.
4. **Document the *why*, not just the *what*.** The code already says what it does. Docs exist
   to carry the parts code can't: the rejected alternative, the trade-off, the constraint that
   shaped the decision. `docs/CONFIG_MANAGEMENT.md`'s decision log (`Decision | Chosen |
   Rejected alternative(s) | Why`) is the model to follow when recording a design choice.
5. **`CLAUDE.md` at the repo root is the AI-agent entry point.** Kept concise and high-signal,
   pointing into `docs/` for depth rather than duplicating it. Update it when core concepts,
   repo structure, or "where to look for X" navigation changes — not for every detail; it
   should stay short enough to actually be read in full.
6. **`docs/CHANGELOG.md` records what changed, at a glance, every merged change.** It's the
   fast path to "what's new since I last looked," separate from the deeper docs.
7. **`docs/ROADMAP.md` is the single source of truth for what's planned and what's next.** An
   AI agent's conversation context does not persist across sessions — anything not written down
   in this repo is lost the moment a session ends. `CHANGELOG.md` records history; `ROADMAP.md`
   records the forward plan, so any session (or human) can resume without needing prior
   context. Update it in the same change as any work that completes, reprioritizes, or adds a
   planned slice — an out-of-date roadmap actively misleads the next reader, which is worse
   than having none.
