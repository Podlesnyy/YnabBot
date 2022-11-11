// See https://aka.ms/new-console-template for more information

using System.Text;
using Adp.Banks.BCC;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var bank = new Raiffeisen();
var unused = bank.Parse(File.ReadAllText(@"g:\account_statement_11.10.22-11.11.22.csv", Encoding.GetEncoding(bank.FileEncoding)));

Console.ReadLine();