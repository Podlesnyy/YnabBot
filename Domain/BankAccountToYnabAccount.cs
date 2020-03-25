namespace Domain
{
    public class BankAccountToYnabAccount
    {
        public int Id { get; set; }
        public string BankAccount { get; set; }
        public YnabAccount YnabAccount { get; set; }
    }
}