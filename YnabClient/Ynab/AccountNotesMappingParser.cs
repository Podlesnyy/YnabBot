using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Adp.YnabClient.Ynab;

internal static class AccountNotesMappingParser
{
    private static readonly Regex YnabBotBlockRegex =
        new( @"\[ynabbot\](?<content>.*?)\[/ynabbot\]", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled );

    public static IReadOnlyCollection< string > ParseBankAccounts( string notes )
    {
        if ( string.IsNullOrWhiteSpace( notes ) )
            return Array.Empty< string >();

        var deserializer = new DeserializerBuilder()
                           .IgnoreUnmatchedProperties()
                           .WithNamingConvention( UnderscoredNamingConvention.Instance )
                           .Build();
        var bankAccounts = new HashSet< string >( StringComparer.Ordinal );
        foreach ( Match block in YnabBotBlockRegex.Matches( notes ) )
        {
            if ( !block.Success )
                continue;

            var content = block.Groups[ "content" ].Value;
            if ( string.IsNullOrWhiteSpace( content ) )
                continue;

            try
            {
                var mapping = deserializer.Deserialize< AccountNotesMapping >( content );
                if ( mapping == null )
                    continue;

                AddBankAccount( bankAccounts, mapping.BankAccount );
                AddBankAccounts( bankAccounts, mapping.BankAccounts );
            }
            catch
            {
                // Keep processing other blocks when one block has malformed YAML.
            }
        }

        return bankAccounts.Count == 0 ? Array.Empty< string >() : bankAccounts.ToList();
    }

    private static void AddBankAccount( ISet< string > bankAccounts, string bankAccount )
    {
        if ( string.IsNullOrWhiteSpace( bankAccount ) )
            return;

        bankAccounts.Add( bankAccount.Trim() );
    }

    private static void AddBankAccount( ISet< string > bankAccounts, object bankAccount )
    {
        if ( bankAccount == null )
            return;

        AddBankAccount( bankAccounts, bankAccount.ToString() );
    }

    private static void AddBankAccounts( ISet< string > bankAccounts, object bankAccountsToAdd )
    {
        if ( bankAccountsToAdd == null )
            return;

        if ( bankAccountsToAdd is IEnumerable< object > enumerable )
        {
            foreach ( var item in enumerable )
                AddBankAccount( bankAccounts, item );

            return;
        }

        AddBankAccount( bankAccounts, bankAccountsToAdd );
    }

    private sealed class AccountNotesMapping
    {
        public object BankAccount { get; set; }
        public object BankAccounts { get; set; }
    }
}
