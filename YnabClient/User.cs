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

namespace Adp.YnabClient
{
    internal sealed class User
    {
        private readonly IDbSaver dbSaver;
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly StateMachine<State, Trigger> machine;
        private readonly IMessageSender messageSender;
        private readonly Oauth oauth;
        private Account account;

        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> applyUserSettingsTrigger;
        private Domain.User dbUser;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> listAccounts;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo, List<Transaction>> onAddTransactionFromFileTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo, string> onMessageTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> setDefaultAccountTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> setDefaultBudgetTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> setReadyTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> startAuthTrigger;

        private string transactionHelp = $"Example transaction:{Environment.NewLine}Apples 7,47";

        public User(IMessageSender messageSender, IDbSaver dbSaver, Oauth oauth)
        {
            this.messageSender = messageSender;
            this.dbSaver = dbSaver;
            this.oauth = oauth;

            machine = new StateMachine<State, Trigger>(State.Unknown);
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
                if (DateTime.Now - dbUser.Access.CreatedAt > TimeSpan.FromSeconds(dbUser.Access.ExpiresIn) - TimeSpan.FromMinutes(5))
                    RefreshToken();

                return dbUser.Access.AccessToken;
            }
        }

        private void RefreshToken()
        {
            var response = oauth.GetRefreshedAccessToken(dbUser.Access.RefreshToken);
            dbUser.Access.AccessToken = response.access_token;
            dbUser.Access.RefreshToken = response.refresh_token;
            dbUser.Access.CreatedAt = UnixTimeStampToDateTime(response.created_at);
            dbUser.Access.ExpiresIn = response.expires_in;
            dbUser.Access.Scope = response.scope;
            dbUser.Access.TokenType = response.token_type;

            dbSaver.Save();
        }

        public void AuthCommand(in ReplyInfo replyInfo)
        {
            machine.Fire(startAuthTrigger, replyInfo);
        }

        public void OnMessage(in ReplyInfo replyInfo, string message)
        {
            machine.Fire(onMessageTrigger, replyInfo, message);
        }

        public void StartSetDefaultBudget(in ReplyInfo replyInfo)
        {
            machine.Fire(setDefaultBudgetTrigger, replyInfo);
        }

        public void AddTransactionsFromFile(in ReplyInfo replyInfo, List<Transaction> transactions)
        {
            machine.Fire(onAddTransactionFromFileTrigger, replyInfo, transactions);
        }

        private void SetupStateApplyingSettings()
        {
            machine.Configure(State.ApplyingSettings).OnEntryFrom(applyUserSettingsTrigger, (replyInfo, _) => OnApplyUserSettings(replyInfo)).Permit(Trigger.Unknown, State.Unknown)
                .Permit(Trigger.StartAuth, State.Authorizing).Permit(Trigger.SetDefaultBudget, State.EnteringDefaultBudget).Permit(Trigger.SetDefaultAccount, State.EnteringDefaultAccount)
                .Permit(Trigger.SetReady, State.Ready);
        }

