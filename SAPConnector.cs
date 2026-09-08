using System;
using Rhino;
using Rhino.DocObjects;
using System.Runtime.InteropServices;
using CSiAPIv1;
using RhinoToSAP.Sync;
using RhinoToSAP.Tools;
using System.Linq.Expressions;

namespace RhinoToSAP
{
    /// <summary>
    /// SAP连接单例，整个插件共用一个SAP实例，避免重复Attach
    /// </summary>
    public static class SAPConnector
    {
        private static cOAPI _sapApp;
        private static cSapModel _sapModel;
        //记录连接开始时间
        private static DateTime _connectStartTime = DateTime.MinValue;
        //对外只读，记录连接时长
        public static TimeSpan ConnectDuration => _connectStartTime == DateTime.MinValue ? TimeSpan.Zero : DateTime.Now - _connectStartTime;
        /// 是否已连接到SAP实例
        public static bool IsConnected => _sapApp != null && _sapModel != null;

        // SAP应用对象
        public static cOAPI SapApp => _sapApp;
        // SAP模型对象
        public static cSapModel SapModel => _sapModel;
        ///Rhino根图层
        public static string RootLayerName { get; set; }= string.Empty;
        // 记录最近一次单位校验结果，供连接电池显示
        public static string UnitCheckMessage = string.Empty;
        // 防止断开过程中重复调用Disconnect导致重复弹窗
        private static bool _isDisconnecting = false;
        // 记录最后一次连接的SAP模型名称（SAP关闭后保存映射文件时用）
        public static string LastSapModelName = string.Empty;


        //==================方法=====================
        // 连接SAP：内部做所有校验、连接、单位校验、初始化，返回结果信息
        public static string Connect(string layerName, string intervalText)
        {
            // ========== 1. 校验 ==========
            // 图层名校验
            if (string.IsNullOrEmpty(layerName))
            {
                return "请先选择图层";
            }

            // 同步间隔解析和校验
            int intervalSeconds;
            if (!int.TryParse(intervalText, out intervalSeconds))
            {
                return "同步间隔输入无效，请输入1~30之间的整数";
            }
            if (intervalSeconds < 1 || intervalSeconds > 30)
            {
                return $"同步间隔必须在1~30秒之间，当前输入：{intervalSeconds}秒";
            }

            // ========== 2. 设置参数 ==========
            RootLayerName = layerName;
            SyncEngine.SyncInterval = intervalSeconds * 1000;

            // ========== 3. 连接SAP ==========
            try
            {
                _sapApp = (cOAPI)Marshal.GetActiveObject("CSI.SAP2000.API.SapObject");
                if (_sapApp == null)
                {
                    return "未找到正在运行的SAP2000实例，请先打开SAP2000并新建空白模型";
                }
                _sapModel = _sapApp.SapModel;
                if (_sapModel == null)
                {
                    return "SAP实例已找到，但获取SapModel失败";
                }
                _connectStartTime = DateTime.Now;
                LastSapModelName = _sapModel.GetModelFilename(true);
            }
            catch (COMException ex)
            {
                _sapApp = null;
                _sapModel = null;
                return $"COM连接异常：{ex.Message}（请确认Rhino和SAP都以管理员身份运行）";
            }
            catch (Exception ex)
            {
                _sapApp = null;
                _sapModel = null;
                return $"连接异常：{ex.Message}";
            }

            // ========== 4. 单位校验 ==========
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc != null)
            {
                string unitMsg;
                if (CheckUnits(doc, out unitMsg))
                {
                    // 单位一致，解锁图层
                    LayerHelper.UnLockLayer(doc, layerName);
                }
                else
                {
                    // 单位不一致，锁定图层
                    LayerHelper.LockLayer(doc, layerName);
                }
            }

            // ========== 5. 初始化同步引擎 ==========
            SyncEngine.Initialize();
            SyncEngine.UpdateTimerState();

            return "连接成功，同步引擎已初始化";
        }

        //检查Rhino和SAP的单位是否一致
        public static bool CheckUnits(RhinoDoc doc, out string message)
        {
            try
            {
                UnitSystem rhinoUnit = doc.ModelUnitSystem;
                eUnits sapUnit = _sapModel.GetPresentUnits();

                eUnits expectedSapUnit;
                switch (rhinoUnit)
                {
                    case UnitSystem.Millimeters:
                        expectedSapUnit = eUnits.kN_mm_C;
                        break;
                    case UnitSystem.Centimeters:
                        expectedSapUnit = eUnits.kN_cm_C;
                        break;
                    case UnitSystem.Meters:
                        expectedSapUnit = eUnits.kN_m_C;
                        break;
                    default:
                        message = $"❌ 连接成功，不支持的Rhino单位：{rhinoUnit}";
                        return false;
                }
                if (expectedSapUnit == sapUnit)
                {
                    // 单位一致
                    message = $"✅ 连接成功，单位一致，当前单位：{rhinoUnit}";
                    return true;
                }
                else
                {
                    // 单位不一致
                    message = $"❌ 连接成功，单位不一致：Rhino单位是{rhinoUnit}，SAP单位是{sapUnit}，请统一单位";
                    return false;
                }
            }
            catch
            {
                message = $"SAP已退出";
                return false;
            }
        }

        // 检测SAP连接是否仍然存活（SAP关闭后会返回false）
        public static bool CheckConnectionAlive()
        {
            if (_sapApp == null || _sapModel == null) return false;
            try
            {
                // 调用一个最简单的API，能成功返回说明连接正常,用GetPresentUnit()，没有参数，调用最快
                _sapModel.GetPresentUnits();
                return true;
            }
            catch
            {
                // SAP关闭后调用COM对象会抛异常，说明连接已经断了
                return false;
            }
        }

        //断开连接（一般不用手动调用，关闭Rhino自动释放）
        public static void Disconnect()
        {
            if (_isDisconnecting) return;// 正在断开中，不重复执行
            _isDisconnecting = true;
            try
            {
                //断开前询问保存映射表
                if (SyncEngine.IsMappingLoaded)
                {
                    var result = Rhino.UI.Dialogs.ShowMessage(
                                "是否保存当前映射文件？", "保存映射文件",
                                Rhino.UI.ShowMessageButton.YesNoCancel,
                                Rhino.UI.ShowMessageIcon.Question);
                    if (result == Rhino.UI.ShowMessageResult.Yes)
                    {
                        SyncPersistenceIO.SaveMapping();
                    }
                }

                // 先停止同步引擎，注销事件、释放所有计时器
                SyncEngine.Shutdown();

                // 锁定绑定的根图层（如果有活动文档和绑定图层）
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc != null && !string.IsNullOrEmpty(RootLayerName))
                {
                    LayerHelper.LockLayer(doc, RootLayerName);
                }

                // 清空绑定的图层名
                RootLayerName = string.Empty;

                // 清空SAP连接对象
                if(_sapModel != null)
                {
                    Marshal.FinalReleaseComObject( _sapModel );
                    _sapModel = null;
                }
                if(_sapApp != null)
                {
                    Marshal.FinalReleaseComObject(_sapApp);
                    _sapApp = null;
                }
                LastSapModelName = string.Empty;
                _connectStartTime = DateTime.MinValue;
            }
            catch
            {
                // 吞掉异常，避免断开过程中出错导致Rhino崩溃
            }
            finally
            {
                _isDisconnecting = false;  // 无论成功失败都重置
            }
        }
    }
}