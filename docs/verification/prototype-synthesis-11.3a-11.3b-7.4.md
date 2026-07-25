# Prototype Synthesis: 11.3a + 11.3b + 7.4

> Append-only evidence. This is the Lead's review and synthesis of the three completed
> prototypes. It does not modify the three individual reports; it rules on their verdicts,
> grounds them in the foundation architecture (PRD, ARCHITECTURE, DATA_MODEL, ADRs), and
> names the downstream consequences.
>
> - `docs/verification/11.3a-vector-knowledge-base.{md,json}` — vector knowledge-base (Neo, #18, PR #96)
> - `docs/verification/11.3b-vector-user-search.{md,json}` — vector user-search (Neo, #19, PR #97)
> - `docs/verification/7.4-screenshot-extraction.{md,json}` — screenshot extraction (Dozer, #84, PR #98)
> - `docs/adr/0015-screenshot-extraction-approach.md` — 7.4's ADR

---

## 1. Executive summary

The three prototypes collectively establish that **the foundation architecture is sound at
MVP scale, and the two open toolchain decisions (vector index, screenshot extraction) are
now closed with measured evidence.** All three ran against the real stack (Aspire-managed
PostgreSQL 17.10 + pgvector 0.8.5; real ffmpeg on macOS arm64 and Linux arm64) with zero
external AI calls, per the synthetic-data prototype policy.

What is now **settled and safe to build on:**

- Document construction (DATA_MODEL §5), staleness-as-derived (ADR-0001), ADR-0004
  duplicate-per-video cardinality, and centroid construction (§3.23) all hold against real
  infrastructure. The Postgres `sha256` matches .NET `SHA256` hex byte-for-byte, so staleness
  is pure-SQL expressible — no application round-trip needed.
- **HNSW is the MVP vector index.** At 500 videos / 11,958 vectors / dim=384, HNSW and
  IVFFlat are both sub-millisecond (0.522 ms vs 0.509 ms) with near-identical recall (0.99 vs
  0.98) and trivial size. HNSW wins on operability (incremental build, no `lists`/`probes`
  tuning) — the right default for a self-hosted single-user product.
- **The §6 ranking formula is mechanically correct.** Hybrid blend, cluster aggregation,
  `relativeSimilarityPercent` over the pre-pagination top-200 pool, note/interaction boosts,
  and related-item discovery all compute exactly as specified.
- **ffmpeg is the screenshot engine**, in-process in the worker, against the Task 6.4
  temp-media file. yt-dlp was disproven as an independent extraction approach (it delegates
  to ffmpeg for all frame work).

What is **honestly not settled** (and must not be misread as proven):

- **Semantic recall quality.** Both vector prototypes used a synthetic embedder that jitters
  vectors from raw text hash, so paraphrase/vague-query recall is *inconclusive by
  construction*. The flat recall surface (r@1 0.005–0.030, r@10 0.195–0.225 across the whole
  blend sweep) is a synthetic artifact, not a design failure.
- **ADR-0012's absolute threshold VALUE.** The mechanism (gate at a fixed cosine) works, but
  synthetic cosines (own 0.4801, best-other 0.5931) are not on a real model's scale, and
  best-other > own means *no threshold separates* under synthetic geometry. The numeric value
  is deferred to real-model calibration.
- **The recommended ranking weights** (0.65/0.35 text/vector, 0.65/0.25/0.10 cluster) derive
  from mechanics and tie-points, not semantic differentiation. They are sensible starting
  points, not validated values.

**Overall confidence:** architecture-shape decisions — high confidence (measured, real
infrastructure). Ranking-quality values — explicitly unproven pending the real embedding
provider in Task 11.3.

---

## 2. Per-prototype review — CONFIRM / OVERTURN rulings

Ruling convention: **CONFIRM** means the verdict stands as the prototyper reported it;
**OVERTURN** means I reverse it with reasoning. I distinguish throughout between
**PROVEN-mechanism** (the plumbing computes correctly) and **PROVEN-value** (the specific
number/threshold/weight is validated). Conflating these is how a team ships untuned defaults
believing they were validated.

### 2.1 — 11.3a (vector knowledge-base, Neo, #18)

| # | Claim | Neo's verdict | Morpheus ruling | Grounding |
|---|-------|---------------|-----------------|-----------|
| 1 | Document construction (§5) | PROVEN | **CONFIRM** (mechanism) | 7 doc types inserted into the real `search_documents` shape against real Postgres. §5 lists exactly these 7 types. |
| 2 | Staleness derivation (ADR-0001) | PROVEN | **CONFIRM** (mechanism + value) | Postgres `sha256` ≡ .NET `SHA256` hex, 0 mismatches; simulated edit flips stale→True. This is the one place a *value* (byte-for-byte hash equality) is genuinely proven. |
| 3 | ADR-0004 dedup cardinality | PROVEN | **CONFIRM** (mechanism) | 40 shared resources × 3 videos → 3 doc rows, 3 parents, **1** embedding row. Cardinality verified by direct DB probe. The *magnitude* (1.34% saving at MVP scale) is a measurement, not a design verdict — and it is small, as ADR-0004's consequences predicted ("storage cost is trivial at MVP scale"). |
| 4 | Cluster centroid construction (§3.23) | PROVEN | **CONFIRM** (mechanism) | Weighted centroid of normalized child embeddings, 500 rows built. §3.23 says "weighted centroid is acceptable." |
| 5 | pgvector storage/index at MVP scale | PROVEN | **CONFIRM** (mechanism) + **refined value** | HNSW 0.522 ms / recall 0.99 / 23.9 MB; IVFFlat 0.509 ms / 0.98 / 19.6 MB; exact 3.22 ms. The *mechanics* are proven. The recall *values* are internal-consistency checks only (synthetic clusters flatter both indexes) — Neo's own boundary note says this correctly. The **HNSW recommendation** is a value judgment I confirm and promote to an ADR (see §6). |

**Neo wrote NO ADR.** I **OVERTURN** that process call — see §6. The technical verdicts all
stand; the ruling is about recording the index decision, not about its correctness.

### 2.2 — 11.3b (vector user-search, Neo, #19)

| # | Claim | Neo's verdict | Morpheus ruling | Grounding |
|---|-------|---------------|-----------------|-----------|
| 1 | Hybrid blend (textWeight/vectorWeight) | PROVEN (mechanism) | **CONFIRM** (mechanism only) | Real `ts_rank_cd` + `pg_trgm` + real pgvector `<=>` execute; blend combines monotonically. The *recall differentiation* is flat — Neo correctly labels this a synthetic-boundary artifact. The mechanism is proven; no weight *value* is. |
| 2 | Cluster aggregation (0.65/0.25/0.10) | PROVEN (mechanism) | **CONFIRM** (mechanism only) | Formula computes correctly; weight sets do not differentiate on a uniform corpus (10 tight clusters × 50 videos). The specific 0.65/0.25/0.10 choice is **not evidence-differentiated** — confirmed as mechanically sound, explicitly unvalidated as a value. |
| 3 | `relativeSimilarityPercent` over pre-pagination top-200 | PROVEN | **CONFIRM** (mechanism + spec-conformance) | This is the strongest 11.3b result. §6 mandates min-max over the top-200 candidate pool *before* pagination; the prototype confirms it spreads well and handles the max==min→100 edge. This is a genuine spec-conformance proof, not just plumbing. |
| 4 | Note/interaction boosts | PROVEN (plumbing) | **CONFIRM** (plumbing only) | Boosts apply correctly and are bounded per §6. They do not move recall on a uniform corpus — so their *product value* is unproven, only their boundedness. |
| 5 | ADR-0012 absolute threshold | INCONCLUSIVE (value) | **CONFIRM the INCONCLUSIVE** | This is the honest answer. Mechanism works (pass rates collapse monotonically); value is unprovable synthetically (own 0.4801 < best-other 0.5931). Deferral to real-model calibration is correct — see §6 / Q4. |
| 6 | Related-item discovery via centroids | PROVEN | **CONFIRM** (mechanism) | Top-5 nearest centroids all same-topic at 0.89–0.94 cosine. §3.23 scopes this to "coarse related-item discovery" — exactly what was exercised. The tight 0.89–0.94 separation is a synthetic-geometry artifact; the *plumbing* is what's proven. |
| 7 | Paraphrase/vague semantic recall | INCONCLUSIVE (by construction) | **CONFIRM the INCONCLUSIVE** | Synthetic embedder jitters from raw query text → no semantic invariance exists for any blend to exploit. This is the single most important boundary in the whole synthesis: the killer journey (PRD §2.1, §4.2 "search a vague project idea") is **not yet evidenced**. |

**Neo wrote NO ADR.** I **CONFIRM** that call — §6's formula was not changed; the findings
refine default weight *ranges* without overturning any decision. See §6.

### 2.3 — 7.4 (screenshot extraction, Dozer, #84)

| # | Claim | Dozer's verdict | Morpheus ruling | Grounding |
|---|-------|-----------------|-----------------|-----------|
| 1 | Frame quality | PROVEN (ffmpeg) | **CONFIRM** | PSNR=inf (pixel-identical) on keyframe-aligned seeks, both paths. |
| 2 | Timestamp accuracy | PROVEN (ffmpeg) | **CONFIRM** (mechanism + bounded value) | 0-frame error on keyframe; **+1 frame (~30 ms @30fps) on non-keyframe** because `-ss` decodes to the next frame at/after target. The bound is measured and small. See §5/Q6 for why it doesn't matter. |
| 3 | File size + WebP encode | PROVEN | **CONFIRM** | WebP 4.3–5.4× smaller than PNG @q80; encode 6–42 ms. |
| 4 | Wall-clock speed | PROVEN (ffmpeg) | **CONFIRM** (with honesty caveat) | Extract 44–80 ms local vs 409–470 ms download-then-extract. Dozer correctly labels the download delta (363–394 ms over **local HTTP**) a *best-case lower bound*, not real YouTube. The conclusion is robust to that substitution because Path B's extract step *is* Path A. |
| 5 | Dependency footprint | PROVEN (ffmpeg) | **CONFIRM** | ffmpeg single binary, no runtime; yt-dlp drags ~102 MB (Python 88 MB + venv 14 MB) via pipx. |
| 6 | Temp-media lifecycle fit | PROVEN (ffmpeg) | **CONFIRM** | Operates on the Task 6.4 temp file inside quota; Path B re-downloads outside the lifecycle. |
| 7 | Failure modes | PROVEN (ffmpeg) | **CONFIRM** | audio-only → exit 234 no file; truncated → exit 183 no file; yt-dlp `--download-sections` fails outright on moov-at-end and re-encodes. Clean, detectable, no partial writes — exactly what "never load-bearing" needs. |
| 8 | Container complexity | PROVEN (ffmpeg) | **CONFIRM** | +543 MB single apt layer (326→869 MB); +659 MB with Python/yt-dlp (985 MB). Debian ffmpeg has libwebp natively; Homebrew's lacks it (toolchain-skew finding, recorded). |
| — | **The task's own framing** | **DISPROVEN** | **CONFIRM the disproof** | The plan posited ffmpeg-vs-yt-dlp as two candidate extraction approaches. Dozer proved yt-dlp has no native frame extraction and delegates to ffmpeg (byte-identical PSNR on all 4 targets). Reframing the comparison as extract-local vs download-then-extract is a *success of the prototype process*, not a failure — the plan's assumption was tested and corrected before production code depended on it. |

**Dozer wrote ADR-0015** (required by the task). I **CONFIRM** ADR-0015 — see §6.

**Measured vs analyzed (preserved):** macOS arm64 + Linux arm64 (Docker, native) were
*measured*. Windows ARM was *analyzed, never measured* — Dozer labeled it packaging-availability
evidence, and I preserve that distinction. No Windows ARM timing or size number is treated as
measured anywhere in this synthesis.

---

## 3. Successes — what is now settled and safe to build on

These are confirmed against the specific doc clause cited; downstream tasks may rely on them
without re-validation.

1. **The knowledge-base storage shape works.** §5 document construction, §3.21
   `search_documents`, §3.22 `embeddings`, §3.23 `video_cluster_embeddings` all hold on real
   Postgres + pgvector. (11.3a claims 1/3/4.)
2. **Staleness is pure-SQL derivable.** ADR-0001's "computed view or function" ergonomic is
   validated: Postgres `sha256(convert_to(coalesce(title_effective,'') || E'\n' ||
   coalesce(body_effective,''),'UTF8'))` ≡ .NET `SHA256` hex, 0 mismatches. Task 11.x can
   derive staleness database-side without trusting a stored flag. (11.3a claim 2.)
3. **HNSW is the MVP index.** Measured at 500 videos / 11,958 vectors: build 979 ms, query
   0.522 ms, recall@10 0.99, 23.9 MB. Incremental build in pgvector 0.8.5, no tuning. Now
   recorded as ADR-0016 (§6). (11.3a claim 5.)
4. **The §6 ranking formula is mechanically correct.** Blend, cluster aggregation, boosts,
   related-items all compute as specified. Task 12.3 implements against a validated formula.
   (11.3b claims 1/2/4/6.)
5. **`relativeSimilarityPercent` must be computed over the pre-pagination top-200 pool.**
   This is a spec-conformance proof of §6's exact requirement — normalize post-pagination and
   you compress the visible range. Task 12.3 must not deviate. (11.3b claim 3.)
6. **ffmpeg is the screenshot engine, in-process in the worker, against the temp-media
   file.** ADR-0015. 44–80 ms/frame, WebP q80 via libwebp (native in the Debian container),
   clean no-partial-file failures. (7.4 all axes.)
7. **The pgvector AppHost pin is production-needed and already merged.** Stock `postgres`
   lacks the extension; `pgvector/pgvector:pg17` is pinned in `src/StreamingDigest.AppHost/
   AppHost.cs` with the data-volume gotcha documented. (11.3a AppHost note.)

---

## 4. Failures, gaps, and disproofs — what the synthetic constraint could not answer

**INCONCLUSIVE ≠ PROVEN.** Every item below is an open question the prototypes honestly could
not settle. None of them is a design failure; all of them are *evidence gaps* that must land
in a named task or issue (see §7).

1. **Semantic recall for vague/paraphrase queries — UNPROVEN.** This is the product's killer
   journey (PRD §2.1: "searches for a vague project idea, and immediately finds the relevant
   video cluster"; §4.2; ARCHITECTURE §4.10's top-3 target). The synthetic embedder has no
   semantic invariance, so the entire blend sweep is flat (r@1 0.005–0.030, r@10 0.195–0.225).
   The architecture *permits* the journey; nothing yet *evidences* it. This is the single
   largest outstanding risk in the MVP.
2. **ADR-0012's absolute threshold value — DEFERRED, not chosen.** The `search.
   highSignalThresholdPercent` default of 80 is uncalibrated. Under synthetic geometry no
   threshold separates own from best-other. The value must be set from the real model's
   own-vs-best-other cosine gap distribution (suggested sweep 0.70–0.98).
3. **Ranking weight values — sensible defaults, not validated.** 0.65/0.35 text/vector and
   0.65/0.25/0.10 cluster aggregation derive from mechanics and tie-points on a flat recall
   surface. They are starting points for Task 12.3, explicitly subject to re-tuning once a
   real provider exists.
4. **Cluster aggregation weight differentiation — UNPROVEN.** The corpus (10 tight,
   well-separated clusters, 50 videos each) is too uniform for 0.65/0.25/0.10 vs alternatives
   to matter. Needs a corpus with overlapping/multi-topic videos.
5. **Cross-topic separation quality — UNPROVEN.** Synthetic clusters are perfectly separated
   by construction; real recall across adjacent topics (e.g. blazor-ui vs dotnet-performance)
   is unmeasured.
6. **HNSW vs IVFFlat at >MVP scale (~2,000 videos / ~48k vectors) — extrapolated, not
   measured.** 11.3a's boundary note says this plainly; Task 12.8's 2,000-video dataset is
   where it gets re-checked.
7. **7.4's disproven framing — a genuine success.** The plan assumed ffmpeg-vs-yt-dlp was a
   two-horse race; it is not. Catching a wrong framing in a cheap prototype (before Task 7.5
   baked yt-dlp into the worker) is exactly what the prototype-early policy is for. This is
   recorded as a success of the process, not a black mark.
8. **The 447 ms hybrid latency — an implementation threat, NOT a design disproof.** See Q3
   below. It is a real architectural result (per-query tsvector construction is the dominant
   cost) but it threatens the *implementation approach*, not the §6 formula.

---

## 5. Architecture doc changes required

Concrete edits, by file and section. These are the *owed* doc changes the prototypes surface.

### 5.1 `docs/architecture/DATA_MODEL.md`

- **§3.22 `embeddings` — Indexes bullet.** Currently: *"HNSW or IVFFlat vector index depending
  pgvector version and dataset size."* **Edit to:** *"HNSW vector index (ADR-0016). pgvector
  0.8.5 builds HNSW incrementally with no `lists`/`probes` tuning; measured at MVP scale
  (11.3a) as sub-millisecond with recall headroom. IVFFlat is the documented fallback if a
  future scale measurement (Task 12.8, ~2,000 videos) favors it."* This is the §3.22 edit the
  brief asks about — **yes, §3.22 should now name HNSW**, because Task 11.3's implementer
  needs to know which index to build and the current text doesn't tell them.
- **§6 Hybrid search implementation notes — add a latency/implementation note.** After the
  "Text search" bullets, add: *"The text side must use a **materialized `tsvector` generated
  column** over weighted `title_effective`/`body_effective` (with the GIN index built on it),
  not per-query `to_tsvector` construction. 11.3b measured per-query tsvector + `ts_rank_cd` +
  GROUP BY over 12k documents at ~447 ms/query; the same query shape with materialized
  tsvector is the Task 12.3 requirement (see 11.3b findings §2)."* §6's *formula* is unchanged;
  this is an implementation-requirement note the formula's text-search bullet currently omits.
- **§3.21 `search_documents` — Indexes bullet (minor consistency).** The "Full-text GIN on
  weighted `title_effective` + `body_effective`" bullet should reference the materialized
  tsvector column from the §6 note, so the table definition and the search implementation note
  don't drift.

### 5.2 `docs/architecture/ARCHITECTURE.md`

- **§4.5 Search flow — add an implementation note to step 4/5.** Currently steps 4–5 describe
  the hybrid search abstractly. Add: *"Implementation: text scoring uses a materialized
  `tsvector` column (DATA_MODEL §6); per-query tsvector construction is the dominant hybrid
  cost and is not acceptable (11.3b)."* Keeps the flow doc and the data-model note aligned.
- **§12 Cross-platform development notes — add the toolchain-skew finding.** Add: *"Homebrew
  ffmpeg (macOS dev) lacks libwebp and drawtext; the Debian/Ubuntu container build has both.
  WebP encoding and any burned-timestamp diagnostics work in the container, not necessarily on
  a Homebrew host (7.4)."* This is a real dev-vs-production skew that will bite a developer
  running Task 7.5 locally.

### 5.3 `docs/product/PRD.md`

- **No edit required.** The PRD's screenshot (§2.2) and search (§2.5) requirements are
  satisfied by the prototype findings as-is. Silence here is correct — the PRD states product
  intent, not implementation mechanism, and nothing the prototypes found contradicts a PRD
  requirement.

### 5.4 New convention doc — `spikes/README.md` (see Q8)

- Create `spikes/README.md` stating the standing convention: spike code is throwaway, lives
  outside `StreamingDigest.slnx`, is kept on `main` as primary-source evidence for its
  `docs/verification/` report, and must never be referenced from `src/` or `tests/`.

---

## 6. ADR rulings

### 6.1 ADR-0015 (screenshot extraction) — **CONFIRMED**

ADR-0015 is well-formed per `.agents/skills/domain-modeling/ADR-FORMAT.md`: it records a
hard-to-reverse toolchain decision (bake ffmpeg into the worker image), the surprising
reframing (yt-dlp is not a frame extractor), the rejected alternatives (yt-dlp as extractor;
sidecar/separate Aspire resource), and non-obvious consequences (Homebrew/Debian skew,
+1-frame tolerance, clean-failure contract feeding 7.5's placeholder design). It closes the
"screenshot extraction approach" open implementation decision. **No amendment needed.**

### 6.2 11.3a — **OVERTURN "no ADR"; ADR-0016 is required**

Neo's reasoning was: "HNSW refines §3.22 without overturning it, and the task said record an
ADR only if findings *change* a storage/index decision." That misreads the ADR bar. Per
ADR-FORMAT, an ADR is warranted when a decision is **hard to reverse**, **surprising without
context**, and **the result of a real trade-off**. Choosing HNSW over IVFFlat is all three:

- **Hard to reverse:** the index is baked into the migration that creates `embeddings`;
  swapping index types at production scale means a rebuild under write load.
- **Surprising without context:** §3.22 explicitly left "HNSW or IVFFlat" open — a future
  reader (Task 11.3's implementer, today) has no recorded reason to pick one.
- **Real trade-off:** measured — HNSW costs ~2.3× build time and ~1.22× size for a marginal
  recall edge; the choice was made for *operability* (incremental build, no tuning), which is
  exactly the kind of "why" an ADR exists to preserve.

A verification report is evidence; it is not a decision record. The implementer reads
DATA_MODEL and the ADR index, not a prototype's findings section.

**ADR-0016 (proposed): "MVP vector index is HNSW, not IVFFlat."**
*Assertion:* The `embeddings` (and `search_query_embeddings`, §3.25) vector index is HNSW.
pgvector 0.8.5 builds HNSW incrementally with no pre-population requirement and no
`lists`/`probes` tuning; measured at MVP scale (500 videos / 11,958 vectors / dim=384) it is
sub-millisecond (0.522 ms) with recall@10 0.99 — the right operability default for a
self-hosted single-user product. IVFFlat (0.509 ms / 0.98 / smaller) is the documented
fallback if a future ~2,000-video measurement (Task 12.8) reverses the trade-off. Refines
DATA_MODEL §3.22, which left the choice open. Synthetic recall numbers are
internal-consistency checks; the HNSW choice rests on build/query mechanics and operability,
not on the recall values.

### 6.3 11.3b — **CONFIRM "no ADR"**

11.3b changed no §6 formula term and overturned no decision. Its recommended weight ranges are
explicitly caveated as mechanics-derived starting points, and its one hard finding (materialize
the tsvector) is an implementation requirement that belongs in DATA_MODEL §6 (§5.1 above), not
an architectural decision — there is no real trade-off to record ("make the hot path fast" is
the obvious path, not a deviation from it). The decision note in
`.squad/decisions/inbox/neo-11-3b-vector-user-search.md` is the correct venue.

### 6.4 No other new ADRs

The 447 ms latency finding (Q3), the +543 MB image (Q5), and the +1-frame seek offset (Q6) are
all implementation/operational facts, not architectural decisions — they don't meet the ADR
bar. They are handled as doc edits (§5) and downstream task impacts (§7).

---

## 7. Downstream task impacts

What each named task must now do differently, with issue numbers.

### Task 11.3 — Implement Semantic Kernel embedding provider (#17)

- **Now carries the real-model evidence burden.** The two INCONCLUSIVE axes from 11.3b
  (semantic recall, ADR-0012 threshold) and the flat-surface caveat on the recommended weights
  all become measurable the moment a real provider (Ollama `bge-m3` / `nomic-embed-text`,
  PRD §2.3) is wired in. Task 11.3's verification ("test embedding service endpoint with
  sample text") is necessary but **not sufficient** — the real-model re-verification list
  below (Q7) must be executed against it. Recommend Task 11.3's issue gain a comment
  enumerating the re-verification list, or that the new calibration issue (Q4) is marked
  `## Depends On: 11.3`.
- **Build the HNSW index** per ADR-0016 / DATA_MODEL §3.22 (once edited).
- **Use the pgvector-pinned AppHost** (already merged).

### Task 11.4 — Store embeddings in pgvector (#20)

- **Use HNSW** (ADR-0016). Idempotent regeneration (the task's verification) is unchanged and
  is supported by the §3.22 unique `(search_document_id, provider, model, dimensions,
  content_hash)` constraint the prototype exercised.
- **Apply the Npgsql + pgvector driver quirks** recorded in 11.3b findings §3:
  `NpgsqlConnection.GlobalTypeMapper.UseVector()` + `GetFieldValue<Pgvector.Vector>`, and a
  typed `new Pgvector.Vector(...)` parameter for `<=>` (a bare `float[]` binds as `real[]` →
  SQLSTATE 42883). Hard-won; do not rediscover.

### Task 12.3 — Implement hybrid ranking (#26)

- **Implement against the validated §6 formula** — blend, 0.65/0.25/0.10 aggregation,
  note_boost 0.08, interaction_boost min(0.05, 0.01×count), cluster_score min(1.0, base +
  boosts). These are confirmed mechanisms.
- **REQUIRED: materialize the tsvector.** Per the 11.3b latency finding and the DATA_MODEL §6
  edit (§5.1), text scoring must use a materialized `tsvector` generated column + GIN index,
  not per-query `to_tsvector`. This is a hard requirement, not a suggestion — 447 ms/query
  against a 12k-doc corpus is the measured alternative.
- **Compute `relativeSimilarityPercent` over the pre-pagination top-200 pool.** Spec-conformance
  requirement; do not normalize post-pagination.
- **Treat 0.65/0.35 text/vector as a starting default,** sweepable 0.35/0.65 → 0.80/0.20,
  and re-tune with the real provider. Do not treat it as validated.

### Task 12.8 — Measure search performance against latency targets (#32)

- **Owns the 2,000-video HNSW re-check.** 11.3a measured only MVP scale; Task 12.8's
  ~2,000-video dataset is where HNSW-vs-IVFFlat and hybrid latency get re-measured. If the
  trade-off reverses, ADR-0016 names IVFFlat as the fallback.
- **Owns the authoritative hybrid-latency number.** 11.3b's 447 ms was on a 12k-doc synthetic
  corpus with per-query tsvector; Task 12.8 re-measures against the latency targets (≤ 2s P50
  / ≤ 5s P95 at < 500 videos; ≤ 3s / 10s at 2,000 videos) with the materialized-tsvector
  implementation. The 447 ms figure must not be quoted as the design's latency — it is the
  pre-optimization baseline.
- **Commits the latency baseline** per the Verification evidence convention (as its
  verification already requires).

### Task 7.5 — Generate WebP screenshots (#85)

- **Implement per ADR-0015:** worker runs `ffmpeg -ss {segmentStart+offset} -i {tempfile}
  -frames:v 1 -c:v libwebp -quality 80 {volumePath}/{videoid}/{segmentid}.webp` in-process
  against the Task 6.4 temp-media file.
- **Bake ffmpeg (not Python/yt-dlp) into the worker image** — single `apt-get install ffmpeg`
  layer (+543 MB). The worker already carries yt-dlp for the download stage (ARCHITECTURE
  §2.2); Python does not enter the image for screenshots.
- **Wire the screenshot volume** via `WithDataVolume`/bind mount (the postgres idiom already
  proven in AppHost.cs); this is a 7.5 implementation detail, not a 7.4 leftover.
- **Failure handling:** on any ffmpeg non-zero exit / absent output file, write **no file**,
  emit the domain event, mark the row retryable — the "never load-bearing" placeholder path
  triggers on absence, which ffmpeg guarantees (measured: audio-only exit 234, truncated exit
  183, no partial writes).
- **Accept the ≤1-frame (~30 ms) non-keyframe landing tolerance** (Q6). Regenerating a
  video's ≤60 screenshots is ~3–5 s of CPU — within an interactive action's budget.

### Task 12.7 — Build search recall evaluation harness (#31)

- **Is the landing site for the semantic-recall evidence** (Q7). The golden dataset (≥ 20
  query-first vague queries) padded to ~500 videos with ADR-0013 distractors is where
  paraphrase/vague recall@k/MRR finally gets measured with the real provider. 11.3b's flat
  synthetic surface is the counter-example that justifies ADR-0013's scale-true gate.

---

## 8. Outstanding risks accepted — what we are knowingly shipping without proof

These are conscious choices, recorded so they are not oversights. Each names its mitigation /
where it gets resolved.

1. **Semantic recall is unproven at the architecture's flagship promise.** We are building the
   search pipeline (Tasks 11.3–12.6) before recall is evidenced. *Accepted because:* the
   mechanism is proven, the formula matches §6, and the recall gate (Task 12.7) is a hard-MVP
   quality gate that cannot pass without real recall. **Mitigation:** Task 12.7 + the new
   calibration issue (Q4). If 12.7 fails, the fix lands in ranking/weights/document
   construction — never by editing the dataset.
2. **ADR-0012's high-signal threshold ships at an uncalibrated default (80).** The digest's
   high-signal gate could be noisy or silent until calibrated. *Accepted because:* the
   mechanism is proven and the default is configurable (`search.highSignalThresholdPercent`).
   **Mitigation:** the calibration issue sweeps 0.70–0.98 against the real own-vs-best-other
   gap and lands the value before Task 12.5a's digest rendering is trusted.
3. **Ranking weights ship as mechanics-derived defaults.** 0.65/0.35 and 0.65/0.25/0.10 could
   be wrong for the real embedding space. *Accepted because:* they are exposed as configurable
   app settings (PRD §2.5) and the §6 formula is proven. **Mitigation:** re-tune against Task
   12.7's recall reports; the flat synthetic surface is documented so nobody mistakes the
   defaults for validated values.
4. **HNSW is validated only at MVP scale (500 videos).** The ~2,000-video case is an
   extrapolation. *Accepted because:* pgvector 0.8.5 HNSW is incremental and the index is
   swappable via migration. **Mitigation:** Task 12.8 re-measures; ADR-0016 names IVFFlat as
   the fallback.
5. **The 447 ms hybrid latency is a pre-optimization number on synthetic data.** It could be
   better or worse with a real corpus. *Accepted because:* the fix (materialized tsvector) is
   identified and made a Task 12.3 requirement. **Mitigation:** Task 12.8 re-measures against
   the ≤ 2s P50 target with the optimized implementation.
6. **Windows ARM screenshot extraction is analyzed, not measured.** BtbN winarm64 ffmpeg
   builds exist, but no timing/size was executed. *Accepted because:* Windows ARM is a dev
   platform, not the Linux deployment target, and ffmpeg's packaging structure (single binary)
   holds by distribution artifact. **Mitigation:** first Windows ARM dev to run Task 7.5
   confirms; the Docker-measured Linux arm64 path is the production path.

---

## Rulings on the 8 coordinator questions (summary)

1. **Per-verdict confirmation:** All technical verdicts **CONFIRMED** — 11.3a 5/5, 11.3b 5
   PROVEN-mechanism + 2 INCONCLUSIVE (both confirmed as honest inconclusives), 7.4 8/8 + the
   framing disproof. The one **OVERTURN** is 11.3a's *process* call of "no ADR" (see Q2), not
   any technical verdict.
2. **Was "no ADR" correct?** **11.3a: NO — ADR-0016 required** (§3.22 left the index open;
   Task 11.3's implementer needs the recorded choice; ADR bar is met). **11.3b: YES** (no §6
   change; latency finding is an implementation note, not a decision).
3. **447 ms — design or implementation threat?** **Implementation only.** Measured on a 12k-doc
   synthetic corpus dominated by per-query tsvector construction — not the right basis for a
   *design* conclusion. Against Task 12.8's actual target (≤ 2s P50 at < 500 videos) even the
   unoptimized 447 ms fits; the materialized-tsvector requirement (DATA_MODEL §6 edit) makes it
   a non-issue. §6's formula needs **no** revision.
4. **ADR-0012 deferred threshold:** **Deferral ACCEPTED.** It currently has no dedicated
   landing task — Task 12.5a (digest assembly) consumes the threshold but doesn't calibrate it.
   **A new issue is required** (acceptance criteria in the decision note): sweep 0.70–0.98 with
   the real provider, measure the own-vs-best-other cosine gap distribution, set
   `search.highSignalThresholdPercent` in the gap, commit evidence per the Verification
   convention. Marked `## Depends On: 11.3`.
5. **+543 MB worker image for ffmpeg:** **ACCEPTABLE.** ARCHITECTURE §2.2 already scopes the
   worker to call yt-dlp (so a download toolchain is already there) and §9.1 sets screenshot
   concurrency to 1 per worker because ffmpeg is CPU/IO-heavy — the worker is unambiguously the
   intended home. A sidecar adds a network hop and second image for a sub-100 ms operation
   (ADR-0015's rejected alternative). In-process **validated**.
6. **+1-frame (~30 ms) non-keyframe seek offset:** **DOES NOT MATTER.** Task 7.5's default is
   segment start + 5 s (configurable), and screenshots are *never load-bearing* — a missing
   file yields a branded placeholder + domain event + retryable row. A 30 ms landing error on a
   decorative search-result thumbnail is far below any perceptible threshold, and the failure
   contract (absence → placeholder) is what actually matters. No design change.
7. **Real-model evidence still owed:** (a) paraphrase/vague recall@k/MRR → Task 12.7 harness;
   (b) ADR-0012 own-vs-best-other gap + threshold value → new calibration issue; (c) cluster
   aggregation weight differentiation on an overlapping-cluster corpus → Task 12.7 distractor
   design / Task 12.3 re-tuning; (d) cross-topic separation → Task 12.7; (e) HNSW recall/latency
   at ~2,000 videos → Task 12.8. **ADR-0013 cross-check: the harness plan covers (a), (c), (d)
   but NOT (b)** — the calibration issue closes that gap. (e) is Task 12.8's, outside ADR-0013's
   scope by design.
8. **Merged throwaway spike code:** **STAYS on `main`.** `spikes/` is the primary-source,
   re-runnable evidence for the committed `docs/verification/` reports; deleting it orphans the
   "Re-run command" sections. It is already excluded from `StreamingDigest.slnx` with zero
   `src/`/`tests/` references, so it cannot affect a solution build. **Convention required
   (standing, since PR #98 adds a second spike):** create `spikes/README.md` + a
   project-conventions note stating spike code is throwaway, excluded from the solution, kept as
   evidence, and never imported by production code. No CI exclusion is needed beyond the slnx
   boundary (there is no spike-specific build to exclude), but the README must say so explicitly
   so future readers don't mistake it for production code.

---

*Synthesis authored by Morpheus (Lead), 2026-07-25. Evidence: the three committed verification
reports + JSON, ADR-0015, PRs #96/#97/#98, issues #17/#18/#19/#20/#26/#31/#32/#84/#85. No
prototype was re-run; all rulings are grounded in committed evidence or a cited doc clause.*
