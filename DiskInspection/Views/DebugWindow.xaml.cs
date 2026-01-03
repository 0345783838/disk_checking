using DiskInspection.Controllers.APIs;
using DiskInspection.Models;
using DiskInspection.Utils;
using DiskInspection.Views.UtilitiesWindows;
using Emgu.CV;
using LiveCharts.Wpf;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DiskInspection.Views
{
    /// <summary>
    /// Interaction logic for DebugWindow.xaml
    /// </summary>
    public partial class DebugWindow : Window, INotifyPropertyChanged
    {
        private Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private EnvReader _envConfigRaw;
        EnvironmentConfig _envConfig;
        public bool CanSave { get; set; } = false;

        public ObservableCollection<ImageDebugInfo> ImagesInfoList { get; set; } = new ObservableCollection<ImageDebugInfo>();
        private ImageDebugInfo _selectedImageInfo;
        public ImageDebugInfo SelectedImageInfo
        {
            get => _selectedImageInfo;
            set
            {
                if (_selectedImageInfo != value)
                {
                    _selectedImageInfo = value;
                    OnPropertyChanged();
                    UpdateSetlectionChanged();
                }
            }
        }

        private void UpdateSetlectionChanged()
        {

        }

        public DebugWindow()
        {
            InitializeComponent();
            DataContext = this;
            GetEnvConfig();
        }
        private void GetEnvConfig()
        {
            var configPath = @"plugin\config\config.env";
            _envConfigRaw = new EnvReader(configPath);

            _envConfig = new EnvironmentConfig(_envConfigRaw.GetFloat("DISK_POINT_DETECT_CONF_THRESH", (float) 0.2), _envConfigRaw.GetFloat("DISK_POINT_DETECT_IOU_THRESH", (float) 0.1),
                _envConfigRaw.GetFloat("DISK_SEGMENT_CONF_THRESH", (float) 0.95), _envConfigRaw.GetFloat("CALIPER_MIN_EDGE_DISTANCE", 4), _envConfigRaw.GetFloat("CALIPER_MAX_EDGE_DISTANCE", 20),
                _envConfigRaw.GetFloat("CALIPER_LENGTH_RATE", (float)0.95), _envConfigRaw.GetIntArray("CALIPER_THICKNESS_LIST"), _envConfigRaw.GetInt("NUM_DISK", 25), _envConfigRaw.GetFloat("MAX_DISK_DISTANCE", 86),
                 _envConfigRaw.GetFloat("MIN_DISK_DISTANCE", 24), _envConfigRaw.GetFloat("MIN_DISK_AREA", 150));

        }
        private void btnLoadFolder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var valPath = string.Empty;
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "Chọn thư mục ảnh dùng để phân tích dữ liệu",
                Multiselect = false
            };
            WindowInteropHelper helper = new WindowInteropHelper(this);
            if (dialog.ShowDialog(helper.Handle) == CommonFileDialogResult.Ok)
            {
                valPath = dialog.FileName;
            }

            if (valPath == string.Empty)
                return;

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
            var imageFiles = Directory.GetFiles(valPath)
                                     .Where(file => imageExtensions.Contains(System.IO.Path.GetExtension(file).ToLower()));

            var imageFilesList = imageFiles.ToList();

            ImagesInfoList.Clear();

            for (var i = 0; i < imageFilesList.Count; i++)
            {
                var imageInfo = new ImageDebugInfo(i, imageFilesList[i]);
                ImagesInfoList.Add(imageInfo);
            }
            StartCheckingThread();
            SelectedImageInfo = ImagesInfoList[0];
        }

        private void btnLoadImages_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Image files (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                var fileName = openFileDialog.FileNames;
                if (fileName.Length == 0)
                {
                    var error = new ErrorWindow("Image paths is empty!\rĐường dẫn ảnh rỗng!");
                    error.ShowDialog();
                    return;
                }
                foreach (var path in fileName)
                {
                    if (ImagesInfoList.Select(obj => obj.FilePath).ToList().Contains(path))
                    {
                        var error = new ErrorWindow($"{IO.GetFileName(path)} is already existed in list!\r{IO.GetFileName(path)} ảnh đã tồn tại trong danh sách!");
                        error.ShowDialog();
                    }
                    else
                    {
                        var newImageInfo = new ImageDebugInfo(ImagesInfoList.Count + 1, path);
                        ImagesInfoList.Add(newImageInfo);
                    }
                }
                SelectedImageInfo = ImagesInfoList[ImagesInfoList.Count - 1];
                dgImageInfoPaths.ScrollIntoView(SelectedImageInfo);
                StartCheckingThread();
            }
        }

        private void StartCheckingThread()
        {
            Task task = new Task(() => CheckingDisk(ImagesInfoList));
            task.Start();
        }
        private void CheckingDisk(ObservableCollection<ImageDebugInfo> imagesInfoList)
        {
            for (var i = 0; i < imagesInfoList.Count; i++)
            {
                var imageInfo = imagesInfoList[i];
                Mat image = CvInvoke.Imread(imageInfo.FilePath);
                APICommunication.DebugImages(_param.API_URL, image, _envConfig);

            }
        }

        private void btnTriggerSoftware_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void btnSetting_Click(object sender, RoutedEventArgs e)
        {
            var settingWindow = new ParamsWindow(this, _envConfig);
            settingWindow.ShowDialog();
        }

        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var warning = new WarningWindow("Are you sure to save settings?\rBạn có chắc muốn lưu lại params mới?");
            var res = false;
            if (warning.ShowDialog() == true)
            {
                WaitingWindow wait = new WaitingWindow("Đang lưu lại params...");
                new Task(() =>
                {
                    res = UpdateEnvConfig();
                    wait.KillMe = true;
                }).Start();

                wait.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                wait.ShowDialog();
            }
            if (res)
            {
                var info = new InformationWindow("Save params successfully!\rLưu params thành công!");
                info.ShowDialog();
            }
            else
            {
                var error = new ErrorWindow("Save params failed!\rLưu params không thành công!");
                error.ShowDialog();
            }
        }

        private bool UpdateEnvConfig()
        {
            try
            {
                _envConfigRaw.Set("DISK_POINT_DETECT_CONF_THRESH", _envConfig.DetectThreshold.ToString());
                _envConfigRaw.Set("DISK_POINT_DETECT_IOU_THRESH", _envConfig.DetectIou.ToString());
                _envConfigRaw.Set("DISK_SEGMENT_CONF_THRESH", _envConfig.SegmentThreshold.ToString());
                _envConfigRaw.Set("CALIPER_MIN_EDGE_DISTANCE", _envConfig.CaliperMinEdgeDistance.ToString());
                _envConfigRaw.Set("CALIPER_MAX_EDGE_DISTANCE", _envConfig.CaliperMaxEdgeDistance.ToString());
                _envConfigRaw.Set("CALIPER_LENGTH_RATE", _envConfig.CaliperLengthRate.ToString());
                _envConfigRaw.Set("CALIPER_THICKNESS_LIST", string.Join(",", _envConfig.CaliperThicknessList));
                _envConfigRaw.Set("NUM_DISK", _envConfig.DiskNumber.ToString());
                _envConfigRaw.Set("MAX_DISK_DISTANCE", _envConfig.DiskMaxDistance.ToString());
                _envConfigRaw.Set("MIN_DISK_DISTANCE", _envConfig.DiskMinDistance.ToString());
                _envConfigRaw.Set("MIN_DISK_AREA", _envConfig.DiskMinArea.ToString());

                _envConfigRaw.Save();
                return true;
            }
            catch 
            {
                return false;
            }
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnResetScale_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ccbbImageIndex_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        internal void UpdateConfig(EnvironmentConfig newConfig)
        {
            _envConfig = newConfig;
            CanSave = true;
            OnPropertyChanged(nameof(CanSave));
        }
    }
}
