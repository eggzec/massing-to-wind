# CLAUDE.md — rules for every agent in this repository

## What this project is

Massing-to-Wind: a pure C# (.NET 8) engine that repairs building massing geometry from generative design tools
so it is ready for wind simulation. Read `AGREEMENT.md` before doing anything. It is the contract between agents.

## Roles

There are three agent roles. Each has a brief in `.claude/agents/`:

| Role | Brief | Owns |
| --- | --- | --- |
| Generator | `.claude/agents/generator.md` | `src/M2W.Generator/`, `cases/`, `tests/M2W.Generator.Tests/` |
| Checker | `.claude/agents/checker.md` | `src/M2W.Checker/`, `tests/M2W.Checker.Tests/` |
| Solver | `.claude/agents/solver.md` | `src/M2W.Repair/`, `tests/M2W.Repair.Tests/` |

`src/M2W.Geometry/` (shared mesh types, OBJ and JSON input/output) and `src/M2W.Cli/` are shared.
An agent may add to them. Changing an existing public signature there is an escalation (see below).

## You decide (no need to ask)

- Algorithms, data structures, internal class design, file organisation inside your own project.
- Pure managed C# NuGet packages with a permissive licence (MIT, Apache-2.0, BSD). Record each one in `DECISIONS.md`.
- Test design, number of cases, random seeds.
- Order of work inside the current milestone in `MILESTONES.md`.

## You must stop and escalate to the owner

Write the question at the top of `PROGRESS.md` under `## Waiting for owner`, then stop that line of work and continue with something else.

- Any change to `AGREEMENT.md`, a file format, a tolerance default or a JSON field.
- Any change to a path listed in `LOCKS` (once locked).
- Changing an existing public signature in `M2W.Geometry` or `M2W.Cli`.
- Native (non-C#) dependencies, or any tool outside .NET.
- Deleting or weakening a test, or raising a tolerance to make a test pass.

## Hard rules

- The solver never reads `expected.json` or generator/checker source code. It reads only `input.obj` and `case.json`.
- Never edit another role's folder.
- Never fake a result: no hard-coded case IDs, no special-casing a test, no catching and hiding failures.
- Every change ends with `scripts/validate.sh` (or `scripts/validate.ps1` on Windows) run, and its result written in `PROGRESS.md`.
- Deterministic output: same input and seed give byte-identical files.

## End of every session

1. Run `scripts/validate.sh`.
2. Add an entry to `PROGRESS.md`: date, role, what changed, pass counts from `out/summary.json`, what is next, any blockers.
3. Record any design choice that someone could reasonably question in `DECISIONS.md` (one entry: context, choice, alternatives, why).
4. Commit on your role branch (`gen/…`, `check/…`, `solve/…`) with a message like `solve: close gaps below minGap (smallGap 18/20 pass)`.

## Definition of done for a milestone

All acceptance criteria in `MILESTONES.md` for that milestone are met, `validate` is green, and the PROGRESS entry says so.
