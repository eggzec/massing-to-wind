# Massing-to-Wind — Interface Agreement v0.1

Owner: Saif ur Rehman · As of 2026-11-18 · LOCKED: changes need owner approval and a new `schemaVersion`.

## Purpose and roles

Three roles share one contract. The generator makes broken massings together with their expected answers, the solver repairs them, and the checker grades the result. No role edits another role's files.

| Role | Language | Writes | Reads | Must not touch |
| --- | --- | --- | --- | --- |
| Test-case generator (agent) | C# | `input.obj`, `case.json`, `expected.json` | This agreement | Solver source |
| Solver (agent) | C# | `repaired.obj`, `report.json` | `input.obj`, `case.json` only | `expected.json`, generator and checker source |
| Checker (locked after Day 1) | C# | `result.json` | Everything in the case folder | Solver source |
| Owner (Saif) | — | Approvals, hidden cases | Everything | — |

The solver never sees the expected answers. It must find and fix faults itself, which keeps the loop closed and honest.

## Conventions

All geometry is in metres with Z up, the ground at Z = 0 and outward-facing normals. Every value is a 64-bit double.

- **Axes:** right-handed. X is east, Y is north, Z is up.
- **Faces:** vertices counter-clockwise when seen from outside, so normals point outward.
- **Coordinates:** input may use large map coordinates, e.g. 530000, 180000. `case.json` gives the `origin`, and the solver writes output in local coordinates (input minus origin).
- **Precision:** OBJ (Wavefront Object) numbers are written with 6 decimal places.

| Tolerance | Meaning | Default |
| --- | --- | --- |
| `cellSize` | Target wind-model cell size near buildings | 1.0 m |
| `minGap` | Gaps narrower than this are closed and the blocks merged | 1.0 m |
| `minEdge` | Edges shorter than this are collapsed | 0.25 m |
| `weld` | Vertices closer than this are treated as one | 0.001 m |
| `minAngleDeg` | Faces with a smaller angle count as slivers | 5° |
| `groundSnap` | Blocks within this distance of Z = 0 are snapped to the ground | 0.5 m |

## Geometry files (OBJ)

Both input and output are plain-text Wavefront Object files. The input may contain any fault; the output must be clean closed shells made of triangles.

**Shared rules**

- UTF-8 text, LF line endings.
- Allowed lines: `v x y z`, `f i j k …` (1-based vertex indices), `o name`, `#` comments.
- Ignored if present: `vn`, `vt`, `usemtl`, `mtllib`, `g`, `s`.

**Input: `input.obj`**

- One `o` per object as the generator emitted it, named `B<block>_F<floor>` (e.g. `o B01_F03`) or `B<block>` for a whole block.
- Faces may be triangles or planar polygons.
- May contain any injected fault: overlaps, stacked floors, touching walls, small gaps, slivers, duplicate vertices, open surfaces, flipped faces, floating blocks, far coordinates.

**Output: `repaired.obj`**

- Triangles only, in local coordinates.
- One `o shell_NNN` per closed shell (`shell_000`, `shell_001`, …).
- Within a shell, every edge is shared by exactly two faces, all normals point outward, and nothing self-intersects.
- Every shell has a closed bottom face at Z = 0.

## Case files (JSON)

Each case has two JSON (JavaScript Object Notation) files. `case.json` holds the settings the solver may read. `expected.json` holds the answers, and only the checker reads it.

**`case.json`** (solver may read)

```json
{
  "schemaVersion": "0.1",
  "caseId": "case_0007",
  "units": "m",
  "origin": [530000.0, 180000.0, 0.0],
  "tolerances": {
    "cellSize": 1.0, "minGap": 1.0, "minEdge": 0.25,
    "weld": 0.001, "minAngleDeg": 5.0, "groundSnap": 0.5
  },
  "files": { "input": "input.obj" }
}
```

**`expected.json`** (checker only)

Example: block B01 (20 × 10 m) is emitted as six stacked 3 m floors, and block B02 (10 × 10 m) is whole. Both are 18 m tall, with a 0.3 m gap between them. After repair they form one shell of 30.3 × 10 m: footprint 303 m², volume 5,454 m³.

