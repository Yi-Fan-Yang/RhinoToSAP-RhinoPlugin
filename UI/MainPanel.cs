using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;  
using Rhino.UI.Controls;
using RhinoToSAP.MappingFile;
using RhinoToSAP.Sync;
using RhinoToSAP.Tools;
using System;
using System.Runtime.InteropServices;

namespace RhinoToSAP.UI
{
    [Guid("3A84F7D4-A737-4761-B069-6FF61F0316CB")]
    // 主面板：Rhino侧边栏面板，后面再填充UI布局
    public class MainPanel : Panel, IPanel
    {
        // ========== 控件声明（后面要用到的控件都在这里声明） ==========
        //==========连接区==========
        private DropDown _layerDropDown;       // 图层下拉列表
        private CheckBox _layerLockCheckBox;   // 图层锁定复选框
        private TextBox _intervalTextBox;      // 同步间隔输入
        private Button _connectButton;          // 连接按钮
        private Button _disconnectButton;       // 断开按钮
        private Label _statusLabel;             // 连接状态显示

        //==========映射区==========
        private Label _mappingPathLabel;        // 当前文件路径
        private Button _loadMappingButton;       // 加载按钮
        private Button _newMappingButton;        // 新建按钮
        private Button _saveMappingButton;       // 保存按钮
        private Button _saveAsMappingButton;     // 另存为按钮
        private Label _mappingStatusLabel;       // 状态显示

        // ========== 同步操作区 ==========
        private Button _manualSyncButton;     // 手动增量同步按钮
        private Button _fullSyncButton;        // 全量同步按钮
        private Label _syncCountLabel;         // 同步次数显示

        // ========== 显示设置区 ==========
        private CheckBox _highlightCheckBox;    // 高亮开关
        private TextBox _highlightWidthTextBox; // 高亮线宽

        // ========== 运行日志区 ==========
        private TextArea _logTextArea;          // 日志文本框

        public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
        {
        }

        public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
        {
        }

        public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
        {
        }

        public MainPanel(uint documentSerialNumber)
        {
            this.Size = new Size(300, 1000);
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            CreateControls();// 创建控件
            LayoutControls();//布局
            BindEvents();//绑定事件
            RefreshLayerList();//刷新图层下拉列表
        }

        // 创建所有控件实例
        private void CreateControls()
        {
            //==============连接区==============
            // 图层下拉列表
            _layerDropDown = new DropDown();
            _layerDropDown.Width = 150;

            // 图层锁定复选框
            _layerLockCheckBox = new CheckBox();
            _layerLockCheckBox.Text = "锁定";
            _layerLockCheckBox.Checked = true;  // 默认锁定
            _layerLockCheckBox.Width = 50;

            // 同步间隔输入
            _intervalTextBox = new TextBox();
            _intervalTextBox.Text = "3";  // 默认3秒

            // 连接按钮
            _connectButton = new Button();
            _connectButton.Text = "连接";
            _connectButton.Width = 100;

            // 断开按钮
            _disconnectButton = new Button();
            _disconnectButton.Text = "断开";
            _disconnectButton.Enabled = false;  // 默认禁用，连接后才能断开
            _disconnectButton.Width = 100;

            // 状态标签
            _statusLabel = new Label();
            _statusLabel.Text = "状态：未连接";
            _statusLabel.BackgroundColor = Colors.Blue;

            // ========== 映射文件区控件 ==========
            _mappingPathLabel = new Label();
            _mappingPathLabel.Text = "当前文件：未加载";

            _loadMappingButton = new Button();
            _loadMappingButton.Text = "加载";
            _loadMappingButton.Width = 100;

            _newMappingButton = new Button();
            _newMappingButton.Text = "新建";
            _newMappingButton.Width = 100;

            _saveMappingButton = new Button();
            _saveMappingButton.Text = "保存";
            _saveMappingButton.Width = 100;

            _saveAsMappingButton = new Button();
            _saveAsMappingButton.Text = "另存为";
            _saveAsMappingButton.Width = 100;

            _mappingStatusLabel = new Label();
            _mappingStatusLabel.Text = "状态：未加载映射文件";
            _mappingStatusLabel.BackgroundColor = Colors.Blue;

            // ========== 同步操作区控件 ==========
            _manualSyncButton = new Button();
            _manualSyncButton.Text = "增量同步";
            _manualSyncButton.Width = 100;

            _fullSyncButton = new Button();
            _fullSyncButton.Text = "全量同步(慎点)";
            _fullSyncButton.Width = 100;

            _syncCountLabel = new Label();
            _syncCountLabel.Text = "已同步：0 次";

            // ========== 显示设置区控件 ==========
            _highlightCheckBox = new CheckBox();
            _highlightCheckBox.Text = "高亮已同步杆件";
            _highlightCheckBox.Checked = true;

            _highlightWidthTextBox = new TextBox();
            _highlightWidthTextBox.Text = "3";
            _highlightWidthTextBox.Width = 60;

            // ========== 运行日志区控件 ==========
            _logTextArea = new TextArea();
            _logTextArea.Height = 120;
            _logTextArea.Width = 270;
            _logTextArea.ReadOnly = true;  // 只读，用户不能编辑
            _logTextArea.Wrap = true;       // 自动换行

        }


