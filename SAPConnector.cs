using CSiAPIv1;
using Rhino;
using Rhino.DocObjects;
using RhinoToSAP.Sync;
using RhinoToSAP.Tools;
using System;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Contexts;

namespace RhinoToSAP
{
    /// <summary>
    // SAP连接单例，整个插件共用一个SAP实例，避免重复Attach
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
        // 配置文件路径：AppData\Roaming\RhinoToSAP\config.txt
        private static readonly string ConfigFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RhinoToSAP", "config.txt");


        //==================方法=====================
        // 连接SAP：内部做所有校验、连接、单位校验、初始化，返回结果信息
        public static string Connect(string layerName, string intervalText)
        {
            SapMessageFilter.Register();
            // ========== 1. 校验 ==========
            // 图层名校验
            if (string.IsNullOrEmpty(layerName))
            {
                return "请先选择图层";
            }

            // 同步间隔解析和校验
            if (string.IsNullOrEmpty(intervalText))
            {
                return "请设置同步间隔";
            }

            // ========== 2. 设置参数 ==========
            RootLayerName = layerName;
            SyncEngine.SyncInterval = int.TryParse(intervalText, out int interval) ? interval * 1000 : 3000;

            // ========== 3. 获取Rhino单位 ==========
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                return "没有打开的Rhino文档";
            }
            eUnits sapUnit = RhinoUnitToSapUnit(doc.ModelUnitSystem);

            // ========== 4. 弹打开文件窗口 ==========
            string sapFileName = OpenSapFileDialog();

            // ========== 5. 创建并启动SAP ==========
            try
            {
                // 5.1 获取SAP2000.exe路径（从配置读，没有就弹窗选）
                string sapExePath = GetSapExePath();
                if (string.IsNullOrEmpty(sapExePath))
                {
                    return "未选择SAP2000.exe，启动取消";
                }

                // 5.2 创建Helper对象，通过Helper从指定路径创建SapObject（不依赖COM注册）
                cHelper helper = new Helper();
                _sapApp = helper.CreateObject(sapExePath);
                if (_sapApp == null)
                {
                    return "创建SAP对象失败，请检查SAP2000.exe路径是否正确";
                }
                // 5.3 启动SAP应用：单位用Rhino当前单位，窗口可见，打开用户选的模型文件
                int ret = _sapApp.ApplicationStart(sapUnit, true, sapFileName);
                if (ret != 0)
                {
                    return $"启动SAP失败，错误码：{ret}";
                }
                // 5.4 获取SapModel
                _sapModel = _sapApp.SapModel;
                if (_sapModel == null)
                {
                    return "SAP启动成功，但获取SapModel失败";
                }
                // 5.5 记录连接开始时间和模型名称
                _connectStartTime = DateTime.Now;
                LastSapModelName = _sapModel.GetModelFilename(true);
            }
            catch (Exception ex)
            {
                _sapApp = null;
                _sapModel = null;
                return $"启动SAP异常：{ex.Message}";
            }


            // ========== 6. 初始化同步引擎 ==========
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

