using Microsoft.Extensions.Configuration;

namespace Adp.YnabBotService.DockerEnv;

internal sealed class ConfigurationSource( string envPath ) : IConfigurationSource
{
    public IConfigurationProvider Build( IConfigurationBuilder builder ) => new EnvProvider( envPath );
}