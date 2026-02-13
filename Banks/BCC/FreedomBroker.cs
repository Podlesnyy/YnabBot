using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Adp.Banks.Interfaces;
using OfficeOpenXml;

namespace Adp.Banks.BCC;

// ReSharper disable once UnusedType.Global
public sealed partial class FreedomBroker : IBank
{
    private const string FileNamePrefix = "tradernet_table_";
    private string bankAccount;

    public bool IsItYour( string fileName )
    {
        if ( string.IsNullOrWhiteSpace( fileName ) )
            return false;

        if ( !fileName.Contains( FileNamePrefix, StringComparison.OrdinalIgnoreCase )
             || !fileName.EndsWith( ".xlsx", StringComparison.OrdinalIgnoreCase ) )
            return false;

        var account = ExtractBankAccountFromFileName( fileName );
        if ( string.IsNullOrWhiteSpace( account ) )
            return false;

        bankAccount = account;
        return true;
    }

    public List< Transaction > Parse( MemoryStream stream )
    {
        if ( string.IsNullOrWhiteSpace( bankAccount ) )
            throw new InvalidOperationException( "Cant parse account mapping from file name." );

        stream.Position = 0;
        ExcelPackage.License.SetNonCommercialPersonal( "Ynab" );

        using var package = new ExcelPackage( stream );
        var worksheet = package.Workbook.Worksheets[ 0 ];
        var rowCount = worksheet.Dimension?.Rows ?? 0;

        var transactions = new List< Transaction >();

        for ( var row = 2; row <= rowCount; row++ )
        {
            if ( !TryParseDate( worksheet.Cells[ row, 1 ].Value, out var date ) )
                continue;

            if ( !TryParseAmount( worksheet.Cells[ row, 4 ].Value, out var amount ) )
                continue;

            var operation = Normalize( worksheet.Cells[ row, 2 ].Text );
            var comment = Normalize( worksheet.Cells[ row, 3 ].Text );
            var currency = Normalize( worksheet.Cells[ row, 5 ].Text );
            var memo = ComposeMemo( comment, currency, date );
            var id = ExtractId( operation, comment );
            var bankAccountWithCurrency = string.IsNullOrWhiteSpace( currency ) ? bankAccount : $"{bankAccount}_{currency}";
            var ynabAmount = -1 * amount;

            transactions.Add( new Transaction( bankAccountWithCurrency, date, ynabAmount, memo, 0, id, operation ) );
        }

        return transactions;
    }

    private static string ExtractBankAccountFromFileName( string fileName )
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension( fileName );
        var separatorIndex = fileNameWithoutExtension.LastIndexOf( '_' );

        return separatorIndex < 0 || separatorIndex == fileNameWithoutExtension.Length - 1
                   ? string.Empty
                   : fileNameWithoutExtension[ ( separatorIndex + 1 ).. ];
    }

    private static bool TryParseDate( object rawValue, out DateTime date )
    {
        switch ( rawValue )
        {
            case DateTime dateTime:
                date = dateTime;
                return true;
            case double oaDate:
                date = DateTime.FromOADate( oaDate );
                return true;
            case decimal oaDateDecimal:
                date = DateTime.FromOADate( ( double )oaDateDecimal );
                return true;
        }

        var dateText = Normalize( rawValue?.ToString() );
        if ( string.IsNullOrWhiteSpace( dateText ) )
        {
            date = default;
            return false;
        }

        if ( double.TryParse( dateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericDate ) )
        {
            date = DateTime.FromOADate( numericDate );
            return true;
        }

        var formats = new[] { "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "dd.MM.yyyy", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" };
        if ( DateTime.TryParseExact( dateText, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date ) )
            return true;

        return DateTime.TryParse( dateText, new CultureInfo( "ru-RU" ), DateTimeStyles.None, out date );
    }

    private static bool TryParseAmount( object rawValue, out double amount )
    {
        switch ( rawValue )
        {
            case double parsedAmount:
                amount = parsedAmount;
                return true;
            case decimal parsedAmountDecimal:
                amount = ( double )parsedAmountDecimal;
                return true;
        }

        var text = Normalize( rawValue?.ToString() );
        if ( string.IsNullOrWhiteSpace( text ) )
        {
            amount = default;
            return false;
        }

        var normalized = text.Replace( "−", "-" )
                             .Replace( " ", "" )
                             .Replace( "\u00A0", "" )
                             .Replace( "\u202F", "" )
                             .Replace( ",", "." );
        normalized = AmountCleanerRegex().Replace( normalized, string.Empty );

        return double.TryParse( normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out amount );
    }

    private static string ComposeMemo( string comment, string currency, DateTime date )
    {
        var commentPart = string.IsNullOrWhiteSpace( comment ) ? string.Empty : comment;
        var amountCurrencyPart = string.IsNullOrWhiteSpace( currency ) ? string.Empty : $"Валюта: {currency}";
        var datePart = date.ToString( "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture );

        var parts = new List< string >();
        if ( !string.IsNullOrWhiteSpace( commentPart ) )
            parts.Add( commentPart );
        if ( !string.IsNullOrWhiteSpace( amountCurrencyPart ) )
            parts.Add( amountCurrencyPart );
        if ( !string.IsNullOrWhiteSpace( datePart ) )
            parts.Add( datePart );

        return parts.Count == 0 ? string.Empty : string.Join( ". ", parts );
    }

    private static string ExtractId( string operation, string comment )
    {
        var source = $"{operation} {comment}";
        if ( string.IsNullOrWhiteSpace( source ) )
            return null;

        var match = IdRegex().Match( source );
        return match.Success ? match.Groups[ "id" ].Value : null;
    }

    private static string Normalize( string value ) =>
        string.IsNullOrWhiteSpace( value ) ? string.Empty : value.Trim();

    [GeneratedRegex( @"[^0-9.\-]" )]
    private static partial Regex AmountCleanerRegex();

    [GeneratedRegex( @"(?:поручению|trade)\s*(?<id>\d+)", RegexOptions.IgnoreCase )]
    private static partial Regex IdRegex();
}
