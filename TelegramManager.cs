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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Unifiedban.Next.BusinessLogic.Telegram;

namespace Unifiedban.Next.Terminal.Telegram;

internal partial class TelegramManager
{
    internal static TelegramBotClient? BotClient;
    internal static readonly CancellationTokenSource Cts = new();
    internal static readonly string LastVersion = "4.0";
    private QueuedUpdateReceiver? _updateReceiver;

    private int _totalMessages = 0;
    private long _myId = 0;

    private readonly TGChatLogic _tgChatLogic = new ();
    private readonly Dictionary<long, List<Message>> _registrationInProgress = new();
    private readonly object _regInProgObject = new ();
    
    public void Init()
    {
        UpdateConfigurations();
        Common.Utils.WriteLine("Initializing Telegram client");

        _totalMessages = 0;

        CacheData.ControlChatId = long.Parse(CacheData.Configuration?["Telegram:ControlChatId"] ?? "0");
        BotClient = new TelegramBotClient(CacheData.Configuration?["Telegram:BotToken"] ?? string.Empty);

        MessageQueueManager.Initialize();
    }
    public async void Start()
    {
        try
        {
            var me = BotClient!.GetMeAsync().Result;
            _myId = me.Id;
            Common.Utils.WriteLine($"Start listening for @{me.Username}");
        }
        catch (Exception ex)
        {
            // TODO - stop startup
            Common.Utils.WriteLine($"Can't start: {ex.Message}", 3);
            return;
        }

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = { } // receive all update types
        };
        
        BotClient!.StartReceiving(
            updateHandler: UpdateHandler,
            pollingErrorHandler: PollingErrorHandler,
            receiverOptions, Cts.Token);
    }

    private Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken cts)
    {
        _totalMessages++;
        
        if (update.Message is not null)
            HandleMessage(update.Message);
        if(update.EditedMessage is not null)
            HandleMessage(update.EditedMessage);
        if (update.MyChatMember is not null)
            HandleUpdateMember(update.MyChatMember);
        if(update.ChatMember is not null)
            HandleUpdateMember(update.ChatMember);
        if (update.CallbackQuery is not null)
            HandleCallbackQuery(update.CallbackQuery);
        if(update.ChatJoinRequest is not null)
            HandleJoinRequestAsync(update.ChatJoinRequest);
        
        return Task.CompletedTask;
    }

    private Task PollingErrorHandler(ITelegramBotClient client, Exception ex, CancellationToken cts)
    {
        Common.Utils.WriteLine($"Polling ERROR: {ex.Message}", 4);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        Cts.Cancel();
    }

    private void UpdateConfigurations()
    {
        Common.Utils.WriteLine("Updating chats configuration");
        var defaultConfigs = new BusinessLogic.ConfigurationParameterLogic().Get();
        if (defaultConfigs.StatusCode != 200) throw new Exception("Error getting default configs!");

        foreach (var configParam in defaultConfigs.Payload)
        {
            configParam.SetDefault();
            
            foreach (var chat in CacheData.Chats.Values)
            {
                var config = chat.Configuration.FirstOrDefault(x =>
                    x.ConfigurationParameterId == configParam.ConfigurationParameterId);
                if (config == null)
                {
                    chat.Configuration.Add(configParam);
                    continue;
                }

                config.Category = configParam.Category;
                config.Platforms = configParam.Platforms;
                config.AcceptedValues = configParam.AcceptedValues;
                config.DefaultValue = configParam.DefaultValue;
            }
        }
    }
}
