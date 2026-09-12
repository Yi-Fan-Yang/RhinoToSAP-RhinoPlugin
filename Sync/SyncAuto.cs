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
        
        // ========== 高亮显示管道 ==========
        private static HighlightAppearance _highlightConduit;

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
                RhinoApp.Closing += OnRhinoClosing;

                // 创建并启用高亮显示管道
                if (_highlightConduit == null)
                {
                    _highlightConduit = new HighlightAppearance();
                    _highlightConduit.Enabled = true;
                }

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
                RhinoApp.Closing -= OnRhinoClosing;

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
                if (_highlightConduit != null)
                {
                    _highlightConduit.Enabled = false;
                    _highlightConduit = null;
                }

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
            // 两个计数器各自+1
            _syncCounter++;
            _connCheckCounter++;

            // 计数器1：数到同步间隔就执行增量同步
            int syncTarget = SyncInterval / 1000;  // 转换为秒
            if (_syncCounter >= syncTarget)
            {
                ProcessPendingChanges();  // 执行增量同步
                _syncCounter = 0;         // 重置计数器
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

                // 触发每秒事件，供面板订阅显示连接时间等
                SecondPassed?.Invoke();
            }
        }



    }
}
