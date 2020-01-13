using System;
using System.Collections.Generic;
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
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly StateMachine<State, Trigger> machine;
        private readonly IMessageSender messageSender;
        private readonly IDbSaver dbSaver;
        private MessengerUser dbUser;
        private Account account;

        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> applyUserSettingsTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> listAccounts;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo, List<Transaction>> onAddTransactionFromFileTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo, string> onMessageTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> setDefaultAccountTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> setDefaultBudgetTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> setReadyTrigger;
        private StateMachine<State, Trigger>.TriggerWithParameters<ReplyInfo> startSetupTrigger;

        public User(IMessageSender messageSender, IDbSaver dbSaver )
        {
            this.messageSender = messageSender;
            this.dbSaver = dbSaver;

            machine = new StateMachine<State, Trigger>(State.Unknown);
            SetupTriggers();
            SetupStateUnknown();
            SetupStateEnteringAccessToken();
            SetupStateEnteringDefaultBudget();
            SetupStateEnteringDefaultAccount();
            SetupStateReady();
            SetupStateApplyingSettings();
        }

        public void StartSetupCommand(in ReplyInfo replyInfo)
        {
            machine.Fire(startSetupTrigger, replyInfo);
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
            machine.Configure(State.ApplyingSettings).
                OnEntryFrom(applyUserSettingsTrigger, (replyInfo, _) => OnApplyUserSettings(replyInfo)).
                Permit(Trigger.Unknown, State.Unknown).
                Permit(Trigger.StartSetup, State.EnteringAccessToken).
                Permit(Trigger.SetDefaultBudget, State.EnteringDefaultBudget).
                Permit(Trigger.SetDefaultAccount, State.EnteringDefaultAccount).
                Permit(Trigger.SetReady, State.Ready);
        }

        private void SetupStateReady()
        {
            machine.Configure(State.Ready).
                InternalTransition(onMessageTrigger, (replyInfo, message, __) => OnEnteringTransaction(replyInfo, message)).
                InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, transactions, __) => OnAddTransactions(replyInfo, transactions)).
                InternalTransition(listAccounts, (replyInfo, _) => ShowAccountList(replyInfo)).
                OnEntryFrom(setReadyTrigger, (replyInfo, _) => messageSender.SendMessage(replyInfo, "Congratulations! Setup is finished")).
                Permit(Trigger.SetDefaultBudget, State.EnteringDefaultBudget).
                Permit(Trigger.StartSetup, State.EnteringAccessToken).
                Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupStateEnteringDefaultAccount()
        {
            machine.Configure(State.EnteringDefaultAccount).
                InternalTransition(onMessageTrigger, (replyInfo, message, __) => OnDefaultAccount(replyInfo, message)).
                InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, transactions, __) => OnAddTransactions(replyInfo, transactions)).
                InternalTransition(listAccounts, (replyInfo, _) => ShowAccountList(replyInfo)).
                PermitReentry(Trigger.SetDefaultAccount).
                Permit(Trigger.SetDefaultBudget, State.EnteringDefaultBudget).
                OnEntryFrom(setDefaultAccountTrigger,
                    replyInfo => messageSender.SendOptions(replyInfo,
                        "Select default account",
                        account.DicAccounts[account.DicAccounts.Keys.First(item => item.Name == dbUser.DefaultYnabAccount.Budget)].Select(item => item.Name).ToList())).
                Permit(Trigger.SetReady, State.Ready).
                Permit(Trigger.StartSetup, State.EnteringAccessToken).
                Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupStateEnteringDefaultBudget()
        {
            machine.Configure(State.EnteringDefaultBudget).
                InternalTransition(onMessageTrigger, (replyInfo, message, __) => OnDefaultBudget(replyInfo, message)).
                InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, transactions, __) => OnAddTransactions(replyInfo, transactions)).
                InternalTransition(listAccounts, (replyInfo, _) => ShowAccountList(replyInfo) ).
                OnEntryFrom(setDefaultBudgetTrigger, replyInfo => messageSender.SendOptions(replyInfo, "Select default budget", account.DicAccounts.Keys.Select(item => item.Name).ToList())).
                PermitReentry(Trigger.SetDefaultBudget).
                Permit(Trigger.SetDefaultAccount, State.EnteringDefaultAccount).
                Permit(Trigger.StartSetup, State.EnteringAccessToken).
                Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupStateEnteringAccessToken()
        {
            const string pleaseEnterYnabToken = "Please enter your YNAB access token. More info on https://api.youneedabudget.com/";
            machine.Configure(State.EnteringAccessToken).
                InternalTransition(onMessageTrigger, (replyInfo, message, _) => OnAccessToken(replyInfo, message)).
                InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, transactions, __) => OnAddTransactions(replyInfo, transactions)).
                InternalTransition(listAccounts, (replyInfo, _) => messageSender.SendMessage(replyInfo, pleaseEnterYnabToken)).
                OnEntryFrom(startSetupTrigger, replyInfo => messageSender.SendMessage(replyInfo, pleaseEnterYnabToken)).
                PermitReentry(Trigger.StartSetup).
                Permit(Trigger.SetDefaultBudget, State.EnteringDefaultBudget).
                Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupStateUnknown()
        {
            const string youShouldRunSetupCommand = "You should run /setup command";
            machine.Configure(State.Unknown).
                InternalTransition(onMessageTrigger, (replyInfo, _, __) => messageSender.SendMessage(replyInfo, youShouldRunSetupCommand)).
                InternalTransition(setDefaultBudgetTrigger, (replyInfo, _) => messageSender.SendMessage(replyInfo, youShouldRunSetupCommand)).
                InternalTransition(listAccounts, (replyInfo, _) => messageSender.SendMessage(replyInfo, youShouldRunSetupCommand)).
                InternalTransition(onAddTransactionFromFileTrigger, (replyInfo, _, __) => messageSender.SendMessage(replyInfo, youShouldRunSetupCommand)).
                Permit(Trigger.StartSetup, State.EnteringAccessToken).
                Permit(Trigger.ApplyUserSettings, State.ApplyingSettings);
        }

        private void SetupTriggers()
        {
            onMessageTrigger = machine.SetTriggerParameters<ReplyInfo, string>(Trigger.OnMessage);
            setDefaultBudgetTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.SetDefaultBudget);
            setDefaultAccountTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.SetDefaultAccount);
            listAccounts = machine.SetTriggerParameters<ReplyInfo>(Trigger.ListAccounts);
            applyUserSettingsTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.ApplyUserSettings);
            startSetupTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.StartSetup);
            setReadyTrigger = machine.SetTriggerParameters<ReplyInfo>(Trigger.SetReady);
            onAddTransactionFromFileTrigger = machine.SetTriggerParameters<ReplyInfo, List<Transaction>>(Trigger.OnAddTransactionsFromFile);
        }

        private void ShowAccountList(ReplyInfo replyInfo)
        {
            var ret = new StringBuilder();
            foreach (var budget in account.DicAccounts)
            {
                ret.AppendLine(budget.Key.Name);
                foreach (var budgetAccount in budget.Value.OrderBy(item=>item.Name))
                {
                    ret.AppendLine( $"----------------{budgetAccount.Name}");
                }
            }
            messageSender.SendMessage(replyInfo, ret.ToString());
        }

        private void OnAddTransactions(in ReplyInfo replyInfo, IReadOnlyCollection<Transaction> transactions)
        {
            foreach (var transaction in transactions)
            {
                var accountSettings = dbUser.BankAccountToYnabAccounts.FirstOrDefault( item => item.BankAccount == transaction.BankAccount);
                if (accountSettings == null)
                {
                    messageSender.SendMessage(replyInfo, $"Cant find bank account {transaction.BankAccount} link to YNAB budget account");
                    ListBankAccountsCommand(replyInfo);
                    return;
                }

                transaction.YnabBudget = accountSettings.YnabAccount.Budget;
                transaction.YnabAccount = accountSettings.YnabAccount.Account;
            }

            var result = account.AddTransactions(transactions);
            messageSender.SendMessage(replyInfo, result);
        }

        private void OnApplyUserSettings(in ReplyInfo replyInfo)
        {
            if (string.IsNullOrEmpty(dbUser.YnabAccessToken))
            {
                machine.Fire(Trigger.Unknown);
                return;
            }

            if (!TryLoadBudgets(replyInfo, dbUser.YnabAccessToken))
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
            var regex = Regex.Match(transaction, @"(\d{1,2}\.\d{1,2} )?(.*) (-{0,1}\d*,*\d*)");

            if (regex.Groups.Count == 1)
            {
                messageSender.SendMessage(replyInfo,
                    $"Cant parse transaction. Example transaction at 21 October: {Environment.NewLine}21.10 Car 15999,99{Environment.NewLine}Or today transaction{Environment.NewLine}Food 19,47");
                return;
            }

            var date = regex.Groups[1].Value;

            var transactionDate = string.IsNullOrEmpty(date) ? DateTime.Today : DateTime.Parse($"{date}.{DateTime.Today.Year}");
            var sum = Convert.ToDouble(regex.Groups[3].Value);
            var payeeName = regex.Groups[2].Value;

            var result = account.AddTransactions(new List<Transaction> {new Transaction(string.Empty, transactionDate, sum, null, 0, CreateId(), payeeName, dbUser.DefaultYnabAccount.Budget, dbUser.DefaultYnabAccount.Account) });

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


        private void OnAccessToken(in ReplyInfo replyInfo, string accessToken)
        {
            if (!TryLoadBudgets(replyInfo, accessToken))
                return;

            dbUser.YnabAccessToken = accessToken;
            dbSaver.Save();
            machine.Fire(setDefaultBudgetTrigger, replyInfo);
        }

        private bool TryLoadBudgets(in ReplyInfo replyInfo, string accessToken)
        {
            account = new Account(accessToken);
            try
            {
                account.LoadBudgets();
            }
            catch (Exception e)
            {
                logger.Debug(e);
                messageSender.SendMessage(replyInfo, "Cant load your budgets. Check your YNAB Access Token and resend it" + Environment.NewLine + e.Message);
                return false;
            }

            return true;
        }

        private enum Trigger
        {
            StartSetup,
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
            EnteringAccessToken,
            EnteringDefaultBudget,
            EnteringDefaultAccount,
            ApplyingSettings,
            Ready
        }

        public void Init(ReplyInfo replyInfo, MessengerUser messengerUser)
        {
            dbUser = messengerUser;
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
    }
}