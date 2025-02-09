// See https://aka.ms/new-console-template for more information

using System.Text;
using Adp.Banks.BCC;

Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );

var bank = new Raiffeisen3406();
//_ = bank.Parse( File.ReadAllText( @"f:\operations Sat Aug 03 07_13_52 MSK 2024-Sun Aug 18 08_33_50 MSK 2024.ofx", Encoding.GetEncoding( bank.FileEncoding ) ) );
// ReSharper disable once UnusedVariable
//var trans = bank.Parse( new MemoryStream( File.ReadAllBytes("/Users/andr/Downloads/!pkg_w_mb_main (4).pdf") ) );
//var trans = bank.Parse( new MemoryStream( File.ReadAllBytes("/Users/andr/Downloads/!pkg_w_mb_main.pdf") ) );
var trans = bank.Parse(File.ReadAllText(@"f:\Downloads\3406_account_statement_01.01.25-08.02.25.csv", Encoding.GetEncoding(bank.FileEncoding)));

Console.ReadLine();