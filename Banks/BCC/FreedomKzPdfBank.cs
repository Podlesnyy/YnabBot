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

// ReSharper disable once UnusedType.Global
public sealed partial class FreedomKzPdfBank : IBank
{
    private string bankAccount;

    public bool IsItYour(string fileName)
    {
        return fileName.Contains("legal_statement") && fileName.Contains("current");
    }

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(MemoryStream fileContent)
    {
        using var pdfDocument = new Document(fileContent);
        bankAccount = ExtractBankAccount(pdfDocument);
        var tableRows = ExtractTableRows(pdfDocument);
        if (tableRows.Count == 0)
            return [];

        var columnMap = BuildColumnMap(tableRows);
        var transactions = new List<Transaction>();

        foreach (var row in tableRows)
        {
            if (row.All(static item => string.IsNullOrWhiteSpace(item)))
                continue;

            if (IsHeaderRow(row))
                continue;

            var dateText = GetCell(row, columnMap.Date);
            if (!TryParseDate(dateText, out var date))
                continue;

            var debitText = GetCell(row, columnMap.Debit);
            var creditText = GetCell(row, columnMap.Credit);

            var hasDebit = TryParseAmount(debitText, out var debit);
            var hasCredit = TryParseAmount(creditText, out var credit);
            if (!hasDebit && !hasCredit)
                continue;

            debit = Math.Abs(debit);
            credit = Math.Abs(credit);

            var amount = hasDebit && debit > 0 ? debit : -credit;
            if (Math.Abs(amount) < 0.0001)
                continue;

            var id = NormalizeSpaces(GetCell(row, columnMap.Document));
            var memo = NormalizeSpaces(GetCell(row, columnMap.Memo));
            var memoCompact = NormalizeForComparison(memo);
            if (memoCompact.Contains(NormalizeForComparison("Выплата вклада с депозитного договора")) ||
                memoCompact.Contains(NormalizeForComparison("Выплата вклада по Договору")) ||
                memoCompact.StartsWith(NormalizeForComparison("по договору KZ")))
                continue;

            transactions.Add(new Transaction(bankAccount, date, amount, memo, 0,
                string.IsNullOrWhiteSpace(id) ? null : id, ""));
        }

        return transactions;
    }

    private static string ExtractBankAccount(Document pdfDocument)
    {
        var textAbsorber = new TextAbsorber();
        pdfDocument.Pages.Accept(textAbsorber);
        var text = NormalizeSpaces(textAbsorber.Text);

        var patterns = new[]
        {
            @"Счет:\s*(KZ[0-9A-Z]{10,})",
            @"IBAN\s*[:№]?\s*(KZ[0-9A-Z]{10,})",
            @"KZ[0-9A-Z]{10,}"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        }

        throw new Exception("Не удалось найти номер счета.");
    }


    private static string NormalizeSpaces(string s)
    {
        // приводим множественные пробелы к одному, убираем неразрывные и табы
        var t = (s ?? string.Empty).Replace('\u00A0', ' ').Replace('\t', ' ').Replace('\u202F', ' ');
        return Regex.Replace(t, @"\s{2,}", " ").Trim();
    }

    private static List<List<string>> ExtractTableRows(Document pdfDocument)
    {
        var rows = new List<List<string>>();
        foreach (var page in pdfDocument.Pages)
        {
            var absorber = new TableAbsorber();
            absorber.Visit(page);

            foreach (var table in absorber.TableList)
                rows.AddRange(table.RowList.Select(row => row.CellList.Select(static cell =>
                    {
                        if (cell.TextFragments == null) return string.Empty;

                        var raw = cell.TextFragments.Aggregate("", static (current, fragment) => fragment.Segments.Aggregate(current, static (current, seg) => current + seg.Text));
                        return NormalizeSpaces(raw);
                    }).ToList()));
        }

        return rows;
    }

    private static ColumnMap BuildColumnMap(List<List<string>> rows)
    {
        return new ColumnMap(0, 1, 7, 8, 9);
    }

    private static bool IsHeaderRow(IReadOnlyList<string> row)
    {
        var normalized = row.Select(NormalizeHeader).ToList();
        return normalized.Any(static item => item.Contains("дата")) &&
               normalized.Any(static item => item.Contains("дебет")) &&
               normalized.Any(static item => item.Contains("кредит"));
    }

    private static string NormalizeHeader(string input)
    {
        return NormalizeSpaces(input).ToLowerInvariant();
    }

    private static string NormalizeForComparison(string input)
    {
        return MyRegex().Replace(NormalizeSpaces(input), "").ToLowerInvariant();
    }


    private static string GetCell(IReadOnlyList<string> row, int index)
    {
        return index >= 0 && index < row.Count ? row[index] : string.Empty;
    }

    private static bool TryParseAmount(string input, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var cleaned = NormalizeSpaces(input)
            .Replace("−", "-")
            .Replace("+", "");
        cleaned = Regex.Replace(cleaned, @"[^\d,.\-]", "")
            .Replace(" ", "")
            .Replace(",", ".");

        return double.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDate(string input, out DateTime date)
    {
        var txt = NormalizeSpaces(input);
        var formats = new[] { "dd.MM.yyyy", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm" };
        return DateTime.TryParseExact(txt, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private readonly record struct ColumnMap(int Date, int Document, int Debit, int Credit, int Memo);

    [GeneratedRegex(@"\s+")]
    private static partial Regex MyRegex();
}