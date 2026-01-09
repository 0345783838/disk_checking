using DiskInspection.Controllers.APIs;
using DiskInspection.Controllers.Camera;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        private void PlcTimer_Tick(object sender, EventArgs e)
        {
            var resTrigger = APICommunication.(_param.ApiUrlCom, 1000);
            var resCamera1 = _camera1.IsOpen();
            var resCamera2 = _camera2.IsOpen();
            _mainWindow.SetStatusPlc(resTrigger, resCamera1, resCamera2);
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
