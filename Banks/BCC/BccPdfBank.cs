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
public sealed partial class BccPdfBank : IBank
{
    public bool IsItYour( string fileName ) => fileName.Contains( "pkg_w_mb_main" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( MemoryStream fileContent )
    {
        using var pdfDocument = new Document( fileContent );
        var bankAccount = ExtractBankAccount( pdfDocument );
        var tableRows = ExtractTableRows( pdfDocument );
        if ( tableRows.Count == 0 )
            return [];

        var columnMap = BuildColumnMap( tableRows );
        var transactions = new List< Transaction >();
        Transaction current = null;
        var inTransactions = false;

        foreach ( var row in tableRows )
        {
            if ( row.All( static item => string.IsNullOrWhiteSpace( item ) ) )
                continue;

            if ( IsHeaderRow( row ) )
            {
                inTransactions = true;
                continue;
            }

            if ( !inTransactions )
                continue;

            var dateText = GetCell( row, columnMap.Date );
            var memoText = NormalizeSpaces( GetCell( row, columnMap.Memo ) );
            var amountText = GetCell( row, columnMap.Amount );
            var commissionText = GetCell( row, columnMap.Commission );

            if ( TryParseDate( dateText, out var date ) )
            {
                if ( !TryParseAmount( amountText, out var amount ) )
                    continue;

                if ( TryParseAmount( commissionText, out var commission ) && Math.Abs( commission ) > 0.0001 )
                {
                    amount -= Math.Abs( commission );
                    if ( !string.IsNullOrWhiteSpace( memoText ) )
                        memoText = $"{memoText} (комиссия {NormalizeSpaces( commissionText )})";
                    else
                        memoText = $"Комиссия {NormalizeSpaces( commissionText )}";
                }

                var transaction = new Transaction( bankAccount, date, amount, memoText, 0, null, "" );
                transactions.Add( transaction );
                current = transaction;
            }
            else
            {
                if ( current != null && !string.IsNullOrWhiteSpace( memoText ) )
                    current.Memo = NormalizeSpaces( $"{current.Memo} {memoText}" );
            }
        }

        return transactions;
    }

    private static string ExtractBankAccount( Document pdfDocument )
    {
        var textAbsorber = new TextAbsorber();
        pdfDocument.Pages.Accept( textAbsorber );
        var text = NormalizeSpaces( textAbsorber.Text );

        var patterns = new[]
        {
            @"Выписка\s*по\s*счету\s*(KZ[0-9]{18,})",
            @"по\s*счету\s*(KZ[0-9]{18,})",
            @"Счет\s*[:№]?\s*(KZ[0-9]{18,})",
            @"(KZ[0-9]{18,})"
        };

        foreach ( var pattern in patterns )
        {
            var match = Regex.Match( text, pattern, RegexOptions.IgnoreCase );
            if ( match.Success )
                return match.Groups.Count > 1 ? match.Groups[ 1 ].Value : match.Value;
        }

        throw new Exception( "Не удалось найти номер счета." );
    }

    private static List< List< string > > ExtractTableRows( Document pdfDocument )
    {
        var rows = new List< List< string > >();
        foreach ( var page in pdfDocument.Pages )
        {
            var absorber = new TableAbsorber();
            absorber.Visit( page );

            foreach ( var table in absorber.TableList )
                rows.AddRange( table.RowList.Select( row => row.CellList.Select( static cell =>
                {
                    if ( cell.TextFragments == null )
                        return string.Empty;

                    var raw = cell.TextFragments.Aggregate( "", static ( current, fragment ) =>
                        fragment.Segments.Aggregate( current, static ( current, seg ) => current + seg.Text ) );
                    return NormalizeSpaces( raw );
                } ).ToList() ) );
        }

        return rows;
    }

    private static ColumnMap BuildColumnMap( List< List< string > > rows )
    {
        foreach ( var row in rows )
        {
            var normalized = row.Select( NormalizeHeader ).ToList();
            if ( normalized.Count == 0 )
                continue;

            if ( normalized.Any( static item => item.Contains( "дата" ) ) &&
                 normalized.Any( static item => item.Contains( "опис" ) ) &&
                 normalized.Any( static item => item.Contains( "сумма" ) ) )
            {
                var date = normalized.FindIndex( static item => item.Contains( "дата" ) );
                var memo = normalized.FindIndex( static item => item.Contains( "опис" ) );
                var amount = normalized.FindIndex( static item => item.Contains( "сумма" ) );
                var commission = normalized.FindIndex( static item => item.Contains( "комис" ) );
                return new ColumnMap(
                    date == -1 ? 0 : date,
                    memo == -1 ? 1 : memo,
                    amount == -1 ? 2 : amount,
                    commission == -1 ? 3 : commission );
            }
        }

        return new ColumnMap( 0, 1, 2, 3 );
    }

    private static bool IsHeaderRow( IReadOnlyList< string > row )
    {
        var normalized = row.Select( NormalizeHeader ).ToList();
        return normalized.Any( static item => item.Contains( "дата" ) ) &&
               normalized.Any( static item => item.Contains( "опис" ) ) &&
               normalized.Any( static item => item.Contains( "сумма" ) );
    }

    private static string NormalizeHeader( string input )
    {
        return NormalizeSpaces( input ).ToLowerInvariant();
    }

    private static string NormalizeSpaces( string s )
    {
        var t = ( s ?? string.Empty ).Replace( '\u00A0', ' ' ).Replace( '\t', ' ' ).Replace( '\u202F', ' ' );
        return MyRegex().Replace( t, " " ).Trim();
    }

    private static string GetCell( IReadOnlyList< string > row, int index )
    {
        return index >= 0 && index < row.Count ? row[ index ] : string.Empty;
    }

    private static bool TryParseAmount( string input, out double value )
    {
        value = 0;
        if ( string.IsNullOrWhiteSpace( input ) )
            return false;

        var cleaned = NormalizeSpaces( input )
            .Replace( "−", "-" )
            .Replace( "–", "-" )
            .Replace( "+", "" );

        cleaned = Regex.Replace( cleaned, @"[^\d,\.\-]", "" );

        if ( cleaned.Count( static c => c == ',' ) > 1 && !cleaned.Contains( '.' ) )
        {
            var last = cleaned.LastIndexOf( ',' );
            var filtered = new char[ cleaned.Length ];
            var idx = 0;
            for ( var i = 0; i < cleaned.Length; i++ )
            {
                var ch = cleaned[ i ];
                if ( ch == ',' && i != last )
                    continue;
                filtered[ idx++ ] = ch;
            }

            cleaned = new string( filtered, 0, idx );
        }

        cleaned = cleaned.Replace( " ", "" ).Replace( ",", "." );

        return double.TryParse( cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value );
    }

    private static bool TryParseDate( string input, out DateTime date )
    {
        var txt = NormalizeSpaces( input );
        var formats = new[] { "dd.MM.yyyy", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm" };
        return DateTime.TryParseExact( txt, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date );
    }

    private readonly record struct ColumnMap( int Date, int Memo, int Amount, int Commission );

    [GeneratedRegex( @"\s+" )]
    private static partial Regex MyRegex();
}
