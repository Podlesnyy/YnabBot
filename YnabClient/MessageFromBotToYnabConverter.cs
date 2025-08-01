using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adp.Banks.Interfaces;
using Adp.Messengers.Interfaces;
using Adp.Persistent;
using Adp.YnabClient.Ynab;
using Domain;
using Microsoft.EntityFrameworkCore;
using NLog;
using YamlDotNet.Serialization;

namespace Adp.YnabClient;

public sealed class MessageFromBotToYnabConverter : IMessageReceiver, IDbSaver
{
    private readonly IBank[] banks;
    private readonly YnabDbContext dbContext;
    private readonly Dictionary< string, User > dicYnabUsers = new();
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly IMessageSender messageSender;
    private readonly Oauth oauth;
    private readonly List< Domain.User > users;

    public MessageFromBotToYnabConverter( IMessageSender messageSender, IBank[] banks, YnabDbContext dbContext, Oauth oauth )
    {
        this.messageSender = messageSender;
        this.banks = banks;
        this.dbContext = dbContext;

        this.oauth = oauth;
        users = dbContext.Users.Include( static item => item.BankAccountToYnabAccounts ).ThenInclude( static item => item.YnabAccount ).Include( static item => item.DefaultYnabAccount ).Include( static item => item.Access ).ToList();

        Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );
    }

    public void Save()
    {
        dbContext.SaveChanges();
    }

    public void OnMessage( ReplyInfo replyInfo, string message )
    {
        /* Bot Commands
        auth-Authorize bot to your YNAB account
        setdefaultbudget-Set your default budget and account for quick transactions adding
        listynabaccounts-List all your YNAB budgets and accounts
        listmatching-List bank account matching YNAB accounts
        removemyinfo-Remove all your stored info
        */

        switch ( message )
        {
            case "/start":
                messageSender.SendMessage( replyInfo,
                                           $"Welcome to bot for YNAB!{Environment.NewLine}This bot is not officially supported by YNAB in any way. Use of this bot could introduce problems into your budget that YNAB, through its official support channels, will not be able to troubleshoot or fix. Please use at your own risk!{Environment.NewLine}You will acknowledge this by using the /auth command" );
                break;
            case "/auth":
                GetUser( replyInfo ).AuthCommand( replyInfo );
                break;
            case "/setdefaultbudget":
                GetUser( replyInfo ).StartSetDefaultBudget( replyInfo );
                break;
            case "/listynabaccounts":
                GetUser( replyInfo ).ListYnabAccountsCommand( replyInfo );
                break;
            case "/listmatching":
                GetUser( replyInfo ).ListBankAccountsCommand( replyInfo );
                break;
            case "/removemyinfo":
                RemoveUser( replyInfo );
                break;
            default:
                GetUser( replyInfo ).OnMessage( replyInfo, message );
                break;
        }
    }

    public void OnFileMessage( ReplyInfo replyInfo, string fileName, MemoryStream fileContent )
    {
        if ( fileName == "settings.yaml" )
        {
            var content = Encoding.UTF8.GetString( fileContent.ToArray() );

            var bankAccountToYnabAccounts = new Deserializer().Deserialize< List< BankAccountToYnabAccount > >( content );
            GetUser( replyInfo ).AddBankAccounts( replyInfo, bankAccountToYnabAccounts );
            return;
        }

        var transactions = ParseFile( fileName, fileContent );
        if ( transactions.Count == 0 )
            messageSender.SendMessage( replyInfo, "Cant find any transaction in file" );

        GetUser( replyInfo ).AddTransactionsFromFile( replyInfo, transactions );
    }

    public void Init()
    {
        messageSender.Start( this );
    }

    private void RemoveUser( ReplyInfo replyInfo )
    {
        var dbUser = users.FirstOrDefault( user => user.MessengerUserId == replyInfo.UserId );
        if ( dbUser != null )
        {
            dicYnabUsers.Remove( replyInfo.UserId );
            dbContext.Users.Remove( dbUser );
            users.Remove( dbUser );
            dbContext.SaveChanges();
        }

        SendByeByeMessage( replyInfo );
    }

    private void SendByeByeMessage( ReplyInfo replyInfo )
    {
        messageSender.SendMessage( replyInfo, "All your account information removed. Thank you for your time. See ya!" );
    }

    private List< Transaction > ParseFile( string fileName, MemoryStream stream )
    {
        var bank = banks.FirstOrDefault( item => item.IsItYour( fileName ) );
        if ( bank != null )
            return bank.Parse( stream ) ?? bank.Parse( Content( bank.FileEncoding ) );

        logger.Info( "Не могу найти банк по имени файла: " + fileName );
        return [ ];

        string Content( string enc ) => Encoding.GetEncoding( enc ).GetString( stream.ToArray() );
    }

    private User GetUser( ReplyInfo replyInfo ) => dicYnabUsers.ContainsKey( replyInfo.UserId ) ? dicYnabUsers[ replyInfo.UserId ] : CreateAndInitUser( replyInfo );

    private User CreateAndInitUser( ReplyInfo replyInfo )
    {
        var user = new User( messageSender, this, oauth );
        dicYnabUsers[ replyInfo.UserId ] = user;

        user.Init( replyInfo, GetDbUser( replyInfo.UserId ) );
        return user;
    }

    private Domain.User GetDbUser( string messengerUserId )
    {
        var dbUser = users.FirstOrDefault( user => user.MessengerUserId == messengerUserId );
        if ( dbUser != null )
            return dbUser;

        dbUser = new Domain.User { MessengerUserId = messengerUserId, BankAccountToYnabAccounts = new List< BankAccountToYnabAccount >(), DefaultYnabAccount = new YnabAccount(), Access = new YnabAccess() };
        dbContext.Add( dbUser );

        dbContext.SaveChanges();

        return dbUser;
    }
}