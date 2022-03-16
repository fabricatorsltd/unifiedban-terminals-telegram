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

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Unifiedban.Next.BusinessLogic.Telegram;
using Unifiedban.Next.Common.Telegram;
using Unifiedban.Next.Models.Log;
using Unifiedban.Next.Models.Telegram;

namespace Unifiedban.Next.Terminal.Telegram;

internal class CacheData
{
    internal static Instance? Instance;
    internal static long ControlChatId { get; set; }
    internal static IConfigurationRoot? Configuration;

    internal static Dictionary<long, UserPrivileges> BotPermissions = new();
    internal static Dictionary<long, Dictionary<long, UserPrivileges>> UserPermissions = new();

    internal static Dictionary<long, TGChat> Chats = new();
    internal static List<long> V3Chats = new();

    internal static (string Exchange, string RoutingKey) MemberJoinQueue = ("telegram", "join");
    internal static (string Exchange, string RoutingKey) MessageBaseQueue = ("checks", "base");

    internal static void Load()
    {
        Common.Utils.WriteLine("Loading cache");

        var chatLogic = new TGChatLogic();
        foreach (var chat in chatLogic.Get().Payload)
        {
            Chats.Add(chat.TelegramChatId, chat);
            MessageQueueManager.AddGroupIfNotPresent(chat);
        }
            
        Common.Utils.WriteLine("Loading cache completed");
    }
}