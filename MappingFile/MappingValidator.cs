using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using CSiAPIv1;
using RhinoToSAP.Data;
using RhinoToSAP.Tools;
using RhinoToSAP.Sync;

namespace RhinoToSAP.MappingFile
{
    /// <summary>
    /// 接续校验与增量对齐类：负责加载后的三层校验、Rhino/SAP两侧状态对比、生成待处理列表
    /// 只做校验和对比，不碰文件读写和同步执行
    /// </summary>
    public static class MappingValidator
    {
        // ========== 待处理列表容器 ==========
        // 待创建：Rhino里有、映射里没有的新对象
        public static List<LineState> PendingCreate = new List<LineState>();
        // 待更新：映射里有、坐标变了的对象
        public static List<LineState> PendingUpdate = new List<LineState>();
        // 待删除：映射里有、Rhino里已经删除的对象ID
        public static List<Guid> PendingDelete = new List<Guid>();
        // 待图层变更：Rhino里有、映射里有、但图层变了的对象
        public static List<LayerChangeInfo> PendingLayerChange = new List<LayerChangeInfo>();


        // ========== 公共方法：对外提供的校验入口 ==========

        /// <summary>
        /// 执行完整的接续校验和增量对齐
        /// </summary>
        /// <param name="errorMsg">校验失败时的错误信息</param>
        /// <returns>校验成功返回true，失败返回false</returns>
        public static bool Validate(out string errorMsg)
        {
            errorMsg = string.Empty;
            //清空待处理列表
            PendingCreate.Clear();
            PendingUpdate.Clear();
            PendingDelete.Clear();
            PendingLayerChange.Clear();

            //校验
            if (!ValidateSapModelMatch(out string modelerror))
            {
                errorMsg = modelerror;
                return false;
            }
            if (!ValidateDataIntegrity(out string dataerror))
            {
                errorMsg = dataerror;
                return false;
            }

            //4. 增量对齐：收集Rhino侧的变化
            CollectRhinoChanges();
            CollectDeletedObjects();
            ValidateSapMappingAlive();

            return true;
        }

        public static void ExecutePendingChanges()
        {
            // Frame单元
            foreach (LineState state in PendingCreate)
            {
                SyncFrame.AddFrame(state);
            }
            foreach (LineState state in PendingUpdate)
            {
                SyncFrame.UpdateFrame(state);
            }
            foreach (Guid rhinoId in PendingDelete)
            {
                SyncFrame.DeleteFrame(rhinoId);
            }
            //Area单元
            //图层变化
            foreach (LayerChangeInfo change in PendingLayerChange)
            {
                if (SyncStateManager.TryGetMapping(change.RhinoId, out string sapId))
                {
                    SyncSapGeneral.UpdateSapObjectGroup(sapId, change.OldLayerFullPath, change.NewLayerFullPath);
                }
            }
        }

        // ========== 私有辅助方法：校验 ==========

