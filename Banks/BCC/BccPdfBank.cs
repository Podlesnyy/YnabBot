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
    private string bankAccount;

    public bool IsItYour( string fileName ) => fileName.Contains( "pkg_w_mb_main" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( MemoryStream fileContent )
    {
        using var pdfDocument = new Document( fileContent );
        var textLines = ExtractTextLines( pdfDocument );

        bankAccount = ExtractBankAccount( textLines );

        var inlineTransactions = ParseInlineRows( textLines );
        var splitTransactions = ParseSplitRows( textLines );
        var drafts = splitTransactions.Count > inlineTransactions.Count ? splitTransactions : inlineTransactions;

        return drafts.Select( CreateTransaction ).ToList();
    }

    private List< DraftTransaction > ParseInlineRows( IReadOnlyList< string > lines )
    {
        var started = false;
        var buffer = new List< string >();
        DraftTransaction current = null;
        var result = new List< DraftTransaction >();

        foreach ( var line in lines )
        {
            if ( !started )
            {
                if ( IsTransactionSectionStart( line ) )
                    started = true;
                continue;
            }

            if ( ShouldSkipLine( line ) )
                continue;

            var rowMatch = InlineTransactionRegex().Match( line );
            if ( rowMatch.Success )
            {
                if ( current != null )
                {
                    SplitTailAndPrelude( buffer, out var tail, out var prelude );
                    current.Memo = AppendMemo( current.Memo, tail );
                    result.Add( current );

                    buffer.Clear();
                    buffer.AddRange( prelude );
                }

                var memoParts = new List< string >( buffer );
                var body = NormalizeSpaces( rowMatch.Groups[ "body" ].Value );
                if ( !string.IsNullOrWhiteSpace( body ) )
                    memoParts.Add( body );

                current = new DraftTransaction(
                    DateTime.ParseExact( rowMatch.Groups[ "date" ].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture ),
                    ParseAmount( rowMatch.Groups[ "amount" ].Value ),
                    ParseAmount( rowMatch.Groups[ "fee" ].Value ),
                    NormalizeMemo( memoParts ) );

                buffer.Clear();
                continue;
            }

            buffer.Add( line );
        }

        if ( current != null )
        {
            current.Memo = AppendMemo( current.Memo, buffer );
            result.Add( current );
        }

        return result;
    }

    private List< DraftTransaction > ParseSplitRows( IReadOnlyList< string > lines )
    {
        var started = false;
        var memoBuffer = new List< string >();
        DateTime? pendingDate = null;
        double? pendingAmount = null;
        var result = new List< DraftTransaction >();

        foreach ( var line in lines )
        {
            if ( !started )
            {
                if ( IsTransactionSectionStart( line ) )
                    started = true;
                continue;
            }

            if ( ShouldSkipLine( line ) )
                continue;

            if ( DateOnlyRegex().IsMatch( line ) )
            {
                pendingDate = DateTime.ParseExact( line, "dd.MM.yyyy", CultureInfo.InvariantCulture );
                pendingAmount = null;
                continue;
            }

            if ( AmountOnlyRegex().IsMatch( line ) )
            {
                if ( pendingDate == null )
                    continue;

                if ( pendingAmount == null )
                {
                    pendingAmount = ParseAmount( line );
                    continue;
                }

                var commission = ParseAmount( line );
                result.Add( new DraftTransaction( pendingDate.Value, pendingAmount.Value, commission,
                                                  NormalizeMemo( memoBuffer ) ) );

                memoBuffer.Clear();
                pendingDate = null;
                pendingAmount = null;
                continue;
            }

            memoBuffer.Add( line );
        }

        return result;
    }

    private static bool IsTransactionSectionStart( string line ) =>
        line.Contains( "Описание операции", StringComparison.OrdinalIgnoreCase );

    private static bool ShouldSkipLine( string line )
    {
        var lower = line.ToLowerInvariant();
        return lower.StartsWith( "дата описание операции", StringComparison.Ordinal ) ||
               lower == "дата" ||
               lower == "описание операции" ||
               lower.StartsWith( "сумма в", StringComparison.Ordinal ) ||
               lower.StartsWith( "комиссия", StringComparison.Ordinal );
    }

    private static void SplitTailAndPrelude( IReadOnlyList< string > buffer, out List< string > tail,
                                             out List< string > prelude )
    {
        var markerIndex = -1;
        for ( var i = 0; i < buffer.Count; i++ )
            if ( IsOperationStartLine( buffer[ i ] ) )
            {
                markerIndex = i;
                break;
            }

        if ( markerIndex < 0 )
        {
            tail = buffer.ToList();
            prelude = [];
            return;
        }

        tail = buffer.Take( markerIndex ).ToList();
        prelude = buffer.Skip( markerIndex ).ToList();
    }

    private static bool IsOperationStartLine( string line )
    {
        var lower = line.ToLowerInvariant();
        return lower.StartsWith( "retail.", StringComparison.Ordinal ) ||
               lower.StartsWith( "перевод (", StringComparison.Ordinal ) ||
               lower.StartsWith( "конверсия валюты", StringComparison.Ordinal ) ||
               lower.StartsWith( "продажа иностранной валюты", StringComparison.Ordinal ) ||
               lower.StartsWith( "согласно с файла", StringComparison.Ordinal ) ||
               lower.StartsWith( "снятие наличных", StringComparison.Ordinal );
    }

    private Transaction CreateTransaction( DraftTransaction draft )
    {
        var memo = NormalizeSpaces( draft.Memo );
        var id = TryExtractId( memo );
        return new Transaction( bankAccount, draft.Date, draft.Amount, memo, 0, id, null );
    }

    private static List< string > ExtractTextLines( Document pdfDocument )
    {
        var textAbsorber = new TextAbsorber();
        pdfDocument.Pages.Accept( textAbsorber );

        return textAbsorber.Text
                           .Split( [ "\r\n", "\n", "\r" ], StringSplitOptions.None )
                           .Select( NormalizeSpaces )
                           .Where( static line => !string.IsNullOrWhiteSpace( line ) )
                           .ToList();
    }

    private static string ExtractBankAccount( IEnumerable< string > lines )
    {
        var text = string.Join( " ", lines );
        var match = BankAccountRegex().Match( text );
        if ( match.Success )
            return match.Groups[ 1 ].Value;

        throw new Exception( "Не удалось найти номер счета." );
    }

    private static string TryExtractId( string memo )
    {
        if ( string.IsNullOrWhiteSpace( memo ) )
            return null;

        var refMatch = ReferenceRegex().Match( memo );
        if ( refMatch.Success )
            return refMatch.Groups[ 1 ].Value;

        var requestMatch = RequestRegex().Match( memo );
        if ( requestMatch.Success )
            return requestMatch.Groups[ 1 ].Value;

        var arnMatch = ArnRegex().Match( memo );
        return arnMatch.Success ? arnMatch.Groups[ 1 ].Value : null;
    }

    private static double ParseAmount( string value )
    {
        var normalized = NormalizeSpaces( value ).Replace( "−", "-" )
                                                 .Replace( " ", string.Empty )
                                                 .Replace( ",", "." );
        return double.Parse( normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                             CultureInfo.InvariantCulture );
    }

    private static string NormalizeMemo( IEnumerable< string > lines ) => NormalizeSpaces( string.Join( " ", lines ) );

    private static string AppendMemo( string source, IEnumerable< string > append ) =>
        NormalizeSpaces( $"{source} {string.Join( " ", append )}" );

    private static string NormalizeSpaces( string value )
    {
        var source = value ?? string.Empty;
        source = source.Replace( '\u00A0', ' ' )
                       .Replace( '\u202F', ' ' )
                       .Replace( '\t', ' ' );
        return MultipleSpacesRegex().Replace( source, " " ).Trim();
    }

    private sealed class DraftTransaction
    {
        public DraftTransaction( DateTime date, double amount, double commission, string memo )
        {
            Date = date;
            Amount = amount;
            Commission = commission;
            Memo = memo;
        }

        public DateTime Date { get; }
        public double Amount { get; }
        public double Commission { get; }
        public string Memo { get; set; }
    }

    [GeneratedRegex( @"^\d{2}\.\d{2}\.\d{4}$" )]
    private static partial Regex DateOnlyRegex();

    [GeneratedRegex( @"^[+\-−]?\d{1,3}(?:\s\d{3})*,\d{2}$" )]
    private static partial Regex AmountOnlyRegex();

    [GeneratedRegex( @"^(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<body>.*?)(?<amount>[+\-−]?\d{1,3}(?:\s\d{3})*,\d{2})\s+(?<fee>[+\-−]?\d{1,3}(?:\s\d{3})*,\d{2})$" )]
    private static partial Regex InlineTransactionRegex();

    [GeneratedRegex( @"\b(KZ\d{18,30})\b" )]
    private static partial Regex BankAccountRegex();

    [GeneratedRegex( @"Референс:\s*([A-Za-z0-9]+)", RegexOptions.IgnoreCase )]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex( @"З[ая]вка\s*№\s*([A-Za-z0-9]+)", RegexOptions.IgnoreCase )]
    private static partial Regex RequestRegex();

    [GeneratedRegex( @"ARN-?\s*([A-Za-z0-9]+)", RegexOptions.IgnoreCase )]
    private static partial Regex ArnRegex();

    [GeneratedRegex( @"\s+" )]
    private static partial Regex MultipleSpacesRegex();
}
