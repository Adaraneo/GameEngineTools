// Program.cs
// Copyright (c) 50PSoftware

using System.Diagnostics;
using GameEngineTools;
using GameEngineTools.Dialogue.Hosting;
using GameEngineTools.World.Simulation;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using WorldMonitor;
using WorldObserver.Simulation;

// The engine reads its config and SourceFiles relative to the working directory; numbers are written the same way everywhere.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

MonitorOptions options;
try
{
    options = MonitorOptions.Load(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

Console.WriteLine($"WorldMonitor: profile {options.ProfileName} — {options.Run.Characters} characters, {options.Run.Days} days, tick {options.Run.TickStepMinutes} min.");

// A fresh world database each run: the seed is loaded again and nothing accumulates between runs.
var worldDir = Path.Combine(Directory.GetCurrentDirectory(), "SourceFiles", "World");
Directory.CreateDirectory(worldDir);
foreach (var file in new[] { "world.db", "world.db-wal", "world.db-shm" })
{
    try { File.Delete(Path.Combine(worldDir, file)); } catch (IOException) { /* in use — leave it */ }
}

var collector = new Collector(options.SampleEveryHours);
var stopwatch = Stopwatch.StartNew();
var progress = new Progress(options.Run.Days, stopwatch);

await using (var runtime = await GameEngineToolsRuntime.StartAsync(
    consoleLogs: false,
    writeJsonLines: false,
    writeTextLogs: false,
    configureServices: static s => s.AddLexicalAcquisition()))
{
    var world = WorldBootstrap.Build(runtime, Math.Max(1, options.Run.Characters));

    var sceneOptions = new SimulationSceneOptions
    {
        Characters = world.Characters,
        LocationService = world.Locations,
        SimulationDays = options.Run.Days,
        TickStep = WTimeSpan.FromMinutes(Math.Max(1, options.Run.TickStepMinutes)),
        InternalSubstep = WTimeSpan.FromMinutes(Math.Max(1, options.Run.SubstepMinutes)),
        DefaultCharacterLod = GameEngineTools.Characters.Hosting.CognitiveResolutionLevel.Nearby,
        ObjectSnapshotCache = world.ObjectCache,
        WriteBuffer = world.WriteBuffer,
        RespawnScheduler = world.Respawn,
        OnTick = (now, characters) =>
        {
            world.Orchestrator.OnTick(now, characters);
            collector.Record(now, characters);
            progress.Update(collector.WorldDays, collector.Samples.Count > 0 ? collector.Samples[^1].Stress.Average() : null, collector.Ticks);
        },
    };

    new SimulationScene(world.Clock, sceneOptions, world.Lod).RunAsync().GetAwaiter().GetResult();
}

stopwatch.Stop();
progress.Finish();

var checks = Report.Evaluate(options.Thresholds, collector);
var markdown = Report.Build(options, collector, stopwatch.Elapsed, checks);

var outputDir = Path.GetFullPath(options.OutputDir);
Directory.CreateDirectory(outputDir);
var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
var reportPath = Path.Combine(outputDir, $"report-{options.ProfileName}-{stamp}.md");
File.WriteAllText(reportPath, markdown);
File.WriteAllText(Path.Combine(outputDir, $"samples-{options.ProfileName}-{stamp}.csv"), Report.BuildCsv(collector));

Console.WriteLine();
Console.WriteLine(markdown);
Console.WriteLine($"Report written to {reportPath}");

return checks.Any(c => !c.Passed) ? 2 : 0;
