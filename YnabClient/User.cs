using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using Adp.Messengers.Interfaces;
using Adp.YnabClient.Ynab;
using Domain;
using NLog;
using Stateless;

namespace Adp.YnabClient;

internal sealed class User
{
    private readonly IDbSaver dbSaver;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly StateMachine< State, Trigger > machine;
    private readonly IMessageSender messageSender;
    private readonly Oauth oauth;

    private readonly string transactionHelp = $"Example transaction:{Environment.NewLine}Apples 7.47";
    private Account account;

    private StateMachine< State, Trigger >.TriggerWithParameters< ReplyInfo > applyUserSettingsTrigger;
    private Domain.User dbUser;
    private StateMachine< State, Trigger >.TriggerWithParameters< ReplyInfo > listAccounts;

    private StateMachine< State, Trigger >.TriggerWithParameters< ReplyInfo, List< Transaction > >
        onAddTransactionFromFileTrigger;

    private StateMachine< State, Trigger >.TriggerWithParameters< ReplyInfo, string > onMessageTrigger;
    private StateMachine< State, Trigger >.TriggerWithParameters< ReplyInfo > setDefaultAccountTrigger;
    private StateMachine< State, Trigger >.TriggerWithParameters< ReplyInfo > setDefaultBudgetTrigger;
    private StateMachine< State, Trigger >.TriggerWithParameters< ReplyInfo > setReadyTrigger;
    private StateMachine< State, Trigger >.TriggerWithParameters< ReplyInfo > startAuthTrigger;

    public User( IMessageSender messageSender, IDbSaver dbSaver, Oauth oauth )
    {
        this.messageSender = messageSender;
        this.dbSaver = dbSaver;
        this.oauth = oauth;

        machine = new StateMachine< State, Trigger >( State.Unknown );
        SetupTriggers();
        SetupStateUnknown();
        SetupStateAuthorizing();
        SetupStateEnteringDefaultBudget();
        SetupStateEnteringDefaultAccount();
        SetupStateReady();
        SetupStateApplyingSettings();
    }

    private string AccessToken
    {
        get
        {
            if ( DateTime.Now - dbUser.Access.CreatedAt > TimeSpan.FromSeconds( dbUser.Access.ExpiresIn ) - TimeSpan.FromMinutes( 5 ) )
                RefreshToken();

            return dbUser.Access.AccessToken;
        }
    }

    public void AuthCommand( in ReplyInfo replyInfo )
    {
        machine.Fire( startAuthTrigger, replyInfo );
    }

    public void OnMessage( in ReplyInfo replyInfo, string message )
    {
        machine.Fire( onMessageTrigger, replyInfo, message );
    }

    public void StartSetDefaultBudget( in ReplyInfo replyInfo )
    {
        machine.Fire( setDefaultBudgetTrigger, replyInfo );
    }

    public void AddTransactionsFromFile( in ReplyInfo replyInfo, List< Transaction > transactions )
    {
        machine.Fire( onAddTransactionFromFileTrigger, replyInfo, transactions );
    }

    private static DateTime UnixTimeStampToDateTime( int unixTimeStamp )
    {
        // Unix timestamp is seconds past epoch
        var dtDateTime = new DateTime( 1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc );
        dtDateTime = dtDateTime.AddSeconds( unixTimeStamp ).ToLocalTime();
        return dtDateTime;
    }

    public void Init( ReplyInfo replyInfo, Domain.User user )
    {
        dbUser = user;
        machine.Fire( applyUserSettingsTrigger, replyInfo );
    }

    public void ListYnabAccountsCommand( ReplyInfo replyInfo )
    {
        machine.Fire( listAccounts, replyInfo );
    }

