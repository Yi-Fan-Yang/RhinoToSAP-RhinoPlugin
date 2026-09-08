using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Rhino;
using Rhino.DocObjects;
using System.Web.Script.Serialization;
using RhinoToSAP.Data;
using System.Collections;


namespace RhinoToSAP.Sync
{
    // 映射文件持久化读写类：负责映射表和历史状态的磁盘读写、备份、文件校验
    // 只做文件IO，不碰任何业务逻辑
    public static class SyncPersistenceIO
    {
        // ========== 常量配置 ==========
        // 文件标识头，用来校验是不是有效的映射文件
        private const string FileIdentifier = "RhinoToSAP_Mapping_File";
        // 当前文件格式版本
        public const int FileVersion = 1;
        // 备份文件后缀
        private const string BackupSuffix = ".bak";
        // 记录本次加载的映射文件版本号
        public static int LoadedVersion { get; private set; } = 0;
        // 记录本次加载的映射文件的sap模型名称
        public static string LoadedSapModelName { get; private set; } = string.Empty;
        // 当前加载/新建的映射文件完整路径
        public static string CurrentMappingFilePath = string.Empty;


        // ========== 公共方法：对外提供的读写接口 ==========

        /// 把当前内存里的映射表和历史状态保存到磁盘文件
        public static bool SaveMapping()
        {
            try
            {
                // 1. 检查是否有已加载的映射文件
                if (string.IsNullOrEmpty(CurrentMappingFilePath))
                {
                    RhinoApp.WriteLine("没有已加载的映射文件，请先加载或新建");
                    return false;
                }
                string filepath = CurrentMappingFilePath;
                // 2. 备份旧文件（方法内部已经做了异常处理，备份失败不影响这里）
                BackupOldFile(filepath);

                // 3. 把内存数据序列化成JSON字符串
                string json = SerializeMapping();

                //4. 把JSON字符串写到文件，自动覆盖旧文件
                File.WriteAllText(filepath, json, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("保存映射表时发生错误" + ex.Message);
                return false;
            }
        }

        // 从磁盘文件读取映射表和历史状态到内存
        public static bool LoadMapping(string filePath, out string errorMsg)
        {
            errorMsg = string.Empty;
            try
            {
                // 1. 检查传入的路径是否有效
                if (string.IsNullOrEmpty(filePath))
                {
                    RhinoApp.WriteLine("映射文件路径为空");
                    errorMsg = "映射文件路径为空";
                    return false;
                }

                // 2. 文件不存在
                if (!File.Exists(filePath))
                {
                    RhinoApp.WriteLine($"未找到映射文件：{filePath}");
                    errorMsg = $"未找到映射文件：{filePath}";
                    return false;
                }

                // 3. 读取文件内容，UTF8编码保证中文不乱码
                string jsonText = File.ReadAllText(filePath, Encoding.UTF8);


                // 4. 反序列化到内存
                if (!DeserializeMapping(jsonText, out string deserializeError))
                {
                    errorMsg = deserializeError;
                    return false;
                }

                RhinoApp.WriteLine($"[加载映射] 成功：{filePath}");
                CurrentMappingFilePath = filePath; // 记录当前加载的映射文件路径
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = $"加载映射文件失败：{ex.Message}";
                SyncStateManager.ClearState(); // 失败清空已加载的部分数据
                return false;
            }
        }

        //卸载当前映射文件，清空所有状态，回到未加载状态（加载失败/用户选否时回滚用）
        public static void UnLoadMapping()
        {
            CurrentMappingFilePath = string.Empty;
            LoadedVersion = 0;
            LoadedSapModelName = string.Empty;
            SyncStateManager.ClearState();
        }

        //连接成功创建空映射
        public static bool CreateEmptyMapping(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                string jsonText = SerializeMapping();
                File.WriteAllText(filePath, jsonText, Encoding.UTF8);
                CurrentMappingFilePath = filePath; // 记录当前创建的映射文件路径
                return true;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"创建映射文件失败：{ex.Message}");
                return false;
            }
        }

