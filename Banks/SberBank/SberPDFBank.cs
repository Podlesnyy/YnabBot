using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using Aspose.Pdf;
using Aspose.Pdf.Text;

namespace Adp.Banks.SberBank;

public sealed class SberPdfBank : IBank
{
    private string bankAccount;

    public bool IsItYour(string fileName)
    {
        return fileName.Contains("Выписка_по_сч") || fileName.Contains("Выписка по сч");
    }

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(MemoryStream fileContent)
    {
        var extractFromPdfFile = ExtractFromPdfFile(fileContent);
        GetBankAccount(extractFromPdfFile);
        var transactions = GetTransactions(extractFromPdfFile);

        return transactions;
    }

    private void GetBankAccount(List<string> extractFromPdfFile)
    {
            // Регулярное выражение для строки вида: "40817 810 8 3829 7144493"
            const string pattern = @"(\d{5} \d{3} \d{1} \d{4} \d{7})";
            var regex = new Regex(pattern);

            // Проход по массиву строк
            foreach (var match in extractFromPdfFile.Select(str => regex.Match(str)).Where(match => match.Success))
            {
                bankAccount = match.Groups[1].Value;
                return;
            }

            throw new Exception("Не удалось найти номер счета.");
    }

    private List<Transaction> GetTransactions(List<string> pdfTextLines)
    {
        const string datePattern = @"^\d{2}\.\d{2}\.\d{4}";
        var ret = new List<Transaction>();
        for (var i = 0; i < pdfTextLines.Count - 1; i++)
        {
            if (Regex.IsMatch(pdfTextLines[i], datePattern) && Regex.IsMatch(pdfTextLines[i + 1], datePattern))
            {
                var firstTransactionLine = pdfTextLines[i];
                var (transaction, time) = CreateTransaction(firstTransactionLine);
                var secondTransactionLine = pdfTextLines[++i];
                transaction.Memo = GetMemo(secondTransactionLine);
                while (pdfTextLines[++i].Trim() != "")
                {
                    transaction.Memo += " " + pdfTextLines[i].Trim();
                }
                transaction.Memo += " " + time;
                
                ret.Add(transaction);
            }
        }

        return ret;
    }

    private string GetMemo(string secondTransactionLine)
    {
        const string pattern = @"\d{2}\.\d{2}\.\d{4}\s+(.*)";
        var regex = new Regex(pattern);
        var match = regex.Match(secondTransactionLine);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim(); // Возвращаем текст после даты без лишних пробелов
        }

        throw new Exception("Не удалось распарсить строку получателя.");
    }

    private static List<string> ExtractFromPdfFile(Stream fileContent)
    {
        var pdfDocument = new Document(fileContent);
        var textAbsorber = new TextAbsorber();

        pdfDocument.Pages.Accept(textAbsorber);

        return textAbsorber.Text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None).ToList();
    }

    private (Transaction, string timeString) CreateTransaction(string firstTransactionLine)
    {
        const string pattern = @"(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<time>\d{2}:\d{2})\s+(?<authCode>\d+)\s+(?<description>.+?)\s+(?<sum>[+-]?\d{1,3}(?:\s?\d{3})*,\d{2})";

        var match = Regex.Match(firstTransactionLine, pattern);

        if (match.Success)
        {
            var dateString = match.Groups["date"].Value;
            var timeString = match.Groups["time"].Value;
            var description = match.Groups["description"].Value.Trim();
            var authCode = match.Groups["authCode"].Value;
            var sumString = match.Groups["sum"].Value.Replace(" ", "").Replace("\u00A0", ""); // Убираем пробелы для корректного парсинга числа
            if (!sumString.StartsWith("+"))
            {
                sumString = "-" + sumString;
            }

            // Преобразование в DateTime для даты
            var date = DateTime.ParseExact(dateString, "dd.MM.yyyy", CultureInfo.InvariantCulture);
            // Преобразование суммы в double
            var sum = -1 * double.Parse(sumString, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, new CultureInfo("ru-RU"));

            return (new Transaction(bankAccount, date, sum, null, 0, authCode, description), timeString);
        }

        throw new Exception("Не удалось распарсить строку транзакции.");
    }
}