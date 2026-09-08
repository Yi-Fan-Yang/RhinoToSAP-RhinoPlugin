using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoToSAP.Tools
{
    public static class LayerHelper
    {
        //rhino图层锁定
        public static bool LockLayer(RhinoDoc doc, string layerName)
        {
            Layer layer = doc.Layers.FindName(layerName);
            if (layer == null)
            {
                return false;
            }
            
            if(IsLayerInHierarchy(layer, layerName))
            {
                Layer templayer = doc.Layers.FindName("RhinoToSAP_Temp");
                if(templayer == null)
                {
                    templayer = new Layer();
                    templayer.Name = "RhinoToSAP_Temp";
                    int tempindex = doc.Layers.Add(templayer);
                    doc.Layers.SetCurrentLayerIndex(tempindex, true);
                }
                else
                {
                    doc.Layers.SetCurrentLayerIndex(templayer.Index, true);
                }
            }
            
            layer.IsLocked = true;
            doc.Layers.Modify(layer, layer.Index, true);
            return true;
        }

        //rhino图层解锁
        public static bool UnLockLayer(RhinoDoc doc, string layerName)
        {
            Layer layer = doc.Layers.FindName(layerName);
            if (layer == null)
            {
                return false;
            }
            else
            {
                layer.IsLocked = false;
                doc.Layers.Modify(layer, layer.Index, true);
                return true;
            }
        }
        //图层递归函数
        public static void GetAllChildLayers(Layer parentLayer, List<Layer> allLayers)
        {
            // 保护：父图层为空时直接返回
            if (parentLayer == null)
                return;

            allLayers.Add(parentLayer);

            // 防御性编程：GetChildren 可能返回 null
            var children = parentLayer.GetChildren();
            if (children == null)
                return;

            foreach (Layer child in children)
            {
                if (child == null) continue;
                GetAllChildLayers(child, allLayers); // 递归调用
            }
        }

        //图层过滤函数
        public static bool IsLayerInHierarchy(Layer layer, string rootLayerName)
        {
            if (string.IsNullOrEmpty(rootLayerName)) return false;
            if (layer == null) return false;
            if (layer.FullPath.StartsWith(rootLayerName+"::")|| layer.FullPath == rootLayerName) return true;
            return false;
        }
        public static bool IsObjectInLayerHierarchy(RhinoObject obj, string rootLayerName)
        {
            if (string.IsNullOrEmpty(rootLayerName)) return false;
            if (obj == null) return false;
            Layer layer = obj.Document.Layers[obj.Attributes.LayerIndex];
            return IsLayerInHierarchy(layer, rootLayerName);
        }
        
        // 获取根图层及其子图层下的所有有效对象
        public static List<RhinoObject> GetValidObjectsInLayer(RhinoDoc doc, string rootLayerName)
        {
            List<RhinoObject> result = new List<RhinoObject>();
            if (doc == null || string.IsNullOrEmpty(rootLayerName)) return result;

            Layer rootlayer = doc.Layers.FindName(rootLayerName);
            if (rootlayer == null) return result;

            List<Layer> allLayers = new List<Layer>();
            GetAllChildLayers(rootlayer, allLayers);

            foreach (Layer i in allLayers)
            {
                RhinoObject[] objects = doc.Objects.FindByLayer(i);
                if (objects == null) continue;
                foreach (RhinoObject obj in objects)
                {
                    if (LineHelper.IsValidLineObject(obj)) result.Add(obj);
                }
            }
            return result;
        }




    }
}
