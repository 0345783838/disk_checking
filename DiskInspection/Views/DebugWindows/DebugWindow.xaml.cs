using DiskInspection.Controllers;
using DiskInspection.Controllers.APIs;
using DiskInspection.Controllers.Camera;
using DiskInspection.Models;
using DiskInspection.Utils;
using DiskInspection.Views.UtilitiesWindows;
using Emgu.CV;
using Emgu.CV.Structure;
using LiveCharts.Wpf;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
using ZedGraph;

namespace DiskInspection.Views.DebugWindows
{
    /// <summary>
    /// Interaction logic for DebugWindow.xaml
    /// </summary>
    public partial class DebugWindow : Window, INotifyPropertyChanged
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        private CameraManager _cameraManager;
        private LincolnCamera _selectedCamera;
        private bool _loaded = false;
        private bool _firstTime = true;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private EnvReader _envConfigRaw;
        private EnvReader _backupConfig;
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
            if (SelectedImageInfo != null && SelectedImageInfo.Images.Count > 0)
            {
                SelectedImage = SelectedImageInfo.Images[0];
            }
        }


        private ImageList _selectedImage;
        private double _curImageScale;

        public ImageList SelectedImage
        {
            get => _selectedImage;
            set
            {
                if (_selectedImage != value)
                {
                    _selectedImage = value;
                    OnPropertyChanged();
                    UpdateImageSetlectionChanged();
                    OnPropertyChanged(nameof(IsBackEnable));
                    OnPropertyChanged(nameof(IsNextEnable));
                }
            }
        }

        private void UpdateImageSetlectionChanged()
        {
            if (SelectedImage == null)
                return;
            lbTitile.Content = SelectedImage.Title;
            UpdateImage(SelectedImage.Image);
        }

        public bool IsBackEnable => (SelectedImageInfo !=null && SelectedImageInfo.Images.IndexOf(SelectedImage) > 0);
        public bool IsNextEnable => (SelectedImageInfo != null && SelectedImageInfo.Images.IndexOf(SelectedImage) < SelectedImageInfo.Images.Count - 1);
        public int MaxValue => (ImagesInfoList.Count > 0 && !Object.ReferenceEquals(ImagesInfoList, null)) ? ImagesInfoList.Count : 1;
        public int ProcessingCount => (ImagesInfoList.Count > 0 && !Object.ReferenceEquals(ImagesInfoList, null)) ? ImagesInfoList.Count(x => x.Status != (int)FileStatus.NOT_DONE) : 0;
        public string ProcessingRatio => (ImagesInfoList.Count > 0 && !Object.ReferenceEquals(ImagesInfoList, null)) ? $"Processed: {(((double)ImagesInfoList.Count(x => x.Status != (int)FileStatus.NOT_DONE)) / (double)ImagesInfoList.Count * 100):F2}%" : "0.00%";
        public bool CanCapture { get; set; } = false;

        public DebugWindow()
        {
            InitializeComponent();
            DataContext = this;
            GetEnvConfig();
            ImagesInfoList.CollectionChanged += (s, e) => OnPropertyChanged(nameof(MaxValue));
            _cameraManager = CameraManager.GetInstance();
        }
        private void GetEnvConfig()
        {
            var configPath = @"plugin\config\config.env";
            _envConfigRaw = new EnvReader(configPath);

            _backupConfig = _envConfigRaw.Clone();

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
                ImagesInfoList.Clear();
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
                var res = APICommunication.DebugImages(_param.ApiUrlAi, image, _envConfig);

                var dctectImg = Converter.Base64ToBitmap(res.DetectImg);
                var segmentImg = Converter.Base64ToBitmap(res.SegmentImg);
                var finalImg = Converter.Base64ToBitmap(res.FinalImg);
                imageInfo.Images.Add(new ImageList(0, "Original Image", image.Bitmap));
                imageInfo.Images.Add(new ImageList(1, "Detect Image", dctectImg));
                imageInfo.Images.Add(new ImageList(2, "Segment Image", segmentImg));
                imageInfo.Images.Add(new ImageList(3, "Final Image", finalImg));
                imageInfo.Status = res.Result ? (int)FileStatus.OK : (int)FileStatus.NG;

                OnPropertyChanged(nameof(ProcessingCount));
                OnPropertyChanged(nameof(ProcessingRatio));
            }
        }

        private void btnTriggerSoftware_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_selectedCamera.Start())
            {
                var error = new ErrorWindow("Can't capture image, please check the Camera connection settings!\rKhông thể chụp ảnh, hãy kiểm tra setting kết nối Camera!");
                error.ShowDialog();
                return;
            }
            if (rbUvLight.IsChecked == false && rbWhiteLight.IsChecked == false)
            {
                var error = new ErrorWindow("Please select a light source to turn on!\rHãy chọn nguồn đèn để bật!");
                error.ShowDialog();
                return;
            }


            Bitmap bitmapImage = _selectedCamera.GetBitmap();
            Image<Bgr, byte> img = new Image<Bgr, byte>(bitmapImage);
            UpdateImage(bitmapImage);

            // Case: White Light
            if (rbWhiteLight.IsChecked == true)
            {
                var imageInfo = new ImageDebugInfo(ImagesInfoList.Count + 1, $"Captured Image {MyDateTime.GetStringDateTime()}");
                ImagesInfoList.Add(imageInfo);

                var imageList = new List<ImageList>();
                var checkingRes = false;
                var waiting = new WaitingWindow("Waiting for image processing...\rĐang xử lý hình ảnh...");
                new Task(() =>
                {
                    var res = APICommunication.DebugImages(_param.ApiUrlAi, img.Mat, _envConfig);
                    var dctectImg = Converter.Base64ToBitmap(res.DetectImg);
                    var segmentImg = Converter.Base64ToBitmap(res.SegmentImg);
                    var finalImg = Converter.Base64ToBitmap(res.FinalImg);
                    checkingRes = res.Result;
                    imageList = new List<ImageList>()
                    {
                        new ImageList(0, "Original Image", bitmapImage),
                        new ImageList(1, "Detect Image", dctectImg),
                        new ImageList(2, "Segment Image", segmentImg),
                        new ImageList(3, "Final Image", finalImg)
                    };
                    waiting.KillMe = true;
                }).Start();
                waiting.ShowDialog();

                imageInfo.Images = imageList;
                imageInfo.Status = checkingRes ? (int)FileStatus.OK : (int)FileStatus.NG;
                SelectedImageInfo = imageInfo;
            }
            else
            {
                var imageInfo = new ImageDebugInfo(ImagesInfoList.Count + 1, $"Captured Image {MyDateTime.GetStringDateTime()}");
                ImagesInfoList.Add(imageInfo);

                var imageList = new List<ImageList>();
                var checkingRes = false;
                var waiting = new WaitingWindow("Waiting for image processing...\rĐang xử lý hình ảnh...");
                new Task(() =>
                {
                    var res = APICommunication.DebugUvImages(_param.ApiUrlAi, img.Mat, _envConfig);
                    var finalImg = Converter.Base64ToBitmap(res.FinalImg);
                    checkingRes = res.Result;
                    imageList = new List<ImageList>()
                    {
                        new ImageList(0, "Original Image", bitmapImage),
                        new ImageList(1, "Final Image", finalImg)
                    };
                    waiting.KillMe = true;
                }).Start();
                waiting.ShowDialog();

                imageInfo.Images = imageList;
                imageInfo.Status = checkingRes ? (int)FileStatus.OK : (int)FileStatus.NG;
                SelectedImageInfo = imageInfo;
            }
        }

        private void btnSetting_Click(object sender, RoutedEventArgs e)
        {
            var settingWindow = new ParamsWindow(this, _envConfig);
            settingWindow.ShowDialog();
        }

        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var warning = new WarningWindow("Are you sure to save settings?\rBạn có chắc muốn lưu lại params mới?");
            var resSaveConfig = false;
            var resRestart = false;
            if (warning.ShowDialog() == true)
            {
                WaitingWindow wait = new WaitingWindow("Đang lưu lại params...");
                new Task(() =>
                {
                    resSaveConfig = UpdateEnvConfig();
                    AIServiceController.CloseProcessExisting();
                    AIServiceController.Start();
                    var timeout = 5000;
                    var timeStep = timeout / 1000;
                    
                    for (int i = 0; i < timeStep; i++)
                    {
                        Thread.Sleep(1000);
                        if (APICommunication.CheckAPIStatus(_param.ApiUrlAi, 1000))
                        {
                            _logger.Info("Re - Start AI Python Engine Successfuly!");
                            resRestart = true;
                            break;
                        }
                    }
                    wait.KillMe = true;
                }).Start();

                wait.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                wait.ShowDialog();
            }
            if (resSaveConfig && resRestart)
            {
                var info = new InformationWindow("Save params successfully!\rLưu params thành công!");
                info.ShowDialog();
            }
            else if (!resSaveConfig)
            {
                var error = new ErrorWindow("Save params failed!\rLưu params không thành công!");
                error.ShowDialog();
            }
            else
            {
                var error = new ErrorWindow("Restart AI service failed!\rKhông khởi động lại được AI, lưu lại params cũ!");
                _backupConfig.Save();
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
            var curIndex = cbbImageIndex.SelectedIndex;
            if (curIndex > 0)
                cbbImageIndex.SelectedIndex = curIndex - 1;
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            var curIndex = cbbImageIndex.SelectedIndex;
            if (curIndex < SelectedImageInfo.Images.Count - 1)
                cbbImageIndex.SelectedIndex = curIndex + 1;
        }

        private void btnResetScale_Click(object sender, RoutedEventArgs e)
        {
            if (!object.ReferenceEquals(imbImage.Source, null))
            {
                imbImage.SetZoomScale(_curImageScale);
                imbImage.GoToXY(0, 0);
            }
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
        public void UpdateImage(Bitmap image)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                if (image == null)
                {
                    imbImage.Source = null;
                }
                else if (imbImage.Source == null)
                {
 
                    _curImageScale = GetFittedZoomScale(imbImage, image.Width, image.Height);
                    imbImage.SourceFromBitmap = image;
                    imbImage.SetZoomScale(_curImageScale);
                }
                else
                {
                    imbImage.SourceFromBitmap = image;
                }

            }));
        }
        private double GetFittedZoomScale(object imb, double imageWidth, double imageHeight)
        {
            var imageBox = imb as Heal.MyControl.ImageBox;
            double imageBoxWidth = imageBox.ActualWidth;
            double imageBoxHeight = imageBox.ActualHeight;
            var scale = Math.Min(imageBoxWidth / imageWidth, imageBoxHeight / imageHeight);
            return scale;
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (tabOffline.IsSelected)
            {
                // Cứ tắt led, uv, nếu chưa có kết nối cũng không sao
                new Task(() =>
                {
                    APICommunication.ControlLed(_param.ApiUrlCom, status: false);
                    APICommunication.ControlUv(_param.ApiUrlCom, status: false);
                }).Start();
                rbUvLight.IsChecked = false;
                rbWhiteLight.IsChecked = false;
            }
            else
            {
                if (_firstTime)
                {
                    _firstTime = false;
                    bool resConnection = false;
                    var waiting = new WaitingWindow("Waiting for connection to PLC...\rĐang chờ kết nối PLC...");
                    new Task(() => 
                    {
                        resConnection = APICommunication.ConnectPlc(_param.ApiUrlCom, _param.PlcIp, _param.PlcPort);
                        waiting.KillMe = true;
                    }).Start();
                    waiting.ShowDialog();

                    if (!resConnection)
                    {
                        var error = new ErrorWindow("Cannot connect to PLC, please check the PLC connection settings!\rKhông thể kết nối PLC, hãy kiểm tra setting kết nối PLC!");
                        error.ShowDialog();
                        return;
                    }
                }
            }
        }

        private void rbLight_Checked(object sender, RoutedEventArgs e)
        {
            if (!_loaded)
                return;

            if (rbWhiteLight.IsChecked == true)
            {
                var res = APICommunication.ControlLed(_param.ApiUrlCom, status: true);
                if (!res)
                {
                    var error = new ErrorWindow("Cannot turn on White Light, please check the PLC connection settings!\rKhông thể bật đèn trắng, hãy kiểm tra setting kết nối PLC!");
                    error.ShowDialog();
                }
            }
            else
            {
                var res = APICommunication.ControlUv(_param.ApiUrlCom, status: true);
                if (!res)
                {
                    var error = new ErrorWindow("Cannot turn on UV Light, please check the PLC connection settings!\rKhông thể bật đèn UV, hãy kiểm tra setting kết nối PLC!");
                    error.ShowDialog();
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            //APICommunication.ConnectPlc(_param.ApiUrlCom, _param.PlcIp, _param.PlcPort);
        }

        private void cbbCamera_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var cameraName = cbbCamera.SelectedValue.ToString();
            if (cameraName == "CAM 1")
            {
                _selectedCamera = _cameraManager.GetCamera1();
            }
            else 
            {
                _selectedCamera = _cameraManager.GetCamera2();
            }

            if (!_selectedCamera.IsOpen())
            {
                var error = new ErrorWindow($"Cannot connect to Camera {cameraName}, please check the Camera connection settings!\rKhông thể kết nối Camera {cameraName}, hãy kiểm tra setting kết nối Camera!");
                error.ShowDialog();
            }
            else
            {
                CanCapture = true;
                OnPropertyChanged(nameof(CanCapture));
            }
        }
    }
}
