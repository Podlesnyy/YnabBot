using System.Globalization;
using Adp.Banks.Interfaces;
using CsvHelper.Configuration;

namespace OzonBank;

public sealed class TransactionMap : ClassMap< Transaction >
{
    public TransactionMap()
    {
        Map( static m => m.Date ).Name( "Дата операции" ).TypeConverterOption.Format( "dd.MM.yyyy HH:mm:ss" );
        Map( static m => m.Id ).Name( "Документ" );
        Map( static m => m.Memo ).Name( "Назначение платежа" );
        Map( static m => m.Amount ).
            Name( "Сумма операции" ).
            Convert( static args =>
                     {
                         var value = args.Row.GetField( "Сумма операции" ).Replace( " ", "" ).Replace( "+", "" );
                         return double.Parse( value, CultureInfo.InvariantCulture );
                     } );

        Map(static m => m.BankAccount).Constant( "ozon_rub_account");
    }
}