    public void ReloadYnabAccountsCommand( ReplyInfo replyInfo )
    {
        if ( string.IsNullOrWhiteSpace( dbUser.Access.AccessToken ) &&
             string.IsNullOrWhiteSpace( dbUser.Access.RefreshToken ) )
        {
            messageSender.SendMessage( replyInfo, "Please authorize bot with /auth before reloading accounts" );
            machine.Fire( Trigger.Unknown );
            return;
        }

        if ( !TryLoadBudgets( replyInfo ) )
            return;

        var mappingsSnapshot = dbUser.BankAccountToYnabAccounts.ToList();
        var valid = new List< BankAccountToYnabAccount >();
        var invalid = new List< BankAccountToYnabAccount >();

        foreach ( var mapping in mappingsSnapshot )
        {
            if ( IsValidMapping( mapping ) )
                valid.Add( mapping );
            else
                invalid.Add( mapping );
        }

        dbUser.BankAccountToYnabAccounts.Clear();
        dbUser.BankAccountToYnabAccounts.AddRange( valid );
        dbSaver.Save();

        if ( invalid.Count > 0 )
        {
            var invalidList = string.Join( Environment.NewLine,
                                           invalid.Select( static item =>
                                                               $"{item.BankAccount} - {item.YnabAccount?.Budget}\\{item.YnabAccount?.Account}" ) );
            messageSender.SendMessage( replyInfo,
                                       "Some mappings are invalid and were removed. Please check budget/account names:" +
                                       Environment.NewLine + invalidList );
        }

        var budgetSummary =
            account.DicAccounts.Keys.FirstOrDefault( item => item.Name == dbUser.DefaultYnabAccount.Budget );
        if ( budgetSummary == null )
        {
            machine.Fire( setDefaultBudgetTrigger, replyInfo );
            return;
        }

        var budgetAccount = account.DicAccounts[ budgetSummary ]
                                   .FirstOrDefault( item => item.Name == dbUser.DefaultYnabAccount.Account );
        if ( budgetAccount == null )
        {
            machine.Fire( setDefaultAccountTrigger, replyInfo );
            return;
        }

        messageSender.SendMessage( replyInfo, "YNAB accounts reloaded" );
        ListBankAccountsCommand( replyInfo );
    }

    public void UpdateMappingsCommand( ReplyInfo replyInfo )
    {
        if ( string.IsNullOrWhiteSpace( dbUser.Access.AccessToken ) &&
             string.IsNullOrWhiteSpace( dbUser.Access.RefreshToken ) )
        {
            messageSender.SendMessage( replyInfo, "Please authorize bot with /auth before updating mappings" );
            machine.Fire( Trigger.Unknown );
            return;
        }

        if ( !TryLoadBudgets( replyInfo ) )
            return;

        var mappingsFromNotes = BuildMappingsFromYnabAccountNotes();
        var existingByBankAccount = new Dictionary< string, BankAccountToYnabAccount >( StringComparer.Ordinal );
        foreach ( var existing in dbUser.BankAccountToYnabAccounts )
        {
            var normalizedBankAccount = NormalizeBankAccount( existing.BankAccount );
            if ( normalizedBankAccount == null )
                continue;

            if ( existingByBankAccount.ContainsKey( normalizedBankAccount ) )
            {
                logger.Warn( $"Duplicate existing mapping '{normalizedBankAccount}' found in DB. Keeping first mapping." );
                continue;
            }

            existingByBankAccount[ normalizedBankAccount ] = existing;
        }

        var added = 0;
        var updated = 0;
        var unchanged = 0;
        foreach ( var mappingFromNotes in mappingsFromNotes.Values )
        {
            var bankAccount = NormalizeBankAccount( mappingFromNotes.BankAccount );
            if ( bankAccount == null )
                continue;

            if ( existingByBankAccount.TryGetValue( bankAccount, out var existing ) )
            {
                existing.YnabAccount ??= new YnabAccount();
                var isChanged = !string.Equals( existing.YnabAccount.Budget, mappingFromNotes.YnabAccount?.Budget,
                                                StringComparison.Ordinal ) ||
                                !string.Equals( existing.YnabAccount.Account, mappingFromNotes.YnabAccount?.Account,
                                                StringComparison.Ordinal );

                existing.BankAccount = bankAccount;
                existing.YnabAccount.Budget = mappingFromNotes.YnabAccount?.Budget;
                existing.YnabAccount.Account = mappingFromNotes.YnabAccount?.Account;

                if ( isChanged )
                    updated++;
                else
                    unchanged++;

                continue;
            }

            dbUser.BankAccountToYnabAccounts.Add( new BankAccountToYnabAccount
            {
                BankAccount = bankAccount,
                YnabAccount = new YnabAccount
                {
                    Budget = mappingFromNotes.YnabAccount?.Budget,
                    Account = mappingFromNotes.YnabAccount?.Account,
                },
            } );

            added++;
        }

        dbSaver.Save();

        messageSender.SendMessage( replyInfo,
                                   $"Mappings updated from YNAB Account Notes. Processed {mappingsFromNotes.Count} mapping(s): added {added}, updated {updated}, unchanged {unchanged}. Existing mappings from settings.yaml were kept." );
        ListBankAccountsCommand( replyInfo );
    }

