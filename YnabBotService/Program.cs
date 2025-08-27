using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Adp.YnabBotService;

file static class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(static (_, configurationBuilder) =>
                configurationBuilder.AddUserSecrets<Worker>())
            .ConfigureServices(static (_, services) => services.AddHostedService<Worker>());
    }
}