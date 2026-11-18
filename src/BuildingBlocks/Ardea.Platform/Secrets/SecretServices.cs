using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ardea.Platform.Secrets;

public static class SecretServices
{
    /// <summary>Data Protection with keys in the database, encrypted at rest with the master key.</summary>
    public static IServiceCollection AddArdeaSecrets(this IServiceCollection services, MasterKey masterKey)
    {
        services.AddSingleton(masterKey);
        services.AddDataProtection().SetApplicationName("Ardea");
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp => new ConfigureOptions<KeyManagementOptions>(options =>
        {
            options.XmlRepository = new DatabaseXmlRepository(sp.GetRequiredService<NpgsqlDataSource>());
            options.XmlEncryptor = new MasterKeyXmlEncryptor(masterKey);
        }));
        services.AddSingleton<ISecretProtector, SecretProtector>();
        return services;
    }
}
