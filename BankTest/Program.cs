// See https://aka.ms/new-console-template for more information

using System.Text;
using Adp.Banks.AlfaBank;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var bank = new AlfaBankExcel();
//var unused = bank.Parse(File.ReadAllText(@"f:\bccironrub.txt", Encoding.GetEncoding(bank.FileEncoding)));
_ = bank.Parse(new MemoryStream(File.ReadAllBytes(@"f:\Downloads\Statement 27.09.2023 - 27.10.2023.xlsx")));

Console.ReadLine();