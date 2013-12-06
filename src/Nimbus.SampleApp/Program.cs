﻿using System;
using System.Configuration;
using System.Reflection;
using Autofac;
using Nimbus.Autofac;
using Nimbus.Configuration;
using Nimbus.Infrastructure;
using Nimbus.InfrastructureContracts;
using Nimbus.Logger;

namespace Nimbus.SampleApp
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            using (var container = CreateContainer())
            {
                var deepThought = container.Resolve<DeepThought>();
                deepThought.ComputeTheAnswer().Wait();
                Console.ReadKey();
            }
        }

        private static IContainer CreateContainer()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<DeepThought>();

            builder.RegisterType<ConsoleLogger>()
                   .AsImplementedInterfaces()
                   .SingleInstance();

            var handlerTypesProvider = new AssemblyScanningTypeProvider(Assembly.GetExecutingAssembly());

            builder.RegisterNimbus(handlerTypesProvider);
            

            //TODO: Set up your own connection string in app.config
            var connectionString = ConfigurationManager.AppSettings["AzureConnectionString"];

            builder.Register(c => new BusBuilder()
                                      .Configure()
                                      .WithConnectionString(
                                          connectionString)
                                      .WithNames("MyApp", Environment.MachineName)
                                      .WithTypesFrom(handlerTypesProvider)
                                      .WithMulticastEventBroker(c.Resolve<IMulticastEventBroker>())
                                      .WithCompetingEventBroker(c.Resolve<ICompetingEventBroker>())
                                      .WithCommandBroker(c.Resolve<ICommandBroker>())
                                      .WithRequestBroker(c.Resolve<IRequestBroker>())
                                      .WithLogger(c.Resolve<ILogger>())
                                      .Build())
                   .As<IBus>()
                   .AutoActivate()
                   .OnActivated(c => c.Instance.Start())
                   .SingleInstance();

            var container = builder.Build();
            return container;
        }
    }
}