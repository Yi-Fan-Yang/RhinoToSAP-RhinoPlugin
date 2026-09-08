using System;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Sync;

namespace RhinoToSAP.Display
{
    // 显示管道：在Rhino视图中用高亮颜色加粗绘制已同步的杆件
    public class HighlightAppearance : DisplayConduit
    {
        // ========== 可配置参数 ==========
        // 高亮颜色：半透明绿色（Alpha=100表示半透明）
        public Color HighlightColor { get; set; } = Color.FromArgb(100, 0, 255, 0);
        // 高亮线宽：像素
        public int HighlightWidth { get; set; } = 5;

        // ========== 核心绘制方法 ==========
        // PostDrawObjects：所有对象绘制完成后触发，叠加高亮效果
        protected override void PostDrawObjects(DrawEventArgs e)
        {
            // 1. 安全检查：映射文件未加载或未连接，不绘制高亮
            if (!SyncEngine.IsMappingLoaded || !SAPConnector.IsConnected) return;

            // 2. 遍历映射表中的所有Rhino对象ID
            foreach (var map in SyncStateManager.GetAllMappings())
            {
                Guid rhinoId = map.Key;
                // 3. 获取Rhino对象
                RhinoObject Obj = e.RhinoDoc.Objects.FindId(rhinoId);
                if (Obj == null) continue; // 对象不存在，跳过
                
                // 4. 只处理可见的有效直线对象
                if (Obj.Visible == false) continue; // 对象不可见，跳过
                
                // 5. 处理直线对象
                if(Obj.Geometry is LineCurve lc)
                {
                    e.Display.DrawLine(lc.Line, HighlightColor, HighlightWidth);
                }

            }
        }


    }
}

