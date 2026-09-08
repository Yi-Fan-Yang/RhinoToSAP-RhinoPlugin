using CSiAPIv1;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Data;
using System;
using System.Collections.Generic;

namespace RhinoToSAP.Tools
{
    public static class LineHelper
    {
        /// 坐标对比容差，和Rhino文档精度一致，误差在这个范围内不算修改
        private const double CoordinateTolerance = 1.0;

        // ----- 公共方法 -----
        //判断Geo是否是直线
        public static bool IsValidLineObject(RhinoObject obj)
        {
            return TryGetStrictLine(obj, out _);
        }

        //将RhinoObject转换为LineState
        public static LineState ToLineState(RhinoObject obj)
        {
            if (!TryGetStrictLine(obj, out Line line)) return null;
            Layer layer = obj.Document.Layers[obj.Attributes.LayerIndex];
            string layerName = layer.FullPath;
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
        public static bool IsLineStateEqual(LineState a, LineState b)
        {
            if
                    (Math.Abs(a.X1 - b.X1) < CoordinateTolerance &&
                    Math.Abs(a.Y1 - b.Y1) < CoordinateTolerance &&
                    Math.Abs(a.Z1 - b.Z1) < CoordinateTolerance &&
                    Math.Abs(a.X2 - b.X2) < CoordinateTolerance &&
                    Math.Abs(a.Y2 - b.Y2) < CoordinateTolerance &&
                    Math.Abs(a.Z2 - b.Z2) < CoordinateTolerance)
            {
                return true;
            }
            return false;
        }

        //严格提取直线对象
        private static bool TryGetStrictLine(RhinoObject obj, out Line line)
        {
            line = Line.Unset;
            if (obj == null) return false;
            if (!obj.Visible) return false;
            if (!(obj.Geometry is Curve curve)) return false;
            if (!curve.IsLinear(obj.Document.ModelAbsoluteTolerance)) return false;
            line = new Line(curve.PointAtStart,curve.PointAtEnd);
            return true;
        }

        //炸开多段线为单独的直线对象
        public static List<Guid> ExplodePolylineCurve(RhinoDoc doc, RhinoObject obj)
        {
            if(obj == null) return null;
            if(!obj.Visible) return null;
            if(!(obj.Geometry is PolylineCurve plc)) return null;
            // 通过ToPolyline方法获取多段线的顶点，然后生成Line对象
            Line[] lines = plc.ToPolyline().GetSegments();
            // 复制原对象的属性，以便在新创建的直线对象中使用
            ObjectAttributes attr = obj.Attributes.Duplicate();
            // 遍历线段并赋予属性，添加到文档中
            List<Guid> lineId = new List<Guid>();
            foreach (Line line in lines)
            {
                if (line.Length < doc.ModelAbsoluteTolerance) continue;
                Guid newId = doc.Objects.AddLine(line, attr);
                lineId.Add(newId);
            }
            // 删除原多段线对象
            doc.Objects.Delete(obj, true);
            return lineId;




        }
    }
}
