using System;

namespace LogGrokX.Data.Monikers
{
    public readonly ref struct ParsedLineComponents
    {
        private readonly Span<int> _placeholder;
        
        public ParsedLineComponents(Span<int> placeholder)
        {
            _placeholder = placeholder;
        }

        public ReadOnlySpan<char> GetComponent(ReadOnlySpan<char> input, int index)
        {
            var componentStart = ComponentStart(index);
            var componentLength = ComponentLength(index);
            return input.Slice(componentStart, componentLength);
        }
        
        public ref int ComponentStart(int index) => ref _placeholder[index * 2];
        
        public ref int ComponentLength(int index) => ref _placeholder[index * 2 + 1];

        public int GetAllCompnentsLength(int componentCount)
        {
            var length = 0;
            for (var i = 0; i < componentCount; i++)
            {
                var end = ComponentStart(i) + ComponentLength(i);
                if (end > length)
                    length = end;
            }

            return length;
        }
    }
}