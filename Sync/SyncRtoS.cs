using CSiAPIv1;
using Rhino;
using Rhino.Collections;
using Rhino.DocObjects;
using Rhino.UI;
using RhinoToSAP.Data;
using RhinoToSAP.Tools;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RhinoToSAP.Sync
{
    // 待处理变化队列及其处理方法
    public static class SyncRtoS
    {
        //FrameSection定义
        public static string FrameSection { get; set; } = "None";
        /// <summary>
        /// 直线待处理列表
        /// </summary>
        //新增（Dictionary：Key=Rhino Guid，Value=LineState，自动去重）
        public static Dictionary<Guid, LineState> _LineAdd = new Dictionary<Guid, LineState>();
        //移动
        public static Dictionary<Guid, LineState> _LineMove = new Dictionary<Guid, LineState>();
        //拖拽
        public static Dictionary<Guid, LineState> _LineDrag = new Dictionary<Guid, LineState>();
        //图层
        public static Dictionary<Guid, LineState> _LineLayer = new Dictionary<Guid, LineState>();
        //删除
        public static List<Guid> _LineDelete = new List<Guid>();

        /// <summary>
        /// 网格待处理列表
        /// </summary>
        //新增（Dictionary：Key=Rhino Guid，Value=MeshState，自动去重）
        public static Dictionary<Guid, MeshState> _MeshAdd = new Dictionary<Guid, MeshState>();
        //移动
        public static Dictionary<Guid, MeshState> _MeshMove = new Dictionary<Guid, MeshState>();
        //拖拽
        public static Dictionary<Guid, MeshState> _MeshDrag = new Dictionary<Guid, MeshState>();
        //图层
        public static Dictionary<Guid, MeshState> _MeshLayer = new Dictionary<Guid, MeshState>();
        //删除
        public static List<Guid> _MeshDelete = new List<Guid>();


        //总调度方法
        public static bool RhinoToSap(RhinoDoc doc, string rootLayerName, out string message)
        {
            message = string.Empty;
            if (!SyncEngine.CheckReady(out string errorMsg))
            {
                message = errorMsg;
                return false;
            }
            try
            {
                CollectChanges(doc, rootLayerName);
                return ExecuteChanges(out message);
            }
            catch (Exception ex)
            {
                message = $"Rhino→SAP同步失败：{ex.Message}";
                ClearAllChanges();
                return false;
            }
        }


        // 只收集变化，不执行（用于加载映射文件后判断有没有变化）
        public static void CollectChanges(RhinoDoc doc, string rootLayerName)
        {
            //1收集 Rhino 当前所有有效对象
            List<RhinoObject> RhinoObjs = LayerHelper.GetValidObjectsInLayer(doc, rootLayerName);
            SyncLayers(doc, rootLayerName);
            //2对比历史状态表，生成 8 张待处理列表
            foreach (RhinoObject obj in RhinoObjs) { RhinoGeoClassfy(obj); }
            RhinoGeoDeleted(RhinoObjs);
        }
        // 执行待处理的变化（执行、保存、清空）
        public static bool ExecuteChanges(out string message)
        {
            message = string.Empty;
            try
            {
                // 1. 按顺序执行所有变化
                ApplyAllChanges();

                // 2. 保存历史状态表和映射表到json文件
                SyncPersistenceIO.SaveMapping();

                // 3. 清空待处理列表
                ClearAllChanges();

                message = "同步成功";
                return true;
            }
            catch (Exception ex)
            {
                message = $"同步失败：{ex.Message}";
                ClearAllChanges();  // 失败也要清空
                return false;
            }
        }


        //==================辅助方法=====================
        //Rhino几何分类1，输出Line/Mesh两大类add/move/drag/layer的8张待处理列表
        public static void RhinoGeoClassfy(RhinoObject obj)
        {
            // 基础过滤：对象为空或不可见，直接无效
            if (obj == null) return;
            if (!obj.Visible) return;

            //类型判断：是不是有效直线
            if (LineHelper.IsValidLineObject(obj))
            {
                LineState currentState = LineHelper.ToLineState(obj);
                if (currentState == null) return;
                //查询历史记录是否存在
                if (!SyncStateManager.TryGetHistory<LineState>(currentState.RhinoLineId, out LineState oldState))
                {
                    _LineAdd[currentState.RhinoLineId] = currentState;// 查不到：说明是新对象
                    return; // 新增对象不需要再判断Move/Drag/Layer，同时避免oldState为null的空引用
                }
                //查到了：第1步：判断图层是否发生变化
                if (oldState.LayerName != currentState.LayerName)
                {
                    _LineLayer[currentState.RhinoLineId] = currentState; // 图层变了：加到图层待变更列表
                }
                //查到了：第2步：判断几何变化类型
                switch( LineHelper.GetLineChangeType(currentState, oldState))
                {
                    case LineChangeType.Equal:
                        break;
                    case LineChangeType.Move:
                        _LineMove[currentState.RhinoLineId] = currentState;
                        break;
                    case LineChangeType.Drag:
                        _LineDrag[currentState.RhinoLineId] = currentState;
                        break;
                }
            }

            //4. 类型判断：是不是有效网格，待添加
            return;
        }
        //Rhino几何分类2，输出Line/Mesh两大类Delete的2张待处理列表
        public static void RhinoGeoDeleted(List<RhinoObject> Objs)
        {
            // 1. 收集当前对象ID集合（HashSet，Contains是O(1)）
            HashSet<Guid> RhinoIds = new HashSet<Guid>();
            foreach (RhinoObject obj in Objs)
            {
                RhinoIds.Add(obj.Id);
            }

            // 2. 只遍历一次历史状态表
            foreach (var kvp in SyncStateManager.GetAllHistoryEntries())
            {
                // 3. 先看Key，没删除就跳过（不碰Value）
                if (RhinoIds.Contains(kvp.Key)) continue;

                // 4. 被删除了，才访问Value判断类型
                if (kvp.Value is LineState)
                {
                    _LineDelete.Add(kvp.Key);
                }
                else if (kvp.Value is MeshState)
                {
                    _MeshDelete.Add(kvp.Key);
                }
            }
        }


        // ========== Line变化操作 ==========
        // 通过Sap Api执行frame添加操作
        public static void ApplyLineAdd(Dictionary<Guid, LineState> Dic)
        {
            foreach (var kvp in Dic)
            {
                LineState state = kvp.Value;
                Guid rhinoId = kvp.Key;

                // 1. 调用SAP API创建杆件
                string frameId = string.Empty;
                int ret = SAPConnector.SapModel.FrameObj.AddByCoord(
                    state.X1, state.Y1, state.Z1,
                    state.X2, state.Y2, state.Z2,
                    ref frameId,
                    FrameSection,
                    "",
                    "Global");
                if (ret != 0 || string.IsNullOrEmpty(frameId)) continue;

                // 2. 设置分组
                if (!string.IsNullOrEmpty(state.LayerName))
                {
                    SAPConnector.SapModel.FrameObj.SetGroupAssign(
                        frameId, state.LayerName, false, eItemType.Objects);
                }

                // 3. 更新映射表和历史状态表
                SyncStateManager.AddMapping(rhinoId, frameId);
                SyncStateManager.AddHistory(rhinoId, state);
            }
        }

        // 通过Sap Api执行frame移动操作
        public static void ApplyLineMove(Dictionary<Guid, LineState> Dic)
        {
            foreach (var kvp in Dic)
            {
                LineState newState = kvp.Value;
                Guid rhinoId = kvp.Key;

                // 1. 查映射表拿SAP ID
                if (!SyncStateManager.TryGetMapping(rhinoId, out string sapId)) continue;

                // 2. 查历史状态拿旧坐标，计算偏移量
                if (!SyncStateManager.TryGetHistory<LineState>(rhinoId, out LineState oldState)) continue;

                double dx = newState.X1 - oldState.X1;
                double dy = newState.Y1 - oldState.Y1;
                double dz = newState.Z1 - oldState.Z1;

                try
                {
                    // 3. 选中杆件 → 整体移动 → 取消选中
                    SAPConnector.SapModel.FrameObj.SetSelected(sapId, true);
                    int ret = SAPConnector.SapModel.EditGeneral.Move(dx, dy, dz);
                    SAPConnector.SapModel.FrameObj.SetSelected(sapId, false);

                    // 4. 成功则更新历史状态
                    if (ret == 0)
                    {
                        SyncStateManager.AddHistory(rhinoId, newState);
                    }
                }
                catch
                {
                }
            }
        }

        // 通过Sap Api执行frame拖拽操作
        public static void ApplyLineDrag(Dictionary<Guid, LineState> Dic)
        {
            // 移动偏移量：把杆件先移动到很远的地方，让节点脱离共用
            const double moveOffset = 10000000;

            foreach (var kvp in Dic)
            {
                LineState newState = kvp.Value;
                Guid rhinoId = kvp.Key;

                // 1. 查映射表拿SAP ID
                if (!SyncStateManager.TryGetMapping(rhinoId, out string sapId)) continue;

                try
                {
                    // 2. 选中杆件 → 移动到远处 → 取消选中
                    // 目的：让杆件两端节点脱离和其他杆件的共用，避免改节点影响其他杆件
                    SAPConnector.SapModel.FrameObj.SetSelected(sapId, true);
                    int retMove = SAPConnector.SapModel.EditGeneral.Move(moveOffset, moveOffset, moveOffset);
                    SAPConnector.SapModel.FrameObj.SetSelected(sapId, false);
                    if (retMove != 0) continue;

                    // 3. 取杆件两端的节点ID
                    string point1 = "";
                    string point2 = "";
                    int retGetPoints = SAPConnector.SapModel.FrameObj.GetPoints(sapId, ref point1, ref point2);
                    if (retGetPoints != 0) continue;

                    // 4. 把两个节点坐标改成目标坐标
                    int retPoint1 = SAPConnector.SapModel.EditPoint.ChangeCoordinates_1(
                        point1, newState.X1, newState.Y1, newState.Z1);
                    int retPoint2 = SAPConnector.SapModel.EditPoint.ChangeCoordinates_1(
                        point2, newState.X2, newState.Y2, newState.Z2);

                    // 5. 成功则更新历史状态
                    if (retPoint1 == 0 && retPoint2 == 0)
                    {
                        SyncStateManager.AddHistory(rhinoId, newState);
                    }
                }
                catch
                {
                }
            }
        }

        // 通过Sap Api执行frame图层调整操作
        public static void ApplyLineLayer(Dictionary<Guid, LineState> Dic)
        {
            foreach (var kvp in Dic)
            {
                LineState newState = kvp.Value;
                Guid rhinoId = kvp.Key;

                // 1. 查映射表拿SAP ID
                if (!SyncStateManager.TryGetMapping(rhinoId, out string sapId)) continue;

                // 2. 图层名为空则跳过
                if (string.IsNullOrEmpty(newState.LayerName)) continue;

                try
                {
                    // 3. 把杆件分配到新的分组（分组已经在EnsureAllGroupsExist里确保存在了，直接分配就行）
                    int ret = SAPConnector.SapModel.FrameObj.SetGroupAssign(
                        sapId, newState.LayerName, false, eItemType.Objects);

                    // 4. 成功则更新历史状态（记录新的图层名，下次对比用）
                    if (ret == 0)
                    {
                        SyncStateManager.AddHistory(rhinoId, newState);
                    }
                }
                catch
                {
                }
            }
        }

        // 通过Sap Api执行frame删除操作
        public static void ApplyLineDelete(List<Guid> Lists)
        {
            foreach (Guid rhinoId in Lists)
            {
                // 1. 查映射表拿SAP ID
                if (!SyncStateManager.TryGetMapping(rhinoId, out string sapId)) continue;

                try
                {
                    // 2. 调用SAP API删除杆件
                    int ret = SAPConnector.SapModel.FrameObj.Delete(sapId);

                    // 3. 成功则移除映射表和历史状态表
                    if (ret == 0)
                    {
                        SyncStateManager.RemoveMapping(rhinoId);
                        SyncStateManager.RemoveHistory(rhinoId);
                    }
                }
                catch
                {
                }
            }
        }


        // ========== Mesh变化操作 ==========
        // 通过Sap Api执行Area添加操作
        public static void ApplyMeshAdd(Dictionary<Guid, MeshState> Dic)
        {
            //待完善
        }

        // 通过Sap Api执行Area移动操作
        public static void ApplyMeshMove(Dictionary<Guid, MeshState> Dic)
        {
            //待完善
        }

        // 通过Sap Api执行Area拖拽操作
        public static void ApplyMeshDrag(Dictionary<Guid, MeshState> Dic)
        {
            //待完善
        }

        // 通过Sap Api执行Area图层调整操作
        public static void ApplyMeshLayer(Dictionary<Guid, MeshState> Dic)
        {
            //待完善
        }

        // 通过Sap Api执行Area删除操作
        public static void ApplyMeshDelete(List<Guid> Lists)
        {
            //待完善
        }





        //执行待处理变化
        public static void ApplyAllChanges()
        {
            ApplyLineAdd(_LineAdd);
            ApplyLineMove(_LineMove);
            ApplyLineDrag(_LineDrag);
            ApplyLineLayer(_LineLayer);
            ApplyLineDelete(_LineDelete);
            ApplyMeshAdd(_MeshAdd);
            ApplyMeshMove(_MeshMove);
            ApplyMeshDrag(_MeshDrag);
            ApplyMeshLayer(_MeshLayer);
            ApplyMeshDelete(_MeshDelete);
        }

        
        // 清空所有待处理变化（处理完成后调用）
        public static void ClearAllChanges()
        {
            _LineAdd.Clear();
            _LineDelete.Clear();
            _LineDrag.Clear();
            _LineMove.Clear();
            _LineLayer.Clear();
            _MeshAdd.Clear();
            _MeshDelete.Clear();
            _MeshDrag.Clear();
            _MeshMove.Clear();
            _MeshLayer.Clear();
        }

        // 是否有待处理的变化
        public static bool HasChanges(out int changes)
        {
            changes = _LineAdd.Count+_LineDelete.Count+_LineDrag.Count+_LineLayer.Count+_LineMove.Count+
                        _MeshAdd.Count + _MeshDelete.Count + _MeshDrag.Count + _MeshLayer.Count + _MeshMove.Count ;
            if (changes == 0) return false;
            else return true;
        }

        // 同步图层
        public static void SyncLayers(RhinoDoc doc, string rootLayerName)
        {
            // 1. 收集所有不重复的图层名（HashSet自动去重）
            Layer rootLayer = doc.Layers.FindName(rootLayerName);
            List<Layer> allLayers = new List<Layer>();
            LayerHelper.GetAllChildLayers(rootLayer, allLayers);

            // 2. 一次性确保所有分组存在
            foreach (Layer _ in allLayers)
            {
                SAPConnector.SapModel.GroupDef.SetGroup(_.FullPath);
            }
        }

    }
}
