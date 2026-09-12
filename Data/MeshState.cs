using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RhinoToSAP.Data
{
    public class MeshState
    {

    }
    public enum MeshChangeType
    {
        Add,
        Move,
        Drag,
        Delete,
        Layer
    }

    // 网格对象变化信息（预留，后面加网格时完善具体字段）
    public class MeshChangeInfo
    {
        public Guid RhinoId { get; set; }
        public MeshChangeType Type { get; set; }

        // 网格的当前状态和旧状态，后面加网格时再定义具体的MeshState类
        // 现在先用object占位，后面替换成具体的状态类
        public object CurrentState { get; set; }
        public object OldState { get; set; }
    }
}
