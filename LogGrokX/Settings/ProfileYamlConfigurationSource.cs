using System;
using System.Globalization;
using System.IO;
using LogGrokX.Diagnostics;
using Microsoft.Extensions.Configuration;
using NetEscapades.Configuration.Yaml;
using YamlDotNet.RepresentationModel;

namespace LogGrokX.Settings;

internal sealed class ProfileYamlConfigurationSource : YamlConfigurationSource
{
    private static readonly Logger Logger = Logger.Get(typeof(ProfileYamlConfigurationSource).Namespace);

    public ProfileYamlConfigurationSource()
    {
        OnLoadException = context =>
        {
            context.Ignore = true;
            LastLoadFailed = true;
            Logger.Error("Failed to load configuration from file '{0}': {1}", Path ?? string.Empty, context.Exception);
        };
    }

    internal bool LastLoadFailed { get; private set; }

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
            try
            {
                base.Load(buffer);

                buffer.Position = 0;
                using var reader = new StreamReader(buffer);
                var yaml = new YamlStream();
                yaml.Load(reader);
                if (yaml.Documents.Count > 0)
                    PreserveEmptySections(yaml.Documents[0].RootNode, string.Empty);

                source.LastLoadFailed = false;
            }
            catch (Exception e)
            {
                source.LastLoadFailed = true;
                Logger.Error("Failed to load configuration from file '{0}': {1}", source.Path ?? string.Empty, e);
            }
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
