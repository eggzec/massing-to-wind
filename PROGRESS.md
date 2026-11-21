# Progress log

## Waiting for owner

_Agents: put questions that need the owner here (newest first). Owner: answer inline and delete when done._

### Q-S1 · 2026-11-19 · solver · Checker misses an anchor that lies on an edge shared by two bottom triangles
- **What happens:** `case_0284` (farCoordinates only). The solver writes the input box unchanged except for the origin
  shift: footprint 67.4 × 14.9 = 1004.26 m², volume 14,561.77 m³, every topology check passes. `volume_m3`,
  `footprintArea_m2` and `maxHeight_m` still fail with "no repaired shell left that contains its anchor (28.6, 56.25)".
  The anchor is the rectangle's centre. Both diagonals of a rectangle pass through its centre, so whichever way the
  bottom quad is split into two triangles, the anchor lies on their shared edge.
- **Reproduction (scratch copy, nothing in the repo changed):** the same box with one extra vertex at the middle of a
  long edge, so that no triangle edge runs through the centre, passes all three checks (`m2w check` on a copy of
  `out/case_0284` with only `repaired.obj` edited).
- **Effect:** 1 case at M3 step 1 (3 had an anchor-containment failure, 2 of them also for other reasons). After M3
  step 4 it is **0 cases**: the rebuilt shells happen not to put a triangle edge through any anchor. The defect is
  still there and will hit any output whose bottom triangulation runs an edge through an anchor (hidden cases too).
- **Suggested fix (checker, locked, so owner's call):** treat a point on the boundary of a bottom triangle as inside
  (or test containment against the bottom face as a polygon, not triangle by triangle).
- **Solver meanwhile:** I am *not* choosing triangulations to dodge anchor positions; that would be tuning output to
  the grader. Failures caused by this are listed as such in each PROGRESS entry.
- **Owner answer (2026-11-21): approved.** Confirmed in review: `Shell.ContainsInPlan`
  (`src/M2W.Checker/Shell.cs:82`) already counts edges as inside (`u >= 0 && v >= 0 && w >= 0`), but for an anchor
  exactly on a diagonal, rounding makes one weight about −1e-17 in both triangles. Fix: accept weights `>= -1e-9`.
  Checker agent implements it (see the 2026-11-21 owner entry in the log). Delete this item once the fix is merged and
  re-locked.


## Current status

| Milestone | Status | Pass rate | Last updated |
| --- | --- | --- | --- |
| M1 Foundations | ✅ Passed (gate approved, `lock-v0.1` tagged) | 0/1 (no solver or checker yet, expected) | 2026-11-18 |
| M2 Test cases and checker | ✅ Passed (gate approved, `cases/` and checker locked, `lock-v0.1` moved) | 0/291 (stub solver, expected until M3) | 2026-11-18 |
| M3 Core repair | ✅ Criteria met, ready for midpoint review | 252/291 (M3 faults without M4 faults 195/195; clean 10/10) | 2026-11-19 |
| M4 Hard faults | ✅ Criteria met; feature-freeze gate held for owner decisions R1 and Q-S1 (approved, being implemented) | 291/291 (unseen seeds 870/870; hidden 290/290) | 2026-11-21 |
| M5 Package | Not started | — | — |

## Log (newest first)

### 2026-11-22 · owner · R1 written into AGREEMENT.md; lock moved; branches for the M4 gate work
- R1 action 1 done. AGREEMENT.md has a new paragraph under the tolerance table: *"Roof steps below `minEdge`: roof
  regions that touch in plan (share an edge or a corner) and whose heights differ by less than `minEdge` are raised
  to the highest of them, repeated until no such step remains. Expected answers include this."*
  - Additive, so `schemaVersion` stays 0.1; recorded as D-037.
  - `lock-v0.1` moved to this commit (was `6eac5c9`). Only AGREEMENT.md changed among the locked paths.
- OWNER_GUIDE.md: the hidden-set seed and folder names are removed. The guide now says to keep the seed, folder,
  output and case details where no agent can read them. `cases/README.md` still names the old seed; the generator
  removes it when it regenerates `cases/` (locked path, approved as part of R1).
- Housekeeping: the `m2w-gen` worktree had lost its `.git` link (folder empty); it was pruned and re-created.
- Branches, all from `main` at this commit:
  - **generator:** `gen/m4-r1` in `m2w-gen`, for R1 actions 2 (expected answers after the flush, `roofStep` fault,
    regenerate `cases/`, drop the seed from `cases/README.md`). Regenerating `cases/` makes `locks` FAIL on that
    branch; this is expected and approved, and the owner moves the tag at merge.
  - **checker:** `check/m4-qs1` in `m2w-check`, for the Q-S1 fix and its test. Changing `src/M2W.Checker/` makes
    `locks` FAIL on that branch; same rule.
  - **solver:** `solve/m5` in `m2w-solve`, for M5 problems 1–3 (2026-11-21 benchmark entry). No locked path
    involved.
- validate (`scripts/validate.ps1`, after the commit and the tag move): build ✅ · tests 2465/2465 (Geometry 88,
  Cli 31, Generator 2198, Checker 86, Repair 62) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Next: generator and checker work in parallel; the owner merges the generator first, then the checker, re-tags,
  runs a new private hidden set and holds the M4 gate.
- Blockers: none.

### 2026-11-21 · owner · Private benchmark run on geometry not made by the generator; 3 problems for M5
- The owner keeps a private benchmark set outside the repo: published wind-engineering benchmark buildings and a
  real urban district model. Like the hidden set, agents get totals only: no names, sources, geometry or margins.
  - Run on `solve/m4` @ `e04558b`.
  - Benchmarks with exact answers: **7/7 pass**, zero error. No repair operation runs besides welding: clean
    geometry comes back unchanged.
  - Real district (about 500 parts, 8.4k triangles, given as one object and as one object per part): all output
    shells are valid by an independent check and the output is deterministic. Status is `partial`: raised slabs
    (canopies) are filled down to the ground, as documented. The changes are explained by the gap closing and
    short-edge rules.
- **Problems for the solver to fix in M5** (behaviour is correct; none of these is a wrong answer):
  1. **Runtime does not scale.**
     - The real district takes about 50 s per run (35–60 s seen), against a mean of 7 ms per generated case. A
       city area of thousands of buildings would take many minutes.
     - Profile it and find the term that grows faster than linear (likely pairwise work over all components,
       plan faces or rebuild rounds).
     - Target: under 5 s for about 500 buildings.
     - Add a timing test with a large synthetic site (e.g. 500 random blocks) so this cannot regress unnoticed.
  2. **Warnings do not say where.**
     - With a single input object, the warning reads "block `<name>` has no support below part of it (it floats
       beyond groundSnap); filled down to the ground" and gives no location. A user with a city model in one
       object cannot find the canopy.
     - Each warning about a region (support fill, holes left open, shell rebuild fallback) should carry its plan
       position, e.g. the bounding box `[xmin, ymin, xmax, ymax]` in local coordinates, and its area or count.
     - Fields may be added to `report.json` (readers ignore unknown fields), but keep the existing ones as they
       are.
  3. **Report detail depends on how the input is split into objects.**
     - With one input object, all gap closing is reported as a single `closeGap` entry with `width_m: 0` and no
       `unionBlocks` entry.
     - With one object per part, parts that merely touch are logged as `closeGap` with `width_m: 0` (244 entries)
       besides `unionBlocks`.
     - Wanted: `closeGap` only for real gaps (width > 0), with the actual width and the plan position; touching or
       overlapping parts reported as unions; and the same count of operations whatever the object split.
     - No `AGREEMENT.md` change is needed: op names and `count` stay; only what is logged under them changes.
