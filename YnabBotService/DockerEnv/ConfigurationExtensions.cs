using Microsoft.Extensions.Configuration;

namespace Adp.YnabBotService.DockerEnv;

public static class ConfigurationExtensions
{
    public static IConfigurationBuilder AddDockerEnv(this IConfigurationBuilder builder, string envPath)
    {
        builder.Add(new ConfigurationSource(envPath));
        return builder;
    }
}