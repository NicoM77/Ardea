using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace Ardea.Platform.Secrets;

/// <summary>
/// Keeps Data Protection keys in <c>platform.data_protection_key</c>, so every API and worker
/// instance shares them. Uses its own short connections: the key ring loads outside any unit of work.
/// </summary>
internal sealed class DatabaseXmlRepository(NpgsqlDataSource dataSource) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var command = dataSource.CreateCommand("SELECT xml FROM platform.data_protection_key WHERE xml IS NOT NULL ORDER BY id");
        using var reader = command.ExecuteReader();
        var elements = new List<XElement>();
        while (reader.Read())
        {
            elements.Add(XElement.Parse(reader.GetString(0)));
        }

        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var command = dataSource.CreateCommand("INSERT INTO platform.data_protection_key (friendly_name, xml) VALUES ($1, $2)");
        command.Parameters.Add(new NpgsqlParameter { Value = friendlyName });
        command.Parameters.Add(new NpgsqlParameter { Value = element.ToString(SaveOptions.DisableFormatting) });
        command.ExecuteNonQuery();
    }
}