- These come after the R1 / Q-S1 actions above and do not block the M4 gate. Record each fix as a D-entry and list
  any remaining limit in `KNOWN_LIMITATIONS.md`.
- validate (`scripts/validate.ps1`, after this entry; docs only): build ✅ · tests 2465/2465 (Geometry 88, Cli 31,
  Generator 2198, Checker 86, Repair 62) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Blockers: none.

### 2026-11-21 · owner · M4 independent review; decisions R1 (roof steps below `minEdge`) and Q-S1 approved
- **Review** (independent reviewer working for the owner; no repo code changed):
  - Hidden set (`run-all ../hidden --out ../hidden-out`): **290/290**, all `repaired`, every fault type 100 %. The
    291st entry in `../hidden` is `index.json`, not a case.
  - Worst share of the tolerance used: footprint 18 %, height 6 %, volume 94 % (`case_0388`), then 55 %
    (`case_0181`); 95 % of shells use under 0.02 %.
  - Two hidden runs give byte-identical `repaired.obj` for all 290 cases.
  - An independent re-check written from AGREEMENT.md alone (not the checker) finds no fault in 693 shells: closed,
    outward, on Z = 0, no edge below `minEdge`, no shells closer than `minGap`. It caught three planted faults.
  - The 44 hidden cases with non-default tolerances are handled at those tolerances (`minGap` 0.5 / 0.75 / 1.5,
    `groundSnap` 0.25 / 1).
  - Integrity: no case IDs, `expected.json` or checker/generator references in `src/M2W.Repair`; `solve/m4` touches
    no other role's or locked path; the solver sessions never read `../hidden` or used its seed.
  - Out-of-distribution inputs (hand-built): a cantilever is filled down to the ground and a sloped roof is passed
    through. Both are reported `partial` with clear warnings, not hidden.
- **Decision R1: approved.** Roof steps lower than `minEdge` are flushed to the higher roof, and expected answers
  include the flush.
  - Why: `case_0388` (crossing bars at 13.6 and 13.8 m) passes at 94 % of the volume tolerance. Its expected answer
    keeps a 0.2 m step whose walls are shorter than `minEdge`, so no valid output can match it. A 0.24 m step on the
    same bars would fail (about 1.13 %). The solver already flushes (D-030, D-036).
  - The rule is kept, not avoided: design tools do produce near-equal neighbouring heights.
  - Additive meaning, no file format change: `schemaVersion` stays 0.1.
- **Decision Q-S1: approved** as answered above: accept barycentric weights `>= -1e-9` in `ContainsInPlan`.
- **Actions:**
  1. **Owner:** add to AGREEMENT.md, under the tolerance table, under the owner's own commit: *"Roof regions that
     touch in plan (share an edge or a corner) and whose heights differ by less than `minEdge` are raised to the
     highest of them, repeated until no such step remains. Expected answers include this."*
  2. **Generator** (`gen/…`):
     - compute expected `volume_m3` / `maxHeight_m` after the flush rule, from the AGREEMENT text, not from solver
       code;
     - add a `roofStep` fault: about 30 cases, steps both just below and just above `minEdge`;
     - regenerate `cases/`, add a D-entry, and validate.
  3. **Checker** (`check/…`):
     - Q-S1 fix and a test with an anchor exactly on a triangle diagonal;
     - check that the existing checks still pass on the regenerated cases; add a D-entry; validate.
  4. **Solver:** no change expected. After the new cases land, run validate and report the `roofStep` results and
     margins.
  5. **Owner:**
     - merge, then move `lock-v0.1`;
     - regenerate the hidden set with a seed recorded only outside the repo;
     - run it and hold the M4 feature-freeze gate.
- **Reviewer recommendations, not yet decided:**
  - remove the hidden seed (90001) from `cases/README.md` and `OWNER_GUIDE.md`;
  - add 5–10 hand-built hidden cases: cantilever, sloped roof, courtyard near `minGap`, large next to tiny, a real
    design-tool export;
  - M5 README results table with the worst margin per check;
  - KNOWN_LIMITATIONS: overhangs filled, sloped roofs `partial`, output slivers, flush volume change, D-034 jogs;
  - CI determinism check.
- validate (`scripts/validate.ps1`, after this entry; docs only): build ✅ · tests 2465/2465 (Geometry 88, Cli 31,
  Generator 2198, Checker 86, Repair 62) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Next: actions 1–5 in order (1 and 2 first; 3 can run in parallel).
- Blockers: none.

### 2026-11-21 · solver · M4 step 4: randomised property tests; M4 criteria met
- New `Step8PropertyTests`: FsCheck properties on random **faulty** sites. 1–4 blocks at random, or placed a random
  0.02–0.98 m from the previous one; 1–4 floors of random height; a random ledge below or above `minEdge` (30 %);
  floating/sunk within `groundSnap` (30 %); objects inside out (20 %); duplicate vertices (20 %). Positions carry
  small per-block offsets so no distance lands within rounding of `minGap` or `minEdge`. The seed comes from three
  FsCheck integers (one `PositiveInt` stays within about 1–100, so it repeated about 100 sites).
  1. any site repairs to valid output: `repaired`, closed outward shells, ground at 0, no edge below `minEdge`, no
     two shells closer than `minGap` (100 cases);
  2. moving to map coordinates and rotating keeps shell count, Euler number and volume (60 cases);
  3. repairing the output again changes nothing: same shells, Euler number and volume, and no repair op runs
     (60 cases).
  The M3 properties (fault-free sites) are kept. Stress runs: properties 1–3 at 1000/500/500 cases, six times in
  a row, and property 2 at 1500 cases three times: all pass.
