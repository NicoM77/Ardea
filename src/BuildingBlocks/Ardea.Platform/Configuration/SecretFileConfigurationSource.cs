using System.Collections;
using Microsoft.Extensions.Configuration;

namespace Ardea.Platform.Configuration;

internal sealed class SecretFileConfigurationSource(string prefix, Func<IDictionary> environment) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(prefix, environment);

    private sealed class Provider(string prefix, Func<IDictionary> environment) : ConfigurationProvider
    {
        private const string Suffix = "_FILE";

        public override void Load()
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var variables = environment();
            foreach (DictionaryEntry entry in variables)
            {
                var name = (string)entry.Key;
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)
                    || entry.Value is not string path
                    || string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var direct = name[..^Suffix.Length];
                if (variables.Contains(direct))
                {
                    continue;
                }

                if (!File.Exists(path))
                {
                    throw new InvalidOperationException($"{name} points to '{path}', which does not exist.");
                }

                var key = direct[prefix.Length..].Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
                data[key] = File.ReadAllText(path).Trim();
            }

            Data = data;
        }
    }
}
