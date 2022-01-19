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
using System.Linq;
using System.Reflection;
using Unifiedban.Next.Common;
using Unifiedban.Next.Models.Log;

namespace Unifiedban.Next.Terminal.Telegram;

internal class Utils
{
    private static readonly BusinessLogic.Log.InstanceLogic _instanceLogic = new();
    private static readonly BusinessLogic.ModuleLogic _moduleLogic = new();
    internal static void WriteLine(string? message, int severity = 1)
    {
        Console.Write("{0} ", DateTime.UtcNow);

        var prefix = "[info]";

        switch (severity)
        {
            case -1:
                prefix = "[trace]";
                Console.ForegroundColor = ConsoleColor.Cyan;
                break;
            case 0:
                prefix = "[debug]";
                Console.ForegroundColor = ConsoleColor.Green;
                break;
            case 2:
                prefix = "[warn]";
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                break;
            case 3:
                prefix = "[error]";
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            case 4:
                prefix = "[fatal]";
                Console.BackgroundColor = ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.White;
                break;
            default:
                prefix = "[info]";
                Console.ForegroundColor = ConsoleColor.White;
                break;
        }

        Console.Write(prefix);
        Console.ResetColor();
        Console.Write(" - {0}", message);
        Console.Write(Environment.NewLine);
    }

    internal static void RegisterInstance()
    {
        var newInstance = new Instance()
        {
            ModuleId = AppDomain.CurrentDomain.FriendlyName,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "NoVersion",
            Start = DateTime.UtcNow,
            Status = Enums.States.Startup
        };

        var registered = _instanceLogic.Add(newInstance);
        if (registered.StatusCode != 200)
        {
            WriteLine("***************************************", 4);
            WriteLine("Error registering instance.", 4);
            WriteLine(registered.StatusDescription, 4);
            Environment.Exit(0);
        }
            
        CacheData.Instance = registered.Payload;
        WriteLine($"== InstanceId {CacheData.Instance?.InstanceId} ==");
    }
    internal static void DeregisterInstance()
    {
        if (CacheData.Instance is null)
        {
            WriteLine("Trying to deregister instance but is null", 3);
            return;
        }

        CacheData.Instance.Stop = DateTime.UtcNow;
        CacheData.Instance.Status = Enums.States.Stopped;
        var updated = _instanceLogic.Update(CacheData.Instance!);
        if (updated.StatusCode == 200) return;
        WriteLine("***************************************", 3);
        WriteLine("Error deregistering instance.", 3);
        WriteLine(updated.StatusDescription, 3);
    }
    internal static void SetInstanceStatus(Enums.States state)
    {
        if (CacheData.Instance is null)
        {
            WriteLine("Trying to set instance but is null", 3);
            return;
        }
        
        CacheData.Instance.Status = state;
        var updated = _instanceLogic.Update(CacheData.Instance!);
        if (updated.StatusCode == 0) return;
    }

    internal static void GetModulesQueues()
    {
        WriteLine("Getting modules queues");
        var modules = _moduleLogic.GetModules().Payload;
        var joinModule = modules.FirstOrDefault(x => x.MessageCategory == Enums.QueueMessageCategories.MemberJoin);
        if (joinModule is not null)
        {
            CacheData.MemberJoinQueue = (joinModule.Exchange, joinModule.RoutingKey);
            WriteLine($"MemberJoinQueue ({joinModule.Exchange}, {joinModule.RoutingKey})");
        }

        var messageModule = modules.FirstOrDefault(x => x.MessageCategory == Enums.QueueMessageCategories.Base);
        if (messageModule is not null)
        {
            CacheData.MessageBaseQueue = (messageModule.Exchange, messageModule.RoutingKey);
            WriteLine($"MessageBaseQueue ({messageModule.Exchange}, {messageModule.RoutingKey})");
        }
    }
}