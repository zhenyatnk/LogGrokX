using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Configuration;
using NetEscapades.Configuration.Yaml;
using YamlDotNet.RepresentationModel;

namespace LogGrokX.Settings;

internal sealed class ProfileYamlConfigurationSource : YamlConfigurationSource
{
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        ResolveFileProvider();
        EnsureDefaults(builder);
        return new Provider(this);
    }

    private sealed class Provider(ProfileYamlConfigurationSource source) : YamlConfigurationProvider(source)
    {
        public override void Load(Stream stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            base.Load(buffer);

            buffer.Position = 0;
            using var reader = new StreamReader(buffer);
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count > 0)
                PreserveEmptySections(yaml.Documents[0].RootNode, string.Empty);
        }

        private void PreserveEmptySections(YamlNode node, string path)
        {
            if (path.StartsWith("Settings:Profiles:", StringComparison.OrdinalIgnoreCase) &&
                node is YamlSequenceNode { Children.Count: 0 } or YamlMappingNode { Children.Count: 0 })
                Data[path] = null;

            if (node is YamlMappingNode mapping)
            {
                foreach (var pair in mapping.Children)
                {
                    var key = ((YamlScalarNode)pair.Key).Value!;
                    PreserveEmptySections(pair.Value, path.Length == 0 ? key : ConfigurationPath.Combine(path, key));
                }
            }
            else if (node is YamlSequenceNode sequence)
            {
                for (var i = 0; i < sequence.Children.Count; i++)
                    PreserveEmptySections(sequence.Children[i], ConfigurationPath.Combine(path, i.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }
}
