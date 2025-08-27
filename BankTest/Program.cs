// See https://aka.ms/new-console-template for more information

using System.Text;
using Adp.Banks.BCC;

Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );

var bank = new BccHtml();
//_ = bank.Parse( File.ReadAllText( @"f:\operations Sat Aug 03 07_13_52 MSK 2024-Sun Aug 18 08_33_50 MSK 2024.ofx", Encoding.GetEncoding( bank.FileEncoding ) ) );
// ReSharper disable once UnusedVariable
//var trans = bank.Parse( new MemoryStream( File.ReadAllBytes( @"f:\Downloads\Выписка по счёту дебетовой карты.pdf" ) ) );
//var trans = bank.Parse( new MemoryStream( File.ReadAllBytes("/Users/andr/Downloads/!pkg_w_mb_main.pdf") ) );
var trans = bank.Parse( File.ReadAllText( @"/Users/andr/Downloads/bcc_kz_rub.html",
                                          Encoding.GetEncoding( bank.FileEncoding ) ) );

Console.ReadLine();