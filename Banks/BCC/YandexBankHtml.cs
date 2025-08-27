using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using Adp.Banks.Interfaces;
using HtmlAgilityPack;
using JetBrains.Annotations;
using static System.Text.RegularExpressions.Regex;

namespace Adp.Banks.BCC;

[UsedImplicitly]
public sealed class YandexBankHtml : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour(string fileName)
    {
        return fileName.Contains("Яндекс");
    }

    public string FileEncoding => "utf-8";

    public List<Transaction> Parse(string fileContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(fileContent);

        var result = new List<Transaction>();
        string currentDate = null;

        // Все узлы с атрибутом data-index
        var nodes = doc.DocumentNode.SelectNodes("//div[@data-index]");
        if (nodes == null)
            return null;

        foreach (var node in nodes)
        {
            // 1) Это заголовок даты?
            var h3 = node.SelectSingleNode(".//h3");
            if (h3 != null)
            {
                currentDate = h3.InnerText.Trim();
                continue;
            }

            // 2) Это транзакция?
            var link = node.SelectSingleNode(".//a");
            if (link == null)
                continue;

            var titlePs = link.SelectNodes(".//div[contains(@class,'OperationTitle-module')]/p");
            var title = "";
            if (titlePs != null && titlePs.Count >= 2)
                title = $"{titlePs[0].InnerText.Trim()} → {titlePs[1].InnerText.Trim()}";

            // Название операции
            var nameNode = link.SelectSingleNode(".//p[contains(@class,'operationName')]");
            // Описание под названием
            var descNode = link.SelectSingleNode(".//span[contains(@class,'operationDescription')]");
            // Сумма
            var amountNode = link.SelectSingleNode(".//p[contains(@class,'balanceChange')]");
            // Есть ли иконка восклицания
            var warningIcon = amountNode.ParentNode.SelectSingleNode(".//svg[@width='16' and @height='16']");

            // 5) Текст под суммой
            var accNode = link.SelectSingleNode(".//span[contains(@class,'statusText')]");
            var acc = accNode?.InnerText.Trim() ?? "";

            var amount = Convert(amountNode.InnerText);
            DateTime date;
            if (string.Equals(currentDate, "Сегодня", StringComparison.OrdinalIgnoreCase))
                date = DateTime.Today;
            else if (string.Equals(currentDate, "Вчера", StringComparison.OrdinalIgnoreCase))
                date = DateTime.Today.AddDays(-1);
            else
                date = DateTime.Parse(currentDate!, RussianCi);

            // 6) ссылка на картинку
            var imgNode = link.SelectSingleNode(".//img");
            var imgUrl = imgNode?.GetAttributeValue("src", "").Trim() ?? "";

            if (warningIcon != null) continue;

            var payee = nameNode?.InnerText.Trim() ?? "";
            if (payee == "Выплата процентов")
                payee = "Проценты";

            if (acc == "")
                acc = "Карта Пэй";
            result.Add(new Transaction
            {
                Date = date, Payee = payee, Memo = $"{descNode?.InnerText.Trim() ?? ""} {ExtractKey(imgUrl)}",
                Amount = -1 * amount, BankAccount = acc
            });
        }


        return result;
    }

    public static string ExtractKey(string url)
    {
        var path = new Uri(url).AbsolutePath.TrimEnd('/');
        // последний сегмент пути — например "savings_light.png"
        var lastSegment = path.Split('/')[^2];
        // убираем расширение → "savings_light"
        return Path.GetFileNameWithoutExtension(lastSegment);
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