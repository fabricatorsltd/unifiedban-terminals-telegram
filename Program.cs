/* unified/ban - Management and protection systems

© fabricators SRL, https://fabricators.ltd , https://unifiedban.solutions

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License with our addition
to Section 7 as published in unified/ban's the GitHub repository.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License and the
additional terms along with this program. 
If not, see <https://docs.fabricators.ltd/docs/licenses/unifiedban>.

For more information, see Licensing FAQ: 

https://docs.fabricators.ltd/docs/licenses/faq */

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unifiedban.Next.Common;
using Unifiedban.Next.Models;
using Timer = System.Timers.Timer;

namespace Unifiedban.Next.Terminal.Telegram;

internal static class Program
{
    private static bool _manualShutdown;
    private static readonly Timer Heartbeat = new();

    private static readonly GetUserPriviliges _telegramManager = new();
    private static readonly RabbitManager _rabbitManager = new();
    
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;

        Common.Utils.WriteLine($"== {AppDomain.CurrentDomain.FriendlyName} Startup ==");

        var builder = new ConfigurationBuilder()
            .SetBasePath(Environment.CurrentDirectory)
            .AddJsonFile("appsettings.json", false, false);
        CacheData.Configuration = builder.Build();
        _ = new Models.UBContext(CacheData.Configuration["Database"]);

        Common.Utils.WriteLine("Registering instance");
        Utils.RegisterInstance();
        Common.Utils.WriteLine("***************************************");
        SetHeartbeat();
        Common.Utils.WriteLine("***************************************");
        Utils.GetModulesQueues();
        Common.Utils.WriteLine("***************************************");
        CacheData.Load();
        Common.Utils.WriteLine("***************************************");
        _telegramManager.Init();
        Common.Utils.WriteLine("***************************************");
        _rabbitManager.Init();
        Common.Utils.WriteLine("***************************************");
        Task.Run(_telegramManager.Start);
        Common.Utils.WriteLine("***************************************");
        Task.Run(_rabbitManager.Start);
        Common.Utils.WriteLine("***************************************");
        Utils.SetInstanceStatus(Enums.States.Operational);
        Common.Utils.WriteLine("Startup completed.\n");

        Console.ReadLine();

        Common.Utils.WriteLine("Manual shutdown started.\n");
        _manualShutdown = true;
        DoShutdown();
    }
    private static void SetHeartbeat()
    {
        Common.Utils.WriteLine($"Starting Hearthbeat " +
                        $"(every {CacheData.Configuration["UptimeMonitor:Seconds"]} seconds)");
        Heartbeat.AutoReset = true;
        Heartbeat.Interval = 1000 * int.Parse(CacheData.Configuration["UptimeMonitor:Seconds"]);
        Heartbeat.Elapsed += (sender, eventArgs) =>
        {
            var http = new HttpClient();
            http.GetStringAsync(CacheData.Configuration["UptimeMonitor:URL"]);
        };
        Heartbeat.Start();
    }

    private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = (e.ExceptionObject as Exception);
            
        Console.WriteLine(ex?.Message);
    }
    private static void CurrentDomainOnProcessExit(object? sender, EventArgs e)
    {
        if (_manualShutdown) return;
        Common.Utils.WriteLine("SIGTERM shutdown started.\n");
        DoShutdown();
    }
    private static void DoShutdown()
    {
        Common.Utils.WriteLine("Stopping Heartbeat");
        Heartbeat.Stop();
        Common.Utils.WriteLine("Stopping Telegram client");
        _telegramManager.Stop();
        Common.Utils.WriteLine("Closing RabbitMQ connection");
        _rabbitManager.Shutdown();
        Common.Utils.WriteLine("Deregistering instance");
        Utils.DeregisterInstance();
        Common.Utils.WriteLine("***************************************");
        Common.Utils.WriteLine("Shutdown completed.");
    }
}