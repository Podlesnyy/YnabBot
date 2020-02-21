using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Adp.Messengers.Interfaces;
using Microsoft.Extensions.Configuration;
using NLog;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Adp.Messengers.Telegram
{
    public sealed class TelegramBot : IMessageSender
    {
        private readonly IConfiguration configuration;
        private readonly Logger logger = LogManager.GetCurrentClassLogger();
        private TelegramBotClient botClient;
        private IMessageReceiver messageReceiver;

        public TelegramBot(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public void Start(IMessageReceiver receiver)
        {
            logger.Info("Telegram bot initializing ");

            var useProxy = configuration.GetValue<bool>("YNABBOT_PROXY_USE");
            logger.Info($"Use proxy = {useProxy}");
            var token = configuration.GetValue<string>("YNABBOT_TELEGRAM_TOKEN");
            if (useProxy)
            {
                var host = configuration.GetValue<string>("YNABBOT_PROXY_HOST");
                logger.Info($"host = {host}");
                var port = configuration.GetValue<int>("YNABBOT_PROXY_PORT");
                logger.Info($"port = {port}");
                botClient = new TelegramBotClient(token, new WebProxy(host, port));
            }
            else
            {
                botClient = new TelegramBotClient(token);
            }

            var me = botClient.GetMeAsync().Result;
            logger.Info($"Hello, World! I am user {me.Id} and my name is {me.FirstName}.");

            botClient.OnMessage += Bot_OnMessage;
            messageReceiver = receiver;
            botClient.StartReceiving();
        }

        public async void SendOptions(ReplyInfo replyInfo, string message, List<string> options)
        {
            try
            {
                logger.Info($"Reply with keyboard in chat {replyInfo.ChatId} message: {string.Join(";", options)} on {replyInfo.MessageId}");
                var rkm = new ReplyKeyboardMarkup {Keyboard = options.Select(item => new KeyboardButton[] {item}).ToArray()};

                await botClient.SendTextMessageAsync(Convert.ToInt64(replyInfo.ChatId), message, replyMarkup: rkm, replyToMessageId: Convert.ToInt32(replyInfo.MessageId));
            }
            catch (Exception e)
            {
                logger.Error(e);
            }
        }

        public async void SendMessage(ReplyInfo replyInfo, string message)
        {
            try
            {
                logger.Info($"Reply in chat {Convert.ToInt64(replyInfo.ChatId)} message: {message} on {Convert.ToInt32(replyInfo.MessageId)}");

                if (string.IsNullOrEmpty(message))
                    return;

                await botClient.SendTextMessageAsync(Convert.ToInt64(replyInfo.ChatId), message, replyToMessageId: Convert.ToInt32(replyInfo.MessageId), replyMarkup: new ReplyKeyboardRemove(), parseMode:ParseMode.Html);
            }
            catch (Exception e)
            {
                logger.Error(e);
            }
        }

        private async void Bot_OnMessage(object sender, MessageEventArgs e)
        {
            try
            {
                if (e.Message.Document == null)
                {
                    logger.Info($"Received a text message in chat {e.Message.Chat.Id} message: {e.Message.Text}");
                    messageReceiver.OnMessage(new ReplyInfo(e.Message.From.Id.ToString(), e.Message.Chat.Id.ToString(), e.Message.MessageId.ToString()), e.Message.Text);
                }
                else
                {
                    logger.Info($"Received a document message in chat {e.Message.Chat.Id} message: {e.Message.Document.FileName}");

                    var file = botClient.GetFileAsync(e.Message.Document.FileId).Result;
                    logger.Info($"File saved: {file.FilePath}");
                    await using var stream = new MemoryStream();
                    await botClient.GetInfoAndDownloadFileAsync(e.Message.Document.FileId, stream);
                    messageReceiver.OnFileMessage(new ReplyInfo(e.Message.From.Id.ToString(), e.Message.Chat.Id.ToString(), e.Message.MessageId.ToString()), e.Message.Document.FileName, stream);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);

                try
                {
                    await botClient.SendTextMessageAsync(e.Message.Chat.Id, ex.Message, replyToMessageId: Convert.ToInt32(e.Message.MessageId), replyMarkup: new ReplyKeyboardRemove());
                }
                catch (Exception exception)
                {
                    logger.Error(exception);
                }
            }
        }
    }
}