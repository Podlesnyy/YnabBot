using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Adp.Banks.BCC;

public sealed class BccPdfBank : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");
    private string bankAccount;

    public bool IsItYour( string fileName ) => fileName.Contains( "pkg_w_mb_main" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( MemoryStream fileContent )
    {
        var extractFromPdfFile = ExtractFromPdfFile(fileContent);
        GetBankAccount(extractFromPdfFile);
        var transactions = GetTransactions(extractFromPdfFile);

        return transactions;
    }
    
    private List<Transaction> GetTransactions(List<string> pdfTextLines)
    {
        var ret = new List<Transaction>();
        
        const string pattern = @"(?<date>\d{2}\.\d{2}\.\d{4}(?: \d{2}:\d{2}:\d{2})?)\s*(?<text>.+?)\s+(?<summa>-?\d[\d\s]*,\d{2})";
        //string pattern = @"(?<date>\d{2}\.\d{2}\.\d{4}(?: \d{2}:\d{2}:\d{2})?)\s*(?<text>.+?)\s+((?:-?\d[\d\s]*,\d{2}\s*)+)";

        foreach (var line in pdfTextLines)
        {
            var match = Regex.Match(line, pattern);

            if (!match.Success) continue;
            
            var dateParsed = DateTime.TryParseExact(
                match.Groups["date"].Value,
                ["dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            );

            if (!dateParsed)
            {
                Console.WriteLine($"Ошибка парсинга даты для строки: {line}");
                continue;
            }

            // Получаем текст
            var text = match.Groups["text"].Value.Trim();
            
            
            // Парсим сумму
            var summaString = match.Groups["summa"].Value.Replace(" ", ""); // Убираем пробелы в числе
            var summa = -1 * double.Parse(summaString, new CultureInfo("ru-RU")); // Парсинг с учетом формата

            ret.Add( new Transaction(bankAccount, date, summa, text, 0, null, null));
        }
        return ret;
    }
    
    private void GetBankAccount(List<string> extractFromPdfFile)
    {
        bankAccount = extractFromPdfFile[1].Trim();
    }

   private static List< string > ExtractFromPdfFile( Stream fileContent )
    {
        var pdfDocument = new Document(fileContent);
        var textAbsorber = new TextAbsorber();

        pdfDocument.Pages.Accept(textAbsorber);

        return textAbsorber.Text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None).ToList();
    }
}