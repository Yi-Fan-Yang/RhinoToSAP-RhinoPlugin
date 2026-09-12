using CSiAPIv1;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Data;
using RhinoToSAP.Sync;
using System;
using System.Collections.Generic;
using static System.Windows.Forms.LinkLabel;

namespace RhinoToSAP.Tools
{
    public static class LineHelper
    {
        /// 坐标对比容差，和Rhino文档精度一致，误差在这个范围内不算修改
        private const double CoordinateTolerance = 1.0;
        // 直线最新状态表：前置去重，同一ID只保留最新状态
        private static Dictionary<Guid, LineState> _latestLineStates = new Dictionary<Guid, LineState>();




        // ----- 公共方法 -----
        //判断Geo是否是直线
        public static bool IsValidLineObject(RhinoObject obj)
        {
            if (obj == null) return false;
            if (!obj.Visible) return false;
            if (!(obj.Geometry is Curve curve)) return false;
            if (!curve.IsLinear(obj.Document.ModelAbsoluteTolerance)) return false;
            return true; ;
        }

        //将RhinoObject转换为LineState
        public static LineState ToLineState(RhinoObject obj)
        {
            if (!IsValidLineObject(obj)) return null;
            Layer layer = obj.Document.Layers[obj.Attributes.LayerIndex];
            string layerName = layer.FullPath;
            Curve c = obj.Geometry as Curve;
            Line line = new Line(c.PointAtStart, c.PointAtEnd);
            return new LineState(obj.Id, line, layerName);
        }

        //按rhino几何新增sap杆件
        public static string CreateFrame(LineState state, string sectionName)
        {
            if (!SAPConnector.IsConnected) return string.Empty;
            if (state == null) return string.Empty;
            if (string.IsNullOrEmpty(sectionName)) return string.Empty;

            string frameId = string.Empty;

            int ret = SAPConnector.SapModel.FrameObj.AddByCoord(
                        state.X1, state.Y1, state.Z1, state.X2, state.Y2, state.Z2,  // 起点终点XYZ
                        ref frameId,    // 杆件ID
                        sectionName,        // 输入的SAP截面
                        "",             // 框架属性名
                        "Global");       // 坐标系
            if (ret != 0) return string.Empty;
            if (!string.IsNullOrEmpty(state.LayerName))
            {
                SAPConnector.SapModel.GroupDef.SetGroup(state.LayerName);
                int ret2 = SAPConnector.SapModel.FrameObj.SetGroupAssign(
                        frameId,
                        state.LayerName,
                        false,
                        eItemType.Objects);
            }
            return frameId;
        }
        //按rhino几何修改sap杆件
        public static bool UpdateFrame(LineState state, string frameId)
        {

            if (!SAPConnector.IsConnected) return false;
            if (state == null) return false;
            if (string.IsNullOrEmpty(frameId)) return false;

            // 移动杆件：先移动杆件到一个远离原点的位置，然后再修改杆件的端点坐标，最后将杆件移动回原来的位置。
            const double moveOffset = 10000000;
            try
            {
                SAPConnector.SapModel.FrameObj.SetSelected(frameId, true);
                int retmove = SAPConnector.SapModel.EditGeneral.Move(moveOffset, moveOffset, moveOffset);
                if (retmove != 0)
                {
                    SAPConnector.SapModel.FrameObj.SetSelected(frameId, false);
                    return false;
                }

                SAPConnector.SapModel.FrameObj.SetSelected(frameId, false);
                string point1 = "";
                string point2 = "";
                int retGetPoints = SAPConnector.SapModel.FrameObj.GetPoints(frameId, ref point1, ref point2);

                if (retGetPoints != 0) return false;

                int retpoint1 = SAPConnector.SapModel.EditPoint.ChangeCoordinates_1(point1, state.X1, state.Y1, state.Z1);
                int retpoint2 = SAPConnector.SapModel.EditPoint.ChangeCoordinates_1(point2, state.X2, state.Y2, state.Z2);

                // 更新图层分组
                bool groupsuccess = true;
                if (!string.IsNullOrEmpty(state.LayerName))
                {
                    SAPConnector.SapModel.GroupDef.SetGroup(state.LayerName);
                    int retgroup = SAPConnector.SapModel.FrameObj.SetGroupAssign(
                            frameId,
                            state.LayerName,
                            false,
                            eItemType.Objects);
                    groupsuccess = retgroup == 0;
                }
                //判断是否成功
                return (retpoint1 == 0 && retpoint2 == 0 && groupsuccess);
            }
            catch
            {
                return false;
            }
        }
        // 删除SAP杆件
        public static bool DeleteFrame(string frameId)
        {
            try
            {
                if (!SAPConnector.IsConnected) return false;
                if (string.IsNullOrEmpty(frameId)) return false;

                int ret = SAPConnector.SapModel.FrameObj.Delete(frameId);
                return ret == 0;
            }
            catch
            {
                return false;
            }
        }

        // 对比两个LineState的坐标是否一致（在容差范围内）
        public static LineChangeType GetLineChangeType(LineState a, LineState b)
        {
            double dx1 = a.X1 - b.X1;
            double dy1 = a.Y1 - b.Y1;
            double dz1 = a.Z1 - b.Z1;
            double dx2 = a.X2 - b.X2;
            double dy2 = a.Y2 - b.Y2;
            double dz2 = a.Z2 - b.Z2;
            if
                (Math.Abs(dx1) < CoordinateTolerance &&
                 Math.Abs(dx2) < CoordinateTolerance &&
                 Math.Abs(dy1) < CoordinateTolerance &&
                 Math.Abs(dy2) < CoordinateTolerance &&
                 Math.Abs(dz1) < CoordinateTolerance &&
                 Math.Abs(dz2) < CoordinateTolerance)
            { return LineChangeType.Equal;}
            if
                (Math.Abs(dx1 - dx2) < CoordinateTolerance &&
                 Math.Abs(dy1 - dy2) < CoordinateTolerance &&
                 Math.Abs(dz1 - dz2) < CoordinateTolerance)
            { return LineChangeType.Move;}
            return LineChangeType.Drag;
        }

        // 判别直线对象的变化，返回LineChangeInfo（type已确定，已去重）或null（没变化）
        // 前置条件：obj已经确认是有效直线对象
    }
}
