using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryReview;

public sealed class Configuration : BasePluginConfiguration
{
    public string DesiredLanguage { get; set; } = "eng";
    public string RadarrUrl { get; set; } = "";
    public string RadarrApiKey { get; set; } = "";
    public string JellyfinMovieRoot { get; set; } = "";
    public string RadarrMovieRoot { get; set; } = "";
    public bool EnableReplacement { get; set; }
}

public sealed class Plugin : BasePlugin<Configuration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer)
    {
        Instance = this;
        StateDirectory = Path.Combine(paths.PluginConfigurationsPath, "library-review");
        Directory.CreateDirectory(StateDirectory);
    }
    public string StateDirectory { get; }
    public override string Name => "Library Review";
    public override string Description => "Review possible edition, runtime and audio mismatches. No automatic deletion.";
    public override Guid Id => Guid.Parse("9eba6ec4-c190-40c2-961c-71e92174680e");
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new() { Name = "library-review", EmbeddedResourcePath = "LibraryReview.Web.review.html", EnableInMainMenu = true },
        new() { Name = "library-review.js", EmbeddedResourcePath = "LibraryReview.Web.review.js" }
    ];
}

public sealed class Registrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        services.AddSingleton<ReviewService>();
    }
}
