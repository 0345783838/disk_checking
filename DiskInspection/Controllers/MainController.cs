using DiskInspection.Controllers.APIs;
using DiskInspection.Controllers.Camera;
using DiskInspection.Controllers.PLC;
using DiskInspection.Models;
using DiskInspection.Security;
using DiskInspection.Utils;
using DiskInspection.Views.ActivationWindows;
using Emgu.CV;
using Emgu.CV.Structure;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace DiskInspection.Controllers
{
    /// <summary>
    /// Điều phối toàn bộ luồng kiểm tra: PLC trigger → chụp ảnh → AI → kết quả.
    /// </summary>
    class MainController
    {
        // ─── Dependencies ────────────────────────────────────────────────────────
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly Properties.Settings _param = Properties.Settings.Default;
        private readonly MainWindow _mainWindow;

        // ─── Camera ──────────────────────────────────────────────────────────────
        private CameraManager _cameraManager;
        private LincolnCamera _camera1;
        private LincolnCamera _camera2;

        // ─── Timers ───────────────────────────────────────────────────────────────
        private System.Timers.Timer _plcTimer;
        private System.Timers.Timer _statusTimer;

        // ─── State ────────────────────────────────────────────────────────────────
        private bool _isRunning;
        private CancellationTokenSource _inspectCts;
        public bool ServiceIsRunning { get; private set; }

        // ─── Last captured frames (dùng để update UI và lưu ảnh) ─────────────────
        // Mỗi cặp (origin, result) được bảo vệ bởi 1 lock duy nhất
        private readonly object _cam1Lock = new object();
        private readonly object _cam2Lock = new object();

        private BitmapSource _cam1WhiteOrigin, _cam1WhiteResult;
        private BitmapSource _cam1UvOrigin, _cam1UvResult;
        private BitmapSource _cam2WhiteOrigin, _cam2WhiteResult;
        private BitmapSource _cam2UvOrigin, _cam2UvResult;

        // ─── Constants ───────────────────────────────────────────────────────────
        private const int PlcPollIntervalMs = 50;
        private const int StatusPollIntervalMs = 2000;
        private const string LicensePath = @"plugin\license.dat";

        // ─────────────────────────────────────────────────────────────────────────

        public MainController(MainWindow window)
        {
            _mainWindow = window;
            PlcController.ControlWhiteLight(_param.ApiUrlCom, true, 1000);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 1. KHỞI ĐỘNG CHƯƠNG TRÌNH
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Khởi động AI engine và chờ nó sẵn sàng trong khoảng thời gian timeout.
        /// </summary>
        public bool StartAIService(int timeoutMs, string loadingMessage)
        {
            _mainWindow.SetLoadingService(loadingMessage);
            _logger.Info("Starting AI service...");
            AppLogger.Instance.Info("Loading Program...", "SYSTEM");

            AIServiceController.CloseProcessExisting();
            AIServiceController.Start();

            int steps = timeoutMs / 1000;
            for (int i = 0; i < steps; i++)
            {
                Thread.Sleep(1000);
                if (APICommunication.CheckAPIStatus(_param.ApiUrlAi, 200))
                {
                    _logger.Info("AI service started successfully.");
                    AppLogger.Instance.Info("Loaded Program Successfully!", "SYSTEM");
                    ServiceIsRunning = true;
                    return true;
                }
            }

            _logger.Error("AI service failed to start within timeout.");
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 2. BẮT ĐẦU KIỂM TRA
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Kiểm tra điều kiện và bắt đầu vòng lặp kiểm tra.
        /// </summary>
        public bool Start()
        {
            Thread.Sleep(500);
            _logger.Info("Starting inspection system...");

            if (!CheckAndStartCamera() || !CheckAndStartPLC() || !CheckAndStartAI())
            {
                _logger.Error("System startup failed — cameras, PLC, or AI not ready.");
                AppLogger.Instance.Error("Cameras, PLC and AI are not ready. Stopping.", "SYSTEM");
                return false;
            }

            _logger.Debug("All systems ready. Starting inspection loop.");
            AppLogger.Instance.Info("Cameras, PLC and AI are ready.", "SYSTEM");

            _isRunning = true;
            _inspectCts = new CancellationTokenSource();
            StartStatusTimer();
            StartPlcTimer();
            return true;
        }

        // ─── Kiểm tra từng hệ thống con ──────────────────────────────────────────

        private bool CheckAndStartAI()
        {
            if (APICommunication.CheckAPIStatus(_param.ApiUrlAi))
                return true;

            _mainWindow.ShowWarning("AI engine is not running. Restart?");
            bool restarted = StartAIService(timeoutMs: 20000, "Restarting AI engine...");

            if (!restarted)
                _mainWindow.ShowError("Failed to restart AI engine. Please contact vendor.");

            return restarted;
        }

        private bool CheckAndStartCamera()
        {
            //return true;
            _cameraManager = CameraManager.GetInstance();
            _camera1 = _cameraManager.GetCamera1();
            _camera2 = _cameraManager.GetCamera2();

            if (!_camera1.IsOpen())
            {
                _camera1 = null;
                _mainWindow.ShowError($"Cannot open Camera 1 (SN: {_param.Cam1Sn})");
                return false;
            }
            if (!_camera2.IsOpen())
            {
                _camera2 = null;
                _mainWindow.ShowError($"Cannot open Camera 2 (SN: {_param.Cam2Sn})");
                return false;
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
            if (PlcController.CheckPlcConnection(_param.ApiUrlCom))
                return true;

            bool connected = PlcController.ConnectPlc(_param.ApiUrlCom, _param.PlcIp, _param.PlcPort);
            if (!connected)
                _mainWindow.ShowError("Cannot connect to PLC. Please check the connection.");

            return connected;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 3. VÒNG LẶP PLC TIMER — đây là "nhịp tim" của hệ thống
        // ═════════════════════════════════════════════════════════════════════════

        private void StartPlcTimer()
        {
            if (_plcTimer != null) return;
            _plcTimer = new System.Timers.Timer(PlcPollIntervalMs) { AutoReset = true };
            _plcTimer.Elapsed += OnPlcTimerElapsed;
            _plcTimer.Start();
        }

        private void StopPlcTimer()
        {
            if (_plcTimer == null) return;
            _plcTimer.Stop();
            _plcTimer.Elapsed -= OnPlcTimerElapsed;
            _plcTimer.Dispose();
            _plcTimer = null;
        }

        /// <summary>
        /// Xử lý mỗi tick của PLC timer: kiểm tra trigger → chụp ảnh → AI → kết quả.
        ///
        /// Dùng "stop-and-restart" pattern thay vì AutoReset để đảm bảo
        /// không có 2 vòng kiểm tra chạy đồng thời.
        /// </summary>
        private async void OnPlcTimerElapsed(object sender, EventArgs e)
        {
            if (!_isRunning || _inspectCts.IsCancellationRequested) return;

            StopPlcTimer();

            try
            {
                var (triggerState, triggered) = PlcController.CheckTrigger(_param.ApiUrlCom, 1000);

                if (triggerState == TriggerState.Error)
                {
                    ShowAndLogError("Cannot connect to PLC to read trigger!", "PLC");
                    _isRunning = false;
                    return; // timer sẽ không được restart → hệ thống dừng an toàn
                }

                if (!triggered) return; // chưa có trigger, chờ tick tiếp theo

                await RunInspectionCycleAsync(_inspectCts.Token);
            }
            finally
            {
                // Chỉ restart timer nếu hệ thống vẫn đang chạy
                if (_isRunning)
                    StartPlcTimer();
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 4. MỘT CHU KỲ KIỂM TRA ĐẦY ĐỦ
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Thực hiện 1 chu kỳ kiểm tra hoàn chỉnh:
        ///   1. Reset trigger
        ///   2. Chụp ảnh (White → UV) — tuần tự theo đèn
        ///   3. Gửi AI — 2 camera song song
        ///   4. Cập nhật UI và PLC
        /// </summary>
        private async Task RunInspectionCycleAsync(CancellationToken token)
        {
            AppLogger.Instance.Info("Trigger received. Starting inspection cycle.", "PLC");
            _logger.Info("Trigger received. Starting inspection cycle.");
            _mainWindow.UpdateInspectingMode();
            App.ImageViewer.ClearImages();

            // Reset trigger ngay lập tức để PLC có thể gửi trigger tiếp theo
            if (!PlcController.ResetTrigger(_param.ApiUrlCom, 1000))
            {
                ShowAndLogError("Cannot reset PLC trigger!", "PLC");
                return;
            }

            try
            {
                // -- Ẩn Image Viewer đi -- 
                App.ImageViewer.HideViewer();

                // ── Bước 1: Start Stopwatch + Bật đèn White → đợi ổn định → chụp ảnh ──────────────
                var aiStopwatch = Stopwatch.StartNew();
                var (whiteFrame1, whiteFrame2) = await CaptureWhiteFramesAsync(token);
                if (whiteFrame1 == null || whiteFrame2 == null) return;

                // ── Bước 2: AI White + delay tắt đèn chạy song song ─────────────
                // Bắt đầu tính thời gian AI từ lúc có ảnh
                
                var whiteAiTask = RunWhiteAIAsync(whiteFrame1, whiteFrame2, token);
                var whiteLightOffTask = TurnOffWhiteLightAsync(token);

                // Đợi AI White xong trước — không cần đợi đèn tắt để update UI ngay
                var (whiteResult1, whiteResult2) = await whiteAiTask;
                if (whiteResult1 == null || whiteResult2 == null) return;

                // Update UI White ngay khi có kết quả, không phải đợi đến cuối chu kỳ
                UpdateUIWithWhiteResults(whiteResult1, whiteResult2);

                // Đảm bảo đèn trắng đã tắt hẳn trước khi bật UV
                await whiteLightOffTask;

                // ── Bước 3: Bật đèn UV → đợi ổn định → chụp ảnh ─────────────────
                var (uvFrame1, uvFrame2) = await CaptureUvFramesAsync(token);
                if (uvFrame1 == null || uvFrame2 == null) return;

                // ── Bước 4: AI UV + delay tắt đèn chạy song song ────────────────
                var uvAiTask = RunUVAIAsync(uvFrame1, uvFrame2, whiteResult1, whiteResult2, token);
                var uvLightOffTask = TurnOffUvLightAsync(token);

                // Đợi AI UV xong → dừng đồng hồ → đây là tổng thời gian AI thuần
                var (cam1Result, cam2Result) = await uvAiTask;
                // ── Bước 5: Cập nhật UI kết quả UV + thông tin tổng ─────────────
                aiStopwatch.Stop();
                // Duration = tổng thời gian AI xử lý (White + UV), không tính delay đèn
                cam1Result.Duration = aiStopwatch.Elapsed;
                cam2Result.Duration = aiStopwatch.Elapsed;
                UpdateUIWithUvResults(cam1Result, cam2Result);

                // Đợi đèn UV tắt hẳn
                await uvLightOffTask;

                if (token.IsCancellationRequested) return;


                // ── Bước 6: Xử lý kết quả tổng ──────────────────────────────────
                InspectionResult finalStatus;
                if (cam1Result.Result == InspectionResult.Passed && cam2Result.Result == InspectionResult.Passed)
                    finalStatus = InspectionResult.Passed;
                else if (cam1Result.Result == InspectionResult.Failed || cam2Result.Result == InspectionResult.Failed)
                    finalStatus = InspectionResult.Failed;
                else
                    finalStatus = InspectionResult.Warning;

                _mainWindow.UpdateInspectionStatus(finalStatus);
                _mainWindow.UpdateStatistics(finalStatus);
                var timeStamp = _mainWindow.UpdateTimeStamp();

                if (finalStatus == InspectionResult.Failed)
                {
                    if (cam1Result.UvErrorCode == ErrorCode.ERROR_003 || cam2Result.UvErrorCode == ErrorCode.ERROR_003)
                        PlcController.OnErrorMixing(_param.ApiUrlCom);
                    else 
                        PlcController.OnErrorAbnormal(_param.ApiUrlCom);
                    App.ImageViewer.ShowFirstErrorImage();
                    AppLogger.Instance.Info("Inspection NG — sent NG signal to PLC.", "PLC");
                    _logger.Info("Inspection NG — sent NG signal to PLC.");
                }
                else
                {
                    AppLogger.Instance.Info("Inspection OK.", "PLC");
                    _logger.Info("Inspection OK.");
                    PlcController.OnOkSignal(_param.ApiUrlCom);
                }

                AppLogger.Instance.Info($"Inspection complete in {aiStopwatch.ElapsedMilliseconds}ms AI time. Waiting for next trigger.", "SYSTEM");
                _logger.Info($"Inspection complete in {aiStopwatch.ElapsedMilliseconds}ms AI time. Waiting for next trigger.");

                // ── Bước 7: Lưu ảnh (fire-and-forget, không block chu kỳ) ────────
                if (_param.SaveEnable)
                    _ = Task.Run(() => SaveImages(timeStamp, finalStatus==InspectionResult.Passed));
            }
            catch (OperationCanceledException)
            {
                AppLogger.Instance.Info("Inspection cancelled.", "SYSTEM");
                _logger.Info("Inspection cancelled.");
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 5. CHỤP ẢNH — tuần tự theo đèn, 2 camera song song trong cùng 1 đèn
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Bật đèn White → đợi WaitWhiteLightOn → chụp 2 camera → trả về ảnh ngay.
        /// KHÔNG tắt đèn ở đây — đèn được tắt riêng bởi TurnOffWhiteLightAsync.
        /// </summary>
        private async Task<(Bitmap cam1, Bitmap cam2)> CaptureWhiteFramesAsync(CancellationToken token)
        {
            if (!await ControlLightAsync(LightType.White, on: true))
            {
                ShowAndLogError("Cannot turn on White light!", "PLC");
                return (null, null);
            }

            // Đợi đèn ổn định rồi mới chụp
            await Task.Delay(_param.WaitWhiteLightOn, token);

            var (frame1, frame2) = await CaptureFromBothCamerasAsync(token);
            UpdateCapturedFrameUI(frame1, frame2, LightType.White);
            AppLogger.Instance.Info("White frames captured.", "CAM");
            _logger.Info("White frames captured.");

            // Trả về ảnh ngay — đèn vẫn còn sáng, sẽ được tắt sau bởi TurnOffWhiteLightAsync
            return (frame1, frame2);
        }

        /// <summary>
        /// Giữ đèn White sáng thêm WaitWhiteLightOff rồi mới tắt.
        /// Chạy song song với RunWhiteAIAsync để tận dụng thời gian chờ.
        /// </summary>
        private async Task TurnOffWhiteLightAsync(CancellationToken token)
        {
            await Task.Delay(_param.WaitWhiteLightOff, token);
            await ControlLightAsync(LightType.White, on: false);
            AppLogger.Instance.Info("White light off.", "PLC");
            _logger.Info("White light off.");
        }

        /// <summary>
        /// Bật đèn UV → đợi WaitUvLightOn → chụp 2 camera → trả về ảnh ngay.
        /// KHÔNG tắt đèn ở đây — đèn được tắt riêng bởi TurnOffUvLightAsync.
        /// </summary>
        private async Task<(Bitmap cam1, Bitmap cam2)> CaptureUvFramesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!await ControlLightAsync(LightType.Uv, on: true))
            {
                ShowAndLogError("Cannot turn on UV light!", "PLC");
                return (null, null);
            }

            // Đợi đèn ổn định rồi mới chụp
            await Task.Delay(_param.WaitUvLightOn, token);

            var (frame1, frame2) = await CaptureFromBothCamerasAsync(token);
            UpdateCapturedFrameUI(frame1, frame2, LightType.Uv);
            AppLogger.Instance.Info("UV frames captured.", "CAM");
            _logger.Info("UV frames captured.");

            // Trả về ảnh ngay — đèn vẫn còn sáng, sẽ được tắt sau bởi TurnOffUvLightAsync
            return (frame1, frame2);
        }

        /// <summary>
        /// Giữ đèn UV sáng thêm WaitUvLightOff rồi mới tắt.
        /// Chạy song song với RunUVAIAsync để tận dụng thời gian chờ.
        /// </summary>
        private async Task TurnOffUvLightAsync(CancellationToken token)
        {
            await Task.Delay(_param.WaitUvLightOff, token);
            await ControlLightAsync(LightType.Uv, on: false);
            AppLogger.Instance.Info("UV light off.", "PLC");
            _logger.Info("UV light off.");
        }

        /// <summary>
        /// Chụp ảnh từ cả 2 camera cùng lúc.
        /// </summary>
        private async Task<(Bitmap cam1, Bitmap cam2)> CaptureFromBothCamerasAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var results = await Task.WhenAll(
                Task.Run(() => _camera1.TriggerAndGetFrame(), token),
                Task.Run(() => _camera2.TriggerAndGetFrame(), token));

            //var results = await Task.WhenAll(
            //       Task.Run(() => new Bitmap(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\raw_data\real_images\empty_disk\Image_20260521165012713.bmp"), token),
            //       Task.Run(() => new Bitmap(@"D:\huynhvc\OTHERS\disk_checking\disk_checking\raw_data\real_images\empty_disk\Image_20260521165035713.bmp"), token));

            return (results[0], results[1]);
        }

        /// <summary>
        /// Điều khiển đèn theo loại (White/UV). 1 call bật/tắt cả 2 đèn cùng loại.
        /// </summary>
        private async Task<bool> ControlLightAsync(LightType light, bool on)
        {
            return await Task.Run(() =>
                light == LightType.White
                    ? PlcController.ControlWhiteLight(_param.ApiUrlCom, on, 1000)
                    : PlcController.ControlUvLight(_param.ApiUrlCom, on, 1000));
        }

        private enum LightType { White, Uv }

        // ═════════════════════════════════════════════════════════════════════════
        // 6. XỬ LÝ AI
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Kết quả kiểm tra của 1 camera (gộp White + UV).
        /// </summary>

        private class CameraInspectionResult
        {
            public InspectionResult Result { get; set; }
            public TimeSpan Duration { get; set; }

            public BitmapSource WhiteResultBitmap { get; set; }
            public BitmapSource UvResultBitmap { get; set; }

            public double MinDiskDistance { get; set; }
            public double MaxDiskDistance { get; set; }
            public int UvDiskCount { get; set; }

            public string WhiteErrorDesc { get; set; }
            public string WhiteErrorCode { get; set; }
            public string UvErrorDesc { get; set; }
            public string UvErrorCode { get; set; }
            public int WhitePassed { get; set; }
            public bool UvPassed { get; set; }
        }

        /// <summary>
        /// Gửi AI White cho cả 2 camera song song.
        /// Chạy đồng thời với việc chụp UV để tiết kiệm thời gian.
        /// Trả về (null, null) nếu 1 trong 2 thất bại.
        /// </summary>
        private async Task<(InspectionResponse cam1, InspectionResponse cam2)>
            RunWhiteAIAsync(Bitmap whiteFrame1, Bitmap whiteFrame2, CancellationToken token)
        {
            AppLogger.Instance.Info("Running White AI for both cameras...", "AI");
            _logger.Info("Running White AI for both cameras...");

            var results = await Task.WhenAll(
                Task.Run(() => APICommunication.InspectWhiteLight(
                    _param.ApiUrlAi,
                    new Image<Bgr, byte>(whiteFrame1).Mat,
                    timeout: 10000), token),
                Task.Run(() => APICommunication.InspectWhiteLight(
                    _param.ApiUrlAi,
                    new Image<Bgr, byte>(whiteFrame2).Mat,
                    timeout: 10000), token));

            whiteFrame1.Dispose();
            whiteFrame2.Dispose();

            if (results[0] == null)
            {
                ShowAndLogError("AI White inspection failed for Camera 1.", "CAM1 AI");
                return (null, null);
            }
            if (results[1] == null)
            {
                ShowAndLogError("AI White inspection failed for Camera 2.", "CAM2 AI");
                return (null, null);
            }

            AppLogger.Instance.Info("White AI complete for both cameras.", "AI");
            _logger.Info("White AI complete for both cameras.");
            return (results[0], results[1]);
        }

        /// <summary>
        /// Gửi AI UV cho cả 2 camera song song.
        /// Dùng tọa độ cropbox từ kết quả White để xác định vùng kiểm tra.
        /// </summary>
        private async Task<(CameraInspectionResult cam1, CameraInspectionResult cam2)>
            RunUVAIAsync(
                Bitmap uvFrame1, Bitmap uvFrame2,
                InspectionResponse whiteResult1, InspectionResponse whiteResult2,
                CancellationToken token)
        {
            AppLogger.Instance.Info("Running UV AI for both cameras...", "AI");
            _logger.Info("Running UV AI for both cameras...");

            var results = await Task.WhenAll(
                Task.Run(() => APICommunication.InspectUvLight(
                    _param.ApiUrlAi,
                    new Image<Bgr, byte>(uvFrame1).Mat,
                    whiteResult1.CropBox, whiteResult1.UvBox1, whiteResult1.UvBox2,
                    whiteResult1.Mid1, whiteResult1.Mid2,
                    timeout: 10000), token),
                Task.Run(() => APICommunication.InspectUvLight(
                    _param.ApiUrlAi,
                    new Image<Bgr, byte>(uvFrame2).Mat,
                    whiteResult2.CropBox, whiteResult2.UvBox1, whiteResult2.UvBox2,
                    whiteResult2.Mid1, whiteResult2.Mid2,
                    timeout: 10000), token));

            uvFrame1.Dispose();
            uvFrame2.Dispose();

            AppLogger.Instance.Info("UV AI complete for both cameras.", "AI");
            _logger.Info("UV AI complete for both cameras.");

            // Ghép kết quả White + UV thành CameraInspectionResult
            return (
                BuildResult(whiteResult1, results[0]),
                BuildResult(whiteResult2, results[1]));
        }

        /// <summary>
        /// Ghép kết quả White và UV thành 1 object kết quả hoàn chỉnh cho 1 camera.
        /// </summary>
        private static CameraInspectionResult BuildResult(
            InspectionResponse white, InspectionUvResponse uv)
        {
            if (uv == null)
                return new CameraInspectionResult { Result = InspectionResult.Failed };

            InspectionResult res;
            if (white.Result == (int)InspectionResult.Failed || !uv.Result )
                res = InspectionResult.Failed;
            else if (white.Result == (int)InspectionResult.Warning && uv.Result)
                res = InspectionResult.Warning;
            else
                res = InspectionResult.Passed;

            return new CameraInspectionResult
            {
                Result = res,
                WhiteResultBitmap = Converter.Base64ToBitmapSource(white.ResImg),
                UvResultBitmap = Converter.Base64ToBitmapSource(uv.ResImg),
                MinDiskDistance = white.MinDiskDistance,
                MaxDiskDistance = white.MaxDiskDistance,
                UvDiskCount = uv.CountUvDisk,
                WhitePassed = white.Result,
                WhiteErrorCode = white.ErrorCode,
                WhiteErrorDesc = white.ErrorDesc,
                UvPassed = uv.Result,
                UvErrorDesc = uv.ErrorDesc,
                UvErrorCode = uv.ErrorCode
            };
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 7. CẬP NHẬT UI
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cập nhật UI ảnh gốc ngay sau khi chụp.
        /// </summary>
        private void UpdateCapturedFrameUI(Bitmap frame1, Bitmap frame2, LightType light)
        {
            if (frame1 == null)
            {
                ShowAndLogError("Captured frame 1 null", "CAM1");
                return;
            }
            if (frame2 == null)
            {
                ShowAndLogError("Captured frame 2 null", "CAM2");
                return;
            }
            if (light == LightType.White)
            {  
                lock (_cam1Lock)
                {
                   
                        
                    _cam1WhiteOrigin = Converter.BitmapToBitmapSource((Bitmap)frame1.Clone());
                    App.ImageViewer.AddImage(_cam1WhiteOrigin, "1-White-Origin", ThumbStatus.Origin, "Camera 1 - White - Original");
                }
                lock (_cam2Lock)
                {
                    
                        
                    _cam2WhiteOrigin = Converter.BitmapToBitmapSource((Bitmap)frame2.Clone());
                    App.ImageViewer.AddImage(_cam2WhiteOrigin, "2-White-Origin", ThumbStatus.Origin, "Camera 2 - White - Original");
                }
                _mainWindow.UpdateCam1WhiteOrigin(_cam1WhiteOrigin);
                _mainWindow.UpdateCam2WhiteOrigin(_cam2WhiteOrigin);
            }
            else
            {
                lock (_cam1Lock)
                {
                    _cam1UvOrigin = Converter.BitmapToBitmapSource((Bitmap)frame1.Clone());
                    App.ImageViewer.AddImage(_cam1UvOrigin, "1-UV-Origin", ThumbStatus.Origin, "Camera 1 - UV - Original");
                }
                lock (_cam2Lock)
                {
                    _cam2UvOrigin = Converter.BitmapToBitmapSource((Bitmap)frame2.Clone());
                    App.ImageViewer.AddImage(_cam2UvOrigin, "2-UV-Origin", ThumbStatus.Origin, "Camera 2 - UV - Original");
                }
                _mainWindow.UpdateCam1UvOrigin(_cam1UvOrigin);
                _mainWindow.UpdateCam2UvOrigin(_cam2UvOrigin);
            }
        }

        /// <summary>
        /// Cập nhật UI ảnh kết quả White ngay sau khi AI White xong.
        /// Dùng InspectionResponse trực tiếp vì chưa có CameraInspectionResult đầy đủ.
        /// </summary>
        private void UpdateUIWithWhiteResults(InspectionResponse white1, InspectionResponse white2)
        {
            if (white1?.ResImg != null)
            {
                var bitmap = Converter.Base64ToBitmapSource(white1.ResImg);
                lock (_cam1Lock) { _cam1WhiteResult = bitmap; }
                _mainWindow.UpdateCam1WhiteResult(_cam1WhiteResult);
                _mainWindow.UpdateCam1MinMaxDis(white1.MinDiskDistance, white1.MaxDiskDistance);

                ThumbStatus status;
                if (white1.Result == (int)InspectionResult.Failed)
                {
                    status = ThumbStatus.Ng;
                }
                else if (white1.Result == (int)InspectionResult.Warning)
                {
                    status = ThumbStatus.Warning;
                }
                else
                {
                    status = ThumbStatus.Ok;
                }
                App.ImageViewer.AddImage(_cam1WhiteResult, "1-White-Result", status, $"CAM1 White: {white1.ErrorDesc}");

            }

            if (white2?.ResImg != null)
            {
                var bitmap = Converter.Base64ToBitmapSource(white2.ResImg);
                lock (_cam2Lock) { _cam2WhiteResult = bitmap; }
                _mainWindow.UpdateCam2WhiteResult(_cam2WhiteResult);
                _mainWindow.UpdateCam2MinMaxDis(white2.MinDiskDistance, white2.MaxDiskDistance);

                ThumbStatus status;
                if (white2.Result == (int)InspectionResult.Failed)
                {
                    status = ThumbStatus.Ng;
                }
                else if (white2.Result == (int)InspectionResult.Warning)
                {
                    status = ThumbStatus.Warning;
                }
                else
                {
                    status = ThumbStatus.Ok;
                }

                App.ImageViewer.AddImage(_cam2WhiteResult, "2-White-Result", status, $"CAM2 White: {white2.ErrorDesc}");
            }
        }

        /// <summary>
        /// Cập nhật UI ảnh kết quả UV + tất cả thông tin tổng hợp sau khi AI UV xong.
        /// </summary>
        private void UpdateUIWithUvResults(CameraInspectionResult cam1, CameraInspectionResult cam2)
        {
            if (cam1.UvResultBitmap != null)
            {
                lock (_cam1Lock) { _cam1UvResult = cam1.UvResultBitmap; }
                _mainWindow.UpdateCam1UvResult(_cam1UvResult);
                _mainWindow.UpdateCam1DiskUv(cam1.UvDiskCount);
                _mainWindow.UpdateInspectionStatusCam1(cam1.Result);
                _mainWindow.UpdateCam1ProcessedTime(cam1.Duration);
                var status = cam1.UvPassed ? ThumbStatus.Ok : ThumbStatus.Ng;
                App.ImageViewer.AddImage(_cam1UvResult, "1-UV-Result", status, $"CAM1 UV: {cam1.UvErrorDesc}");
            }

            if (cam2.UvResultBitmap != null)
            {
                lock (_cam2Lock) { _cam2UvResult = cam2.UvResultBitmap; }
                _mainWindow.UpdateCam2UvResult(_cam2UvResult);
                _mainWindow.UpdateCam2DiskUv(cam2.UvDiskCount);
                _mainWindow.UpdateInspectionStatusCam2(cam2.Result);
                _mainWindow.UpdateCam2ProcessedTime(cam2.Duration);
                var status = cam2.UvPassed ? ThumbStatus.Ok : ThumbStatus.Ng;
                App.ImageViewer.AddImage(_cam2UvResult, "2-UV-Result", status, $"CAM2 UV: {cam2.UvErrorDesc}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 8. LƯU ẢNH
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Lưu ảnh theo SaveMode. Chạy trong background thread, không block UI.
        /// </summary>
        private void SaveImages(DateTime timeStamp, bool inspectionStatus)
        {
            AppLogger.Instance.Info("Saving images...", "SYSTEM");
            try
            {
                var saveOption = (SaveOption)_param.SaveOption;
                if (saveOption == SaveOption.OK && !inspectionStatus) return;
                if (saveOption == SaveOption.NG && inspectionStatus) return;

                var saveMode = (SaveType)_param.SaveMode;
                bool success;

                switch (saveMode)
                {
                    case SaveType.ORIGINAL:
                        success = SaveOriginals(timeStamp);
                        break;
                    case SaveType.RESULT:
                        success = SaveResults(timeStamp);
                        break;
                    default:
                        success = SaveOriginals(timeStamp) && SaveResults(timeStamp);
                        break;
                }


                if (success)
                {
                    AppLogger.Instance.Info("Images saved successfully.", "SYSTEM");
                    _logger.Info("Images saved successfully.");
                }
                    
                else
                {
                    AppLogger.Instance.Error("Some images failed to save (empty or invalid path).", "SYSTEM");
                    _logger.Error("Some images failed to save (empty or invalid path).");
                }
                   
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"Failed to save images: {ex.Message}", "SYSTEM");
                _logger.Error($"Failed to save images: {ex.Message}");
            }
        }

        private bool SaveOriginals(DateTime ts) =>
            ImageSaver.SaveImage(_cam1WhiteOrigin, _param.SavePath, ts, "Cam1_White_Original") &&
            ImageSaver.SaveImage(_cam2WhiteOrigin, _param.SavePath, ts, "Cam2_White_Original") &&
            ImageSaver.SaveImage(_cam1UvOrigin, _param.SavePath, ts, "Cam1_UV_Original") &&
            ImageSaver.SaveImage(_cam2UvOrigin, _param.SavePath, ts, "Cam2_UV_Original");

        private bool SaveResults(DateTime ts) =>
            ImageSaver.SaveImage(_cam1WhiteResult, _param.SavePath, ts, "Cam1_White_Result") &&
            ImageSaver.SaveImage(_cam2WhiteResult, _param.SavePath, ts, "Cam2_White_Result") &&
            ImageSaver.SaveImage(_cam1UvResult, _param.SavePath, ts, "Cam1_UV_Result") &&
            ImageSaver.SaveImage(_cam2UvResult, _param.SavePath, ts, "Cam2_UV_Result");

        // ═════════════════════════════════════════════════════════════════════════
        // 9. STATUS TIMER — kiểm tra trạng thái kết nối định kỳ
        // ═════════════════════════════════════════════════════════════════════════

        public void StartStatusTimer()
        {
            if (_statusTimer != null) return;
            _statusTimer = new System.Timers.Timer(StatusPollIntervalMs) { AutoReset = false };
            _statusTimer.Elapsed += OnStatusTimerElapsed;
            _statusTimer.Start();
        }

        private void OnStatusTimerElapsed(object sender, EventArgs e)
        {
            bool aiOk = APICommunication.CheckAPIStatus(_param.ApiUrlAi, timeout: 1000);
            bool plcOk = PlcController.CheckPlcConnection(_param.ApiUrlCom, timeout: 1000);
            bool cam1Ok = _camera1?.IsOpen() == true;
            bool cam2Ok = _camera2?.IsOpen() == true;

            _mainWindow.SetStatusService(aiOk, plcOk, cam1Ok, cam2Ok);

            // Restart timer sau khi xử lý xong (tránh overlap)
            _statusTimer?.Start();
        }

        public void StopStatusTimer()
        {
            if (_statusTimer == null) return;
            _statusTimer.Stop();
            _statusTimer.Elapsed -= OnStatusTimerElapsed;
            _statusTimer.Dispose();
            _statusTimer = null;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 10. DỪNG VÀ ĐÓNG CHƯƠNG TRÌNH
        // ═════════════════════════════════════════════════════════════════════════

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            PlcController._firstTrigger = true;

            StopPlcTimer();
            StopStatusTimer();
            _inspectCts?.Cancel();

            ShutdownAllLights();
            StopAllCameras();

            _logger.Info("System stopped by user.");
            AppLogger.Instance.Info("System stopped.", "SYSTEM");
        }

        internal void CloseAIService()
        {
            AIServiceController.CloseProcessExisting();
            ServiceIsRunning = false;
        }

        internal void CloseCamera()
        {
            StopAllCameras(andClose: true);
        }

        internal void ShutdownLight()
        {
            ShutdownAllLights();
        }

        private void StopAllCameras(bool andClose = false)
        {
            foreach (var cam in new[] { _camera1, _camera2 })
            {
                if (cam == null || !cam.IsOpen()) continue;
                cam.Stop();
                if (andClose) cam.Close();
            }
        }

        private void ShutdownAllLights()
        {
            // Tắt tất cả đèn khi dừng — dùng API mới (gộp 2 đèn)
            PlcController.ControlWhiteLight(_param.ApiUrlCom, false);
            PlcController.ControlUvLight(_param.ApiUrlCom, false);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // 11. LICENSE
        // ═════════════════════════════════════════════════════════════════════════

        internal bool CheckLicense()
        {
            if (!File.Exists(LicensePath))
                return ShowActivationWindow();

            var (isValid, message) = LicenseManager.ValidateActivationKey(File.ReadAllText(LicensePath));

            if (isValid)
            {
                AppLogger.Instance.Info("License is valid.", "SYSTEM");
                _logger.Info("License is valid.");
                return true;
            }

            AppLogger.Instance.Error(message, "SYSTEM");
            _logger.Error("License is not valid. Please contact vendor.");
            _mainWindow.ShowError("License is not valid. Please contact vendor.");
            return ShowActivationWindow();
        }

        private bool ShowActivationWindow()
        {
            bool activated = false;
            _mainWindow.Dispatcher.Invoke(() =>
            {
                var win = new ActivationWindow { Topmost = true };
                if (win.ShowDialog() == true)
                {
                    AppLogger.Instance.Info("Activation successful.", "SYSTEM");
                    _mainWindow.ShowInfo("Activation key is valid. Continue using the program.");
                    activated = true;
                }
                else
                {
                    AppLogger.Instance.Error("Activation failed or cancelled.", "SYSTEM");
                    _mainWindow.ShowError("License is not valid. Please contact vendor.");
                }
            });
            return activated;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Log lỗi vào cả NLog lẫn AppLogger và hiển thị lên UI cùng 1 lúc.
        /// </summary>
        private void ShowAndLogError(string message, string tag)
        {
            _logger.Error(message);
            AppLogger.Instance.Error(message, tag);
            _mainWindow.ShowError(message);
        }
    }
}