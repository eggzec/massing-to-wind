// m2w command line. Exit codes follow AGREEMENT.md: 0 = success, 1 = failed, 2 = bad input / not implemented.
// Agents replace the stubs below with calls into their own projects during M1.

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          m2w generate <casesDir> [--seed N]     build test cases (generator)
          m2w repair   <caseDir>  [--out DIR]    repair one case (solver)
          m2w check    <caseDir>  [--out DIR]    grade one case (checker)
          m2w run-all  <casesDir> [--out DIR]    repair + check every case, write out/summary.json
        """);
    return 2;
}

static int NotImplemented(string command)
{
    Console.Error.WriteLine($"m2w {command}: not implemented yet (milestone M1).");
    return 2;
}

if (args.Length < 2) return Usage();

return args[0] switch
{
    "generate" => NotImplemented("generate"),
    "repair" => NotImplemented("repair"),
    "check" => NotImplemented("check"),
    "run-all" => NotImplemented("run-all"),
    _ => Usage(),
};