        /// <summary>
        /// 校验1：文件版本号兼容性
        /// </summary>
        private static bool ValidateSapModelMatch(out string errorMsg)
        {
            errorMsg = string.Empty;
            // 1. 拿到当前SAP模型名称
            string currentSapModelName = SAPConnector.SapModel.GetModelFilename(true);

            // 2. 比较加载的SAP模型名称和当前模型名称
            if (currentSapModelName != SyncPersistenceIO.LoadedSapModelName)
            {
                errorMsg = $"SAP模型不匹配，当前打开的是[{currentSapModelName}]，映射记录的是[{SyncPersistenceIO.LoadedSapModelName}]";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 校验2：映射数据完整性（映射表和历史状态是不是对应、有没有损坏）
        /// </summary>
        private static bool ValidateDataIntegrity(out string errorMsg)
        {
            errorMsg = string.Empty;
            // 检查历史状态是否存在
            foreach (var map in SyncStateManager.GetAllMappings())
            {
                Guid rhinoId = map.Key;
                if (!SyncStateManager.TryGetHistory<LineState>(rhinoId, out _))
                {
                    errorMsg = $"数据不完整：映射存在但缺少对应的历史状态（对象{rhinoId}）";
                    return false;
                }
            }
            // 检查历史状态的每个rhinoId，映射表中也存在
            foreach (LineState state in SyncStateManager.GetAllLineStates())
            {
                if (!SyncStateManager.TryGetMapping(state.RhinoLineId, out _))
                {
                    errorMsg = $"数据不完整：历史状态存在但缺少对应的映射（对象{state.RhinoLineId}）";
                    return false;
                }
            }
            return true;
        }


        // ========== 校验3：增量对齐 ==========

        /// <summary>
        /// Rhino侧遍历：对比当前Rhino对象和历史状态，生成待创建/待更新列表
        /// </summary>
        private static void CollectRhinoChanges()
        {
            // 1.拿到当前Rhino文档，为空直接返回
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;

            // 2. 找到目标根图层，找不到直接返回
            Layer rootLayer = doc.Layers.FindName(SAPConnector.RootLayerName);
            if (rootLayer == null) return;

            // 3. 递归拿到根图层下的所有子图层（用已经写好的LayerHelper方法）
            List<Layer> allLayers = new List<Layer>();
            LayerHelper.GetAllChildLayers(rootLayer, allLayers);

            // 4. 遍历所有图层里的所有对象
            foreach (Layer i in allLayers)
            {
                RhinoObject[] objects = doc.Objects.FindByLayer(i);
                if (objects == null) continue;

                foreach (RhinoObject obj in objects)
                {
                    // 5. 过滤：只保留有效直线（用已经写好的LineHelper方法）
                    if (!LineHelper.IsValidLineObject(obj)) continue;

                    // 6. 把当前直线转成LineState状态快照
                    LineState currentState = LineHelper.ToLineState(obj);

                    // 7. 查内存里的历史状态，看这个对象之前有没有同步过
                    if (!SyncStateManager.TryGetHistory<LineState>(currentState.RhinoLineId, out LineState oldState))
                    {
                        // 查不到：说明是新对象，加到待创建列表
                        PendingCreate.Add(currentState);
                        continue;
                    }
                    //8. 查到了：判断图层是否发生变化
                    if (oldState.LayerName != currentState.LayerName)
                    {
                        // 图层变了：加到待图层变更列表
                        PendingLayerChange.Add(new LayerChangeInfo
                        (
                            currentState.RhinoLineId,
                            oldState.LayerName,
                            currentState.LayerName
                        ));
                    }
                    //9. 对比坐标有没有变化，用已经写好的对比方法
                    if (!LineHelper.IsLineStateEqual(currentState, oldState))
                    {
                        // 坐标变了：加到待更新列表
                        PendingUpdate.Add(currentState);
                    }
                    // 坐标没变：什么都不用做，跳过
                }
            }
        }

        /// <summary>
        /// 删除检测：找出映射里有、Rhino里已经删除的对象，生成待删除列表
        /// </summary>
        private static void CollectDeletedObjects()
        {
            // 1. 拿到当前Rhino文档
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;

            // 2. 记录要删除的ID列表，遍历过程中不能直接修改映射表
            List<Guid> toDelete = new List<Guid>();

            //3. 遍历所有映射：Key是Rhino对象ID，Value是SAP ID
            foreach (LineState oldState in SyncStateManager.GetAllLineStates())
            {
                // 4. 检查Rhino里有没有这个对象
                RhinoObject obj = doc.Objects.Find(oldState.RhinoLineId);
                if (obj == null)
                {
                    // 5. Rhino里找不到：说明被删除了，加入待删除列表
                    toDelete.Add(oldState.RhinoLineId);
                }
            }
            //6. 把待删除列表加入全局待删除容器
            PendingDelete.AddRange(toDelete);
        }

        /// <summary>
        /// SAP侧有效性校验：检查映射里的SAP FrameID是否还存在，清理无效映射
        /// </summary>
        private static void ValidateSapMappingAlive()
        {
            // 检测链接
            if (!SAPConnector.IsConnected) return;
            //2. 调用GetNameList，取出SAP里所有Frame名称
            int count = 0;
            string[] frameNames = new string[0];
            int ret = SAPConnector.SapModel.FrameObj.GetNameList(ref count, ref frameNames);
            if (ret != 0) return; // 获取失败，直接返回
            //3. 遍历映射表，检查每个SAP FrameID是否还存在
            HashSet<string> aliveFrames = new HashSet<string>(frameNames);
            List<Guid> deadMappings = new List<Guid>();
            foreach (var map in SyncStateManager.GetAllMappings())
            {
                Guid rhinoID = map.Key;
                string sapFrameID = map.Value;

                if (!aliveFrames.Contains(sapFrameID))
                {
                    deadMappings.Add(rhinoID);
                }
            }
            //4. 将无效映射从状态管理器中移除
            foreach (Guid rhinoID in deadMappings)
            {
                SyncStateManager.RemoveMapping(rhinoID);
                SyncStateManager.RemoveHistory(rhinoID);
            }
        }



    }
}
