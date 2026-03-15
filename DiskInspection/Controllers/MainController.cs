using DiskInspection.Controllers.APIs;
using DiskInspection.Controllers.Camera;
using DiskInspection.Models;
using DiskInspection.Utils;
using Emgu.CV.Structure;
using Emgu.CV;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DiskInspection.Controllers.PLC;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Emgu.CV.UI;
using System.IO;
using DiskInspection.Security;
using DiskInspection.Views.ActivationWindows;

namespace DiskInspection.Controllers
{
    class MainController
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private Properties.Settings _param = Properties.Settings.Default;
        private MainWindow _mainWindow;
        public bool _serviceIsRun = false;
        private bool _ForceStopProcess;
        private CameraManager _cameraManager;
        private LincolnCamera _camera1;
        private LincolnCamera _camera2;
        private System.Timers.Timer _statusTimer;
        private System.Timers.Timer _plcTimer;
        private readonly object _cam1WhiteOriginLock = new object();
        private readonly object _cam1WhiteResultLock = new object();
        private readonly object _cam1UvOriginLock = new object();
        private readonly object _cam1UvResultLock = new object();
        private readonly object _cam2WhiteOriginLock = new object();
        private readonly object _cam2WhiteResultLock = new object();
        private readonly object _cam2UvOriginLock = new object();
        private readonly object _cam2UvResultLock = new object();

        private readonly object _statusLock = new object();
        private readonly object _plcLock = new object();

        private BitmapSource _cam1LastWhiteBitmap;
        private BitmapSource _cam1LastWhiteResultBitmap;
        private BitmapSource _cam1LastUvBitmap;
        private BitmapSource _cam1LastUvResultBitmap;
        private BitmapSource _cam2LastWhiteBitmap;
        private BitmapSource _cam2LastWhiteResultBitmap;
        private BitmapSource _cam2LastUvBitmap;
        private BitmapSource _cam2LastUvResultBitmap;

        private CancellationTokenSource _inspectCts;
        private bool _isRunning = false;

