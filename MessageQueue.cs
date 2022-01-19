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
using Unifiedban.Next.Common.Telegram;

namespace Unifiedban.Next.Terminal.Telegram;

internal class MessageQueue
{
    private long TelegramChatId { get; set; }
    private DateTime FirstMessageUtc { get; set; }
    private int LastMinuteMessagesCount { get; set; }
    private short MaxMessagePerMinute { get; set; }
    private System.Timers.Timer QueueTimer { get; set; }
    private bool _handlingInProgress;
    public Queue<ActionRequest> Queue { get; set; } = new();

    public MessageQueue(long telegramChatId, short maxMsgPerMinute)
    {
        TelegramChatId = telegramChatId;
        MaxMessagePerMinute = maxMsgPerMinute;

        FirstMessageUtc = DateTime.UtcNow;

        QueueTimer = new System.Timers.Timer(100);
        QueueTimer.Elapsed += QueueTimer_Elapsed;
        QueueTimer.AutoReset = true;
        QueueTimer.Start();
    }

    private void QueueTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Check if there is any message in queue
        if (Queue.Count == 0)
            return;
        if (_handlingInProgress)
            return;

        _handlingInProgress = true;
        var doResetCount = false;
        while (DateTime.UtcNow
                   .Subtract(FirstMessageUtc)
                   .Minutes < 1
               && LastMinuteMessagesCount >= MaxMessagePerMinute)
        {
            System.Threading.Thread.Sleep(100);
            doResetCount = true;
        }

        ProcessAction(Queue.Dequeue());

        // Reset counter and time if we waited previously
        if (doResetCount)
        {
            LastMinuteMessagesCount = 1;
            FirstMessageUtc = DateTime.UtcNow;
        }
        else
        {
            LastMinuteMessagesCount += 1;
        }

        _handlingInProgress = false;
    }

    private void ProcessAction(ActionRequest request)
    {
        switch (request.Action)
        {
            case ActionRequest.Actions.LeaveChat:
                new ActionHandler().LeaveChat(request.Message);
                break;
            case ActionRequest.Actions.SendText:
                new ActionHandler().SendMessage(request.Message);
                break;
            case ActionRequest.Actions.SendImage:
                break;
            case ActionRequest.Actions.SendVideo:
                break;
            case ActionRequest.Actions.SendGif:
                break;
            case ActionRequest.Actions.SendAudio:
                break;
            case ActionRequest.Actions.DeleteMessage:
                new ActionHandler().DeleteMessage(request.Message);
                break;
            case ActionRequest.Actions.EditMessage:
                break;
            case ActionRequest.Actions.EditMessageCaption:
                break;
            case ActionRequest.Actions.KickUser:
                break;
            case ActionRequest.Actions.BanUser:
                break;
            case ActionRequest.Actions.ApplyUserPermissions:
                break;
            case ActionRequest.Actions.ApplyChatPermissions:
                break;
            case ActionRequest.Actions.ExportChatInvite:
                break;
            case ActionRequest.Actions.CreateChatInvite:
                break;
            case ActionRequest.Actions.RevokeChatInvite:
                break;
            case ActionRequest.Actions.ApproveChatJoinRequest:
                break;
            case ActionRequest.Actions.DeclineChatJoinRequest:
                break;
            case ActionRequest.Actions.PinMessage:
                new ActionHandler().PinMessage(request.Message);
                break;
            case ActionRequest.Actions.UnpinMessage:
                break;
            case ActionRequest.Actions.UnpinAllMessages:
                break;
        }
    }
}