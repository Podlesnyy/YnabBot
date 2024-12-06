// See https://aka.ms/new-console-template for more information

using System.Text;

Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );

var bank = new OzonBank.OzonBank();
//_ = bank.Parse( File.ReadAllText( @"f:\operations Sat Aug 03 07_13_52 MSK 2024-Sun Aug 18 08_33_50 MSK 2024.ofx", Encoding.GetEncoding( bank.FileEncoding ) ) );
_ = bank.Parse( new MemoryStream( File.ReadAllBytes( @"f:\ozonbank_document_7517854.pdf" ) ) );

Console.ReadLine();