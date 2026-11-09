#!/usr/bin/env python3
"""Config coverage guard. Run it after adding a setting, a config type or a command-line option:

    python scripts/check-config.py            # static checks, then build + run ConfigCoverageTests
    python scripts/check-config.py --no-tests # static checks only

It checks three things:
  1. Every WorldMonitor command-line option (--x) has a key in appsettings.json that the code reads.
  2. Every config section the code binds (Characters:*, Economy, World:*) is in the table of ConfigCoverageTests.
  3. (with tests) Every setting of those config types is present in the live appsettings files, beyond the known baseline.
"""
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

# Command-line option -> where its default lives under the WorldMonitor section (a profile key unless marked section).
# Adding an option to MonitorOptions.cs without adding it here fails the check, which is the point.
MONITOR_OPTIONS = {
    "profile": "Profile",
    "days": "Days",
    "characters": "Characters",
    "tick": "TickStepMinutes",
    "substep": "SubstepMinutes",
    "couples": "Couples",
    "seasons": "Seasons",
    "seed": "Seed",
    "world-db": "WorldDb",
    "barter": "Barter",
    "lod": "CharacterLod",
    "planet": "Planet",
    "out": "OutputDir",
}

# Numeric constants in WorldMonitor that are not tunables, with the reason.
ALLOWED_CONSTANTS = {
    ("Progress.cs", "width"): "width of the console progress bar, cosmetic",
}

failures = []


def fail(message):
    failures.append(message)
    print(f"FAIL  {message}")


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def check_monitor():
    options = read("WorldMonitor/MonitorOptions.cs")
    settings_text = read("WorldMonitor/appsettings.json")
    json.loads(settings_text)

    cli = set(re.findall(r'overrides\.(?:GetValueOrDefault|TryGetValue)\("([a-z-]+)"', options))
    cli |= set(re.findall(r'IntOverride\(overrides, "([a-z-]+)"', options))

    for option in sorted(cli):
        key = MONITOR_OPTIONS.get(option)
        if key is None:
            fail(f"WorldMonitor --{option} has no config key: add it to MonitorOptions.cs, appsettings.json and MONITOR_OPTIONS here.")
            continue
        if not re.search(rf'\b{key}\b', options):
            fail(f"WorldMonitor --{option}: MonitorOptions.cs never reads the config key '{key}'.")
        if f'"{key}"' not in settings_text:
            fail(f"WorldMonitor --{option}: appsettings.json shows no '{key}' (put its default there so it is visible).")

    for option in MONITOR_OPTIONS:
        if option not in cli:
            fail(f"MONITOR_OPTIONS lists --{option} but MonitorOptions.cs no longer has it: drop it here.")

    # Settings that used to be fixed in the code must stay in the config.
    for key in ("HungerLevel", "FarmPeriodDays", "ChronicStressLevel", "SampleEveryHours"):
        if f'"{key}"' not in settings_text:
            fail(f"WorldMonitor appsettings.json lost '{key}'.")

    # A new numeric constant in the monitor is a tunable that probably belongs in the config.
    for source in ("Collector.cs", "Report.cs", "Program.cs", "Progress.cs"):
        for number, line in enumerate(read(f"WorldMonitor/{source}").splitlines(), 1):
            found = re.search(r'\bconst\s+(?:double|int)\s+(\w+)\s*=', line)
            if found and (source, found.group(1)) not in ALLOWED_CONSTANTS:
                fail(f"WorldMonitor/{source}:{number} has a numeric constant ({line.strip()}): make it a setting or justify it in this script.")


def check_sections():
    table = read("EngineTests/ConfigCoverageTests.cs")
    table_sections = set(re.findall(r'\("(?:Characters|Economy|World)", "([A-Za-z:]+)"', table))
    bound = set()
    for folder in ("GameEngineTools", "GameSandbox", "WorldObserver", "WorldMonitor"):
        for path in (ROOT / folder).rglob("*.cs"):
            if any(part in ("bin", "obj") for part in path.parts):
                continue
            text = path.read_text(encoding="utf-8-sig")
            bound |= set(re.findall(r'(?:BindConfiguration|GetSection)\("((?:Characters|World)(?::[A-Za-z]+)+|Economy)"\)', text))

    for section in sorted(bound):
        covered = any(section == t or section.startswith(t + ":") for t in table_sections)
        if not covered:
            fail(f"Config section '{section}' is bound in the code but missing from the table in EngineTests/ConfigCoverageTests.cs.")


def run_tests():
    vstest = pathlib.Path("C:/Program Files/Microsoft Visual Studio/18/Community/Common7/IDE/Extensions/TestPlatform/vstest.console.exe")
    build = subprocess.run(["powershell", "-ExecutionPolicy", "Bypass", "-File", str(ROOT / "build.ps1"),
                            "-Project", "EngineTests/EngineTests.csproj", "-NoTest"], cwd=ROOT, capture_output=True)
    if build.returncode != 0:
        fail("EngineTests did not build.")
        return

    trx = ROOT / "EngineTests" / "TestResults" / "config-coverage.trx"
    trx.unlink(missing_ok=True)
    subprocess.run([str(vstest), "EngineTests/bin/Debug/net8.0/EngineTests.dll", "--TestCaseFilter:FullyQualifiedName~ConfigCoverage",
                    f"--logger:trx;LogFileName=config-coverage.trx", f"--ResultsDirectory:{trx.parent}"], cwd=ROOT, capture_output=True)
    if not trx.exists():
        fail("vstest produced no result file.")
        return

    text = trx.read_text(encoding="utf-8-sig")
    counters = re.search(r'<Counters total="(\d+)" executed="(\d+)" passed="(\d+)"', text)
    total, passed = int(counters.group(2)), int(counters.group(3))
    print(f"ConfigCoverageTests: {passed} of {total} passed")
    for message in re.findall(r'<Message>(.*?)</Message>', text, re.S):
        print(message.strip())
    if total == 0 or passed != total:
        fail("ConfigCoverageTests failed: add the missing keys to the JSON (see the list above), or run with UPDATE_CONFIG_BASELINE=1 to accept them.")


check_monitor()
check_sections()
if "--no-tests" not in sys.argv:
    run_tests()

if failures:
    print(f"\n{len(failures)} problem(s).")
    sys.exit(1)
print("Config coverage OK.")
