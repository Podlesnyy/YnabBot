using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Adp.Banks.Interfaces;
using HtmlAgilityPack;
using static System.Text.RegularExpressions.Regex;

namespace Adp.Banks.Tinkoff;

public sealed class BankHtml : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour(string fileName)
    {
        return fileName.Contains("Т-Банк");
    }

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(fileContent);

        // Ищем в порядке документа заголовки дат и блоки операций
        var nodes = doc.DocumentNode.SelectNodes(
            "//p[@content='h2'] | //div[starts-with(@aria-label,'Детали операции')]");

        var transactions = new List<Transaction>();
        string currentDate = null;

        foreach (var node in nodes)
        {
            // Обновляем текущую дату при встрече <p content="h2">
            if (node.Name == "p" && node.GetAttributeValue("content", "") == "h2")
            {
                currentDate = node.InnerText.Trim();
                continue;
            }

            // При встрече блока операции парсим нужные поля
            if (node.Name != "div" || !node.GetAttributeValue("aria-label", "")
                    .StartsWith("Детали операции", StringComparison.Ordinal))
                continue;

            var titleNode = node.SelectSingleNode(".//span[@data-qa-type='atom-operations-feed-operation-title']")
                .InnerText.Trim();
            var amountNode = node.SelectSingleNode(".//span[@data-qa-type='atom-operations-feed-operation-amount']")
                .InnerText.Trim();
            var categoryNode = node.SelectSingleNode(".//p[@data-qa-type='atom-operations-feed-operation-subtitle']")
                .InnerText.Trim();
            var descNode = node.SelectSingleNode(".//div[@data-qa-type='atom-operations-feed-operation-description']")
                .InnerText.Trim();

            DateTime date;
            if (string.Equals(currentDate, "Сегодня", StringComparison.OrdinalIgnoreCase))
                date = DateTime.Today;
            else if (string.Equals(currentDate, "Вчера", StringComparison.OrdinalIgnoreCase))
                date = DateTime.Today.AddDays(-1);
            else
                date = DateTime.Parse(currentDate!, RussianCi);

            transactions.Add(new Transaction
            {
                Date = date, BankAccount = Normalize(descNode), Amount = -1 * Convert(amountNode), Payee = titleNode,
                Memo = categoryNode
            });
        }


        return transactions;
    }

    private static string Normalize(string input)
    {
        // Паттерн — ищем любую из подстрок
        const string pattern = "(Black Premium|Накопительный счет)";
        var match = Match(input, pattern);
        return match.Success ? match.Value : input;
    }

    private static double Convert(string input)
    {
        // 1. Декодируем HTML-сущности (&nbsp; → '\u00A0', &minus; → '−' и т. д.)
        input = WebUtility.HtmlDecode(input);

        // 2. Меняем юникод-минус на обычный дефис
        input = input.Replace('\u2212', '-');

        // 3. Убираем всё, кроме цифр, дефиса, точки и запятой
        input = Replace(input, @"[^\d\-,.]", "");

        // 4. Парсим с учётом русской культуры (запятая разделяет дробную часть)
        if (decimal.TryParse(input,
                NumberStyles.AllowLeadingSign
                | NumberStyles.AllowThousands
                | NumberStyles.AllowDecimalPoint,
                new CultureInfo("ru-RU"),
                out var value))
            return (double)value;

        throw new FormatException($"Cannot parse '{input}' as decimal.");
    }
}