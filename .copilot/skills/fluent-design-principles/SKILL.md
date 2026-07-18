---
name: fluent-design-principles
description: >
  Platform-agnostic UI/UX design principles distilled from Microsoft's Fluent 2
  design system (fluent2.microsoft.design). USE FOR: visual hierarchy, layout,
  spacing, grid, responsive design, breakpoints, color roles and semantic color,
  typography and type ramps, elevation and shadows, corner radius and shape,
  iconography usage, motion and animation principles, loading/wait UX, empty
  states, onboarding UX, accessibility and WCAG contrast, content design, voice
  and tone, design tokens, theming (light/dark/high-contrast), interaction
  states (rest/hover/pressed/focus/selected), touch target sizes, and general
  UI/UX design reviews of pages or components.
  DO NOT USE FOR: Fluent UI Blazor NuGet package usage, Fluent* component APIs,
  providers, or Blazor-specific implementation (use the fluentui-blazor skill),
  or any platform-specific component library code (React/iOS/Android/Windows).
---

# Fluent Design Principles — Platform-Agnostic UI/UX Guidance

This skill teaches the **design language and UX principles** of Microsoft's Fluent 2 design system, independent of any operating system, device, or implementation framework. Use it to make and review design decisions: hierarchy, layout, color, type, elevation, motion, accessibility, and content.

> **Scope boundary:** This skill covers *what* good design looks like and *why*. For *how* to build it with the Fluent UI Blazor NuGet package (`FluentButton`, providers, etc.), use the `fluentui-blazor` skill. This skill never prescribes framework APIs.

Source: scraped from https://fluent2.microsoft.design (Design principles, Color, Typography, Elevation, Motion, Layout, Shapes, Iconography, Material, Accessibility, Content design, Design tokens, Onboarding, Wait UX).

---

## 1. Core Design Principles

Four principles guide every Fluent design decision. Evaluate UI work against them:

| Principle | Functional meaning | Emotional payoff |
|---|---|---|
| **Natural on every platform** | Adapt to the device/context; build on familiar patterns. Reuse native conventions ~80% of the time; spend effort on signature moments. | Reliability, trust |
| **Built for focus** | Remove visual clutter and noise; draw people toward the next action; never get in the way. | Centered, calm, confident |
| **One for all, all for one** | Design for the full range of human ability. Solve for one, extend to many. Inclusion is a foundation, not a feature. | Belonging |
| **Unmistakably Microsoft** | Signature color, type, icons, and motion create coherence and brand recognition. | Personality |

**Review heuristic:** When a design feels wrong, name which principle it violates (e.g., "three competing CTAs on one screen" violates *Built for focus*).

---

## 2. Layout, Spacing & Responsive Design

### Spacing & proximity

- **Proximity = relationship.** Elements close together are perceived as related; more space weakens the perceived relationship. Use spacing — not divider lines — to create logical sections.
- **Empty space creates hierarchy.** Elements with more surrounding space draw focus. Dense information is disorienting; white space lets the eye rest.
- **Global spacing ramp — base unit 4px.** All spacing is a multiple of 4, with 2/6/10 added to pad icons onto the 4px grid. Measure from an element's bounding box.
  - Ramp: `0, 2, 4, 6, 8, 10, 12, 16, 20, 24, 28, 32, 36, 40, 48, 52, 56` (Fluent tokens: `sizeNone`=0 … `size560`=56; token number = 25 × px value).
- **Apply spacing at three levels:** component (small spacers, implied relationships), pattern (consistent rhythm across screens), layout (direct the eye to what matters).
- **Use spacers as a tool, not a rule.** Adjust for visual rhythm — e.g., left-align text but center-align icons in list rows. Don't keep identical spacing if it breaks a pattern.
- **Touch targets:** minimum **44×44** (web/iOS convention) or **48×48** (Android convention). Never shrink interactive targets below 44px in the smallest dimension.

### Grid

- Anatomy: **columns** (12-column framework is standard — divides into halves/thirds/fourths/sixths), **gutters** (multiples of the 4px base unit, may change per breakpoint), **margins** (fixed or percentage, change per breakpoint), **regions** (important content takes the most grid area).
- Grid types: **column** (default for app layouts), **baseline** (dense horizontal rows aligning text for vertical rhythm), **manuscript** (single column, optimal line length for reading), **modular** (columns × rows matrix for dashboards/galleries).

### Alignment

