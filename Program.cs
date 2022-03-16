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
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unifiedban.Next.BusinessLogic.Log;
using Unifiedban.Next.Common;
using Unifiedban.Next.Models.Log;
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

        Utils.WriteLine($"== {AppDomain.CurrentDomain.FriendlyName} Startup ==");

        var builder = new ConfigurationBuilder()
            .SetBasePath(Environment.CurrentDirectory)
            .AddJsonFile("appsettings.json", false, false);
        CacheData.Configuration = builder.Build();
        _ = new Models.UBContext(CacheData.Configuration["Database"]);
        
        Utils.WriteLine("Registering instance");
        Utils.RegisterInstance();
        Utils.WriteLine("***************************************");
        SetHeartbeat();
        Utils.WriteLine("***************************************");
        Utils.GetModulesQueues();
        Utils.WriteLine("***************************************");
        _telegramManager.Init();
        Utils.WriteLine("***************************************");
        _rabbitManager.Init();
        Utils.WriteLine("***************************************");
        Task.Run(_telegramManager.Start);
        Utils.WriteLine("***************************************");
        Task.Run(_rabbitManager.Start);
        Utils.WriteLine("***************************************");
        Utils.SetInstanceStatus(Enums.States.Operational);
        Utils.WriteLine("Startup completed.\n");

        Console.ReadLine();

        Utils.WriteLine("Manual shutdown started.\n");
        _manualShutdown = true;
        DoShutdown();
    }
    private static void SetHeartbeat()
    {
        Utils.WriteLine($"Starting Hearthbeat " +
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
        Utils.WriteLine("SIGTERM shutdown started.\n");
        DoShutdown();
    }
    private static void DoShutdown()
    {
        Utils.WriteLine("Stopping Heartbeat");
        Heartbeat.Stop();
        Utils.WriteLine("Stopping Telegram client");
        _telegramManager.Stop();
        Utils.WriteLine("Closing RabbitMQ connection");
        _rabbitManager.Shutdown();
        Utils.WriteLine("Deregistering instance");
        Utils.DeregisterInstance();
        Utils.WriteLine("***************************************");
        Utils.WriteLine("Shutdown completed.");
    }
}