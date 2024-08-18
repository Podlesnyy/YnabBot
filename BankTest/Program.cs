// See https://aka.ms/new-console-template for more information

using System.Text;
using Adp.Banks.Tinkoff;

Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );

var bank = new TinkoffBank();
_ = bank.Parse( File.ReadAllText( @"f:\operations Sat Aug 03 07_13_52 MSK 2024-Sun Aug 18 08_33_50 MSK 2024.ofx", Encoding.GetEncoding( bank.FileEncoding ) ) );
//_ = bank.Parse(new MemoryStream(File.ReadAllBytes(@"f:\operations Sat Aug 03 07_13_52 MSK 2024-Sun Aug 18 08_33_50 MSK 2024.ofx")));

Console.ReadLine();