- Consistent horizontal rhythm drives legibility.
- **Align objects (icons/images) centrally; align text left** (for LTR).
- Central alignment concentrates focus on one spot — use deliberately.
- Baseline alignment of text across columns creates vertical rhythm.

### Responsive design

**Breakpoints (px):**

| Name | Range |
|---|---|
| small | 320–479 |
| medium | 480–639 |
| large | 640–1023 |
| x-large | 1024–1365 |
| xx-large | 1366–1919 |
| xxx-large | 1920+ |

- **Responsive** = one fluid layout that adapts. **Adaptive** = distinct fixed layouts per size (progressive enhancement). Mix as needed; prefer responsive unless content fundamentally changes per device.
- **Five responsive techniques:** **Reposition** (restack for reading order), **Resize** (adjust size/margins), **Reflow** (1 column → 2 columns above the fold), **Show/hide** (progressive metadata disclosure), **Re-architect** (collapse master→detail on small screens; side-by-side on large).

---

## 3. Color

Three palettes, three distinct jobs:

| Palette | Role | Rules |
|---|---|---|
| **Neutral** | Surfaces, text, layout scaffolding; state changes | Use **lighter neutrals on surfaces** to highlight primary focus areas and build hierarchy |
| **Shared** | Cross-product recognition (avatars, calendars, badges, personas) | Use **sparingly** as accents; in dark mode they shift saturation/brightness to reduce eye strain |
| **Brand** | Product identity; anchors the user in the experience | Buttons/CTAs and selected states only. **Never** flood large surfaces — dilutes hierarchy |

### Semantic colors

- Red = danger/error, yellow = caution/warning, green = success/positive, (plus info). They communicate **feedback, status, urgency** — always.
- **Never use semantic colors decoratively.** A green badge must mean success.
- **Never rely on color alone.** Pair with icon, text, or shape so meaning survives color-blindness.

### Interaction states

- **Ordered darkening:** a control gets darker as interaction progresses: rest → hover → pressed → selected. (Darkest = most committed state.)
- **Focus is a stroke, not a fill change.** Focused controls keep their color and gain a **thicker stroke/outline** — this distinguishes keyboard navigation from pointer hover.
- Keep state treatment consistent across all interactive elements.

### Color accessibility

- Standard text: contrast ≥ **4.5:1** against background.
- Large text (>18.5px bold or >24px regular): ≥ **3:1**.
- Interactive components and meaningful icons/graphics: ≥ **3:1** against adjacent colors.
- Support personalization (light/dark/high-contrast) via tokens, not one-off overrides.

---

## 4. Typography

- **Prefer the platform's native/system typeface** for familiarity, performance, and accessibility; use a brand typeface (Fluent uses Segoe) only where it renders natively.
- **Use a semantic type ramp**, not ad-hoc sizes. Fluent's web ramp (size/line-height, px):

| Style | Weight | Size / Line |
|---|---|---|
| Caption 2 | Regular/Semibold | 10 / 14 |
| Caption 1 | Regular/Semibold/Bold | 12 / 16 |
| Body 1 | Regular/Semibold/Bold | 14 / 20 |
| Subtitle 2 | Semibold/Bold | 16 / 22 |
| Subtitle 1 | Semibold | 20 / 26 |
| Title 3 | Semibold | 24 / 32 |
| Title 2 | Semibold | 28 / 36 |
| Title 1 | Semibold | 32 / 40 |
| Large Title | Semibold | 40 / 52 |
| Display | Semibold | 68 / 92 |

- **Semantic roles, not raw sizes:** pick `Body 1` vs `Caption 1` vs `Title 2` by the content's role in the hierarchy. This keeps scannability and lets theming swap the ramp.
- **Casing:** sentence case almost everywhere. Never use ALL CAPS for emphasis — it's hard to read.
- **Alignment:** left-align body text (LTR). Right-align only RTL languages or short side notes. Center only small copy meant to draw attention. Never right-align or center long passages.
- **Text color = hierarchy lever:** standard text color for body, lighter neutral to de-emphasize (metadata, captions), brand color sparingly for prominence.
- Respect contrast minimums (4.5:1 / 3:1 large).

---

## 5. Elevation & Depth

- Elevation = perceived z-axis distance via shadow. Use it to create hierarchy and focus, not decoration.
- **Physics:** one consistent light source (consistent shadow direction). Sharp/crisp shadows read as *close*; large/soft shadows read as *distant*.
- Shadows combine a **key** shadow (sharp, directional, defines edges) + an **ambient** shadow (soft, diffused, implies distance).

