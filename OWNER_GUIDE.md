# Owner guide — how to oversee this project in about 30 minutes a day

You set direction, run gates and keep the loop honest. The agents make the technical decisions.

## One-time setup (Day 1, ~20 min)

1. Install the .NET 8 SDK and Git. Unzip this folder.
2. Run `scripts/setup.sh` (Windows: `scripts/setup.ps1`). It creates the solution, the first commit, the
   `lock-v0.1` tag and three branches: `gen/m2`, `check/m2`, `solve/m3`.
3. Create a GitHub repository and push `main`, the branches and the tag (`git push --all && git push --tags`).
4. Make a private `hidden/` folder **outside** the repository for your own test cases (see "Hidden cases").

## How to run the agents

- One agent session per role, each on its own branch (`git switch gen/m2`, and so on).
- Start each session with: *"You are the <role> agent. Read CLAUDE.md, your brief in .claude/agents/<role>.md,
  MILESTONES.md and the latest PROGRESS.md entry. Continue the current milestone."*
- Days 1–2: run **generator** and **checker** (in parallel if you can). The solver waits until M2 is approved.
- Days 2–6: run the **solver**. Use the generator again only if you approve new cases.
- Let a session run until it hits its milestone, a blocker, or about 2 hours. Then review.

## Your daily routine (~30 min)

1. **Read `PROGRESS.md`:** the "Waiting for owner" list first, then today's entries. Answer the questions.
2. **Look at the numbers:** `out/summary.json` pass rate per fault. Is it moving up? A flat rate for a whole
   day means the agent is stuck; ask it to write down three alternative approaches in `DECISIONS.md`.
3. **Skim `DECISIONS.md`:** you don't have to agree with every choice, only to see that each one was reasoned.
4. **Check the diff of locked paths:** `scripts/check-locks.sh` must say OK. A failure means an agent touched
   something it shouldn't have; reject that change.
5. **Run your hidden cases** (below). A big gap between visible and hidden pass rates means overfitting.
6. **Merge** the role branch into `main` if validate is green and you are happy.

## Gates (from MILESTONES.md)

| Gate | When | You check | Then |
| --- | --- | --- | --- |
| M1 | End of Day 1 | Example case reads, `validate` runs | Keep `lock-v0.1` tag |
| M2 | End of Day 2 | 5 random cases by eye; clean controls pass the checker | Uncomment `cases/`, checker lines in `LOCKS`, commit, `git tag -f lock-v0.1` |
| M3 | End of Day 4 | ≥ 90 % on the 8 core faults; hidden cases | Continue or redirect |
| M4 | End of Day 6 | ≥ 80 % overall; every failure explained | Feature freeze |
| M5 | Day 7 | README, images, video | Publish |

## Hidden cases

After the M2 gate, run the generator yourself with a new private seed into your private folder:
`dotnet run --project src/M2W.Cli -- generate <private folder> --seed <private seed>`. Then each evening:
`dotnet run --project src/M2W.Cli -- run-all <private folder> --out <private output folder>`. Never put these in the
repository, and never write the seed, the folder or any case detail anywhere an agent can read it (repo, PROGRESS,
prompts). Keep the output folder private too: `result.json` contains the expected answers. Give agents totals only.
Once case details have been shared, regenerate the set with a new seed.

## When to step in

- A "Waiting for owner" question older than one session.
- The same failure reported in two sessions in a row.
- Any change to `AGREEMENT.md`, a tolerance, or a test being weakened.
- Scope creep: work on anything not in the current milestone.
