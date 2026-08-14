// Reproduces PKHeX's plugin loading process and prints real load errors.
// Usage: DiagnosticPluginLoad <pkhexAppDir> <pluginDir>

using System.Reflection;
using System.Runtime.InteropServices;

var appDir = args.Length > 0 ? args[0] : @"C:\codes\PKHeX";
var pluginDir = args.Length > 1 ? args[1] : @"C:\codes\PKHeX\plugins";

// --- Part 1: metadata-only signature comparison ---
var desktopDir = Path.GetDirectoryName(typeof(System.Windows.Forms.Control).Assembly.Location)!;
var resolverDirs = new[] { appDir, pluginDir, RuntimeEnvironment.GetRuntimeDirectory(), desktopDir };
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(resolverDirs.SelectMany(Directory.EnumerateFiles).Where(f => f.EndsWith(".dll")).Select(Path.GetFullPath)));

var coreMeta = mlc.LoadFromAssemblyPath(Path.Combine(appDir, "PKHeX.Core.dll"));
var pluginMeta = mlc.LoadFromAssemblyPath(Path.Combine(pluginDir, "AutoModPlugins.dll"));

var islot = coreMeta.GetType("PKHeX.Core.ISlotViewer`1")!;
var ifaceMethod = islot.GetMethods().First(m => m.Name == "ApplyNewFilter");
Console.WriteLine($"PKHeX.Core {coreMeta.GetName().Version} interface member:");
Console.WriteLine($"  {ifaceMethod}");
foreach (var p in ifaceMethod.GetParameters())
    Console.WriteLine($"    param {p.Name}: {p.ParameterType.FullName} @ {p.ParameterType.Assembly.GetName().Name} v{p.ParameterType.Assembly.GetName().Version}");

var live = pluginMeta.GetType("AutoModPlugins.LiveHeXUI")!;
Console.WriteLine("LiveHeXUI ApplyNewFilter overloads:");
foreach (var m in live.GetMethods().Where(m => m.Name == "ApplyNewFilter"))
{
    Console.WriteLine($"  {m}");
    foreach (var p in m.GetParameters())
        Console.WriteLine($"    param {p.Name}: {p.ParameterType.FullName} @ {p.ParameterType.Assembly.GetName().Name} v{p.ParameterType.Assembly.GetName().Version}");
}

Console.WriteLine();
Console.WriteLine("LiveHeXUI PKHeX-related methods:");
foreach (var m in live.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name.Contains("Slot") || m.Name.Contains("Filter"))
    )
    Console.WriteLine($"  {m}");
Console.WriteLine("LiveHeXUI interfaces:");
foreach (var i in live.GetInterfaces())
    Console.WriteLine($"  {i.FullName} @ {i.Assembly.GetName().Name} v{i.Assembly.GetName().Version}");
Console.WriteLine();

// --- Part 2: runtime load like PKHeX does ---

Console.WriteLine($"App dir    : {appDir}");
Console.WriteLine($"Plugin dir : {pluginDir}");

var corePath = Path.Combine(appDir, "PKHeX.Core.dll");
var core = Assembly.LoadFrom(corePath);
Console.WriteLine($"Loaded PKHeX.Core {core.GetName().Version}");

var pluginPath = Directory.EnumerateFiles(pluginDir, "*.dll", SearchOption.AllDirectories).ToList();
foreach (var file in pluginPath)
{
    Console.WriteLine();
    Console.WriteLine($"=== {file} ===");
    Assembly asm;
    try { asm = Assembly.LoadFrom(file); }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED to load assembly: {ex.Message}");
        continue;
    }

    var loaderType = asm.GetType("Costura.AssemblyLoader", false);
    loaderType?.GetMethod("Attach", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, []);

    // Inspect the known problem type directly (wrap in try: GetType can throw TypeLoadException)
    try
    {
        var liveType = asm.GetType("AutoModPlugins.LiveHeXUI", throwOnError: false);
        Console.WriteLine(liveType is not null
            ? "LiveHeXUI runtime type load OK"
            : "LiveHeXUI type not found in assembly!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"LiveHeXUI type load FAILED: {ex.GetType().Name}: {ex.Message}");
    }

    Type[] types;
    try
    {
        types = asm.GetExportedTypes();
        Console.WriteLine($"GetExportedTypes OK: {types.Length} exported types");
    }
    catch (ReflectionTypeLoadException ex)
    {
        types = ex.Types.OfType<Type>().ToArray();
        Console.WriteLine($"ReflectionTypeLoadException: {types.Length}/{ex.Types.Length} types loaded. Loader exceptions:");
        foreach (var le in ex.LoaderExceptions.Where(z => z is not null).Distinct().Take(20))
            Console.WriteLine($"  - {le!.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED GetExportedTypes: {ex.GetType().Name}: {ex.Message}");
        continue;
    }

    var pluginInterface = core.GetType("PKHeX.Core.IPlugin");
    var plugins = types
        .Where(t => !t.IsInterface && !t.IsAbstract && pluginInterface!.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
        .ToList();
    Console.WriteLine($"IPlugin implementers found: {plugins.Count}");
    foreach (var p in plugins)
        Console.WriteLine($"  - {p.FullName}");
}