        private void LayoutControls()
        {
            // 主布局：垂直排列
            StackLayout mainLayout = new StackLayout();
            mainLayout.Orientation = Orientation.Vertical;
            mainLayout.Spacing = 8;
            mainLayout.Padding = new Padding(10);

            // ========== 连接区 ==========

            // 图层行
            StackLayout layerRow = new StackLayout();
            layerRow.Orientation = Orientation.Horizontal;
            layerRow.Spacing = 5;
            layerRow.Items.Add(new Label { Text = "图层" });
            layerRow.Items.Add(_layerDropDown);
            layerRow.Items.Add(_layerLockCheckBox);
            mainLayout.Items.Add(layerRow);

            // 间隔行
            StackLayout intervalRow = new StackLayout();
            intervalRow.Orientation = Orientation.Horizontal;
            intervalRow.Spacing = 5;
            intervalRow.Items.Add(new Label { Text = "间隔(s)" });
            intervalRow.Items.Add(_intervalTextBox);
            mainLayout.Items.Add(intervalRow);

            // 按钮行
            StackLayout buttonRow = new StackLayout();
            buttonRow.Orientation = Orientation.Horizontal;
            buttonRow.Spacing = 5;
            buttonRow.Items.Add(_connectButton);
            buttonRow.Items.Add(_disconnectButton);
            mainLayout.Items.Add(buttonRow);

            // 状态
            mainLayout.Items.Add(_statusLabel);

            Divider divider = new Divider();
            mainLayout.Items.Add(divider);

            // ========== 映射文件区 ==========

            // 文件路径行
            StackLayout pathRow = new StackLayout();
            pathRow.Orientation = Orientation.Horizontal;
            pathRow.Spacing = 5;
            pathRow.Items.Add(new Label { Text = "映射文件" });
            pathRow.Items.Add(_mappingPathLabel);
            mainLayout.Items.Add(pathRow);

            // 按钮行1
            StackLayout mapButtonRow1 = new StackLayout();
            mapButtonRow1.Orientation = Orientation.Horizontal;
            mapButtonRow1.Spacing = 5;
            mapButtonRow1.Items.Add(_loadMappingButton);
            mapButtonRow1.Items.Add(_newMappingButton);
            mainLayout.Items.Add(mapButtonRow1);

            // 按钮行2
            StackLayout mapButtonRow2 = new StackLayout();
            mapButtonRow2.Orientation = Orientation.Horizontal;
            mapButtonRow2.Spacing = 5;
            mapButtonRow2.Items.Add(_saveMappingButton);
            mapButtonRow2.Items.Add(_saveAsMappingButton);
            mainLayout.Items.Add(mapButtonRow2);

            // 状态
            mainLayout.Items.Add(_mappingStatusLabel);

            // 分隔线（Rhino自带）
            mainLayout.Items.Add(divider);

            // ========== 同步操作区 ==========
            StackLayout syncButtonRow = new StackLayout();
            syncButtonRow.Orientation = Orientation.Horizontal;
            syncButtonRow.Spacing = 5;
            syncButtonRow.Items.Add(_manualSyncButton);
            syncButtonRow.Items.Add(_fullSyncButton);
            mainLayout.Items.Add(syncButtonRow);
            mainLayout.Items.Add(_syncCountLabel);

            // 分隔线
            mainLayout.Items.Add(divider);

            // ========== 显示设置区 ==========
            mainLayout.Items.Add(_highlightCheckBox);

            StackLayout highlightWidthRow = new StackLayout();
            highlightWidthRow.Orientation = Orientation.Horizontal;
            highlightWidthRow.Spacing = 5;
            highlightWidthRow.Items.Add(new Label { Text = "高亮线宽" });
            highlightWidthRow.Items.Add(_highlightWidthTextBox);
            mainLayout.Items.Add(highlightWidthRow);

            // 分隔线
            mainLayout.Items.Add(divider);

            // ========== 运行日志区 ==========
            mainLayout.Items.Add(new Label { Text = "运行日志" });
            mainLayout.Items.Add(_logTextArea);

            // ==============底部可拉伸空白（让内容靠上）================
            mainLayout.Items.Add(null);

            this.Content = mainLayout;

        }