**Shadow ramp** (name = blur px; light-theme formula: Blur = n, Y-offset = 0.5×n, X = 0, opacity ~14% key + softer ambient):

| Token | Typical usage |
|---|---|
| `shadow2` | Pressed cards, pressed FABs |
| `shadow4` | Resting cards, grid items, list items |
| `shadow8` | FABs, raised cards, command bars, dropdowns, tooltips |
| `shadow16` | Callouts, hover cards |
| `shadow28` | Bottom sheets, side navigation, raised tab bars |
| `shadow64` | Dialogs, panels (modal surfaces) |

- Dark theme roughly doubles shadow opacity (e.g., 28% key).
- **Colored surfaces:** the neutral ramp looks wrong on brand colors. Adjust shadow opacity by surface luminosity — $L = 0.2126R + 0.7152G + 0.0722B$; key opacity ≈ `Round(42 − 0.116L)`, ambient ≈ `Round(34 − 0.09L)`. Use brand-shadow tokens rather than the neutral ramp on colored surfaces.

### Materials (surface treatments)

| Material | Character | Use for |
|---|---|---|
| **Solid** | Opaque; color + elevation | Default surfaces; light/dark aware |
| **Acrylic** | Semi-transparent frosted glass | Transient, light-dismiss surfaces (menus, popovers) |
| **Mica** | Opaque, subtly tinted | Window-level surfaces; signals focus state |
| **Smoke** | Translucent dark dim layer | Beneath modals to signal blocked interaction (always dark) |

Rule: match surface treatment to hierarchy and dismissability — transient UI gets translucency; blocking UI dims what's beneath.

---

## 6. Shape & Stroke

**Four forms carry meaning:**

- **Rectangle** — default for buttons, cards, menus, inputs, images.
- **Circle** — people: avatars, personas, presence.
- **Pill** — tracks and selections: sliders, toggles, tags, filters.
- **Beak** — anchors a floating surface (callout, popover) to its source element.

Forms are distinguished by **fill** (emphasis) or **border** (unfilled containers like cards).

**Corner radius ramp:**

| Token | Value | Usage |
|---|---|---|
| None | 0 | Navigation bars, tab bars, screen-edge elements |
| Small | 2px | Small badges; any shape < 32px |
| Medium | 4px | **Default** — buttons, dropdowns, inputs |
| Large | 8px | Large buttons, larger containers |
| X-Large | 12px | Bottom sheets, popovers |
| Circle | 50% | Personas/avatars |

- **Don't round** where it creates awkward gaps (e.g., between the two halves of a split button) or on elements flush to a screen edge.
- Nested rounded surfaces: inner radius < outer radius (keep corners concentric).

**Stroke:**

- Thickness tokens: Thin 1px, Thick 2px, Thicker ~3–4px, Thickest ~4–6px. Scale stroke with element size so visual weight feels constant (e.g., 2px ring on small avatars → 4px on large).
- Always **rounded stroke caps**; avoid square caps.
- Dash arrays scale proportionally from stroke thickness n.

---

## 7. Iconography

- Icons have **semantic purpose** — they represent concepts, objects, or actions and must be recognizable at a glance.
- **Two visual themes:** **Regular/outline** for wayfinding (available actions: download, launch, settings) and **Filled** for selected states or small moments needing extra weight.
- **Size matches input precision:** small icons (≤12–16px) *inform only* — never make them interactive. Use larger icons on touch/small screens to hit touch-target minimums.
- Use icons at their designed sizes; don't arbitrarily scale (product icons simplify below 48px; scale up by factors of 4: 48/64/96/192).
- **Naming/metaphor:** good icons are literal metaphors of the shape ("shield"), not abstract functions ("security").
- **Modifiers** (badge overlays like a plus or check): filled style, bottom-right corner, and only if they don't add visual complexity.
- **Color:** at most one solid color on a system icon, minding contrast. Never recolor brand/product icons.
- **Localization:** validate cultural connotations — symbols acceptable in one culture may offend in another.
- Pair icons with text labels for anything not universally understood; icon-only buttons need tooltips and accessible names.

---

## 8. Motion & Animation

**Four principles:** **Functional** (purposeful — shows next steps, confirms change, celebrates), **Natural** (obeys physics — inertia, weight, velocity), **Consistent** (same vocabulary everywhere), **Appealing** (delight without delay).

**Duration & easing:**

