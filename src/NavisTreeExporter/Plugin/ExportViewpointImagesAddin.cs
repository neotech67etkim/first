using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisTreeExporter.Core;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Moves the camera to each saved viewpoint in turn and saves a
    /// screenshot of just the 3D viewport as a PNG. Requires the Navisworks
    /// window to stay visible/unminimized for the duration (it's a real
    /// screen capture, not an off-screen render).
    ///
    /// Captures the whole main window, then crops the result - no window
    /// hiding/showing involved (an earlier attempt at hiding docked panels
    /// ended up hiding an essential frame window and broke the whole
    /// layout). The Selection Tree / Saved Viewpoints panels are drawn as an
    /// overlay directly on top of the 3D viewport's own window region rather
    /// than shrinking it, so the crop rectangle is narrowed using those
    /// panels' own window bounds (read-only lookups) instead of trusting the
    /// viewport's rect alone. All of the Win32 capture/crop logic lives in
    /// ViewpointCaptureService, shared with the auto-mode watcher.
    ///
    /// NOTE: the saved-viewpoint-restoration call
    /// (Document.CurrentViewpoint.CopyFrom) is unverified against the real
    /// Navisworks SDK - see SavedViewpointCollector for the same caveat on
    /// the tree-walking side.
    ///
    /// For unattended daily runs, see ExportViewpointImagesAutoWatcher - an
    /// AddInPlugin like this one only runs when its ribbon button is
    /// clicked (confirmed via a real build: AddInPlugin has no Load/Unload
    /// override to hook document-open), so the auto-mode bootstrap lives in
    /// a separate EventWatcherPlugin instead, which is loaded automatically.
    /// </summary>
    [Plugin("NavisTreeExporter.ExportViewpointImages", "NTE",
        DisplayName = "Export Viewpoint Images",
        ToolTip = "Move the camera to each saved viewpoint and save a screenshot of each")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class ExportViewpointImagesAddin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (document == null)
            {
                MessageBox.Show("열려 있는 Navisworks 문서가 없습니다.", "Export Viewpoint Images",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }

            List<(string Path, SavedViewpoint Viewpoint)> viewpoints;
            try
            {
                viewpoints = SavedViewpointCollector.Collect(document);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "저장된 관측점을 읽는 중 오류가 발생했습니다:" + Environment.NewLine +
                    ex.GetType().Name + ": " + ex.Message,
                    "Export Viewpoint Images", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }

            if (viewpoints.Count == 0)
            {
                MessageBox.Show("저장된 관측점이 없습니다.", "Export Viewpoint Images",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            using (var folderDialog = new FolderBrowserDialog { Description = "이미지를 저장할 폴더를 선택하세요" })
            {
                if (folderDialog.ShowDialog() != DialogResult.OK) return 0;

                var savedCount = 0;
                var skippedCount = 0;
                var cancelled = false;
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Figure out where the 3D viewport is just once, up front -
                // this is entirely read-only (no hiding/showing), so it
                // can't break the window layout. Start from the largest
                // visible leaf window, then narrow it away from any docked
                // panel that overlaps it horizontally.
                var mainHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                var viewportRect = ViewpointCaptureService.ComputeViewportRect(mainHandle);

                try
                {
                    if (mainHandle == IntPtr.Zero)
                    {
                        MessageBox.Show("Navisworks 창을 찾지 못했습니다.", "Export Viewpoint Images",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 0;
                    }

                    for (var i = 0; i < viewpoints.Count; i++)
                    {
                        var (path, viewpoint) = viewpoints[i];

                        try
                        {
                            document.CurrentViewpoint.CopyFrom(viewpoint.Viewpoint);
                        }
                        catch (Exception)
                        {
                            continue; // skip viewpoints that fail to apply, keep going
                        }

                        System.Windows.Forms.Application.DoEvents();

                        using (var prompt = new CapturePromptForm())
                        {
                            prompt.SetStatus($"({i + 1} / {viewpoints.Count}) {path}" + Environment.NewLine +
                                "로딩이 끝나면 캡처를 누르세요.");
                            prompt.Show();

                            while (prompt.Result == null)
                            {
                                System.Windows.Forms.Application.DoEvents();
                                System.Threading.Thread.Sleep(30);
                            }

                            var result = prompt.Result.Value;

                            if (result == CapturePromptForm.PromptResult.Cancel)
                            {
                                cancelled = true;
                                break;
                            }

                            if (result == CapturePromptForm.PromptResult.Skip)
                            {
                                skippedCount++;
                                continue;
                            }

                            // Hide the prompt itself before capturing - otherwise it's
                            // sitting on top of the Navisworks view and ends up in the
                            // screenshot instead of the model. (This is our own small
                            // dialog, not part of the Navisworks window, so it's safe.)
                            prompt.Hide();
                            for (var pump = 0; pump < 3; pump++)
                            {
                                System.Windows.Forms.Application.DoEvents();
                                System.Threading.Thread.Sleep(30);
                            }

                            var fileName = FileNameSanitizer.Sanitize(path, "Viewpoint" + i);
                            var uniqueFileName = fileName;
                            var suffix = 1;
                            while (!usedNames.Add(uniqueFileName))
                            {
                                uniqueFileName = fileName + "_" + suffix;
                                suffix++;
                            }

                            var imagePath = Path.Combine(folderDialog.SelectedPath, uniqueFileName + ".png");
                            if (ViewpointCaptureService.CaptureAndCropToViewport(mainHandle, viewportRect, imagePath))
                            {
                                savedCount++;
                            }
                        }
                    }

                    var summary = cancelled
                        ? $"취소됨: {savedCount} / {viewpoints.Count}개 이미지를 저장한 상태에서 중단했습니다."
                        : $"완료: {savedCount} / {viewpoints.Count}개 이미지를 저장했습니다.";
                    if (skippedCount > 0)
                    {
                        summary += Environment.NewLine + $"건너뛴 관측점: {skippedCount}개";
                    }

                    MessageBox.Show(summary + Environment.NewLine + folderDialog.SelectedPath,
                        "Export Viewpoint Images", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "내보내기 중 오류가 발생했습니다:" + Environment.NewLine +
                        ex.GetType().Name + ": " + ex.Message + Environment.NewLine + Environment.NewLine +
                        ex.StackTrace,
                        "Export Viewpoint Images", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return 0;
        }
    }
}