        private void BindEvents()
        {
            // ========== 连接区 ==========
            // 图层锁定复选框：勾选时禁用下拉列表，取消勾选时启用
            _layerLockCheckBox.CheckedChanged += (sender, e) =>
            {
                _layerDropDown.Enabled = !_layerLockCheckBox.Checked.Value;
            };

            // 连接按钮点击事件
            _connectButton.Click += (sender, e) =>
            {
                try
                {
                    string layerName = _layerDropDown.SelectedKey?.ToString() ?? "";
                    string result = SAPConnector.Connect(layerName, _intervalTextBox.Text);
                    AddLog(result);
                    UpdateConnectionStatus();
                }
                catch (Exception ex)
                {
                    AddLog($"连接异常：{ex.Message}");
                }
            };

            //断开按钮事件
            _disconnectButton.Click += (sender, e) =>
            {
                try
                {
                    SAPConnector.Disconnect();
                    AddLog("已断开连接");
                    UpdateConnectionStatus();
                    UpdateMappingStatus();
                }
                catch (Exception ex)
                {
                    AddLog($"断开失败：{ex.Message}");
                }
            };

            // ========== 映射文件区 ==========

            // 加载按钮
            _loadMappingButton.Click += (sender, e) =>
            {
                try
                {
                    if (!SAPConnector.IsConnected)
                    {
                        AddLog("请先连接SAP，再加载映射文件");
                        return;
                    }
                    MappingFileManager.LoadWithDialog();
                    AddLog(MappingFileManager.report);
                    UpdateMappingStatus();
                }
                catch (Exception ex)
                {
                    AddLog($"加载映射文件异常：{ex.Message}");
                }
            };

            // 新建按钮
            _newMappingButton.Click += (sender, e) =>
            {
                try
                {
                    if (!SAPConnector.IsConnected)
                    {
                        AddLog("请先连接SAP，再新建映射文件");
                        return;
                    }
                    MappingFileManager.NewWithDialog();
                    AddLog(MappingFileManager.report);
                    UpdateMappingStatus();
                }
                catch (Exception ex)
                {
                    AddLog($"新建映射文件异常：{ex.Message}");
                }
            };

            // 保存按钮
            _saveMappingButton.Click += (sender, e) =>
            {
                try
                {
                    MappingFileManager.Save();
                    AddLog(MappingFileManager.report);
                }
                catch (Exception ex)
                {
                    AddLog($"保存映射文件异常：{ex.Message}");
                }
            };

            // 另存为按钮
            _saveAsMappingButton.Click += (sender, e) =>
            {
                try
                {
                    MappingFileManager.SaveAsWithDialog();
                    AddLog(MappingFileManager.report);
                    UpdateMappingStatus();
                }
                catch (Exception ex)
                {
                    AddLog($"另存为映射文件异常：{ex.Message}");
                }
            };

            // ========== 同步操作区 ==========

            // 增量同步按钮
            _manualSyncButton.Click += (sender, e) =>
            {
                try
                {
                    SyncEngine.ManualSync();
                    // 更新同步次数显示
                    _syncCountLabel.Text = $"已同步：{SyncEngine.SyncCount} 次";
                    AddLog("手动增量同步完成");
                }
                catch (Exception ex)
                {
                    AddLog($"增量同步异常：{ex.Message}");
                }
            };

            // 全量同步按钮
            _fullSyncButton.Click += (sender, e) =>
            {
                try
                {
                    // 弹窗确认
                    var confirm = Rhino.UI.Dialogs.ShowMessage(
                                "全量同步会清空当前映射表,并删除SAP2000中全部对象，确定要执行吗？",
                                "全量同步确认",
                                Rhino.UI.ShowMessageButton.YesNo,
                                Rhino.UI.ShowMessageIcon.Warning);
                    if(confirm==Rhino.UI.ShowMessageResult.No)
                    {
                        AddLog("已取消全量同步");
                        return;
                    }
                    string result = SyncEngine.FullSync();
                    AddLog(result);                    
                    // 更新同步次数显示
                    _syncCountLabel.Text = $"已同步：{SyncEngine.SyncCount} 次";
                    AddLog("全量同步完成");
                }
                catch (Exception ex)
                {
                    AddLog($"全量同步异常：{ex.Message}");
                }
            };

            // ========== 显示设置区 ==========

            // 高亮开关
            _highlightCheckBox.CheckedChanged += (sender, e) =>
            {
                string result = SyncEngine.SetHighlightEnabled(_highlightCheckBox.Checked.Value);
                AddLog(result);
            };

            // 高亮线宽
            _highlightWidthTextBox.TextChanged += (sender, e) =>
            {
                string result = SyncEngine.SetHighlightWidth(_highlightWidthTextBox.Text);
                AddLog(result);
            };

        }
            

