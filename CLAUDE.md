# Working notes for Claude

## Before tackling a task: check what tooling is actually available

Do NOT assume the environment is missing a tool just because it isn't
pre-installed. Up front, consider whether something can be installed or set up
to do the job properly — and either do it or raise it with the user — *before*
working around the limitation or proceeding blind.

Concrete example from this repo: the environment had no `.NET SDK`, so an
earlier session reviewed all code changes by hand with no compile check. In
fact the SDK could be installed in minutes and a real build run. Always probe
for this kind of option first.

Checklist at the start of a non-trivial task:
- Can I install the required SDK/toolchain? (e.g. `dotnet`, package managers)
- Is there a way to actually run/test/compile, even with stubs or mocks?
- If a capability seems missing, verify before assuming — and tell the user
  what's possible rather than silently working around it.

## Building this mod without Stardew Valley installed

The .NET 8 SDK can be installed via the official script:
```
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 8.0 --install-dir /home/user/.dotnet
```

The game assemblies aren't present, so stub assemblies were created under
`/tmp/stubs` (MonoGame.Framework, Stardew Valley, StardewModdingAPI, xTile,
0Harmony, SMAPI.Toolkit.CoreInterfaces, StardewValley.GameData) and placed in a
fake game folder `/tmp/stubs/gamefolder`. The SMAPI build config is pointed at
it via `~/stardewvalley.targets` (note: `$HOME` is `/root`).

Build with:
```
export PATH="/home/user/.dotnet:$PATH"
dotnet build -p:EnableModDeploy=false -p:EnableModZip=false
```

NOTE: stubs only verify compilation (signatures), not runtime behavior. If stub
signatures drift from the real SDV/SMAPI APIs, a clean build is not a guarantee
of in-game correctness.
