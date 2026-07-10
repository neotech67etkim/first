using System;
using System.Windows.Forms;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Lightweight progress dialog for the tree export. Everything runs on
    /// Navisworks's own UI thread (no background thread, since Navisworks API
    /// thread-safety for tree reads isn't guaranteed) — the caller keeps this
    /// form responsive by calling Application.DoEvents() between updates.
    /// </summary>
    internal sealed class ExportProgressForm : Form
    {
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly Button _cancelButton;

        public bool CancelRequested { get; private set; }

        public ExportProgressForm()
        {
            Text = "Export Selection Tree";
            ClientSize = new System.Drawing.Size(400, 110);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            TopMost = true;

            _statusLabel = new Label
            {
                Left = 12,
                Top = 12,
                Width = 376,
                Text = "준비 중...",
                AutoEllipsis = true,
            };

            _progressBar = new ProgressBar
            {
                Left = 12,
                Top = 36,
                Width = 376,
                Height = 22,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
            };

            _cancelButton = new Button
            {
                Left = 313,
                Top = 68,
                Width = 75,
                Text = "취소",
            };
            _cancelButton.Click += (sender, e) => CancelRequested = true;

            Controls.Add(_statusLabel);
            Controls.Add(_progressBar);
            Controls.Add(_cancelButton);
        }

        public void SetTotal(int total)
        {
            if (total > 0)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Minimum = 0;
                _progressBar.Maximum = total;
                _progressBar.Value = 0;
            }
        }

        public void ReportProgress(int processed, int total)
        {
            _statusLabel.Text = total > 0
                ? string.Format("선택 트리 읽는 중... ({0:N0} / {1:N0})", processed, total)
                : string.Format("선택 트리 읽는 중... ({0:N0})", processed);

            if (total > 0)
            {
                _progressBar.Value = Math.Min(processed, total);
            }
        }

        public void SetIndeterminate(string message)
        {
            _statusLabel.Text = message;
            _progressBar.Style = ProgressBarStyle.Marquee;
        }
    }
}
