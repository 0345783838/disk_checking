using DiskInspection.Controllers;
using DiskInspection.Controllers.APIs;
using DiskInspection.Controllers.Camera;
using DiskInspection.Controllers.PLC;
using DiskInspection.Models;
using DiskInspection.Services;
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
using System.Text.RegularExpressions;
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
        private string _selectedCameraName;
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
                _envConfigRaw.GetFloat("DISK_SEGMENT_CONF_THRESH", (float) 0.5), _envConfigRaw.GetFloat("DISK_SEGMENT_IOU_THRESH", (float)0.5), _envConfigRaw.GetFloat("CALIPER_MIN_EDGE_DISTANCE", 4), 
                _envConfigRaw.GetFloat("CALIPER_MAX_EDGE_DISTANCE", 20), _envConfigRaw.GetFloat("CALIPER_LENGTH_RATE", (float)0.95), _envConfigRaw.GetIntArray("CALIPER_THICKNESS_LIST"), 
                _envConfigRaw.GetInt("NUM_DISK", 25), _envConfigRaw.GetFloat("MAX_DISK_DISTANCE", 86), _envConfigRaw.GetFloat("MIN_DISK_DISTANCE", 24), _envConfigRaw.GetFloat("MIN_DISK_AREA", 150), 
                _envConfigRaw.GetInt("UV_DISK_THRESHOLD", 10), _envConfigRaw.GetFloat("UV_MIN_DISK_AREA", 20));

        }
        private void btnLoadFolder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var valPath = string.Empty;
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "Chọn thư mục ảnh dùng để chạy debug offline",
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

            // Đoạn này check xem các cặp White và UV rồi sort lại 1 cặp để sau chạy Checking theo thứ tự
            var allImages = imageFilesList
            .Select((path, idx) => new ImageDebugInfo(idx, path))
            .ToList();

            ImagesInfoList.Clear();

            ReorderImages(allImages, ImagesInfoList);
            StartCheckingThread();
        }
        bool IsWhite(string name) => name.IndexOf("White", StringComparison.OrdinalIgnoreCase) >= 0;

        bool IsUV(string name) => name.IndexOf("UV", StringComparison.OrdinalIgnoreCase) >= 0;

        string GetBaseKey(string filePath)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(filePath);

            name = Regex.Replace(name, "White", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, "UV", "", RegexOptions.IgnoreCase);

            return name;
        }
        void ReorderImages(IList<ImageDebugInfo> source,
                   ObservableCollection<ImageDebugInfo> ImagesInfoList)
        {
            ImagesInfoList.Clear();

            var groups = source.GroupBy(x => GetBaseKey(x.FilePath));

            foreach (var g in groups)
            {
                var white = g.FirstOrDefault(x => IsWhite(x.FilePath));
                var uv = g.FirstOrDefault(x => IsUV(x.FilePath));

                // ❌ chỉ UV → skip
                if (white == null && uv != null)
                    continue;

                // ✅ add White
                if (white != null)
                {
                    ImagesInfoList.Add(white);

                    // add UV liền sau nếu có
                    if (uv != null)
                        ImagesInfoList.Add(uv);
                }
            }

            for (var i = 0; i < ImagesInfoList.Count; i++)
            {
                ImagesInfoList[i].ID = i + 1;
            }
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
                    ShowError("Image paths is empty!\rĐường dẫn ảnh rỗng!");
                    return;
                }

                // Đoạn này check xem các cặp White và UV rồi sort lại 1 cặp để sau chạy Checking theo thứ tự
                var allImages = fileName
                .Select((path, idx) => new ImageDebugInfo(idx, path))
                .ToList();

                ImagesInfoList.Clear();

                ReorderImages(allImages, ImagesInfoList);
                StartCheckingThread();
            }
        }

        private void StartCheckingThread()
        {
            Task task = new Task(() => CheckingDisk(ImagesInfoList));
            task.Start();
        }
        private void SetProgressActive(bool isActive)
        {
            this.Dispatcher.Invoke(() => { pgbProgress.IsIndeterminate = isActive; });
        }

        private void CheckingDisk(ObservableCollection<ImageDebugInfo> imagesInfoList)
        {
            if (imagesInfoList.Count == 0)
            {
                SetProgressActive(false);
                return;
            }
            OnPropertyChanged(nameof(ProcessingCount));
            OnPropertyChanged(nameof(ProcessingRatio));
            SetProgressActive(true);
            var firstActive = true;
            for (var i = 0; i < imagesInfoList.Count; i++)
            {
                var current = imagesInfoList[i];

                if (IsWhite(current.FilePath))
                {
                    // Run white inspection
                    Mat image = CvInvoke.Imread(current.FilePath);
                    var whiteOriginal = SafeBitmapFromMat(image);

                    var resWhite = APICommunication.DebugImages(_param.ApiUrlAi, image, _envConfig);
                    var dctectImg = Converter.Base64ToBitmap(resWhite.DetectImg);
                    var segmentImg = Converter.Base64ToBitmap(resWhite.SegmentImg);
                    var finalImg = Converter.Base64ToBitmap(resWhite.FinalImg);

                    // Update UI 
                    current.Images.Add(new ImageList(0, "Original Image", whiteOriginal));
                    current.Images.Add(new ImageList(1, "Detect Image", dctectImg));
                    current.Images.Add(new ImageList(2, "Segment Image", segmentImg));
                    current.Images.Add(new ImageList(3, "Final Image", finalImg));
                    current.Status = resWhite.Result ? (int)FileStatus.OK : (int)FileStatus.NG;
                    
                    OnPropertyChanged(nameof(ProcessingCount));
                    OnPropertyChanged(nameof(ProcessingRatio));

                    // check xem ảnh sau có phải UV cùng cặp không
                    if (i + 1 < ImagesInfoList.Count)
                    {
                        var next = ImagesInfoList[i + 1];
                        if (IsUV(next.FilePath) && GetBaseKey(next.FilePath) == GetBaseKey(current.FilePath))
                        {
                            // chạy UV, dùng kết quả White
                            Mat uvImage = CvInvoke.Imread(next.FilePath);
                            var uvOriginal = SafeBitmapFromMat(uvImage);
                            var resUv = APICommunication.DebugUvImages(_param.ApiUrlAi, uvImage, resWhite.CropBox, resWhite.UvBox1, resWhite.UvBox2, resWhite.Mid1, resWhite.Mid2, _envConfig);
                            var uvThresholdImg = Converter.Base64ToBitmap(resUv.ThresholdImg);
                            var uvFinalImg = Converter.Base64ToBitmap(resUv.FinalImg);

                            // Update UI 
                            next.Images.Add(new ImageList(0, "Original UV Image", uvOriginal));
                            next.Images.Add(new ImageList(1, "Threshold UV Image", uvThresholdImg));
                            next.Images.Add(new ImageList(2, "Final UV Image", uvFinalImg));
                            next.Status = resUv.Result ? (int)FileStatus.OK : (int)FileStatus.NG;

                            OnPropertyChanged(nameof(ProcessingCount));
                            OnPropertyChanged(nameof(ProcessingRatio));

                            i++;
                        }
                    }
                    else
                    {
                        // ❌ UV mà không có White trước → bỏ
                        continue;
                    }

                }
            }
            SetProgressActive(false);
        }
        Bitmap SafeBitmapFromMat(Mat mat)
        {
            using (var bmp = mat.Bitmap)
                return new Bitmap(bmp); // deep copy, độc lập bộ nhớ
        }

        private async void btnTriggerSoftware_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_selectedCamera.IsOpen())
            {
                ShowError("Can't capture image, please check the Camera connection settings!\rKhông thể chụp ảnh, hãy kiểm tra setting kết nối Camera!");
                return;
            }

            // Capture White Light

            _selectedCameraName = CameraName.CAM_1;

            if (_selectedCameraName == CameraName.CAM_1)
            {
                if (!await Task.Run(() => PlcController.ControlLed1(_param.ApiUrlCom, true, 1000)))
                {
                    ShowError(
                        "Cannot turn on LED 1! Please check the PLC connection\r" +
                        "Không bật được đèn LED 1, hãy kiểm tra kết nối PLC!");
                    return;
                }
                await Task.Delay(_param.Cam1Exposure + 10);
            }
            else if (_selectedCameraName == CameraName.CAM_2)
            {
                if (!await Task.Run(() => PlcController.ControlLed2(_param.ApiUrlCom, true, 1000)))
                {
                    ShowError(
                        "Cannot turn on LED 2! Please check the PLC connection\r" +
                        "Không bật được đèn LED 2, hãy kiểm tra kết nối PLC!");
                    return;
                }
                await Task.Delay(_param.Cam2Exposure + 10);
            }

            //Bitmap bitmapImage = new Bitmap(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\APP\test_white_ok.bmp");
            Bitmap bitmapImage = _selectedCamera.GetBitmap();
            await Task.Run(() => PlcController.ControlLed1(_param.ApiUrlCom, false, 1000));
            await Task.Run(() => PlcController.ControlLed2(_param.ApiUrlCom, false, 1000));
            Image<Bgr, byte> img = new Image<Bgr, byte>(bitmapImage);
            UpdateImage(bitmapImage);

            var imageInfo = new ImageDebugInfo(ImagesInfoList.Count + 1, $"Captured White Light Image: {MyDateTime.GetStringDateTime()}");
            ImagesInfoList.Add(imageInfo);

            var imageList = new List<ImageList>();
            var checkingRes = false;
            DebugImageResponse resWhite = new DebugImageResponse();
            var waiting = new WaitingWindow("Waiting for oringinal image processing...\rĐang xử lý hình ảnh gốc...");
            new Task(() =>
            {
                resWhite = APICommunication.DebugImages(_param.ApiUrlAi, img.Mat, _envConfig);
                var dctectImg = Converter.Base64ToBitmap(resWhite.DetectImg);
                var segmentImg = Converter.Base64ToBitmap(resWhite.SegmentImg);
                var finalImg = Converter.Base64ToBitmap(resWhite.FinalImg);
                checkingRes = resWhite.Result;
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

            // Capture UV Light
            if (_selectedCameraName == CameraName.CAM_1)
            {
                if (!await Task.Run(() => PlcController.ControlUv1(_param.ApiUrlCom, true, 1000)))
                {
                    ShowError(
                        "Cannot turn on LED 1! Please check the PLC connection\r" +
                        "Không bật được đèn LED 1, hãy kiểm tra kết nối PLC!");
                    return;
                }
                await Task.Delay(_param.Cam1Exposure + 10);
            }
            else if (_selectedCameraName == CameraName.CAM_2)
            {
                if (!await Task.Run(() => PlcController.ControlUv2(_param.ApiUrlCom, true, 1000)))
                {
                    ShowError(
                        "Cannot turn on LED 2! Please check the PLC connection\r" +
                        "Không bật được đèn LED 2, hãy kiểm tra kết nối PLC!");
                    return;
                }
                await Task.Delay(_param.Cam2Exposure + 10);
            }
            Bitmap bitmapUvImage = new Bitmap(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\APP\test_uv.bmp");
            //Bitmap bitmapUVImage = _selectedCamera.GetBitmap();
            await Task.Run(() => PlcController.ControlUv1(_param.ApiUrlCom, false, 1000));
            await Task.Run(() => PlcController.ControlUv2(_param.ApiUrlCom, false, 1000));
            Image<Bgr, byte> imgUv = new Image<Bgr, byte>(bitmapUvImage);
            UpdateImage(bitmapUvImage);

            var imageUVInfo = new ImageDebugInfo(ImagesInfoList.Count + 1, $"Captured UV Light Image {MyDateTime.GetStringDateTime()}");
            ImagesInfoList.Add(imageUVInfo);

            var checkingUvRes = false;
            waiting = new WaitingWindow("Waiting for UV image processing...\rĐang xử lý hình ảnh UV...");
            new Task(() =>
            {
                var res = APICommunication.DebugUvImages(_param.ApiUrlAi, imgUv.Mat, resWhite.CropBox, resWhite.UvBox1, resWhite.UvBox2, resWhite.Mid1, resWhite.Mid2, _envConfig);
                var thresholdImg = Converter.Base64ToBitmap(res.ThresholdImg);
                var finalImg = Converter.Base64ToBitmap(res.FinalImg);
                checkingUvRes = res.Result;
                imageList = new List<ImageList>()
                {
                    new ImageList(0, "Original UV Image", bitmapUvImage),
                    new ImageList(1, "Threshold UV Image", thresholdImg),
                    new ImageList(1, "Final UV Image", finalImg)
                };
                waiting.KillMe = true;
            }).Start();
            waiting.ShowDialog();

            imageUVInfo.Images = imageList;
            imageUVInfo.Status = checkingUvRes ? (int)FileStatus.OK : (int)FileStatus.NG;
            SelectedImageInfo = imageUVInfo;
        }

        private void btnSetting_Click(object sender, RoutedEventArgs e)
        {
            var settingWindow = new ParamsWindow(this, _envConfig);
            settingWindow.ShowDialog();
        }

        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var warning = ShowWarning("Are you sure to save settings?\rBạn có chắc muốn lưu lại params mới?");
            var resSaveConfig = false;
            var resRestart = false;
            if (warning == true)
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
                ShowInfo("Save params successfully!\rLưu params thành công!");
                CanSave = false;
                OnPropertyChanged(nameof(CanSave));
            }
            else if (!resSaveConfig)
            {
                ShowError("Save params failed!\rLưu params không thành công!");
            }
            else
            {
                ShowError("Restart AI service failed!\rKhông khởi động lại được AI, lưu lại params cũ!");
                _backupConfig.Save();
            }   
        }

        private bool UpdateEnvConfig()
        {
            try
            {
                _envConfigRaw.Set("DISK_POINT_DETECT_CONF_THRESH", _envConfig.DetectThreshold.ToString());
                _envConfigRaw.Set("DISK_POINT_DETECT_IOU_THRESH", _envConfig.DetectIou.ToString());
                _envConfigRaw.Set("DISK_SEGMENT_CONF_THRESH", _envConfig.SegmentThreshold.ToString());
                _envConfigRaw.Set("DISK_SEGMENT_IOU_THRESH", _envConfig.SegmentIou.ToString());
                _envConfigRaw.Set("CALIPER_MIN_EDGE_DISTANCE", _envConfig.CaliperMinEdgeDistance.ToString());
                _envConfigRaw.Set("CALIPER_MAX_EDGE_DISTANCE", _envConfig.CaliperMaxEdgeDistance.ToString());
                _envConfigRaw.Set("CALIPER_LENGTH_RATE", _envConfig.CaliperLengthRate.ToString());
                _envConfigRaw.Set("CALIPER_THICKNESS_LIST", string.Join(",", _envConfig.CaliperThicknessList));
                _envConfigRaw.Set("NUM_DISK", _envConfig.DiskNumber.ToString());
                _envConfigRaw.Set("MAX_DISK_DISTANCE", _envConfig.DiskMaxDistance.ToString());
                _envConfigRaw.Set("MIN_DISK_DISTANCE", _envConfig.DiskMinDistance.ToString());
                _envConfigRaw.Set("MIN_DISK_AREA", _envConfig.DiskMinArea.ToString());
                _envConfigRaw.Set("UV_DISK_THRESHOLD", _envConfig.UvThreshold.ToString());
                _envConfigRaw.Set("UV_MIN_DISK_AREA", _envConfig.UvMinArea.ToString());

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
                    PlcController.ControlLed1(_param.ApiUrlCom, status: false);
                    PlcController.ControlLed2(_param.ApiUrlCom, status: false);
                    PlcController.ControlUv1(_param.ApiUrlCom, status: false);
                    PlcController.ControlUv2(_param.ApiUrlCom, status: false);
                }).Start();
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
                        resConnection = PlcController.ConnectPlc(_param.ApiUrlCom, _param.PlcIp, _param.PlcPort);
                        waiting.KillMe = true;
                    }).Start();
                    waiting.ShowDialog();

                    if (!resConnection)
                    {
                        ShowError("Cannot connect to PLC, please check the PLC connection settings!\rKhông thể kết nối PLC, hãy kiểm tra setting kết nối PLC!");
                        return;
                    }
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
            // Debug
            //{
            //    CanCapture = true;
            //    OnPropertyChanged(nameof(CanCapture));
            //    return;
            //}


            var cameraName = cbbCamera.SelectedValue.ToString();
            if (_selectedCamera !=null && _selectedCamera.IsOpen())
            {
                _selectedCamera.Stop();
            }
            var waiting = new WaitingWindow("Connecting to camera...\rĐang kết nối đến...");
            new Task(() =>
            {
                if (cameraName == "CAM 1")
                {
                    _selectedCamera = _cameraManager.GetCamera1();
                    _selectedCameraName = CameraName.CAM_1;
                }
                else
                {
                    _selectedCamera = _cameraManager.GetCamera2();
                    _selectedCameraName = CameraName.CAM_2;
                }
                waiting.KillMe = true;
            }).Start();
            waiting.ShowDialog();

            if (_selectedCamera == null || (!_selectedCamera.Start() && !_selectedCamera.IsOpen()))
            {
                ShowError($"Cannot connect to Camera {cameraName}, please check the Camera connection settings!\rKhông thể kết nối Camera {cameraName}, hãy kiểm tra setting kết nối Camera!");
            }
            else
            {
                CanCapture = true;
                OnPropertyChanged(nameof(CanCapture));
            }
        }
        #region Show Dialogs
        public bool ShowWarning(string content)
        {
            var res = false;
            this.Dispatcher.Invoke(new Action(() =>
            {
                var box = new WarningWindow(content);
                box.ShowDialog();
                res = (bool)box.DialogResult;
            }));
            return res;
        }
        public void ShowError(string content)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                ErrorService.ShowError(content);
            }));
        }
        public void ShowInfo(string content)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                var box = new InformationWindow(content);
                box.ShowDialog();
            }));
        }
        #endregion

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_selectedCamera != null && _selectedCamera.IsOpen())
            {
                _selectedCamera.Stop();
            }
        }
    }
}
