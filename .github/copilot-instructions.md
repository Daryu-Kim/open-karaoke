# Project Core Rules (Optimized EN)

## 0. Response & Conciseness (OVERRIDES ALL)
- **Default response language: English.**
- **EXCEPTIONS (Use Korean ONLY):** (1) When asking for user permission/confirmation before critical actions. (2) When providing the final task summary report (Section 8).
- **CRITICAL:** Keep responses concise. Show only relevant code and brief explanations. Avoid verbose introductions, redundant warnings, or over-explaining basic concepts. Focus strictly on the requested task.

## 1. Core Principles
- Preserve existing code, functions, and design. No massive refactoring.
- **Reuse first**: Check existing components, composables, APIs, utils before writing new code.
- Make minimal changes (fewest files/lines). No out-of-scope features.
- Never guess structure/behavior without reading actual code.

## 2. Pre-Check (Before Start)
- Check `git status/diff`, `package.json`, and lock file to detect package manager.
- Search related existing files and similar implementations.
- Evaluate impact on other screens/features.
- **If only analysis/plan requested -> DO NOT modify files.**

## 3. Workflow & Error Handling
- Split complex tasks into steps. Run build/typecheck after each step.
- On error: Fix the **first** error first. No temp bypasses (commenting out, `any`, empty catch, disabling lint).
- Unrelated errors: Report separately. Do not fix arbitrarily.

## 4. Strict Bans (Require explicit consent)
`git reset --hard`, `git clean -fd`, delete lock files, change package manager/framework versions, batch update major libs, change API/DB schema arbitrarily, rewrite working features, install unverified packages.

## 5. Tech Stack (Nuxt/Vue/TS)
- Keep current versions, file structure, naming, and Composition API patterns.
- SSR Safety: Use `process.client` or `onMounted` for `window`/`document`/`localStorage`.
- TypeScript: No `any`. Never disable type checks.

## 6. Security & Sensitive Data
- Never output/modify: `.env` values, API keys, passwords, tokens, payment keys.
- Validate ALL user inputs on server. Do NOT trust client-side admin checks.
- Prevent XSS, SQLi, SSRF. Validate file uploads (ext, MIME, size).

## 7. Feature-Specific (Quick)
- **Customer**: Keep existing cart/price/stock flow. Never trust client-calculated final price. Show loading/error/empty states. Preserve mobile UX and SEO.
- **Admin**: Bulk changes -> show target count, split success/fail logs, provide retry. Critical actions (delete/price/stock) require confirmation.
- **Wholesale Scraper**: Reuse existing parsers. Stop if required fields missing. Check duplicates (never overwrite blindly). Use server price logic. Sanitize external HTML (XSS). Isolate item-level failures (don't halt all).
- **DB/API**: Check existing schema. Review impact before column changes. Keep API response format. Always re-validate inputs server-side.

## 8. Reporting & Validation
- After changes: Run existing scripts from `package.json` (build, typecheck, lint).
- Clear Nuxt/cache ONLY if cache suspected. Never delete source/lock files.
- **Final Summary Report (In KOREAN):** Briefly state: Changed files, reasons, reused elements, verification results, remaining warnings/errors, next steps.
- If task is incomplete, clearly distinguish: Complete / Partial / Incomplete / Unverified / Needs review.

## 9. Agent Behavior
- Investigate actual project code first. Do not ask the user for info already in the code.
- If the request is clear, proceed without unnecessary confirmation questions.
- If uncertain, state it clearly instead of guessing.
- For major data/structure changes, warn before applying.
- For implementation requests, complete the actual code changes and verification. Do not stop after writing a plan.

## 10. Ultimate Priority Order
Data/Function Safety > Build Stability > Security > Accurate Implementation > UX Convenience > Code Cleanup > Extra Features.

---

## 11. plan.md & Task Management (Token Optimization)
- **Primary context source:** Use `plan.md` as the main reference for current task status and next steps.
- **NEVER read `session.db`** — it is a binary SQLite file. The "SQL todos" are already recorded as plain text in `plan.md`. Reading `session.db` wastes tokens and may corrupt context.
- If the conversation exceeds 5 turns, **ask the user**:
  - "Shall we start a new session (`/new`)?"
  - "Or should I summarize the current progress into `plan.md`?"
- **Do NOT load the entire `plan.md`** if only a small section is needed. Instead, ask the user: "Which part of the plan should I focus on?"
- If `plan.md` grows too large (>500 lines), **ask for permission** before restructuring:
  1. Move completed task details to `history.md`.
  2. Keep only **current active tasks** and **next 3–5 todos** in `plan.md` (target: 200–300 lines).
- Always get explicit user consent before splitting, moving, or deleting content from `plan.md`.

## 12. SQL Todos & session.db Clarification
- **SQL Todos are already in `plan.md`** as "SQL todos: ..." entries. No need to query `session.db`.
- If you need the latest todos, read the `plan.md` file (see Rule 11).
- **Never attempt to read, parse, or interpret `session.db`** — it is a binary file used internally by the agent, not a source of task information.

## 13. File Language (plan.md, history.md, etc.)
- **All planning files (`plan.md`, `history.md`, `todos.md`, etc.) MUST be written in English.**
- This includes creating new files, updating existing tasks, and restructuring content.
- **Exception:** The final summary report (Section 8) and permission requests (Section 0) are still in Korean, as already specified.
- Reason: English uses fewer tokens and keeps the file size smaller, which reduces context costs.
- If you need to explain something complex in the plan, keep it in English. The user will ask for clarification in Korean if needed.

## 14. UI & Customer-Facing Content Language (CRITICAL)
- **The language rules (Rule 0 and Rule 13) apply ONLY to:**
  1. AI responses in the chat.
  2. Internal planning files (`plan.md`, `history.md`, `todos.md`).
- **They DO NOT apply to the application source code or UI.**
- **ALL customer-facing content MUST remain in KOREAN:**
  - Page text, headings, and labels.
  - Button text, placeholder text, and form labels.
  - Toast messages, error messages, and success notifications.
  - Product descriptions, category names, and marketing copy.
  - Any text that appears in the browser for the end-user.
- **NEVER translate existing UI text to English.** 
- When creating new features or pages, always write user-facing strings in Korean.
- Code comments may be in English (to save tokens), but visible UI strings must be Korean.
- If you are unsure whether a string is user-facing or internal, treat it as user-facing and keep it in Korean.

## 15. Validation & Commit Policy (Manual Only)
- **NEVER run `prettier`, `eslint`, `test`, `typecheck`, `build`, `git commit`, or `git push` unless explicitly requested by the user.**
- After completing the core code changes for a task, **DO NOT automatically proceed to validation or commit.**
- Instead, **ask the user**:
  > "The code changes are ready. Should I proceed with validation (lint/typecheck/build) and commit/push, or should I just report the changes and wait for your next instruction?"
- **If the user says "proceed" / "yes"** → Run the relevant scripts and commit/push.
- **If the user says "report" / "no"** → Provide a summary of changes without running any scripts and stop.
- This rule **overrides** any conflicting instructions in Rule 8 or Rule 9 about automatic validation after changes.
- **Exception:** If the user explicitly says "run build" or "run typecheck" in the request, then you may run that specific script.