        private void SetupStateReady()
        {
            machine.Configure(State.Ready).InternalTransition(onMessageTrigger, (replyInfo, message, __) => OnEnteringTransaction(replyInfo, message))
                .InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, transactions, __) => OnAddTransactions(replyInfo, transactions))
                .InternalTransition(listAccounts, (replyInfo, _) => ShowAccountList(replyInfo)).OnEntryFrom(setReadyTrigger,
                    (replyInfo, _) => messageSender.SendMessage(replyInfo, $"Congratulations!{Environment.NewLine}Now you can add transaction directly to your YNAB account {dbUser.DefaultYnabAccount.Budget}/{dbUser.DefaultYnabAccount.Account}{Environment.NewLine}{transactionHelp}"))
                .Permit(Trigger.SetDefaultBudget, State.EnteringDefaultBudget).Permit(Trigger.StartAuth, State.Authorizing).Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupStateEnteringDefaultAccount()
        {
            machine.Configure(State.EnteringDefaultAccount).InternalTransition(onMessageTrigger, (replyInfo, message, __) => OnDefaultAccount(replyInfo, message))
                .InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, transactions, __) => OnAddTransactions(replyInfo, transactions))
                .InternalTransition(listAccounts, (replyInfo, _) => ShowAccountList(replyInfo)).PermitReentry(Trigger.SetDefaultAccount).Permit(Trigger.SetDefaultBudget, State.EnteringDefaultBudget).OnEntryFrom(
                    setDefaultAccountTrigger,
                    replyInfo => messageSender.SendOptions(replyInfo,
                        "Select account for adding transaction. You can easy change it later",
                        account.DicAccounts[account.DicAccounts.Keys.First(item => item.Name == dbUser.DefaultYnabAccount.Budget)].Select(item => item.Name).ToList())).Permit(Trigger.SetReady, State.Ready)
                .Permit(Trigger.StartAuth, State.Authorizing).Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupStateEnteringDefaultBudget()
        {
            machine.Configure(State.EnteringDefaultBudget).InternalTransition(onMessageTrigger, (replyInfo, message, __) => OnDefaultBudget(replyInfo, message))
                .InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, transactions, __) => OnAddTransactions(replyInfo, transactions))
                .InternalTransition(listAccounts, (replyInfo, _) => ShowAccountList(replyInfo))
                .OnEntryFrom(setDefaultBudgetTrigger,
                    replyInfo => messageSender.SendOptions(replyInfo, "Select budget for adding transaction. You can easy change it later", account.DicAccounts.Keys.Select(item => item.Name).ToList()))
                .PermitReentry(Trigger.SetDefaultBudget).Permit(Trigger.SetDefaultAccount, State.EnteringDefaultAccount).Permit(Trigger.StartAuth, State.Authorizing)
                .Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupStateAuthorizing()
        {
            var pleaseAuthorize = $"<a href=\"{oauth.GetAuthLink()}\">Please authorize bot</a> and click Start button after your return back to telegram";

            machine.Configure(State.Authorizing).InternalTransition(onMessageTrigger, (replyInfo, message, _) => OnAuthCode(replyInfo, message))
                .InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, transactions, __) => OnAddTransactions(replyInfo, transactions))
                .InternalTransition(listAccounts, (replyInfo, _) => messageSender.SendMessage(replyInfo, pleaseAuthorize)).OnEntryFrom(startAuthTrigger, replyInfo => messageSender.SendMessage(replyInfo, pleaseAuthorize))
                .PermitReentry(Trigger.StartAuth).Permit(Trigger.SetDefaultBudget, State.EnteringDefaultBudget).Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupStateUnknown()
        {
            const string youShouldRunSetupCommand = "At first you should run /auth command";
            machine.Configure(State.Unknown).InternalTransition(onMessageTrigger, (replyInfo, _, __) => messageSender.SendMessage(replyInfo, youShouldRunSetupCommand))
                .InternalTransition(setDefaultBudgetTrigger, (replyInfo, _) => messageSender.SendMessage(replyInfo, youShouldRunSetupCommand))
                .InternalTransition(listAccounts, (replyInfo, _) => messageSender.SendMessage(replyInfo, youShouldRunSetupCommand))
                .InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, _, __) => messageSender.SendMessage(replyInfo, youShouldRunSetupCommand)).Permit(Trigger.StartAuth, State.Authorizing)
                .Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupTriggers()
        {
            onMessageTrigger = machine.SetTriggerParameters<ReplyInfo, string>(Trigger.OnMessage);
            setDefaultBudgetTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.SetDefaultBudget);
            setDefaultAccountTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.SetDefaultAccount);
            listAccounts = machine.SetTriggerParameters<ReplyInfo>(Trigger.ListAccounts);
            applyUserSettingsTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.ApplyUserSettings);
            startAuthTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.StartAuth);
            setReadyTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.SetReady);
            onAddTransactionFromFileTrigger = machine.SetTriggerParameters<ReplyInfo, List<Transaction>>(Trigger.OnAddTransactionsFromFile);
        }

        private void ShowAccountList(ReplyInfo replyInfo)
        {
            var ret = new StringBuilder();
            foreach (var budget in account.DicAccounts)
            {
                ret.AppendLine(budget.Key.Name);
                foreach (var budgetAccount in budget.Value.OrderBy(item => item.Name)) ret.AppendLine($"----------------{budgetAccount.Name}");
            }

            messageSender.SendMessage(replyInfo, ret.ToString());
        }

        private void OnAddTransactions(in ReplyInfo replyInfo, IReadOnlyCollection<Transaction> transactions)
        {
            foreach (var transaction in transactions)
            {
                var accountSettings = dbUser.BankAccountToYnabAccounts.FirstOrDefault(item => item.BankAccount == transaction.BankAccount);
                if (accountSettings == null)
                {
                    messageSender.SendMessage(replyInfo, $"Cant find bank account {transaction.BankAccount} link to YNAB budget account");
                    ListBankAccountsCommand(replyInfo);
                    return;
                }

                transaction.YnabBudget = accountSettings.YnabAccount.Budget;
                transaction.YnabAccount = accountSettings.YnabAccount.Account;
            }

            var result = account.AddTransactions(transactions, AccessToken);
            messageSender.SendMessage(replyInfo, result);
        }

        private void OnApplyUserSettings(in ReplyInfo replyInfo)
        {
            if (string.IsNullOrEmpty(AccessToken))
            {
                machine.Fire(Trigger.Unknown);
                return;
            }

            if (!TryLoadBudgets(replyInfo, AccessToken))
            {
                machine.Fire(Trigger.Unknown);
                return;
            }

            var budgetSummary = account.DicAccounts.Keys.FirstOrDefault(item => item.Name == dbUser.DefaultYnabAccount.Budget);
            if (budgetSummary == null)
            {
                machine.Fire(setDefaultBudgetTrigger, replyInfo);
                return;
            }

            var budgetAccount = account.DicAccounts[budgetSummary].FirstOrDefault(item => item.Name == dbUser.DefaultYnabAccount.Account);
            if (budgetAccount == null)
            {
                machine.Fire(setDefaultAccountTrigger, replyInfo);
                return;
            }

            machine.Fire(setReadyTrigger, replyInfo);
        }

        private void OnEnteringTransaction(in ReplyInfo replyInfo, string transaction)
        {
            transaction = transaction.Replace(".", ",");
            var regex = Regex.Match(transaction, @"(.*) (-{0,1}\d*,*\d*)");

            if (regex.Groups.Count != 3)
            {
                messageSender.SendMessage(replyInfo,
                    $"Cant parse transaction{Environment.NewLine}{transactionHelp}");
                return;
            }

            var sum = Convert.ToDouble(regex.Groups[2].Value, CultureInfo.InvariantCulture);
            var payeeName = regex.Groups[1].Value;

            var result = account.AddTransactions(
                new List<Transaction> {new Transaction(string.Empty, DateTime.Today, sum, null, 0, CreateId(), payeeName, dbUser.DefaultYnabAccount.Budget, dbUser.DefaultYnabAccount.Account)}, AccessToken);

            messageSender.SendMessage(replyInfo, result);
        }

        private static string CreateId()
        {
            return "bot_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff");
        }

        private void OnDefaultAccount(in ReplyInfo replyInfo, string accountName)
        {
            var budgetSummary = account.DicAccounts.Keys.FirstOrDefault(item => item.Name == dbUser.DefaultYnabAccount.Budget);
            if (budgetSummary == null)
            {
                machine.Fire(setDefaultBudgetTrigger, replyInfo);
                return;
            }

            var budgetAccount = account.DicAccounts[budgetSummary].FirstOrDefault(item => item.Name == accountName);
            if (budgetAccount == null)
            {
                machine.Fire(setDefaultAccountTrigger, replyInfo);
                return;
            }

            dbUser.DefaultYnabAccount.Account = budgetAccount.Name;
            dbSaver.Save();
            machine.Fire(setReadyTrigger, replyInfo);
        }

        private void OnDefaultBudget(in ReplyInfo replyInfo, string budgetName)
        {
            var budgetSummary = account.DicAccounts.Keys.FirstOrDefault(item => item.Name == budgetName);
            if (budgetSummary == null)
            {
                machine.Fire(setDefaultBudgetTrigger, replyInfo);
                return;
            }

            dbUser.DefaultYnabAccount.Budget = budgetName;
            dbSaver.Save();
            machine.Fire(setDefaultAccountTrigger, replyInfo);
        }


        private void OnAuthCode(in ReplyInfo replyInfo, string authCode)
        {
            const string start = "/start ";
            if (!authCode.StartsWith(start))
            {
                messageSender.SendMessage(replyInfo, "Auth code parsing failed. Please try again");
                machine.Fire(startAuthTrigger, replyInfo);
                return;
            }

            authCode = authCode.Replace(start, string.Empty);

            try
            {
                var response = oauth.GetAccessToken(authCode);
                dbUser.Access.AccessToken = response.access_token;
                dbUser.Access.RefreshToken = response.refresh_token;
                dbUser.Access.CreatedAt = UnixTimeStampToDateTime(response.created_at);
                dbUser.Access.ExpiresIn = response.expires_in;
                dbUser.Access.Scope = response.scope;
                dbUser.Access.TokenType = response.token_type;

                dbSaver.Save();

                if (!TryLoadBudgets(replyInfo, AccessToken))
                {
                    machine.Fire(startAuthTrigger, replyInfo);
                    return;
                }

                machine.Fire(setDefaultBudgetTrigger, replyInfo);
            }
            catch (Exception e)
            {
                logger.Debug(e);
                messageSender.SendMessage(replyInfo, "Cant get auth code from YNAB. Please try again");
                machine.Fire(startAuthTrigger, replyInfo);
            }
        }

        public static DateTime UnixTimeStampToDateTime(int unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            var dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        private bool TryLoadBudgets(in ReplyInfo replyInfo, string accessToken)
        {
            account = new Account(accessToken);
            try
            {
                account.LoadBudgets(AccessToken);
            }
            catch (Exception e)
            {
                logger.Debug(e);
                messageSender.SendMessage(replyInfo, "Cant load your budgets. Check your YNAB Access Token and resend it" + Environment.NewLine + e.Message);
                return false;
            }

            return true;
        }

        public void Init(ReplyInfo replyInfo, Domain.User user)
        {
            dbUser = user;
            machine.Fire(applyUserSettingsTrigger, replyInfo);
        }

        public void ListYnabAccountsCommand(ReplyInfo replyInfo)
        {
            machine.Fire(listAccounts, replyInfo);
        }

        public void AddBankAccounts(ReplyInfo replyInfo, IEnumerable<BankAccountToYnabAccount> listSynonyms)
        {
            dbUser.BankAccountToYnabAccounts.Clear();
            dbUser.BankAccountToYnabAccounts.AddRange(listSynonyms);
            dbSaver.Save();
            ListBankAccountsCommand(replyInfo);
        }

        public void ListBankAccountsCommand(ReplyInfo replyInfo)
        {
            messageSender.SendMessage(replyInfo,
                dbUser.BankAccountToYnabAccounts.Count == 0
                    ? "You haven't any bank accounts linked to YNAB accounts "
                    : string.Join(Environment.NewLine, dbUser.BankAccountToYnabAccounts.Select(item => $"{item.BankAccount} - {item.YnabAccount.Budget}\\{item.YnabAccount.Account}")));
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
            ListAccounts
        }

        private enum State
        {
            Unknown,
            Authorizing,
            EnteringDefaultBudget,
            EnteringDefaultAccount,
            ApplyingSettings,
            Ready
        }
    }
}