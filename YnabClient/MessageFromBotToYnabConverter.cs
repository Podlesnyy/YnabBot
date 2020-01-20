using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adp.Banks.Interfaces;
using Adp.Messengers.Interfaces;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NLog;
using Persistent;
using YamlDotNet.Serialization;

namespace Adp.YnabClient
{
    public sealed class MessageFromBotToYnabConverter : IMessageReceiver, IDbSaver
    {
        private readonly IBank[] banks;
        private readonly YnabDbContext dbContext;
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IMessageSender messageSender;
        private readonly Dictionary<string, User> dicYnabUsers = new Dictionary<string, User>();
        private List<MessengerUser> users;
        private string ynabClientSecret;
        private string ynabClientID;

        public MessageFromBotToYnabConverter(IMessageSender messageSender, IBank[] banks, YnabDbContext dbContext, IConfiguration configuration )
        {
            this.messageSender = messageSender;
            this.banks = banks;
            this.dbContext = dbContext;
            ynabClientID = configuration.GetValue<string>("YNAB_CLIENT_ID");
            ynabClientSecret = configuration.GetValue<string>("YNAB_CLIENT_SECRET");
            users = dbContext.Users.Include(item => item.BankAccountToYnabAccounts).ThenInclude(item=>item.YnabAccount).Include(item=>item.DefaultYnabAccount).ToList();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public void OnMessage(ReplyInfo replyInfo, string message)
        {
            /* Bot Commands
            auth-Authorize bot to your YNAB account
            setdefaultbudget-Set your default budget and account for quick transactions adding
            listynabaccounts-List all your YNAB budgets and accounts
            listmatching-List bank account matching YNAB accounts
            removemyinfo-Remove all your stored info
            */
            switch (message)
            {
                case "/start":
                    messageSender.SendMessage(replyInfo, "Welcome to bot for YNAB!");
                    break;
                case "/auth":
                    GetUser(replyInfo).AuthCommand(replyInfo);
                    break;
                case "/setdefaultbudget":
                    GetUser(replyInfo).StartSetDefaultBudget(replyInfo);
                    break;
                case "/listynabaccounts":
                    GetUser(replyInfo).ListYnabAccountsCommand(replyInfo);
                    break;
                case "/listmatching":
                    GetUser(replyInfo).ListBankAccountsCommand(replyInfo);
                    break;
                case "/removemyinfo":
                    //TODO
                    break;
                default:
                    GetUser(replyInfo).OnMessage(replyInfo, message);
                    break;
            }
        }

        public void OnFileMessage(ReplyInfo replyInfo, string fileName, MemoryStream fileContent)
        {
            if (fileName == "settings.yaml")
            {
                var content = Encoding.UTF8.GetString(fileContent.ToArray());

                var bankAccountToYnabAccounts = new Deserializer().Deserialize<List<BankAccountToYnabAccount>>(content);
                GetUser(replyInfo).AddBankAccounts(replyInfo, bankAccountToYnabAccounts);
                return;
            }

            var transactions = ParseFile(fileName, fileContent);
            if (transactions.Count == 0)
                messageSender.SendMessage(replyInfo, "Cant find any transaction in file");

            GetUser(replyInfo).AddTransactionsFromFile(replyInfo, transactions);
        }

        public void Init()
        {
            messageSender.Start(this);
        }

        private List<Transaction> ParseFile(string fileName, MemoryStream stream)
        {
            string Content(string enc)
            {
                return Encoding.GetEncoding(enc).GetString(stream.ToArray());
            }

            var bank = banks.FirstOrDefault(item => item.IsItYour(fileName));
            if (bank != null) return bank.Parse(Content(bank.FileEncoding));

            logger.Info("Не могу найти банк по имени файла: " + fileName);
            return new List<Transaction>();
        }

        private User GetUser(ReplyInfo replyInfo)
        {
            return dicYnabUsers.ContainsKey(replyInfo.UserId) ? dicYnabUsers[replyInfo.UserId] : CreateAndInitUser(replyInfo);
        }

        private User CreateAndInitUser(ReplyInfo replyInfo)
        {
            var user = new User(messageSender, this, ynabClientID, ynabClientSecret);
            dicYnabUsers[replyInfo.UserId] = user;

            user.Init(replyInfo, GetDbUser(replyInfo.UserId));
            return user;
        }

        private MessengerUser GetDbUser( string messengerUserId )
        {
            var dbUser = users.FirstOrDefault(user => user.MessengerUserId == messengerUserId);
            if (dbUser != null) return dbUser;

            dbUser = new MessengerUser {MessengerUserId = messengerUserId, BankAccountToYnabAccounts = new List<BankAccountToYnabAccount>(), DefaultYnabAccount = new YnabAccount()};
            dbContext.Add(dbUser);
            
            dbContext.SaveChanges();

            return dbUser;
        }

        public void Save()
        {
            dbContext.SaveChanges();
        }
    }
}