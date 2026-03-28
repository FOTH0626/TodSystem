---
name: personal-coding-standards
description: "Personal engineering workflow and coding standards for repository work. Use when Codex needs to inspect a codebase, plan or implement changes, review code, debug issues, explain code, or collaborate on code while following preferences for concise communication, evidence-first exploration, safe edits, narrow verification, and high-signal summaries. Also trigger for requests about coding standards, code review, bug fixing, refactoring, debugging, or technical planning."
---

# Personal Coding Standards

## Overview

Apply this skill as the default working standard for code changes, debugging, refactoring, explanation, and code review. Optimize for clarity, pragmatism, and rigor. Prefer evidence from the repository over guesses, and keep communication concise and implementation-focused.

## Workflow

1. Ground in the codebase before proposing or editing. Inspect the relevant files, configs, schemas, and current behavior first.
2. Resolve discoverable facts locally. Ask the user only when a missing decision changes scope, behavior, or risk.
3. Prefer minimal, well-scoped changes that match existing patterns instead of broad rewrites.
4. Carry work through implementation and verification unless the user clearly asks only for analysis, brainstorming, or planning.

## Communication

- Send a short progress update before substantial exploration, before edits, and during longer work.
- State the current objective and next action. Avoid filler, cheerleading, and vague status reports.
- Make reasonable low-risk assumptions. State them after acting.
- Keep the final response short. Lead with outcome, then verification, then residual risk or blockers.

## Exploration And Editing

- Prefer `rg` and `rg --files` for searching.
- Read surrounding code before touching a file. Follow established naming, architecture, and conventions.
- Use `apply_patch` for manual edits.
- Keep text ASCII unless the file already relies on non-ASCII characters.
- Add comments only when they clarify non-obvious logic.
- Avoid rewriting unrelated code for style consistency alone.

## Repository Safety

- Treat the worktree as user-owned. Preserve unrelated changes.
- Never revert or overwrite changes you did not make unless explicitly told to do so.
- Avoid destructive commands such as `git reset --hard` and checkout-based reverts.
- Prefer non-interactive git commands and narrow diffs.

## Verification

- Run the smallest meaningful check that exercises the changed behavior.
- Prefer targeted tests or builds over full-suite runs unless the change is broad.
- If validation cannot be run, say so explicitly and explain why.
- Call out known gaps, assumptions, and likely regressions.

## Review Mode

- Switch to a review mindset when the user asks for a review.
- Report findings first, ordered by severity, with file references.
- Focus on correctness issues, regressions, unsafe assumptions, and missing tests.
- Keep summaries brief if there are findings. Say explicitly when no findings are found.

## UI And Graphics Work

- Preserve an existing design system when one exists.
- Avoid generic, boilerplate UI when creating a new interface. Make visual choices intentional.
- Treat render order, coordinate space, data flow, and performance budgets as first-class constraints in graphics or Unity work.

## Typical Requests

- "Use $personal-coding-standards to review this patch and list the risky parts."
- "Use $personal-coding-standards to fix this bug without rewriting the whole subsystem."
- "Use $personal-coding-standards to refactor this module while preserving behavior."
- "Use $personal-coding-standards to explain this code path and then patch the issue."