- The properties found 9 defects, all fixed (each now also a fixed regression test in `Step8RegressionTests`):
  1. roof steps below `minEdge` were flushed only between faces that differ in height: a roof made of several plan
     faces was raised in part, leaving a new 0.2 m step. Equal heights now join the group;
  2. roofs touching only at a corner, 0.1 m apart, gave a 0.1 m vertical edge: faces sharing a vertex now group;
  3. flushing to a fixpoint: raising a bridge to 3.3 m put it within `minEdge` of a 3.5 m block;
  4. a thin ledge was raised to the highest neighbour (a taller block touching its end through a gap bridge),
     building a thin fin above its own block; now to the higher neighbour it shares the longest boundary with;
  5. overlapping corner fillets left clusters of points closer than `minEdge`, which snapping pinched into a point
     contact, so gap closing never settled: one convex hull per pair of components now;
  6. a facing strip only 0.033 m long vanished in snapping: strips shorter than `minEdge` along the walls no longer
     count, and fillets take the wall parts within `minGap` + `minEdge` (fat enough to survive snapping);
  7.–9. snapping bent fill polygons (gap bridges) onto points up to `minEdge` away and folded them, while keeping
     fills straight left slivers between a fill and a bent wall, and the result was not a fixpoint (the second
     repair snapped again). Fixed by restructuring the final phase (D-036): repairs at weld precision until stable,
     then the height field is baked into one outline per roof level and only those are snapped; if snapping leaves
     a gap, thin region or jog, the snapped plan becomes the base and the loop repeats.
- Tests: 62 solver tests (+8: 3 properties, 5 regression cases).
- Determinism: two full runs give byte-identical `repaired.obj` for all 291 cases. Runtime: mean 7 ms, max 171 ms
  per case.
- validate (`scripts/validate.ps1`): build ✅ · tests 2465/2465 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 62) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 30/30 · farCoordinates 31/31 · flippedFaces 29/29 ·
  floatingBlock 30/30 · openSurface 31/31 · overlap 29/29 · shortEdge 30/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 30/30 · touchingWalls 30/30. All 291 reports `repaired`.
- Unseen sets (`m2w generate --seed N` into a scratch folder, only results read): seed 777 290/290, seed 2024
  290/290, seed 99991 290/290, all `repaired`.
- **M4 target (≥ 80 % overall, every failure explained): met** — 291/291 (100 %), no failures to explain. All four
  M4 items done: gap closing (step 1), sliver and short-edge removal (step 2), combined-fault cases (step 3),
  randomised properties (this step).
- Known risks for hidden cases (none fails today): (a) single jogs close to `minEdge` deep along long walls, where
  the least-change rule may move the other wall than the fault did (worst visible margin 32 %, unseen 50 %;
  D-034); (b) 100 of 291 outputs still contain triangles below `minAngleDeg` (not graded, reported; D-035);
  (c) Q-S1 (checker anchor on a shared edge).
- Next: owner feature-freeze gate for M4. Then M5 (README with results table, KNOWN_LIMITATIONS.md, CI).
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-21 · solver · M4 step 3: combined-fault cases
- No solver change was needed: every case with two or more faults passes. By number of faults: 0 → 10/10,
  1 → 240/240, 2 → 14/14, 3 → 13/13, 4 → 14/14.
- Unseen cases: generated three new full sets with `m2w generate --seed N` into a scratch folder (outside the repo)
  and ran `run-all` on them, reading only `summary.json`, `result.json`, `report.json` and the inputs (never the
  generated `expected.json`). Seed 777: 290/290 · seed 2024: 290/290 · seed 99991: 290/290 (seed 1 reproduces the
  committed set: 290/290 + the example). All reports `repaired`.
- Margins on the new sets: worst 50 % of the footprint tolerance (seed 2024 `case_0262`), then 45 % (seed 777
  `case_0364`), 37 % (seed 99991 `case_0253`). All are single jogs where the fault had moved the other wall than
  the least-change rule moves (D-034). This is the main risk for hidden cases: a jog close to `minEdge` deep along
  a long wall. I have not switched to "move the longer wall": four cases suggest it, but it has no geometric
  reason and would be fitted to the grader.
- Tests: 54 solver tests (+2): one hand-built case with far coordinates, stacked floors with a 0.1 m ledge, a
  0.4 m gap and a block floating 0.3 m up with all faces flipped and duplicate vertices gives one closed shell
  with the expected volume, local coordinates, ground at 0, height 9, every op reported and no edge below
  `minEdge`; the same case is deterministic and a second repair changes nothing.
- validate (`scripts/validate.ps1`): build ✅ · tests 2457/2457 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 54) · cases 291/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 30/30 · farCoordinates 31/31 · flippedFaces 29/29 ·
  floatingBlock 30/30 · openSurface 31/31 · overlap 29/29 · shortEdge 30/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 30/30 · touchingWalls 30/30.
- Next: M4 step 4, randomised property tests.
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-21 · solver · M4 step 2: short-edge and sliver removal (shortEdge, sliver)
- The plan is now repaired in phases before the shells are built (`HeightFieldUnion`), each phase building the
  plan again with what the previous one added:
  1. at weld precision: gap bridges (step 1) until none is left;
  2. at weld precision: **thin regions** (one height, 2 × area / perimeter < `minEdge`, next to a higher roof) are
     raised to the highest roof beside them, as roof steps are (D-030): a ledge thinner than `minEdge` becomes part
     of the block above it and the footprint is kept. Op `raiseThinRegion`;
  3. at weld precision: **short outline edges** between two long walls with the same heights (outline = plan edges
     where the height changes, collinear pieces chained):
     - a section with a jog at each end, both stepping the same way (shallow bump or notch), no longer than its two
       neighbours together: cut back / filled out onto their line (ops `cutBump`, `fillNotch`);
     - a single jog between parallel walls: the shorter wall moves onto the other's line (fill if the strip is
       outside, cut if inside; ops `alignJog`, `alignJogCut`). Least change of area, see D-034;
     - a convex corner cut off by a short edge (not a kink in line with a wall): the corner is squared
       (`squareCorner`);
     Fills can now also cut (lower to a height); cuts apply after fills;
  4. final plan with points closer than `minEdge` merged and segments within `minEdge` of a point split there, so
     **no plan edge is shorter than `minEdge`**. Which point survives a merge: the one on the lowest outline (the
     footprint), then the sharper corner of the phase-3 plan, then the one on the higher roof (D-033). Gaps were
     bridged before this, so a gap narrower than `minEdge` is still filled to the lower block. Op
     `collapseShortEdges` (merges/splits that moved geometry beyond weld).
- Own output check (new): any output edge shorter than `minEdge` gives `partial`; triangles thinner than
  `minAngleDeg` are counted in a warning that does not change the status (not graded in v0.1, D-025, D-035).
- Bugs found while doing this (by the new FsCheck property and by tolerance margins, not by failures):
  - snapping by first-come point order could move a tall block's wall onto a lower block's across a gap narrower
    than `minEdge` (volume error gap × wall × height difference: > 1 % for a 0.2 m gap at a 30 m, 10 m wide
    block). Fixed by the merge priority above;
  - point priority alone shaved half a ledge off `case_0222`/`case_0160` (78 % of the volume tolerance used,
    still passing). Fixed by raising thin regions first (now 0 %) and the corner tie-break;
  - per-jog decisions moved both long neighbours of a shallow bump outward in `case_0241` (+3.7 m², 26 %); now
    decided per section (8 %).
