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

using System.Linq;
using System.Collections.Concurrent;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Unifiedban.Next.Common.Telegram;

namespace Unifiedban.Next.Terminal.Telegram;

internal class MessageQueueManager
{
    private static bool _isInitialized;
    private static bool _isDisposing;

    private static readonly ConcurrentDictionary<long, MessageQueue> PrivateChats = new();
    private static readonly ConcurrentDictionary<long, MessageQueue> GroupChats = new();

    public static void Initialize()
    {
        var controlChatAdded = AddChatIfNotPresent(CacheData.ControlChatId);
        if (!controlChatAdded)
        {
            // todo - log
        }

        _isInitialized = true;

        // todo - log initialization
    }

    public static void Dispose()
    {
        _isDisposing = true;
        // Wait until all queues are dispatched
        while (PrivateChats.Values
                   .Where(x => x.Queue.Count > 0)
                   .ToList().Count > 0
               ||
               GroupChats.Values
                   .Where(x => x.Queue.Count > 0)
                   .ToList().Count > 0)
        {
        }
    }

    public static bool AddGroupIfNotPresent(Models.Telegram.TGChat group)
    {
        // Do not accept new chat if going to shutdown
        if (_isDisposing)
            return false;

        if (GroupChats.ContainsKey(group.TelegramChatId))
            return false;

        var added = GroupChats.TryAdd(group.TelegramChatId,
            new MessageQueue(group.TelegramChatId, 20));
        return added;
    }

    public static bool RemoveGroupIfPresent(long telegramChatId)
    {
        if (_isDisposing)
            return false;

        return GroupChats.ContainsKey(telegramChatId) && GroupChats.TryRemove(telegramChatId, out _);
    }

    private static bool AddChatIfNotPresent(long chatId)
    {
        // Do not accept new chat if going to shutdown
        if (_isDisposing)
            return false;

        if (PrivateChats.ContainsKey(chatId))
            return false;

        return PrivateChats.TryAdd(chatId, new MessageQueue(chatId, 60));
    }

    public static void EnqueueMessage(ActionRequest actionRequest)
    {
        if (!_isInitialized || _isDisposing)
            return;

        if (actionRequest.Message.Chat.Type is ChatType.Group or ChatType.Supergroup)
        {
            if (!GroupChats.ContainsKey(actionRequest.Message.Chat.Id)) return;
            GroupChats[actionRequest.Message.Chat.Id]
                .Queue
                .Enqueue(actionRequest);
        }

        if (actionRequest.Message.Chat.Type != ChatType.Private &&
            actionRequest.Message.Chat.Type != ChatType.Channel) return;

        if (!PrivateChats.ContainsKey(actionRequest.Message.Chat.Id))
            PrivateChats.TryAdd(actionRequest.Message.Chat.Id, new MessageQueue(actionRequest.Message.Chat.Id, 60));

        PrivateChats[actionRequest.Message.Chat.Id]
            .Queue
            .Enqueue(actionRequest);
    }

    public static void EnqueueLog(ActionRequest actionRequest)
    {
        if (!_isInitialized || _isDisposing)
            return;

#if DEBUG
        actionRequest.Message.Text = actionRequest.Message.Text.Replace("#UB", "#UBB");
#endif

        actionRequest.Message.Chat = new Chat()
        {
            Id = CacheData.ControlChatId,
            Type = ChatType.Channel
        };
        actionRequest.Message.DisableWebPagePreview = true;

        PrivateChats[CacheData.ControlChatId]
            .Queue
            .Enqueue(actionRequest);

        if (actionRequest.Message.ControlChatId == default) return;
        if (PrivateChats.ContainsKey(actionRequest.Message.ControlChatId))
            PrivateChats[CacheData.ControlChatId]
                .Queue
                .Enqueue(actionRequest);
    }
}