    public void AddBankAccounts( ReplyInfo replyInfo, IEnumerable< BankAccountToYnabAccount > listSynonyms )
    {
        if ( string.IsNullOrWhiteSpace( dbUser.Access.AccessToken ) &&
             string.IsNullOrWhiteSpace( dbUser.Access.RefreshToken ) )
        {
            messageSender.SendMessage( replyInfo, "Please authorize bot with /auth before uploading settings.yaml" );
            machine.Fire( Trigger.Unknown );
            return;
        }

        if ( !TryLoadBudgets( replyInfo ) )
            return;

        var mappings = listSynonyms?.ToList() ?? new List< BankAccountToYnabAccount >();
        var valid = new List< BankAccountToYnabAccount >();
        var invalid = new List< BankAccountToYnabAccount >();

        foreach ( var mapping in mappings )
        {
            if ( IsValidMapping( mapping ) )
                valid.Add( mapping );
            else
                invalid.Add( mapping );
        }

        dbUser.BankAccountToYnabAccounts.Clear();
        dbUser.BankAccountToYnabAccounts.AddRange( valid );
        dbSaver.Save();

        if ( invalid.Count > 0 )
        {
            var invalidList = string.Join( Environment.NewLine,
                                           invalid.Select( static item =>
                                                               $"{item.BankAccount} - {item.YnabAccount?.Budget}\\{item.YnabAccount?.Account}" ) );
            messageSender.SendMessage( replyInfo,
                                       "Some mappings are invalid and were skipped. Please check budget/account names:" +
                                       Environment.NewLine + invalidList );
        }

        ListBankAccountsCommand( replyInfo );
    }

    public void ListBankAccountsCommand( ReplyInfo replyInfo )
    {
        messageSender.SendMessage( replyInfo,
                                   dbUser.BankAccountToYnabAccounts.Count == 0
                                       ? "You haven't any bank accounts linked to YNAB accounts "
                                       : string.Join( Environment.NewLine,
                                                      dbUser.BankAccountToYnabAccounts.Select( static item =>
                                                                                                   $"{item.BankAccount} - {item.YnabAccount.Budget}\\{item.YnabAccount.Account}" ) ) );
    }

    private void RefreshToken()
    {
        var response = oauth.GetRefreshedAccessToken( dbUser.Access.RefreshToken );
        dbUser.Access.AccessToken = response.access_token;
        dbUser.Access.RefreshToken = response.refresh_token;
        dbUser.Access.CreatedAt = UnixTimeStampToDateTime( response.created_at );
        dbUser.Access.ExpiresIn = response.expires_in;
        dbUser.Access.Scope = response.scope;
        dbUser.Access.TokenType = response.token_type;

        dbSaver.Save();
    }

    private void SetupStateApplyingSettings()
    {
        machine.Configure( State.ApplyingSettings )
               .OnEntryFrom( applyUserSettingsTrigger, ( replyInfo, _ ) => OnApplyUserSettings( replyInfo ) )
               .Permit( Trigger.Unknown, State.Unknown )
               .Permit( Trigger.StartAuth, State.Authorizing )
               .Permit( Trigger.SetDefaultBudget, State.EnteringDefaultBudget )
               .Permit( Trigger.SetDefaultAccount, State.EnteringDefaultAccount )
               .Permit( Trigger.SetReady, State.Ready );
    }

