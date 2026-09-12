using Rhino;
using Rhino.Collections;
using Rhino.DocObjects;
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
        /// <summary>
        /// 直线待处理列表
        /// </summary>
        //新增
        private static List<Guid> _LineAdd= new List<Guid>();
        //移动
        private static List<Guid> _LineMove = new List<Guid>();
        //拖拽
        private static List<Guid> _LineDrag = new List<Guid>();
        //删除
        private static List<Guid> _LineDelete = new List<Guid>();
        //图层
        private static List<Guid> _LineLayer = new List<Guid>();

        /// <summary>
        /// 网格待处理列表
        /// </summary>
        //新增
        private static List<Guid> _MeshAdd = new List<Guid>();
        //移动
        private static List<Guid> _MeshMove = new List<Guid>();
        //拖拽
        private static List<Guid> _MeshDrag = new List<Guid>();
        //删除
        private static List<Guid> _MeshDelete = new List<Guid>();
        //图层
        private static List<Guid> _MeshLayer = new List<Guid>();

        //总调度方法
        public static bool RhinoToSap(RhinoDoc doc, string rootLayerName, out string message)
        {
            message = string.Empty;
            if (!SyncEngine.CheckReady(out string errorMsg))
            {
                message = errorMsg;
                return false;
            }
            
            //1收集 Rhino 当前所有有效对象
            List<RhinoObject> RhinoObjs=LayerHelper.GetValidObjectsInLayer(doc, rootLayerName);
            //2对比历史状态表，生成 8 张待处理列表
            foreach (RhinoObject obj in RhinoObjs){RhinoGeoClassfy(obj);}
            //3按顺序执行所有变化
            ApplyAllChanges();
            //5保存历史状态表和映射表
            SyncPersistenceIO.SaveMapping();
            //6返回成功 / 失败
            return false;
        }

        //Rhino几何分类1
        private static void RhinoGeoClassfy(RhinoObject obj)
        {
            // 基础过滤：对象为空或不可见，直接无效
            if (obj == null) return;
            if (!obj.Visible) return;

            //类型判断：是不是有效直线
            if (LineHelper.IsValidLineObject(obj))
            {
                LineState currentState = LineHelper.ToLineState(obj);
                //查询历史记录是否存在
                if (!SyncStateManager.TryGetHistory<LineState>(currentState.RhinoLineId, out LineState oldState))
                {
                    _LineAdd.Add(currentState.RhinoLineId);// 查不到：说明是新对象，加到待创建列表
                }
                //查到了：第1步：判断图层是否发生变化
                if (oldState.LayerName != currentState.LayerName)
                {
                    _LineLayer.Add(currentState.RhinoLineId); // 图层变了：加到图层待变更列表
                }
                //查到了：第2步：判断几何变化类型
                switch( LineHelper.GetLineChangeType(currentState, oldState))
                {
                    case LineChangeType.Equal:
                        break;
                    case LineChangeType.Move:
                        _LineMove.Add(currentState.RhinoLineId);
                        break;
                    case LineChangeType.Drag:
                        _LineDrag.Add(currentState.RhinoLineId);
                        break;
                }
            }

            //4. 类型判断：是不是有效网格，待添加
            return;
        }
        //Rhino几何分类2
        private static void RhinoGeoDeleted(RhinoObject obj)
        {
            //if 
        }


        // ========== Line变化操作 ==========
        // 通过Sap Api执行frame添加操作
        private static void ApplyLineAdd(List<Guid> Lists)
        {
            //待完善
        }

        // 通过Sap Api执行frame移动操作
        private static void ApplyLineMove(List<Guid> Lists)
        {
            //待完善
        }

        // 通过Sap Api执行frame拖拽操作
        private static void ApplyLineDrag(List<Guid> Lists)
        {
            //待完善
        }

        // 通过Sap Api执行frame图层调整操作
        private static void ApplyLineLayer(List<Guid> Lists)
        {
            //待完善
        }

        // 通过Sap Api执行frame删除操作
        private static void ApplyLineDelete(List<Guid> Lists)
        {
            //待完善
        }


        // ========== Mesh变化操作 ==========
        // 通过Sap Api执行Area添加操作
        private static void ApplyMeshAdd(List<Guid> Lists)
        {
            //待完善
        }

        // 通过Sap Api执行Area移动操作
        private static void ApplyMeshMove(List<Guid> Lists)
        {
            //待完善
        }

        // 通过Sap Api执行Area拖拽操作
        private static void ApplyMeshDrag(List<Guid> Lists)
        {
            //待完善
        }

        // 通过Sap Api执行Area图层调整操作
        private static void ApplyMeshLayer(List<Guid> Lists)
        {
            //待完善
        }

        // 通过Sap Api执行Area删除操作
        private static void ApplyMeshDelete(List<Guid> Lists)
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
        public static bool HasChanges()
        {
            int count = _LineAdd.Count+_LineDelete.Count+_LineDrag.Count+_LineLayer.Count+_LineMove.Count+
                        _MeshAdd.Count + _MeshDelete.Count + _MeshDrag.Count + _MeshLayer.Count + _MeshMove.Count ;
            if (count == 0) return false;
            else return true;
        }
    }
}
