namespace M2W.Cli;

/// <summary>Help and usage texts.</summary>
internal static class CliHelp
{
    private const string General = """
        m2w - turn rough building massings into clean, closed geometry for wind simulation

        usage: m2w <command> [options]

        commands:
          repair     repair one model (OBJ file or case folder)
          render     draw an OBJ as an SVG picture

        developer commands (test cases for the repair engine):
          generate   write test cases with known answers
          check      grade one repaired case against its answer
          run-all    repair and grade every case in a folder

        m2w <command> --help shows the options of a command; m2w --version shows the version.
        Exit codes: 0 done, 1 failed (or only partly repaired), 2 bad input or arguments.
        """;

    private const string Repair = """
        usage: m2w repair <model.obj> [--out DIR] [--origin X,Y,Z] [tolerances]
               m2w repair <caseDir>   [--out DIR]

        Repairs one model and writes repaired.obj and report.json to DIR/<name>/. The model must be in metres with
        Z up. For model.obj, <name> is the file name without .obj. A case folder holds a case.json, which gives the
        name, origin, tolerances and input file.

          --out DIR          output folder (default: out)
          --origin X,Y,Z     point subtracted from the model, for map coordinates (default: 0,0,0)

        tolerances, only with model.obj:
          --min-gap M        close gaps between blocks narrower than this (default: 1.0 m)
          --min-edge M       remove edges, jogs and roof steps smaller than this (default: 0.25 m)
          --ground-snap M    move blocks this close to the ground onto it (default: 0.5 m)
          --weld M           merge vertices closer than this (default: 0.001 m)
          --min-angle DEG    report triangles with a smaller angle (default: 5)

        Exit codes: 0 repaired, 1 partial or failed (see report.json), 2 bad input.
        """;

    private const string Render = """
        usage: m2w render <model.obj> --out <picture.svg> [--frame <other.obj>] [--title <text>]

        Draws an axonometric SVG picture of an OBJ file.

          --out FILE         the SVG to write (required)
          --frame OBJ        fit the view to both files, so a before/after pair shares scale and position
          --title TEXT       title (default: the file name)
        """;

    private const string Generate = """
        usage: m2w generate <casesDir> [--seed N]

        Writes test cases with known answers to casesDir.

          --seed N           random seed (default: 1)
        """;

    private const string Check = """
        usage: m2w check <caseDir> [--out DIR]

        Grades DIR/<caseId>/repaired.obj against the case's expected.json and writes result.json next to it.

          --out DIR          the folder given to m2w repair (default: out)

        Exit codes: 0 all checks pass, 1 a check failed, 2 missing file.
        """;

    private const string RunAll = """
        usage: m2w run-all <casesDir> [--out DIR]

        Repairs and grades every folder under casesDir that contains case.json, and writes DIR/summary.json.

          --out DIR          output folder (default: out)
        """;

    /// <summary>The help of a command, or the general help if the command is null or unknown.</summary>
    public static string For(string? command) => command switch
    {
        "repair" => Repair,
        "render" => Render,
        "generate" => Generate,
        "check" => Check,
        "run-all" => RunAll,
        _ => General,
    };

    public static bool IsCommand(string s) => s is "repair" or "render" or "generate" or "check" or "run-all";

    /// <summary>The usage line(s) only: the first paragraph of the help.</summary>
    public static string Usage(string command) => string.Join('\n', For(command).Split('\n').TakeWhile(l => l.Trim().Length > 0));

    public static string PathName(string command) => command switch
    {
        "repair" => "<model.obj> or <caseDir>",
        "check" => "<caseDir>",
        _ => "<casesDir>",
    };
}