    private void SetupStateReady()
    {
        machine.Configure( State.Ready )
               .InternalTransition( onMessageTrigger, ( replyInfo, message, _ ) => OnEnteringTransaction( replyInfo, message ) )
               .InternalTransition( onAddTransactionFromFileTrigger,
                                    ( replyInfo, transactions, _ ) => OnAddTransactions( replyInfo, transactions ) )
               .InternalTransition( listAccounts, ( replyInfo, _ ) => ShowAccountList( replyInfo ) )
               .OnEntryFrom( setReadyTrigger,
                             ( replyInfo, _ ) => messageSender.SendMessage( replyInfo,
                                                                            $"Congratulations!{Environment.NewLine}Now you can add transaction directly to your YNAB account {dbUser.DefaultYnabAccount.Budget}/{dbUser.DefaultYnabAccount.Account}{Environment.NewLine}{transactionHelp}" ) )
               .Permit( Trigger.SetDefaultBudget, State.EnteringDefaultBudget )
               .Permit( Trigger.StartAuth, State.Authorizing )
               .Permit( Trigger.ApplyUserSettings, State.ApplyingSettings );
    }

    private void SetupStateEnteringDefaultAccount()
    {
        machine.Configure( State.EnteringDefaultAccount )
               .InternalTransition( onMessageTrigger, ( replyInfo, message, _ ) => OnDefaultAccount( replyInfo, message ) )
               .InternalTransition( onAddTransactionFromFileTrigger,
                                    ( replyInfo, transactions, _ ) => OnAddTransactions( replyInfo, transactions ) )
               .InternalTransition( listAccounts, ( replyInfo, _ ) => ShowAccountList( replyInfo ) )
               .PermitReentry( Trigger.SetDefaultAccount )
               .Permit( Trigger.SetDefaultBudget, State.EnteringDefaultBudget )
               .OnEntryFrom( setDefaultAccountTrigger,
                             replyInfo => messageSender.SendOptions( replyInfo,
                                                                     "Select account for adding transaction. You can easy change it later",
                                                                     account.DicAccounts[
                                                                                         account.DicAccounts.Keys.First( item => item.Name == dbUser.DefaultYnabAccount.Budget ) ]
                                                                            .Select( static item => item.Name )
                                                                            .ToList() ) )
               .Permit( Trigger.SetReady, State.Ready )
               .Permit( Trigger.StartAuth, State.Authorizing )
               .Permit( Trigger.ApplyUserSettings, State.ApplyingSettings );
    }

    private void SetupStateEnteringDefaultBudget()
    {
        machine.Configure( State.EnteringDefaultBudget )
               .InternalTransition( onMessageTrigger, ( replyInfo, message, _ ) => OnDefaultBudget( replyInfo, message ) )
               .InternalTransition( onAddTransactionFromFileTrigger,
                                    ( replyInfo, transactions, _ ) => OnAddTransactions( replyInfo, transactions ) )
               .InternalTransition( listAccounts, ( replyInfo, _ ) => ShowAccountList( replyInfo ) )
               .OnEntryFrom( setDefaultBudgetTrigger,
                             replyInfo => messageSender.SendOptions( replyInfo,
                                                                     "Select budget for adding transaction. You can easy change it later",
                                                                     account.DicAccounts.Keys.Select( static item => item.Name ).ToList() ) )
               .PermitReentry( Trigger.SetDefaultBudget )
               .Permit( Trigger.SetDefaultAccount, State.EnteringDefaultAccount )
               .Permit( Trigger.StartAuth, State.Authorizing )
               .Permit( Trigger.ApplyUserSettings, State.ApplyingSettings );
    }

