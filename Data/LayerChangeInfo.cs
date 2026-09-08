using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RhinoToSAP.Data
{
    public class LayerChangeInfo
    {
        public Guid RhinoId { get; set; }
        public string OldLayerFullPath { get; set; }
        public string NewLayerFullPath { get; set; }
        public LayerChangeInfo(Guid rhinoId, string oldLayer, string newLayer) 
        {
            RhinoId = rhinoId;
            OldLayerFullPath = oldLayer;
            NewLayerFullPath = newLayer;
        }
    }
}
