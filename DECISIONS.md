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

## D-001 · 2026-11-18 · owner · Pure C#, no external validators in v0.1
- Context: one-week build; the checker must close the loop without OpenFOAM or Python.
- Choice: all generation, repair and checking in C# / .NET 8.
- Alternatives: OpenFOAM `surfaceCheck`/`checkMesh`, Python trimesh as independent validators.
- Why: keeps the loop fast and the stack aligned with the target role.
- Revisit if: v0.2 adds external validation (roadmap step 2).
