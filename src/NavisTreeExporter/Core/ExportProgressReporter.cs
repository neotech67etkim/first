using System;

namespace NavisTreeExporter.Core
{
    /// <summary>
    /// Reports tree-walk progress back to the caller. Deliberately has no
    /// WinForms dependency (or thread) — the UI layer decides how to render
    /// updates and pump messages (see ExportProgressForm).
    /// </summary>
    public sealed class ExportProgressReporter
    {
        private readonly Action<int, int> _onProgress;
        private readonly Func<bool> _isCancelled;
        private const int ReportEveryNItems = 25;

        public ExportProgressReporter(int total, Action<int, int> onProgress, Func<bool> isCancelled)
        {
            Total = total;
            _onProgress = onProgress;
            _isCancelled = isCancelled;
        }

        public int Total { get; }
        public int Processed { get; private set; }

        public void ReportItem()
        {
            Processed++;

            if (Processed % ReportEveryNItems == 0 || Processed == Total)
            {
                _onProgress?.Invoke(Processed, Total);
            }

            if (_isCancelled != null && _isCancelled())
            {
                throw new OperationCanceledException("내보내기가 취소되었습니다.");
            }
        }
    }
}
