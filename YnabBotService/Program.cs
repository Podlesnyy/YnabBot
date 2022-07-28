using Adp.YnabBotService.DockerEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Adp.YnabBotService;

public static class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args).
            ConfigureAppConfiguration(static (_, configurationBuilder) => configurationBuilder.AddDockerEnv(@"d:\Projects\YnabBot\.env")).
            ConfigureServices(static (_, services) => services.AddHostedService<Worker>());
    }
}