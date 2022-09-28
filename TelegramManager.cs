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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Unifiedban.Next.BusinessLogic.Telegram;
using Unifiedban.Next.Common;
using Unifiedban.Next.Common.Telegram;
using Unifiedban.Next.Models.Telegram;

namespace Unifiedban.Next.Terminal.Telegram;

internal class TelegramManager
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

        /*_updateReceiver = new QueuedUpdateReceiver(BotClient!, receiverOptions);
            
        try
        {
            await foreach (var update in _updateReceiver.WithCancellation(Cts.Token))
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
            }
        }
        catch (OperationCanceledException exception)
        {
            Common.Utils.WriteLine($"_updateReceiver OperationCanceledException: {exception.Message} (Stopping?)", 2);
            Common.Utils.WriteLine($"_updateReceiver OperationCanceledException: {exception.InnerException?.Message} (Stopping?)", 2);
        }
        catch (Exception ex)
        {
            Common.Utils.WriteLine($"_updateReceiver Exception: {ex.Message}", 3);
            Common.Utils.WriteLine($"_updateReceiver Exception: {ex.InnerException?.Message}", 3);
        }
        */
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
    
    #region " Handlers "
    private void HandleMessage(Message message)
    {
        var isGroup = message.Chat.Type is ChatType.Group or ChatType.Supergroup;
        if (isGroup && !CacheData.Chats.ContainsKey(message.Chat.Id))
        {
            if (bool.Parse(CacheData.Configuration?["Telegram:V3ChatMigration"] ?? "false") &&
                CacheData.V3Chats.Contains(message.Chat.Id))
            {
                Task.Run(() => MigrateFromV3(message));
                return;
            }

            lock (_regInProgObject)
            {
                if (!_registrationInProgress.ContainsKey(message.Chat.Id))
                {
                    _registrationInProgress.Add(message.Chat.Id, new List<Message>());
                    Common.Utils.WriteLine($"Received message for not known chat {message.Chat.Id} (going to register)", 2);
                    RegisterNewChat(message);
                }
                
                _registrationInProgress[message.Chat.Id].Add(message);
            }

            return;
        }

        lock (_regInProgObject)
        {
            if (_registrationInProgress.ContainsKey(message.Chat.Id))
            {
                _registrationInProgress[message.Chat.Id].Add(message);
                return;
            }
        }

        if (isGroup && CacheData.Chats[message.Chat.Id].Status != Enums.ChatStates.Active)
        {
#if DEBUG
            Common.Utils.WriteLine($"Received message from chat with status {CacheData.Chats[message.Chat.Id].Status.ToString()}", 0);
#endif
            return;
        }

        switch (message.Type)
        {
            case MessageType.Text:
            case MessageType.Photo:
            case MessageType.Audio:
            case MessageType.Video:
            case MessageType.Voice:
            case MessageType.Document:
            case MessageType.Sticker:
                Task.Run(() => HandleBaseMessage(message));
                break;
            case MessageType.ChatMembersAdded:
                Task.Run(() => HandleMemberJoin(message));
                break;
            case MessageType.ChatMemberLeft:
                Task.Run(() => HandleMemberLeft(message));
                break;
            case MessageType.ChatTitleChanged:
            case MessageType.ChatPhotoChanged:
            case MessageType.ChatPhotoDeleted:
            case MessageType.MigratedToSupergroup:
            case MessageType.MigratedFromGroup:
                Task.Run(() => HandleChatDetailUpdate(message));
                break;
            case MessageType.GroupCreated:
            case MessageType.SupergroupCreated:
            case MessageType.ChannelCreated:
                Task.Run(() => HandleChatCreation(message));
                break;
            case MessageType.Location:
            case MessageType.Contact:
            case MessageType.Venue:
            case MessageType.Game:
            case MessageType.VideoNote:
            case MessageType.Invoice:
            case MessageType.SuccessfulPayment:
            case MessageType.WebsiteConnected:
            case MessageType.MessagePinned:
            case MessageType.Poll:
            case MessageType.Dice:
            case MessageType.MessageAutoDeleteTimerChanged:
            case MessageType.ProximityAlertTriggered:
            case MessageType.VideoChatScheduled:
            case MessageType.VideoChatStarted:
            case MessageType.VideoChatEnded:
            case MessageType.VideoChatParticipantsInvited:
                return;
            case MessageType.Unknown:
            default:
                Common.Utils.WriteLine($"Received message of Type {message.Type.ToString()} that has no handler", 2);
                return;
        }
    }
    private async void HandleUpdateMember(ChatMemberUpdated update)
    {
        if (!CacheData.Chats.ContainsKey(update.Chat.Id)) return;
        if (update.NewChatMember.User.Id == _myId)
        {
            if (update.NewChatMember is ChatMemberAdministrator chatMemberAdministrator)
            {
                CacheData.BotPermissions[update.Chat.Id] = new UserPrivileges
                {
                    CanManageChat = chatMemberAdministrator.CanManageChat,
                    CanPostMessages = chatMemberAdministrator.CanPostMessages ?? false,
                    CanEditMessages = chatMemberAdministrator.CanEditMessages ?? false,
                    CanDeleteMessages = chatMemberAdministrator.CanDeleteMessages,
                    CanManageVoiceChats = chatMemberAdministrator.CanManageVoiceChats,
                    CanRestrictMembers = chatMemberAdministrator.CanRestrictMembers,
                    CanPromoteMembers = chatMemberAdministrator.CanPromoteMembers,
                    CanChangeInfo = chatMemberAdministrator.CanChangeInfo,
                    CanInviteUsers = chatMemberAdministrator.CanInviteUsers,
                    CanPinMessages = chatMemberAdministrator.CanPinMessages ?? false
                };
#if DEBUG
                await BotClient!.SendTextMessageAsync(
                    update.Chat!,
                    $"Telegram told me I'm a chat admin. CanPinMessages status is {CacheData.BotPermissions[update.Chat.Id].CanPinMessages}",
                    cancellationToken: Cts.Token
                );
#endif
            }
            else
            {
                if (update.NewChatMember is not ChatMemberLeft)
                {
#if DEBUG
                    await BotClient!.SendTextMessageAsync(
                        update.Chat!,
                        $"Telegram told me I'm not a chat admin.",
                        cancellationToken: Cts.Token
                    );
#endif
                }
            }
        }
        else
        {
            if(!CacheData.UserPermissions.ContainsKey(update.Chat.Id))
                CacheData.UserPermissions[update.Chat.Id] = new Dictionary<long, UserPrivileges>();
            
            if (update.NewChatMember is ChatMemberAdministrator chatMemberAdministrator)
            {
                CacheData.UserPermissions[update.Chat.Id][update.NewChatMember.User.Id] = new UserPrivileges
                {
                    ChatId = update.Chat.Id,
                    UserId = update.NewChatMember.User.Id,
                    CanManageChat = chatMemberAdministrator.CanManageChat,
                    CanPostMessages = chatMemberAdministrator.CanPostMessages ?? false,
                    CanEditMessages = chatMemberAdministrator.CanEditMessages ?? false,
                    CanDeleteMessages = chatMemberAdministrator.CanDeleteMessages,
                    CanManageVoiceChats = chatMemberAdministrator.CanManageVoiceChats,
                    CanRestrictMembers = chatMemberAdministrator.CanRestrictMembers,
                    CanPromoteMembers = chatMemberAdministrator.CanPromoteMembers,
                    CanChangeInfo = chatMemberAdministrator.CanChangeInfo,
                    CanInviteUsers = chatMemberAdministrator.CanInviteUsers,
                    CanPinMessages = chatMemberAdministrator.CanPinMessages ?? false
                };
#if DEBUG
                await BotClient!.SendTextMessageAsync(
                    update.Chat!,
                    $"Telegram told me that {update.NewChatMember.User.Id} is chat admin. CanPinMessages status is {CacheData.BotPermissions[update.Chat.Id].CanPinMessages}",
                    cancellationToken: Cts.Token
                );
#endif
            }
            else if (CacheData.UserPermissions[update.Chat.Id].ContainsKey(update.NewChatMember.User.Id))
                CacheData.UserPermissions[update.Chat.Id].Remove(update.NewChatMember.User.Id);
        }
    }
    private async void HandleCallbackQuery(CallbackQuery callbackQuery)
    {
        _totalMessages++;
        Common.Utils.WriteLine("New callbackQuery: " + callbackQuery.Message?.Text, 0);
#if DEBUG
        await BotClient!.SendTextMessageAsync(
            callbackQuery.Message?.Chat!,
            $"New callbackQuery: {callbackQuery.Message?.Text}",
            cancellationToken: Cts.Token
        );
#endif
    }
    private async void HandleJoinRequestAsync(ChatJoinRequest chatJoinRequest)
    {
        if (!CacheData.Chats.ContainsKey(chatJoinRequest.Chat.Id)) return;
#if DEBUG
        await BotClient!.SendTextMessageAsync(
            chatJoinRequest.Chat,
            $"New ChatJoinRequest: {chatJoinRequest.From.Username ?? chatJoinRequest.From.Id.ToString()}",
            cancellationToken: Cts.Token
        );
#endif
    }
    private async void HandleBaseMessage(Message message)
    {
        if (!CacheData.Chats.ContainsKey(message.Chat.Id)) return;
        if (message.From is null)
        {
            Common.Utils.WriteLine("Received message with null sender (message.From)", 2);
            return;
        }
        if(message.Text is null)
        {
            Common.Utils.WriteLine("Received message with null text (message.Text)", 2);
            return;
        }
        
        Common.Utils.WriteLine(message.EditDate is null ? $"New msg: {message.Text}" : $"Edited msg: {message.Text}", 0);

        var botPrivileges = new UserPrivileges();
        if (CacheData.BotPermissions.ContainsKey(message.Chat.Id))
            botPrivileges = CacheData.BotPermissions[message.Chat.Id];

        var sender = message.From!.Id;
        if (message.SenderChat is not null)
            sender = message.SenderChat.Id;

        var queueMsg = new QueueMessage<TGChat, UserPrivileges, UserPrivileges, Message>
        {
            UBChat = CacheData.Chats[message.Chat.Id],
            Platform = Enums.Platforms.Telegram,
            Category = Enums.QueueMessageCategories.Base,
            BotPermissions = botPrivileges,
            UserPermissions = await GetUserPrivileges(message.Chat.Id, sender),
            Payload = message
        };

        var json = JsonConvert.SerializeObject(queueMsg);
        var body = Encoding.UTF8.GetBytes(json);

        if (message.Text.StartsWith(queueMsg.UBChat.CommandPrefix))
           RabbitManager.PublishMessage("telegram", "cmd", body);
        else
            RabbitManager.PublishMessage(CacheData.MessageBaseQueue.Exchange, CacheData.MessageBaseQueue.RoutingKey, body);
    }
    private async void HandleMemberJoin(Message message)
    {
        if (!CacheData.Chats.ContainsKey(message.Chat.Id)) return;
        // TODO - if group activated delete join system message, remove it
        var botPrivileges = new UserPrivileges();

        if (CacheData.BotPermissions.ContainsKey(message.Chat.Id))
            botPrivileges = CacheData.BotPermissions[message.Chat.Id];

        foreach (var newChatMember in message.NewChatMembers!)
        {
            if (newChatMember.Id == _myId)
            {
                if (CacheData.Chats[message.Chat.Id].Status == Enums.ChatStates.Disabled)
                {
                    CacheData.Chats[message.Chat.Id].Status = Enums.ChatStates.Active;
                    if (_tgChatLogic.Update(CacheData.Chats[message.Chat.Id]).StatusCode != 200)
                    {
                        // TODO - send to control chat
                        Common.Utils.WriteLine($"Error enabling chat {message.Chat.Id} on HandleUpdateMember", 3);
                    }
                }
                
                if (LoadChatPermissions(message.Chat.Id))
                {
                    BotClient!.SendTextMessageAsync(message.Chat.Id, "Welcome to IC!").Wait();
                }
                continue;
            }
            
            var queueMsg = new QueueMessage<TGChat, UserPrivileges, UserPrivileges, Message>
            {
                UBChat = CacheData.Chats[message.Chat.Id],
                Platform = Enums.Platforms.Telegram,
                Category = Enums.QueueMessageCategories.MemberJoin,
                BotPermissions = botPrivileges,
                UserPermissions = await GetUserPrivileges(message.Chat.Id, newChatMember.Id),
                Payload = message
            };

            var json = JsonConvert.SerializeObject(queueMsg);
            var body = Encoding.UTF8.GetBytes(json);

            RabbitManager.PublishMessage(CacheData.MemberJoinQueue.Exchange, CacheData.MemberJoinQueue.RoutingKey, body);
        }
    }
    private async void HandleMemberLeft(Message message)
    {
        if (!CacheData.Chats.ContainsKey(message.Chat.Id)) return;
        // TODO - if group activated delete left system message, remove it
        if (message.LeftChatMember!.Id == _myId)
        {
            if (CacheData.BotPermissions.ContainsKey(message.Chat.Id))
                CacheData.BotPermissions.Remove(message.Chat.Id);

            CacheData.Chats[message.Chat.Id].Status = Enums.ChatStates.Disabled;
            if (_tgChatLogic.Update(CacheData.Chats[message.Chat.Id]).StatusCode != 200)
            {
                // TODO - send to control chat
                Common.Utils.WriteLine($"Error disabling chat {message.Chat.Id} on HandleUpdateMember", 3);
            }
        }
        else
        {
            if (!CacheData.UserPermissions.ContainsKey(message.Chat.Id)) return;
            
            if (CacheData.UserPermissions[message.Chat.Id].ContainsKey(message.LeftChatMember!.Id))
                CacheData.UserPermissions[message.Chat.Id].Remove(message.LeftChatMember!.Id);
        }
    }
    private async void HandleChatDetailUpdate(Message message)
    {
        if (!CacheData.Chats.ContainsKey(message.Chat.Id)) return;
        // TODO - update chat details on database
        Common.Utils.WriteLine($"Received chat update for {message.Chat.Id}", -1);
    }
    private async void HandleChatCreation(Message message)
    {
        Common.Utils.WriteLine($"Received chat update to create {message.Chat.Id}");
        if (!CacheData.Chats.ContainsKey(message.Chat.Id))
        {
            RegisterNewChat(message);
        }
    }
    #endregion

    #region " Functions "
    private async Task<UserPrivileges> GetUserPrivileges(long chatId, long? userId)
    {
        if(userId is null)
            return new UserPrivileges();
        
        if (!CacheData.UserPermissions.ContainsKey(chatId))
            CacheData.UserPermissions[chatId] = new Dictionary<long, UserPrivileges>();
            
        if (CacheData.UserPermissions[chatId].ContainsKey((long)userId))
        {
            return CacheData.UserPermissions[chatId][(long)userId];
        }

        UserPrivileges userPrivileges;
        var chatMember = await BotClient!.GetChatMemberAsync(chatId, (long)userId);
        switch (chatMember)
        {
            case ChatMemberAdministrator chatMemberAdministrator:
                userPrivileges = new UserPrivileges
                {
                    CanManageChat = chatMemberAdministrator.CanManageChat,
                    CanPostMessages = chatMemberAdministrator.CanPostMessages ?? false,
                    CanEditMessages = chatMemberAdministrator.CanEditMessages ?? false,
                    CanDeleteMessages = chatMemberAdministrator.CanDeleteMessages,
                    CanManageVoiceChats = chatMemberAdministrator.CanManageVoiceChats,
                    CanRestrictMembers = chatMemberAdministrator.CanRestrictMembers,
                    CanPromoteMembers = chatMemberAdministrator.CanPromoteMembers,
                    CanChangeInfo = chatMemberAdministrator.CanChangeInfo,
                    CanInviteUsers = chatMemberAdministrator.CanInviteUsers,
                    CanPinMessages = chatMemberAdministrator.CanPinMessages ?? false
                };
                break;
            case ChatMemberRestricted chatMemberRestricted:
                userPrivileges = new UserPrivileges
                {
                    CanManageChat = false,
                    CanPostMessages = chatMemberRestricted.CanSendMessages,
                    CanEditMessages = false,
                    CanDeleteMessages = false,
                    CanManageVoiceChats = false,
                    CanRestrictMembers = false,
                    CanPromoteMembers = false,
                    CanChangeInfo = chatMemberRestricted.CanChangeInfo,
                    CanInviteUsers = chatMemberRestricted.CanInviteUsers,
                    CanPinMessages = chatMemberRestricted.CanPinMessages
                };
                break;
            default:
            {
                if (CacheData.UserPermissions[chatId].ContainsKey(0))
                    return CacheData.UserPermissions[chatId][0];
                
                // TODO - reload permissions time by time. No notification is sent by Telegram
                var chat = await BotClient!.GetChatAsync(chatId);
                CacheData.UserPermissions[chatId][0] = new UserPrivileges
                {
                    CanManageChat = false,
                    CanPostMessages = chat.Permissions?.CanSendMessages ?? false,
                    CanEditMessages = false,
                    CanDeleteMessages = false,
                    CanManageVoiceChats = false,
                    CanRestrictMembers = false,
                    CanPromoteMembers = false,
                    CanChangeInfo = chat.Permissions?.CanChangeInfo ?? false,
                    CanInviteUsers = chat.Permissions?.CanInviteUsers ?? false,
                    CanPinMessages = chat.Permissions?.CanPinMessages ?? false
                };
                return CacheData.UserPermissions[chatId][0];
            }
        }
        
        CacheData.UserPermissions[chatId][(long)userId] = userPrivileges;

        return userPrivileges;
    }
    private void MigrateFromV3(Message message)
    {
        HandleMessage(message);
    }
    private async void RegisterNewChat(Message message)
    {
        if (message.MigrateFromChatId != null)
        {
            MigrateChat(message);
            return;
        }
        
        Common.Utils.WriteLine($"Registering new chat {message.Chat.Id}");
        ChatMember? creator;
        try
        {
            var admins = await BotClient!.GetChatAdministratorsAsync(message.Chat!);
            creator = admins.FirstOrDefault(x => x.Status == ChatMemberStatus.Creator);
            if (creator is null)
            {
                Common.Utils.WriteLine($"Can't register new chat {message.Chat.Id} since can't get creator", 3);
                SendToControlChat($"Can't register new chat {message.Chat.Id} since can't get creator");
                return;
            }
        }
        catch (Exception ex)
        {
            Common.Utils.WriteLine($"Can't register new chat {message.Chat.Id}: {ex.Message}", 2);
            return;
        }
        
        var newChat = _tgChatLogic.Register(message.Chat.Id, message.Chat.Title ?? "", 
            CacheData.ControlChatId, message.From?.LanguageCode ?? "en", 
            creator.User.Id, LastVersion);
        if (newChat.StatusCode == 200)
        {
            CacheData.Chats.Add(message.Chat.Id, newChat.Payload);
            if (LoadChatPermissions(message.Chat.Id))
            {
                // TODO - Send welcome to IC message
                if(CacheData.Chats[message.Chat.Id].LastVersion == LastVersion)
                    BotClient!.SendTextMessageAsync(message.Chat.Id, "Welcome to IC!").Wait();
                else
                    BotClient!.SendTextMessageAsync(message.Chat.Id, "Thank you for migrating to IC!").Wait();
            }

            MessageQueueManager.AddGroupIfNotPresent(CacheData.Chats[message.Chat.Id]);
            HandleRegistrationMessages(message.Chat.Id);
            return;
        }
        Common.Utils.WriteLine($"Can't register new chat {message.Chat.Id} with error: {newChat.StatusDescription}", 3);
        SendToControlChat($"Can't register new chat {message.Chat.Id} with error: {newChat.StatusDescription}");
    }
    private void MigrateChat(Message message)
    {
        if (message.MigrateFromChatId == null)
        {
            // TODO - log error
            return;
        }
        
        // get chat from database
        var oldChat = CacheData.Chats[message.MigrateFromChatId!.Value];
        oldChat.TelegramChatId = message.Chat.Id;
        
        // update record with new id
        var newChat = _tgChatLogic.Update(oldChat);
        if (newChat.StatusCode != 200)
        {
            // TODO - log
            return;
        }
        
        CacheData.Chats.Add(message.Chat.Id, newChat.Payload);
        LoadChatPermissions(message.Chat.Id);

        if (!CacheData.BotPermissions.ContainsKey(message.MigrateFromChatId!.Value) &&
            CacheData.BotPermissions[message.Chat.Id].CanManageChat)
        {
            BotClient!.SendTextMessageAsync(message.Chat.Id, "Welcome to IC!").Wait();
        }
        else
        {
            CacheData.BotPermissions.Remove(message.MigrateFromChatId!.Value);
        }

        CacheData.Chats.Remove(message.MigrateFromChatId!.Value);
        HandleRegistrationMessages(message.Chat.Id);
    }

    private void HandleRegistrationMessages(long chatId)
    {
        List<Message> toHandle;
        lock (_regInProgObject)
        {
            toHandle = new List<Message>(_registrationInProgress[chatId]);
            _registrationInProgress.Remove(chatId);
        }
        foreach (var message in toHandle)
            HandleMessage(message);
    }
    private void SendToControlChat(string message)
    {
        if (CacheData.ControlChatId == 0) return;
        BotClient!.SendTextMessageAsync(CacheData.ControlChatId,message);
    }
    private bool LoadChatPermissions(long chatId)
    {
        Common.Utils.WriteLine("Getting permissions from chat(s)");
            
        var chatMember = BotClient!.GetChatMemberAsync(chatId, _myId).Result;
        if (chatMember is ChatMemberAdministrator chatMemberAdministrator)
        {
            CacheData.BotPermissions[chatId] = new UserPrivileges
            {
                CanManageChat = chatMemberAdministrator.CanManageChat,
                CanPostMessages = chatMemberAdministrator.CanPostMessages ?? false,
                CanEditMessages = chatMemberAdministrator.CanEditMessages ?? false,
                CanDeleteMessages = chatMemberAdministrator.CanDeleteMessages,
                CanManageVoiceChats = chatMemberAdministrator.CanManageVoiceChats,
                CanRestrictMembers = chatMemberAdministrator.CanRestrictMembers,
                CanPromoteMembers = chatMemberAdministrator.CanPromoteMembers,
                CanChangeInfo = chatMemberAdministrator.CanChangeInfo,
                CanInviteUsers = chatMemberAdministrator.CanInviteUsers,
                CanPinMessages = chatMemberAdministrator.CanPinMessages ?? false
            };
            return true;
        }
        
        BotClient!.SendTextMessageAsync(chatId, "Bot must be set as Administrator to work properly").Wait();
        return false;
    }
    #endregion
}