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
    public static class SyncRhinoDispatcher
    {
        // ----- 事件处理方法：Rhino对象增删改时触发 -----

        // 添加待处理列表方法
        public static void AddToPendingIfValid(RhinoObject obj)
        {
            if (obj == null) return;
            if (!LineHelper.IsValidLineObject(obj))
            {
                RhinoApp.WriteLine($"[过滤] 对象{obj.Id}不是有效直线");
                return;
            }
            if (!LayerHelper.IsObjectInLayerHierarchy(obj, SAPConnector.RootLayerName))
            {
                RhinoApp.WriteLine($"[过滤] 对象{obj.Id}不在目标图层层级，当前图层：{obj.Document.Layers[obj.Attributes.LayerIndex].FullPath}");
                return;
            }
            SyncEngine.pendingChanges.Add(obj.Id);
        }
        
        //Rhino对象添加事件处理:过滤后加到待处理列表
        public static void OnObjectAdded(object sender, RhinoObjectEventArgs e)
        {
            if (SyncFrame.isExploding) return;
            try
            {
                AddToPendingIfValid(e.TheObject);
            }
            catch
            {
            }
        }

        // Rhino对象删除事件处理：直接加到待处理列表
        public static void OnObjectDeleted(object sender, RhinoObjectEventArgs e)
        {
            if (SyncFrame.isExploding) return;
            try
            {
                // 对象已经被删了，拿不到几何和图层，直接加ID到待处理列表
                // 后面处理的时候会判断这个ID有没有对应的SAP杆件，有就删，没有就忽略
                SyncEngine.pendingChanges.Add(e.ObjectId);
            }
            catch
            {
            }
        }

        // Rhino对象修改事件处理：过滤后加到待处理列表
        public static void OnObjectReplaced(object sender, EventArgs e)
        {
            if (SyncFrame.isExploding) return;
            try
            {
                // 拿到当前活动文档，为空就返回
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null) return;

                // 递归获取目标图层的所有子图层
                Layer rootLayer = doc.Layers.FindName(SAPConnector.RootLayerName);
                if (rootLayer == null) return;
                List<Layer> layers = new List<Layer>();
                LayerHelper.GetAllChildLayers(rootLayer, layers);

                // 遍历所有图层，把所有对象的Guid都加到待处理列表
                foreach (Layer layer in layers)
                {
                    RhinoObject[] objs = doc.Objects.FindByLayer(layer);
                    if (objs == null) continue;
                    foreach (RhinoObject obj in objs)
                    {
                        if (obj == null) continue;
                        SyncEngine.pendingChanges.Add(obj.Id); // HashSet自动去重
                    }
                }
            }
            catch
            {
            }
        }

        //Rhino对象属性修改事件，如layer等
        public static void OnObjectAttributesModified(object sender, RhinoModifyObjectAttributesEventArgs e)
        {
            if (SyncFrame.isExploding) return;
            try
            {
                AddToPendingIfValid(e.RhinoObject);
            }
            catch
            {
            }
        }

        // Rhino关闭时触发：弹窗问是否保存映射文件，保存或放弃后自动断开
        public static void OnRhinoClosing(object sender, EventArgs e)
        {
            if (!SyncEngine.IsMappingLoaded) return;
            {
                try
                {
                    SAPConnector.Disconnect();
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"[OnRhinoClosing] 异常：{ex.Message}");
                }
            }
        }


        // 处理单个Frame对象的变化：判断是新增、修改还是删除，调用对应方法
        public static void ProcessSingleChange(RhinoDoc doc, Guid rhinoId)
        {
            RhinoObject obj = doc.Objects.FindId(rhinoId);
            if (obj == null)
            {
                SyncFrame.DeleteFrame(rhinoId);
                return;
            }

            if (!LineHelper.IsValidLineObject(obj)) return;

            bool inTargetLayer = LayerHelper.IsObjectInLayerHierarchy(obj, SAPConnector.RootLayerName);
            if (!inTargetLayer) //判断是否图层有修改
            {
                // 不在目标图层了，但是有对应的SAP对象，就删除
                if (SyncStateManager.TryGetMapping(rhinoId, out string oldSapId))
                {
                    LineHelper.DeleteFrame(oldSapId); // 后续加Area这里也要加类型判断
                    SyncStateManager.RemoveMapping(rhinoId);
                    SyncStateManager.RemoveHistory(rhinoId);
                }
                return;
            }

            LineState currentState = LineHelper.ToLineState(obj);
            if (!SyncStateManager.TryGetHistory<LineState>(rhinoId, out LineState oldState))
            {
                SyncFrame.AddFrame(currentState);
                return;
            }
            //对比图层变化并应用修改
            if (oldState.LayerName != currentState.LayerName)
            {
                if (SyncStateManager.TryGetMapping(rhinoId, out string sapId))
                {
                    SyncSapGeneral.UpdateSapObjectGroup(sapId, oldState.LayerName, currentState.LayerName);
                }
                // 更新状态里的图层名（即使几何没改也要更新）
                oldState.LayerName = currentState.LayerName;
                SyncStateManager.AddHistory(rhinoId, oldState); 
            }

            if (!LineHelper.IsLineStateEqual(currentState, oldState))
            {
                SyncFrame.UpdateFrame(currentState);
            }
        }
    }
}
