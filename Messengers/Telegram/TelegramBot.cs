using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adp.Messengers.Interfaces;
using Microsoft.Extensions.Configuration;
using NLog;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Adp.Messengers.Telegram;

public sealed class TelegramBot( IConfiguration configuration ) : IMessageSender
{
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private TelegramBotClient botClient;
    private CancellationTokenSource cts;
    private IMessageReceiver messageReceiver;

    public async Task Start( IMessageReceiver receiver )
    {
        logger.Info( "Telegram bot initializing " );
        messageReceiver = receiver;

        var useProxy = configuration.GetValue< bool >( "YNABBOT_PROXY_USE" );
        logger.Info( $"Use proxy = {useProxy}" );
        var token = configuration.GetValue< string >( "YNABBOT_TELEGRAM_TOKEN" );
        if ( useProxy )
        {
            var host = configuration.GetValue< string >( "YNABBOT_PROXY_HOST" );
            logger.Info( $"host = {host}" );
            var port = configuration.GetValue< int >( "YNABBOT_PROXY_PORT" );
            logger.Info( $"port = {port}" );
            botClient = new TelegramBotClient( token );
        }
        else
            botClient = new TelegramBotClient( token );

        var me = await botClient.GetMe();
        logger.Info( $"Hello, World! I am user {me.Id} and my name is {me.FirstName}." );

        cts = new CancellationTokenSource();

        // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
        var receiverOptions = new ReceiverOptions { AllowedUpdates = [], DropPendingUpdates = true };

        botClient.StartReceiving( ( _, update, _ ) => HandleUpdateAsync( update ), ( _, exception, _ ) => PollingErrorHandler( exception ), receiverOptions, cts.Token );
    }

    public async Task SendOptions( ReplyInfo replyInfo, string message, List< string > options )
    {
        try
        {
            logger.Info( $"Reply with keyboard in chat {replyInfo.ChatId} message: {string.Join( ";", options )} on {replyInfo.MessageId}" );
            var rkm = new ReplyKeyboardMarkup( options.Select( static item => new KeyboardButton[] { item } ).ToArray() );
            await botClient.SendMessage( Convert.ToInt64( replyInfo.ChatId ), message, replyMarkup: rkm, replyParameters: new ReplyParameters { MessageId = Convert.ToInt32( replyInfo.MessageId ) } );
        }
        catch ( Exception e )
        {
            logger.Error( e );
        }
    }

    public async Task SendMessage( ReplyInfo replyInfo, string message )
    {
        try
        {
            logger.Info( $"Reply in chat {Convert.ToInt64( replyInfo.ChatId )} message: {message} on {Convert.ToInt32( replyInfo.MessageId )}" );

            if ( string.IsNullOrEmpty( message ) )
                return;

            await botClient.SendMessage( Convert.ToInt64( replyInfo.ChatId ), message, replyParameters: new ReplyParameters { MessageId = Convert.ToInt32( replyInfo.MessageId ) }, replyMarkup: new ReplyKeyboardRemove(),
                parseMode: ParseMode.Html );
        }
        catch ( Exception e )
        {
            logger.Error( e );
        }
    }

    private async Task HandleUpdateAsync( Update update )
    {
        try
        {
            if ( update.Type == UpdateType.Message )
                await Bot_OnMessage( update.Message );
            else
                logger.Error( "Unknown type {type}", update.Type );
        }
        catch ( Exception exception )
        {
            await PollingErrorHandler( exception );
        }
    }

    private Task PollingErrorHandler( Exception exception )
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        logger.Error( errorMessage );
        return Task.CompletedTask;
    }

    private async Task Bot_OnMessage( Message message )
    {
        try
        {
            if ( message.Document == null )
            {
                logger.Info( $"Received a text message in chat {message.Chat.Id} message: {message.Text}" );
                messageReceiver.OnMessage( new ReplyInfo( message.From!.Id.ToString(), message.Chat.Id.ToString(), message.MessageId.ToString() ), message.Text );
            }
            else
            {
                logger.Info( $"Received a document message in chat {message.Chat.Id} message: {message.Document.FileName}" );

                var file = botClient.GetFile( message.Document.FileId ).Result;
                logger.Info( $"File saved: {file.FilePath}" );
                await using var stream = new MemoryStream();
                await botClient.GetInfoAndDownloadFile( message.Document.FileId, stream );
                messageReceiver.OnFileMessage( new ReplyInfo( message.From!.Id.ToString(), message.Chat.Id.ToString(), message.MessageId.ToString() ), message.Document.FileName, stream );
            }
        }
        catch ( Exception ex )
        {
            logger.Error( ex );

            try
            {
                await botClient.SendMessage( message.Chat.Id, ex.Message, replyParameters: new ReplyParameters { MessageId = Convert.ToInt32( message.MessageId ) }, replyMarkup: new ReplyKeyboardRemove() );
            }
            catch ( Exception exception )
            {
                logger.Error( exception );
            }
        }
    }
}