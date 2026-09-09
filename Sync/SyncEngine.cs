using CSiAPIv1;
using Eto;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Display;
using RhinoToSAP.MappingFile;
using RhinoToSAP.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WinFormsTimer = System.Windows.Forms.Timer;


namespace RhinoToSAP.Sync
{
    public static class SyncEngine
    {
        // ========== 核心调度字段 ==========
        // 计时器
        private static WinFormsTimer _Timer;
        // 增量同步计数器：数到目标值就执行一次同步，然后归零
        private static int _syncCounter = 0;
        // 连接检测计数器：数到5就检查一次SAP连接，然后归零
        private static int _connCheckCounter = 0;
        // 高亮显示管道实例
        private static HighlightAppearance _highlightConduit;

        public static HashSet<Guid> pendingChanges = new HashSet<Guid>();
        
        // 初始化标志位：避免重复注册事件、重复启动计时器
        public static bool isInitialized = false;
        
        //映射文件是否已加载成功（只有连接+映射都就绪，Timer才启动，同步才可用）
        public static bool IsMappingLoaded = false;
        // 事件：每秒触发一次，供外部订阅
        public static event Action SecondPassed;  


        // ========== 常量配置 ==========
        // 同步间隔，默认3000ms
        private static int _syncInterval = 3000;
        public static int SyncInterval
        {
            get => _syncInterval; 
            set
            {
                if (value < 1000) value = 1000; // 最小1000ms
                if(value > 30000) value = 30000; // 最大30000ms
                _syncInterval = value;
            }
        }
        // 同步次数计数器：每次执行ProcessPendingChanges就+1
        private static int _syncCount = 0;
        public static int SyncCount => _syncCount;






        // ========== 公共方法：对外提供的接口 ==========
        // 初始化同步引擎：注册Rhino事件、启动防抖计时器，只需要调用一次
        // 连接成功、单位校验通过、绑定图层后调用
        public static void Initialize()
        {
            if (isInitialized) return;
            _syncCount = 0;// 重新连接时同步次数清零
            try
            {
                // 注册Rhino对象事件：用户增删改对象时自动触发对应方法
                // RhinoDoc是Rhino的文档类，这些都是静态事件，直接+=订阅
                RhinoDoc.AddRhinoObject += SyncRhinoDispatcher.OnObjectAdded;       // 对象添加事件
                RhinoDoc.DeleteRhinoObject += SyncRhinoDispatcher.OnObjectDeleted;   // 对象删除事件
                RhinoDoc.ReplaceRhinoObject += SyncRhinoDispatcher.OnObjectReplaced; // 对象修改事件（移动、改坐标等都会触发）
                RhinoDoc.ModifyObjectAttributes += SyncRhinoDispatcher.OnObjectAttributesModified;//对象属性修改时间
                RhinoApp.Closing += SyncRhinoDispatcher.OnRhinoClosing;  // Rhino关闭时弹窗保存映射文件

                // 创建并启用高亮显示管道
                if (_highlightConduit == null)
                {
                    _highlightConduit = new HighlightAppearance();
                    _highlightConduit.Enabled = true;
                }

                //标记已经初始化
                isInitialized = true;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"SyncEngine初始化失败：{ex.Message}");
            }
        }

