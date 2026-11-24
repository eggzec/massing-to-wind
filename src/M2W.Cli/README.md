# m2w (Massing-to-Wind)

Turns rough building massings (OBJ) into clean, closed geometry for wind (CFD) simulation.

```
dotnet tool install -g MassingToWind.Tool
m2w repair tower.obj
```

This writes `out/tower/repaired.obj` and `out/tower/report.json`, which lists every change made. The model must be in
metres with Z up.

```
m2w repair site.obj --out results --origin 530000,180000,0 --min-gap 0.5
m2w render out/tower/repaired.obj --out tower.svg
m2w repair --help
```

Documentation, limitations and source: https://github.com/eggzec/massing-to-wind