    private void SetupStateAuthorizing()
    {
        var pleaseAuthorize =
            $"<a href=\"{oauth.GetAuthLink()}\">Please authorize bot</a> and click Start button after your return back to telegram";

        machine.Configure( State.Authorizing )
               .InternalTransition( onMessageTrigger, ( replyInfo, message, _ ) => OnAuthCode( replyInfo, message ) )
               .InternalTransition( onAddTransactionFromFileTrigger,
                                    ( replyInfo, transactions, _ ) => OnAddTransactions( replyInfo, transactions ) )
               .InternalTransition( listAccounts, ( replyInfo, _ ) => messageSender.SendMessage( replyInfo, pleaseAuthorize ) )
               .OnEntryFrom( startAuthTrigger, replyInfo => messageSender.SendMessage( replyInfo, pleaseAuthorize ) )
               .PermitReentry( Trigger.StartAuth )
               .Permit( Trigger.SetDefaultBudget, State.EnteringDefaultBudget )
               .Permit( Trigger.ApplyUserSettings, State.ApplyingSettings );
    }

    private void SetupStateUnknown()
    {
        const string youShouldRunSetupCommand = "At first you should run /auth command";
        machine.Configure( State.Unknown )
               .InternalTransition( onMessageTrigger,
                                    ( replyInfo, _, _ ) => messageSender.SendMessage( replyInfo, youShouldRunSetupCommand ) )
               .InternalTransition( setDefaultBudgetTrigger,
                                    ( replyInfo, _ ) => messageSender.SendMessage( replyInfo, youShouldRunSetupCommand ) )
               .InternalTransition( listAccounts,
                                    ( replyInfo, _ ) => messageSender.SendMessage( replyInfo, youShouldRunSetupCommand ) )
               .InternalTransition( onAddTransactionFromFileTrigger,
                                    ( replyInfo, _, _ ) => messageSender.SendMessage( replyInfo, youShouldRunSetupCommand ) )
               .Permit( Trigger.StartAuth, State.Authorizing )
               .Permit( Trigger.ApplyUserSettings, State.ApplyingSettings );
    }

    private void SetupTriggers()
    {
        onMessageTrigger = machine.SetTriggerParameters< ReplyInfo, string >( Trigger.OnMessage );
        setDefaultBudgetTrigger = machine.SetTriggerParameters< ReplyInfo >( Trigger.SetDefaultBudget );
        setDefaultAccountTrigger = machine.SetTriggerParameters< ReplyInfo >( Trigger.SetDefaultAccount );
        listAccounts = machine.SetTriggerParameters< ReplyInfo >( Trigger.ListAccounts );
        applyUserSettingsTrigger = machine.SetTriggerParameters< ReplyInfo >( Trigger.ApplyUserSettings );
        startAuthTrigger = machine.SetTriggerParameters< ReplyInfo >( Trigger.StartAuth );
        setReadyTrigger = machine.SetTriggerParameters< ReplyInfo >( Trigger.SetReady );
        onAddTransactionFromFileTrigger =
            machine.SetTriggerParameters< ReplyInfo, List< Transaction > >( Trigger.OnAddTransactionsFromFile );
    }

    private void ShowAccountList( ReplyInfo replyInfo )
    {
        var ret = new StringBuilder();
        foreach ( var (budgetSummary, accounts) in account.DicAccounts )
        {
            ret.AppendLine( budgetSummary.Name );
            foreach ( var budgetAccount in accounts.OrderBy( static item => item.Name ) )
                ret.AppendLine( $"----------------{budgetAccount.Name}" );
        }

        messageSender.SendMessage( replyInfo, ret.ToString() );
    }

    private void OnAddTransactions( in ReplyInfo replyInfo, IReadOnlyCollection< Transaction > transactions )
    {
        logger.Trace( $"Transactios count = {transactions.Count}" );

        foreach ( var transaction in transactions )
        {
            var accountSettings =
                dbUser.BankAccountToYnabAccounts.FirstOrDefault( item => item.BankAccount == transaction.BankAccount );
            if ( accountSettings == null )
            {
                messageSender.SendMessage( replyInfo,
                                           $"Cant find bank account {transaction.BankAccount} link to YNAB budget account" );
                ListBankAccountsCommand( replyInfo );
                return;
            }

            transaction.YnabBudget = accountSettings.YnabAccount.Budget;
            transaction.YnabAccount = accountSettings.YnabAccount.Account;
        }

        var result = account.AddTransactions( transactions, AccessToken );
        messageSender.SendMessage( replyInfo, result );
    }