        // 刷新图层下拉列表
        private void RefreshLayerList()
        {
            _layerDropDown.Items.Clear();
            var layers = RhinoDoc.ActiveDoc.Layers;
            foreach (var layer in layers)
            {
                if (layer.ParentLayerId == Guid.Empty)
                {
                    _layerDropDown.Items.Add(layer.Name);
                }
            }
        }

        //添加日志
        private void AddLog(string message)
        {
            _logTextArea.Text += $"{DateTime.Now:HH:mm:ss} - {message}\n";
            // 自动滚动到底部
            _logTextArea.CaretIndex = _logTextArea.Text.Length;
        }

        // 更新连接状态显示
        private void UpdateConnectionStatus()
        {
            if (SAPConnector.IsConnected)
            {
                _statusLabel.Text = "状态：已连接";
                _statusLabel.BackgroundColor = Colors.Green;
                _connectButton.Enabled = false;
                _disconnectButton.Enabled = true;
            }
            else
            {
                _statusLabel.Text = "状态：未连接";
                _statusLabel.BackgroundColor = Colors.Red;
                _connectButton.Enabled = true;
                _disconnectButton.Enabled = false;
            }
        }

        // 更新映射文件状态显示
        private void UpdateMappingStatus()
        {
            if (SyncEngine.IsMappingLoaded)
            {
                _mappingPathLabel.Text = System.IO.Path.GetFileName(SyncPersistenceIO.CurrentMappingFilePath);
                _mappingStatusLabel.Text = $"状态：已加载映射文件 ";
                _mappingStatusLabel.BackgroundColor = Colors.Green;
            }
            else
            {
                _mappingPathLabel.Text = "未加载";
                _mappingStatusLabel.Text = "状态：未加载映射文件";
                _mappingStatusLabel.BackgroundColor = Colors.Red;
            }
        }

        // 检查连接和映射文件是否都就绪，就绪返回true，否则显示日志并返回false


    }
}
