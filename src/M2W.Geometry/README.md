# M2W.Geometry (shared)

Built in milestone M1. Expected contents (names are suggestions; the agent decides):

- `Vec3` (double precision), `Mesh` (vertices, faces as index lists, named objects)
- `ObjReader` / `ObjWriter` following `AGREEMENT.md` (6 decimal places, LF, 1-based indices)
- JSON models: `CaseSpec` (case.json), `ExpectedSpec` (expected.json), `RepairReport` (report.json), `CheckResult` (result.json), using `System.Text.Json`
- Basic measures every role needs: signed volume, edge list, bounding box

Everything here must be deterministic and culture-invariant (use `CultureInfo.InvariantCulture`).
