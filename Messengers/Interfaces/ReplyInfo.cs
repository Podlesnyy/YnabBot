namespace Adp.Messengers.Interfaces;

public class ReplyInfo
{
    public ReplyInfo(string userId, string chatId, string messageId)
    {
        UserId = userId;
        ChatId = chatId;
        MessageId = messageId;
    }

    public string UserId { get; }
    public string ChatId { get; }
    public string MessageId { get; }
}