        // 停止同步引擎：注销事件、停止计时器，插件卸载时调用
        public static void Shutdown()
        {
            if (!isInitialized) return;

            try
            {
                // 注销事件，和注册的时候一一对应，用-=
                RhinoDoc.AddRhinoObject -= SyncRhinoDispatcher.OnObjectAdded;
                RhinoDoc.DeleteRhinoObject -= SyncRhinoDispatcher.OnObjectDeleted;
                RhinoDoc.ReplaceRhinoObject -= SyncRhinoDispatcher.OnObjectReplaced;
                RhinoDoc.ModifyObjectAttributes -= SyncRhinoDispatcher.OnObjectAttributesModified;
                RhinoApp.Closing -= SyncRhinoDispatcher.OnRhinoClosing;

                // 停止并释放同步计时器
                if (_Timer != null)
                {
                    _Timer.Stop();
                    _Timer.Dispose();
                    _Timer = null;
                }

                // 停止并释放高亮显示管道
                if (_highlightConduit != null)
                {
                    _highlightConduit.Enabled = false;
                    _highlightConduit = null;
                }
                isInitialized = false;
                IsMappingLoaded = false;  // 停止同步引擎时重置映射加载状态
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"SyncEngine销毁失败：{ex.Message}");
            }
        }

        //全量同步
        public static string FullSync()
        {
            if (!CheckReady(out string errorMsg))
            {
                return errorMsg;
            }

            if (string.IsNullOrEmpty(SAPConnector.RootLayerName))
            {
                return "未设置根图层";
            }
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return "没有打开的Rhino文档";
            
            //初始化前清空状态，避免旧的映射关系干扰
            SyncStateManager.ClearState();
            if (!ClearAllSapObjects(out string Msg))
            {
                return Msg;
            }   

            //递归获取根图层下的所有子图层
            Layer rootlayer = doc.Layers.FindName(SAPConnector.RootLayerName);
            List<Layer> layers = new List<Layer>();
            LayerHelper.GetAllChildLayers(rootlayer, layers);

            //遍历所有子图层，获取每个图层下的所有有效杆件
            foreach (Layer i in layers)
            {
                RhinoObject[] objs = doc.Objects.FindByLayer(i);
                if (objs == null) continue;
                foreach (RhinoObject obj in objs)
                {
                    if (SyncFrame.TryExplodePolylineAndQueue(doc, obj)) continue;
                    SyncRhinoDispatcher.AddToPendingIfValid(obj);
                }
            }
            RhinoApp.WriteLine($"[FullSync] 找到图层数量：{layers.Count}，待处理变化数量：{pendingChanges.Count}");
            // 遍历完所有图层后，立刻执行处理
            ProcessPendingChanges();
            MappingFileManager.Save();
            return ($"全量同步完成：{MappingFileManager.report}");
        }

        //手动同步：立即处理所有待处理列表，不等待计时器触发
        public static string ManualSync()
        {
            if (!CheckReady(out string errorMsg))
            {
                return errorMsg;
            }
            ProcessPendingChanges();//立即处理全部待处理列表
            _syncCounter = 0;//手动同步后归零
            return "手动增量同步完成";
        }


        // ----- 计时器触发时执行 -----
        // 根据连接状态和映射加载状态，自动启动或停止Timer
        public static void UpdateTimerState()
        {
            bool shouldRun = SAPConnector.IsConnected && IsMappingLoaded;
            //RhinoApp.WriteLine($"[UpdateTimerState] shouldRun={shouldRun}, IsConnected={SAPConnector.IsConnected}, " +
                //$"IsMappingLoaded={IsMappingLoaded}, _Timer==null={_Timer == null}");
            if (shouldRun && _Timer == null)
            {
                // 初始化计时器
                _Timer = new WinFormsTimer();
                _Timer.Interval = 1000;
                _Timer.Tick += OnTimerTick;
                _Timer.Start();
                RhinoApp.WriteLine("[SyncEngine] Timer已启动");
            }
            else if (!shouldRun && _Timer != null)
            {
                _Timer.Stop();
                _Timer.Dispose();
                _Timer = null;
                _syncCounter = 0;
                _connCheckCounter = 0;
                RhinoApp.WriteLine("[SyncEngine] Timer已停止");
            }
        }

        // 计时器Tick事件：批量处理所有待处理的变化
        public static void OnTimerTick(object sender, EventArgs e)
        {
            RhinoApp.WriteLine($"[OnTimerTick] 第{_syncCounter}秒");
            // 两个计数器各自+1
            _syncCounter++;
            _connCheckCounter++;

            // 计数器1：数到同步间隔就执行增量同步
            int syncTarget = SyncInterval / 1000; // 转换为秒
            if (_syncCounter >= syncTarget)
            {
                ProcessPendingChanges();
                _syncCounter = 0; // 重置计数器
            }
            
            // 计数器2：数到5就检查SAP连接
            if (_connCheckCounter >= 5)
            {
                if(!SAPConnector.CheckConnectionAlive())
                {
                    RhinoApp.WriteLine("[SyncEngine] SAP连接已断开，自动锁定图层");
                    SAPConnector.Disconnect();
                    _connCheckCounter = 0; // 重置计数器
                    return;
                }

                if(!SAPConnector.CheckUnits(RhinoDoc.ActiveDoc, out string unitMsg))
                {
                    LayerHelper.LockLayer(RhinoDoc.ActiveDoc, SAPConnector.RootLayerName);
                    SAPConnector.UnitCheckMessage = unitMsg;
                    RhinoApp.WriteLine($"[SyncEngine] 单位不匹配,，自动锁定图层");
                }
                _connCheckCounter = 0;// 重置计数器
                SecondPassed?.Invoke();
            }
        }

        //整体处理待处理列表
        public static void ProcessPendingChanges()
        {
            if (!IsMappingLoaded) return;  // 映射文件未加载，不处理
            try
            {
                if (!SAPConnector.IsConnected
                    || (string.IsNullOrEmpty(SAPConnector.RootLayerName))
                    || (RhinoDoc.ActiveDoc == null))
                {
                    pendingChanges.Clear();
                    return;
                }
                _syncCount++;
                Guid[] pengdingIds = pendingChanges.ToArray();
                if (pengdingIds.Length == 0) return;
                RhinoApp.WriteLine($"[处理] 开始处理{pengdingIds.Length}个对象");
                foreach (Guid i in pengdingIds)
                {
                    try
                    {
                        SyncRhinoDispatcher.ProcessSingleChange(RhinoDoc.ActiveDoc, i);
                    }
                    catch (Exception ex)
                    {
                        RhinoApp.WriteLine($"[处理] 对象{i}处理异常：{ex.Message}");
                    }
                }
                pendingChanges.Clear();
                RhinoApp.WriteLine("[处理] 处理完成");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[处理] 整体异常：{ex.Message}");
                pendingChanges.Clear();
            }
        }

        // 内部统一校验：连接、映射文件、初始化都就绪才返回true
        private static bool CheckReady(out string errorMsg)
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


        // 设置高亮显示开关
        public static string SetHighlightEnabled(bool enabled)
        {
            if (_highlightConduit != null) { _highlightConduit.Enabled = enabled; }
            return $"高亮显示：{(enabled ? "开启" : "关闭")}";
        }
        // 设置高亮线宽
        public static string SetHighlightWidth(string widthText)
        {
            // 1. 解析成int
            int width;
            if (!int.TryParse(widthText, out width))
            {
                return "高亮线宽输入无效，请输入1~20之间的整数";
            }

            // 2. 范围校验
            if (width < 1 || width > 20)
            {
                return $"高亮线宽需在1~20像素之间，当前输入：{width}";
            }

            // 3. 校验通过，设置线宽
            if (_highlightConduit != null)
            {
                _highlightConduit.HighlightWidth = width;
            }

            return $"高亮线宽：{width}像素";
        }

        // 删除SAP里所有单元和节点（全量同步前调用）
        private static bool ClearAllSapObjects(out string Msg)
        {
            try
            {
                // 1. 全选模型中所有对象
                SAPConnector.SapModel.SelectObj.All(false);

                // 2. 删除
                SAPConnector.SapModel.FrameObj.Delete("ignore", eItemType.Objects);
                SAPConnector.SapModel.AreaObj.Delete("ignore", eItemType.Objects);     
                SAPConnector.SapModel.LinkObj.Delete("ignore", eItemType.Objects);     
                SAPConnector.SapModel.PointObj.DeleteSpecialPoint("ignore", eItemType.Objects);
                Msg = "已清空SAP中所有单元和节点";
                return true;
            }
            catch (Exception ex)
            {
                Msg = $"清空SAP对象失败{ex.Message}";
                return false;
            }
        }

        //读取同步间隔方法
        public static string SetSyncInterval(string intervalText)
        {
            int interval;
            if (!int.TryParse(intervalText, out interval))
            {
                return "同步间隔输入无效，请输入1~30之间的整数";
            } 
            if (interval < 1 || interval > 30)
            {
                return "同步间隔需在1~30秒之间";
            }
            SyncInterval = interval * 1000;
            return $"同步间隔：{interval}秒";
        }

    }
}