        public MainController(MainWindow window)
        {
            _mainWindow = window;
        }
        #region Initialize Program
        public bool RunServiceAsync(int timeout, string content)
        {
            _mainWindow.SetLoadingService(content);
            _logger.Info("Start Service");
            AppLogger.Instance.Info("Loading Program...", "SYSTEM");
            AIServiceController.CloseProcessExisting();
            AIServiceController.Start();

            var timeStep = timeout / 1000;
            for (int i = 0; i < timeStep; i++)
            {
                Thread.Sleep(1000);
                if (APICommunication.CheckAPIStatus(_param.ApiUrlAi, 200))
                {
                    _logger.Info("Start AI Engine Successfuly!");
                    AppLogger.Instance.Info("Loaded Program Successfuly!", "SYSTEM");
                    _serviceIsRun = true;
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Start Program
        public bool Start()
        {
            _logger.Info("Starting inspection...");
            _ForceStopProcess = false;
            if (CheckAndStartCamera() && CheckAndStartPLC() && CheckAndStartAI())
            {
                _logger.Debug("Cameras, PLC and AI are ready, Ready for inspection...");
                AppLogger.Instance.Info("Cameras, PLC and AI are ready, Ready for inspection...", "SYSTEM");
                _isRunning = true;
                _inspectCts = new CancellationTokenSource();
                StartStatusTimer();
                StartPlcTimer();
                return true;
            }
            else
            {
                _logger.Error("Cameras, PLC and AI are not ready, Stop inspection...");
                AppLogger.Instance.Error("Cameras, PLC and AI are not ready, Stop inspection...", "SYSTEM");
                return false;
            }
        }
        #endregion

        #region Check Condition + Initialize
        private bool CheckAndStartAI()
        {
            if (!APICommunication.CheckAPIStatus(_param.ApiUrlAi))
            {
                var res = _mainWindow.ShowWarning($"AI engine is not running, proceed to restart?\nAI engine đang không chạy, bạn muốn khởi động lại AI engine?!");
                var resRestart = RunServiceAsync(20000, "Restarting AI engine...");
                if (!resRestart)
                {
                    _mainWindow.ShowError("Restart AI engine fail, please contact the vendor!\r AI engine khởi động thất bại, hãy liên hệ với vendor!");
                    return false;
                }
            }
            return true;
        }

        private bool CheckAndStartCamera()
        {
            return true;
            _cameraManager = CameraManager.GetInstance();
            _camera1 = _cameraManager.GetCamera1();
            _camera2 = _cameraManager.GetCamera2();
            if (!_camera1.IsOpen())
            {
                _mainWindow.ShowError(string.Format("Không mở được camera 1 với SN {0}\nCan't open 1 camera with SN:{0}", _param.Cam1Sn));
                return false;
            }
            if (!_camera2.IsOpen())
            {
                _mainWindow.ShowError(string.Format("Không mở được camera 2 với SN {0}\nCan't open 2 camera with SN:{0}", _param.Cam2Sn));
                return true;
            }
            _camera1.SetExposureTime(_param.Cam1Exposure);
            _camera2.SetExposureTime(_param.Cam2Exposure);
            _camera1.Start();
            _camera2.Start();
            return true;
        }
        private bool CheckAndStartPLC()
        {
            return true;
            if (!PlcController.CheckPlcConnection(_param.ApiUrlCom))
            {
                var resConnection = PlcController.ConnectPlc(_param.ApiUrlCom, _param.PlcIp, _param.PlcPort);
                if (!resConnection)
                {
                    _mainWindow.ShowError("Không kết nối được với PLC, hãy kiểm tra kết nối\nCannot connect to PLC! Please check the connection");
                    return false;
                }
            }
            return true;
        }

        #endregion

        #region PLC Timer
        private void StartPlcTimer()
        {
            if (_plcTimer != null) return;
            _plcTimer = new System.Timers.Timer(50);
            _plcTimer.Elapsed += PlcTimer_Elapsed;
            _plcTimer.AutoReset = true;
            _plcTimer.Enabled = true;
        }

        private async void PlcTimer_Elapsed(object sender, EventArgs e)
        {
            if (!_isRunning || _inspectCts.IsCancellationRequested)
                return;

            StopPlcTimer();

            var (resTrigger, status) = PlcController.CheckTrigger(_param.ApiUrlCom, 1000);
            if (resTrigger == TriggerState.Error)
            {
                _mainWindow.ShowError(
                    "Cannot connect to PLC to read trigger! Please check the connection\r " +
                    "Không kết nối được với PLC để đọc trigger, hãy kiểm tra kết nối!");
                AppLogger.Instance.Error(
                    "Cannot connect to PLC to read trigger! Please check the connection\r " +
                    "Không kết nối được với PLC để đọc trigger, hãy kiểm tra kết nối!", "PLC");
                return;

            }
            if (resTrigger == TriggerState.Ok && !status)
            {
                return;
            }
            AppLogger.Instance.Info("Trigger received, start inspection...", "PLC");
            _mainWindow.UpdateInspectingMode();
            // Trigger OK
            // --- reset trigger first
            var resResetTg = PlcController.ResetTrigger(_param.ApiUrlCom, 1000);
            if (!resResetTg)
            {
                _mainWindow.ShowError(
                    "Cannot reset trigger! Please check the PLC connection\r" +
                    "Không reset được trigger, hãy kiểm tra kết nối PLC!");
                AppLogger.Instance.Error(
                    "Cannot reset trigger! Please check the PLC connection\r" +
                    "Không reset được trigger, hãy kiểm tra kết nối PLC!", "PLC");
                return;
            }

            // --- start inspection
            var token = _inspectCts.Token;
            App.ImageViewer.ClearImages();
            try
            {
                var results = await Task.WhenAll(
               InpsectCamera1Async(token),
               InpsectCamera2Async(token));

                if (token.IsCancellationRequested)
                    return;

                var cam1Result = results[0];
                var cam2Result = results[1];

                _mainWindow.UpdateInspectionStatusCam1(cam1Result.status);
                _mainWindow.UpdateCam1ProcessedTime(cam1Result.time);
                _mainWindow.UpdateInspectionStatusCam2(cam2Result.status);
                _mainWindow.UpdateCam2ProcessedTime(cam2Result.time);

                var totalStatus = cam1Result.status && cam2Result.status;
                _mainWindow.UpdateInspectionStatus(totalStatus);
                _mainWindow.UpdateStatistics(totalStatus);
                var timeStamp = _mainWindow.UpdateTimeStamp();

                if (!totalStatus)
                {
                    PlcController.OnError(_param.ApiUrlCom);
                    AppLogger.Instance.Info("Inspection NG, sent NG signal", "PLC");
                    App.ImageViewer.ShowFirstErrorImage();
                }

                AppLogger.Instance.Info("Inspection completed. Waiting for trigger...", "SYSTEM");

                // Save images if enabled
                if (_param.SaveEnable)
                {
                    Task task = Task.Run(() => SaveImage(timeStamp));
                }
            }
            catch (OperationCanceledException)
            {
                AppLogger.Instance.Info("Inspection cancelled.", "SYSTEM");
            }
            finally
            {
                if (_isRunning)
                    StartPlcTimer();
            }
        }

        private async Task<(bool status, List<string> errors, TimeSpan time)> InpsectCamera1Async(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            AppLogger.Instance.Info("Start inspecting camera 1...", "CAM1");
            try
            {
                bool totalStatus = true;

                token.ThrowIfCancellationRequested();
                List<string> errors = new List<string>();

                // ================= WHITE LIGHT =================
                if (!await Task.Run(() => PlcController.ControlLed1(_param.ApiUrlCom, true, 1000)))
                {
                    _mainWindow.ShowError(
                        "Cannot turn on LED 1! Please check the PLC connection\r" +
                        "Không bật được đèn LED 1, hãy kiểm tra kết nối PLC!");
                    AppLogger.Instance.Error(
                        "Cannot turn on LED 1! Please check the PLC connection\r" +
                        "Không bật được đèn LED 1, hãy kiểm tra kết nối PLC!", "PLC");
                    return (false, null, sw.Elapsed);
                }

                await Task.Delay(_param.Cam1Exposure + 10);

                Bitmap frameWhite = await Task.Run(() => _camera1.GetBitmap());
                //Bitmap frameWhite = new Bitmap(@"C:\Vision\test_white_ok.bmp");
                AppLogger.Instance.Info("Captured image from camera 1 with white light.", "CAM1");

                await Task.Run(() => PlcController.ControlLed1(_param.ApiUrlCom, false, 1000));

                lock (_cam1WhiteOriginLock)
                {
                    _cam1LastWhiteBitmap = Converter.BitmapToBitmapSource((Bitmap)frameWhite.Clone());
                    App.ImageViewer.AddImage(_cam1LastWhiteBitmap, "1-White-Origin", ThumbStatus.Origin, "Camera 1 - White Light - Original Image");
                }
                _mainWindow.UpdateCam1WhiteOrigin(_cam1LastWhiteBitmap);   // 🔥 update NGAY

                var sw2 = Stopwatch.StartNew();

                token.ThrowIfCancellationRequested();
                var resWhite = await Task.Run(() =>
                    APICommunication.InspectWhiteLight(
                        _param.ApiUrlAi,
                        new Image<Bgr, byte>(frameWhite).Mat,
                        10000));

                AppLogger.Instance.Info("Call API inspection: " + sw2.ElapsedMilliseconds + "ms", "CAM1");

                if (resWhite == null)
                {
                    totalStatus = false;
                    _mainWindow.ShowError(
                        "Cannot run AI inspection! Please check the AI engine\r" +
                        "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
                    AppLogger.Instance.Error(
                        "Cannot run AI inspection! Please check the AI engine\r" +
                        "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!", "CAM1 AI");
                    return (false, null,  sw.Elapsed);
                }
                else
                {
                    token.ThrowIfCancellationRequested();
                    lock (_cam1WhiteResultLock)
                    {
                        _cam1LastWhiteResultBitmap = Converter.Base64ToBitmapSource(resWhite.ResImg);
                        
                    }
                    _mainWindow.UpdateCam1WhiteResult(_cam1LastWhiteResultBitmap); // 🔥 update NGAY
                    _mainWindow.UpdateCam1MinMaxDis(resWhite.MinDiskDistance, resWhite.MaxDiskDistance);
                    if (!resWhite.Result)
                    {
                        App.ImageViewer.AddImage(_cam1LastWhiteResultBitmap, "1-White-Result", ThumbStatus.Ng, $"CAM 1 - White Light - NG: {resWhite.ErrorDesc}");
                        totalStatus = false;
                    }
                    else
                    {
                        App.ImageViewer.AddImage(_cam1LastWhiteResultBitmap, "1-White-Result", ThumbStatus.Ok, $"CAM 1 - White Light - OK: {resWhite.ErrorDesc}");
                    }
                        
                    AppLogger.Instance.Info("AI inspection for camera 1 with white light completed.", "CAM1 AI");
                }

                frameWhite.Dispose();
                await Task.Yield(); // 👈 nhường UI render

                // ================= UV LIGHT =================

                token.ThrowIfCancellationRequested();
                if (!await Task.Run(() => PlcController.ControlUv1(_param.ApiUrlCom, true, 1000)))
                {
                    _mainWindow.ShowError(
                        "Cannot turn on UV light! Please check the PLC connection\r" +
                        "Không bật được đèn UV, hãy kiểm tra kết nối PLC!");
                    AppLogger.Instance.Error(
                        "Cannot turn on UV light! Please check the PLC connection\r" +
                        "Không bật được đèn UV, hãy kiểm tra kết nối PLC!", "PLC");
                    return (false, null, sw.Elapsed);
                }

                await Task.Delay(_param.Cam1Exposure + 10);

                Bitmap frameUv = await Task.Run(() => _camera1.GetBitmap());
                //Bitmap frameUv = new Bitmap(@"C:\Vision\test_uv.bmp");
                AppLogger.Instance.Info("Captured image from camera 1 with UV light.", "CAM1");

                await Task.Run(() => PlcController.ControlUv1(_param.ApiUrlCom, false, 1000));

                lock (_cam1UvOriginLock)
                {
                    _cam1LastUvBitmap = Converter.BitmapToBitmapSource((Bitmap)frameUv.Clone());
                    App.ImageViewer.AddImage(_cam1LastUvBitmap, "1-UV-Origin", ThumbStatus.Origin, "Camera 1 - UV Light - Original Image");
                }

                // Update result to UI
                _mainWindow.UpdateCam1UvOrigin(_cam1LastUvBitmap); // 🔥 update NGAY

                token.ThrowIfCancellationRequested();
                var resUv = await Task.Run(() =>
                    APICommunication.InspectUvLight(
                        _param.ApiUrlAi,
                        new Image<Bgr, byte>(frameUv).Mat,
                        resWhite.CropBox,
                        resWhite.UvBox1,
                        resWhite.UvBox2,
                        resWhite.Mid1,
                        resWhite.Mid2,
                        10000));

                if (resUv == null)
                {
                    totalStatus = false;
                    _mainWindow.ShowError(
                        "Cannot run AI inspection! Please check the AI engine\r" +
                        "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
                    AppLogger.Instance.Error(
                        "Cannot run AI inspection! Please check the AI engine\r" +
                        "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!", "CAM1 AI");
                    return (false, null, sw.Elapsed);
                }
                else
                {
                    token.ThrowIfCancellationRequested();
                    lock (_cam1UvResultLock)
                    {
                        _cam1LastUvResultBitmap = Converter.Base64ToBitmapSource(resUv.ResImg);
                    }
                    _mainWindow.UpdateCam1UvResult(_cam1LastUvResultBitmap);
                    _mainWindow.UpdateCam1DiskUv(resUv.CountUvDisk);
                    if (!resUv.Result)
                    {
                        App.ImageViewer.AddImage(_cam1LastUvResultBitmap, "1-UV-Result", ThumbStatus.Ng, $"CAM 1 - UV Light - NG: {resUv.ErrorDesc}");
                        totalStatus = false;
                    }
                    else
                    {
                        App.ImageViewer.AddImage(_cam1LastUvResultBitmap, "1-UV-Result", ThumbStatus.Ok, $"CAM 1 - UV Light - OK: {resUv.ErrorDesc}");
                    }

                    AppLogger.Instance.Info("AI inspection for camera 1 with UV light completed.", "CAM1 AI");
                }

                frameUv.Dispose();
                return (totalStatus, errors, sw.Elapsed);

            }
            finally
            {
                sw.Stop();
            }
        }
        private async Task<(bool status, List<string> errors, TimeSpan time)> InpsectCamera2Async(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            AppLogger.Instance.Info("Start inspecting camera 2...", "CAM2");
            try
            {
                bool totalStatus = true;

                token.ThrowIfCancellationRequested();

                List<string> errors = new List<string>();

                // ================= WHITE LIGHT =================
                if (!await Task.Run(() => PlcController.ControlLed2(_param.ApiUrlCom, true, 1000)))
                {
                    _mainWindow.ShowError(
                        "Cannot turn on LED 2! Please check the PLC connection\r" +
                        "Không bật được đèn LED 2, hãy kiểm tra kết nối PLC!");
                    AppLogger.Instance.Error(
                        "Cannot turn on LED 2! Please check the PLC connection\r" +
                        "Không bật được đèn LED 2, hãy kiểm tra kết nối PLC!", "PLC");
                    return (false, null, sw.Elapsed);
                }

                await Task.Delay(_param.Cam2Exposure + 10);

                Bitmap frameWhite = await Task.Run(() => _camera2.GetBitmap());
                //Bitmap frameWhite = new Bitmap(@"C:\Vision\test_white.bmp");
                AppLogger.Instance.Info("Captured image from camera 2 with white light.", "CAM2");

                await Task.Run(() => PlcController.ControlLed2(_param.ApiUrlCom, false, 1000));

                lock (_cam2WhiteOriginLock)
                {
                    _cam2LastWhiteBitmap = Converter.BitmapToBitmapSource((Bitmap)frameWhite.Clone());
                    App.ImageViewer.AddImage(_cam2LastWhiteBitmap, "2-White-Origin", ThumbStatus.Origin, "Camera 2 - White Light - Original Image");
                }
                _mainWindow.UpdateCam2WhiteOrigin(_cam2LastWhiteBitmap);   // 🔥 update NGAY

                token.ThrowIfCancellationRequested();
                var resWhite = await Task.Run(() =>
                    APICommunication.InspectWhiteLight(
                        _param.ApiUrlAi,
                        new Image<Bgr, byte>(frameWhite).Mat,
                        10000));


                if (resWhite == null)
                {
                    totalStatus = false;
                    _mainWindow.ShowError(
                        "Cannot run AI inspection! Please check the AI engine\r" +
                        "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
                    AppLogger.Instance.Error(
                        "Cannot run AI inspection! Please check the AI engine\r" +
                        "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!", "CAM2 AI");
                    return (false, null, sw.Elapsed);
                }
                else
                {
                    token.ThrowIfCancellationRequested();
                    lock (_cam2WhiteResultLock)
                    {
                        _cam2LastWhiteResultBitmap = Converter.Base64ToBitmapSource(resWhite.ResImg);
                    }
                    _mainWindow.UpdateCam2WhiteResult(_cam2LastWhiteResultBitmap); // 🔥 update NGAY
                    _mainWindow.UpdateCam2MinMaxDis(resWhite.MinDiskDistance, resWhite.MaxDiskDistance);
                    if (!resWhite.Result)
                    {
                        App.ImageViewer.AddImage(_cam2LastWhiteResultBitmap, "2-White-Result", ThumbStatus.Ng, $"CAM 2 - White Light - NG: {resWhite.ErrorDesc}");
                        totalStatus = false;
                    }
                    else
                    {
                        App.ImageViewer.AddImage(_cam2LastWhiteResultBitmap, "2-White-Result", ThumbStatus.Ok, $"CAM 2 - White Light - OK: {resWhite.ErrorDesc}");
                    }

                    AppLogger.Instance.Info("AI inspection for camera 2 with white light completed.", "CAM2 AI");
                }

                frameWhite.Dispose();
                await Task.Yield(); // 👈 nhường UI render

                // ================= UV LIGHT =================
                token.ThrowIfCancellationRequested();
                if (!await Task.Run(() => PlcController.ControlUv2(_param.ApiUrlCom, true, 1000)))
                {
                    _mainWindow.ShowError(
                        "Cannot turn on UV light! Please check the PLC connection\r" +
                        "Không bật được đèn UV, hãy kiểm tra kết nối PLC!");
                    AppLogger.Instance.Error(
                        "Cannot turn on UV light! Please check the PLC connection\r" +
                        "Không bật được đèn UV, hãy kiểm tra kết nối PLC!", "PLC");
                    return (false, null, sw.Elapsed);
                }

                await Task.Delay(_param.Cam2Exposure + 10);

                Bitmap frameUv = await Task.Run(() => _camera2.GetBitmap());
                //Bitmap frameUv = new Bitmap(@"C:\Vision\test_uv.bmp");
                AppLogger.Instance.Info("Captured image from camera 2 with UV light.", "CAM2");

                await Task.Run(() => PlcController.ControlUv2(_param.ApiUrlCom, false, 1000));

                lock (_cam2UvOriginLock)
                {
                    _cam2LastUvBitmap = Converter.BitmapToBitmapSource((Bitmap)frameUv.Clone());
                    App.ImageViewer.AddImage(_cam2LastUvBitmap, "2-Uv-Origin", ThumbStatus.Origin, "Camera 2 - UV Light - Original Image");
                }
                _mainWindow.UpdateCam2UvOrigin(_cam2LastUvBitmap); // 🔥 update NGAY

                token.ThrowIfCancellationRequested();
                var resUv = await Task.Run(() =>
                    APICommunication.InspectUvLight(
                        _param.ApiUrlAi,
                        new Image<Bgr, byte>(frameUv).Mat,
                        resWhite.CropBox,
                        resWhite.UvBox1,
                        resWhite.UvBox2,
                        resWhite.Mid1,
                        resWhite.Mid2,
                        10000));

                if (resUv == null)
                {
                    totalStatus = false;
                    _mainWindow.ShowError(
                        "Cannot run AI inspection! Please check the AI engine\r" +
                        "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
                    AppLogger.Instance.Error(
                        "Cannot run AI inspection! Please check the AI engine\r" +
                        "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!", "CAM2 AI");
                    return (false, null, sw.Elapsed);
                }
                else
                {
                    token.ThrowIfCancellationRequested();
                    lock (_cam2UvResultLock)
                    {
                        _cam2LastUvResultBitmap = Converter.Base64ToBitmapSource(resUv.ResImg);
                    }
                    _mainWindow.UpdateCam2UvResult(_cam2LastUvResultBitmap); // 🔥 update NGAY
                    _mainWindow.UpdateCam2DiskUv(resUv.CountUvDisk);
                    if (!resUv.Result)
                    {
                        App.ImageViewer.AddImage(_cam2LastUvResultBitmap, "2-Uv-Result", ThumbStatus.Ng, $"CAM 2 - UV Light - NG: {resUv.ErrorDesc}");
                        totalStatus = false;
                    }
                    else
                    {
                        App.ImageViewer.AddImage(_cam2LastUvResultBitmap, "2-Uv-Result", ThumbStatus.Ok, $"CAM 2 - UV Light - OK: {resUv.ErrorDesc}");
                    }
                        
                    AppLogger.Instance.Info("AI inspection for camera 2 with UV light completed.", "CAM2 AI");

                }

                frameUv.Dispose();

                return (totalStatus, errors, sw.Elapsed);
            }
            finally
            {
                sw.Stop();
            }
        }

        //private (bool status, List<string> errors) InpsectCamera1()
        //{
        //    bool totalStatus = true;
        //    List<string> errors = new List<string>();

        //    #region White Light
        //    // Turn on LED 1
        //    var resLed1 = APICommunication.ControlLed1(_param.ApiUrlCom, true, 1000);
        //    if (!resLed1)
        //    {
        //        _mainWindow.ShowError(
        //            "Cannot turn on LED 1! Please check the PLC connection\r" +
        //            "Không bật được đèn LED 1, hãy kiểm tra kết nối PLC!");
        //        return (false, null);
        //    }

        //    // Capture image
        //    Thread.Sleep(_param.Cam1Exposure + 10);
        //    Bitmap frame = _camera1.GetBitmap();
        //    // Turn off LED 1
        //    APICommunication.ControlLed1(_param.ApiUrlCom, false, 1000);

        //    // Keep origin image
        //    lock (_cam1WhiteOriginLock)
        //    {
        //        _cam1LastWhiteBitmap = Converter.BitmapToBitmapSource((Bitmap)frame.Clone()); 
        //    }
        //    // Update cam 1 white origin image
        //    _mainWindow.UpdateCam1WhiteOrigin(_cam1LastWhiteBitmap);

        //    // Call API
        //    Image<Bgr, byte> openCvImg = new Image<Bgr, byte>(frame);
        //    var resWlInspect = APICommunication.InspectWhiteLight(_param.ApiUrlAi, openCvImg.Mat, 1000);
        //    if (resWlInspect == null)
        //    {
        //        _mainWindow.ShowError(
        //            "Cannot run AI inspection! Please check the AI engine\r" +
        //            "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
        //        return (false, null);
        //    }

        //    // Check response
        //    if (!resWlInspect.Result)
        //    {
        //        totalStatus = false;
        //        errors.Add(resWlInspect.ErrorDesc);
        //    }
        //    else
        //    {
        //        lock (_cam1WhiteResultLock)
        //        {
        //            _cam1LastWhiteResultBitmap = Converter.Base64ToBitmapSource(resWlInspect.ResImg);
        //        }
        //        _mainWindow.UpdateCam1WhiteResult(_cam1LastWhiteResultBitmap);
        //    }

        //    // Dispose temp image
        //    frame.Dispose();
        //    #endregion

        //    // Turn on UV light
        //    var resUv = APICommunication.ControlUv(_param.ApiUrlCom, true, 1000);
        //    if (!resUv)
        //    {
        //        _mainWindow.ShowError(
        //            "Cannot turn on UV light! Please check the PLC connection\r" +
        //            "Không bật được đèn UV, hãy kiểm tra kết nối PLC!");
        //        return (false, null);
        //    }

        //    // Capture image
        //    Thread.Sleep(_param.Cam1Exposure + 10);
        //    Bitmap frame2 = _camera1.GetBitmap();
        //    // Turn off UV light
        //    APICommunication.ControlUv(_param.ApiUrlCom, false, 1000);

        //    // Keep origin image
        //    lock (_cam1UvOriginLock)
        //    {
        //        _cam1LastUvBitmap = Converter.BitmapToBitmapSource((Bitmap)frame2.Clone());
        //    }
        //    // Update cam 1 UV origin image
        //    _mainWindow.UpdateCam1UvOrigin(_cam1LastUvBitmap);

        //    // Call API
        //    Image<Bgr, byte> openCvImg2 = new Image<Bgr, byte>(frame2);
        //    var resUvInspect = APICommunication.InspectUvLight(_param.ApiUrlAi, openCvImg2.Mat, 1000);
        //    if (resUvInspect == null)
        //    {
        //        _mainWindow.ShowError(
        //            "Cannot run AI inspection! Please check the AI engine\r" +
        //            "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
        //        return (false, null);
        //    }
        //    if (!resUvInspect.Result)
        //    {
        //        totalStatus = false;
        //        errors.Add(resWlInspect.ErrorDesc);
        //    }
        //    else
        //    {
        //        lock (_cam1UvResultLock)
        //        {
        //            _cam1LastUvResultBitmap = Converter.Base64ToBitmapSource(resUvInspect.ResImg);
        //        }
        //        _mainWindow.UpdateCam1UvResult(_cam1LastUvResultBitmap);
        //    }
        //    // Dispose temp image
        //    frame2.Dispose();


        //    return (totalStatus, errors);
        //}

        //private (bool status, List<string> errors) InpsectCamera2()
        //{
        //    bool totalStatus = true;
        //    List<string> errors = new List<string>();

        //    #region White Light
        //    // Turn on LED 2
        //    var resLed2 = APICommunication.ControlLed2(_param.ApiUrlCom, true, 1000);
        //    if (!resLed2)
        //    {
        //        _mainWindow.ShowError(
        //            "Cannot turn on LED 2! Please check the PLC connection\r" +
        //            "Không bật được đèn LED 2, hãy kiểm tra kết nối PLC!");
        //        return (false, null);
        //    }

        //    // Capture image
        //    Thread.Sleep(_param.Cam2Exposure + 10);
        //    Bitmap frame = _camera2.GetBitmap();
        //    // Turn off LED 2
        //    APICommunication.ControlLed2(_param.ApiUrlCom, false, 1000);

        //    // Keep origin image
        //    lock (_cam2WhiteOriginLock)
        //    {
        //        _cam2LastWhiteBitmap = Converter.BitmapToBitmapSource((Bitmap)frame.Clone());
        //    }
        //    // Update cam 2 white origin image
        //    _mainWindow.UpdateCam2WhiteOrigin(_cam2LastWhiteBitmap);

        //    // Call API
        //    Image<Bgr, byte> openCvImg = new Image<Bgr, byte>(frame);
        //    var resWlInspect = APICommunication.InspectWhiteLight(_param.ApiUrlAi, openCvImg.Mat, 1000);
        //    if (resWlInspect == null)
        //    {
        //        _mainWindow.ShowError(
        //            "Cannot run AI inspection! Please check the AI engine\r" +
        //            "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
        //        return (false, null);
        //    }

        //    // Check response
        //    if (!resWlInspect.Result)
        //    {
        //        totalStatus = false;
        //        errors.Add(resWlInspect.ErrorDesc);
        //    }
        //    else
        //    {
        //        lock (_cam2WhiteResultLock)
        //        {
        //            _cam2LastWhiteResultBitmap = Converter.Base64ToBitmapSource(resWlInspect.ResImg);
        //        }
        //        _mainWindow.UpdateCam2WhiteResult(_cam2LastWhiteResultBitmap);
        //    }

        //    // Dispose temp image
        //    frame.Dispose();
        //    #endregion

        //    // Turn on UV light
        //    var resUv = APICommunication.ControlUv(_param.ApiUrlCom, true, 1000);
        //    if (!resUv)
        //    {
        //        _mainWindow.ShowError(
        //            "Cannot turn on UV light! Please check the PLC connection\r" +
        //            "Không bật được đèn UV, hãy kiểm tra kết nối PLC!");
        //        return (false, null);
        //    }

        //    // Capture image
        //    Thread.Sleep(_param.Cam2Exposure + 10);
        //    Bitmap frame2 = _camera2.GetBitmap();
        //    // Turn off UV light
        //    APICommunication.ControlUv(_param.ApiUrlCom, false, 1000);

        //    // Keep origin image
        //    lock (_cam2UvOriginLock)
        //    {
        //        _cam2LastUvBitmap = Converter.BitmapToBitmapSource((Bitmap)frame2.Clone());
        //    }
        //    // Update cam 2 UV origin image
        //    _mainWindow.UpdateCam2UvOrigin(_cam2LastUvBitmap);

        //    // Call API
        //    Image<Bgr, byte> openCvImg2 = new Image<Bgr, byte>(frame2);
        //    var resUvInspect = APICommunication.InspectUvLight(_param.ApiUrlAi, openCvImg2.Mat, 1000);
        //    if (resUvInspect == null)
        //    {
        //        _mainWindow.ShowError(
        //            "Cannot run AI inspection! Please check the AI engine\r" +
        //            "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
        //        return (false, null);
        //    }
        //    if (!resUvInspect.Result)
        //    {
        //        totalStatus = false;
        //        errors.Add(resWlInspect.ErrorDesc);
        //    }
        //    else
        //    {
        //        lock (_cam2UvResultLock)
        //        {
        //            _cam2LastUvResultBitmap = Converter.Base64ToBitmapSource(resUvInspect.ResImg);
        //        }
        //        _mainWindow.UpdateCam2UvResult(_cam2LastUvResultBitmap);
        //    }
        //    // Dispose temp image
        //    frame2.Dispose();


        //    return (totalStatus, errors);
        //}


        private void StopPlcTimer()
        {
            if (_plcTimer != null)
            {
                _plcTimer.Stop();
                _plcTimer.Elapsed -= PlcTimer_Elapsed;
                _plcTimer.AutoReset = false;
                _plcTimer.Dispose();
                _plcTimer = null;
            }
        }

        private void SaveImage(DateTime timeStamp)
        {
            AppLogger.Instance.Info("Saving images...", "SYSTEM");
            try
            {
                var saveResult = true;
                if (_param.SaveMode == (int)SaveType.ORIGINAL)
                {
                    var res_1 = ImageSaver.SaveImage(_cam1LastWhiteBitmap, _param.SavePath, timeStamp, "Cam1_White_Original");
                    var res_2 = ImageSaver.SaveImage(_cam2LastWhiteBitmap, _param.SavePath, timeStamp, "Cam2_White_Original");
                    var res_3 = ImageSaver.SaveImage(_cam1LastUvBitmap, _param.SavePath, timeStamp, "Cam1_UV_Original");
                    var res_4 = ImageSaver.SaveImage(_cam2LastUvBitmap, _param.SavePath, timeStamp, "Cam2_UV_Original");

                    saveResult = res_1 && res_2 && res_3 && res_4;
                }
                else if (_param.SaveMode == (int)SaveType.RESULT)
                {
                    var res_1 = ImageSaver.SaveImage(_cam1LastWhiteResultBitmap, _param.SavePath, timeStamp, "Cam1_White_Result");
                    var res_2 = ImageSaver.SaveImage(_cam2LastWhiteResultBitmap, _param.SavePath, timeStamp, "Cam2_White_Result");
                    var res_3 = ImageSaver.SaveImage(_cam1LastUvResultBitmap, _param.SavePath, timeStamp, "Cam1_UV_Result");
                    var res_4 = ImageSaver.SaveImage(_cam2LastUvResultBitmap, _param.SavePath, timeStamp, "Cam2_UV_Result");

                    saveResult = res_1 && res_2 && res_3 && res_4;
                }
                else
                {
                    var res_1 = ImageSaver.SaveImage(_cam1LastWhiteBitmap, _param.SavePath, timeStamp, "Cam1_White_Original");
                    var res_2 = ImageSaver.SaveImage(_cam2LastWhiteBitmap, _param.SavePath, timeStamp, "Cam2_White_Original");
                    var res_3 = ImageSaver.SaveImage(_cam1LastUvBitmap, _param.SavePath, timeStamp, "Cam1_UV_Original");
                    var res_4 = ImageSaver.SaveImage(_cam2LastUvBitmap, _param.SavePath, timeStamp, "Cam2_UV_Original");

                    var res_5 = ImageSaver.SaveImage(_cam1LastWhiteResultBitmap, _param.SavePath, timeStamp, "Cam1_White_Result");
                    var res_6 = ImageSaver.SaveImage(_cam2LastWhiteResultBitmap, _param.SavePath, timeStamp, "Cam2_White_Result");
                    var res_7 = ImageSaver.SaveImage(_cam1LastUvResultBitmap, _param.SavePath, timeStamp, "Cam1_UV_Result");
                    var res_8 = ImageSaver.SaveImage(_cam2LastUvResultBitmap, _param.SavePath, timeStamp, "Cam2_UV_Result");

                    saveResult = res_1 && res_2 && res_3 && res_4 && res_5 && res_6 && res_7 && res_8;
                }
                if (saveResult)
                    AppLogger.Instance.Info("Saved images successfuly!", "SYSTEM");
                else 
                    AppLogger.Instance.Error("Save images failed!: Images are empty or save path is not valid!", "SYSTEM");
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(@"Save images failed: " + ex.Message, "SYSTEM");
            }
        }

        #endregion

        #region Status Timer
        public void StartStatusTimer()
        {
            if (_statusTimer != null) return;
            _statusTimer = new System.Timers.Timer(2000);
            _statusTimer.Elapsed += StatusTimer_Elapsed;
            _statusTimer.AutoReset = true;
            _statusTimer.Enabled = true;
        }

        private void StatusTimer_Elapsed(object sender, EventArgs e)
        {
            lock (_statusLock)
            {
                StopStatusTimer();
                var resAI = APICommunication.CheckAPIStatus(_param.ApiUrlAi, timeout: 100);
                var resPLC = PlcController.CheckPlcConnection(_param.ApiUrlCom, timeout: 100);
                var resCamera1 = _camera1 != null && _camera1.IsOpen();
                var resCamera2 = _camera2 != null && _camera2.IsOpen();
                _mainWindow.SetStatusService(resAI, resPLC, resCamera1, resCamera2);
                StartStatusTimer();
            }
        }
        public void StopStatusTimer()
        {
            if (_statusTimer != null)
            {
                _statusTimer.Stop();
                _statusTimer.Elapsed -= StatusTimer_Elapsed;
                _statusTimer.AutoReset = false;
                _statusTimer.Dispose();
                _statusTimer = null;
            }
        }
        #endregion

        #region Stop Program
        public void Stop()
        {
            if (!_isRunning)
                return;

            PlcController._firstTrigger = true;
            _isRunning = false;

            StopPlcTimer();
            StopStatusTimer();

            if (_inspectCts != null && !_inspectCts.IsCancellationRequested)
                _inspectCts.Cancel();

            // đảm bảo tắt hết đèn
            PlcController.ControlLed1(_param.ApiUrlCom, false, 500);
            PlcController.ControlLed2(_param.ApiUrlCom, false, 500);
            PlcController.ControlUv1(_param.ApiUrlCom, false, 500);
            PlcController.ControlUv2(_param.ApiUrlCom, false, 500);
            // close hết cameras
            if (_camera1 != null)
                _camera1.Stop();
            if (_camera2 != null)
                _camera2.Stop();

            _logger.Info("User stopped system.");
            AppLogger.Instance.Info("User stopped system.", "SYSTEM");
        }
        #endregion

        #region Close Program
        internal void CloseAIService()
        {
            AIServiceController.CloseProcessExisting();
            _serviceIsRun = false;
        }
        internal void CloseCamera()
        {
            if (_camera1 != null && _camera1.IsOpen())
            {
                _camera1.Close();
            }
            if (_camera2 != null && _camera2.IsOpen())
            {
                _camera2.Close();
            }
        }

        internal void ShutdownLight()
        {
            PlcController.ControlLed1(_param.ApiUrlCom, false);
            PlcController.ControlLed2(_param.ApiUrlCom, false);
            PlcController.ControlUv1(_param.ApiUrlCom, false);
            PlcController.ControlUv2(_param.ApiUrlCom, false);

        }

        internal bool CheckLicense()
        {
            string licensePath = @"plugin\license.dat";
            var error = "License is not valid, contact with vendor to active!\rLicense không hợp lệ, liên hệ với vendor để active!";
            var info = "Activation key is valid, continue to use!\rActivation key hợp lệ, hãy tiếp tục sử dụng chương trình!";
            var res = false;
            if (!File.Exists(licensePath))
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    var win = new ActivationWindow();
                    win.Topmost = true;

                    if (win.ShowDialog() != true)
                    {
                        AppLogger.Instance.Error("License is not valid!", "SYSTEM");
                        _mainWindow.ShowError(error);
                    }
                    else
                    {
                        AppLogger.Instance.Info("License is valid!", "SYSTEM");
                        _mainWindow.ShowInfo(info);
                        res = true;
                    }
                });
            }
            else
            {
                string key = File.ReadAllText(licensePath);
                (bool isValid, string message) = LicenseManager.ValidateActivationKey(key);
                if (!isValid)
                {
                    AppLogger.Instance.Error(message, "SYSTEM");
                    _mainWindow.ShowError(error);

                    AppLogger.Instance.Info("Processing creating new activation key!", "SYSTEM");

                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        var win = new ActivationWindow();
                        win.Topmost = true;
                        if (win.ShowDialog() != true)
                        {
                            AppLogger.Instance.Error("License is not valid!", "SYSTEM");
                            _mainWindow.ShowError(error);
                        }

                        else
                        {
                            AppLogger.Instance.Info("License is valid!", "SYSTEM");
                            _mainWindow.ShowInfo(info);
                            res = true;
                        }
                    });
                }
                else
                {
                    AppLogger.Instance.Info("License is valid!", "SYSTEM");
                    res = true;
                }
            }
            return res;
        }
        #endregion
    }
}
