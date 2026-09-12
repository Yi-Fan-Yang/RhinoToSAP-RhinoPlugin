using CSiAPIv1;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Data;
using RhinoToSAP.Display;
using RhinoToSAP.MappingFile;
using RhinoToSAP.Tools;
using System;
using System.Collections.Generic;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace RhinoToSAP.Sync
{
    // 自动同步类：订阅Rhino事件，计时器防抖批处理，调用SyncRtoS执行
    // 手动同步直接调用SyncRtoS.RhinoToSap，不走这个类
    public static class SyncAuto
    {
        // ========== 计时器和计数器 ==========
        private static WinFormsTimer _Timer;           // 计时器
        private static int _syncCounter = 0;           // 同步计数器：数到目标值就执行一次同步
        private static int _connCheckCounter = 0;      // 连接检测计数器：数到5就检查一次SAP连接

        // ========== 临时待处理字典 ==========
        // Replace事件触发时先存这里，计时器到时间后再分类到Move/Drag
        private static Dictionary<Guid, LineState> _pendingLineModify = new Dictionary<Guid, LineState>();
        private static Dictionary<Guid, MeshState> _pendingMeshModify = new Dictionary<Guid, MeshState>();
        

        // ========== 状态标志 ==========
        public static bool isInitialized = false;       // 初始化标志位
        public static bool IsMappingLoaded = false;     // 映射文件是否已加载

        // ========== 配置 ==========
        private static int _syncInterval = 3000;        // 同步间隔，默认3000ms
        public static int SyncInterval
        {
            get => _syncInterval;
            set
            {
                if (value < 1000) value = 1000;         // 最小1000ms
                if (value > 30000) value = 30000;       // 最大30000ms
                _syncInterval = value;
            }
        }

        private static int _syncCount = 0;               // 同步次数计数器
        public static int SyncCount => _syncCount;
        // 自动同步开关：默认开启，关闭时计时器不执行同步（只做连接检测）
        public static bool isAutoSyncEnabled { get; set; } = true;


        // 事件：每秒触发一次，供外部订阅（面板显示连接时间等）
        public static event Action SecondPassed;





        //===================公共方法====================
        // 初始化自动同步：注册Rhino事件、启动高亮管道，只需要调用一次
        // 连接成功、单位校验通过、绑定图层后调用
        public static void Initialize()
        {
            if (isInitialized) return;
            _syncCount = 0;  // 重新连接时同步次数清零

            try
            {
                // 注册Rhino对象事件：用户增删改对象时自动触发对应方法
                RhinoDoc.AddRhinoObject += OnObjectAdded;
                RhinoDoc.DeleteRhinoObject += OnObjectDeleted;
                RhinoDoc.ReplaceRhinoObject += OnObjectReplaced;
                RhinoDoc.ModifyObjectAttributes += OnObjectAttributesModified;

                // 创建并启用高亮显示管道
                SyncDisplay.Initialize();
                isInitialized = true;
                RhinoApp.WriteLine("[SyncAuto] 初始化完成，事件已注册");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[SyncAuto] 初始化失败：{ex.Message}");
            }
        }

        // 停止自动同步：注销事件、停止计时器、释放高亮管道
        public static void Shutdown()
        {
            if (!isInitialized) return;

            try
            {
                // 注销事件，和注册的时候一一对应，用-=
                RhinoDoc.AddRhinoObject -= OnObjectAdded;
                RhinoDoc.DeleteRhinoObject -= OnObjectDeleted;
                RhinoDoc.ReplaceRhinoObject -= OnObjectReplaced;
                RhinoDoc.ModifyObjectAttributes -= OnObjectAttributesModified;

                // 停止并释放计时器
                if (_Timer != null)
                {
                    _Timer.Stop();
                    _Timer.Dispose();
                    _Timer = null;
                }

                // 清空临时字典
                _pendingLineModify.Clear();
                _pendingMeshModify.Clear();

                // 停止并释放高亮显示管道
                SyncDisplay.Shutdown();

                isInitialized = false;
                IsMappingLoaded = false;
                RhinoApp.WriteLine("[SyncAuto] 已停止，事件已注销");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[SyncAuto] 停止失败：{ex.Message}");
            }
        }

        // 根据连接状态和映射加载状态，自动启动或停止Timer
        // 连接成功 + 映射加载完成 → 启动Timer
        // 任一不满足 → 停止Timer
        public static void UpdateTimerState()
        {
            bool shouldRun = SAPConnector.IsConnected && IsMappingLoaded;

            if (shouldRun && _Timer == null)
            {
                // 启动计时器
                _Timer = new WinFormsTimer();
                _Timer.Interval = 1000;  // 每秒触发一次Tick
                _Timer.Tick += OnTimerTick;
                _Timer.Start();
                RhinoApp.WriteLine("[SyncAuto] Timer已启动");
            }
            else if (!shouldRun && _Timer != null)
            {
                // 停止计时器
                _Timer.Stop();
                _Timer.Dispose();
                _Timer = null;
                _syncCounter = 0;
                _connCheckCounter = 0;
                RhinoApp.WriteLine("[SyncAuto] Timer已停止");
            }
        }

        // 计时器Tick事件：每秒触发一次
        // 两个计数器各自+1：
        //   _syncCounter到同步间隔 → 执行增量同步
        //   _connCheckCounter到5 → 检查SAP连接和单位
        public static void OnTimerTick(object sender, EventArgs e)
        {
            SecondPassed?.Invoke();
            // 两个计数器各自+1
            _syncCounter++;
            _connCheckCounter++;

            int syncTarget = SyncInterval / 1000;
            if (_syncCounter >= syncTarget)
            {
                // 只有自动同步开启时才执行
                if (isAutoSyncEnabled && !SyncRtoS.IsManualSyncRunning)
                {
                    _syncCount++;

                    // 第一步：准备10张列表
                    PreparePendingChanges();

                    // 第二步：有变化才执行
                    if (SyncRtoS.HasChanges(out int changes))
                    {
                        SyncRtoS.ApplyAllChanges();       // 执行10张列表
                        SyncPersistenceIO.SaveMapping();   // 保存
                    }

                    // 第三步：清空所有待处理
                    SyncRtoS.ClearAllChanges();
                    _pendingLineModify.Clear();
                    _pendingMeshModify.Clear();
                }
                _syncCounter = 0;
            }

            // 计数器2：数到5就检查SAP连接和单位
            if (_connCheckCounter >= 5)
            {
                // 检查SAP连接是否还活着
                if (!SAPConnector.CheckConnectionAlive())
                {
                    RhinoApp.WriteLine("[SyncAuto] SAP连接已断开，自动锁定图层");
                    SAPConnector.Disconnect();
                    _connCheckCounter = 0;
                    return;
                }

                // 检查单位是否一致
                if (!SAPConnector.CheckUnits(RhinoDoc.ActiveDoc, out string unitMsg))
                {
                    LayerHelper.LockLayer(RhinoDoc.ActiveDoc, SAPConnector.RootLayerName);
                    SAPConnector.UnitCheckMessage = unitMsg;
                    RhinoApp.WriteLine($"[SyncAuto] 单位不匹配，自动锁定图层");
                }

                _connCheckCounter = 0;  // 重置计数器

            }
        }

        // 准备10张待处理列表：把Replace事件的临时字典分类到Move/Drag
        // Add/Delete/Layer已经在事件处理时加入了，这里不需要处理
        public static void PreparePendingChanges()
        {
            // ========== 处理_pendingLineModify临时字典（Replace事件）==========
            foreach (var kvp in _pendingLineModify)
            {
                Guid rhinoId = kvp.Key;
                LineState currentState = kvp.Value;

                // 查历史状态，没有就跳过（Add事件已经处理了）
                if (!SyncStateManager.TryGetHistory<LineState>(rhinoId, out LineState oldState))
                {
                    continue;
                }

                // 用LineHelper.GetLineChangeType判断变化类型
                LineChangeType changeType = LineHelper.GetLineChangeType(currentState, oldState);

                if (changeType == LineChangeType.Move)
                {
                    SyncRtoS._LineMove[rhinoId] = currentState;  // 加入LineMove，自动去重
                }
                else if (changeType == LineChangeType.Drag)
                {
                    SyncRtoS._LineDrag[rhinoId] = currentState;  // 加入LineDrag，自动去重
                }
                // Equal的话跳过
            }

            // ========== Mesh的后面再加 ==========
        }

        // 对象添加事件：新增对象时触发
        // 过滤有效直线，判断在目标图层层级，直接加入SyncRtoS._LineAdd
        public static void OnObjectAdded(object sender, RhinoObjectEventArgs e)
        {
            try
            {
                RhinoObject obj = e.TheObject;
                if (obj == null) return;

                // 判断在不在目标图层及其子图层下
                if (!LayerHelper.IsObjectInLayerHierarchy(obj, SAPConnector.RootLayerName)) return;

                // 判断是直线还是网格
                if (LineHelper.IsValidLineObject(obj))
                {
                    // 第三步：是直线，加入LineAdd字典（索引器赋值自动去重）
                    LineState state = LineHelper.ToLineState(obj);
                    SyncRtoS._LineAdd[obj.Id] = state;
                }
                // 网格的后面再加
            }
            catch
            {
            }
        }


        // 对象删除事件：删除对象时触发
        // 对象已经被删了，拿不到几何，查历史状态表判断是Line还是Mesh，加入对应删除列表
        public static void OnObjectDeleted(object sender, RhinoObjectEventArgs e)
        {
            try
            {
                Guid rhinoId = e.ObjectId;

                // 查历史状态表，判断是Line还是Mesh
                if (SyncStateManager.TryGetHistory<LineState>(rhinoId, out _))
                {
                    // 是直线，加入LineDelete列表
                    SyncRtoS._LineDelete.Add(rhinoId);
                }
                // Mesh的后面再加
            }
            catch
            {
            }
        }

        // 对象修改事件：移动、改坐标等操作时触发
        // 过滤有效直线，判断在目标图层层级，加入临时字典_pendingLineModify（去重）
        // 计时器到时间后，PreparePendingChanges会从临时字典分类到Move/Drag
        public static void OnObjectReplaced(object sender, EventArgs e)
        {
            try
            {
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null) return;

                // Replace事件参数是RhinoReplaceObjectEventArgs，有ObjectId
                if (e is RhinoReplaceObjectEventArgs replaceArgs)
                {
                    RhinoObject obj = doc.Objects.FindId(replaceArgs.ObjectId);
                    if (obj == null) return;

                    // 第一步：判断在不在目标图层及其子图层下
                    if (!LayerHelper.IsObjectInLayerHierarchy(obj, SAPConnector.RootLayerName)) return;

                    // 第二步：判断是直线还是网格
                    if (LineHelper.IsValidLineObject(obj))
                    {
                        // 第三步：是直线，加入临时字典_pendingLineModify（索引器赋值自动去重）
                        LineState state = LineHelper.ToLineState(obj);
                        _pendingLineModify[obj.Id] = state;
                    }
                    // 网格的后面再加
                }
            }
            catch
            {
            }
        }
        // 对象属性修改事件：改图层等操作时触发
        // 过滤有效直线，判断在目标图层层级，直接加入SyncRtoS._LineLayer
        public static void OnObjectAttributesModified(object sender, RhinoModifyObjectAttributesEventArgs e)
        {
            try
            {
                RhinoObject obj = e.RhinoObject;
                if (obj == null) return;

                // 第一步：判断在不在目标图层及其子图层下
                if (!LayerHelper.IsObjectInLayerHierarchy(obj, SAPConnector.RootLayerName)) return;

                // 第二步：判断是直线还是网格
                if (LineHelper.IsValidLineObject(obj))
                {
                    // 第三步：是直线，加入LineLayer字典（索引器赋值自动去重）
                    LineState state = LineHelper.ToLineState(obj);
                    SyncRtoS._LineLayer[obj.Id] = state;
                }
                // 网格的后面再加
            }
            catch
            {
            }
        }

        // 内部统一校验：连接、映射文件、初始化都就绪才返回true
        public static bool CheckReady(out string errorMsg)
        {
            if (!SAPConnector.IsConnected)
            {
                errorMsg = "请先连接SAP";
                return false;
            }
            if (!IsMappingLoaded)
            {
                errorMsg = "请先加载或新建映射文件";
                return false;
            }
            if (!isInitialized)
            {
                errorMsg = "同步引擎未初始化";
                return false;
            }
            errorMsg = string.Empty;
            return true;
        }

        // 设置同步间隔（秒），带范围校验1~30秒
        public static string SetSyncInterval(string intervalText)
        {
            if (!int.TryParse(intervalText, out int interval))
            {
                return "同步间隔输入无效，请输入1~30之间的整数";
            }
            if (interval < 1 || interval > 30)
            {
                return "同步间隔需在1~30秒之间";
            }
            SyncInterval = interval * 1000;  // 转成毫秒
            return $"同步间隔：{interval}秒";
        }

        public static string SetAutoSyncEnabled(bool enabled)
        {
            isAutoSyncEnabled = enabled;
            return $"自动同步：{(enabled ? "开启" : "关闭")}";
        }
    }
}
