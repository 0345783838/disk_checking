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
        private DispatcherTimer _statusTimer;
        private DispatcherTimer _plcTimer;
        private readonly object _cam1WhiteOriginLock = new object();
        private readonly object _cam1WhiteResultLock = new object();
        private readonly object _cam1UvOriginLock = new object();
        private readonly object _cam1UvResultLock = new object();
        private readonly object _cam2WhiteOriginLock = new object();
        private readonly object _cam2WhiteResultLock = new object();
        private readonly object _cam2UvOriginLock = new object();
        private readonly object _cam2UvResultLock = new object();

        private BitmapSource _cam1LastWhiteBitmap;
        private BitmapSource _cam1LastWhiteResultBitmap;
        private BitmapSource _cam1LastUvBitmap;
        private BitmapSource _cam1LastUvResultBitmap;
        private BitmapSource _cam2LastWhiteBitmap;
        private BitmapSource _cam2LastWhiteResultBitmap;
        private BitmapSource _cam2LastUvBitmap;
        private BitmapSource _cam2LastUvResultBitmap;

        public MainController(MainWindow window)
        {
            _mainWindow = window;
        }
        public bool RunServiceAsync(int timeout, string content)
        {
            _mainWindow.SetLoadingService(content);
            _logger.Info("Start Service");
            AIServiceController.CloseProcessExisting();
            AIServiceController.Start();

            var timeStep = timeout / 1000;
            for (int i = 0; i < timeStep; i++)
            {
                Thread.Sleep(1000);
                if (CheckAPIStatus())
                {
                    _logger.Info("Start AI Python Engine Successfuly!");
                    _serviceIsRun = true;
                    return true;
                }
            }
            return false;
        }
        public bool CheckAPIStatus()
        {
            return APICommunication.CheckAPIStatus(_param.ApiUrlAi, 1000);
        }

        internal void CloseAIService()
        {
            AIServiceController.CloseProcessExisting();
            _serviceIsRun = false;
        }

        internal void Start()
        {
            _logger.Info("Starting inspection...");
            _ForceStopProcess = false;
            if (CheckAndStartCamera() && CheckAndStartPLC() && CheckAndStartAI())
            {
                _logger.Debug("Cameras, PLC and AI are ready, Ready for inspection...");
                StartStatusTimer();
                StartPlcTimer();
            }
        }

        #region Status PLC
        private void StartPlcTimer()
        {
            if (_plcTimer != null) return;
            _plcTimer = new DispatcherTimer();
            _plcTimer.Interval = TimeSpan.FromSeconds(0.5);
            _plcTimer.Tick += PlcTimer_Tick;
            _plcTimer.Start();
        }

        private async void PlcTimer_Tick(object sender, EventArgs e)
        {
            StopPlcTimer();
            var (resTrigger, status) = APICommunication.CheckTrigger(_param.ApiUrlCom, 1000);
            if (resTrigger == TriggerState.ERROR)
            {
                _mainWindow.ShowError(
                    "Cannot connect to PLC to read trigger! Please check the connection\r " +
                    "Không kết nối được với PLC để đọc trigger, hãy kiểm tra kết nối!");
                return;
            }
            if (resTrigger == TriggerState.OK)
            {
                if (!status)
                    return;
            }

            // Trigger OK
            // --- reset trigger first
            var resResetTg = APICommunication.ResetTrigger(_param.ApiUrlCom, 1000);
            if (!resResetTg)
            {
                _mainWindow.ShowError(
                    "Cannot reset trigger! Please check the PLC connection\r" +
                    "Không reset được trigger, hãy kiểm tra kết nối PLC!");
                return;
            }

            // --- start inspection

            var cam1Task = Task.Run(() => InpsectCamera1());
            var cam2Task = Task.Run(() => InpsectCamera2());

            await Task.WhenAll(cam1Task, cam2Task);



        }
        private (bool status, List<string> errors) InpsectCamera1()
        {
            bool totalStatus = true;
            List<string> errors = new List<string>();

            #region White Light
            // Turn on LED 1
            var resLed1 = APICommunication.ControlLed1(_param.ApiUrlCom, true, 1000);
            if (!resLed1)
            {
                _mainWindow.ShowError(
                    "Cannot turn on LED 1! Please check the PLC connection\r" +
                    "Không bật được đèn LED 1, hãy kiểm tra kết nối PLC!");
                return (false, null);
            }

            // Capture image
            Thread.Sleep(_param.Cam1Exposure + 10);
            Bitmap frame = _camera1.GetBitmap();
            // Turn off LED 1
            APICommunication.ControlLed1(_param.ApiUrlCom, false, 1000);

            // Keep origin image
            lock (_cam1WhiteOriginLock)
            {
                _cam1LastWhiteBitmap = Converter.BitmapToBitmapSource((Bitmap)frame.Clone()); 
            }
            // Update cam 1 white origin image
            _mainWindow.UpdateCam1WhiteOrigin(_cam1LastWhiteBitmap);

            // Call API
            Image<Bgr, byte> openCvImg = new Image<Bgr, byte>(frame);
            var resWlInspect = APICommunication.InspectWhiteLight(_param.ApiUrlAi, openCvImg.Mat, 1000);
            if (resWlInspect == null)
            {
                _mainWindow.ShowError(
                    "Cannot run AI inspection! Please check the AI engine\r" +
                    "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
                return (false, null);
            }

            // Check response
            if (!resWlInspect.Result)
            {
                totalStatus = false;
                errors.Add(resWlInspect.ErrorDesc);
            }
            else
            {
                lock (_cam1WhiteResultLock)
                {
                    _cam1LastWhiteResultBitmap = Converter.Base64ToBitmapSource(resWlInspect.ResImg);
                }
                _mainWindow.UpdateCam1WhiteResult(_cam1LastWhiteResultBitmap);
            }

            // Dispose temp image
            frame.Dispose();
            #endregion

            // Turn on UV light
            var resUv = APICommunication.ControlUv(_param.ApiUrlCom, true, 1000);
            if (!resUv)
            {
                _mainWindow.ShowError(
                    "Cannot turn on UV light! Please check the PLC connection\r" +
                    "Không bật được đèn UV, hãy kiểm tra kết nối PLC!");
                return (false, null);
            }

            // Capture image
            Thread.Sleep(_param.Cam1Exposure + 10);
            Bitmap frame2 = _camera1.GetBitmap();
            // Turn off UV light
            APICommunication.ControlUv(_param.ApiUrlCom, false, 1000);

            // Keep origin image
            lock (_cam1UvOriginLock)
            {
                _cam1LastUvBitmap = Converter.BitmapToBitmapSource((Bitmap)frame2.Clone());
            }
            // Update cam 1 UV origin image
            _mainWindow.UpdateCam1UvOrigin(_cam1LastUvBitmap);

            // Call API
            Image<Bgr, byte> openCvImg2 = new Image<Bgr, byte>(frame2);
            var resUvInspect = APICommunication.InspectUvLight(_param.ApiUrlAi, openCvImg2.Mat, 1000);
            if (resUvInspect == null)
            {
                _mainWindow.ShowError(
                    "Cannot run AI inspection! Please check the AI engine\r" +
                    "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
                return (false, null);
            }
            if (!resUvInspect.Result)
            {
                totalStatus = false;
                errors.Add(resWlInspect.ErrorDesc);
            }
            else
            {
                lock (_cam1UvResultLock)
                {
                    _cam1LastUvResultBitmap = Converter.Base64ToBitmapSource(resUvInspect.ResImg);
                }
                _mainWindow.UpdateCam1UvResult(_cam1LastUvResultBitmap);
            }
            // Dispose temp image
            frame2.Dispose();


            return (totalStatus, errors);
        }

        private (bool status, List<string> errors) InpsectCamera2()
        {
            bool totalStatus = true;
            List<string> errors = new List<string>();

            #region White Light
            // Turn on LED 2
            var resLed2 = APICommunication.ControlLed2(_param.ApiUrlCom, true, 1000);
            if (!resLed2)
            {
                _mainWindow.ShowError(
                    "Cannot turn on LED 2! Please check the PLC connection\r" +
                    "Không bật được đèn LED 2, hãy kiểm tra kết nối PLC!");
                return (false, null);
            }

            // Capture image
            Thread.Sleep(_param.Cam2Exposure + 10);
            Bitmap frame = _camera2.GetBitmap();
            // Turn off LED 2
            APICommunication.ControlLed2(_param.ApiUrlCom, false, 1000);

            // Keep origin image
            lock (_cam2WhiteOriginLock)
            {
                _cam2LastWhiteBitmap = Converter.BitmapToBitmapSource((Bitmap)frame.Clone());
            }
            // Update cam 2 white origin image
            _mainWindow.UpdateCam2WhiteOrigin(_cam2LastWhiteBitmap);

            // Call API
            Image<Bgr, byte> openCvImg = new Image<Bgr, byte>(frame);
            var resWlInspect = APICommunication.InspectWhiteLight(_param.ApiUrlAi, openCvImg.Mat, 1000);
            if (resWlInspect == null)
            {
                _mainWindow.ShowError(
                    "Cannot run AI inspection! Please check the AI engine\r" +
                    "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
                return (false, null);
            }

            // Check response
            if (!resWlInspect.Result)
            {
                totalStatus = false;
                errors.Add(resWlInspect.ErrorDesc);
            }
            else
            {
                lock (_cam2WhiteResultLock)
                {
                    _cam2LastWhiteResultBitmap = Converter.Base64ToBitmapSource(resWlInspect.ResImg);
                }
                _mainWindow.UpdateCam2WhiteResult(_cam2LastWhiteResultBitmap);
            }

            // Dispose temp image
            frame.Dispose();
            #endregion

            // Turn on UV light
            var resUv = APICommunication.ControlUv(_param.ApiUrlCom, true, 1000);
            if (!resUv)
            {
                _mainWindow.ShowError(
                    "Cannot turn on UV light! Please check the PLC connection\r" +
                    "Không bật được đèn UV, hãy kiểm tra kết nối PLC!");
                return (false, null);
            }

            // Capture image
            Thread.Sleep(_param.Cam2Exposure + 10);
            Bitmap frame2 = _camera2.GetBitmap();
            // Turn off UV light
            APICommunication.ControlUv(_param.ApiUrlCom, false, 1000);

            // Keep origin image
            lock (_cam2UvOriginLock)
            {
                _cam2LastUvBitmap = Converter.BitmapToBitmapSource((Bitmap)frame2.Clone());
            }
            // Update cam 2 UV origin image
            _mainWindow.UpdateCam2UvOrigin(_cam2LastUvBitmap);

            // Call API
            Image<Bgr, byte> openCvImg2 = new Image<Bgr, byte>(frame2);
            var resUvInspect = APICommunication.InspectUvLight(_param.ApiUrlAi, openCvImg2.Mat, 1000);
            if (resUvInspect == null)
            {
                _mainWindow.ShowError(
                    "Cannot run AI inspection! Please check the AI engine\r" +
                    "Không chạy được kiểm tra AI, hãy kiểm tra kết nối AI!");
                return (false, null);
            }
            if (!resUvInspect.Result)
            {
                totalStatus = false;
                errors.Add(resWlInspect.ErrorDesc);
            }
            else
            {
                lock (_cam2UvResultLock)
                {
                    _cam2LastUvResultBitmap = Converter.Base64ToBitmapSource(resUvInspect.ResImg);
                }
                _mainWindow.UpdateCam2UvResult(_cam2LastUvResultBitmap);
            }
            // Dispose temp image
            frame2.Dispose();


            return (totalStatus, errors);
        }



        private void StopPlcTimer()
        {
            if (_plcTimer != null)
            {
                _plcTimer.Stop();
                _plcTimer = null;
            }
        }

        #endregion

        #region Status Timer
        public void StartStatusTimer()
        {
            if (_statusTimer != null) return;
            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(1);
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            var resAI = APICommunication.CheckAPIStatus(_param.ApiUrlAi);
            var resPLC = APICommunication.CheckPlcConnection(_param.ApiUrlCom);
            var resCamera1 = _camera1.IsOpen();
            var resCamera2 = _camera2.IsOpen();
            _mainWindow.SetStatusService(resAI, resPLC, resCamera1, resCamera2);
        }
        public void StopStatusTimer()
        {
            if (_statusTimer != null)
            {
                _statusTimer.Stop();
                _statusTimer = null;
            }
        }
        #endregion

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
            //return true;
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
            //return true;
            if (!APICommunication.CheckPlcConnection(_param.ApiUrlCom))
            {
                var resConnection = APICommunication.ConnectPlc(_param.ApiUrlCom, _param.PlcIp, _param.PlcPort);
                if (!resConnection)
                {
                    _mainWindow.ShowError("Không kết nối được với PLC, hãy kiểm tra kết nối\nCannot connect to PLC! Please check the connection");
                    return false;
                }
            }
            return true;
        }
    }
}
