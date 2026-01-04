using DiskInspection.Controllers;
using DiskInspection.Views;
using DiskInspection.Views.UtilitiesWindows;
using System;
using System.Collections.Generic;
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

namespace DiskInspection
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainController _mainController;
        public MainWindow()
        {
            InitializeComponent();
            _mainController = new MainController(this);
            DataContext = this;
        }
        private double GetFittedZoomScale(object imb, double imageWidth, double imageHeight)
        {
            var imageBox = imb as Heal.MyControl.ImageBox;
            var imageBoxWidth = imageBox.ActualWidth;
            var imageBoxHeight = imageBox.ActualHeight;
            var scale = Math.Min(imageBoxWidth / imageWidth, imageBoxHeight / imageHeight);
            return scale;
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnDebug_Click(object sender, RoutedEventArgs e)
        {
            var debugWindow = new DebugWindow();
            debugWindow.Show();
        }

        private void btnSetupCamera_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //new Task(new Action(() =>
            //{
            //    var res = _mainController.RunServiceAsync(20000, "Loading...");
            //})).Start();
        }

        internal void SetLoadingService(string content)
        {
            var timeout = 10000;
            new Thread(() =>
            {
                this.Dispatcher.Invoke(new Action(() =>
                {
                    WaitingWindow wait = new WaitingWindow(content);
                    wait.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    wait.Topmost = true;
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
                        if (!_mainController._serviceIsRun)
                        {
                            this.Dispatcher.Invoke(new Action(() =>
                            {
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
    }
}
