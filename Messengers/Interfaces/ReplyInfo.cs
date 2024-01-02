namespace Adp.Messengers.Interfaces;

public sealed class ReplyInfo( string userId, string chatId, string messageId )
{
    public string UserId { get; } = userId;
    public string ChatId { get; } = chatId;
    public string MessageId { get; } = messageId;
}