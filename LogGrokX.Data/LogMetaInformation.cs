using System;

namespace LogGrokX.Data
{
    public class LogMetaInformation
    {
        private readonly LogFormat _logFormat;

        public static LogMetaInformation CreateTextFileMetaInformation()
        {
            var plainTextLogFormat = new LogFormat()
            {
                Regex = @"^(?'Text'.*)",
                IndexedFields = Array.Empty<string>(),
                Transformations = Array.Empty<string>(),
                XorMask = 0
            };
            
            return new LogMetaInformation(plainTextLogFormat);
        }

        public string[] Transformations => _logFormat.Transformations;

        public string[] FieldNames => _logFormat.FieldNames;

        public int[] IndexedFieldNumbers => _logFormat.IndexedFieldNumbers;

        public string LineRegex => _logFormat.Regex;

        public int ComponentCount => FieldNames.Length;

        public byte XorMask => _logFormat.XorMask;

        public string TimeFormat => _logFormat.TimeFormat;

        public int TimeFieldIndex { get; }

        public bool HasTime => TimeFieldIndex >= 0;

        public int TimeGroupNumber => TimeFieldIndex + 1;

        public LogMetaInformation(LogFormat logFormat)
        {
            _logFormat = logFormat;
            TimeFieldIndex = ResolveTimeFieldIndex();
        }

        private int ResolveTimeFieldIndex()
        {
            if (!string.IsNullOrEmpty(_logFormat.TimeField))
                return GetFieldIndexByName(_logFormat.TimeField, StringComparison.OrdinalIgnoreCase);

            return Array.FindIndex(FieldNames,
                name => string.Equals(name, "Time", StringComparison.OrdinalIgnoreCase));
        }

        private int GetFieldIndexByName(string fieldName) => Array.IndexOf(FieldNames, fieldName);

        private int GetFieldIndexByName(string fieldName, StringComparison comparison) =>
            Array.FindIndex(FieldNames, name => string.Equals(name, fieldName, comparison));

        public int GetIndexedFieldIndexByName(string fieldName) =>
            GetIndexedFieldIndexByFieldIndex(GetFieldIndexByName(fieldName));
        
        public int GetIndexedFieldIndexByFieldIndex(int index) =>
            Array.IndexOf(IndexedFieldNumbers, index);

        public string GetFieldNameByIndexedFieldIndex(int indexedFieldIndex)
        {
            if (indexedFieldIndex < 0 || indexedFieldIndex >= IndexedFieldNumbers.Length)
                return string.Empty;
            return FieldNames[IndexedFieldNumbers[indexedFieldIndex]];
        }

        public bool IsFieldIndexed(string fieldName)
        {
            var index = Array.IndexOf(FieldNames, fieldName);
            if (index < 0) return false;
            return Array.IndexOf(IndexedFieldNumbers, index) >= 0;
        }
    }
}