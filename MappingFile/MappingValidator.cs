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

        // ========== 公共方法：对外提供的校验入口 ==========

        /// <summary>
        /// 执行完整的接续校验和增量对齐
        /// </summary>
        /// <param name="errorMsg">校验失败时的错误信息</param>
        /// <returns>校验成功返回true，失败返回false</returns>
        public static bool Validate(out string errorMsg)
        {
            errorMsg = string.Empty;

            // 校验1：SAP模型匹配
            if (!ValidateSapModelMatch(out string modelError))
            {
                errorMsg = modelError;
                return false;
            }

            // 校验2：数据完整性
            if (!ValidateDataIntegrity(out string dataError))
            {
                errorMsg = dataError;
                return false;
            }

            // 校验3：SAP映射有效性（清理无效映射）
            ValidateSapMappingAlive();

            return true;
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
