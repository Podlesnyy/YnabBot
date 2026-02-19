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
public sealed partial class KaspiPdf : IBank
{
    public bool IsItYour( string fileName )
    {
        if ( string.IsNullOrWhiteSpace( fileName ) )
            return false;

        return fileName.Contains( "gold_statement", StringComparison.OrdinalIgnoreCase )
               && fileName.EndsWith( ".pdf", StringComparison.OrdinalIgnoreCase );
    }

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( MemoryStream stream )
    {
        stream.Position = 0;
        using var pdfDocument = new Document( stream );
        var bankAccount = ExtractBankAccountFromCell( pdfDocument );
        var lines = ExtractLines( pdfDocument );
        var transactions = new List< Transaction >();

        foreach ( var line in lines )
        {
            var match = TransactionLineRegex().Match( line );
            if ( !match.Success )
                continue;

            var dateText = NormalizeSpaces( match.Groups[ "date" ].Value );
            if ( !TryParseDate( dateText, out var date ) )
                continue;

            var amountText = NormalizeSpaces( match.Groups[ "amount" ].Value );
            if ( !TryParseSignedAmount( amountText, out var signedAmount ) )
                continue;

            if ( Math.Abs( signedAmount ) < 0.0001 )
                continue;

            var operation = NormalizeSpaces( match.Groups[ "operation" ].Value );
            var details = NormalizeSpaces( match.Groups[ "details" ].Value );
            var memo = string.IsNullOrWhiteSpace( details ) ? operation : $"{operation} {details}";
            var ynabAmount = -1 * signedAmount;

            transactions.Add( new Transaction( bankAccount, date, ynabAmount, memo, 0, null, operation ) );
        }

        return transactions;
    }

    private static string ExtractBankAccountFromCell( Document pdfDocument )
    {
        foreach ( var page in pdfDocument.Pages )
        {
            var tableAbsorber = new TableAbsorber();
            tableAbsorber.Visit( page );

            foreach ( var table in tableAbsorber.TableList )
            {
                foreach ( var row in table.RowList )
                {
                    var cells = row.CellList.Select( static cell => NormalizeSpaces( GetCellText( cell ) ) )
                                   .Where( static cell => !string.IsNullOrWhiteSpace( cell ) )
                                   .ToList();
                    if ( cells.Count == 0 )
                        continue;

                    for ( var i = 0; i < cells.Count; i++ )
                    {
                        var normalized = NormalizeForComparison( cells[ i ] );
                        if ( normalized != "номерсчета" && normalized != "номерсчёта" )
                            continue;

                        if ( i + 1 < cells.Count && TryExtractBankAccount( cells[ i + 1 ], out var accountInNextCell ) )
                            return accountInNextCell;

                        if ( TryExtractBankAccount( string.Join( " ", cells ), out var accountInRow ) )
                            return accountInRow;
                    }
                }
            }
        }

        var textAbsorber = new TextAbsorber();
        pdfDocument.Pages.Accept( textAbsorber );
        if ( TryExtractBankAccount( textAbsorber.Text, out var bankAccount ) )
            return bankAccount;

        throw new Exception( "Не удалось найти номер счета в ячейке 'Номер счета'." );
    }

    private static List< string > ExtractLines( Document pdfDocument )
    {
        var textAbsorber = new TextAbsorber();
        pdfDocument.Pages.Accept( textAbsorber );

        return textAbsorber.Text.Split( [ "\r\n", "\r", "\n" ], StringSplitOptions.RemoveEmptyEntries )
                           .Select( NormalizeSpaces )
                           .Where( static line => !string.IsNullOrWhiteSpace( line ) )
                           .ToList();
    }

    private static string GetCellText( AbsorbedCell cell )
    {
        if ( cell.TextFragments == null )
            return string.Empty;

        return cell.TextFragments.Aggregate( "", static ( current, fragment ) =>
            fragment.Segments.Aggregate( current, static ( value, segment ) => value + segment.Text ) );
    }

    private static bool TryExtractBankAccount( string input, out string bankAccount )
    {
        bankAccount = null;
        if ( string.IsNullOrWhiteSpace( input ) )
            return false;

        var match = BankAccountRegex().Match( input.ToUpperInvariant() );
        if ( !match.Success )
            return false;

        bankAccount = match.Value;
        return true;
    }

    private static bool TryParseDate( string input, out DateTime date )
    {
        date = default;
        var normalized = NormalizeSpaces( input );
        if ( string.IsNullOrWhiteSpace( normalized ) )
            return false;

        if ( DateTime.TryParseExact( normalized, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out date ) )
            return true;

        var parts = normalized.Split( '.', StringSplitOptions.RemoveEmptyEntries );
        if ( parts.Length != 3 || parts[ 2 ].Length != 2 )
            return false;

        if ( !int.TryParse( parts[ 0 ], NumberStyles.None, CultureInfo.InvariantCulture, out var day )
             || !int.TryParse( parts[ 1 ], NumberStyles.None, CultureInfo.InvariantCulture, out var month )
             || !int.TryParse( parts[ 2 ], NumberStyles.None, CultureInfo.InvariantCulture, out var year ) )
            return false;

        try
        {
            date = new DateTime( year + 2000, month, day );
            return true;
        }
        catch ( ArgumentOutOfRangeException )
        {
            return false;
        }
    }

    private static bool TryParseSignedAmount( string input, out double value )
    {
        value = default;
        if ( string.IsNullOrWhiteSpace( input ) )
            return false;

        var normalized = NormalizeSpaces( input ).Replace( "−", "-" )
                                                 .Replace( " ", "" )
                                                 .Replace( "\u00A0", "" )
                                                 .Replace( "\u202F", "" )
                                                 .Replace( ",", "." );
        normalized = AmountCleanerRegex().Replace( normalized, string.Empty );

        return double.TryParse( normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out value );
    }

    private static string NormalizeSpaces( string input )
    {
        var value = ( input ?? string.Empty ).Replace( '\u00A0', ' ' )
                                             .Replace( '\u202F', ' ' )
                                             .Replace( '\t', ' ' );
        return MultipleSpaceRegex().Replace( value, " " ).Trim();
    }

    private static string NormalizeForComparison( string input ) =>
        ComparisonCleanerRegex().Replace( NormalizeSpaces( input ).ToLowerInvariant(), string.Empty );

    [GeneratedRegex( @"^(?<date>\d{2}\.\d{2}\.(?:\d{2}|\d{4}))\s+(?<amount>[+\-−]\s*\d[\d\s\u00A0\u202F]*,\d{2})\s*₸\s+(?<operation>\S+)(?:\s+(?<details>.*))?$" )]
    private static partial Regex TransactionLineRegex();

    [GeneratedRegex( @"KZ[0-9A-Z]{10,}", RegexOptions.IgnoreCase )]
    private static partial Regex BankAccountRegex();

    [GeneratedRegex( @"[^0-9+\-.]" )]
    private static partial Regex AmountCleanerRegex();

    [GeneratedRegex( @"[^a-zа-яё0-9]" )]
    private static partial Regex ComparisonCleanerRegex();

    [GeneratedRegex( @"\s+" )]
    private static partial Regex MultipleSpaceRegex();
}
