# Probe Roulette

It seems the Nomai like to play pranks.

Adds a configurable chance for the orbital probe cannon to either fire directly at you or your ship starting at loop #7.

## Install

The easiest way is through [Outer Wilds Mod Manager](https://outerwildsmods.com/mod-manager/):

1. Open the manager and install OWML if prompted.
2. Search for **Probe Roulette** in **Get Mods** and install it. If it isn't listed yet, download the mod ZIP from the [latest release](https://github.com/depthbomb/OuterWildsProbeRoulette/releases/latest) and use the manager's option to install from a ZIP file.
3. Launch the game through the manager.

For a manual install on Windows, close the game and extract the mod ZIP into:

```text
%APPDATA%\OuterWildsModManager\OWML\Mods\Depthbomb.ProbeRoulette
```

Make sure `manifest.json` sits directly in that folder, then launch through the mod manager.

## Settings

You can change the launch chance, first eligible loop, target, death type, and player-hit radius in the mod settings. The chance field shows the current percentage; select it to enter a number from 0 to 100 (decimals work too). For testing, set the chance to 100% and pick Player or Ship. A bigger hit radius makes near misses count, too.

This is a beta. Other mods and unexpected movement can throw off the aim. If something looks wrong, turn on **Log closest approach** and include the `[Probe Roulette]` log lines when reporting it.

## Building

With the .NET 10 SDK, Outer Wilds, and OWML installed:

```powershell
.\Install.ps1
```

That builds, runs the checks, and installs the mod. Add `-PackageOnly` to just make the release packages. Custom install locations can be passed with `-GamePath` and `-OwmlPath`.
