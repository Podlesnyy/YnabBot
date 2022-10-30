// See https://aka.ms/new-console-template for more information

using Adp.Banks.BCC;
using System.Text;


var bank= new BinanceBuffer();
var tr = bank.Parse(File.ReadAllText(@"g:\binance.txt", Encoding.GetEncoding(bank.FileEncoding)));

Console.ReadLine();
