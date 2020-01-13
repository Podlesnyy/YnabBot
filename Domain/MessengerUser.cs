using System.Collections.Generic;
using System.Linq;

namespace Domain
{
    public class MessengerUser
    {
        public int Id { get; set; }
        public string MessengerUserId { get; set; }
        public string YnabAccessToken { get; set; }
        public YnabAccount DefaultYnabAccount { get; set; }
        public List<BankAccountToYnabAccount> BankAccountToYnabAccounts { get; set;  }
    }
}
