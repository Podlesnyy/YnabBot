using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using Aspose.Pdf;
using Aspose.Pdf.Text;

namespace Adp.Banks.BCC;

public sealed class BccPdfBank : IBank
{
    private string bankAccount;

    public bool IsItYour(string fileName)
    {
        return fileName.Contains("pkg_w_mb_main");
    }

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(MemoryStream fileContent)
    {
        var extractFromPdfFile = ExtractFromPdfFile(fileContent);
        GetBankAccount(extractFromPdfFile);
        var transactions = GetTransactions(extractFromPdfFile);

        return transactions;
    }

    private List<Transaction> GetTransactions(List<string> pdfTextLines)
    {
        var ret = new List<Transaction>();

        const string patternKzt =
            @"(?<date>\d{2}\.\d{2}\.\d{4})\s+\d{2}:\d{2}:\d{2}\s+\d{2}\.\d{2}\.\d{4}\s+(?<text>.+?)\s+-?\d[\d\s.,]*\s+KZT\s+(?<number>-?\d[\d\s]+,\d\d)\s";
        const string patternOther =
            @"(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<description>.+?)\s+(?<amount>-?\d+,\d+)\s+\d+,\d+";

        foreach (var line in pdfTextLines)
        {
            var match = Regex.Match(line, patternKzt);

            if (match.Success)
            {
                ret.Add(GetFromKzt(match));
                continue;
            }

            match = Regex.Match(line, patternOther);

            if (!match.Success) continue;
            ret.Add(GetFromAnother(match));
        }

        return ret;
    }

    private Transaction GetFromAnother(Match match)
    {
        var date = DateTime.ParseExact(match.Groups["date"].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture);
        var description = match.Groups["description"].Value.Trim();
        var amount = double.Parse(match.Groups["amount"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);

        return new Transaction(bankAccount, date, amount, description, 0, null, null);
    }

    private Transaction GetFromKzt(Match match)
    {
        var date = DateTime.ParseExact(match.Groups["date"].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture);

        // Получаем текст
        var text = match.Groups["text"].Value.Trim();
        // Получаем число и заменяем запятую на точку для преобразования
        var summa = -1 * double.Parse(match.Groups["number"].Value.Replace(" ", "").Replace(",", "."),
            CultureInfo.InvariantCulture);

        return new Transaction(bankAccount, date, summa, text, 0, null, null);
    }

    private void GetBankAccount(List<string> extractFromPdfFile)
    {
        bankAccount = extractFromPdfFile[1].Trim();
    }

    private static List<string> ExtractFromPdfFile(Stream fileContent)
    {
        var pdfDocument = new Document(fileContent);
        var textAbsorber = new TextAbsorber();

        pdfDocument.Pages.Accept(textAbsorber);

        return textAbsorber.Text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None).ToList();
    }
}