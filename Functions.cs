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
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Unifiedban.Next.Common.Telegram;

namespace Unifiedban.Next.Terminal.Telegram;

internal partial class TelegramManager
{
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

        if (CacheData.UserPermissions[chatId].ContainsKey(0))
        {
            return CacheData.UserPermissions[chatId][0];
        }
        
        // TODO - reload permissions time by time. No notification is sent by Telegram
        // TODO - get chat admins too
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
    
    /// <summary>
    /// Get Bot's privileges in chat
    /// </summary>
    /// <param name="chatId">The target chat id</param>
    /// <returns>True if bot is chat admin</returns>
    private bool LoadChatPermissions(long chatId)
    {
        Common.Utils.WriteLine("Getting bot's permissions for chat(s)");
            
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

        if (!CacheData.UserPermissions.ContainsKey(chatId)) return false;
        if (!CacheData.UserPermissions[chatId].ContainsKey(0)) return false;
        if (CacheData.UserPermissions[chatId][0].CanPostMessages)
        {
            BotClient!.SendTextMessageAsync(chatId, "Bot must be set as Administrator to work properly").Wait();
        }

        return false;
    }

    /// <summary>
    /// Get all chat admins and fill <see cref="CacheData.UserPermissions"/> with obtained data
    /// </summary>
    /// <param name="chatId">The target chat id</param>
    private async void GetChatAdmins(long chatId)
    {
        if (!CacheData.UserPermissions.ContainsKey(chatId))
            CacheData.UserPermissions[chatId] = new Dictionary<long, UserPrivileges>();
        
        var chatAdmins = await BotClient!.GetChatAdministratorsAsync(chatId);
        foreach (var chatMember in chatAdmins)
        {
            var chatAdmin = (ChatMemberAdministrator) chatMember;
            CacheData.UserPermissions[chatId][chatMember.User.Id] = new UserPrivileges
            {
                CanManageChat = chatAdmin.CanManageChat,
                CanPostMessages = chatAdmin.CanPostMessages ?? false,
                CanEditMessages = chatAdmin.CanEditMessages ?? false,
                CanDeleteMessages = chatAdmin.CanDeleteMessages,
                CanManageVoiceChats = chatAdmin.CanManageVoiceChats,
                CanRestrictMembers = chatAdmin.CanRestrictMembers,
                CanPromoteMembers = chatAdmin.CanPromoteMembers,
                CanChangeInfo = chatAdmin.CanChangeInfo,
                CanInviteUsers = chatAdmin.CanInviteUsers,
                CanPinMessages = chatAdmin.CanPinMessages ?? false
            };
        }
    }
}