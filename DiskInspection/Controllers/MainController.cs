using DiskInspection.Controllers.APIs;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiskInspection.Controllers
{
    class MainController
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private Properties.Settings _param = Properties.Settings.Default;
        private MainWindow _mainWindow;
        public bool _serviceIsRun = false;

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
    }
}