    private IReadOnlyDictionary< string, BankAccountToYnabAccount > BuildMappingsFromYnabAccountNotes()
    {
        var mappings = new Dictionary< string, BankAccountToYnabAccount >( StringComparer.Ordinal );
        var ambiguousBankAccounts = new HashSet< string >( StringComparer.Ordinal );
        foreach ( var (budgetSummary, accounts) in account.DicAccounts )
        {
            foreach ( var budgetAccount in accounts )
            {
                foreach ( var bankAccount in AccountNotesMappingParser.ParseBankAccounts( budgetAccount.Note ) )
                {
                    if ( ambiguousBankAccounts.Contains( bankAccount ) )
                        continue;

                    if ( mappings.ContainsKey( bankAccount ) )
                    {
                        mappings.Remove( bankAccount );
                        ambiguousBankAccounts.Add( bankAccount );
                        logger.Warn( $"Duplicate bank account mapping '{bankAccount}' found in Account Notes. Mapping ignored until duplicate is removed." );
                        continue;
                    }

                    mappings[ bankAccount ] = new BankAccountToYnabAccount
                    {
                        BankAccount = bankAccount,
                        YnabAccount = new YnabAccount
                        {
                            Budget = budgetSummary.Name,
                            Account = budgetAccount.Name,
                        },
                    };
                }
            }
        }

        return mappings;
    }

    private static string NormalizeBankAccount( string bankAccount ) =>
        string.IsNullOrWhiteSpace( bankAccount ) ? null : bankAccount.Trim();

    private void OnApplyUserSettings( in ReplyInfo replyInfo )
    {
        if ( string.IsNullOrEmpty( AccessToken ) )
        {
            machine.Fire( Trigger.Unknown );
            return;
        }

        if ( !TryLoadBudgets( replyInfo ) )
        {
            machine.Fire( Trigger.Unknown );
            return;
        }

        var budgetSummary =
            account.DicAccounts.Keys.FirstOrDefault( item => item.Name == dbUser.DefaultYnabAccount.Budget );
        if ( budgetSummary == null )
        {
            machine.Fire( setDefaultBudgetTrigger, replyInfo );
            return;
        }

        var budgetAccount = account.DicAccounts[ budgetSummary ]
                                   .FirstOrDefault( item => item.Name == dbUser.DefaultYnabAccount.Account );
        if ( budgetAccount == null )
        {
            machine.Fire( setDefaultAccountTrigger, replyInfo );
            return;
        }

        machine.Fire( setReadyTrigger, replyInfo );
    }

    private void OnEnteringTransaction( in ReplyInfo replyInfo, string transaction )
    {
        transaction = transaction.Replace( ",", "." );
        var regex = Regex.Match( transaction, @"(.*) (-{0,1}\d*\.*\d*)" );

        if ( regex.Groups.Count != 3 )
        {
            messageSender.SendMessage( replyInfo, $"Cant parse transaction{Environment.NewLine}{transactionHelp}" );
            return;
        }

        var sum = Convert.ToDouble( regex.Groups[ 2 ].Value, CultureInfo.InvariantCulture );
        var payeeName = regex.Groups[ 1 ].Value;

        var result =
            account.AddTransactions( new List< Transaction >
                                     {
                                         new( string.Empty, DateTime.Today, sum, null, 0, CreateId(), payeeName,
                                              dbUser.DefaultYnabAccount.Budget, dbUser.DefaultYnabAccount.Account ),
                                     }, AccessToken );

        messageSender.SendMessage( replyInfo, result );
    }

    private static string CreateId() => "bot_" + DateTime.UtcNow.ToString( "yyyyMMddHHmmssfffffff" );

