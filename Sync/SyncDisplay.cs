using Rhino;
using RhinoToSAP.Display;
using RhinoToSAP.Sync;
using System;

namespace RhinoToSAP.Sync
{
    // 高亮显示管理：管理已同步对象的高亮显示
    // 从SyncAuto里摘出来，单独管理显示相关的逻辑
    public static class SyncDisplay
    {
        // 高亮显示管道实例
        private static HighlightAppearance _highlightConduit;

        // 初始化高亮管道（连接成功后调用）
        public static void Initialize()
        {
            if (_highlightConduit == null)
            {
                _highlightConduit = new HighlightAppearance();
                _highlightConduit.Enabled = true;
            }
        }

        // 释放高亮管道（断开连接时调用）
        public static void Shutdown()
        {
            if (_highlightConduit != null)
            {
                _highlightConduit.Enabled = false;
                _highlightConduit = null;
            }
        }

        // 设置高亮开关
        public static string SetHighlightEnabled(bool enabled)
        {
            if (_highlightConduit != null)
            {
                _highlightConduit.Enabled = enabled;
            }
            return $"高亮显示：{(enabled ? "开启" : "关闭")}";
        }

        // 设置高亮线宽
        public static string SetHighlightWidth(string widthText)
        {
            int width;
            if (!int.TryParse(widthText, out width))
            {
                return "高亮线宽输入无效，请输入1~20之间的整数";
            }
            if (width < 1 || width > 20)
            {
                return $"高亮线宽需在1~20像素之间，当前输入：{width}";
            }
            if (_highlightConduit != null)
            {
                _highlightConduit.HighlightWidth = width;
            }
            return $"高亮线宽：{width}像素";
        }
    }
}