- Tests: 52 solver tests (+9): thin ledge absorbed (volume exact, shortest edge ≥ `minEdge`), ledge of 0.3 m at
  `minEdge` 0.25 kept, Z-jog cut back / filled out, shallow bump cut and notch filled (only y = 0 and y = 10
  remain), cut corner squared, split vertex next to a corner merged, case `minEdge` respected (0.3 m jog kept at
  0.25, removed at 0.4), and an FsCheck property: rotated L-shaped block with a random bump, notch or jog of
  0.01–0.24 m gives one closed shell, no edge below `minEdge`, volume within depth × wall of the clean block, and
  repairing again changes nothing. Properties stress-run once at 600/600/300 cases: all pass.
- Determinism: two full runs give byte-identical `repaired.obj` for all 291 cases. All 291 reports `repaired`.
- validate (`scripts/validate.ps1`): build ✅ · tests 2455/2455 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 52) · cases **291/291** · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 30/30 · farCoordinates 31/31 · flippedFaces 29/29 ·
  floatingBlock 30/30 · openSurface 31/31 · overlap 29/29 · shortEdge 30/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 30/30 · touchingWalls 30/30.
- Margins (how much of the volume/footprint tolerance is used, worst first): `case_0336` 32 %, `case_0298` 19 %,
  `case_0180` 11 %, everything else < 10 %. 0336 and 0298 are jogs where the expected answer moved the other wall
  than the least-change rule does; which piece a jog fault moved cannot be told from the geometry (D-034). Known
  risk for hidden cases: a jog of depth close to `minEdge` (0.4 m in some cases) along a long wall could exceed 1 %.
- Slivers: 100 of 291 outputs have triangles below `minAngleDeg` (long low walls, tall narrow walls between close
  heights, thin roof pieces). Not graded (D-025); now reported in `warnings`. Removing them needs Steiner points
  on walls and roofs (possible without T-junctions: wall columns are shared per vertex). Not done; see D-035.
- Next: M4 step 3 (combined-fault cases: all 40+ pass already; review margins and partial paths) and step 4
  (properties: moving/rotating input keeps volume and topology, repair twice = once, extended to gaps and
  short edges).
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-21 · solver · M4 step 1: gap closing below `minGap` (smallGap)
- New `Union/GapCloser`: a morphological closing of the height field, one height level at a time. For each roof
  level h, the slice "height ≥ h" of the plan is split into connected components; boundary walls (collinear pieces
  chained) of *different* components closer than `minGap` get a bridge polygon, filled from the ground up to h.
  Slices are nested, so a gap is filled up to the lower of the two blocks, and to the lower tower above a shared
  podium.
  - Walls facing each other within 10° of anti-parallel: the strip between them where one projects onto the other
    (an exact rectangle for parallel walls, so walls that end together add no new vertex).
  - Component pairs with no facing walls (corner to corner): for each pair of walls closer than `minGap`, the convex
    hull of the parts of each wall within `minGap` of the other and in front of it (a small fillet).
  - Gaps inside one component (a narrow inlet of a U) are left open (see D-032).
  - `HeightFieldUnion` now builds its plan in a repeatable `BuildPlan`; bridges are added as extra tagged polygons
    and the plan is built again (at most 4 rounds; a gap that cannot be bridged, or remains, gives `partial`).
  - A distance within 1e-6 m of `minGap` counts as `minGap` (OBJ precision); it is not closed.
  - Report op `closeGap` with `pair` (blocks on each side), `width_m` (narrowest), `fillHeight_m`, `minGap_m`.
- Tests: 43 solver tests (+10): parallel gap filled to the lower block (volume exact, op contents), gaps of 1.0 /
  1.2 / 5.0 m stay open, shorter neighbour bridged only over the facing stretch, corner-to-corner fillet, towers on
  one podium closed above it to the lower tower, narrow inlet of one block not closed, case `minGap` respected, and
  an FsCheck property: random rotation (0–360°), gap 0.02–1.98 m and height gives one shell with the exact bridged
  volume below `minGap` and two shells at or above it.
- Determinism: two full runs give byte-identical `repaired.obj` for all 291 cases. All 291 reports `repaired`, no
  warnings.
- validate (`scripts/validate.ps1`): build ✅ · tests 2446/2446 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 43) · cases 274/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 29/30 · farCoordinates 30/31 · flippedFaces 27/29 ·
  floatingBlock 28/30 · openSurface 28/31 · overlap 29/29 · shortEdge 13/30 · sliver 30/30 · smallGap 31/31 ·
  stackedFloors 32/32 · sunkBlock 29/30 · touchingWalls 29/30.
- Also fixed by this step: shortEdge `case_0202` and `case_0302` (a jog left two parts of one block 0.17 m apart).
- Remaining 17 failures, all shortEdge cases (M4 step 2): 16 fail `minEdge_m` only (an output edge shorter than
  `minEdge`); `case_0196` fails `euler` (2 instead of 0: a courtyard closed off by a tiny feature).
- Next: M4 step 2, short-edge and sliver removal.
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-19 · solver · M3 step 4: footprint union (stacked floors, overlaps, touching walls); M3 criteria met
- New `Union/HeightFieldUnion` (with `Planar/Planarizer`, `Planar/Dcel`, `Union/ShellBuilder`). The output is
  now rebuilt as a 2.5D height field, one closed shell per connected footprint:
  1. Each object's horizontal triangles are grouped by height level (levels within `weld` are one level); XOR of
     their edges leaves that level's boundary. Each boundary edge is tagged (object, level). Walls are not used.
  2. All tagged edges are planarised together (points within `weld` merged, edges split at vertices within `weld`
     and at crossings, coordinates on a 1e-6 m grid = the OBJ precision).
  3. Faces of the plan are labelled by a walk from outside that toggles each crossed edge's tags. Per object the
     toggles give its solid intervals; the face height is the highest roof over it, filled down to the ground.
     Every edge is checked for a consistent label; any open level boundary is reported (`partial`).
  4. Neighbouring roofs less than `minEdge` apart are raised to the higher one (the step would be an edge shorter
     than `minEdge`); op `flushRoofStep`. Only adjacent faces are touched, so separate blocks keep their heights.
  5. Edges with the same height on both sides are dropped, collinear chains merged, and each shell is built from a
     ground face, a roof per plan face (triangulated with holes) and walls whose vertex columns are split at every
     height used around that vertex, so all triangles meet edge to edge.
  Report ops: `mergeStackedFloors` (block, count), `unionBlocks` (shell, blocks), `flushRoofStep`, `rebuildShells`.
  A block floating beyond `groundSnap` with nothing under it is filled down and reported `partial`. If the union
  yields nothing, the per-object output of steps 1–3 is written instead, always as `partial`.
- Bugs found on the cases and fixed:
  - `case_0179` (touchingWalls): 13 self-intersecting pairs. B03's corners lie on B02's roof edge, and greedy
    best-ear clipping left four nearly collinear points to the end, forcing near-zero-area triangles. Roofs up to
    150 vertices now get an optimal triangulation (dynamic programming over valid diagonals, maximising the smallest
    angle); ear clipping is the fallback for larger polygons.
  - Welding test: walls with a vertex nudged within `weld` were taken as sloped. Vertical now means the plan
    projection is thinner than 2 × `weld`.
  - `case_0137` (overlap): roofs at 17.4 and 17.5 m left a 0.1 m step, below `minEdge`. Fixed by item 4.