    private void OnDefaultAccount( in ReplyInfo replyInfo, string accountName )
    {
        var budgetSummary =
            account.DicAccounts.Keys.FirstOrDefault( item => item.Name == dbUser.DefaultYnabAccount.Budget );
        if ( budgetSummary == null )
        {
            machine.Fire( setDefaultBudgetTrigger, replyInfo );
            return;
        }

        var budgetAccount = account.DicAccounts[ budgetSummary ].FirstOrDefault( item => item.Name == accountName );
        if ( budgetAccount == null )
        {
            machine.Fire( setDefaultAccountTrigger, replyInfo );
            return;
        }

        dbUser.DefaultYnabAccount.Account = budgetAccount.Name;
        dbSaver.Save();
        machine.Fire( setReadyTrigger, replyInfo );
    }

    private void OnDefaultBudget( in ReplyInfo replyInfo, string budgetName )
    {
        var budgetSummary = account.DicAccounts.Keys.FirstOrDefault( item => item.Name == budgetName );
        if ( budgetSummary == null )
        {
            machine.Fire( setDefaultBudgetTrigger, replyInfo );
            return;
        }

        dbUser.DefaultYnabAccount.Budget = budgetName;
        dbSaver.Save();
        machine.Fire( setDefaultAccountTrigger, replyInfo );
    }

    private void OnAuthCode( in ReplyInfo replyInfo, string authCode )
    {
        const string start = "/start ";
        if ( !authCode.StartsWith( start, StringComparison.Ordinal ) )
        {
            messageSender.SendMessage( replyInfo, "Auth code parsing failed. Please try again" );
            machine.Fire( startAuthTrigger, replyInfo );
            return;
        }

        authCode = authCode.Replace( start, string.Empty );

        try
        {
            var response = oauth.GetAccessToken( authCode );
            dbUser.Access.AccessToken = response.access_token;
            dbUser.Access.RefreshToken = response.refresh_token;
            dbUser.Access.CreatedAt = UnixTimeStampToDateTime( response.created_at );
            dbUser.Access.ExpiresIn = response.expires_in;
            dbUser.Access.Scope = response.scope;
            dbUser.Access.TokenType = response.token_type;

            dbSaver.Save();

            if ( !TryLoadBudgets( replyInfo ) )
            {
                machine.Fire( startAuthTrigger, replyInfo );
                return;
            }

            machine.Fire( setDefaultBudgetTrigger, replyInfo );
        }
        catch ( Exception e )
        {
            logger.Debug( e );
            messageSender.SendMessage( replyInfo, "Cant get auth code from YNAB. Please try again" );
            machine.Fire( startAuthTrigger, replyInfo );
        }
    }

    private bool TryLoadBudgets( in ReplyInfo replyInfo )
    {
        account = new Account();
        try
        {
            account.LoadBudgets( AccessToken );
        }
        catch ( Exception e )
        {
            logger.Debug( e );
            messageSender.SendMessage( replyInfo,
                                       "Cant load your budgets. Check your YNAB Access Token and resend it" + Environment.NewLine + e.Message );
            return false;
        }

        return true;
    }

    private bool IsValidMapping( BankAccountToYnabAccount mapping )
    {
        if ( mapping == null || string.IsNullOrWhiteSpace( mapping.BankAccount ) )
            return false;

        if ( mapping.YnabAccount == null || string.IsNullOrWhiteSpace( mapping.YnabAccount.Budget ) ||
             string.IsNullOrWhiteSpace( mapping.YnabAccount.Account ) )
            return false;

        var budgetSummary = account.DicAccounts.Keys.FirstOrDefault( item => item.Name == mapping.YnabAccount.Budget );
        if ( budgetSummary == null )
            return false;

        return account.DicAccounts[ budgetSummary ].Any( item => item.Name == mapping.YnabAccount.Account );
    }

    private enum Trigger
    {
        StartAuth,
        SetDefaultBudget,
        SetDefaultAccount,
        SetReady,
        ApplyUserSettings,
        OnMessage,
        OnAddTransactionsFromFile,
        Unknown,
        ListAccounts,
    }

    private enum State
    {
        Unknown,
        Authorizing,
        EnteringDefaultBudget,
        EnteringDefaultAccount,
        ApplyingSettings,
        Ready,
    }
}
