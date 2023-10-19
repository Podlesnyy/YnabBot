// See https://aka.ms/new-console-template for more information

using System.Text;
using Adp.Banks.BCC;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var bank = new BccRub();
var unused = bank.Parse(File.ReadAllText(@"f:\bccironrub.txt", Encoding.GetEncoding(bank.FileEncoding)));

Console.ReadLine();