- Tests: 33 solver tests (+11: stacked floors without internal floors, crossing bars, tower through podium,
  terrace of three touching blocks, separate blocks stay separate, courtyard keeps genus 1, roof step below / at
  `minEdge`, wall column split at a neighbour's height, and two FsCheck properties on random multi-block,
  multi-floor sites: rotating plus moving to map coordinates keeps shell count, Euler number and volume; repairing
  the output again changes nothing). Step 1/3 tests updated where the union changes the expected output (floors
  merge; a block floating 4 m up is filled down and reported partial).
- Determinism: two full runs give byte-identical `repaired.obj` for all 291 cases.
- validate (`scripts/validate.ps1`): build ✅ · tests 2436/2436 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 33) · cases 252/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 25/30 · farCoordinates 28/31 · flippedFaces 24/29 ·
  floatingBlock 25/30 · openSurface 28/31 · overlap 29/29 · shortEdge 9/30 · sliver 28/30 · smallGap 11/31 ·
  stackedFloors 29/32 · sunkBlock 28/30 · touchingWalls 27/30.
- **M3 target (≥ 90 % on the M3 faults, 100 % on clean):** met. Cases with an M3 fault: 205/220 (93 %). Every case
  whose faults are all M3 faults passes: 195/195. Per fault over all its cases: farCoordinates 90 %,
  duplicateVertices 83 %, flippedFaces 83 %, openSurface 90 %, floatingBlock 83 %, sunkBlock 93 %,
  stackedFloors 91 %, overlap 100 %, touchingWalls 90 %. Every one of those failures is a combined case that also
  has smallGap or shortEdge (M4). Clean 10/10.
- All 39 remaining failures are M4 work:
  - 22 · a gap below `minGap` not closed (shellCount / mustMerge / minGap fail): 20 smallGap cases and 2 shortEdge
    cases whose jog leaves a gap.
  - 16 · an output edge shorter than `minEdge` (shortEdge: split vertices, tiny ledges or jogs kept as geometry).
  - 1 · `case_0196` (shortEdge): Euler 2 instead of 0, a courtyard closed off by a tiny feature.
  - The sliver failures (2) are among these: `case_0263` and `case_0385` also have smallGap.
- Next: owner midpoint review of M3. Then M4: gap closing below `minGap`, short-edge and sliver removal.
- Blockers: Q-S1 (owner; no case affected right now).

### 2026-11-19 · solver · M3 step 3: ground snap
- New: `Steps/GroundSnapper`. A block (all objects sharing `B<nn>`) whose lowest point is within `groundSnap` of
  Z = 0, above or below, is moved rigidly by minus that height (height unchanged; vertices landing within 1e-9 of
  the ground are set to exactly 0). Blocks further up are left alone (they may stand on another block). A block
  reaching below ground beyond `groundSnap` is not moved and gives `partial` with a warning. Report op
  `groundSnap` with `block` and `dz_m`.
- Tests: 21 solver tests (+4: FsCheck property over offsets within ±0.9 × groundSnap, floors of one block move
  together and other blocks separately, block on a podium not moved, deep sunk block reported partial).
- validate (`scripts/validate.ps1`, run from PowerShell; launched from Git Bash it cannot find Git Bash for the
  lock step): build ✅ · tests 2424/2424 (Geometry 88, Cli 31, Generator 2198, Checker 86, Repair 21) ·
  cases 159/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 22/30 · farCoordinates 21/31 · flippedFaces 21/29 ·
  floatingBlock 20/30 · openSurface 24/31 · overlap 0/29 · shortEdge 0/30 · sliver 17/30 · smallGap 10/31 ·
  stackedFloors 0/32 · sunkBlock 21/30 · touchingWalls 0/30.
- Every single-fault floatingBlock and sunkBlock case passes. The 10 + 9 failures are combined with stackedFloors,
  overlap, touchingWalls, smallGap, sliver or shortEdge.
- Next: M3 step 4, footprint union (stackedFloors, overlap, touchingWalls).
- Blockers: Q-S1 (owner).

### 2026-11-19 · solver · M3 step 2: orientation fix and hole closing
- New: `Steps/Orienter` (orientation spread across manifold edges by BFS, then each edge-connected component with
  negative volume turned inside out) and `Steps/HoleFiller` (first splits free edges at free-edge vertices lying on
  them within `weld`, so a split vertex on a neighbour's edge is joined, not filled; then fills free-edge loops,
  grouping coplanar nested loops into one polygon with holes so a deleted courtyard ground face is filled as a ring).
  Report ops: `fixOrientation`, `splitTJunctions`, `closeHoles` with counts. A loop that cannot be filled gives
  `partial` with a warning.
- First run filled each loop as a disk: `case_0107` and `case_0221` (courtyard ground deleted) then covered the
  courtyard twice (self-intersections, Euler 2 instead of 0). Fixed by the grouping above.
- Known limit: two *adjacent* deleted faces leave one bent loop; one planar fill would cut the corner. The
  generator deletes only non-adjacent faces; not handled.
- Tests: 17 solver tests (+6: courtyard helper, inside-out object, FsCheck property: any random 40 % of courtyard
  faces reversed comes back closed with the right volume, roof + ground deleted, courtyard ground deleted, split
  vertex on an edge).
- validate (`scripts/validate.ps1`): build ✅ · tests 2420/2420 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 17) · cases 118/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 21/30 · farCoordinates 21/31 · flippedFaces 21/29 ·
  floatingBlock 0/30 · openSurface 23/31 · overlap 0/29 · shortEdge 0/30 · sliver 17/30 · smallGap 10/31 ·
  stackedFloors 0/32 · sunkBlock 0/30 · touchingWalls 0/30.
- Every flippedFaces and openSurface case where that is the only fault passes. The 8 flippedFaces and 8 openSurface
  failures are all combined with a fault from a later step (ground snap, stackedFloors, overlap, touchingWalls,
  smallGap, shortEdge).
- Next: M3 step 3, ground snap.
- Blockers: Q-S1 (owner).

### 2026-11-19 · solver · M3 step 1: coordinate shift, welding, duplicate and degenerate faces
- New in `src/M2W.Repair` (first real solver; `m2w run-all` now finds `M2W.Repair.MassingRepairer` instead of the stub):
  - `RepairPipeline`: one `Solid` per input object with its own vertex list, shifted by minus `case.json` `origin`.
  - `Welder`: grid + union-find clustering within `weld`; each cluster keeps its lowest-numbered vertex's position.
  - `FaceCleanup`: collapses repeated indices, drops faces with < 3 distinct vertices, triangulates polygons, drops
    triangles that repeat the same three vertices inside one object.
  - `Planar/Triangulator`: ear clipping with hole bridges that keeps collinear vertices and never emits a zero-area
    triangle (needed later so faces stay conforming).
  - `ShellCheck`: the solver's own closed/manifold/oriented/volume check; any failure gives `partial` with a warning.
  - Until step 4, each input object is written as one shell.
