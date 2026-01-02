using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Adp.Banks.BCC;

// ReSharper disable once UnusedType.Global
public sealed class FreedomKzPdfBank : IBank
{
    private string bankAccount;

    public bool IsItYour( string fileName ) => fileName.Contains( "legal_statement" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( MemoryStream fileContent )
    {
        var extractFromPdfFile = ExtractLines( fileContent );
        GetBankAccount( extractFromPdfFile );
        var transactions = GetTransactions( extractFromPdfFile );

        return transactions;
    }

    private void GetBankAccount( IEnumerable< string > extractFromPdfFile )
    {
        // Ищем строку вида: "Счет: KZ71551A600002124455"
        const string pattern = @"Счет:\s*(KZ[0-9A-Z]+)";
        var regex = new Regex( pattern );

        foreach ( var line in extractFromPdfFile )
        {
            var match = regex.Match( line );
            if ( match.Success )
            {
                bankAccount = match.Groups[ 1 ].Value;
                return;
            }
        }

        throw new Exception( "Не удалось найти номер счета." );
    }

    private List< Transaction > GetTransactions( IReadOnlyList< string > pdfTextLines )
    {
        const string datePattern = @"^\s*\d{2}\.\d{2}\.\d{4}";
        var ret = new List< Transaction >();
        for ( var i = 0; i < pdfTextLines.Count - 1; i++ )
            if ( Regex.IsMatch( pdfTextLines[ i ], datePattern ) && Regex.IsMatch( pdfTextLines[ i + 1 ], datePattern ) )
            {
                var firstTransactionLine = pdfTextLines[ i ];
                var (transaction, time) = CreateTransaction( firstTransactionLine );
                var secondTransactionLine = pdfTextLines[ ++i ];
                transaction.Memo = GetMemo( secondTransactionLine );
                while ( pdfTextLines[ ++i ].Trim() != "" )
                    transaction.Memo += " " + pdfTextLines[ i ].Trim();
                transaction.Memo += " " + time;

                ret.Add( transaction );
            }

        return ret;
    }

    private static string GetMemo( string secondTransactionLine )
    {
        const string pattern = @"\d{2}\.\d{2}\.\d{4}\s+(.*)";
        var regex = new Regex( pattern );
        var match = regex.Match( secondTransactionLine );
        if ( match.Success )
            return match.Groups[ 1 ].Value.Trim(); // Возвращаем текст после даты без лишних пробелов

        throw new Exception( "Не удалось распарсить строку получателя." );
    }

    static List<string> ExtractLines(Stream fileContent)
    {
        var lines = new List<(double y, List<Word> words)>();
        using var doc = PdfDocument.Open(fileContent);
        const double yTol = 5.8; // допуск склейки слов в одну строку

        foreach (var page in doc.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                var y = word.BoundingBox.Bottom;
                var line = lines.FirstOrDefault(l => Math.Abs(l.y - y) < yTol);
                if (line.words == null)
                {
                    lines.Add((y, new List<Word> { word }));
                }
                else
                {
                    line.words.Add(word);
                }
            }
        }

        // сортировка сверху-вниз, слева-направо
        var textLines = lines
                        .OrderByDescending(l => l.y)
                        .Select(l => string.Join(" ", l.words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))
                        .Select(t => NormalizeSpaces(t))
                        .ToList();

        return textLines;
    }

    static string NormalizeSpaces(string s)
    {
        // приводим множественные пробелы к одному, убираем неразрывные и табы
        var t = s.Replace('\u00A0', ' ').Replace('\t', ' ');
        return Regex.Replace(t, @"\s{2,}", " ").Trim();
    }
    private (Transaction, string timeString) CreateTransaction( string firstTransactionLine )
    {
        const string pattern =
            @"(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<time>\d{2}:\d{2})\s+(?<authCode>\d+)\s+(?<description>.+?)\s+(?<sum>[+-]?\d{1,3}(?:\s?\d{3})*,\d{2})";

        var match = Regex.Match( firstTransactionLine, pattern );

        if ( !match.Success )
            throw new Exception( "Не удалось распарсить строку транзакции." );

        var dateString = match.Groups[ "date" ].Value;
        var timeString = match.Groups[ "time" ].Value;
        var description = match.Groups[ "description" ].Value.Trim();
        var authCode = match.Groups[ "authCode" ].Value;
        var sumString =
            match.Groups[ "sum" ]
                 .Value.Replace( " ", "" )
                 .Replace( "\u00A0", "" ); // Убираем пробелы для корректного парсинга числа
        if ( !sumString.StartsWith( "+", StringComparison.Ordinal ) )
            sumString = "-" + sumString;

        // Преобразование в DateTime для даты
        var date = DateTime.ParseExact( dateString, "dd.MM.yyyy", CultureInfo.InvariantCulture );
        // Преобразование суммы в double
        var sum = -1
                  * double.Parse( sumString, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                  new CultureInfo( "ru-RU" ) );

        return ( new Transaction( bankAccount, date, sum, null, 0, authCode, description ), timeString );
    }
}