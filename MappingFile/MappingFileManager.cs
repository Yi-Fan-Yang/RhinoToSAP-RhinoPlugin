using System;
using Rhino;
using Rhino.DocObjects;
using RhinoToSAP.Sync;
using RhinoToSAP.Tools;
using System.Collections.Generic;

namespace RhinoToSAP.MappingFile
{
    public static class MappingFileManager
    {
        // ========== 统一返回结果 ==========
        // 所有文件操作方法都返回这个对象，电池只管读取显示
        public static string report=string.Empty;// 状态报告
        public static string filePath = string.Empty;// 当前文件路径
        public static bool isReady = false;// 是否就绪
         // 防重入标志位：防止操作被重复调用
        private static bool _isProcessing = false;

        // ========== 私有方法 ==========
        // 回滚：清空刚加载的内容，回到未加载状态
        private static void Rollback()
            {
                SyncPersistenceIO.UnLoadMapping();
                SyncEngine.IsMappingLoaded = false;
                SyncEngine.UpdateTimerState();
            }

        // 标记就绪：设置加载状态，启动Timer
        private static void MarkReady()
            {
                SyncEngine.IsMappingLoaded = true;
                SyncEngine.UpdateTimerState();
            }

        
        //========== 公共方法 ==========
        //新建映射文件
        public static void NewWithDialog()
        {
            if (_isProcessing) return;  // 正在处理中，直接返回，不重复执行
            _isProcessing = true;
            try
            {
                var saveDlg = new Eto.Forms. SaveFileDialog();
                saveDlg.Filters.Add(new Eto.Forms.FileFilter("映射文件", ".json" ));
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
                    SyncEngine.IsMappingLoaded = false;
                    SyncEngine.UpdateTimerState();
                    report = $"❌ 新建映射文件失败";
                    return;
                }

                // 新建后自动加载
                SyncPersistenceIO.LoadMapping(newPath, out string errorMsg);
                SyncEngine.IsMappingLoaded = true;
                SyncEngine.UpdateTimerState();
                report = $"✅ 新建映射文件成功: {newPath}";
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

                SyncEngine.FullSync();
                report = $"✅ 新建映射文件成功: {newPath}\n已执行全量同步（{objCount}个对象）";
            }
            finally
            {
                _isProcessing = false;
            }

         }
        //打开已有文件
        public static void LoadWithDialog()
        {
            if (_isProcessing) return;  // 正在处理中，直接返回，不重复执行
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
                //加载文件到内存，不成功报错返回
                if (!SyncPersistenceIO.LoadMapping(selectedPath, out string errorMsg1))
                {
                    Rollback();
                    report = $"❌ 映射文件加载失败: {errorMsg1}";
                    isReady = false;
                    return;
                }
                //加载成功后进行校验
                if (!MappingValidator.Validate(out string errorMsg2))
                {
                    Rollback();
                    report = $"❌ 映射文件校验失败: {errorMsg2}";
                    isReady = false;
                    return;
                }

                //校验成功后弹窗查询是否有变动，无变动则完成加载
                if (MappingValidator.PendingUpdate.Count == 0
                    && MappingValidator.PendingDelete.Count == 0
                    && MappingValidator.PendingCreate.Count == 0
                    && MappingValidator.PendingLayerChange.Count == 0)
                {
                    MarkReady();
                    report = $"✅ 映射文件加载成功: {selectedPath}";
                    filePath = selectedPath;
                    isReady = true;
                    return;
                }

                //如有变动，弹窗询问是否进行增量对齐
                var result = Rhino.UI.Dialogs.ShowMessage(
                    "检测到rhino有变化，是否立刻进行增量对齐", "增量对齐",
                    Rhino.UI.ShowMessageButton.YesNo,
                    Rhino.UI.ShowMessageIcon.Question);

                //选NO则取消操作，回滚
                if (result == Rhino.UI.ShowMessageResult.No)
                {
                    Rollback();
                    report = $"❌ 用户不进行对齐，打开操作取消";
                    isReady = false;
                    return;
                }

                //选Yes则进行增量对齐
                MappingValidator.ExecutePendingChanges();
                MarkReady();
                report = $"✅ 映射文件加载成功: {selectedPath}";
                filePath = selectedPath;
                isReady = true;
                return;
            }
            finally
            {
                _isProcessing = false;
            }

        }

        //保存文件
        public static void Save()
        {
            if (_isProcessing) return;  // 正在处理中，直接返回，不重复执行
            _isProcessing = true;
            try
            {
                if (!SyncEngine.IsMappingLoaded)
                {
                    report = "❌ 没有加载的映射文件，无法保存";
                    return;
                }
                if (!SyncPersistenceIO.SaveMapping())
                {
                    report = $"❌ 映射文件保存失败";
                    return; 
                }
                report = $"✅ 映射文件保存成功: {SyncPersistenceIO.CurrentMappingFilePath}";
            }
            finally
            {
                _isProcessing = false;
            }

        }
        //另存为文件
        public static void SaveAsWithDialog()
        {
            if (_isProcessing) return;  // 正在处理中，直接返回，不重复执行
            _isProcessing = true;
            try
            {
                if (!SyncEngine.IsMappingLoaded)
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
                    report = $"❌ 映射文件另存为失败";
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