```json
{
  "schemaVersion": "0.1",
  "caseId": "case_0007",
  "seed": 4127,
  "faultsInjected": ["stackedFloors", "smallGap", "duplicateVertices"],
  "expected": {
    "shellCount": 1,
    "shells": [
      { "volume_m3": 5454.0, "footprintArea_m2": 303.0,
        "maxHeight_m": 18.0, "genus": 0, "blocks": ["B01", "B02"], "anchor_m": [10.0, 5.0] }
    ],
    "mustMerge": [["B01", "B02"]],
    "mustStaySeparate": []
  },
  "checks": {
    "watertight": true, "outwardNormals": true,
    "noSelfIntersections": true,
    "minGap_m": 1.0, "minEdge_m": 0.25
  },
  "tolerances": { "volumeRel": 0.01, "areaRel": 0.01, "height_m": 0.01 }
}
```

`genus` is the number of through-holes, such as courtyards. The checker tests it with the Euler number: V − E + F = 2 − 2 × genus for each shell.

Each entry in `expected.shells` may also carry `blocks` (the input block names in that shell, e.g. `["B01", "B02"]`) and `anchor_m` (`[x, y]` in local coordinates, strictly inside the shell's footprint). The checker matches a repaired shell to an expected one by which shell contains the anchor in plan. `footprintArea_m2` is the area of the shell's bottom face at z = 0.

## Solver report and checker result

The solver explains what it did in `report.json`. The checker grades the case in `result.json`. Both are written to `out/<caseId>/`.

**`report.json`** (written by the solver)

```json
{
  "schemaVersion": "0.1",
  "caseId": "case_0007",
  "solverVersion": "0.1.0",
  "status": "repaired",
  "shiftApplied": [-530000.0, -180000.0, 0.0],
  "shellCount": 1,
  "operations": [
    { "op": "weld", "count": 48 },
    { "op": "mergeStackedFloors", "block": "B01", "count": 6 },
    { "op": "closeGap", "pair": ["B01", "B02"], "width_m": 0.3 }
  ],
  "warnings": [],
  "runtime_ms": 42
}
```

`status` is `repaired`, `partial` or `failed`.

**`result.json`** (written by the checker)

```json
{
  "schemaVersion": "0.1",
  "caseId": "case_0007",
  "pass": false,
  "checks": [
    { "name": "watertight", "pass": true, "value": 0, "limit": 0 },
    { "name": "shellCount", "pass": true, "value": 1, "limit": 1 },
    { "name": "volume_m3", "pass": false, "value": 5400.0, "limit": 5454.0 }
  ]
}
```

`value` for `watertight` is the number of free edges. A case passes only if every check passes. The checker also writes `out/summary.json` with pass counts per fault type.

**Command line**

| Command | Exit 0 | Exit 1 | Exit 2 |
| --- | --- | --- | --- |
| `m2w repair <caseDir>` | Repaired | Repair failed | Bad input |
| `m2w check <caseDir>` | All checks pass | A check failed | Missing file |

## Folder layout and rules

Generated cases, the checker and this agreement are locked. Changing any of them needs the owner's approval and a new `schemaVersion`.

```
massing-to-wind/
  AGREEMENT.md          # this document, locked
  cases/                # generator-owned, locked
    case_0007/
      input.obj
      case.json
      expected.json     # checker only
  src/
    M2W.Geometry/       # shared mesh types (owner approves changes)
    M2W.Generator/      # test-case agent
    M2W.Checker/        # locked after Day 1
    M2W.Repair/         # solver agent
    M2W.Cli/            # m2w repair / m2w check
  tests/
  out/                  # solver and checker output, never committed
```

- The owner keeps hidden cases outside the agents' workspace and runs them at the end of each day.
- Generation is seeded: the same seed must produce identical files.
- Readers ignore unknown JSON fields. A missing required field is an input error (exit 2).
- `schemaVersion` changes from 0.1 to 0.2 for any breaking change to a file format.
- Faults to cover in v0.1: `overlap`, `stackedFloors`, `touchingWalls`, `smallGap`, `sliver`, `shortEdge`, `duplicateVertices`, `openSurface`, `flippedFaces`, `floatingBlock`, `sunkBlock`, `farCoordinates`.
