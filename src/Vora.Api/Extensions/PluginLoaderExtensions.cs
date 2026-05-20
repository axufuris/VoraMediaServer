using System.Reflection;
using System.Runtime.Loader;
using Vora.Plugins.Interfaces;

namespace Vora.Api.Extensions;

public static class PluginLoaderExtensions
{
    private static readonly Type[] PluginProviderInterfaces =
    {
        typeof(IChronologyProvider),
        typeof(IMetadataProvider),
        typeof(ICollectionSyncProvider),
        typeof(IFolderWatcherProvider),
        typeof(ILocalMediaScannerProvider),
        typeof(IRatingsProvider),
        typeof(IArtworkProvider),
        typeof(IDiscoveryProvider),
        typeof(IDiscoveryTheaterProvider),
        typeof(IRequestProvider),
        typeof(ICalendarProvider),
        typeof(IRecommendationProvider),
        typeof(IOverlayProvider),
        typeof(IMusicArtworkProvider),
        typeof(IPodcastDiscoveryProvider),
        typeof(ILyricsProvider),
        typeof(IListeningDataProvider)
    };

    public static IServiceCollection AddVoraPlugins(this IServiceCollection services, string pluginsFolderPath)
    {
        LoadBuiltInPlugins(services);
        LoadExternalPlugins(services, pluginsFolderPath);
        return services;
    }

    private static void LoadBuiltInPlugins(IServiceCollection services)
    {
        var basePath = AppContext.BaseDirectory;
        foreach (var dll in Directory.GetFiles(basePath, "Vora*.dll"))
        {
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(dll);
                if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName == assemblyName.FullName))
                {
                    Assembly.Load(assemblyName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Plugin System] Could not pre-load {dll}: {ex.Message}");
            }
        }

        var pluginTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(IsConcretePlugin);

        foreach (var type in pluginTypes)
        {
            RegisterPluginType(services, type);
            Console.WriteLine($"[Plugin System] Loaded BUILT-IN plugin: {type.Name}");
        }
    }

    private static void LoadExternalPlugins(IServiceCollection services, string pluginsFolderPath)
    {
        if (!Directory.Exists(pluginsFolderPath))
        {
            Directory.CreateDirectory(pluginsFolderPath);
            return;
        }

        var pluginFiles = Directory.GetFiles(pluginsFolderPath, "*.dll", SearchOption.AllDirectories);

        foreach (var file in pluginFiles)
        {
            if (file.EndsWith(".deleted", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(file);

                foreach (var type in SafeGetTypes(assembly).Where(IsConcretePlugin))
                {
                    RegisterPluginType(services, type);
                    Console.WriteLine($"[Plugin System] Loaded EXTERNAL plugin: {type.Name} from {Path.GetFileName(file)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Plugin System] Failed to load plugin {file}: {ex.Message}");
            }
        }
    }

    private static void RegisterPluginType(IServiceCollection services, Type type)
    {
        foreach (var providerInterface in PluginProviderInterfaces)
        {
            if (providerInterface.IsAssignableFrom(type))
            {
                services.AddTransient(providerInterface, type);
                break;
            }
        }

        services.AddTransient(typeof(IVoraPlugin), type);
    }

    private static bool IsConcretePlugin(Type type) =>
        typeof(IVoraPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract;

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }
}