        // 另存为：把当前映射数据保存到新文件，覆盖已存在的文件，成功后切换当前路径
        public static bool SaveMappingAs(string newFilePath)
        {
            if (string.IsNullOrEmpty(newFilePath)) return false;
            try
            {
                BackupOldFile(newFilePath); // 备份旧文件
                string json = SerializeMapping();
                File.WriteAllText(newFilePath, json, Encoding.UTF8);
                CurrentMappingFilePath = newFilePath; // 记录当前创建的映射文件路径
                return true;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"另存为映射文件失败：{ex.Message}");
                return false;
            }
        }

        // ========== 私有辅助方法 ==========


        // 备份上一版本的映射文件
        private static void BackupOldFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;// 文件不存在，不需要备份
                {
                    string backupPath = filePath + BackupSuffix;
                    File.Copy(filePath, backupPath, true);
                }
            }
            catch
            {
                // 备份失败不影响主流程
            }
        }

        // 校验文件头和版本号
        private static bool ValidateFileHeader(Dictionary<string, object> root, out string errorMsg)
        {
            errorMsg = string.Empty;
            //校验文件标识：必须是我们生成的文件
            if (!root.ContainsKey("identifier") || root["identifier"].ToString() != FileIdentifier)
            {
                errorMsg = "无效的映射文件：文件标识不匹配，不是本插件生成的映射文件";
                return false;
            }

            //校验版本号
            if (!root.ContainsKey("version") || Convert.ToInt32(root["version"]) != FileVersion)
            {
                errorMsg = $"映射文件版本不兼容：文件版本是{root["version"]}，当前插件只支持v{FileVersion}";
                return false;
            }
            return true;
        }

        //序列化方法
        private static string SerializeMapping()
        {
            //构件外围大字典，所有数据储存在这里
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["identifier"] = FileIdentifier;
            root["version"] = FileVersion;
            root["saving time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            //Sap文件名记录
            string sapModelName = string.Empty;
            try
            {
                if (SAPConnector.IsConnected)
                {
                    sapModelName = SAPConnector.SapModel.GetModelFilename(true);
                }
            }
            catch
            {
                sapModelName = SAPConnector.LastSapModelName;// SAP已关闭，实时获取失败，用最后一次记录的名称
            }
            if (string.IsNullOrEmpty(sapModelName)) sapModelName = SAPConnector.LastSapModelName;
            root["sapmodelname"] = sapModelName;

            //rhino-sap映射表数组，遍历所有映射，转成字典列表
            List<Dictionary<string, string>> mappings = new List<Dictionary<string, string>>();
                foreach (var map in SyncStateManager.GetAllMappings())
                {
                    Dictionary<string, string> item = new Dictionary<string, string>();
                    item["RhinoID"] = map.Key.ToString();// Guid必须转成字符串才能存JSON
                    item["SAPID"] = map.Value;
                    mappings.Add(item);
                }
            root["mappings"] = mappings;

            //历史状态数组，遍历所有LineState，转成字典列表
            List<Dictionary<string, object>> States = new List<Dictionary<string, object>>();
                foreach (var state in SyncStateManager.GetAllLineStates())
                {
                Dictionary<string, object> item = new Dictionary<string, object>();
                    item["RhinoID"] = state.RhinoLineId.ToString();
                    item["x1"] = state.X1;
                    item["y1"] = state.Y1;
                    item["z1"] = state.Z1;
                    item["x2"] = state.X2;
                    item["y2"] = state.Y2;
                    item["z2"] = state.Z2;
                    item["layername"] = state.LayerName;
                    States.Add(item);
                }
            root["states"] = States;

            //用.NET自带的序列化器转成JSON字符串
            return new JavaScriptSerializer().Serialize(root);
        }

        //反序列化方法
        private static bool DeserializeMapping(string jsonText,out string errorMsg)
        {
            errorMsg = string.Empty;
            try
            {
                // 1. 反序列化前先清空旧状态，避免新旧数据混在一起
                SyncStateManager.ClearState();

                // 2. 把JSON字符串反序列化成最外层字典
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(jsonText);

                // 1.文件头校验
                if (!ValidateFileHeader(root,out string validateError))
                {
                    errorMsg = validateError;
                    SyncStateManager.ClearState();
                    return false;
                }
                LoadedVersion = Convert.ToInt32(root["version"]);// 记录加载的版本号

                //2. 读取sap模型名称
                LoadedSapModelName = root.ContainsKey("sapmodelname") ? root["sapmodelname"].ToString() : string.Empty;

                // 3. 解析映射表数组
                foreach (object item in (ArrayList)root["mappings"])//读取“mappings”对应的键值并转换成ArrayList
                {
                    Dictionary<string, object> map = (Dictionary<string, object>)item;//遍历数组将item拆分成字典
                    Guid rhinoId = Guid.Parse((string)map["RhinoID"]);
                    string sapId = (string)map["SAPID"];
                    SyncStateManager.AddMapping(rhinoId, sapId);
                }

                // 4. 解析历史状态数组
                foreach (object item in (ArrayList)root["states"])
                {
                    Dictionary<string, object> state = (Dictionary<string, object>)item;
                    LineState lineState = new LineState();
                    lineState.RhinoLineId = Guid.Parse((string)state["RhinoID"]);
                    lineState.X1 = Convert.ToDouble(state["x1"]);
                    lineState.Y1 = Convert.ToDouble(state["y1"]);
                    lineState.Z1 = Convert.ToDouble(state["z1"]);
                    lineState.X2 = Convert.ToDouble(state["x2"]);
                    lineState.Y2 = Convert.ToDouble(state["y2"]);
                    lineState.Z2 = Convert.ToDouble(state["z2"]);
                    lineState.LayerName = (string)state["layername"];
                    SyncStateManager.AddHistory(lineState.RhinoLineId, lineState);
                }
                return true;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("读取映射文件失败：" + ex.Message);
                SyncStateManager.ClearState();// 读取失败，清空内存状态，避免残留旧数据
                return false;
            }
        }
    }
}