- Scale duration to element size and travel distance — larger elements get more time. Fast and smooth; never make people wait on an animation.
- Easing vocabulary: **ease-out** (fast→slow) for entrances — the default for UI appearing; **ease-in** (slow→fast) for exits; **ease-in-out** for moves/transforms; **linear only** for constant-rate loops like rotation.

**Four transition patterns:**

1. **Enter/exit** — menus, dialogs, transient UI.
2. **Elevation** — depth changes (button press, drag-and-drop lift).
3. **Top-level navigation** — use a **quick fade**, not sliding, to avoid disorientation and false hierarchy.
4. **Container transform** — resize/reposition, common in responsive adaptation.

**Choreography:**

- **Stagger** group entrances with short offsets to soften arrival and direct the gaze. Only very large groups animate simultaneously.
- **Hierarchy:** important elements move more prominently and longer; group secondary elements with synchronized timing. Moving everything together creates ambiguity about what matters.

**Accessible motion (non-negotiable):**

- Honor reduced-motion settings (provide a no-motion mode per WCAG).
- No flashes or sudden jarring movement (seizure risk).
- Constrain motion to the focused element; peripheral motion distracts.
- Never make animation the only channel for information — provide equivalent state announcements.

---

## 9. Accessibility Foundations

Target: meet or exceed **WCAG 2.1 AA**.

- **Structure:** logical, predictable hierarchy. Headings in order — never skip levels or use headings for visual size alone. Scannable headings aid everyone.
- **Keyboard & focus:** every interactive element reachable and operable by keyboard. Focus order follows a Z-pattern (left→right, top→bottom). **Never lose focus** — after a dialog closes, focus returns to the invoking element.
- **Contrast:** text ≥ 4.5:1; large text ≥ 3:1; interactive/non-text UI ≥ 3:1 against adjacent colors.
- **Zoom & reflow:** content reflows without horizontal scrolling at **400% zoom** (design down to 320px width); text-only zoom to **200%** without clipping.
- **Media alternatives:** descriptive alt text for meaningful images/icons (empty alt for decorative); captions for video, customizable or high-contrast.
- **Meaningful text:** plain, concise language; every sentence communicates something new; avoid jargon (also helps non-native speakers).
- **Semantics:** use correct structural roles/landmarks; follow WAI-ARIA authoring practices only where native semantics fall short.
- **Spec accessibility:** design deliverables should document focus order, screen-reader annotations, and semantic structure — as rigorously as padding and color.

---

## 10. Content Design (Voice & Tone)

Content is a design material — treat words like iconography or color.

**Frame every string with three questions:** Who is the audience? What are they trying to accomplish? How do they feel right now (anxious, confused, successful)?

**Style rules:**

- **Keep it simple** — plain language, short sentences and fragments. Scannable beats literary.
- **Get to the point fast** — prune every excess word; make next steps obvious.
- **Talk like a person** — conversational, one-on-one.
- **Present tense**, **active voice** (passive only to avoid blaming or naming the system).
- **Second person** (you/your). Avoid first person (we/our) unless speaking as the user's own intent (e.g., a button the user mentally reads as "I want to…").
- **Sentence case** by default (easier to read and localize); adapt to host-platform conventions when they differ.
- **Punctuation:** question marks always; periods only after full sentences — not in headers, buttons, labels, or list items; exclamation points only for genuine celebration.
- **Links:** short, descriptive destination text — never "Click here."
- **No directional terms** ("above", "below", "to the right") — assumes sight and localizes poorly.
- **Inclusivity:** nonjudgmental helper language ("Need help?" not "Stuck?").

---

## 11. Wait UX (Loading & Progress)

Moments of delay define trust. Handle them deliberately.

**Principles:**

1. **Communicate clearly and honestly** — descriptive labels ("Uploading photo …") beat bare spinners. Use determinate progress when measurable; indeterminate only for short waits.
2. **Optimize perceived performance** — skeletons/shimmer shift attention from the wait. Never blank screens, never static loaders with no label.
3. **Maintain context** — one indicator tied to the user's action; keep the user in the same view; never scatter competing spinners.

**Timing thresholds:**

| Wait | Treatment |
|---|---|
| < 1 second | **No indicator** — a flash of UI confuses |
| 1–3 seconds | Spinner + short "-ing …" label |
| > 3 seconds | Progress bar or reassuring content string |
| Conversational/AI | Respond/acknowledge immediately to maintain flow |

**Pattern selection:**

