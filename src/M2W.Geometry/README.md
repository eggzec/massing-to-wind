# M2W.Geometry (shared)

Built in milestone M1. Everything here is deterministic and culture-invariant. Changing an existing public
signature needs the owner (see `CLAUDE.md`); adding new members is fine.

| File | What it is |
| --- | --- |
| `Vec3.cs` | `Vec3` (double precision) with `+ - * /`, `Dot`, `Cross`, `Length` |
| `Mesh.cs` | `Mesh` (shared `Vertices` + ordered `Objects`), `MeshObject` (name + faces), `Face` (0-based indices, value equality) |
| `ObjReader.cs` | Strict OBJ reader (AGREEMENT.md rules). Throws `ObjFormatException` with the line number |
| `ObjWriter.cs` | OBJ writer: 6 decimals, LF, no BOM, `-0` written as `0`. Byte-identical for identical meshes |
| `MeshMeasures.cs` | `BoundingBox`, `AreaVector`, `SignedVolume` (precise at map coordinates), `SurfaceArea`, `EdgeUseCounts` |
| `CaseDiscovery.cs` | `FindCaseDirs(root)`: every folder at any depth containing `case.json`, ordinal order |
| `Json/M2WJson.cs` | `Parse`/`ReadFile`/`Serialize`/`WriteFile` for every model; LF output; validation on read and write |
| `Models/` | `CaseSpec` (case.json), `ExpectedSpec` (expected.json), `RepairReport` (report.json), `CheckResult` (result.json), `RunSummary` (summary.json), `CaseFiles` (file names) |
| `Stages/Stages.cs` | Role entry points: `ICaseRepairer`, `ICaseChecker`, `ICaseGenerator` and their request/outcome types |

## Reading rules

- Unknown JSON fields are ignored. A missing required field, a wrong type, or a broken rule throws
  `ModelValidationException` (m2w turns that into exit 2). A missing file throws `FileNotFoundException`.
- `case.json`: every field is required except `tolerances`; any tolerance left out takes its AGREEMENT.md default.
- `schemaVersion` must be exactly `"0.1"`.
- `RepairOperation` and `CheckEntry` keep unmodelled fields in `Extra`, so you can add detail (e.g. `"detail"`)
  without a schema change.

## How a role plugs into `m2w`

Add **one** public, non-abstract class with a public parameterless constructor to your own project:

| Role | Project | Implements |
| --- | --- | --- |
| Solver | `src/M2W.Repair` | `ICaseRepairer` — gets `CaseSpec` + input `Mesh` only; returns `RepairReport` + repaired `Mesh` |
| Checker | `src/M2W.Checker` | `ICaseChecker` — gets case folder, `CaseSpec`, `ExpectedSpec`, path to `repaired.obj` |
| Generator | `src/M2W.Generator` | `ICaseGenerator` — gets the cases folder and seed; writes cases, returns the count |

`m2w` finds it by reflection (`M2W.Cli/StageSet.cs`); no change to `M2W.Cli` is needed. Until a class exists,
`repair` uses a stub that reports `failed`, and `check` / `generate` exit 2 with "not implemented yet".
`m2w` writes `repaired.obj`, `report.json` and `result.json` itself, after validating them, and deletes the
previous run's copies first so stale output is never graded.
