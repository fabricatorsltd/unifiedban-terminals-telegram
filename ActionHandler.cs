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
            TelegramManager.BotClient?
                .LeaveChatAsync(actionData.Chat.Id, TelegramManager.Cts.Token);
        }
        catch (Exception ex)
        {
            Common.Utils.WriteLine(ex.Message, 3);
        }
    }
    public void SendMessage(ActionData actionData)
    {
        try
        {
            var sent = TelegramManager.BotClient?.SendTextMessageAsync(
                actionData.Chat.Id,
                actionData.Text,
                actionData.ParseMode,
                disableWebPagePreview: actionData.DisableWebPagePreview,
                disableNotification: actionData.DisableNotification,
                replyToMessageId: actionData.ReferenceMessageId,
                replyMarkup: actionData.ReplyMarkup,
                cancellationToken: TelegramManager.Cts.Token
            ).Result;

            if (sent == null)
            {
                // TODO - log

                return;
            }

            switch (actionData.PostSentAction)
            {
                case ActionData.PostSentActions.Pin:
                    TelegramManager.BotClient?.PinChatMessageAsync(actionData.Chat.Id, sent.MessageId);
                    break;
                case ActionData.PostSentActions.Destroy:
                    Task.Run(() =>
                    {
                        System.Threading.Thread.Sleep(1000 * actionData.AutoDestroyTimeInSeconds);
                        TelegramManager.BotClient?.DeleteMessageAsync(actionData.Chat.Id, sent.MessageId);
                    });
                    break;
                case ActionData.PostSentActions.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception ex)
        {
            Common.Utils.WriteLine(ex.Message, 3);
        }
    }
    public void DeleteMessage(ActionData actionData)
    {
        if (actionData.ReferenceMessageId is null) return;
        try
        {
            TelegramManager.BotClient?
                .DeleteMessageAsync(actionData.Chat.Id, (int)actionData.ReferenceMessageId, TelegramManager.Cts.Token);
        }
        catch (Exception ex)
        {
            Common.Utils.WriteLine(ex.Message, 3);
        }
    }
    public void PinMessage(ActionData actionData)
    {
        if (actionData.ReferenceMessageId is null) return;
        try
        {
            TelegramManager.BotClient?
                .PinChatMessageAsync(actionData.Chat.Id,
                    (int)actionData.ReferenceMessageId,
                    actionData.DisableNotification,
                    TelegramManager.Cts.Token);
        }
        catch (Exception ex)
        {
            Common.Utils.WriteLine(ex.Message, 3);
        }
    }
}