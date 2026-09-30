using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LogGrokX.Controls
{
    public class Selection : IEnumerable<int>
    {
        private readonly HashSet<int> _indices = new();
        private readonly List<WeakReference<Action>> _weakHandlers = new();

        public (int min, int max)? Bounds => _indices.Count == 0 ? null : (_indices.Min(), _indices.Max());

        public void SubscribeWeak(Action handler) => _weakHandlers.Add(new WeakReference<Action>(handler));

        private void RaiseChanged()
        {
            Changed?.Invoke();

            for (var i = _weakHandlers.Count - 1; i >= 0; i--)
            {
                if (_weakHandlers[i].TryGetTarget(out var handler))
                    handler();
                else
                    _weakHandlers.RemoveAt(i);
            }
        }

        public void Add(int index)
        {
            _indices.Add(index);
            RaiseChanged();
        }

        public void AddRangeToValue(int selectedValue)
        {
            if (Bounds is not {min: var min, max: var max})
            {
                Add(selectedValue);
            }
            else
            {
                var valueFrom = selectedValue > max ? max : min;
                for (var index = Math.Min(valueFrom, selectedValue);
                    index <= Math.Max(valueFrom, selectedValue);
                    index++)
                {
                    _indices.Add(index);
                }

                RaiseChanged();
            }
        }

        public void Clear()
        {
            _indices.Clear();
            RaiseChanged();
        }

        public void Remove(in int index)
        {
            _indices.Remove(index);
            RaiseChanged();
        }

        public void Set(int index)
        {
            _indices.Clear();
            _indices.Add(index);
            RaiseChanged();
        }

        public bool Contains(int index) => _indices.Contains(index);

        public event Action? Changed; 

        public IEnumerator<int> GetEnumerator()
        {
            return _indices.GetEnumerator();
        }
        
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}