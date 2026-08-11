# Model Selection Reference

### Per-Agent Model Selection

Before spawning an agent, determine which model to use. Check these layers in order — first match wins:

**Layer 0 — Persistent Config (`.squad/config.json`):** On session start, read `.squad/config.json`. If `agentModelOverrides.{agentName}` exists, use that model for this specific agent. Otherwise, if `defaultModel` exists, use it for ALL agents. This layer survives across sessions — the user set it once and it sticks.

- **When user says "always use X" / "use X for everything" / "default to X":** Write `defaultModel` to `.squad/config.json`. Acknowledge: `✅ Model preference saved: {model} — all future sessions will use this until changed.`
- **When user says "use X for {agent}":** Write to `agentModelOverrides.{agent}` in `.squad/config.json`. Acknowledge: `✅ {Agent} will always use {model} — saved to config.`
- **When user says "switch back to automatic" / "clear model preference":** Remove `defaultModel` (and optionally `agentModelOverrides`) from `.squad/config.json`. Acknowledge: `✅ Model preference cleared — returning to automatic selection.`

**Layer 1 — Session Directive:** Did the user specify a model for this session? ("use opus for this session", "save costs"). If yes, use that model. Session-wide directives persist until the session ends or contradicted.

**Layer 2 — Charter Preference:** Does the agent's charter have a `## Model` section with `Preferred` set to a specific model (not `auto`)? If yes, use that model.

**Layer 3 — Task-Aware Auto-Selection:** Use the governing principle: **cost first, unless code is being written.** Match the agent's task to determine output type, then select accordingly:

| Task Output | Model | Tier | Rule |
|-------------|-------|------|------|
| Writing code (implementation, refactoring, test code, bug fixes) | `claude-sonnet-4.6` | Standard | Quality and accuracy matter for code. Use standard tier. |
| Writing prompts or agent designs (structured text that functions like code) | `claude-sonnet-4.6` | Standard | Prompts are executable — treat like code. |
| NOT writing code (docs, planning, triage, logs, changelogs, mechanical ops) | `claude-haiku-4.5` | Fast | Cost first. Haiku handles non-code tasks. |
| Visual/design work requiring image analysis | `claude-opus-4.5` | Premium | Vision capability required. Overrides cost rule. |

**Role-to-model mapping** (applying cost-first principle):

| Role | Default Model | Why | Override When |
|------|--------------|-----|---------------|
| Core Dev / Backend / Frontend | `claude-sonnet-4.6` | Writes code — quality first | Heavy code gen → `gpt-5.3-codex` |
| Tester / QA | `claude-sonnet-4.6` | Writes test code — quality first | Simple test scaffolding → `claude-haiku-4.5` |
| Lead / Architect | auto (per-task) | Mixed: code review needs quality, planning needs cost | Architecture proposals → premium; triage/planning → haiku |
| Prompt Engineer | auto (per-task) | Mixed: prompt design is like code, research is not | Prompt architecture → sonnet; research/analysis → haiku |
| Copilot SDK Expert | `claude-sonnet-4.6` | Technical analysis that often touches code | Pure research → `claude-haiku-4.5` |
| Designer / Visual | `claude-opus-4.5` | Vision-capable model required | — (never downgrade — vision is non-negotiable) |
| DevRel / Writer | `claude-haiku-4.5` | Docs and writing — not code | — |
| Scribe / Logger | `claude-haiku-4.5` | Mechanical file ops — cheapest possible | — (never bump Scribe) |
| Git / Release | `claude-haiku-4.5` | Mechanical ops — changelogs, tags, version bumps | — (never bump mechanical ops) |

**Task complexity adjustments** (apply at most ONE — no cascading):
- **Bump UP to premium:** architecture proposals, reviewer gates, security audits, multi-agent coordination (output feeds 3+ agents)
- **Bump DOWN to fast/cheap:** typo fixes, renames, boilerplate, scaffolding, changelogs, version bumps
- **Switch to code specialist (`gpt-5.3-codex`):** large multi-file refactors, complex implementation from spec, heavy code generation (500+ lines)
- **Switch to analytical diversity (`gemini-3-pro-preview`):** code reviews where a second perspective helps, security reviews, architecture reviews after a rejection

**Layer 4 — Default:** If nothing else matched, use `claude-haiku-4.5`. Cost wins when in doubt, unless code is being produced.

**Fallback chains — when a model is unavailable:**

If a spawn fails because the selected model is unavailable (plan restriction, org policy, rate limit, deprecation, or any other reason), silently retry with the next model in the chain. Do NOT tell the user about fallback attempts. Maximum 3 retries before jumping to the nuclear fallback.

```
Premium:  claude-opus-4.6 → claude-opus-4.5 → claude-sonnet-4.6 → claude-sonnet-4.5 → (omit model param)
Standard: claude-sonnet-4.6 → claude-sonnet-4.5 → gpt-5.4 → gpt-5.3-codex → claude-sonnet-4 → (omit model param)
Fast:     claude-haiku-4.5 → gpt-5.4-mini → gpt-5.1-codex-mini → gpt-4.1 → (omit model param)
```

