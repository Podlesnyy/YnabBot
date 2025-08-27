using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Adp.Banks.Interfaces;
using Adp.Messengers.Interfaces;
using Adp.Messengers.Telegram;
using Adp.Persistent;
using Adp.YnabClient;
using Adp.YnabClient.Ynab;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Adp.YnabBotService;

internal sealed class Worker( IConfiguration configuration ) : BackgroundService
{
    protected override async Task ExecuteAsync( CancellationToken stoppingToken )
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance( configuration ).As< IConfiguration >();
        builder.RegisterType< TelegramBot >().As< IMessageSender >();
        builder.RegisterType< Oauth >().AsSelf().SingleInstance();
        builder.RegisterType< MessageFromBotToYnabConverter >().AsSelf().SingleInstance();

        builder.RegisterType< YnabDbContext >().AsSelf().InstancePerLifetimeScope();

        var assemblies = Directory
                         .EnumerateFiles( AppDomain.CurrentDomain.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly )
                         .Select( Assembly.LoadFrom )
                         .ToArray();
        builder.RegisterAssemblyTypes( assemblies ).AssignableTo< IBank >().AsImplementedInterfaces();
        var container = builder.Build();

        var messageToUser = container.Resolve< MessageFromBotToYnabConverter >();
        messageToUser.Init();

        while ( !stoppingToken.IsCancellationRequested )
            await Task.Delay( 1000, stoppingToken );
    }
}