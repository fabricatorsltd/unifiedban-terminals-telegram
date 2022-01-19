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
using System.Threading.Tasks;
using Telegram.Bot;
using Unifiedban.Next.Common.Telegram;

namespace Unifiedban.Next.Terminal.Telegram;

internal class ActionHandler
{
    public void LeaveChat(ActionData actionData)
    {
        try
        {
            GetUserPriviliges.BotClient?
                .LeaveChatAsync(actionData.Chat.Id, GetUserPriviliges.Cts.Token);
        }
        catch (Exception ex)
        {
            Utils.WriteLine(ex.Message, 3);
        }
    }
    public void SendMessage(ActionData actionData)
    {
        try
        {
            _ = GetUserPriviliges.BotClient?.SendTextMessageAsync(
                actionData.Chat.Id,
                actionData.Text,
                actionData.ParseMode,
                disableWebPagePreview: actionData.DisableWebPagePreview,
                disableNotification: actionData.DisableNotification,
                replyToMessageId: actionData.ReferenceMessageId,
                replyMarkup: actionData.ReplyMarkup,
                cancellationToken: GetUserPriviliges.Cts.Token
            ).Result;

            switch (actionData.PostSentAction)
            {
                case ActionData.PostSentActions.Pin:
                    // Bot.Manager.BotClient.PinChatMessageAsync(msgToSend.Chat.Id, sent.MessageId);
                    break;
                case ActionData.PostSentActions.Destroy:
                    Task.Run(() =>
                    {
                        System.Threading.Thread.Sleep(1000 * actionData.AutoDestroyTimeInSeconds);
                        // Bot.Manager.BotClient.DeleteMessageAsync(msgToSend.Chat.Id, sent.MessageId);
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            Utils.WriteLine(ex.Message, 3);
        }
    }
    public void DeleteMessage(ActionData actionData)
    {
        if (actionData.ReferenceMessageId is null) return;
        try
        {
            GetUserPriviliges.BotClient?
                .DeleteMessageAsync(actionData.Chat.Id, (int)actionData.ReferenceMessageId, GetUserPriviliges.Cts.Token);
        }
        catch (Exception ex)
        {
            Utils.WriteLine(ex.Message, 3);
        }
    }
    public void PinMessage(ActionData actionData)
    {
        if (actionData.ReferenceMessageId is null) return;
        try
        {
            GetUserPriviliges.BotClient?
                .PinChatMessageAsync(actionData.Chat.Id,
                    (int)actionData.ReferenceMessageId,
                    actionData.DisableNotification,
                    GetUserPriviliges.Cts.Token);
        }
        catch (Exception ex)
        {
            Utils.WriteLine(ex.Message, 3);
        }
    }
}