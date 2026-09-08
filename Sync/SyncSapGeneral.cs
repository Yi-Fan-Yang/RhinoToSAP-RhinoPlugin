using CSiAPIv1;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RhinoToSAP.Sync
{
    public static class SyncSapGeneral
    {
        //SAP中group修改方法
        public static bool UpdateSapObjectGroup(string sapObjectId, string oldGroup, string newGroup)
        {
            if (string.IsNullOrEmpty(sapObjectId)) return false;
            if (oldGroup == newGroup) return true; // 图层没变化，不用处理

            try
            {
                // 1. 从旧Group移除（第三个参数true表示移除）
                if (!string.IsNullOrEmpty(oldGroup))
                {
                    SAPConnector.SapModel.FrameObj.SetGroupAssign(sapObjectId, oldGroup, true);
                }
                // 2. 加入新Group（第三个参数false表示加入）
                if (!string.IsNullOrEmpty(newGroup))
                {
                    SAPConnector.SapModel.FrameObj.SetGroupAssign(sapObjectId, newGroup, false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
