using System.Globalization;
using System.Text;
using Adp.Banks.Interfaces;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace OzonBank;

public sealed class OzonBank : IBank
{
    private static readonly CultureInfo RussianCi = new("ru");

    public bool IsItYour( string fileName ) => fileName.Contains( "ozonbank" );

    public string FileEncoding => "utf-8";

    public List< Transaction > Parse( MemoryStream fileContent )
    {
        var tablesText = ExtractFromPdfFile( fileContent );
        var csvString = ConvertToCsv( tablesText );
        var transactions = GetTransactions( csvString );

        return transactions;
    }

    private static List< Transaction > GetTransactions( string csvString )
    {
        using var reader = new StringReader( csvString );
        var config = new CsvConfiguration( RussianCi ) { Delimiter = ",", HasHeaderRecord = true, BadDataFound = null };
        using var csv = new CsvReader( reader, config );
        csv.Context.RegisterClassMap< TransactionMap >();
        return csv.GetRecords< Transaction >().ToList();
    }

    private string ConvertToCsv( IReadOnlyList< string > tablesText )
    {
        // Считываем строки из файла
        var csvBuilder = new StringBuilder();

        // Заголовок для CSV
        csvBuilder.AppendLine( "Дата операции,Документ,Назначение платежа,Сумма операции" );

        // Обработка строк
        for ( var i = 0; i < tablesText.Count; i++ )
        {
            var line = tablesText[ i ].Trim();

            // Пропускаем строки, начинающиеся с "ООО <ОЗОН БАНК>"
            if ( line.StartsWith( "ООО <ОЗОН БАНК>", StringComparison.Ordinal ) || string.IsNullOrWhiteSpace( line ) )
                continue;

            // Если строка соответствует началу новой записи (дата), собираем данные
            if ( !DateTime.TryParse( line.Split( ' ' )[ 0 ], out _ ) )
                continue;

            // Проверяем, что следующие строки корректно собирают запись
            var document = i + 1 < tablesText.Count ? tablesText[ i + 1 ].Trim() : string.Empty;
            var description = i + 2 < tablesText.Count ? tablesText[ i + 2 ].Trim() : string.Empty;
            var amount = i + 3 < tablesText.Count ? tablesText[ i + 3 ].Trim() : string.Empty;

            // Добавляем запись в CSV
            csvBuilder.AppendLine( $"\"{line}\",\"{document}\",\"{description}\",\"{amount}\"" );

            // Пропускаем уже обработанные строки
            i += 3;
        }

        return csvBuilder.ToString();
    }

    private static List< string > ExtractFromPdfFile( Stream fileContent )
    {
        var pdfDocument = new Document( fileContent );
        var ret = new List< string >();
        foreach ( var page in pdfDocument.Pages )
        {
            var absorber = new TableAbsorber();
            absorber.Visit( page );

            foreach ( var table in absorber.TableList )
            {
                ret.AddRange( from row in table.RowList select row.CellList.Aggregate( "",
                    static ( current, cell ) => cell.TextFragments.Aggregate( current, static ( current, fragment ) => fragment.Segments.Aggregate( current, static ( current, seg ) => current + seg.Text ) ) ) );
            }
        }

        return ret;
    }
}