- **Spinner** — short indeterminate waits; label with -ing verb + nonbreaking space + ellipsis ("Searching …").
- **Progress bar** — longer measurable waits; label above ("Uploading file …"), optional status below ("30% complete — about 20 seconds"); include recovery instructions if abandoning has consequences.
- **Skeleton** — content rendering delays; mirror the incoming layout; shimmer.
- **Progress toast** — long background processes the user shouldn't have to watch; clear label + status updates.

**Content:** -ing verbs for in-progress, past tense for completion ("File uploaded"); avoid passive ("File is being uploaded" ✗); one short phrase; give time estimates when knowable; fallback strings like "Working on it …" when details are unavailable; announce state changes to assistive technology (status-role semantics).

---

## 12. Onboarding UX

**Five principles:** **Relevant** (contextual to the current task), **Non-distracting**, **Optional** (dismissible and resumable), **Benefit-focused** (state value up front), **Coherent** (built from standard components).

**Match goal → surface:**

| Goal | Surface | Rules |
|---|---|---|
| Welcome | Once-only screen/banner/modal | 1–2 points max |
| Orient | Empty states, teaching popovers | First-use moments |
| Notify | Banners, toasts, message bars | New features/changes |
| Explain | Empty states with CTA, hints at point of need | Just-in-time, not front-loaded |
| Take action | Setup wizard, multi-step flow | Show progress; set expectations with step/time counts |

Write CTAs that complete "I want to …". Avoid surprises unless delightful.

---

## 13. Design Tokens & Theming

- **Never hardcode hex codes or pixel values.** Two token layers:
  - **Global tokens** — context-agnostic raw values (color hex, radius px, spacing, durations).
  - **Alias tokens** — semantic layer mapping globals to purpose (e.g., "surface background", "foreground subtle", "brand stroke"). Named so the function is obvious without knowing the value.
- Choosing an alias token means choosing a *role*, which keeps hierarchy consistent and makes light/dark/high-contrast/brand themes free.
- Complex values (shadows, type styles) are condensed into single alias tokens.
- **Theming:** design for light, dark, and high-contrast from the start. Dark mode is not inverted light mode — shared/brand colors shift saturation and brightness to reduce eye strain, and shadow opacities increase.

---

## 14. Review Checklist

When reviewing a UI design or page, check in order:

1. **Focus** — one obvious primary action? Anything competing for attention? (Built for focus)
2. **Hierarchy** — do spacing, neutrals, type roles, and elevation point to what matters most?
3. **Semantics** — semantic colors/icons used only for meaning; states follow rest→hover→pressed→selected; focus shown by stroke.
4. **Spacing/grid** — 4px ramp, alignment rhythm, touch targets ≥ 44px.
5. **Type** — semantic ramp roles, sentence case, left-aligned body, contrast.
6. **Responsive** — breakpoint behavior chosen from Reposition/Resize/Reflow/Show-hide/Re-architect; reflows at 400% zoom.
7. **Motion** — purposeful, eased, short; reduced-motion honored; top-level nav fades.
8. **Waits** — thresholds respected (<1s none, 1–3s spinner, >3s progress); skeletons for content; no blank screens.
9. **Content** — plain, active, second-person, present tense; no "click here"; no directional terms.
10. **Accessibility** — 4.5:1/3:1 contrast, keyboard path with managed focus, alt text, logical headings, semantic structure.
11. **Tokens** — no hardcoded colors/sizes; alias roles chosen; dark/high-contrast themes considered.

## Anti-Patterns

- **Semantic color as decoration** — green/red/yellow must mean status, never style.
- **Color-only communication** — always pair with icon/text; fails color-blind users and WCAG.
- **Brand color flooding** — large brand-colored surfaces destroy hierarchy and navigation clarity.
- **ALL CAPS for emphasis** — harms readability; use weight or color instead.
- **Ad-hoc pixel values** — bypasses tokens, breaks theming and consistency.
- **Focus by fill change** — focus must be a visible stroke; fill changes read as state changes.
- **Blank or unlabeled loading** — no indicator under 1s, labeled spinner 1–3s, progress beyond; never a bare spinner for a long wait.
- **Sliding page transitions** — top-level navigation fades; slides create false spatial hierarchy.
- **Animating everything at once** — stagger and hierarchy direct attention; synchronized movement creates ambiguity.
- **"Click here" links and directional language** — meaningless to screen readers, poor localization.
- **Icon-only critical actions without labels/tooltips** — metaphors aren't universal.
- **Rounded corners everywhere** — skip at screen edges and within compound controls (split buttons).
