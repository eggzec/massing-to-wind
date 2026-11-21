# Decision log

One entry per design choice someone could reasonably question. Newest first. Keep each entry short.

<!--
Template:

## D-NNN · YYYY-MM-DD · <role> · <title>
- Context: what problem forced a choice
- Choice: what was decided
- Alternatives: what else was considered
- Why: the deciding reason
- Revisit if: the condition that would change this
-->

## D-038 · 2026-11-22 · checker (M4 gate, Q-S1) · Plan containment of anchors accepts barycentric weights ≥ −1e-9
- Context: `Shell.ContainsInPlan` (anchor matching, D-020) counted a point as inside a plan triangle only if all three
  barycentric weights were ≥ 0. For an anchor exactly on an edge two triangles share (a rectangle's centre on the
  diagonal of its split), rounding gives one weight of about −1e-17 to −2e-16 in **both** triangles, so the shell
  counted as unmatched and `volume_m3`, `footprintArea_m2` and `maxHeight_m` failed on a correct output
  (`case_0284`, Q-S1). Whether it happens depends on the coordinates, the anchor's decimal value and the order in
  which each triangle lists its vertices, not only on which diagonal is used.
- Choice: accept weights ≥ −`Shell.PlanWeightTol` = −1e-9 (owner's answer to Q-S1). Weights have no units, so the
  bound is absolute. In length it is 1e-9 × the triangle's height over the edge: 1e-8 m for a 10 m triangle,
  1e-5 m for a 10 km one, always below the 1e-6 m OBJ resolution at building scale. The bounding-box prefilter is
  padded by 2 × 1e-9 × the box extent per axis (at most two weights can be negative), so it never rejects a point
  the weight test accepts. "Edges included" is kept.
- Audit of the other point and boundary tests in the checker:
  - **Changed:** `Shell.ContainsInPlan`, both the weight test and its box prefilter. Its only caller is
    `Grader.Match`, so the anchor rule (D-020) is the only behaviour affected.
  - **Not changed, `Shell.SolidDistance`** (used by `BlockLocator` for `mustMerge` / `mustStaySeparate`, D-021): a
    point on the surface is caught by its distance to the nearest triangle (≤ `Geom.TouchTol`, 1e-8 m). That
    distance is continuous across shared edges, so a point on one has distance ≈ 0 to both triangles and cannot fall
    between them. Interior points use the winding number with a threshold of 0.5, nowhere near rounding.
  - **Not changed, `BlockLocator` voting:** ties within `AmbiguityTol` (1e-6 m) already stop a point on a wall two
    shells share from voting; this is by design (D-021).
  - **Not changed, `Geom.ClosestPointOnTriangle`:** its Voronoi-region sign tests can tip either way on a region
    boundary, but the closest point is continuous there, so the distance changes only by rounding (~1e-15 m), far
    below `TouchTol`. The same holds for `SegmentTouchesTriangle`, `TrianglesTouch` and `TriangleTriangleDistance`
    (`minGap_m`, `noSelfIntersections`), which compare distances with `TouchTol`.
  - **Not changed, `ShellGap.Distance` nesting test:** winding number of one vertex, threshold 0.5.
  - **Not changed, `SelfIntersection.EdgeEntersCorner`:** an angular test with `AngleTol` (1e-10) on both sides; it
    is not a plan-point test.
  - **Not changed, `Grader.Measure` footprint:** classifies faces, not points (`n.Z >= 0` skips walls and roofs; a
    wall whose normal rounds to −1e-17 in Z adds ~0 m²). `groundContact` uses `weld`.
  - **Not changed, `Box.Distance` in `SolidDistance`:** exact for points within the box, and points on the surface
    have already returned through the distance branch.
- Tests (`AnchorContainmentTests`, hand-built meshes): three boxes, the Q-S1 box among them, with a decimal anchor at
  the centre, under all 4 diagonal splits × 81 vertex rotations of the plan triangles: always contained and all
  checks pass. Each box has triangulations the old rule misses, asserted so the test cannot pass by luck. Also:
  points on outer, corner and slanted edges are contained; 1e-12 m outside an outer edge is contained; 1e-6 m outside
  an axis-aligned or a slanted edge (inside the bounding box) is not; an anchor 1e-6 m outside every shell still
  leaves its expected shell unmatched; the Q-S1 far-coordinate box, shifted to local coordinates and written through
  a 6-decimal OBJ, passes in every triangulation. With the bound set to 0 (old rule), 8 of these 12 tests fail.
- Alternatives: test containment against the bottom face as a polygon (more code, and needs the bottom faces to be
  identified and joined, which a repaired shell does not promise); a tolerance in metres (needs scaling by the
  triangle size to compare with weights; the owner chose the weight bound).
- Why: smallest change that removes the double miss, approved by the owner. Graded results are unchanged: all 291
  visible `result.json` files are byte-identical before and after.
- Revisit if: anchors may legitimately sit within 1e-9 × triangle height outside a shell (not possible with v0.1
  anchors, which are interior points of their shell's plan, D-017).

## D-037 · 2026-11-22 · owner (M4 gate) · Roof steps below `minEdge` are flushed to the higher roof, and expected answers include it
- Context: neighbouring roofs whose heights differ by less than `minEdge` leave a wall step with edges shorter than
  `minEdge`. The expected answers kept the step, so no valid output could match them exactly. The solver already
  raises such steps (D-030, D-036), and the volume difference ate up to 94 % of the tolerance on an unseen case.
- Choice: AGREEMENT.md now says that touching roof regions (sharing an edge or a corner) whose heights differ by less
  than `minEdge` are raised to the highest of them, until no such step remains. The generator computes expected
  `volume_m3` and `maxHeight_m` after this rule and adds a `roofStep` fault. Additive meaning, no format change:
  `schemaVersion` stays 0.1. `lock-v0.1` moved to the commit that adds the text.
- Alternatives: never generate near-equal neighbouring heights (hides a fault real design tools produce); lower to
  the lower roof (changes the graded maximum height); area-weighted height (changes every roof).
- Why: it is the only reading consistent with "edges shorter than `minEdge` are collapsed" that keeps the true
  maximum height, and it bounds the volume change by step × raised roof area.
- Revisit if: wind modellers need the lower roof kept (e.g. the step matters for a specific study).

## D-036 · 2026-11-21 · solver (M4) · Final phase: bake the height field into level outlines, then snap only those
- Context: the randomised properties showed that snapping the raw plan (solid level boundaries plus fill polygons
  for gap bridges, thin regions and jogs) at `minEdge` is fragile: bending a fill's edge onto a point up to
  `minEdge` away can fold a narrow bridge onto itself; keeping fills straight leaves slivers between a fill and a
  bent wall; either way the output was not a fixpoint (a second repair changed it).
- Choice: all plan repairs run at weld precision until nothing changes. Then the height field is baked: one fill
  per roof level z with the outline of "height ≥ z", built from the plan's edges; the solids and earlier fills are
  dropped. Only these nested outlines are snapped (points merge, segments split at points, both at `minEdge`). If
  the snapped plan shows a gap, a thin region or a jog, it becomes the new base (baked again), the repair is added,
  and the loop repeats (at most 12 builds; then `partial`). Which solid is in which shell is found by locating an
  interior point of each face of the last plan built from the solids. Also: roof flushing (D-030) now groups faces
  meeting at a vertex and equal heights, and repeats until stable; thin regions (D-034) take the higher roof they
  share the longest boundary with; gap bridges (D-032) must survive snapping (facing strips at least `minEdge`
  long; fillets as one hull of the wall parts within `minGap + minEdge`).
- Alternatives: repeat the whole repair on its own output until stable (hides why it changes); rigid fills (tried:
  slivers); snapping as a mesh edge-collapse step (needs manifold checks after every collapse).
- Why: after snapping, all points are at least `minEdge` apart and from every segment, so snapping again is a
  no-op; repairs never see a half-snapped fill. Found and checked by the Step8 properties.
- Revisit if: a case needs more than 12 builds (reported in warnings).

## D-035 · 2026-11-21 · solver (M4) · Output slivers are reported, not removed
- Context: 100 of 291 outputs contain triangles below `minAngleDeg` (long low walls, tall narrow walls, thin roof
  pieces). The `sliver` fault itself is gone: the output is rebuilt from the plan, input triangles never survive.
- Choice: count them and say so in `warnings` (status unchanged). No Steiner points yet.
- Alternatives: split long walls and column heights (adds vertices to roofs and walls, more triangles); constrained
  Delaunay roofs with refinement.
- Why: the owner chose not to grade minimum angle in v0.1 (D-025); adding vertices now risks new short edges for no
  graded gain. Honest reporting keeps it visible.
- Revisit if: v0.2 grades `minAngle_deg` or the wind mesher needs it.

## D-034 · 2026-11-21 · solver (M4) · Short outline edges: thin regions raised, jogs by least change of area
- Context: a short edge in the outline is a ledge, a jog, a shallow bump or notch, a cut corner or a split vertex.
  Removing it means moving geometry by less than `minEdge`; the original (which wall was moved) is not knowable.
- Choice: (1) regions thinner than `minEdge` next to a higher roof are raised to it (like D-030 in plan); (2) a
  bump/notch section is cut/filled onto its neighbours' line if it is no longer than both neighbours together; (3)
  a single jog moves the shorter wall onto the other's line; (4) a convex cut corner is squared; (5) whatever is
  left is merged by snapping (D-033).
- Alternatives: always add material (fill outward); always the lower-numbered point; ask the generator's rule.
- Why: least change bounds the error by depth × the shorter wall, whichever piece the fault moved. On the visible
  cases: ledges exact, bumps exact, two jogs at 19 % and 32 % of the tolerance where the fault had moved the
  other piece. Reading the generator's rule would tune the solver to the grader.
- Revisit if: hidden cases fail on jogs close to `minEdge` deep along long walls.
- Revised by D-036: a thin region takes the higher roof it shares the longest boundary with.

## D-033 · 2026-11-21 · solver (M4) · No plan edge shorter than minEdge: final plan merged at minEdge, by priority
- Context: after the outline rules some short edges remain (several in a row, kinks, crossings).
- Choice: the final plan is planarised with tolerance `minEdge − 1e-6`: points closer are one point, a segment
  closer to a point is split there. All points are then pairwise ≥ `minEdge` apart, so every plan edge, wall edge and
  roof diagonal is. Surviving point: lowest outline first (the footprint at the ground), then the sharper corner of
  the weld-precision plan, then the higher roof. Gaps are bridged before (at weld precision).
- Alternatives: collapse edges on the final mesh (needs re-checking manifoldness and crossings); first-come order.
- Why: one rule gives the guarantee by construction. First-come order could move a tall block's wall onto a lower
  block across a narrow gap (found by the FsCheck property), or shave half a ledge off.
- Revisit if: snapping is ever seen to change topology (a courtyard closing); the solver's own check reports any
  edge below `minEdge` as `partial`.
- Revised by D-036: only baked level outlines are snapped, never fills on their own.

## D-032 · 2026-11-21 · solver (M4) · Gap closing: per-level closing between components, strips for facing walls
- Context: AGREEMENT.md: "gaps narrower than minGap are closed and the blocks merged". A disk closing of the plan
  would round every concave corner (arcs, many vertices); a square one depends on the axes, and sites are rotated.
- Choice: at each roof level, close only between different connected components of the slice "height ≥ level".
  Facing, nearly parallel walls (within 10°) get the strip where one projects onto the other; components without
  such walls get convex-hull fillets of the parts of their walls within minGap of each other. Each bridge is filled
  from the ground to the level, so a gap is filled to the lower block (the grey-scale closing of the height field).
  Distances within 1e-6 m of minGap are not closed.
- Alternatives: true disk closing (offset out then in) with arcs approximated; Clipper2 offsets (licence, D-027);
  also closing narrow inlets within one part.
- Why: the strip adds no vertices when walls end together, is exact for parallel walls and independent of
  rotation; fillets are only used where nothing else connects. Inlets are within one shell, and the agreement asks
  for gaps between blocks.
- Revisit if: hidden cases need inlets or narrow courtyards closed, or curved/sharply non-parallel gaps need a
  better bridge than a hull.
- Revised by D-036: bridges must survive snapping (strip length and fillet reach).

## D-027 · 2026-11-19 · solver (M3) · Per-object vertex lists; own polygon geometry, no Clipper
- Context: blocks that touch may share or nearly share vertices; ground snap and welding must not move another
  block's geometry. Step 4 needs 2D polygon work (union, later offsets).
- Choice: each input object becomes a `Solid` with its own copy of the vertices it uses; welding runs inside one
  object. Planar geometry (triangulation now, the plan arrangement in step 4) is written in `M2W.Repair`.
- Alternatives: weld the whole input globally; Clipper2 for polygon booleans and offsets.
- Why: per-object keeps steps independent and lets the union step see coincident faces explicitly. Clipper2 is
  Boost-licensed (not MIT/Apache/BSD as CLAUDE.md lists) and was not in the package cache; owning the geometry
  also keeps tolerances under the solver's control.
- Revisit if: offsetting for `smallGap` (M4) turns out too costly to write; then ask the owner about Clipper2.

## D-029 · 2026-11-19 · solver (M3) · Output is rebuilt as a 2.5D height field, not a mesh boolean
- Context: stacked floors, overlaps and touching walls all need a union of closed solids that gives clean closed
  shells, with coincident faces, crossings and mm-level misalignment.
- Choice: union in plan. Each object's horizontal faces give tagged level boundaries; these are planarised, plan
  faces are labelled by tag parity, and each face gets the highest roof over it, filled down to the ground. Shells
  are rebuilt from the plan (ground face, roofs, walls with split columns). Walls of the input are not used.
- Alternatives: 3D mesh booleans (triangle-triangle intersection, re-triangulation); voxels; footprint union per
  height band followed by stitching bands together.
- Why: in plan, coincident faces and crossings are a 2D problem with one tolerance; parity labelling needs no input
  orientation; building shells from the plan makes them closed by construction. The generator's expected answer
  and AGREEMENT.md's output rules (closed bottom at Z = 0) are 2.5D as well.
- Limits: sloped or overhanging geometry is not represented (reported `partial`); anything floating beyond
  `groundSnap` is filled down to the ground (reported `partial`).
- Revisit if: hidden cases contain sloped roofs or real overhangs (would need a 3D boolean or a sloped-roof
  height field).

## D-030 · 2026-11-19 · solver (M3) · Roof steps lower than `minEdge` between neighbours are raised to the higher roof
- Context: two neighbouring roofs at 17.4 and 17.5 m leave a 0.1 m wall step, i.e. edges shorter than `minEdge`
  (0.25 m), which AGREEMENT.md says are collapsed.
- Choice: faces that share a plan edge and differ in height by less than `minEdge` are grouped; each group takes its
  highest roof. Only adjacent faces change.
- Alternatives: lower to the lower roof; area-weighted height; snap all levels globally within `minEdge`.
- Why: raising keeps the true maximum height (graded to 0.01 m) and changes volume by at most step × roof area;
  global snapping would wrongly change the height of separate blocks.
- Revisit if: a case needs the lower roof kept (the volume change would show it).
- Revised by D-036: faces meeting at a vertex and equal heights are grouped too; repeated until stable.

## D-031 · 2026-11-19 · solver (M3) · Optimal triangulation for roofs up to 150 vertices
- Context: greedy ear clipping stranded nearly collinear vertices (B03's corners on B02's roof edge) and was forced
  into near-zero-area triangles, which the checker counts as self-intersections.
- Choice: dynamic programming over valid diagonals that maximises the smallest triangle angle (tie: larger sum).
  Holes are bridged first. Ear clipping (strict ears only) stays as the fallback above 150 vertices.
- Alternatives: constrained Delaunay triangulation; dropping collinear vertices (leaves T-junctions).
- Why: exact, short, and roof polygons are small (O(n³) is cheap at this size).
- Revisit if: roofs get much larger (then implement constrained Delaunay).

## D-028 · 2026-11-19 · solver (M3) · `runtime_ms` is measured, so report.json is not byte-identical run to run
- Context: CLAUDE.md asks for byte-identical output; AGREEMENT.md asks for `runtime_ms`.
- Choice: `repaired.obj` is byte-identical for the same input (tested); `report.json` differs only in `runtime_ms`.
- Alternatives: always write 0 (false information).
- Why: a fixed fake runtime would misreport; every other field is deterministic.
- Revisit if: the owner prefers `runtime_ms` omitted or zeroed.

## D-026 · 2026-11-18 · owner (pre-M3) · The lock check fails when it cannot use git; validate.ps1 uses Git Bash
- Context: on Windows, plain `bash` is WSL bash. Inside a git worktree, WSL's git cannot follow the `.git` file
  (it holds a Windows path), so `check-locks.sh` read that as "no lock-v0.1 tag", skipped, and validate stayed GREEN.
- Choice: `check-locks.sh` first runs `git rev-parse --git-dir` and exits 1, printing git's error, if that
  fails. A failing `git diff` / `git ls-files` also exits 1. A missing tag in a readable repository still skips,
  but now prints `WARNING`. `validate.ps1` finds `bash.exe` next to `git.exe` (Git for Windows) and fails the
  locks step if it cannot find it, rather than falling back to WSL.
- Alternatives: also fail when the tag is missing (rejected for now: a fresh CI checkout without tags would go RED
  for the wrong reason; revisit with M5 CI, using `fetch-tags`); support WSL by rewriting the `.git` path (fragile).
- Why: a lock check that cannot run must never look like a passing one.
- Revisit if: M5 CI is set up. Then make a missing tag fail too, once CI fetches tags.

## D-025 · 2026-11-18 · owner (M2, checker Q3) · Thin slivers are not graded in v0.1
- Context: after zero repair, 16 of 30 `sliver` cases pass every check, because no check looks at triangle angles
  (the zero-area slivers are caught by `noSelfIntersections`).
- Choice: owner chose option B. There is no minimum-angle check in v0.1, and the limit is documented in
  KNOWN_LIMITATIONS.md. The checker keeps its 15 checks.
- Alternatives: option A, a 16th check `minAngle_deg` ≥ `case.json` `minAngleDeg`.
- Why: a minimum-angle check would also fail valid repairs of long, low walls unless the solver adds vertices;
  not worth it for v0.1.
- Revisit if: v0.2 shows the wind mesher needs a minimum angle.

## D-024 · 2026-11-18 · checker (M2) · Shape of `result.json` entries
- Context: AGREEMENT.md gives `name/pass/value/limit` and says `watertight` value = free edges, nothing more.
- Choice: always the same 15 checks in the same order (`CheckNames.All`). Count checks (`parse`, `watertight`,
  `manifold`, `outwardNormals`, `euler`, `noSelfIntersections`, `mustMerge`, `mustStaySeparate`): value = number of
  violations, limit 0. `minGap_m`, `minEdge_m`: value = measured minimum, limit = expected. `shellCount`: count vs
  expected. `volume_m3`, `footprintArea_m2`, `maxHeight_m`: value/limit of the worst shell. `groundContact`: value =
  largest |lowest Z|, limit = weld. Each entry has a `detail` string (kept in `CheckEntry.Extra`, no schema change).
  If `repaired.obj` does not parse, `parse` fails and every other check fails with "not checked". `minGap_m` with
  fewer than two shells passes with value 0 and says so. `expected.json` `checks.watertight/outwardNormals/
  noSelfIntersections = false` → still measured and reported, but not enforced.
- Alternatives: one entry per shell per check; omit checks that cannot run.
- Why: stable names make `summary.json` `failedChecks` comparable across cases; detail keeps it debuggable.
- Revisit if: the owner wants per-shell entries.

## D-023 · 2026-11-18 · checker (M2) · Numeric tolerances inside the checker
- Context: exact geometry tests need a "touching" threshold, and the OBJ format rounds to 6 decimals.
- Choice: `Geom.TouchTol` = 1e-8 m for intersection/contact. `minGap_m` and `minEdge_m` pass at `limit − 1e-6`
  (one unit of the 6th decimal: a length on the limit can read back that much short). `groundContact` uses
  `case.json` `weld`. Volume/area/height use exactly the `expected.json` tolerances.
- Alternatives: no slack (an on-limit 1.0 m gap could fail by 1e-7); slack = `weld` (too generous: 1 mm).
- Why: strict where the agreement is strict, never stricter than the file format can represent. None of these is
  an AGREEMENT.md tolerance default.
- Revisit if: OBJ precision changes.

## D-022 · 2026-11-18 · checker (M2) · Footprint and height definitions
- Context: AGREEMENT.md names `footprintArea_m2` and `maxHeight_m` without defining them.
- Choice: footprint = plan area of the downward-facing triangles lying at the shell's lowest Z (within `weld`), i.e.
  the closed bottom face; height = highest Z (ground is Z = 0). Measured at the shell's own base so a floating
  shell fails `groundContact` only, not also footprint.
- Alternatives: area of the shell's projection onto the ground (differs only for overhangs/cantilevers).
- Why: matches "every shell has a closed bottom face at Z = 0" and the case_0007 example (303 m²). The generator
  uses the same definition (its D-015), and its massings never overhang, so the two readings agree on every case.
- Revisit if: a future generator version adds overhangs or cantilevers.

## D-021 · 2026-11-18 · checker (M2) · Locating input blocks in repaired shells (mustMerge / mustStaySeparate)
- Context: groups name input blocks (`B01`), but output objects are `shell_NNN`; the solver's naming says nothing.
- Choice: read `input.obj`, subtract `origin`, group objects by block (`B01_F03` → `B01`). Each block's vertices
  and face centres vote for the repaired shell whose solid is nearest (0 inside, by winding number), if within
  `minGap + groundSnap + minEdge` of it. A point equally near two shells (within 1e-6 m, e.g. on a shared wall)
  does not vote. Most votes wins; ties go to the lowest shell index; no votes = "not found" = group violated.
  `input.obj` is read only when there are groups.
- Alternatives: trust object order or the solver's report (not allowed); block centroid only (fails for L/U
  shapes and moved blocks); the new `expected.shells[].blocks` (D-017) says which blocks belong together but not
  where they ended up in the repair, so it cannot replace this.
- Why: purely positional, so it cannot be gamed by naming, and it survives the legitimate moves a repair makes
  (ground snap, gap closing, short-edge collapse).
- Revisit if: a fault moves a block further than that distance.

## D-020 · 2026-11-18 · checker (M2) · Matching expected shells to repaired shells: by anchor, best fit as fallback
- Context: the brief says "match shells by position". The owner approved `anchor_m` and `blocks` per expected
  shell (generator D-017) in answer to the checker's Q1.
- Choice: one-to-one minimum-cost assignment (Hungarian method). An expected shell with `anchor_m` may only pair
  with a repaired shell that contains the anchor in plan (some triangle's ground projection covers it); if no
  free one does, it stays unmatched and fails volume, footprint and height. Among allowed pairs the cost is the
  volume, footprint and height errors in tolerance units plus 1 per genus mismatch (this decides, for example,
  between stacked floors left as separate shells that all cover the anchor). Expected shells without an anchor
  (case_0007, hidden cases written before D-017) use best fit over all shells. `euler` compares each matched shell
  with its expected genus. `blocks` is not used for matching (D-021).
- Alternatives: best fit only (a repair that swaps two blocks' sizes could pass; a test shows this); file order.
- Why: matching that no fault can move, and no stricter than the agreement for files without anchors.
- Revisit if: a shell can be hollow in plan at its anchor (not possible with v0.1 massings, per D-017).

## D-019 · 2026-11-18 · checker (M2) · Self-intersection scope and adjacency
- Context: "no two non-adjacent triangles intersect" leaves out folds between triangles that do share vertices.
- Choice: per shell (contact between shells is `minGap_m`). Broad phase: sweep and prune on X over triangle boxes.
  Narrow phase by shared vertex indices: 0 → any contact (exact segment/triangle tests, 1e-8 m); 1 → the opposite
  edges touch the other triangle, or an edge leaving the shared vertex lies in the other triangle's plane and
  points into (or along a side of) its corner there, judged by angle (1e-10); 2 → coplanar fold onto the same
  side of the shared edge, or a zero-area face lying on it; 3 → duplicate face. So a zero-area sliver counts as a
  self-intersection; a thin but non-degenerate one does not (not graded in v0.1, D-025).
- Alternatives: skip all adjacent pairs (misses folds); uniform 3D grid (large roof triangles span many cells).
  The first version tested shared-vertex edges by distance after cutting 0.1 mm off them. On rotated plots with
  mm-rounded coordinates (case_0195) that flagged valid, nearly flat fans 3e-8 rad apart, which made the check
  stricter than the agreement. It was replaced by the angular test, and a regression test covers it.
- Why: correctness first; 21,600 triangles grade in about 3 s; all 290 generated cases in about 17 s.
- Revisit if: hidden cases are much larger (then switch to a BVH).

## D-018 · 2026-11-18 · checker (M2) · Topology by vertex index; checker does not weld
- Context: two vertices at the same position with different indices could be treated as one or two.
- Choice: by index. A shell is one `o` object; `watertight`, `manifold`, `outwardNormals` and `euler` use its
  faces' vertex indices. Duplicate positions are therefore free edges, which is the `duplicateVertices` fault the
  solver must fix. Polygons are fan-triangulated for analysis (and fail `parse`).
- Alternatives: weld coincident positions within `weld` before checking.
- Why: the wind mesher sees indices; welding in the checker would hide an unrepaired fault.
- Revisit if: the owner says output may legitimately contain duplicate positions.
## D-017 · 2026-11-18 · generator (M2) · expected.shells[] gains `blocks` and `anchor_m` (owner-approved)
- Context: the checker matches expected to repaired shells "by position", but expected.json had no positions;
  best fit on volume/footprint/height/genus is ambiguous when shells are alike (0 cases at seed 1, 1 at seed 90001)
  and can pair a wrong repair with the wrong answer.
- Choice (owner approved 2026-11-18): two optional fields per expected shell. `blocks`: the block names in the shell
  (same names as mustMerge; `B01_F03` belongs to `B01`). `anchor_m`: [x, y] in local coordinates (input minus
  origin, the frame of repaired.obj), strictly inside the shell's footprint: the grid-cell centre with the largest
  clearance from everything outside the shell, rounded to 1 mm. The checker matches a repaired shell by "contains
  the anchor in plan". Tested: each anchor ±0.5 m in x and y lies inside its own blocks and outside all others.
  `ExpectedShell` in M2W.Geometry got two optional properties (additive; old files such as case_0007 still read).
  `schemaVersion` stays 0.1 (not a breaking change). AGREEMENT.md text is with the owner (PROGRESS, Waiting for owner).
- Alternatives: `blocks` only (checker would derive positions from the faulted input.obj: lifted, offset, and the
  centroid of an L/U/courtyard can be outside the building); keep best fit.
- Why: matching independent of every fault, with no new solver-visible information.
- Revisit if: shells could ever be hollow in plan at the anchor (not possible with v0.1 massings).

## D-016 · 2026-11-18 · generator (M2) · Case IDs, order, seeds and regeneration
- Context: `run-all` rejects duplicate caseIds, and `cases/_example/case_0007` already exists. Hidden sets use other seeds.
- Choice: generated IDs run from `case_0101` (`case_0001`–`0100` kept for hand-made cases). The 290 recipes are
  shuffled with the master seed before IDs are given, so an ID says nothing about its faults and differs per seed.
  Each case has its own seed (hash of master seed and recipe position), recorded in `expected.json` and `index.json`.
  `generate` first deletes only `case_NNNN` folders that contain `case.json`.
- Alternatives: IDs grouped by fault (easier to browse, but invites ID-based overfitting); skipping only `0007`.
- Why: IDs stay clear of hand-made cases and carry no information; `index.json` gives the mapping for people.
- Revisit if: the owner wants stable IDs across seeds.

## D-015 · 2026-11-18 · generator (M2) · Exact ground truth from a 2.5D height field in a grid frame
- Context: expected.json must be computed from the clean geometry, including unions (overlap, touching, closed gaps)
  and genus, with no hand-typed numbers and no general 3D boolean library.
- Choice: every site has one grid frame (direction of its longest edge, a unit vector built with one square root,
  no trigonometry). Footprints are unions of rectangles in that frame; tiers (setbacks) are nested; everything
  stands on z = 0. So the union is a height field on a rectilinear grid: volume, footprint, height, shells and genus
  (footprint holes) are exact sums. Configurations that would be ambiguous are rejected and redrawn: diagonal-only
  contact (non-manifold), outline steps closer than `max(2 m, 2.5 × minGap, 4 × minEdge)`, separate shells closer
  than `max(4 m, 3 × minGap)` (except deliberate smallGap-above pairs). A test casts vertical rays through every
  emitted mesh and checks it against the height field, cell by cell, for all 290 cases.
  **Footprint** (`footprintArea_m2`) = area of the shell's bottom face at z = 0 (agreed with the checker,
  2026-11-18). Upper tiers always sit inside lower ones and everything stands on the ground, so it always equals the
  full plan outline (courtyards excluded either way); the two would differ only with overhangs, which v0.1 never makes.
- Alternatives: arbitrary-angle footprints with a polygon clipping library; voxel estimates (not exact).
- Why: exact, fast, independent of the solver's methods; the plot and the grid can still be at any angle.
- Revisit if: v0.2 needs curved or non-orthogonal footprints, bridges or overhangs.

## D-014 · 2026-11-18 · generator (M2) · smallGap answer: the gap is filled up to the lower block
- Context: AGREEMENT.md says gaps narrower than `minGap` are closed and the blocks merged, but not how high the
  fill goes when the blocks differ.
- Choice: below-threshold pairs are built so the answer is unambiguous. The smaller block B faces the larger block A
  across the gap, B's facing wall lies within A's, and B is no taller than A along it. The expected model extends B
  across the gap (including tiers that are flush with that wall), which is what morphological closing gives. Half the
  smallGap cases are above the threshold (1.1–2.0 × minGap) and must stay separate (`mustStaySeparate`); they are
  labelled `smallGap` too, because they test the same rule from the other side. Exactly 0.9 × and 1.1 × appear in
  4 single-fault cases each. 30 % of smallGap cases use `minGap` 0.5, 0.75 or 1.5 instead of 1.0.
- Alternatives: fill to the taller block (not what closing does); allow partial facing overlaps (fillets at the
  ends make the answer depend on the structuring element).
- Why: fair to any reasonable closing implementation; the threshold is tested from both sides.
- Revisit if: the owner wants gaps between walls of different extents.

## D-013 · 2026-11-18 · generator (M2) · floatingBlock / sunkBlock: rigid move within groundSnap, answer moves it back
- Context: "blocks within groundSnap of z = 0 are snapped to the ground". A lifted block could be moved down
  (height unchanged) or stretched down (height + lift). The two differ by more than the 1 % volume tolerance.
- Choice: the fault is a rigid vertical move of a whole block (all its floors) by 0.1–0.9 × `groundSnap`
  (0.9 × in a quarter of the single-fault cases). The expected answer is the clean model: moved back, height
  unchanged. Never beyond `groundSnap`, where the agreement gives no answer.
- Alternatives: stretch to the ground; also generate lifts above `groundSnap` (undefined by the agreement).
- Why: "snap" reads as "move"; the clean model is the design intent.
- Revisit if: the owner reads "snap" as "extend".

## D-012 · 2026-11-18 · generator (M2) · Sliver and shortEdge: what is injected and what is expected
- Context: both faults are "remove what is too small", so the repaired geometry should not depend on how it is removed.
- Choice: `sliver` adds thin triangles (smallest angle < 0.3 × `minAngleDeg`) or zero-area triangles along shared
  edges, re-routing the face through the new vertex; the surface stays closed and the solid is unchanged.
  `shortEdge` (one third each) adds a split vertex `0.1–0.8 × minEdge` from an edge end (geometry unchanged), or a
  ledge (upper floors set back by ε on one side), or a jog (a footprint rectangle moved ε off its neighbour's line),
  with ε = 0.1–0.8 × `minEdge`. Expected: the flush design. A ledge or jog may change a shell's volume by at most
  0.3 %, so filling it or cutting it both pass. Blocks of a closing gap pair are never given a ledge (it could push
  the gap over `minGap`).
- Alternatives: slivers that change geometry (slanted thin faces); bigger misalignments.
- Why: realistic exporter and modelling defects with one clear answer.
- Revisit if: the checker adds a minimum-angle check (the expected answer already has none below `minAngleDeg`).

## D-011 · 2026-11-18 · generator (M2) · Clean controls are already valid output
- Context: M2 requires clean controls that pass the checker "with zero repair".
- Choice: clean controls have origin [0, 0, 0], triangles only, one closed outward shell per block, every block on
  z = 0, no triangle angle below `minAngleDeg`, no edge below `minEdge`, blocks ≥ 4 m apart. Object names are
  `B01`, `B02` … (the input naming in AGREEMENT.md), not `shell_NNN`. Triangle meshes split walls at every floor and
  keep grid cells at an aspect ratio of 4 or less (all angles ≥ 14°). Other cases use polygons (55 %) or triangles.
- Alternatives: name objects `shell_NNN` (breaks the input naming rule); polygons (not valid output).
- Why: the only "repair" a clean control needs is a rename, which lets the checker prove it is not too strict.
- Revisit if: the checker wants clean inputs that are byte-for-byte valid `repaired.obj` files.

## D-010 · 2026-11-18 · generator (M2) · Tolerances vary in some cases
- Context: a solver that hard-codes the AGREEMENT.md defaults would pass every case if the defaults never changed.
- Choice: in about 30 % of the cases with the relevant fault, case.json moves one threshold: `minGap` 0.5/0.75/1.5
  (smallGap), `minEdge` 0.15/0.4 (shortEdge), `groundSnap` 0.25/1.0 (floating/sunk), `weld` 0.0005/0.005
  (duplicates), `minAngleDeg` 3/10 (sliver). `expected.json` `checks.minGap_m`/`minEdge_m` copy the case values.
  Clean controls use the defaults. The defaults themselves are unchanged.
- Alternatives: defaults everywhere.
- Why: a harder, fairer exam; it is what case.json tolerances are for.
- Revisit if: the owner wants v0.1 cases on defaults only.

## D-009 · 2026-11-18 · generator (M2) · Determinism: own PRNG, no trigonometry, a committed-cases test
- Context: same seed must give byte-identical files, ideally on every OS and .NET patch version.
- Choice: xoshiro256** seeded by SplitMix64, streams forked by tag (FNV-1a) so one extra draw does not shift other
  cases; directions from integer vectors and `Math.Sqrt` (IEEE exact) instead of `Math.Sin/Cos`; coordinates rounded
  to 6 decimals. `Committed_cases_match_the_generator` regenerates seed 1 and compares every byte with `cases/`.
- Alternatives: `System.Random` (algorithm may change), `Math.Sin/Cos` (results may differ by platform in the last bit).
- Why: hidden cases on the owner's machine and CI on Linux must match.
- Revisit if: the committed-cases test ever fails on CI.

## D-008 · 2026-11-18 · shared (M1) · `.gitattributes` forces LF for `*.sh`, `*.obj`, `*.json`
- Context: with `core.autocrlf=true` and no `.gitattributes`, Windows checkouts got CRLF; `check-locks.sh` then
  failed in bash (`set: pipefail\r: invalid option name`) and the example OBJ was CRLF despite AGREEMENT.md saying LF.
- Choice: `* text=auto` plus `eol=lf` for `*.sh`, `*.obj`, `*.json`. Repository content is unchanged (already LF).
- Alternatives: tell each machine to set `core.autocrlf=false`; make the reader/scripts CR-tolerant only.
- Why: one file fixes it for every clone, and generated cases stay byte-identical across platforms.
- Revisit if: the owner prefers a different line-ending policy.

## D-007 · 2026-11-18 · shared (M1) · Strict readers; only tolerances have defaults
- Context: AGREEMENT.md says "a missing required field is an input error" but does not list which fields are required.
- Choice: every field shown in the AGREEMENT.md examples is required, except `case.json` `tolerances` (and each
  tolerance inside it), which fall back to the AGREEMENT.md default table. `schemaVersion` must be exactly `"0.1"`.
  `faultsInjected` must use the 12 v0.1 fault names. OBJ: unknown keywords, index 0, out-of-range indices,
  non-finite numbers and faces with fewer than 3 vertices are errors; `vn vt usemtl mtllib g s` are ignored.
  Extra numbers after `v x y z` and `/t/n` suffixes on face indices are accepted and dropped.
- Alternatives: lenient readers that guess; every tolerance required.
- Why: the generator writes every field, so strictness only catches real mistakes; the tolerance table is
  explicitly labelled "Default".
- Revisit if: hidden or third-party inputs use OBJ lines outside the AGREEMENT.md list.

## D-006 · 2026-11-18 · shared (M1) · Exit codes where AGREEMENT.md is silent
- Context: AGREEMENT.md defines exit codes for `repair` and `check` only, and does not cover `partial`.
- Choice: `repair`: `repaired` → 0, `partial` or `failed` → 1, bad input → 2. `check`: missing/invalid case.json,
  expected.json or repaired.obj → 2; no checker yet → 2. `run-all`: 0 once every case has run and summary.json is
  written, whatever the pass rate; 2 for a missing folder, no cases, or two folders sharing a caseId. `generate`:
  no generator yet → 2. Unexpected exceptions are printed in full and give 1.
- Alternatives: `run-all` exits 1 if any case fails (would make `validate` red until every case passes).
- Why: `run-all` is the harness; pass rates live in summary.json, which validate prints. `partial` is not a full repair.
- Revisit if: the owner wants CI to fail on a pass-rate threshold (add a `--min-pass` option rather than changing 0).
- Owner approved at the M1 gate (2026-11-18), including `partial` → 1 and stub `generate`/`check` until M2.

## D-005 · 2026-11-18 · shared (M1) · m2w deletes stale output before each run
- Context: if a solver stops writing `repaired.obj`, an old copy in `out/<caseId>/` would still be graded.
- Choice: `repair` deletes `repaired.obj`, `report.json`, `result.json` in `out/<caseId>/` first; `check` deletes
  `result.json` first. Nothing else in `out/` is touched.
- Alternatives: wipe `out/` at the start of `run-all`.
- Why: makes a fake pass from stale files impossible without deleting anything we didn't write.
- Revisit if: never, unless outputs gain new files.

## D-004 · 2026-11-18 · shared (M1) · Layout of `out/summary.json`
- Context: AGREEMENT.md asks for "pass counts per fault type" but gives no layout; M1 needs `run-all` to write it.
- Choice: `{ schemaVersion, casesTotal, casesPassed, byFault: { <fault>: { total, passed } }, cases: [ { caseId, path,
  faults, repairExit, checkExit, pass, failedChecks } ] }`. Faults come from each case's `expected.json`
  `faultsInjected` (m2w may read it; the solver never sees it). No faults → `clean`; unreadable → `unknown`.
  A multi-fault case counts once under each fault. Written by `M2W.Cli` (`RunSummary` model in M2W.Geometry).
- Alternatives: read faults from `cases/index.json` (does not exist yet; per-case files are always present);
  let the checker write it (the checker brief says so, but there is no checker in M1).
- Why: works today and in M2 without depending on index.json; per-case rows make failures easy to find.
- Revisit if: the checker takes it over. (Owner approved this layout at the M1 gate, 2026-11-18.)

## D-003 · 2026-11-18 · shared (M1) · Roles plug into m2w through interfaces found by reflection
- Context: three roles on three branches all need `m2w` to call their code. If each edits `Program.cs`, every
  merge conflicts, and `M2W.Repair` must not reference the checker or generator.
- Choice: `M2W.Geometry.Stages` defines `ICaseRepairer`, `ICaseChecker`, `ICaseGenerator`. `M2W.Cli/StageSet`
  loads `M2W.Repair`, `M2W.Checker`, `M2W.Generator` and uses the single public implementation in each (more than one
  is an error). The solver's request holds only `CaseSpec` and the input `Mesh`, so it physically cannot reach
  `expected.json`. m2w validates and writes all output files itself.
- Alternatives: each role edits the CLI switch; a plugin folder scanned at run time.
- Why: no shared-file edits after M1, honest-by-construction solver input, one place that enforces output rules.
- Revisit if: a role needs more inputs than the request carries (add a property; do not pass the case folder to the solver).

## D-002 · 2026-11-18 · shared (M1) · Separate test projects for shared code
- Context: M1 needs tests for `M2W.Geometry` and `M2W.Cli`, but the three existing test projects each belong to a role.
- Choice: add `tests/M2W.Geometry.Tests` and `tests/M2W.Cli.Tests` (same xUnit + FsCheck versions as the others),
  and add them to `M2W.sln`, which is now committed.
- Alternatives: put shared tests in a role's test project.
- Why: keeps role ownership clean; shared tests travel with shared code.
- Revisit if: the owner wants shared tests under LOCKS after the M1 gate.

## D-001 · 2026-11-18 · owner · Pure C#, no external validators in v0.1
- Context: one-week build; the checker must close the loop without OpenFOAM or Python.
- Choice: all generation, repair and checking in C# / .NET 8.
- Alternatives: OpenFOAM `surfaceCheck`/`checkMesh`, Python trimesh as independent validators.
- Why: keeps the loop fast and the stack aligned with the target role.
- Revisit if: v0.2 adds external validation (roadmap step 2).
