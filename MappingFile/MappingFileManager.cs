using System;
using Rhino;
using Rhino.DocObjects;
using RhinoToSAP.Sync;
using RhinoToSAP.Tools;
using System.Collections.Generic;

namespace RhinoToSAP.MappingFile
{
    // 映射文件操作管理：新建、打开、保存、另存为
    // 增量对齐统一调用SyncRtoS.RhinoToSap()，不再用MappingValidator的待处理列表
    public static class MappingFileManager
    {
        // ========== 统一返回结果 ==========
        public static string report = string.Empty;  // 状态报告
        public static string filePath = string.Empty; // 当前文件路径
        public static bool isReady = false;           // 是否就绪

        // 防重入标志位
        private static bool _isProcessing = false;

        // ========== 私有方法 ==========
        // 回滚：清空刚加载的内容，回到未加载状态
        private static void Rollback()
        {
            SyncPersistenceIO.UnLoadMapping();
            SyncAuto.IsMappingLoaded = false;
            SyncAuto.UpdateTimerState();
        }

        // 标记就绪：设置加载状态，启动Timer
        private static void MarkReady()
        {
            SyncAuto.IsMappingLoaded = true;
            SyncAuto.UpdateTimerState();
        }

        // ========== 公共方法 ==========

        // 新建映射文件
        public static void NewWithDialog()
        {
            if (_isProcessing) return;
            _isProcessing = true;
            try
            {
                var saveDlg = new Eto.Forms.SaveFileDialog();
                saveDlg.Filters.Add(new Eto.Forms.FileFilter("映射文件", ".json"));
                saveDlg.Title = "新建映射文件";
                isReady = false;
                filePath = SyncPersistenceIO.CurrentMappingFilePath;

                if (saveDlg.ShowDialog(Eto.Forms.Application.Instance.MainForm) != Eto.Forms.DialogResult.Ok)
                {
                    report = "❌ 新建映射文件取消";
                    return;
                }
                string newPath = saveDlg.FileName;

                if (!SyncPersistenceIO.CreateEmptyMapping(newPath))
                {
                    SyncAuto.IsMappingLoaded = false;
                    SyncAuto.UpdateTimerState();
                    report = "❌ 新建映射文件失败";
                    return;
                }

                // 新建后自动加载
                SyncPersistenceIO.LoadMapping(newPath, out string errorMsg);
                SyncAuto.IsMappingLoaded = true;
                SyncAuto.UpdateTimerState();
                filePath = newPath;
                isReady = true;

                // 统计当前图层下的有效对象数量
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                List<RhinoObject> validObjects = LayerHelper.GetValidObjectsInLayer(doc, SAPConnector.RootLayerName);
                int objCount = validObjects.Count;

                if (objCount == 0)
                {
                    report = $"✅ 新建映射文件成功: {newPath}\n当前图层下无对象，后续新增将自动同步";
                    return;
                }

                // 弹窗询问是否执行全量同步
                var result = Rhino.UI.Dialogs.ShowMessage(
                    $"检测到当前图层下有 {objCount} 个有效对象，是否执行全量同步？",
                    "全量同步确认",
                    Rhino.UI.ShowMessageButton.YesNo,
                    Rhino.UI.ShowMessageIcon.Question);

                if (result != Rhino.UI.ShowMessageResult.Yes)
                {
                    report = $"✅ 新建映射文件成功: {newPath}\n已跳过全量同步，后续新增对象将自动同步";
                    return;
                }

                // 执行全量同步（调用SyncRtoS，替代原来的SyncEngine.FullSync()）
                SyncRtoS.RhinoToSap(doc, SAPConnector.RootLayerName, out string syncMsg);
                report = $"✅ 新建映射文件成功: {newPath}\n已执行全量同步（{objCount}个对象）：{syncMsg}";
            }
            finally
            {
                _isProcessing = false;
            }
        }

