using System.Windows.Forms;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Small, non-modal prompt shown per saved viewpoint so the user can
    /// confirm the 3D view has finished loading before a screenshot is
    /// taken. Positioned in a screen corner (not centered) so it stays out
    /// of the way of the 3D view while visible.
    /// </summary>
    internal sealed class CapturePromptForm : Form
    {
        public enum PromptResult
        {
            Capture,
            Skip,
            Cancel,
        }

        private readonly Label _statusLabel;

        public PromptResult? Result { get; private set; }

        public CapturePromptForm()
        {
            Text = "Export Viewpoint Images";
            ClientSize = new System.Drawing.Size(360, 110);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = false;

            var workingArea = Screen.PrimaryScreen.WorkingArea;
            Location = new System.Drawing.Point(
                workingArea.Right - ClientSize.Width - 20,
                workingArea.Bottom - ClientSize.Height - 20);

            _statusLabel = new Label
            {
                Left = 12,
                Top = 12,
                Width = 336,
                Height = 40,
                AutoEllipsis = true,
            };

            var captureButton = new Button
            {
                Left = 12,
                Top = 60,
                Width = 105,
                Height = 32,
                Text = "캡처",
            };
            captureButton.Click += (sender, e) => Choose(PromptResult.Capture);

            var skipButton = new Button
            {
                Left = 128,
                Top = 60,
                Width = 105,
                Height = 32,
                Text = "건너뛰기",
            };
            skipButton.Click += (sender, e) => Choose(PromptResult.Skip);

            var cancelButton = new Button
            {
                Left = 244,
                Top = 60,
                Width = 104,
                Height = 32,
                Text = "전체 취소",
            };
            cancelButton.Click += (sender, e) => Choose(PromptResult.Cancel);

            Controls.Add(_statusLabel);
            Controls.Add(captureButton);
            Controls.Add(skipButton);
            Controls.Add(cancelButton);

            AcceptButton = captureButton;
        }

        public void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private void Choose(PromptResult result)
        {
            Result = result;
        }
    }
}
