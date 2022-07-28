using Microsoft.Extensions.Configuration;

namespace Adp.YnabBotService.DockerEnv;

public sealed class ConfigurationSource : IConfigurationSource
{
    private readonly string envPath;

    public ConfigurationSource(string envPath)
    {
        this.envPath = envPath;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new EnvProvider(envPath);
    }
}