        // 打开已有文件
        public static void LoadWithDialog()
        {
            if (_isProcessing) return;
            _isProcessing = true;
            try
            {
                var openDlg = new Eto.Forms.OpenFileDialog();
                openDlg.Filters.Add(new Eto.Forms.FileFilter("映射文件", ".json"));
                openDlg.Filters.Add(new Eto.Forms.FileFilter("所有文件", ".*"));
                openDlg.Title = "打开映射文件";

                if (openDlg.ShowDialog(Eto.Forms.Application.Instance.MainForm) != Eto.Forms.DialogResult.Ok)
                {
                    report = "❌ 映射文件加载取消";
                    return;
                }
                string selectedPath = openDlg.FileName;

                // 1. 加载文件到内存
                if (!SyncPersistenceIO.LoadMapping(selectedPath, out string errorMsg1))
                {
                    Rollback();
                    report = $"❌ 映射文件加载失败: {errorMsg1}";
                    isReady = false;
                    return;
                }

                // 2. 校验
                if (!MappingValidator.Validate(out string errorMsg2))
                {
                    Rollback();
                    report = $"❌ 映射文件校验失败: {errorMsg2}";
                    isReady = false;
                    return;
                }

                // 3. 收集变化
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc != null && !string.IsNullOrEmpty(SAPConnector.RootLayerName))
                {
                    SyncRtoS.CollectChanges(doc, SAPConnector.RootLayerName);
                }

                // 4. 判断有没有变化，没有变化就直接加载成功，不弹窗
                if (!SyncRtoS.HasChanges(out int changes))
                {
                    MarkReady();
                    report = $"✅ 映射文件加载成功，无变化";
                    filePath = selectedPath;
                    isReady = true;
                    return;
                }

                // 5. 有变化，弹窗询问是否进行增量同步
                var result = Rhino.UI.Dialogs.ShowMessage(
                    $"检测到Rhino有{changes}个变化，是否进行增量同步？",
                    "增量同步",
                    Rhino.UI.ShowMessageButton.YesNo,
                    Rhino.UI.ShowMessageIcon.Question);

                if (result == Rhino.UI.ShowMessageResult.Yes)
                {
                    // 选Yes：执行变化
                    SyncRtoS.ExecuteChanges(out string syncMsg);
                    report = $"✅ 映射文件加载成功，已执行增量同步：{syncMsg}";
                }
                else
                {
                    // 选No：清空待处理列表，跳过
                    SyncRtoS.ClearAllChanges();
                    report = "✅ 映射文件加载成功，已跳过增量同步";
                }

                // 6. 标记就绪
                MarkReady();
                filePath = selectedPath;
                isReady = true;
            }
            finally
            {
                _isProcessing = false;
            }
        }


        // 保存文件
        public static void Save()
        {
            if (_isProcessing) return;
            _isProcessing = true;
            try
            {
                if (!SyncAuto.IsMappingLoaded)
                {
                    report = "❌ 没有加载的映射文件，无法保存";
                    return;
                }
                if (!SyncPersistenceIO.SaveMapping())
                {
                    report = "❌ 映射文件保存失败";
                    return;
                }
                report = $"✅ 映射文件保存成功: {SyncPersistenceIO.CurrentMappingFilePath}";
            }
            finally
            {
                _isProcessing = false;
            }
        }

        // 另存为文件
        public static void SaveAsWithDialog()
        {
            if (_isProcessing) return;
            _isProcessing = true;
            try
            {
                if (!SyncAuto.IsMappingLoaded)
                {
                    report = "❌ 没有加载的映射文件，无法另存为";
                    return;
                }
                var saveDlg = new Eto.Forms.SaveFileDialog();
                saveDlg.Filters.Add(new Eto.Forms.FileFilter("映射文件", ".json"));
                saveDlg.Title = "另存为映射文件";

                if (saveDlg.ShowDialog(Eto.Forms.Application.Instance.MainForm) != Eto.Forms.DialogResult.Ok) return;
                string newPath = saveDlg.FileName;

                if (!SyncPersistenceIO.SaveMappingAs(newPath))
                {
                    report = "❌ 映射文件另存为失败";
                    return;
                }
                report = $"✅ 映射文件另存为成功: {newPath}";
                filePath = newPath;
            }
            finally
            {
                _isProcessing = false;
            }
        }
    }
}
