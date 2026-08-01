using Grillisoft.Tools.DatabaseDeploy.Contracts;
using Microsoft.Extensions.Configuration;

namespace Grillisoft.Tools.DatabaseDeploy;

/// <summary>
/// The <c>databases</c> section of the settings, read and merged with the global ones. It answers
/// what the configuration says about a database - which databases there are, which provider one
/// uses, which hook scripts it has - without building anything or connecting anywhere.
/// </summary>
public class DatabasesConfiguration
{
    public const string SectionName = "databases";

    private readonly IConfigurationSection _section;
    private readonly GlobalSettings _global;
    private readonly Lazy<List<string>> _names;

    public DatabasesConfiguration(IConfiguration configuration)
    {
        _section = configuration.GetSection(SectionName);
        _global = configuration.GetSection(GlobalSettings.SectionName)?.Get<GlobalSettings>() ?? new GlobalSettings();
        _names = new Lazy<List<string>>(() => _section.GetChildren().Select(c => c.Key).ToList());
    }

    /// <summary>
    /// The databases the settings know about, in the order they were written in.
    /// </summary>
    public IReadOnlyCollection<string> Names => _names.Value;

    /// <summary>
    /// The settings of one database, as the provider factories want them.
    /// </summary>
    public IConfigurationSection GetSection(string name) => _section.GetSection(name);

    /// <summary>
    /// The provider of a database, or the default one when it does not name its own. Empty when
    /// nothing says which provider to use.
    /// </summary>
    public string GetProvider(string name) =>
        _global.DefaultProvider.OverrideWith(GetSection(name)["provider"]);

    /// <summary>
    /// The hook script names of a database. They are merged allowing an empty one: unlike the
    /// other settings an empty value here is not "nothing was said", it is how a database opts out
    /// of a hook the global settings turned on.
    /// </summary>
    public DatabaseHooks GetHooks(string name)
    {
        var section = GetSection(name);

        var dictionary = new Dictionary<DatabaseHook, string>()
        {
            { DatabaseHook.PreDeploy, _global.PreDeploy.OverrideWithAllowEmpty(section["preDeploy"]) },
            { DatabaseHook.PostDeploy, _global.PostDeploy.OverrideWithAllowEmpty(section["postDeploy"]) },
            { DatabaseHook.PreRollback, _global.PreRollback.OverrideWithAllowEmpty(section["preRollback"]) },
            { DatabaseHook.PostRollback, _global.PostRollback.OverrideWithAllowEmpty(section["postRollback"]) }
        };

        return new DatabaseHooks(dictionary);
    }
}