`(omit model param)` = call the `task` tool WITHOUT the `model` parameter. The platform uses its built-in default. This is the nuclear fallback — it always works.

**Fallback rules:**
- If the user specified a provider ("use Claude"), fall back within that provider only before hitting nuclear
- Never fall back UP in tier — a fast/cheap task should not land on a premium model
- Log fallbacks to the orchestration log for debugging, but never surface to the user unless asked

**Passing the model to spawns — TOOL SHAPE MATTERS:**

The `model` parameter lives in DIFFERENT places depending on the spawn tool. Getting this wrong is a **silent footgun**: a misplaced `model` is dropped without error, and the session inherits the coordinator's default model instead.

**`task` tool (CLI) — top-level `model`:**

```
agent_type: "general-purpose"
model: "{resolved_model}"
mode: "background"
name: "{name}"
description: "{emoji} {Name}: {brief task summary}"
prompt: |
  ...
```

**`create_session` tool (App mode) — `model` at top level, NOT inside `kickoff`:**

```
project_id: "{project_id}"
name: "{Name} {verb}ing {noun}"
coordinate_with_creator: true
notify_on_idle: "once"
model: "{resolved_model}"        // ← model lives HERE, at top level of create_session
kickoff: {
  "mode": "autopilot",
  "prompt": "..."
}
```

⚠ **CRITICAL:** On `create_session`, the model goes at the **top level** of the tool call (parallel to `project_id`, `name`, etc.), NOT inside `kickoff`. A model inside `kickoff` is silently ignored — the session launches on the platform default instead. Third-party model IDs require the full UUID-prefixed form (`{uuid}/provider/model`); bare short IDs like `moonshotai/kimi-k3` will fail with "model provider not found" unless fully resolved.

Only set `model` when it differs from the platform default. If the resolved model IS the platform default, you MAY omit `model` entirely.

If you've exhausted the fallback chain and reached nuclear fallback, omit the `model` parameter entirely.

**Spawn output format — show the model choice:**

When spawning, include the model in your acknowledgment:

```
🔧 Fenster (claude-sonnet-4.6) — refactoring auth module
🎨 Redfoot (claude-opus-4.5 · vision) — designing color system
📋 Scribe (claude-haiku-4.5 · fast) — logging session
⚡ Keaton (claude-opus-4.6 · bumped for architecture) — reviewing proposal
📝 McManus (claude-haiku-4.5 · fast) — updating docs
```

Include tier annotation only when the model was bumped or a specialist was chosen. Default-tier spawns just show the model name.

**Resolving model IDs (once per session — no static catalog):**

Two ID shapes: **bare** (`claude-haiku-4.5`, first-party) and **prefixed** (`{uuid}/{vendor}/{model}`, all models). Catalog shifts over time — resolve at spawn, don't hardcode.

**HARD GATE — run before the first `create_session`/`task` spawn of the session.** If you skip this and pass a short ID straight through, `create_session` fails with `model provider not found` and you waste a spawn cycle.

1. **Discover (HARD GATE):** BEFORE the first spawn, call `create_session` with `model: "__discover__"` at the **top level** (parallel to `project_id`, `name`, etc., NOT inside `kickoff`). The response returns the live catalog of valid prefixed IDs. Copy the IDs verbatim — never hand-build the `{uuid}`. Cache the resolved short→prefixed mapping in **coordinator session memory only** (e.g. a `model_resolution` row in the session SQL DB, or an in-prompt variable). If `__discover__` fails for any reason, fall back to writing the prefixed ID directly into `.squad/config.json`.
2. **Pick prefixed form.** `create_session` needs prefixed ALWAYS for third-party providers — bare short IDs like `moonshotai/kimi-k3` are rejected with "model provider not found". `task` accepts bare first-party but rejects bare third-party. Prefixed works everywhere — use it. When in doubt, use prefixed.
3. **Validate:** throwaway spawn with chosen model. If it launches, the prefixed ID is good for the rest of this session. Re-run the gate only if a new provider appears or the session restarts.

**Tiers (vendor-agnostic):**

- Premium (architecture, deep analysis, complex planning): `moonshotai/kimi-k3`, `z-ai/glm-5.2`
- Standard (code, refactoring, tests): `mai-code-1-flash-picker`, `openai/gpt-5.6-luna-pro`
- Fast (docs, logs, triage, mechanical): `openai/gpt-5.6-luna`, `gemini-3.5-flash-lite`


**On `model provider not found` / `Model not available`:** short ID given to a tool that needs prefixed. Re-resolve (step 1), use prefixed form, retry. If resolution fails, edit config to add the prefixed ID — that's a permanent fix, not a workaround.
