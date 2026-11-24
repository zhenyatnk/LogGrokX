using System;

namespace LogGrokX.Controls
{
    public class NavigateToLineRequest
    {
        public event Action<int, bool>? Navigate;

        private Action<int, int>? _restorePosition;
        private (int current, int scroll)? _pendingRestorePosition;

        public event Action<int, int>? RestorePosition
        {
            add
            {
                _restorePosition += value;
                if (_pendingRestorePosition is not { } pending)
                    return;
                _pendingRestorePosition = null;
                value?.Invoke(pending.current, pending.scroll);
            }
            remove => _restorePosition -= value;
        }

        public void Raise(int lineNumber, bool center = false)
        {
            Navigate?.Invoke(lineNumber, center);
        }

        public void RaiseRestorePosition(int currentLineNumber, int scrollLineNumber)
        {
            if (_restorePosition == null)
                _pendingRestorePosition = (currentLineNumber, scrollLineNumber);
            else
                _restorePosition.Invoke(currentLineNumber, scrollLineNumber);
        }
    }
}
