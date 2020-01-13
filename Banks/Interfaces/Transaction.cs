using System;

namespace Adp.Banks.Interfaces
{
    public class Transaction
    {
        public Transaction(string bankAccount, DateTime date, double amount, string memo, int mcc, string id, string payee, string ynabBudget = null, string ynabAccount = null)
        {
            BankAccount = bankAccount;
            Date = date;
            Amount = amount;
            Memo = memo;
            Mcc = mcc;
            Id = id;
            Payee = payee;
            YnabBudget = ynabBudget;
            YnabAccount = ynabAccount;
        }

        public string BankAccount { get; }
        public DateTime Date { get; }
        public double Amount { get; }
        public string Memo { get; }
        public int Mcc { get; }
        public string Id { get; }
        public string Payee { get; }
        public string YnabBudget { get; set; }
        public string YnabAccount { get; set; }
    }
}