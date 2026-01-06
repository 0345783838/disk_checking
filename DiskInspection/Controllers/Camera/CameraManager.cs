using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Controllers.Camera
{
    internal class CameraManager
    {
        private static CameraManager _cameraManager;
        private LincolnCamera _leftCam;
        private LincolnCamera _rightCam;
        public static CameraManager GetInstance()
        {
            if (_cameraManager == null)
            {
                _cameraManager = new CameraManager();
            }

            return _cameraManager;
        }
        public static void Reload()
        {
            _cameraManager = new CameraManager();
        }
        public LincolnCamera GetTopCamera()
        {
            if (((_leftCam != null) && (_leftCam.SN != Properties.Settings.Default.LeftCamSN)) || (_leftCam == null))
            {
                if (_leftCam != null)
                    _leftCam.Close();
                _leftCam = new LincolnCamera(Properties.Settings.Default.RightCamSN);
            }
            return _leftCam;
        }

        public LincolnCamera GetBotCamera() 
        {
            if (((_rightCam != null) && (_rightCam.SN != Properties.Settings.Default.LeftCamSN)) || (_rightCam == null))
            {
                if (_rightCam != null)
                    _rightCam.Close();
                _rightCam = new LincolnCamera(Properties.Settings.Default.RightCamSN);
            }
            return _rightCam;
        }
        public bool CheckCameraConnection(string SN)
        {
            var cam = new LincolnCamera(SN);
            if (cam.IsOpen())
            {
                cam.Close();
                return cam.IsOpen();
            }
            return false;
        }
    }
}
