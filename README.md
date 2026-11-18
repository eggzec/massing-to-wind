# Massing-to-Wind

**Automatic repair of generative-design building massings into simulation-ready geometry, in pure C#.**

## The problem

Generative design tools produce hundreds of massing options per site, built to look right on screen: stacked
floor boxes, overlapping blocks, touching walls, tiny gaps, slivers, open or flipped surfaces, blocks floating
above the ground, large map coordinates. Wind (CFD) meshers need clean, closed, non-intersecting shells.
Geometry preparation is widely reported as one of the main bottlenecks of the CFD workflow, so pedestrian wind
comfort is usually checked late, when changing the massing is expensive.

## The approach

1. A seeded generator reproduces how generative tools build massings, then injects the faults they cause,
   and records the correct answer for each case.
2. A repair engine fixes the faults without seeing the answers.
3. An independent checker grades every repaired case: watertightness, orientation, topology, gaps, edge
   length, volume, footprint and height.

See `AGREEMENT.md` for the file formats and `MILESTONES.md` for the plan.

## Quick start

```bash
scripts/setup.sh          # creates M2W.sln and adds all projects (Windows: scripts/setup.ps1)
scripts/validate.sh       # build, test, run all cases, print summary
dotnet run --project src/M2W.Cli -- check cases/_example/case_0007
```

Requires the .NET 8 SDK.

## Results

_Filled in at milestone M5 from `out/summary.json`._

## Roadmap

1. **v0.1 (now):** pure C# repair engine with generated test cases and checker.
2. External validation: OpenFOAM `surfaceCheck`, `snappyHexMesh` and `checkMesh` on repaired output.
3. Rhino/Grasshopper component wrapping the engine.
4. Batch repair of many options on AWS (Amazon Web Services): one case per job.
5. Fast wind-comfort surrogate model trained on the repaired, simulated options.
