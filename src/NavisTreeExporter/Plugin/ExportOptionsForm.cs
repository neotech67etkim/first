using System.Windows.Forms;
using NavisTreeExporter.Core;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Lets the user pick how much detail to export, plus an optional
    /// "geometry only" filter, before the folder picker runs. Clicking a
    /// detail-level button closes the dialog immediately, using whatever the
    /// checkbox is set to at that point.
    /// </summary>
    internal sealed class ExportOptionsForm : Form
    {
        public ExportDetailLevel? SelectedLevel { get; private set; }
        public bool GeometryOnly { get; private set; }

        private readonly CheckBox _geometryOnlyCheckBox;

        public ExportOptionsForm()
        {
            Text = "Export Selection Tree";
            ClientSize = new System.Drawing.Size(360, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;

            var label = new Label
            {
                Left = 12,
                Top = 12,
                Width = 336,
                Text = "내보낼 정보 범위를 선택하세요.",
            };

            var namesOnlyButton = new Button
            {
                Left = 12,
                Top = 44,
                Width = 336,
                Height = 32,
                Text = "이름 + 계층 구조만 (가장 가볍고 빠름)",
            };
            namesOnlyButton.Click += (sender, e) => Choose(ExportDetailLevel.NamesOnly);

            var basicInfoButton = new Button
            {
                Left = 12,
                Top = 84,
                Width = 336,
                Height = 32,
                Text = "이름 + 계층 + 기본 정보 (클래스, GUID 등)",
            };
            basicInfoButton.Click += (sender, e) => Choose(ExportDetailLevel.HierarchyAndBasicInfo);

            var fullButton = new Button
            {
                Left = 12,
                Top = 124,
                Width = 336,
                Height = 32,
                Text = "이름 + 계층 + 전체 속성 (느림, 대용량)",
            };
            fullButton.Click += (sender, e) => Choose(ExportDetailLevel.HierarchyAndProperties);

            _geometryOnlyCheckBox = new CheckBox
            {
                Left = 12,
                Top = 168,
                Width = 336,
                Height = 24,
                Text = "형상이 있는 항목만 (컨테이너/그룹 노드 제외)",
            };

            var cancelButton = new Button
            {
                Left = 12,
                Top = 204,
                Width = 336,
                Height = 28,
                Text = "취소",
            };
            cancelButton.Click += (sender, e) =>
            {
                SelectedLevel = null;
                Close();
            };

            Controls.Add(label);
            Controls.Add(namesOnlyButton);
            Controls.Add(basicInfoButton);
            Controls.Add(fullButton);
            Controls.Add(_geometryOnlyCheckBox);
            Controls.Add(cancelButton);
        }

        private void Choose(ExportDetailLevel level)
        {
            SelectedLevel = level;
            GeometryOnly = _geometryOnlyCheckBox.Checked;
            Close();
        }
    }
}
