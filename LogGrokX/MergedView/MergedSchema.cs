using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LogGrokX.Data;

namespace LogGrokX.MergedView
{
    internal sealed class MergedSchema
    {
        private const string TextField = "Text";
        private const string MessageField = "Message";
        private const string MessageFieldKey = "message";
        private const string LevelField = "Level";
        private const string SeverityField = "Severity";
        private const string SeverityFieldKey = "severity";

        private readonly Dictionary<string, int> _fieldIndices;

        private MergedSchema(IReadOnlyList<string> fields, LogMetaInformation metaInformation)
        {
            Fields = fields;
            MetaInformation = metaInformation;
            _fieldIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < fields.Count; i++)
                _fieldIndices[GetFieldKey(fields[i])] = i;
        }

        public IReadOnlyList<string> Fields { get; }

        public LogMetaInformation MetaInformation { get; }

        public bool IsEmpty => Fields.Count == 0;

        public int IndexOf(string fieldName) =>
            _fieldIndices.TryGetValue(GetFieldKey(fieldName), out var index) ? index : -1;

        public int ThreadFieldIndex => IndexOf("Thread");

        public static IReadOnlyList<string> BuildFields(IEnumerable<MergedDocumentItem> documents)
        {
            var fieldSets = new List<IReadOnlyList<string>>();
            foreach (var document in documents)
                fieldSets.Add(document.Document.MetaInformation.FieldNames);
            return BuildFields(fieldSets);
        }

        internal static IReadOnlyList<string> BuildFields(IReadOnlyList<IReadOnlyList<string>> fieldSets)
        {
            var fields = new List<string>();
            var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fieldSet in fieldSets)
            {
                foreach (var field in fieldSet)
                {
                    var key = GetFieldKey(field);
                    if (indexByKey.TryGetValue(key, out var existing))
                    {
                        if (IsBetterFieldName(field, fields[existing]))
                            fields[existing] = field;
                        continue;
                    }

                    indexByKey[key] = fields.Count;
                    fields.Add(field);
                }
            }

            return fields;
        }

        public static MergedSchema Build(IReadOnlyList<MergedDocumentItem> documents) =>
            Build(documents.Select(document => document.Document.MetaInformation).ToArray());

        internal static MergedSchema Build(IReadOnlyList<LogMetaInformation> sources)
        {
            var fields = BuildFields(sources.Select(source => (IReadOnlyList<string>)source.FieldNames).ToArray());
            var indexedKeys = sources.SelectMany(source => source.IndexedFieldNumbers
                    .Select(index => GetFieldKey(source.FieldNames[index])))
                .ToHashSet(StringComparer.Ordinal);
            var indexedFields = fields.Where(field => indexedKeys.Contains(GetFieldKey(field))).ToArray();
            return new MergedSchema(fields, CreateMetaInformation(fields, indexedFields));
        }

        internal static int FindSourceFieldIndex(IReadOnlyList<string> fieldNames, string mergedFieldName)
        {
            var key = GetFieldKey(mergedFieldName);
            for (var i = 0; i < fieldNames.Count; i++)
            {
                if (GetFieldKey(fieldNames[i]) == key)
                    return i;
            }

            return -1;
        }

        private static string GetFieldKey(string fieldName)
        {
            if (fieldName.Equals(TextField, StringComparison.OrdinalIgnoreCase))
                return MessageFieldKey;
            if (fieldName.Equals(LevelField, StringComparison.OrdinalIgnoreCase) ||
                fieldName.Equals(SeverityField, StringComparison.OrdinalIgnoreCase))
                return SeverityFieldKey;
            return fieldName.ToLowerInvariant();
        }

        private static bool IsBetterFieldName(string candidate, string current)
        {
            if (candidate.Equals(current, StringComparison.Ordinal))
                return false;

            if (candidate.Equals(MessageField, StringComparison.OrdinalIgnoreCase) &&
                current.Equals(TextField, StringComparison.OrdinalIgnoreCase))
                return true;

            if (candidate.Equals(SeverityField, StringComparison.OrdinalIgnoreCase) &&
                current.Equals(LevelField, StringComparison.OrdinalIgnoreCase))
                return true;

            return char.IsUpper(candidate[0]) && char.IsLower(current[0]);
        }

        public void ApplyTo(IReadOnlyList<MergedDocumentItem> documents)
        {
            foreach (var document in documents)
                document.UpdateFieldMap(Fields);
        }

        private static LogMetaInformation CreateMetaInformation(IReadOnlyList<string> fields, string[] indexedFields)
        {
            var builder = new StringBuilder("^");
            foreach (var field in fields)
                builder.Append("(?<").Append(field).Append(">)");

            var format = new LogFormat
            {
                Regex = builder.ToString(),
                IndexedFields = indexedFields
            };

            return new LogMetaInformation(format);
        }
    }
}
