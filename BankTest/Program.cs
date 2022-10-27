// See https://aka.ms/new-console-template for more information

using Adp.Banks.BCC;
using System.Text;

Console.WriteLine("Hello, World!");

var bank= new BCCBank();
var tr = bank.Parse(File.ReadAllText(@"g:\bcctenge.txt", Encoding.GetEncoding(bank.FileEncoding)));
