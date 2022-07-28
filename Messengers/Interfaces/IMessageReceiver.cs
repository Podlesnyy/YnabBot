using System.IO;

namespace Adp.Messengers.Interfaces;

public interface IMessageReceiver
{
    void OnMessage(ReplyInfo replyInfo, string message);
    void OnFileMessage(ReplyInfo replyInfo, string fileName, MemoryStream fileContent);
}