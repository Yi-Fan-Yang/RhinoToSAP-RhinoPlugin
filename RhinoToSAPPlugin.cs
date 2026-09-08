using System;
using Rhino;
using Rhino.PlugIns;
using RhinoToSAP.Sync;
using RhinoToSAP.UI;

namespace RhinoToSAP
{
    ///<summary>
    /// <para>Every RhinoCommon .rhp assembly must have one and only one PlugIn-derived
    /// class. DO NOT create instances of this class yourself. It is the
    /// responsibility of Rhino to create an instance of this class.</para>
    /// <para>To complete plug-in information, please also see all PlugInDescription
    /// attributes in AssemblyInfo.cs (you might need to click "Project" ->
    /// "Show All Files" to see it in the "Solution Explorer" window).</para>
    ///</summary>
    public class RhinoToSAPPlugin : Rhino.PlugIns.PlugIn
    {
        public RhinoToSAPPlugin()
        {
            Instance = this;
        }

        ///<summary>Gets the only instance of the RhinoToSAPPlugin plug-in.</summary>
        public static RhinoToSAPPlugin Instance { get; private set; }

        // You can override methods here to change the plug-in behavior on
        // loading and shut down, add options pages to the Rhino _Option command
        // and maintain plug-in wide options in a document.

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            try
            {
                // 1. 注册面板到Rhino右侧面板栏
                Rhino.UI.Panels.RegisterPanel(
                    this,                              // 插件实例
                    typeof(MainPanel),                 // 面板类型
                    "RhinoToSAP",                      // 面板标题
                    null,                              // 图标（null用默认）
                    Rhino.UI.PanelType.PerDoc);        // 每个文档一个面板实例

                // 2. 订阅Rhino关闭事件
                RhinoApp.Closing += OnRhinoClosing;
                // 2. 订阅Rhino空闲事件，第一次空闲时自动打开面板
                RhinoApp.Idle += OnFirstIdle;

                RhinoApp.WriteLine("[RhinoToSAP] 插件加载成功");
                return LoadReturnCode.Success;
            }
            catch (Exception ex)
            {
                errorMessage = $"RhinoToSAP插件加载失败：{ex.Message}";
                return LoadReturnCode.ErrorShowDialog;
            }
        }

        // Rhino关闭时触发：保存映射文件+断开连接
        private void OnRhinoClosing(object sender, EventArgs e)
        {
            try{SAPConnector.Disconnect();}
            catch{}
        }
        
        // 第一次Rhino空闲时打开面板，然后取消订阅
        private void OnFirstIdle(object sender, EventArgs e)
        {
            try { Rhino.UI.Panels.OpenPanel(typeof(MainPanel).GUID); }// 打开面板（浮动独立窗口）
            catch { }
            finally { RhinoApp.Idle -= OnFirstIdle; }                // 只执行一次，取消订阅
        }
        // 插件卸载时调用
        protected override void OnShutdown()
        {
            try
            {
                RhinoApp.Closing -= OnRhinoClosing;
                SyncEngine.Shutdown();
            }
            catch{ }
            base.OnShutdown();
        }
    }
}