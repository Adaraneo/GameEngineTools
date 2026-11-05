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

// Numbers are written the same way everywhere.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
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

// The engine reads its config and SourceFiles (and keeps world.db) under the working directory. Each run gets its own copy,
// so several monitors can run at the same time without touching each other's world.
var outputDir = Path.GetFullPath(options.OutputDir, AppContext.BaseDirectory);
var runDir = RunDirectory.Create();
Directory.SetCurrentDirectory(runDir);

var collector = new Collector(options.SampleEveryHours);
var stopwatch = Stopwatch.StartNew();
var progress = new Progress(options.Run.Days, stopwatch);

try
{
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
}
finally
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
    RunDirectory.TryDelete(runDir);
}

stopwatch.Stop();
progress.Finish();

var checks = Report.Evaluate(options.Thresholds, collector);
var markdown = Report.Build(options, collector, stopwatch.Elapsed, checks);

Directory.CreateDirectory(outputDir);
var stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}-p{Environment.ProcessId}";
var reportPath = Path.Combine(outputDir, $"report-{options.ProfileName}-{stamp}.md");
File.WriteAllText(reportPath, markdown);
File.WriteAllText(Path.Combine(outputDir, $"samples-{options.ProfileName}-{stamp}.csv"), Report.BuildCsv(collector));

Console.WriteLine();
Console.WriteLine(markdown);
Console.WriteLine($"Report written to {reportPath}");

return checks.Any(c => !c.Passed) ? 2 : 0;
