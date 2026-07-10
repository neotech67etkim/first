using System;

namespace NavisTreeExporter.Core
{
    /// <summary>
    /// Reports tree-walk progress back to the caller. Deliberately has no
    /// WinForms dependency (or thread) — the UI layer decides how to render
    /// updates and pump messages (see ExportProgressForm).
    ///
    /// There's no cheap way to know the total item count up front without a
    /// full extra pass over the tree (which doubles the walk cost on large
    /// models), so this only reports a running count.
    /// </summary>
    public sealed class ExportProgressReporter
    {
        private readonly Action<int> _onProgress;
        private readonly Func<bool> _isCancelled;
        private const int ReportEveryNItems = 50;

        public ExportProgressReporter(Action<int> onProgress, Func<bool> isCancelled)
        {
            _onProgress = onProgress;
            _isCancelled = isCancelled;
        }

        public int Processed { get; private set; }

        public void ReportItem()
        {
            Processed++;

            if (Processed % ReportEveryNItems == 0)
            {
                _onProgress?.Invoke(Processed);
            }

            if (_isCancelled != null && _isCancelled())
            {
                throw new OperationCanceledException("내보내기가 취소되었습니다.");
            }
        }
    }
}
