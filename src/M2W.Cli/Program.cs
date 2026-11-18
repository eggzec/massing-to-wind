// m2w command line. Exit codes follow AGREEMENT.md: 0 = success, 1 = failed, 2 = bad input / missing file /
// not implemented yet. All logic lives in CliApp so it can be tested in-process. Role code plugs in through the
// stage interfaces in M2W.Geometry.Stages (see StageSet), not by editing this file.

return M2W.Cli.CliApp.RunConsole(args);
