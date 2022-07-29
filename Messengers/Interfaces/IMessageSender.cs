using System.Collections.Generic;
using System.Threading.Tasks;

namespace Adp.Messengers.Interfaces;

public interface IMessageSender
{
    void Start(IMessageReceiver receiver);
    Task SendOptions(ReplyInfo replyInfo, string message, List<string> options);
    Task SendMessage(ReplyInfo replyInfo, string message);
}