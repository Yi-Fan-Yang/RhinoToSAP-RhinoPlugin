using CSiAPIv1;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoToSAP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RhinoToSAP.Sync
{
    public static class SyncStateManager
    {
        // ========== 私有字段 ==========
        //历史状态快照：Key=Rhino对象ID，Value=上次同步时的杆件状态,用来对比变化
        private static Dictionary<Guid, object> _historyStates = new Dictionary<Guid, object>();

        //映射表：Key=Rhino对象ID，Value=对应的SAP杆件ID,增量更新的核心，通过它找到Rhino对象对应的SAP杆件
        private static Dictionary<Guid, string> _idMapping = new Dictionary<Guid, string>();


        // ========== 公共属性 ==========
        //统计数据
        public static int HistoryStatesCount => _historyStates.Count;


        // ==========公共方法 ==========
        // 清空同步状态：历史快照、映射表、待处理列表。重新连接SAP、更换绑定图层时调用，避免用旧的映射关系
        public static void ClearState()
        {
            _historyStates.Clear();
            _idMapping.Clear();
        }

        // 根据Rhino对象ID获取历史状态,T是类型占位符，调用时填LineState/AreaState
        public static bool TryGetHistory<T>(Guid rhinoid,out T state) where T:class
        {
           if( _historyStates.TryGetValue(rhinoid, out object obj))
           {
                //把object转换成调用方指定的T类型（LineState/AreaState）
                state = obj as T ;
               return state != null;
            }
            // 字典里没有这个ID，返回null和false
            state = null;
            return false;
        }

        //添加/更新历史状态
        public static void AddHistory(Guid rhinoid, object state)
        {
            _historyStates[rhinoid] = state;
        }

        //删除历史状态
        public static void RemoveHistory(Guid rhinoid)
        {
            _historyStates.Remove(rhinoid);
        }

        //查询映射表
        public static bool TryGetMapping(Guid rhinoid, out string sapId)
        {
            return _idMapping.TryGetValue(rhinoid, out sapId);
        }
        
        //映射表增项
        public static void AddMapping(Guid rhinoid, string sapId)
        {
            _idMapping[rhinoid] = sapId;
        }
        
        //映射表减项
        public static void RemoveMapping(Guid rhinoid)
        {
            _idMapping.Remove(rhinoid);
        }

        //外部遍历映射表，返回值是遍历的权限，可以当映射表表用
        public static IEnumerable<KeyValuePair<Guid, string>> GetAllMappings()
        {
            return _idMapping;
        }

        // 获取所有LineState类型的历史状态，筛选掉其他类型（比如后面的AreaState）
        public static IEnumerable<LineState> GetAllLineStates()
        {
            foreach (var item in _historyStates.Values)
            {
                if (item is LineState lineState)
                {
                    yield return lineState;
                }
            }
        }
    }
}
