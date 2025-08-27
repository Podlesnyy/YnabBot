using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using HtmlAgilityPack;

namespace Adp.Banks.BCC;

public sealed partial class BccHtml : IBank
{
    private static readonly CultureInfo RussianCi = new( "ru" );

    public bool IsItYour( string fileName ) => fileName.Contains( "bcc" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( string fileContent )
    {
        var doc = new HtmlDocument();
        doc.LoadHtml( fileContent );

        var result = new List< Transaction >();
        var items = doc.DocumentNode.SelectNodes( "//div[contains(@class,'history__list__item')]" );
        if ( items == null )
            return result;

        foreach ( var item in items )
            try
            {
                // дата
                var dateNode =
                    item.SelectSingleNode( ".//div[contains(@class,'fw-color-steel') and contains(@class,'fw-fz-12')]" );
                var dateStr = NormalizeSpaces( dateNode?.InnerText ?? "" );
                // формат из выписки: "26.08.2025"
                if ( !DateTime.TryParseExact( dateStr, "dd.MM.yyyy", new CultureInfo( "ru-RU" ), DateTimeStyles.None,
                                              out var date ) )
                    // иногда дата может быть в другом формате — можно расширить парсер
                    continue; // пропустить, если не распознали

                // описание и тип
                // первый div внутри "правого" текстового блока содержит либо "Списание", либо строку контрагента
                var textBlock = item.SelectSingleNode( ".//div[contains(@class,'ml-2') or contains(@class,'ml-md-3')]" );
                // там обычно два div подряд: [0] — тип/описание, [1] — дата
                var innerDivs = textBlock?.SelectNodes( ".//div" );
                var description = NormalizeSpaces( innerDivs?.FirstOrDefault()?.InnerText ?? "" );

                // сумма
                var amountNode = item.SelectSingleNode( ".//div[contains(@class,'money-info')]//div" );
                var (amount, currency) = ParseAmount( NormalizeSpaces( amountNode?.InnerText ?? "" ) );

                var acc = currency switch
                          {
                              "₽" => "bccironrub",
                              "$" => "bccironusd",
                              "€" => "bccironeuro",
                              "₸" => "bccirontenge",
                              _ => "BCC_UNKNOWN",
                          };

                result.Add( new Transaction { Date = date, Payee = description, Amount = -1 * amount, BankAccount = acc } );
            }
            catch ( Exception e )
            {
                Console.WriteLine( e.Message );
            }

        return result;
    }

    private static string NormalizeSpaces( string s ) => s?.Replace( '\u00A0', ' ' ).Replace( '\u202F', ' ' ).Trim() ?? "";

    private static (double amount, string currency) ParseAmount( string input )
    {
        if ( string.IsNullOrWhiteSpace( input ) )
            throw new ArgumentException( "Пустая строка" );

        // Нормализация пробелов и минусов
        var txt = input.Replace( '\u00A0', ' ' ) // неразрывные пробелы
                       .Replace( '\u202F', ' ' ) // узкие пробелы
                       .Replace( "−", "-" ) // минус из Word
                       .Trim();

        // Определим валютный символ (последние буквы/символы)
        var currencyMatch = MyRegex().Match( txt );
        var currency = currencyMatch.Success ? currencyMatch.Value : "";

        // Извлекаем число
        var numberMatch = MyRegex1().Match( txt );
        if ( !numberMatch.Success )
            throw new FormatException( "Не удалось распознать число" );

        var numberPart = numberMatch.Groups[ 1 ].Value.Replace( " ", "" ).Replace( ",", "." );
        if ( !double.TryParse( numberPart, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                               CultureInfo.InvariantCulture, out var amount ) )
            throw new FormatException( $"Ошибка конвертации суммы: {numberPart}" );

        // Учитываем знак
        if ( txt.Contains( "-" ) )
            amount = -amount;

        return ( amount, currency );
    }

    [GeneratedRegex( @"([^\d\s.,-]+)$" )]
    private static partial Regex MyRegex();

    [GeneratedRegex( @"[-+]?\s*([\d\s]+(?:[.,]\d{1,2})?)" )]
    private static partial Regex MyRegex1();
}