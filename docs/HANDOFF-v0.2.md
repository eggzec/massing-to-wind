# Handoff for v0.2

For any agent starting v0.2 work. Written at the close of v0.1 (2026-11-22, tag `v0.1.0`). One page on purpose:
it points to the long files instead of repeating them.

## Read first, in this order

| # | File | Why | Size |
| --- | --- | --- | --- |
| 1 | `CLAUDE.md` | Rules for every agent: roles, what needs the owner, hard rules | 55 lines |
| 2 | `AGREEMENT.md` | File formats, faults, tolerances, grading. Locked: changes go through the owner | 190 lines |
| 3 | this file | State, how work is done here, v0.2 backlog, pitfalls | |
| 4 | `MILESTONES.md`, v0.2 part | What to build, with acceptance criteria and gates | |
| 5 | `.claude/agents/<role>.md` | Your role: what you own, may read, may not read | ~40 lines |
| 6 | `KNOWN_LIMITATIONS.md` | What v0.1 does not do, each with its decision | 130 lines |
| 7 | `DECISIONS.md` | Look up a D-number when something cites it; do not read it end to end | 820+ lines |
| 8 | `PROGRESS.md` | Read "Waiting for owner", the status table and the newest entries only | 1100+ lines |

Your brief for a milestone comes from the owner. On the owner's machine the briefs are in `../agent-briefs/`, outside
the repository. Each brief gives your branch, worktree and the D-numbers you may use.

## State at `v0.1.0`

- **Results:**
  - visible cases 311/311;
  - unseen seeds 777, 2024 and 99991: 930/930;
  - the owner's private hidden set: 310/310, worst tolerance use volume 6.6 %, footprint 11.7 %, height 6.2 %;
  - owner benchmarks pass: the exact cases with zero error, and a real district comes out valid in about 2 s.

  Details are in the README "Validation" section.
- **Solver** 0.7.1: a 2.5D height-field pipeline in 9 steps (README "How it works", each step with its D-number).
  Output order depends on geometry only (D-047).
- **CI** (GitHub Actions) is green. Each push runs validate and the determinism check on Linux and Windows, and
  checks that all 311 `repaired.obj` are byte-identical across the two (D-045, D-050, D-051).
- **Locks** (`LOCKS`, tag `lock-v0.1` at `5c5c7a6`): `AGREEMENT.md`, `LOCKS`, `cases/`, `src/M2W.Checker/`,
  `tests/M2W.Checker.Tests/`. They stay locked until the owner moves the tag for a v0.2 milestone.

## Repository map

| Path | Owner | What |
| --- | --- | --- |
| `src/M2W.Geometry/` | shared | Mesh types, OBJ and JSON input/output. Changing a public signature needs the owner |
| `src/M2W.Cli/` | shared | `m2w generate / repair / check / run-all / render`; packed as the .NET tool `MassingToWind.Tool` (D-056) |
| `src/M2W.Generator/`, `cases/` | generator | Seeded cases with injected faults and exact expected answers |
| `src/M2W.Checker/` | checker | Grades `repaired.obj` against `expected.json` and the validity rules (locked) |
| `src/M2W.Repair/` | solver | The repair engine; `README.md` is also the solver's |
| `scripts/` | shared | `setup`, `validate`, `determinism`, `install-tool` (`.sh` and `.ps1`), `check-locks.sh` |
| `.github/workflows/ci.yml` | checker (M5) | CI: Linux and Windows jobs, plus the cross-OS hash comparison |
| `demo/` | owner | Demo runner and ParaView scene (D-053), not part of the build or CI |
| `docs/images/` | generator | README before/after SVGs and `showcase.json` |
| `global.json` | owner | Pins the .NET 8 SDK (D-051) |

## How work is done here

1. **One branch and one worktree per role and milestone,** e.g. `solve/v1` in `../m2w-solve`. Branch from `main`.
   Never edit another role's folder. Never merge: the owner merges.
2. **Before you finish:** run `scripts/validate.sh` (Git Bash or Linux) or `scripts/validate.ps1` (PowerShell). It
   must end `VALIDATE: GREEN` with `locks: OK`. For output changes, also run `scripts/determinism.sh` or `.ps1`.
3. **Write it down:**
   - a PROGRESS entry: date, role, what changed, counts from `out/summary.json`, next steps, blockers;
   - a D-entry for each choice someone could question, using only your assigned numbers;
   - questions for the owner go under "Waiting for owner". Then stop that line of work and do something else.
4. **Commit** on your branch, e.g. `solve: piecewise planar roofs (sloped 27/30)`.
5. **Owner's merge practice:**
   - merge `main` into the branch;
   - resolve PROGRESS and DECISIONS conflicts by keeping both sides (decisions in number order, log entries newest
     first);
   - fast-forward `main`, or use a merge commit;
   - validate, push, and watch CI with `gh run watch`.
