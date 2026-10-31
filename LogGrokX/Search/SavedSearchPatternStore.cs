using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LogGrokX.Search
{
    public class SavedSearchPatternStore
    {
        private readonly string _storeFileName;

        public ObservableCollection<SavedSearchPattern> Items { get; }

        public SavedSearchPatternStore() : this(HomeDirectoryPathProvider.GetDataFileFullPath("SavedSearches.json"))
        {
        }

        private SavedSearchPatternStore(string storeFileName)
        {
            _storeFileName = storeFileName;
            Items = Load();
        }

        public static SavedSearchPatternStore LoadFromFile(string storeFileName) => new(storeFileName);

        public void Save()
        {
            try
            {
                using var createStream = File.Create(_storeFileName);
                var options = new JsonSerializerOptions { WriteIndented = true };
                JsonSerializer.Serialize(createStream, Items, options);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public void AddOrUpdate(string name, in SearchPattern searchPattern)
        {
            var existing = Items.FirstOrDefault(
                item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                Items.Remove(existing);

            Items.Insert(0, SavedSearchPattern.FromSearchPattern(name, searchPattern));
            Save();
        }

        public void ReplaceAll(IEnumerable<SavedSearchPattern> searches)
        {
            var items = searches.ToArray();
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            Save();
        }

        public void Update(SavedSearchPattern savedSearchPattern, string name, in SearchPattern searchPattern)
        {
            savedSearchPattern.Name = name;
            savedSearchPattern.Pattern = searchPattern.Pattern;
            savedSearchPattern.IsCaseSensitive = searchPattern.IsCaseSensitive;
            savedSearchPattern.UseRegex = searchPattern.UseRegex;
            Save();
        }

        public void Restore(SavedSearchPattern savedSearchPattern, int index)
        {
            if (Items.Contains(savedSearchPattern)) return;
            Items.Insert(Math.Clamp(index, 0, Items.Count), savedSearchPattern);
            Save();
        }

        public void Remove(SavedSearchPattern savedSearchPattern)
        {
            if (!Items.Remove(savedSearchPattern)) return;
            Save();
        }

        private ObservableCollection<SavedSearchPattern> Load()
        {
            var result = new ObservableCollection<SavedSearchPattern>();
            try
            {
                if (!File.Exists(_storeFileName)) return result;
                using var stream = File.OpenRead(_storeFileName);
                var items = JsonSerializer.Deserialize<ObservableCollection<SavedSearchPattern>>(stream);
                if (items == null) return result;

                foreach (var item in items)
                    result.Add(item);
            }
            catch (Exception)
            {
                // ignored
            }

            return result;
        }
    }
}
