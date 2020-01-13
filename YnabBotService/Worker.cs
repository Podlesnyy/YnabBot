using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Adp.Banks.Interfaces;
using Adp.Messengers.Interfaces;
using Adp.Messengers.Telegram;
using Adp.YnabClient;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Persistent;

namespace Adp.YnabBotService
{
    internal class Worker : BackgroundService
    {
        private readonly IConfiguration configuration;

        public Worker(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(configuration).As<IConfiguration>();
            builder.RegisterType<TelegramBot>().As<IMessageSender>();
            builder.RegisterType<MessageFromBotToYnabConverter>().AsSelf().SingleInstance();

            builder.RegisterType<YnabDbContext>().AsSelf().InstancePerLifetimeScope();

            var assemblies = Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly).Select(Assembly.LoadFrom).ToArray();
            builder.RegisterAssemblyTypes(assemblies).AssignableTo<IBank>().AsImplementedInterfaces();
            var container = builder.Build();

            var messageToUser = container.Resolve<MessageFromBotToYnabConverter>();
            messageToUser.Init();

            while (!stoppingToken.IsCancellationRequested) await Task.Delay(1000, stoppingToken);
        }
    }
}