                eUnits expectedSapUnit = RhinoUnitToSapUnit(rhinoUnit);
                if (expectedSapUnit == sapUnit)
                {
                    // 单位一致
                    message = $"✅ 单位一致，当前单位：{rhinoUnit}";
                    return true;
                }
                else
                {
                    // 单位不一致
                    message = $"❌ 单位不一致：Rhino单位是{rhinoUnit}，SAP单位是{sapUnit}，请统一单位";
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

                // 关闭SAP程序（true表示保存文件后关闭）
                if (_sapApp != null)
                {
                    try { _sapApp.ApplicationExit(true); }
                    catch { }
                    _sapModel = null;
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

        // Rhino单位转SAP单位枚举
        private static eUnits RhinoUnitToSapUnit(UnitSystem rhinoUnit)
        {
            switch (rhinoUnit)
            {
                case UnitSystem.Millimeters:
                    return eUnits.kN_mm_C;
                case UnitSystem.Centimeters:
                    return eUnits.kN_cm_C;
                case UnitSystem.Meters:
                    return eUnits.kN_m_C;
                default:
                    return eUnits.kN_mm_C;  // 默认毫米
            }
        }

        // 弹打开文件窗口，让用户选择SAP模型文件，取消返回空字符串
        private static string OpenSapFileDialog()
        {
            var openDlg = new Eto.Forms.OpenFileDialog();
            openDlg.Filters.Add(new Eto.Forms.FileFilter("SAP模型文件", ".sdb"));
            openDlg.Filters.Add(new Eto.Forms.FileFilter("所有文件", ".*"));
            openDlg.Title = "打开SAP模型文件";

            if (openDlg.ShowDialog(Eto.Forms.Application.Instance.MainForm) != Eto.Forms.DialogResult.Ok)
            {
                return string.Empty;
            }

            return openDlg.FileName;
        }

        // 从配置文件读取SAP2000.exe的路径，没有或读取失败返回空字符串
        private static string LoadSapPath()
        {
            try
            {
                //RhinoApp.WriteLine("[LoadSapPath] 配置文件路径：" + ConfigFilePath);
                //hinoApp.WriteLine("[LoadSapPath] 文件是否存在：" + System.IO.File.Exists(ConfigFilePath));
                if (!System.IO.File.Exists(ConfigFilePath))return string.Empty;
                //string content = System.IO.File.ReadAllText(ConfigFilePath);
                //RhinoApp.WriteLine("[LoadSapPath] 读取到的原始内容：" + content);
                //RhinoApp.WriteLine("[LoadSapPath] 原始内容长度：" + content.Length);
                return System.IO.File.ReadAllText(ConfigFilePath).Trim();

            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("[LoadSapPath] 读取异常：" + ex.Message);
                return string.Empty;
            }
        }

        // 把SAP2000.exe的路径保存到配置文件
        private static void SaveSapPath(string path)
        {
            try
            {
                RhinoApp.WriteLine("[SaveSapPath] 开始保存，路径：" + path);
                string dir = System.IO.Path.GetDirectoryName(ConfigFilePath);
                RhinoApp.WriteLine("[SaveSapPath] 目标文件夹：" + dir);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                    RhinoApp.WriteLine("[SaveSapPath] 文件夹已创建");
                }
                System.IO.File.WriteAllText(ConfigFilePath, path);
                RhinoApp.WriteLine("[SaveSapPath] 保存成功");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("[SaveSapPath] 保存失败：" + ex.Message);
            }
        }
        // 获取SAP2000.exe路径：先从配置读，没有或无效就弹窗选，选完保存
        private static string GetSapExePath()
        {
            string path = LoadSapPath();
            RhinoApp.WriteLine("[GetSapExePath] 从配置读取的路径：" + path);
            RhinoApp.WriteLine("[GetSapExePath] 路径长度：" + path.Length);
            RhinoApp.WriteLine("[GetSapExePath] 文件是否存在：" + System.IO.File.Exists(path));

            if (System.IO.File.Exists(path) && !string.IsNullOrEmpty(path))
            {
                return path;
            }

            // 弹窗选择SAP2000.exe路径
            var openDlg = new Eto.Forms.OpenFileDialog();
            openDlg.Filters.Add(new Eto.Forms.FileFilter("SAP2000.exe", "SAP2000.exe"));
            openDlg.Title = "选择SAP2000.exe路径";

            if (openDlg.ShowDialog(Eto.Forms.Application.Instance.MainForm) != Eto.Forms.DialogResult.Ok)
            {
                return string.Empty;
            }

            path = openDlg.FileName;
            SaveSapPath(path);
            return path;
        }
    }

    // OLE消息过滤器：处理SAP启动时的"服务器正在运行中"弹窗，自动重试不弹窗
    [ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);
        [PreserveSig]
        int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);
        [PreserveSig]
        int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
    }

    public class SapMessageFilter : IOleMessageFilter
    {
        public int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        {
            return 0; // 正常处理
        }

        public int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
        {
            if (dwRejectType == 2) // 2 = SERVERCALL_RETRYLATER（服务器忙）
            {
                return 1000; // 1000毫秒后自动重试
            }
            return -1; // 其他情况取消
        }

        public int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        {
            return 2; // 等待默认处理
        }

        // 注册消息过滤器（调用一次就行）
        public static void Register()
        {
            CoRegisterMessageFilter(new SapMessageFilter(), out _);
        }

        [DllImport("Ole32.dll")]
        private static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);
    }

}