6. **Output guard when the solver changes:** compare old and new `repaired.obj` for all visible cases and the
   unseen seeds. Use bytes, or, if order changed, the set of shells and their oriented triangles. Every changed
   file needs a reason in the PROGRESS entry.

## v0.2 backlog

The milestones are V0–V5 in `MILESTONES.md`. Each item below needs owner decisions first, made at the V0 gate.

| Item | Source | Roles | Locked files it touches | Milestone |
| --- | --- | --- | --- | --- |
| Validity-only grading for real data (no `expected.json`) | README roadmap | checker, CLI | `src/M2W.Checker/`, `AGREEMENT.md` (result fields) | V1 |
| Minimum-angle check | D-025, KNOWN_LIMITATIONS "Thin slivers" | checker | `src/M2W.Checker/`, `tests/M2W.Checker.Tests/`, `AGREEMENT.md` | V2 |
| Refine thin output triangles | D-035, D-031 (roofs over 150 vertices) | solver | none | V2 |
| Sloped roofs as piecewise planar height field | D-029, D-015 | generator, solver, checker | `cases/`, `AGREEMENT.md`, checker if volume or height grading changes | V3 |
| Overhangs, canopies, bridges: 3D union | D-029, D-042, D-015 | all three | `cases/`, `AGREEMENT.md`, `src/M2W.Checker/` | V4 |
| Order-independent weld choice and roof-triangulation ties | D-047, Q-S2.2 | solver | none (changes output: run the guard) | V0 carry-over or V2 |
| Gaps inside one part (inlets, narrow courtyards) | D-032 | solver, maybe `AGREEMENT.md` | `AGREEMENT.md` if the rule changes | owner decides |
| Jog rule: the least-change rule can move the wrong wall | D-034 (worst 44.5 % of footprint tolerance, unseen) | solver | none | owner decides |
| xUnit2013 warning; `Enumerable.Reverse` for C# 14 | CI annotation, D-051 | solver, shared | none | V0 |
| Later: OpenFOAM validation, Rhino/Grasshopper, AWS batch, surrogate | README roadmap, D-001 | | | after v0.2 |

## Pitfalls already found (do not rediscover them)

- **SDK:** keep `global.json`. Without it, CI uses the runners' .NET 10 SDK (C# 14), where `int[].Reverse()` binds
  to the in-place `void` span method, and the build breaks only on CI (D-051).
- **Git Bash on Windows:** `validate.ps1` finds Git Bash by walking up from `git.exe`, and never uses plain `bash`,
  which can be WSL's (D-050). Run validate from a normal PowerShell or from Git Bash; both work.
- **Worktrees:** `.git` is a file in a worktree. `scripts/setup.sh` used to re-initialise git in a worktree and
  commit everything in it; that is fixed for `v0.1.0`. Other scripts must not test `-d .git` either; use
  `git rev-parse --is-inside-work-tree`.
- **Every worktree has its own `out/`.** Compare outputs only from the same commit.
- **The solver never reads answers,** including indirectly:
  - `docs/images/showcase.json` holds expected volumes;
  - the showcase writer (`M2W_WRITE_SHOWCASE=1 dotnet test tests/M2W.Cli.Tests --filter Showcase`) reads
    `expected.json`.

  Only the owner or the generator runs it, after the solver's output changes.
- **No static switches in the repair.** Tests run in parallel; pass options down instead (D-048).
- **`report.json` is not byte-identical** across runs (`runtime_ms`, D-028). The determinism scripts zero it before
  comparing; compare `repaired.obj` and `result.json` as bytes.
- **D-numbers collide** when roles work in parallel. Use only the numbers in your brief; the owner renumbers at
  merge if needed.
- **ParaView** ignores OBJ `o` lines; the demo reads the OBJ itself (`demo/paraview_scene.py`).
- **Windows PowerShell 5.1:**
  - keep `.ps1` files ASCII (no BOM means the file is read as ANSI);
  - `$input` is a reserved variable;
  - `2>&1` on native commands turns stderr lines into errors.

## Useful commands

```bash
dotnet run --project src/M2W.Cli -- run-all cases --out out                  # repair and grade every visible case
dotnet run --project src/M2W.Cli -- generate ../unseen-777 --seed 777        # unseen cases, outside the repo
dotnet run --project src/M2W.Cli -- render out/case_0191/repaired.obj --out x.svg --frame cases/case_0191/input.obj
scripts/determinism.sh                                                       # two runs, byte comparison, out/hashes.txt
powershell -ExecutionPolicy Bypass -File demo\run-demo.ps1 -Case case_0191   # the demo (ParaView for step 4)
gh run list --workflow ci.yml                                                # CI status (GitHub CLI)
```