- Tests: 11 solver tests (clean box, far coordinates, welding incl. nudged copies and the tolerance boundary,
  duplicate/degenerate faces, block names, byte-identical output, triangulator on collinear points, courtyard hole,
  L shape both ways, FsCheck property: triangles tile random star polygons exactly).
- validate (`scripts/validate.ps1`): build ✅ · tests 2414/2414 (Geometry 88, Cli 31, Generator 2198, Checker 86,
  Repair 11) · cases 75/291 · locks OK · **VALIDATE: GREEN**.
- Per fault (passed/total): clean 10/10 · duplicateVertices 20/30 · farCoordinates 19/31 · flippedFaces 0/29 ·
  floatingBlock 0/30 · openSurface 0/31 · overlap 0/29 · shortEdge 0/30 · sliver 16/30 · smallGap 10/31 ·
  stackedFloors 0/32 · sunkBlock 0/30 · touchingWalls 0/30.
- Failures in this step's two faults: farCoordinates misses only `case_0284` among single-fault cases, which is
  Q-S1 (checker anchor on a shared edge). duplicateVertices: the 10 failing cases are all combined with a fault
  from a later step (openSurface, flippedFaces, smallGap, overlap, stackedFloors, shortEdge).
- Next: M3 step 2, orientation fix and hole closing.
- Blockers: Q-S1 (owner).

### 2026-11-18 · owner · lock check no longer skips silently (shared `scripts/`)
- Fixes the generator's "Waiting for owner" item (removed). `scripts/check-locks.sh` exits 1 when git cannot read
  the repository, and prints git's error. A failing `git diff` / `git ls-files` also exits 1. A missing tag still
  skips, now with a `WARNING`. `scripts/validate.ps1` runs the lock check with Git for Windows' bash (found next to
  `git.exe`), never WSL, and fails the step if that bash is missing. D-026.
- Verified: WSL bash in a scratch worktree, which used to skip and pass, now reports `locks: FAIL — git cannot read
  this repository` (exit 1). Git Bash in the same worktree reports `locks: OK`. In the main checkout, an edit to
  `src/M2W.Checker/Geom.cs` and an untracked `cases/probe.txt` each fail and are named; both reverted.
