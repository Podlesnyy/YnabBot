using System.Collections.Generic;

namespace Adp.Messengers.Interfaces;

public interface IMessageSender
{
    void Start(IMessageReceiver receiver);
    void SendOptions(ReplyInfo replyInfo, string message, List<string> options);
    void SendMessage(ReplyInfo replyInfo, string message);
}