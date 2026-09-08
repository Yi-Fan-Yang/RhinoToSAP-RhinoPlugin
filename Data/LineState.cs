using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;


namespace RhinoToSAP.Data
{
    // 单根杆件的状态快照，用来对比几何变化、做增量更新
    // 存：Rhino对象唯一ID、起点终点坐标、所在图层名
    public class LineState
    {
        public Guid RhinoLineId { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double Z1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Z2 { get; set; }
        public string LayerName { get; set; }
        public LineState(Guid rhinoLineId, Line line, string layerName)
        {
            RhinoLineId = rhinoLineId;
            X1 = line.FromX;
            Y1 = line.FromY;
            Z1 = line.FromZ;
            X2 = line.ToX;
            Y2 = line.ToY;
            Z2 = line.ToZ;
            LayerName = layerName;
        }
        public LineState() { } // 反序列化需要无参构造函数
    }
}
