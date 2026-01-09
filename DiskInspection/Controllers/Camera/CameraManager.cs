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
        private LincolnCamera _camera1;
        private LincolnCamera _camera2;
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
        public LincolnCamera GetCamera1()
        {
            if (((_camera1 != null) && (_camera1.SN != Properties.Settings.Default.Cam1Sn)) || (_camera1 == null))
            {
                if (_camera1 != null)
                    _camera1.Close();
                _camera1 = new LincolnCamera(Properties.Settings.Default.Cam1Sn);
            }
            return _camera1;
        }

        public LincolnCamera GetCamera2() 
        {
            if (((_camera2 != null) && (_camera2.SN != Properties.Settings.Default.Cam2Sn)) || (_camera2 == null))
            {
                if (_camera2 != null)
                    _camera2.Close();
                _camera2 = new LincolnCamera(Properties.Settings.Default.Cam2Sn);
            }
            return _camera2;
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
