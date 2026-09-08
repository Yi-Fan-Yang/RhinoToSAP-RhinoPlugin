using CSiAPIv1;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Data;
using RhinoToSAP.Tools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RhinoToSAP.Sync
{
    public static class SyncFrame
    {
        //是否正在处理炸开，避免重复触发
        public static bool isExploding = false;


        //FrameSection定义
        public static string FrameSection { get; set; } = string.Empty;

        // 新增杆件：在SAP创建杆件，记录映射和历史状态
        public static void AddFrame(LineState state)
        {
            if (state == null) return;
            if (string.IsNullOrEmpty(FrameSection)) return;

            string sapId = LineHelper.CreateFrame(state, FrameSection);
            if (string.IsNullOrEmpty(sapId)) return;

            SyncStateManager.AddMapping(state.RhinoLineId, sapId);
            SyncStateManager.AddHistory(state.RhinoLineId, state);
        }

        // 修改杆件：只更新SAP对应杆件的坐标，保留所有属性，更新历史状态
        public static void UpdateFrame(LineState newState)
        {
            if (newState == null) return;
            // 检查是否有历史状态，如果没有历史状态，说明是新增杆件，直接调用AddFrame
            if (!SyncStateManager.TryGetMapping(newState.RhinoLineId, out string sapId))
            {
                AddFrame(newState);
                return;
            }
            // 有历史状态，说明是修改杆件，更新SAP对应杆件的坐标，保留所有属性，更新历史状态
            bool success = LineHelper.UpdateFrame(newState, sapId);
            if (!success) return;
            SyncStateManager.AddHistory(newState.RhinoLineId, newState);
        }

        // 删除杆件：删除SAP对应杆件，移除映射和历史状态
        public static void DeleteFrame(Guid rhinoId)
        {
            if (SyncStateManager.TryGetMapping(rhinoId, out string sapId))
            {
                int ret = SAPConnector.SapModel.FrameObj.Delete(sapId);
                if (ret == 0)
                {
                    SyncStateManager.RemoveMapping(rhinoId);
                    SyncStateManager.RemoveHistory(rhinoId);
                }
            }
        }
        //炸开多段线并添加到待处理列表
        public static bool TryExplodePolylineAndQueue(RhinoDoc doc, RhinoObject obj)
        {
            // 1. 还没连接绑定图层，直接返回，不炸开任何多段线
            if (string.IsNullOrEmpty(SAPConnector.RootLayerName)) return false;
            // 2. 对象不在目标图层层级里，直接返回，不炸开
            if (!LayerHelper.IsObjectInLayerHierarchy(obj, SAPConnector.RootLayerName)) return false;
            //3.炸开多段线，获取炸开后的所有新对象ID
            isExploding = true;
            List<Guid> explodedIds = LineHelper.ExplodePolylineCurve(doc, obj);
            if (explodedIds == null || explodedIds.Count == 0)
            {
                isExploding = false;
                return false;
            }
            // 4.把所有新生成的对象ID加入待处理列表
            foreach (Guid i in explodedIds)
            {
                SyncEngine.pendingChanges.Add(i);
            }
            isExploding = false;
            return true;
        }
    }
}
