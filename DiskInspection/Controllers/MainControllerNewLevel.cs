using DiskInspection.Controllers.APIs;
using DiskInspection.Domain;
using DiskInspection.Models;
using DiskInspection.Utils;
using Emgu.CV;
using Emgu.CV.Structure;
using NLog;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace DiskInspection.Controllers
{
    public sealed class MainControllerNewLevel
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly Properties.Settings _param = Properties.Settings.Default;

        private CancellationTokenSource _cts;
        private InspectState _state = InspectState.Idle;

        #region EVENTS (UI SUBSCRIBE)

        public event Action<InspectState> StateChanged;
        public event Action<CameraInspectResult> Cam1WhiteDone;
        public event Action<CameraInspectResult> Cam1UvDone;
        public event Action<CameraInspectResult> Cam2WhiteDone;
        public event Action<CameraInspectResult> Cam2UvDone;
        public event Action<InspectSummary> InspectionDone;
        public event Action<string> ErrorOccurred;

        public event Action<bool> OnPlcConnected;


        #endregion

        #region LIFECYCLE

        public async Task StartAsync()
        {
            if (_cts != null)
                return;

            _cts = new CancellationTokenSource();
            SetState(InspectState.WaitTrigger);

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await RunStateAsync(_cts.Token);
                    await Task.Delay(50);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Info("Inspection stopped");
            }
            finally
            {
                _cts = null;
                SetState(InspectState.Idle);
            }
        }

        public void Stop()
        {
            if (_cts != null)
                _cts.Cancel();
        }

        #endregion

        #region STATE MACHINE

        private async Task RunStateAsync(CancellationToken ct)
        {
            switch (_state)
            {
                case InspectState.WaitTrigger:
                    if (await CheckTriggerAsync())
                        SetState(InspectState.InspectCam1);
                    break;

                case InspectState.InspectCam1:
                    await InspectCamera1Async(ct);
                    SetState(InspectState.InspectCam2);
                    break;

                case InspectState.InspectCam2:
                    await InspectCamera2Async(ct);
                    SetState(InspectState.Report);
                    break;

                case InspectState.Report:
                    InspectionDone?.Invoke(new InspectSummary(true, true));
                    SetState(InspectState.WaitTrigger);
                    break;
            }
        }

        private void SetState(InspectState newState)
        {
            if (_state == newState)
                return;

            _state = newState;
            StateChanged?.Invoke(newState);
        }

        #endregion

        #region PLC

        private async Task<bool> CheckTriggerAsync()
        {
            return await Task.Run(() =>
            {
                var result = APICommunication.CheckTrigger(_param.ApiUrlCom, 1000);

                if (result.Item1 == TriggerState.ERROR)
                {
                    ErrorOccurred?.Invoke("PLC trigger read failed");
                    return false;
                }

                if (!result.Item2)
                    return false;

                APICommunication.ResetTrigger(_param.ApiUrlCom, 1000);
                return true;
            });
        }

        #endregion

        #region CAMERA

        private async Task InspectCamera1Async(CancellationToken ct)
        {
            var white = await CaptureAndInspectAsync(
                () => APICommunication.ControlLed1(_param.ApiUrlCom, true, 1000),
                () => APICommunication.ControlLed1(_param.ApiUrlCom, false, 1000),
                img => APICommunication.InspectWhiteLight(_param.ApiUrlAi, img, 1000),
                _param.Cam1Exposure);

            Cam1WhiteDone?.Invoke(white);

            var uv = await CaptureAndInspectAsync(
                () => APICommunication.ControlUv(_param.ApiUrlCom, true, 1000),
                () => APICommunication.ControlUv(_param.ApiUrlCom, false, 1000),
                img => APICommunication.InspectUvLight(_param.ApiUrlAi, img, 1000),
                _param.Cam1Exposure);

            Cam1UvDone?.Invoke(uv);
        }

        private async Task InspectCamera2Async(CancellationToken ct)
        {
            var white = await CaptureAndInspectAsync(
                () => APICommunication.ControlLed2(_param.ApiUrlCom, true, 1000),
                () => APICommunication.ControlLed2(_param.ApiUrlCom, false, 1000),
                img => APICommunication.InspectWhiteLight(_param.ApiUrlAi, img, 1000),
                _param.Cam2Exposure);

            Cam2WhiteDone?.Invoke(white);

            var uv = await CaptureAndInspectAsync(
                () => APICommunication.ControlUv(_param.ApiUrlCom, true, 1000),
                () => APICommunication.ControlUv(_param.ApiUrlCom, false, 1000),
                img => APICommunication.InspectUvLight(_param.ApiUrlAi, img, 1000),
                _param.Cam2Exposure);

            Cam2UvDone?.Invoke(uv);
        }

        private async Task<CameraInspectResult> CaptureAndInspectAsync(
            Func<bool> ledOn,
            Func<bool> ledOff,
            Func<Mat, dynamic> inspect,
            int exposure)
        {
            await Task.Run(ledOn);
            await Task.Delay(exposure + 10);

            Bitmap bmp = new Bitmap(@"D:\dummy.bmp"); // TODO: real camera

            await Task.Run(ledOff);

            var res = await Task.Run(() =>
                inspect(new Image<Bgr, byte>(bmp).Mat));

            if (res == null)
                return new CameraInspectResult(false, bmp, null);

            Bitmap result = Converter.Base64ToBitmap(res.ResImg);
            return new CameraInspectResult(res.Result, bmp, result);
        }

        #endregion

        #region CHECK STATUS
        private async Task CheckPlcAsync(CancellationToken ct)
        {
            bool ok = await Task.Run(() => APICommunication.CheckPlcConnection(_param.ApiUrlCom), ct);
            OnPlcConnected?.Invoke(ok);
        }


        // ở MainWindow sẽ là
        //_mainController.OnPlcConnected += status =>
        //{
        //    // chạy trên UI thread
        //    Dispatcher.Invoke(() =>
        //    {
        //        lblPlcStatus.Content = status? "PLC OK" : "PLC ERROR";
        //        lblPlcStatus.Foreground = status? Brushes.Green : Brushes.Red;
        //    });
        //};
        private CancellationTokenSource? _ctsStatus;

        public async Task StartStatusLoopAsync()
        {
            _ctsStatus = new CancellationTokenSource();
            var ct = _ctsStatus.Token;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await CheckPlcAsync(ct);
                    await CheckCameraAsync(_cam1, OnCam1Connected, ct);
                    await CheckCameraAsync(_cam2, OnCam2Connected, ct);
                    await CheckAiAsync(ct);

                    await Task.Delay(1000, ct); // check mỗi giây
                }
            }
            catch (OperationCanceledException)
            {
                Log.Info("Status loop cancelled");
            }
        }

        public void StopStatusLoop() => _ctsStatus?.Cancel();
        #endregion
    }
}