- validate (`scripts/validate.ps1`, now safe to use on Windows): build ✅ (0 errors) · tests 2403/2403 (Geometry
  88, Cli 31, Generator 2198, Checker 86) · cases passing 0/291 (stub solver, expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- Next: fast-forward `solve/m3` to `main`, generate the hidden cases, then start M3.
- Blockers: none.

### 2026-11-18 · owner · M2 gate
- Approved. `check/m2` (which already contains all of `gen/m2`) fast-forwarded into `main`. Commit `6eac5c9`
  locks the M2 paths, and `lock-v0.1` moved to it (was `0b79d96`, the M1 lock).
- Eye check: 5 cases drawn at random from the 290 generated (awk `srand(20261118)` shuffle of the sorted
  `cases/case_*` list), opened in ParaView 6.1.1. In each, the owner verified that the injected fault is present
  as `expected.json` describes it:
  - `case_0108` flippedFaces
  - `case_0151` touchingWalls
  - `case_0104` sunkBlock
  - `case_0308` stackedFloors
  - `case_0379` shortEdge + openSurface + flippedFaces + floatingBlock
- Locked from now on (`LOCKS`): `AGREEMENT.md`, `LOCKS`, `cases/`, `src/M2W.Checker/`, `tests/M2W.Checker.Tests/`.
  Probe: an edit to `src/M2W.Checker/Geom.cs` made `check-locks.sh` fail and name the file; reverted.
- AGREEMENT.md: added the owner-approved optional `blocks` / `anchor_m` per expected shell (generator D-017) with
  the generator's suggested text, plus the footprint definition (bottom-face area at z = 0). Additive;
  `schemaVersion` stays 0.1. The generator's "Waiting for owner" item for it is removed.
- Owner decisions this milestone: Q1 anchor matching (D-017, D-020), Q2 footprint definition (D-022), Q3 thin
  slivers not graded in v0.1 (D-025, KNOWN_LIMITATIONS.md).
- validate (`scripts/validate.sh`, Git Bash, on `main`): build ✅ (0 warnings) · tests 2403/2403 (Geometry 88,
  Cli 31, Generator 2198, Checker 86) · cases passing 0/291 (stub solver, expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- Next: M3. The solver works on `solve/m3`, which must be rebased onto or merged with `main` first: it branched
  from the M1 gate and has neither the cases nor the checker.
- Still waiting for owner: the lock check can falsely skip under WSL bash (generator item above). Until it is
  fixed, run `scripts/validate.sh` under Git Bash, not `validate.ps1`.

### 2026-11-18 · checker · M2 Q3 answered: thin slivers not graded in v0.1
- Owner answered Q3 with option B: no minimum-angle check; the checker keeps its 15 checks. Recorded as D-025.
  The limit is written up in the new `KNOWN_LIMITATIONS.md` (Checker section: what is not graded, the effect on
  `sliver` scores (16/30 pass after zero repair), why, and when to revisit). D-019 now points to D-025. Q3
  removed from "Waiting for owner". No code changed.
- validate (`scripts/validate.sh`, Git Bash): build ✅ (0 warnings) · tests 2403/2403 (Geometry 88, Cli 31,
  Generator 2198, Checker 86) · cases passing 0/291 (stub solver, expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- M2 checker criteria all met; no checker questions open. Ready for the owner's M2 gate (5-case eye check, then
  move `lock-v0.1` so `cases/`, `src/M2W.Checker/` and `tests/M2W.Checker.Tests/` are locked).
- Next: owner gate. After the lock, checker changes need the owner.
- Blockers: none.

### 2026-11-18 · checker · M2 checker: anchor matching, generated cases, a self-intersection fix
- Merged `gen/m2` into `check/m2` (merge commit). The checker's decisions were renumbered D-018 to D-024, because
  the generator had already used D-009 to D-017.
- Q1 answered (owner-approved `anchor_m` and `blocks`, generator D-017): expected shells are now matched by anchor
  (D-020). A shell with an anchor can only pair with a repaired shell that contains it in plan. Best fit remains
  the fallback for files without anchors. New tests: anchors catch a repair that swaps two blocks' sizes, which
  best fit would accept; an anchor outside every shell, or two anchors in one merged shell, leaves an expected
  shell unmatched; plan containment excludes a courtyard. Q2 answered: the generator's footprint is the same
  bottom-face area, and no case can differ (D-022).
- **Clean controls (M2 criterion):** all 10 generated clean controls pass all 15 checks with zero repair, graded
  through `CaseChecker` from files. All 290 generated cases grade without error in about 17 s.
- **Checker bug found and fixed:** grading the generated cases after zero repair showed 3 above-threshold smallGap
  cases failing `noSelfIntersections` with no real intersection. Rotated plots with mm-rounded coordinates give
  valid, nearly flat triangle fans (3e-8 rad), and the shared-vertex test (cut 0.1 mm, then measure distance)
  read them as contact. It is replaced by an exact angular test (D-019), with a regression test confirmed to fail
  on the old code. The checker was stricter than the agreement there; it no longer is.
- Zero repair passes per fault, now fully explained: farCoordinates 20/31 (the 20 single-fault cases: the origin
  shift is the fix) · smallGap 10/31 (the 10 above-threshold ones: staying apart is the answer) · sliver 16/30
  (thin slivers: no check looks at angles; owner Q3) · every other fault 0.
- validate (`scripts/validate.sh`, Git Bash): build ✅ (0 warnings) · tests 2403/2403 (Geometry 88, Cli 31,
  Generator 2198, Checker 86) · cases passing 0/291 (stub solver, expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- M2 checker criteria: all checks ✅ · unit tests on good and bad meshes ✅ · clean controls pass with zero repair ✅
  (10/10 generated, 7 hand-built). Ready for the owner's M2 gate once Q3 is decided.
- Next: Q3 (add a minimum-angle check or document the limit), then the owner's 5-case eye check and the M2 lock.
- Blockers: none. One owner question (Q3).

### 2026-11-18 · checker · M2 checker
- Changed (`src/M2W.Checker`, `tests/M2W.Checker.Tests` only):
  - `CaseChecker` (the `ICaseChecker` that `m2w check` now finds) → `Grader`. It writes 15 checks to `result.json`
    with a `detail` string each: parse, watertight, manifold (non-manifold edges and vertices), outwardNormals
    (winding and signed volume > 0), euler (V−E+F = 2−2·genus), noSelfIntersections, minGap_m, minEdge_m,
    shellCount, volume_m3, footprintArea_m2, maxHeight_m, groundContact, mustMerge, mustStaySeparate.
  - Exact geometry (`Geom`: closest points, segment/triangle contact, triangle distance, winding number),
    `Topology`, `SelfIntersection` (sweep and prune), `ShellGap` (exact minimum shell distance with box pruning,
    nesting counts as 0), `BlockLocator` (input blocks located in output shells by position), `Assignment` (Hungarian).
  - It never reads `report.json` (a test proves a garbage report changes nothing). An unreadable `repaired.obj` is
    graded as a failed `parse` and does not throw.
  - Design choices D-018 to D-024 in DECISIONS.md.
- Tests: 80 checker tests, with one hand-built mesh that must pass and one that must fail for every check
  (including boundary values: gaps 0.9/1.0/1.1 m, edges 0.2/0.25/0.3 m, ±1 % volume, weld on ground contact,
  touching-but-unmerged walls, a nested shell, a diagonal corner gap, folds, a shared-vertex crossing,
  21,600-triangle split faces).
- Clean controls with zero repair (origin shift, ear-clip triangulation, shell naming; `Kit.ZeroRepair`): bar, L,
  U, acute plot, courtyard (genus 1), podium + set-back tower, and a 5-block site in map coordinates, all pass every
  check, including through the 6-decimal OBJ round trip. `Generated_clean_controls_pass_with_zero_repair` runs the
  same proof on every clean case in `cases/`. There are none yet, because the generator's cases have not landed.
  **Re-run it after the M2 merge.**
- case_0007: the correct repair (one 30.3 × 10 × 18 box) passes all 15 checks via `m2w check` (exit 0). The
  unrepaired input fails shellCount, minGap, mustMerge, volume, footprint and groundContact (exit 1).
- `out/summary.json`: the checker brief says the checker writes it. Per D-004 (owner-approved) `m2w run-all`
  already does, and the checker does not duplicate it.
- validate (`scripts/validate.sh`): build ✅ (0 warnings) · tests 193/193 (Geometry 82, Cli 31, Checker 80) ·
  cases passing 0/1 (case_0007: repair=1 stub solver, check=2 no repaired.obj; expected until M3) · locks OK ·
  **VALIDATE: GREEN**.
- M2 checker criteria: all checks ✅ · unit tests on good and bad meshes ✅ · clean controls pass with zero
  repair: ✅ on hand-built massings, ⏳ on generated controls (none exist yet).
- Next: once the generator's cases are merged, run the clean-control test on them and look at the details of any
  failure. A failure there means the checker is too strict or the case is wrong, and I will settle it with the
  generator. Then the owner's 5-case eye check, and the M2 lock.
- Blockers: none for the checker itself. Two owner questions above (Q1 shell matching, Q2 footprint definition).
### 2026-11-18 · generator · M2 shell matching fields (owner-approved)
- Changed: `expected.shells[]` now has `blocks` and `anchor_m` (D-017), answering the checker's Q1. Added two optional
  properties to `ExpectedShell` in shared `M2W.Geometry` (no existing signature changed) plus
  `tests/M2W.Geometry.Tests/ExpectedShellFieldsTests.cs` (optional, round trip, bad values). Generator fills them;
  `HeightField.InteriorPoint` picks the anchor. New test: for all 290 cases each anchor ±0.5 m lies inside its own
  shell's blocks and outside every other block, and `blocks` partitions the case's blocks consistently with mustMerge.
  Regenerated `cases/`: only the 290 `expected.json` files changed; every `input.obj` and `case.json` is byte-identical.
  Checker's Q2 (footprint = bottom-face area): same definition as the generator, recorded in D-015; no case differs.
- validate (`scripts/validate.sh`, Git Bash): build ✅ · tests 2317/2317 (Geometry 88, Cli 31, Generator 2198) · cases passing 0/291 (stub solver, no checker yet; per-fault totals unchanged) · locks OK · **VALIDATE: GREEN**.
- Next: owner adds the AGREEMENT.md text (Waiting for owner) and re-tags; checker can switch to anchor matching.
  Note for merging: `check/m2` may also touch `ExpectedSpec.cs`; this change only adds members.
- Blockers: none.

### 2026-11-18 · generator · M2 test cases
- Changed (all in `src/M2W.Generator/`, `tests/M2W.Generator.Tests/`, `cases/`):
  - Seeded mini generative pipeline: convex site (rectangle, irregular, acute trapezoid, corner-cut) → 2–6 m setback →
    grid frame along the longest site edge → footprints (bar, L, U, courtyard, tower, tower on podium) → floor-by-floor
    extrusion (taller ground floor, typical 3.0–3.5 m) with setback tiers → placement with seeded rejection sampling.
  - Ground truth (`HeightField`): the corrected model merged as an exact 2.5D height field → shellCount, volume,
    footprint, maxHeight, genus, mustMerge, mustStaySeparate. Ambiguous designs are rejected and redrawn (D-015).
  - Emission (`Mesher`): whole blocks (`B01`) or stacked floors (`B01_F01`), planar polygons or triangles; every
    object is a closed outward manifold shell before faults.
  - All 12 faults injected (`Layouts`, `CaseBuilder`, `MeshFaults`); conventions in D-012/D-013/D-014; tolerances
    vary in some cases (D-010).
  - `CaseGenerator : ICaseGenerator` → `m2w generate cases` wrote 290 cases `case_0101`–`case_0390` (seed 1) plus
    `cases/index.json`; `_example/case_0007` untouched. 10 clean controls, 20 single-fault cases per fault, 40
    combined (2–4 faults). Per fault including combined cases: 29–32. smallGap: 10 below / 10 above minGap,
    4 at exactly 0.9× and 4 at 1.1×. 7.3 MB. Every case builds for 11 other seeds tested (incl. 90001).
  - Tests (1908): plan counts and IDs; height-field maths (incl. the case_0007 numbers 5454 m³ / 303 m², courtyard
    genus 1, union of overlaps); mesher closed/outward/volume/Euler for every shape × style; fault injectors keep
    shells closed; **for all 290 cases, vertical ray casting through the emitted meshes equals the model cell by
    cell**; expected == corrected model; clean and expected models differ only by closed gaps; clean controls meet
    the output rules (triangles, closed, no slivers/short edges, z = 0, volumes); each fault really shows in the
    written input and absent faults do not; same seed → byte-identical files; committed `cases/` == generator.
- Found and fixed while testing: a group's quarter-turn was drawn per block, so blocks of one group could end up
  rotated differently (case_0106). Rotation is now drawn once per group, and a test guards it.
- Notes for the checker (see D-011, D-014): clean controls are triangles with origin 0 but objects are named `B<nn>`
  (input naming), so "zero repair" = copy input.obj and rename objects to `shell_NNN`. Block names in
  mustMerge/mustStaySeparate are the prefix of the input object names (`B01_F03` belongs to `B01`). Expected shells are
  listed largest volume first. Floating/sunk blocks are expected back on the ground with their original height.
- For the owner at the gate: please glance at D-013 (floating = rigid move back) and D-014 (gap fill rule). They are
  my reading of AGREEMENT.md and decide what counts as correct.
- validate (`scripts/validate.sh`, Git Bash): build ✅ · tests 2021/2021 (Geometry 82, Cli 31, Generator 1908) ·
  cases passing 0/291 (stub solver, no checker yet; per fault: clean 0/10, overlap 0/29, stackedFloors 0/32,
  touchingWalls 0/30, smallGap 0/31, sliver 0/30, shortEdge 0/30, duplicateVertices 0/30, openSurface 0/31,
  flippedFaces 0/29, floatingBlock 0/30, sunkBlock 0/30, farCoordinates 0/31) · locks OK · **VALIDATE: GREEN**.
  (`validate.ps1` also GREEN but its lock step skips falsely here; see "Waiting for owner".)
- Next: owner gate M2 (5 random cases by eye; `index.json` notes say what was injected where). After the checker
  lands, run-all with a "copy input" repair on the clean controls should pass them; if the checker finds a case it
  thinks is wrong, the generator fixes the generator, not the case.
- Blockers: none.

### 2026-11-18 · owner · M1 gate
- Changed: M1 approved. `m1/foundations` fast-forwarded into `main` and tagged `lock-v0.1` (commit `0b79d96`);
  `AGREEMENT.md` and `LOCKS` are now locked. Role branches `gen/m2`, `check/m2`, `solve/m3` created from `main`.
- Owner answers (all approved as proposed): `summary.json` layout per D-004; `partial` → `repair` exit 1 per D-006;
  `generate` and `check` may stay stubs (exit 2 "not implemented yet") until M2.
- validate: build ✅ · tests 113/113 · cases passing 0/1 (expected until M2/M3) · locks OK · **VALIDATE: GREEN**.
- Next: M2. Generator on `gen/m2` implements `ICaseGenerator`; checker on `check/m2` implements `ICaseChecker`
  (see `src/M2W.Geometry/README.md`, "How a role plugs into m2w").
- Blockers: none.

### 2026-11-18 · shared (M2W.Geometry, M2W.Cli) · M1 Foundations
- Changed:
  - `src/M2W.Geometry`: `Vec3`; `Mesh` / `MeshObject` / `Face`; strict `ObjReader` (line-numbered errors) and
    `ObjWriter` (6 dp, LF, no BOM); `MeshMeasures` (bounds, signed volume, area, edge use counts);
    JSON models `CaseSpec`, `ExpectedSpec`, `RepairReport`, `CheckResult` (+ `RunSummary`) with validation;
    `CaseDiscovery`; stage interfaces `ICaseRepairer` / `ICaseChecker` / `ICaseGenerator` (D-003). README updated.
  - `src/M2W.Cli`: `generate`, `repair`, `check`, `run-all` wired (`CliApp`). Roles plug in by implementing a stage
    interface in their own project; until then repair is an honest stub (`status: failed`, no repaired.obj),
    and check/generate exit 2 "not implemented yet". `run-all` finds every folder containing `case.json` at any
    depth (so `cases/_example/case_0007` is found), rejects duplicate caseIds, and writes `out/summary.json`.
  - New shared test projects `tests/M2W.Geometry.Tests` (82 tests: OBJ round trip on case_0007 and an FsCheck
    property on random meshes, reader errors, writer format, measures, example case.json/expected.json validation,
    AGREEMENT.md report/result examples, required-field and bad-value cases, discovery) and `tests/M2W.Cli.Tests`
    (31 tests: run-all discovery, stub honesty, byte-identical reruns, per-fault counts, exit codes, stale-output
    removal, solver contract violations). `M2W.sln` committed with them.
  - `.gitattributes` added (D-008): this machine has `core.autocrlf=true`, which checked out `check-locks.sh`
    with CRLF and broke the lock step of `validate.ps1`.
- M1 criteria: Geometry mesh/OBJ/JSON models ✅ · round-trip test ✅ · case_0007 reads and validates ✅ ·
  m2w runs generate/repair/check/run-all ✅ (stubs, see owner question) · validate runs build, tests, run-all,
  locks and prints the summary ✅.
- validate (`scripts/validate.ps1` and `scripts/validate.sh`): build ✅ (0 warnings) · tests 113/113
  (Geometry 82, Cli 31; role test projects have no tests yet) · cases passing 0/1 (per fault: stackedFloors 0/1,
  smallGap 0/1, duplicateVertices 0/1, farCoordinates 0/1; expected until M2/M3) · locks: skipped, no `lock-v0.1`
  tag yet · **VALIDATE: GREEN**.
- Notes: `report.json` `runtime_ms` is the only output field that is not deterministic (AGREEMENT.md requires it).
  M1 work is committed on branch `m1/foundations` (M1 is shared work, so no role branch fits); merge to `main` at the gate.
- Next: owner gate for M1 (tag `lock-v0.1`), then M2 (generator and checker implement `ICaseGenerator` / `ICaseChecker`).
- Blockers: none. Three non-blocking questions under "Waiting for owner".

<!--
Template — copy for each session:

### YYYY-MM-DD · <role> · <milestone>
- Changed:
- validate: build ✅/❌ · tests x/y · cases passing x/y (per fault: …)
- Next:
- Blockers:
-->
