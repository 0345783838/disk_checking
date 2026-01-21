using DiskInspection.Controllers;
using DiskInspection.Models;
using DiskInspection.Services;
using DiskInspection.Utils;
using DiskInspection.Views;
using DiskInspection.Views.DebugWindows;
using DiskInspection.Views.SettingsWindows;
using DiskInspection.Views.UtilitiesWindows;
using LiveCharts.Wpf;
using LiveCharts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Globalization;

namespace DiskInspection
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private static NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private Properties.Settings _param = Properties.Settings.Default;
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private MainController _mainController;
        public int AiStatus { get; set; } = (int)(StatusState.Unknown);
        public int PlcStatus { get; set; } = (int)(StatusState.Unknown);
        public int Cam1Status { get; set; } = (int)(StatusState.Unknown);
        public int Cam2Status { get; set; } = (int)(StatusState.Unknown);
        public int InspectionStatusCam1 { get; set; } = (int)(StatusState.Unknown);
        public int InspectionStatusCam2 { get; set; } = (int)(StatusState.Unknown);
        public int InspectionStatus { get; set; } = (int)(StatusState.Unknown);

        public AppLogger Logger => AppLogger.Instance;

        private PieSeries _okSeries;
        private PieSeries _ngSeries;
        public SeriesCollection PieSeriesCollection { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            _mainController = new MainController(this);
            DataContext = this;
            InitStatistics();
            Logger.Logs.CollectionChanged += Logs_CollectionChanged;
        }
        private void Logs_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action !=
                System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                return;

            if (LogListBox.Items.Count == 0)
                return;

            // Scroll sau khi UI render xong
            LogListBox.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    LogListBox.ScrollIntoView(
                        LogListBox.Items[LogListBox.Items.Count - 1]);
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var window = new CommonSettingsWindow();
            window.Show();
        }

        private void btnDebug_Click(object sender, RoutedEventArgs e)
        {
            var debugWindow = new DebugWindow();
            debugWindow.Show();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            WaitingWindow wait = new WaitingWindow("Checking running conditions...\rKiểm tra điều kiện chạy");
            bool startOK = false;
            new Task(() =>
            {
                startOK = _mainController.Start();
                wait.KillMe = true;
            }).Start();
            wait.ShowDialog();

            if (startOK)
            {
                btnStart.IsEnabled = false;
                btnStop.IsEnabled = true;
            }
            else
            {
                btnStart.IsEnabled = true;
                btnStop.IsEnabled = false;
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            new Task(new Action(() =>
            {
                var res = _mainController.RunServiceAsync(20000, "Program is loading...");
            })).Start();
        }

        internal void SetLoadingService(string content)
        {
            var timeout = 10000;
            new Thread(() =>
            {
                this.Dispatcher.Invoke(new Action(() =>
                {
                    WaitingWindow wait = new WaitingWindow(content);
                    new Task(() =>
                    {
                        var timestep = timeout / 500;
                        for (int i = 0; i < timestep; i++)
                        {
                            Thread.Sleep(500);
                            if (_mainController._serviceIsRun)
                            {
                                break;
                            }
                        }
                        wait.KillMe = true;
                        UpdateAIStatus(true);
                        if (!_mainController._serviceIsRun)
                        {
                            this.Dispatcher.Invoke(new Action(() =>
                            {
                                UpdateAIStatus(false);
                                var box = new ErrorWindow("Cannot start AI service! Please contact IT!\rKhông khởi động được AI, Hãy liên hệ bộ phận PI");
                                box.ShowDialog();
                            }));
                        }
                    }).Start();
                    wait.ShowDialog();
                }));
            }).Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _mainController.CloseCamera();
            _mainController.CloseAIService();
            foreach (var item in System.Windows.Application.Current.Windows)
            {
                if (item != this)
                {
                    ((Window)item).Close();
                }
            }
            Environment.Exit(0);
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

        private void UpdateAIStatus(bool resAI)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                AiStatus = resAI ? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                OnPropertyChanged(nameof(AiStatus));
            }));
        }
        internal void SetStatusService(bool resAI, bool resPLC, bool resCamera1, bool resCamera2)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                AiStatus = resAI ? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                PlcStatus = resPLC? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                Cam1Status = resCamera1? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                Cam2Status = resCamera2? (int)(StatusState.Ok) : (int)(StatusState.Ng);

                OnPropertyChanged(nameof(AiStatus));
                OnPropertyChanged(nameof(PlcStatus));
                OnPropertyChanged(nameof(Cam1Status));
                OnPropertyChanged(nameof(Cam2Status));
            }));
        }
        #endregion

        #region Update Image


        public void UpdateCam1WhiteOrigin(BitmapSource image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imbCam1WhiteOrigin.Source = image;
            }));
        }
        public void UpdateCam1WhiteResult(BitmapSource image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imbCam1WhiteResult.Source = image;
            }));
        }
        public void UpdateCam1UvResult(BitmapSource image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imbCam1UvResult.Source = image;
            }));
        }
        public void UpdateCam1UvOrigin(BitmapSource image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imbCam1UvOrigin.Source = image;
            }));
        }
        public void UpdateCam2WhiteOrigin(BitmapSource image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imbCam2WhiteOrigin.Source = image;
            }));
        }
        public void UpdateCam2WhiteResult(BitmapSource image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imbCam2WhiteResult.Source = image;
            }));
        }
        public void UpdateCam2UvResult(BitmapSource image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imbCam2UvResult.Source = image;
            }));
        }
        public void UpdateCam2UvOrigin(BitmapSource image)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                imbCam2UvOrigin.Source = image;
            }));
        }

        #endregion

        #region Update Result CAM 1
        internal void UpdateInspectionStatusCam1(bool status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InspectionStatusCam1 = status ? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                OnPropertyChanged(nameof(InspectionStatusCam1));
            }));
        }
        internal void UpdateCam1DiskUv(int countUvDisk)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbUvDiskCountCam1.Content = countUvDisk.ToString();
            }));
        }
        internal void UpdateCam1MinMaxDis(double maxDis, double minDis)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbMaxDisCam1.Content = maxDis.ToString("F2");
                lbMinDisCam1.Content = minDis.ToString("F2");
            }));
        }
        internal void UpdateCam1ProcessedTime(TimeSpan time)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbFinishTimeCam1.Content = $"{time.Seconds}.{time.Milliseconds:D2} (s)";
            }));
        }

        #endregion

        #region Update Result CAM 2
        internal void UpdateInspectionStatusCam2(bool status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InspectionStatusCam2 = status ? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                OnPropertyChanged(nameof(InspectionStatusCam2));
            }));
        }
        internal void UpdateCam2DiskUv(int countUvDisk)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbUvDiskCountCam2.Content = countUvDisk.ToString();
            }));
        }
        internal void UpdateCam2MinMaxDis(double minDis, double maxDis)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbMaxDisCam2.Content = maxDis.ToString("F2");
                lbMinDisCam2.Content = minDis.ToString("F2");
            }));
        }
        internal void UpdateCam2ProcessedTime(TimeSpan time)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbFinishTimeCam2.Content = $"{time.Seconds}.{time.Milliseconds:D2} (s)";
            }));
        }
        #endregion

        #region Update Statistic
        internal void UpdateInspectionStatus(bool status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InspectionStatus = status ? (int)(StatusState.Ok) : (int)(StatusState.Ng);
                OnPropertyChanged(nameof(InspectionStatus));
            }));
        }

        internal void UpdateTimeStamp()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbTimestamp.Content = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }));
        }

        internal void UpdateCurrentShiftTime(string curShiftTime)
        {
            DateTime dt = DateTime.ParseExact(
                curShiftTime,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbDate.Content = dt.ToString("dd/MM/yyyy");
                lbWorkingShift.Content = $"{dt.ToString("HH:mm")} - {dt.AddHours(12).ToString("HH:mm")}";
            }));
        }
        internal void UpdateStatistics(bool status, bool firstTime=false)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Check shift time
                var curShiftTime = MyDateTime.GetCurShiftStartTime();
                if (curShiftTime != _param.StartShiftTime)
                {
                    _param.StartShiftTime = curShiftTime;
                    _param.CurrentOK = 0;
                    _param.CurrentNG = 0;
                    _param.Save();
                    UpdateCurrentShiftTime(_param.StartShiftTime);
                }

                if (firstTime)
                {
                    tbOKCount.Text = _param.CurrentOK.ToString();
                    tbNGCount.Text = _param.CurrentNG.ToString();
                    UpdateStatistics(_param.CurrentOK, _param.CurrentNG);
                    UpdateCurrentShiftTime(_param.StartShiftTime);
                }
                else
                {
                    if (status)
                    {
                        _param.CurrentOK += 1;
                        tbOKCount.Text = _param.CurrentOK.ToString();
                    }
                    else
                    {
                        _param.CurrentNG += 1;
                        tbNGCount.Text = _param.CurrentNG.ToString();
                    }
                    UpdateStatistics(_param.CurrentOK, _param.CurrentNG);
                    _param.Save();
                }
            }));
        }
        private void InitStatistics()
        {
            _okSeries = new PieSeries
            {
                Title = "OK",
                Values = new ChartValues<double> { 0 },
                DataLabels = true,
                LabelPoint = chartPoint => $"{chartPoint.Participation:P2}", // <-- thêm %
                Fill = new SolidColorBrush(System.Windows.Media.Colors.Green)
            };

            _ngSeries = new PieSeries
            {
                Title = "NG",
                Values = new ChartValues<double> { 0 },
                DataLabels = true,
                LabelPoint = chartPoint => $"{chartPoint.Participation:P2}",
                Fill = new SolidColorBrush(System.Windows.Media.Colors.Red)
            };
            PieSeriesCollection = new SeriesCollection { _okSeries, _ngSeries };
            UpdateStatistics(true, firstTime: true);
        }
        public void UpdateStatistics(int okCount, int ngCount)
        {
            this.Dispatcher.Invoke(() =>
            {
                _okSeries.Values[0] = (double)okCount;
                _ngSeries.Values[0] = (double)ngCount;
            });
        }
        #endregion
    }
}
